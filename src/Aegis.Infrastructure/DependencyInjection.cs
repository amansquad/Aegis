using System.Net.Http.Headers;
using Aegis.Application.Abstractions.Ai;
using Aegis.Application.Abstractions.Caching;
using Aegis.Application.Abstractions.Events;
using Aegis.Application.Abstractions.Notifications;
using Aegis.Application.Identity.Commands;
using Aegis.Infrastructure.Notifications;
using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Abstractions.Requests;
using Aegis.Application.Abstractions.Security;
using Aegis.Infrastructure.Ai;
using Aegis.Infrastructure.Caching;
using Aegis.Infrastructure.Events;
using Aegis.Infrastructure.Multitenancy;
using Aegis.Infrastructure.Persistence;
using Aegis.Infrastructure.Persistence.Interceptors;
using Aegis.Infrastructure.Security;
using Aegis.Infrastructure.Security.Hashing;
using Aegis.Infrastructure.Security.Tokens;
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
        services.AddSecurity(configuration);
        services.AddNotifications(configuration);
        services.AddAi(configuration);

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

    /// <summary>Registers password hashing and token issuance.</summary>
    private static IServiceCollection AddSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.SigningKey),
                "Jwt:SigningKey must be configured.")
            .Validate(
                o => o.AccessTokenMinutes is > 0 and <= 60,
                "Jwt:AccessTokenMinutes must be between 1 and 60. A longer-lived access token " +
                "cannot be revoked and widens the window in which a withdrawn permission still works.")
            .ValidateOnStart();

        // Stateless and thread-safe, so a single instance serves the process. The PBKDF2 work
        // factor is a compile-time constant rather than per-instance state.
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordPolicy, PasswordPolicy>();

        // Scoped: depends on the DbContext for the cache-miss path.
        services.AddScoped<ISecurityStampService, SecurityStampService>();

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

            // Tracking stays on by default, and read paths opt out with an explicit AsNoTracking().
            //
            // The reverse — NoTracking globally, with commands opting back in — looks like the
            // better default because most queries are reads. It is not, and the integration suite
            // proved it: a handler that loads an aggregate, mutates it and calls SaveChangesAsync
            // succeeds and writes nothing at all. No exception, no warning, no failed assertion
            // anywhere near the cause. Sign-out, lockout and refresh-token rotation all silently
            // did nothing.
            //
            // The asymmetry decides it. Forgetting AsNoTracking costs some memory and snapshot
            // work on a read. Forgetting AsTracking loses a write, and loses it quietly. Defaults
            // should fail in the direction that is noisy and cheap, not silent and destructive.
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
        });

        services.AddScoped<IAegisDbContext>(sp => sp.GetRequiredService<AegisDbContext>());

        return services;
    }

    /// <summary>
    /// Registers the incident extractor, choosing the model adapter or the rule-based fallback.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The choice is made once at startup from whether a key is present, rather than per request.
    /// A per-request check would mean the behaviour of the intake form depended on configuration
    /// state that could differ between two instances behind the same load balancer.
    /// </para>
    /// <para>
    /// Falling back rather than failing to start is deliberate: a developer with no key still gets
    /// a working intake form and a green test suite, and a production deployment that loses its
    /// key degrades to human classification instead of losing the ability to accept reports at all.
    /// The log line at startup says plainly which one is active, so nobody has to guess.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddAi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .Validate(
                o => o.TimeoutSeconds is > 0 and <= 60,
                "Ai:TimeoutSeconds must be between 1 and 60. This runs while a member of the " +
                "public waits on a form.")
            .ValidateOnStart();

        var ai = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();

        if (!ai.IsConfigured)
        {
            services.AddSingleton<IIncidentExtractor, HeuristicIncidentExtractor>();

            return services;
        }

        services
            .AddHttpClient<IIncidentExtractor, OpenRouterIncidentExtractor>(client =>
            {
                client.BaseAddress = new Uri($"{ai.BaseUrl.TrimEnd('/')}/");
                client.Timeout = TimeSpan.FromSeconds(ai.TimeoutSeconds);

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", ai.ApiKey);

                // OpenRouter attribution headers. Optional, and only ever carrying values we
                // configured — never anything derived from a report.
                if (!string.IsNullOrWhiteSpace(ai.SiteUrl))
                {
                    client.DefaultRequestHeaders.Add("HTTP-Referer", ai.SiteUrl);
                }

                client.DefaultRequestHeaders.Add("X-Title", ai.SiteName);
            })
            // One retry on a transient fault, no more. The caller is a person waiting on a form,
            // and a long retry ladder turns a slow provider into an abandoned report. Failing
            // through to manual entry is the better outcome past the first attempt.
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.Retry.MaxRetryAttempts = 1;
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(ai.TimeoutSeconds);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(ai.TimeoutSeconds * 2.5);
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(ai.TimeoutSeconds * 4);
            });

        return services;
    }

    /// <summary>Registers outbound notification adapters.</summary>
    private static IServiceCollection AddNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<InvitationOptions>()
            .Bind(configuration.GetSection(InvitationOptions.SectionName))
            .Validate(
                o => o.LifetimeDays is > 0 and <= 30,
                "Invitations:LifetimeDays must be between 1 and 30. An invitation link is a " +
                "standing credential, and one that never expires is a permanent way into the tenant.")
            .ValidateOnStart();

        // The only implementation writes to the log and refuses to start in production, so a
        // deployment without a real mail adapter fails loudly rather than silently swallowing
        // every invitation.
        services.AddSingleton<IEmailSender, LoggingEmailSender>();

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
