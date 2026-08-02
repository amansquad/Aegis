using System.Reflection;
using Aegis.Application.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Application;

/// <summary>
/// Registers the application layer with the dependency injection container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Marker used to scan this assembly for handlers and validators.</summary>
    public static Assembly Assembly => typeof(DependencyInjection).Assembly;

    /// <summary>
    /// Adds MediatR, FluentValidation and the request pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Registration order is execution order.</b> MediatR composes behaviours in the order they
    /// are registered, outermost first, so this method's sequence is a functional specification —
    /// not a stylistic one. Reordering it changes behaviour.
    /// </para>
    /// <code>
    /// Request
    ///   → RequestLogging     scope for every downstream log line; outermost so nothing escapes it
    ///     → Performance      times everything below, including validation
    ///       → Validation     rejects bad input before a transaction is ever opened
    ///         → Caching      a cache hit must not open a transaction either
    ///           → UnitOfWork commands only; the innermost wrapper around the handler
    ///             → Handler
    /// </code>
    /// <para>
    /// Two orderings are load-bearing. <b>Validation before UnitOfWork</b>: opening a transaction
    /// for a request that is about to be rejected takes a connection from the pool and holds it
    /// for nothing. <b>Caching before UnitOfWork</b>: a cache hit should cost no database
    /// connection at all, which is most of the point of having a cache.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(Assembly);

            configuration.AddOpenBehavior(typeof(RequestLoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(PerformanceBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(CachingBehavior<,>));
            configuration.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
        });

        // Scoped, not singleton: validators may depend on IAegisDbContext for uniqueness checks
        // such as "is this asset serial number already registered in this organization?".
        services.AddValidatorsFromAssembly(Assembly, ServiceLifetime.Scoped, includeInternalTypes: true);

        return services;
    }
}
