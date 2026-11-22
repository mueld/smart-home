using System.Data.Common;
using System.Reflection;
using Backend.Kernel.Domain;
using Backend.Kernel.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.Kernel;

public static class KernelServiceCollectionExtensions
{
    /// <summary>
    /// Registers what the kernel needs besides the mediator: validators, the domain event interceptor and the
    /// transaction state (domain events are mediator notifications). The mediator itself comes from the Mediator source generator in
    /// the host (<c>AddMediator</c>, scoped lifetime) with the pipeline <see cref="Behaviors.ValidationBehavior{TMessage,TResponse}"/>
    /// followed by <see cref="Behaviors.TransactionBehavior{TMessage,TResponse}"/> (AD-5). Needs a scoped
    /// <see cref="DbConnection"/> that all module DbContexts of a scope share (AD-14).
    /// </summary>
    public static IServiceCollection AddKernel(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.TryAddSingleton<ModuleRegistry>();
        services.TryAddScoped<TransactionCoordinator>();
        services.TryAddScoped<DomainEventInterceptor>();

        services.AddValidatorsFromAssemblies(assemblies, includeInternalTypes: true);

        return services;
    }

    /// <summary>
    /// Registers the DbContext of a module (AD-3) on the shared connection with the domain event interceptor.
    /// Commands in <paramref name="rootNamespace"/> and below (including the Contracts project) belong to the module.
    /// </summary>
    public static IServiceCollection AddModule<TDbContext>(
        this IServiceCollection services,
        string rootNamespace,
        Action<DbContextOptionsBuilder, DbConnection> configure)
        where TDbContext : DbContext
    {
        services.AddDbContext<TDbContext>((provider, options) =>
        {
            configure(options, provider.GetRequiredService<DbConnection>());
            options.AddInterceptors(provider.GetRequiredService<DomainEventInterceptor>());
        });

        services.AddSingleton(new ModuleRegistration(typeof(TDbContext), rootNamespace));

        return services;
    }
}
