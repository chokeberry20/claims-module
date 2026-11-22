using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Documents;

/// <summary>Maps to DOCUMENT_UPLOADED, with RelatedEntityId = documentId (S13, S14.1).</summary>
public sealed record ClaimDocumentUploaded(
    Guid ClaimId,
    Guid DocumentId,
    string DocumentType,
    string DocumentName,
    long FileSizeBytes,
    Guid ActorUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>
/// Metadata for a document attached to a claim (S9.7). The bytes live in blob storage behind
/// IStorageService (BR-D-01..03). It is a small aggregate of its own: immutable after upload,
/// with no invariants shared with the Claim, so an upload doesn't load or lock the claim.
/// </summary>
public sealed class ClaimDocument : AggregateRoot
{
    /// <summary>S13: "implement a reasonable 50 MB limit".</summary>
    public const long MaxFileSizeBytes = 50L * 1024 * 1024;

    public const int MaxDocumentTypeLength = 100;
    public const int MaxDocumentNameLength = 255;
    public const int MaxBlobPathLength = 500;
    public const int MaxContentTypeLength = 100;
    public const int MaxNotesLength = 500;

    private ClaimDocument()
    {
    }

    private ClaimDocument(Guid id)
        : base(id)
    {
    }

    public Guid ClaimId { get; private set; }

    public string DocumentType { get; private set; } = string.Empty;

    /// <summary>The file name as uploaded, after sanitising.</summary>
    public string DocumentName { get; private set; } = string.Empty;

    public string BlobPath { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long FileSizeBytes { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public Guid UploadedByUserId { get; private set; }

    public string UploadedByName { get; private set; } = string.Empty;

    public string? Notes { get; private set; }

    /// <summary>
    /// <paramref name="documentId"/> is allocated before the upload because the blob name
    /// contains it (D-29).
    /// </summary>
    public static ClaimDocument Register(
        Guid documentId,
        Guid claimId,
        string documentType,
        string documentName,
        string blobPath,
        string contentType,
        long fileSizeBytes,
        string? notes,
        Actor uploadedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(uploadedBy);
        if (claimId == Guid.Empty)
        {
            throw new ArgumentException("Claim id must not be empty.", nameof(claimId));
        }

        var violations = new List<RuleViolation>();
        Require(violations, documentType, MaxDocumentTypeLength, "DocumentType", "Document type");
        Require(violations, documentName, MaxDocumentNameLength, "DocumentName", "Document name");
        Require(violations, blobPath, MaxBlobPathLength, "BlobPath", "Blob path");
        Require(violations, contentType, MaxContentTypeLength, "ContentType", "Content type");

        if (fileSizeBytes <= 0)
        {
            violations.Add(new("DOCUMENT_EMPTY", "The uploaded file is empty.", "File"));
        }
        else if (fileSizeBytes > MaxFileSizeBytes)
        {
            violations.Add(new("DOCUMENT_TOO_LARGE", "The file exceeds the 50 MB limit.", "File"));
        }

        if (notes is { Length: > MaxNotesLength })
        {
            violations.Add(new("DOCUMENT_NOTES_TOO_LONG", $"Notes must be at most {MaxNotesLength} characters.", "Notes"));
        }

        if (violations.Count > 0)
        {
            throw new DomainRuleViolationException(violations);
        }

        var document = new ClaimDocument(documentId)
        {
            ClaimId = claimId,
            DocumentType = documentType.Trim(),
            DocumentName = documentName.Trim(),
            BlobPath = blobPath.Trim(),
            ContentType = contentType.Trim(),
            FileSizeBytes = fileSizeBytes,
            UploadedAt = now.ToUniversalTime(),
            UploadedByUserId = uploadedBy.UserId,
            UploadedByName = uploadedBy.DisplayName,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };

        document.Raise(new ClaimDocumentUploaded(
            claimId, document.Id, document.DocumentType, document.DocumentName, fileSizeBytes, uploadedBy.UserId, now));
        return document;
    }

    private static void Require(List<RuleViolation> violations, string? value, int max, string field, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            violations.Add(new("DOCUMENT_FIELD_REQUIRED", $"{label} is required.", field));
        }
        else if (value.Trim().Length > max)
        {
            violations.Add(new("DOCUMENT_FIELD_TOO_LONG", $"{label} must be at most {max} characters.", field));
        }
    }
}
