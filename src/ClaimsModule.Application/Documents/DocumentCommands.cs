using System.Text.Json.Serialization;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Abstractions.Storage;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.Common.Exceptions;
using ClaimsModule.Application.Common.Messaging;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Documents;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;

namespace ClaimsModule.Application.Documents;

/// <summary>A claim document with a short-lived read link (S13, BR-D-02).</summary>
public sealed record DocumentDto(
    Guid DocumentId,
    Guid ClaimId,
    string DocumentType,
    string DocumentName,
    string ContentType,
    long FileSizeBytes,
    DateTimeOffset UploadedAt,
    Guid UploadedByUserId,
    string UploadedByName,
    string? Notes,
    Uri DownloadUrl,
    DateTimeOffset DownloadUrlExpiresAt);

/// <summary>Stored document metadata (no link yet). Implemented in Persistence.</summary>
public interface IClaimDocumentReader
{
    /// <summary>Null when the claim doesn't exist (in this organisation).</summary>
    Task<IReadOnlyList<ClaimDocumentRecord>?> ListAsync(Guid claimId, CancellationToken cancellationToken);
}

public sealed record ClaimDocumentRecord(
    Guid DocumentId, Guid ClaimId, string DocumentType, string DocumentName, string BlobPath, string ContentType,
    long FileSizeBytes, DateTimeOffset UploadedAt, Guid UploadedByUserId, string UploadedByName, string? Notes);

/// <summary>Creates read links for stored documents with the configured lifetime (1 h).</summary>
internal sealed class DocumentLinks(IStorageService storage, IOptions<StorageOptions> options, TimeProvider clock)
{
    public DocumentDto ToDto(ClaimDocumentRecord d)
    {
        var lifetime = options.Value.ReadLinkLifetime;
        return new DocumentDto(
            d.DocumentId, d.ClaimId, d.DocumentType, d.DocumentName, d.ContentType, d.FileSizeBytes, d.UploadedAt,
            d.UploadedByUserId, d.UploadedByName, d.Notes, storage.GetReadUri(d.BlobPath, lifetime), clock.GetUtcNow() + lifetime);
    }
}

// ------------------------------------------------------------------ upload (POST /api/claims/{id}/documents)

/// <summary>
/// Stores a file and registers its metadata (S13, FR-60..63). <see cref="Content"/> must be seekable
/// (the API buffers the multipart file): a transient database retry replays the command and the
/// upload is repeated to the same path. It is excluded from the idempotency hash; a replayed key
/// returns the first result without uploading again (its link may have expired; the list query
/// always issues fresh links).
/// </summary>
public sealed record UploadDocumentCommand(Guid ClaimId, string DocumentType, string FileName, string ContentType, long Length)
    : IdempotentCommand, ICommand<DocumentDto>
{
    public string? Notes { get; init; }

    [JsonIgnore]
    public Stream Content { get; init; } = Stream.Null;
}

internal sealed class UploadDocumentValidator : AbstractValidator<UploadDocumentCommand>
{
    public UploadDocumentValidator()
    {
        RuleFor(c => c.DocumentType).NotEmpty().WithErrorCode("DOCUMENT_TYPE_REQUIRED").WithMessage("Document type is required.")
            .MaximumLength(ClaimDocument.MaxDocumentTypeLength).WithErrorCode("DOCUMENT_TYPE_TOO_LONG")
            .WithMessage($"Document type must be at most {ClaimDocument.MaxDocumentTypeLength} characters.");
        RuleFor(c => c.Length).GreaterThan(0).WithErrorCode("DOCUMENT_EMPTY").WithMessage("The uploaded file is empty.")
            .LessThanOrEqualTo(DocumentRules.MaxFileSizeBytes).WithErrorCode("DOCUMENT_TOO_LARGE").WithMessage("The file exceeds the 50 MB limit.");
        RuleFor(c => c).Must(c => DocumentRules.IsAllowed(c.FileName, c.ContentType)).OverridePropertyName("File")
            .WithErrorCode("DOCUMENT_TYPE_NOT_ALLOWED")
            .WithMessage("File type is not allowed. Allowed: PDF, JPEG, PNG, DOCX, XLSX, TXT, CSV (extension and content type must match).");
        RuleFor(c => c.Content).Must(s => s.CanSeek).WithErrorCode("DOCUMENT_STREAM_NOT_SEEKABLE")
            .WithMessage("The upload must be buffered before it is stored.");
        RuleFor(c => c.Notes).MaximumLength(ClaimDocument.MaxNotesLength).WithErrorCode("DOCUMENT_NOTES_TOO_LONG")
            .WithMessage($"Notes must be at most {ClaimDocument.MaxNotesLength} characters.");
    }
}

internal sealed class UploadDocumentHandler(
    IClaimRepository claims,
    IClaimDocumentRepository documents,
    IStorageService storage,
    DocumentLinks links,
    ITenantProvider tenant,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<UploadDocumentCommand, DocumentDto>
{
    public async Task<DocumentDto> Handle(UploadDocumentCommand request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetRequiredAsync(request.ClaimId, cancellationToken);
        if (request.Content.Length != request.Length)
        {
            throw new DomainRuleViolationException("DOCUMENT_LENGTH_MISMATCH", "The uploaded file size does not match the declared size.", "File");
        }

        var documentId = SequentialGuid.NewGuid();
        var fileName = DocumentRules.SanitiseFileName(request.FileName);
        var path = DocumentRules.StoragePath(tenant.OrganisationId, claim.Id, documentId, fileName);
        var contentType = DocumentRules.NormaliseContentType(request.ContentType);

        request.Content.Position = 0; // a replayed attempt reads the buffered file again
        await storage.UploadAsync(path, request.Content, contentType, cancellationToken);

        ClaimDocument document;
        try
        {
            document = ClaimDocument.Register(
                documentId, claim.Id, request.DocumentType, fileName, path, contentType, request.Length, request.Notes,
                currentUser.GetActor(), clock.GetUtcNow());
        }
        catch
        {
            // Don't leave an unreferenced file behind. (A later commit failure can still orphan one; its
            // name is unique, so it is never served or overwritten.)
            await storage.DeleteIfExistsAsync(path, CancellationToken.None);
            throw;
        }

        documents.Add(document);

        return links.ToDto(new ClaimDocumentRecord(
            document.Id, document.ClaimId, document.DocumentType, document.DocumentName, document.BlobPath, document.ContentType,
            document.FileSizeBytes, document.UploadedAt, document.UploadedByUserId, document.UploadedByName, document.Notes));
    }
}

// ------------------------------------------------------------------ list (GET /api/claims/{id}/documents)

public sealed record ListClaimDocumentsQuery(Guid ClaimId) : IQuery<IReadOnlyList<DocumentDto>>;

internal sealed class ListClaimDocumentsHandler(IClaimDocumentReader reader, DocumentLinks links)
    : IRequestHandler<ListClaimDocumentsQuery, IReadOnlyList<DocumentDto>>
{
    public async Task<IReadOnlyList<DocumentDto>> Handle(ListClaimDocumentsQuery request, CancellationToken cancellationToken)
    {
        var records = await reader.ListAsync(request.ClaimId, cancellationToken) ?? throw new NotFoundException("Claim", request.ClaimId);
        return records.Select(links.ToDto).ToList();
    }
}
