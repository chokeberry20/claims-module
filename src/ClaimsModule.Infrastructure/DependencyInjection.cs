using ClaimsModule.Application.Abstractions.Storage;
using ClaimsModule.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClaimsModule.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the external-service adapters. The host binds <see cref="StorageOptions"/>; the
    /// storage provider is picked from <c>Storage:Provider</c> when first resolved (BR-D-03).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IStorageService>(sp => sp.GetRequiredService<IOptions<StorageOptions>>().Value.Provider switch
        {
            StorageProvider.AzureBlob => ActivatorUtilities.CreateInstance<AzureBlobStorageService>(sp),
            StorageProvider.LocalFileSystem => ActivatorUtilities.CreateInstance<LocalFileSystemStorageService>(sp),
            var other => throw new InvalidOperationException($"Unknown storage provider '{other}'."),
        });

        return services;
    }
}
