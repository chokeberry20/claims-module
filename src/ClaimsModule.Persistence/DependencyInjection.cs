using ClaimsModule.Application.Abstractions.Audit;
using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Claims;
using ClaimsModule.Application.ReferenceData;
using ClaimsModule.Persistence.Interceptors;
using ClaimsModule.Persistence.Queries;
using ClaimsModule.Persistence.Repositories;
using ClaimsModule.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClaimsModule.Persistence;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the DbContext with its interceptors, the Unit of Work, repositories, the claim number
    /// generator and the audit log service. The host must register ICurrentUser, ITenantProvider,
    /// ICorrelationContext and IDomainEventDispatcher. TimeProvider defaults to the system clock.
    /// <para>
    /// Transient SQL errors (Azure SQL failovers, throttling) are retried by EF Core's SQL Server
    /// retrying execution strategy (D-56, P2.5 task 17f). <paramref name="configureSqlServer"/> runs
    /// after that default, so a caller can change or replace the strategy.
    /// </para>
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        string connectionString,
        Action<SqlServerDbContextOptionsBuilder>? configureSqlServer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<AppendOnlyGuardInterceptor>();
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<TenantInterceptor>();
        services.AddScoped<AuditableEntityInterceptor>();

        services.AddDbContext<ClaimsDbContext>((sp, options) => options
            .UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                configureSqlServer?.Invoke(sql);
            })
            .AddInterceptors(
                sp.GetRequiredService<AppendOnlyGuardInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<TenantInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>()));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IClaimNumberGenerator, ClaimNumberGenerator>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IReferenceDataReader, ReferenceDataReader>();
        services.AddScoped<IClaimReader, ClaimReader>();
        services.AddScoped<ISlaQueries, SlaQueries>();
        services.AddScoped<global::ClaimsModule.Application.Reserves.IReserveReader, ReserveReader>();
        services.AddScoped<IClaimRepository, ClaimRepository>();
        services.AddScoped<IReserveComponentRepository, ReserveComponentRepository>();
        services.AddScoped<IClaimDocumentRepository, ClaimDocumentRepository>();
        services.AddScoped<IPolicyRepository, PolicyRepository>();
        services.AddScoped<ICauseOfLossCodeRepository, CauseOfLossCodeRepository>();

        return services;
    }
}
