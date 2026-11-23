using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using ClaimsModule.Application.Abstractions.Storage;
using Microsoft.Extensions.Options;

namespace ClaimsModule.Infrastructure.Storage;

/// <summary>
/// Azure Blob storage (S13, D-30): files in the private <c>claim-documents</c> container, downloads
/// through an <b>account-key SAS</b> for one blob, read-only, valid for the configured lifetime
/// (1 h). The connection string comes from Key Vault in Azure.
/// </summary>
internal sealed class AzureBlobStorageService : IStorageService
{
    private readonly BlobContainerClient _container;
    private readonly TimeProvider _clock;

    public AzureBlobStorageService(IOptions<StorageOptions> options, TimeProvider clock)
    {
        var settings = options.Value.AzureBlob;
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new InvalidOperationException("Storage:AzureBlob:ConnectionString is not configured (a Key Vault reference in Azure).");
        }

        _container = new BlobContainerClient(settings.ConnectionString, settings.ContainerName);
        _clock = clock;
        if (!_container.CanGenerateSasUri)
        {
            throw new InvalidOperationException("The storage connection string has no account key, so no SAS can be issued (D-30).");
        }
    }

    public async Task UploadAsync(string path, Stream content, string contentType, CancellationToken cancellationToken) =>
        await _container.GetBlobClient(path).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);

    public async Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken) =>
        await _container.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public Uri GetReadUri(string path, TimeSpan validFor)
    {
        var blob = _container.GetBlobClient(path);
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, _clock.GetUtcNow() + validFor)
        {
            BlobContainerName = _container.Name,
            BlobName = path,
            Resource = "b", // a single blob, never the container
            Protocol = SasProtocol.Https,
        };
        return blob.GenerateSasUri(sas);
    }

    public async Task<StorageCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var exists = await _container.ExistsAsync(cancellationToken);
        return exists.Value
            ? new StorageCheckResult(true, $"Container '{_container.Name}' is reachable.")
            : new StorageCheckResult(false, $"Container '{_container.Name}' does not exist.");
    }
}

/// <summary>
/// Local folder storage for development (BR-D-03). Read links are plain URLs under
/// <c>PublicBaseUrl</c>, served by the API's static-files middleware (D-30; signed links are
/// Stretch). Never used in Azure: the host refuses this provider in Production.
/// </summary>
internal sealed class LocalFileSystemStorageService(IOptions<StorageOptions> options) : IStorageService
{
    private readonly string _root = Path.GetFullPath(options.Value.LocalFileSystem.RootPath);
    private readonly string _baseUrl = options.Value.LocalFileSystem.PublicBaseUrl.TrimEnd('/');

    public async Task UploadAsync(string path, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var file = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await using var target = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(target, cancellationToken);
    }

    public Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        File.Delete(Resolve(path)); // no error if it doesn't exist
        return Task.CompletedTask;
    }

    public Uri GetReadUri(string path, TimeSpan validFor) =>
        new($"{_baseUrl}/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}");

    public Task<StorageCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        return Task.FromResult(new StorageCheckResult(true, $"Local folder '{_root}'."));
    }

    /// <summary>Maps a storage path into the root folder and refuses anything that would escape it.</summary>
    private string Resolve(string path)
    {
        var full = Path.GetFullPath(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The storage path resolves outside the storage root.");
        }

        return full;
    }
}
