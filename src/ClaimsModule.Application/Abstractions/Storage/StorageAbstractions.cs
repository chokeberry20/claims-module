namespace ClaimsModule.Application.Abstractions.Storage;

/// <summary>
/// Document storage (S13, BR-D-03): Azure Blob in Azure, the local file system in development. The
/// provider is chosen in configuration (<c>Storage:Provider</c>). The API never streams file bytes
/// back: downloads go through <see cref="GetReadUri"/> (BR-D-02).
/// </summary>
public interface IStorageService
{
    /// <summary>Stores the content at <paramref name="path"/> (overwrites, so a retried upload is safe).</summary>
    Task UploadAsync(string path, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>Removes a stored file if it exists (cleanup after a failed registration).</summary>
    Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>A read-only link to one file, valid for <paramref name="validFor"/> (a SAS URL for Azure Blob).</summary>
    Uri GetReadUri(string path, TimeSpan validFor);

    /// <summary>Health: whether the store (container or folder) is reachable. Used by /health.</summary>
    Task<StorageCheckResult> CheckAsync(CancellationToken cancellationToken);
}

public sealed record StorageCheckResult(bool IsHealthy, string Description);

/// <summary>Configuration section <c>Storage</c>.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public StorageProvider Provider { get; set; } = StorageProvider.AzureBlob;

    /// <summary>Lifetime of a download link (S13: 1 hour).</summary>
    public TimeSpan ReadLinkLifetime { get; set; } = TimeSpan.FromHours(1);

    public AzureBlobStorageOptions AzureBlob { get; set; } = new();

    public LocalFileSystemStorageOptions LocalFileSystem { get; set; } = new();
}

public enum StorageProvider
{
    AzureBlob = 0,
    LocalFileSystem = 1,
}

public sealed class AzureBlobStorageOptions
{
    /// <summary>A storage connection string with the account key (account-key SAS, D-30). A Key Vault reference in Azure.</summary>
    public string? ConnectionString { get; set; }

    public string ContainerName { get; set; } = "claim-documents";
}

public sealed class LocalFileSystemStorageOptions
{
    /// <summary>Folder for stored files; relative paths are under the app's content root. Development only.</summary>
    public string RootPath { get; set; } = "uploads";

    /// <summary>Absolute base URL the API serves the folder from (static files at /uploads).</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5180/uploads";
}
