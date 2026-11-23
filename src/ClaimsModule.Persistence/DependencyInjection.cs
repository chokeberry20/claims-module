using ClaimsModule.Application.Abstractions.Audit;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Persistence.Interceptors;
using ClaimsModule.Persistence.Repositories;
using ClaimsModule.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClaimsModule.Persistence;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the DbContext with its interceptors, the Unit of Work, repositories, the claim number
    /// generator and the audit log service. The host must register ICurrentUser, ITenantProvider,
    /// ICorrelationContext and IDomainEventDispatcher. TimeProvider defaults to the system clock.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<AppendOnlyGuardInterceptor>();
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<TenantInterceptor>();
        services.AddScoped<AuditableEntityInterceptor>();

        services.AddDbContext<ClaimsDbContext>((sp, options) => options
            .UseSqlServer(connectionString)
            .AddInterceptors(
                sp.GetRequiredService<AppendOnlyGuardInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<TenantInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>()));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IClaimNumberGenerator, ClaimNumberGenerator>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IClaimRepository, ClaimRepository>();
        services.AddScoped<IReserveComponentRepository, ReserveComponentRepository>();
        services.AddScoped<IClaimDocumentRepository, ClaimDocumentRepository>();
        services.AddScoped<IPolicyRepository, PolicyRepository>();
        services.AddScoped<ICauseOfLossCodeRepository, CauseOfLossCodeRepository>();

        return services;
    }
}
