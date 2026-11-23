using System.Text;
using ClaimsModule.Domain.Documents;

namespace ClaimsModule.Application.Documents;

/// <summary>
/// Upload rules (S13): the MIME/extension allowlist, the 50 MB limit, the filename sanitiser and the
/// storage path (BR-D-01, D-29). Magic-byte sniffing is Stretch (task 29s): the extension and the
/// declared content type must both be on the list and match each other.
/// </summary>
public static class DocumentRules
{
    public const long MaxFileSizeBytes = ClaimDocument.MaxFileSizeBytes;
    public const int MaxFileNameLength = 200;

    /// <summary>Allowed extensions and the content types a browser may declare for each.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> AllowedTypes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ["application/pdf"],
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"],
        [".png"] = ["image/png"],
        [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
        [".xlsx"] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
        [".txt"] = ["text/plain"],
        [".csv"] = ["text/csv", "application/csv", "application/vnd.ms-excel"],
    };

    /// <summary>True when the file's extension is allowed and the declared content type belongs to it.</summary>
    public static bool IsAllowed(string fileName, string contentType)
    {
        var extension = Path.GetExtension(SanitiseFileName(fileName));
        var declared = contentType.Split(';')[0].Trim();
        return AllowedTypes.TryGetValue(extension, out var types) && types.Contains(declared, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The canonical content type stored with the file (parameters such as charset are dropped).</summary>
    public static string NormaliseContentType(string contentType) => contentType.Split(';')[0].Trim().ToLowerInvariant();

    /// <summary>
    /// A safe file name (BR-D-01): no directory parts (so no "..", "/" or "\"), no control or reserved
    /// characters, no leading dots, collapsed whitespace, at most 200 characters with the extension
    /// kept. An empty result becomes "document".
    /// </summary>
    public static string SanitiseFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];

        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsControl(c) || "<>:\"|?*".Contains(c, StringComparison.Ordinal) ? '_' : c);
        }

        var cleaned = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimStart('.').Trim();
        if (cleaned.Length == 0 || cleaned.All(c => c is '.' or '_'))
        {
            cleaned = "document";
        }

        if (cleaned.Length > MaxFileNameLength)
        {
            var extension = Path.GetExtension(cleaned);
            extension = extension.Length <= 10 ? extension : string.Empty;
            cleaned = cleaned[..(MaxFileNameLength - extension.Length)].TrimEnd() + extension;
        }

        return cleaned;
    }

    /// <summary>D-29: <c>{organisation}/{claim}/{documentId:N}-{sanitised name}</c>; the id prefix means nothing is overwritten.</summary>
    public static string StoragePath(Guid organisationId, Guid claimId, Guid documentId, string sanitisedFileName) =>
        $"{organisationId:D}/{claimId:D}/{documentId:N}-{sanitisedFileName}";
}
