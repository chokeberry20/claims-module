using ClaimsModule.API.Contracts;
using ClaimsModule.API.Hosting;
using ClaimsModule.Application.Documents;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

/// <summary>Claim documents (S13, BR-D-01..03). Files are stored in Blob storage; reads use short-lived links, never the API.</summary>
[Route("api/claims/{claimId:guid}/documents")]
[Tags("Documents")]
public sealed class DocumentsController : ApiControllerBase
{
    /// <summary>Multipart limit: the 50 MB file plus room for the other form fields and boundaries.</summary>
    public const long MaxRequestBytes = DocumentRules.MaxFileSizeBytes + (1024 * 1024);

    /// <summary>The claim's documents, each with a fresh read-only download link (1 hour, S13).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DocumentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IReadOnlyList<DocumentDto>> List(Guid claimId, CancellationToken cancellationToken) =>
        await Sender.Send(new ListClaimDocumentsQuery(claimId), cancellationToken);

    /// <summary>Uploads a document (multipart: file, documentType, notes). PDF, JPEG, PNG, DOCX, XLSX, TXT or CSV, at most 50 MB.</summary>
    [HttpPost]
    [Idempotent]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Upload(Guid claimId, [FromForm] UploadDocumentForm form, CancellationToken cancellationToken)
    {
        var file = form.File!;
        // The multipart file is buffered by ASP.NET Core (memory, then a temp file), so the stream is
        // seekable: a transient database retry can replay the command and read it again.
        await using var content = file.OpenReadStream();
        var result = await Sender.Send(
            new UploadDocumentCommand(claimId, form.DocumentType, file.FileName, file.ContentType, file.Length)
            {
                Notes = form.Notes,
                Content = content,
                IdempotencyKey = IdempotencyKey,
            },
            cancellationToken);
        return CreatedAtAction(nameof(List), new { claimId }, result);
    }
}
