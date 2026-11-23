using ClaimsModule.Application.Abstractions.Persistence;
using ClaimsModule.Application.Common.Behaviours;
using ClaimsModule.Application.Common.Events;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClaimsModule.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers MediatR (handlers, notification handlers, the pipeline behaviours in order),
    /// FluentValidation validators and the MediatR domain event dispatcher. The host must also
    /// register Persistence (AddPersistence) and the execution-context services.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(typeof(AssemblyReference).Assembly);
            config.AddOpenBehavior(typeof(LoggingBehaviour<,>));
            config.AddOpenBehavior(typeof(ValidationBehaviour<,>));
            config.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
            config.AddOpenBehavior(typeof(TransactionBehaviour<,>));
            config.AddOpenBehavior(typeof(IdempotencyBehaviour<,>));
        });

        services.AddAutoMapper(_ => { }, typeof(AssemblyReference).Assembly);
        services.AddValidatorsFromAssembly(typeof(AssemblyReference).Assembly, includeInternalTypes: true);
        services.Replace(ServiceDescriptor.Scoped<IDomainEventDispatcher, MediatorDomainEventDispatcher>());

        return services;
    }
}
