using Aegis.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace Aegis.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API against throwaway SQL Server and Redis containers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not the EF Core in-memory provider.</b> It does not enforce unique constraints, does not
/// translate the same SQL, and — decisively for this codebase — does not exercise global query
/// filters against a cached model the way the real provider does. A green in-memory suite would
/// therefore prove almost nothing about tenant isolation, which is the single property most
/// needing proof here.
/// </para>
/// <para>
/// Containers are started once per test collection rather than per test. A SQL Server container
/// takes several seconds to become ready, and paying that per test would make the suite something
/// developers skip.
/// </para>
/// </remarks>
public sealed class AegisWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _database = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Integration_Test_P@ssw0rd")
        .WithCleanUp(true)
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .WithCleanUp(true)
        .Build();

    /// <summary>Connection string for the containerised database.</summary>
    public string DatabaseConnectionString => _database.GetConnectionString();

    /// <summary>Starts the containers and applies migrations.</summary>
    /// <remarks>
    /// xUnit constructs and initialises a collection fixture even when every test in the collection
    /// is skipped, so without this guard a machine with no Docker daemon would fail during fixture
    /// setup rather than reporting clean skips. <see cref="DockerRequirement.IsRequired"/> keeps CI
    /// honest by disabling the guard there.
    /// </remarks>
    public async Task InitializeAsync()
    {
        if (!DockerRequirement.IsAvailable && !DockerRequirement.IsRequired)
        {
            return;
        }

        // Started in parallel: they are independent, and serialising them roughly doubles the
        // fixed cost every test run pays before the first assertion.
        await Task.WhenAll(_database.StartAsync(), _redis.StartAsync());

        PublishContainerEndpoints();

        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AegisDbContext>();

        // Migrate rather than EnsureCreated. EnsureCreated builds the schema from the model and
        // bypasses migrations entirely, so a broken or missing migration would still produce a
        // green suite and fail only on deployment.
        await context.Database.MigrateAsync();
    }

    /// <summary>Stops and removes the containers.</summary>
    public new async Task DisposeAsync()
    {
        await Task.WhenAll(_database.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
        await base.DisposeAsync();
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Development);
    }

    /// <summary>
    /// Exposes the containers' endpoints to the application through environment variables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Environment variables rather than <c>ConfigureAppConfiguration</c>, for a specific
    /// reason.</b> Under minimal hosting, <c>Program.cs</c> reads <c>builder.Configuration</c> while
    /// registering services, but the configuration sources a <c>WebApplicationFactory</c> adds
    /// through <c>ConfigureAppConfiguration</c> are only applied later, when the host is built. The
    /// injected values therefore arrive after <c>AddInfrastructure</c> has already looked for the
    /// connection string and found nothing.
    /// </para>
    /// <para>
    /// Environment variables are read by the application's own <c>CreateBuilder</c> call, so
    /// setting them before the host is first touched puts the values in place in time. CI caught
    /// this the hard way: every integration test failed at startup with a missing connection string.
    /// </para>
    /// <para>
    /// These are process-wide, which is acceptable because the test process hosts exactly one
    /// fixture for the lifetime of the run.
    /// </para>
    /// </remarks>
    private void PublishContainerEndpoints()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Database", _database.GetConnectionString());
        Environment.SetEnvironmentVariable("Cache__ConnectionString", _redis.GetConnectionString());
        Environment.SetEnvironmentVariable("Cache__InstanceName", "aegis-test");

        // Caching is off by default so that persistence assertions observe the database rather than
        // a cached answer left by an earlier test. Tests that exist to exercise the cache turn it
        // back on explicitly.
        Environment.SetEnvironmentVariable("Cache__Enabled", "false");

        // A fixed, obviously fake signing key. Fixed rather than random so that a token minted in
        // one test can be presented in another; obviously fake so it can never be mistaken for a
        // real one if it escapes into a log or a copied snippet.
        Environment.SetEnvironmentVariable("Jwt__SigningKey", TestSigningKey);

        // Every test in the suite shares one source address, so production budgets would exhaust
        // the registration allowance within the first few tests and fail everything after them —
        // with a 429 that looks nothing like the assertion that actually failed.
        //
        // Raised rather than disabled, so the limiter is still in the pipeline and still wired to
        // the endpoints. RateLimitingTests then verifies the mechanism against these budgets, which
        // keeps the control tested without letting it interfere with unrelated assertions.
        Environment.SetEnvironmentVariable("RateLimiting__RegistrationPermitLimit", "500");
        Environment.SetEnvironmentVariable("RateLimiting__RegistrationWindowSeconds", "60");
        Environment.SetEnvironmentVariable("RateLimiting__AuthenticationPermitLimit", "40");
        Environment.SetEnvironmentVariable("RateLimiting__AuthenticationWindowSeconds", "60");
    }

    /// <summary>Authentication requests permitted per window in the test host.</summary>
    public const int TestAuthenticationPermitLimit = 40;

    /// <summary>The signing key used by the test host. Not a secret, and deliberately not usable.</summary>
    public const string TestSigningKey =
        "integration-test-signing-key-not-a-secret-0123456789abcdef";

    /// <summary>
    /// Creates a scope whose tenant is already established, for tests that exercise persistence
    /// directly rather than through HTTP.
    /// </summary>
    /// <param name="organizationId">The organization to scope the scope to, or null for none.</param>
    public AsyncServiceScope CreateTenantScope(Guid? organizationId)
    {
        var scope = Services.CreateAsyncScope();

        if (organizationId.HasValue)
        {
            scope.ServiceProvider
                .GetRequiredService<Application.Abstractions.Multitenancy.ITenantContext>()
                .SetTenant(organizationId.Value);
        }

        return scope;
    }
}
