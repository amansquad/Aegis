using Aegis.Application.Abstractions.Caching;
using Aegis.Application.Abstractions.Events;
using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Abstractions.Requests;
using Aegis.Application.Abstractions.Security;
using Aegis.Infrastructure.Caching;
using Aegis.Infrastructure.Events;
using Aegis.Infrastructure.Multitenancy;
using Aegis.Infrastructure.Persistence;
using Aegis.Infrastructure.Persistence.Interceptors;
using Aegis.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Aegis.Infrastructure;

/// <summary>
/// Registers the infrastructure adapters with the dependency injection container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Adds persistence, caching, tenancy and request-context adapters.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddAmbientContext();
        services.AddPersistence(configuration);
        services.AddCaching(configuration);

        return services;
    }

    /// <summary>Registers the per-request identity, tenant and request-detail adapters.</summary>
    private static IServiceCollection AddAmbientContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        // TimeProvider rather than a bespoke IDateTimeProvider. It is a BCL abstraction, so no
        // interface of ours is needed, and Microsoft.Extensions.TimeProvider.Testing supplies a
        // controllable fake for tests that assert on timestamps and expiry.
        services.AddSingleton(TimeProvider.System);

        // Scoped: each request gets its own tenant, identity and event buffer. A singleton here
        // would let one request's tenant leak into another's queries.
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IRequestContext, RequestContext>();
        services.AddScoped<IDomainEventCollector, DomainEventCollector>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        return services;
    }

    private static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' is not configured. The application cannot start " +
                "without it, and failing here is preferable to failing on the first request.");

        // Interceptors are scoped because they depend on scoped services (the current user, the
        // tenant, the event collector). Registering them as singletons would capture the first
        // request's user for the lifetime of the process.
        services.AddScoped<PersistenceMetadataInterceptor>();
        services.AddScoped<AuditTrailInterceptor>();
        services.AddScoped<DomainEventCollectionInterceptor>();

        services.AddDbContext<AegisDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "dbo");

                // Transient faults are normal against managed SQL, which recycles connections
                // during failover and throttling. Retrying is what keeps that invisible to users;
                // UnitOfWorkBehavior runs transactions through the execution strategy so the whole
                // unit is replayed rather than a fragment of it.
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);

                sql.CommandTimeout(30);

                // Required for the spatial columns the Assets module introduces.
                sql.UseNetTopologySuite();
            });

            // Interceptor order is functional, not cosmetic:
            //   1. Metadata  - stamps tenant and audit fields, converts deletes into soft deletes.
            //   2. AuditTrail - reads those stamped values and the converted delete state.
            //   3. Events     - harvests domain events last, once all mutations are settled.
            options.AddInterceptors(
                serviceProvider.GetRequiredService<PersistenceMetadataInterceptor>(),
                serviceProvider.GetRequiredService<AuditTrailInterceptor>(),
                serviceProvider.GetRequiredService<DomainEventCollectionInterceptor>());

            // Queries do not need change tracking, and tracking every read costs memory and
            // fixup time proportional to result size. Commands opt back in per query.
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        services.AddScoped<IAegisDbContext>(sp => sp.GetRequiredService<AegisDbContext>());

        return services;
    }

    private static IServiceCollection AddCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName))
            .ValidateOnStart();

        var cacheOptions = configuration
            .GetSection(CacheOptions.SectionName)
            .Get<CacheOptions>() ?? new CacheOptions();

        var multiplexerConfiguration = ConfigurationOptions.Parse(cacheOptions.ConnectionString);

        // Without this the process cannot start when Redis is briefly unavailable, which turns a
        // cache outage into an application outage. The cache adapter already degrades gracefully.
        multiplexerConfiguration.AbortOnConnectFail = false;
        multiplexerConfiguration.ConnectRetry = 3;

        // One multiplexer for the whole process, shared by IDistributedCache and by the adapter's
        // SCAN-based prefix eviction. StackExchange.Redis multiplexes many logical operations over
        // a single connection, so creating a second one wastes a connection and doubles the
        // reconnect storm during a Redis failover.
        //
        // Lazy so that the connection attempt happens on first use rather than during service
        // registration, keeping Redis off the application's startup critical path.
        var multiplexer = new Lazy<IConnectionMultiplexer>(
            () => ConnectionMultiplexer.Connect(multiplexerConfiguration),
            LazyThreadSafetyMode.ExecutionAndPublication);

        services.AddSingleton<IConnectionMultiplexer>(_ => multiplexer.Value);

        services.AddStackExchangeRedisCache(options =>
        {
            options.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer.Value);
            options.InstanceName = $"{cacheOptions.InstanceName}:";
        });

        services.AddScoped<ICacheService, RedisCacheService>();

        return services;
    }
}
