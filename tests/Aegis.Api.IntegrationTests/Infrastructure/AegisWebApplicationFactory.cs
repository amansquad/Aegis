using Aegis.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = _database.GetConnectionString(),
                ["Cache:ConnectionString"] = _redis.GetConnectionString(),
                ["Cache:InstanceName"] = "aegis-test",

                // Caching is disabled by default so that persistence assertions observe the
                // database rather than a cached answer from a previous test. Tests that exist to
                // exercise the cache re-enable it explicitly.
                ["Cache:Enabled"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replaces JWT bearer authentication with a header-driven stub, so tests can assert on
            // tenant scoping without an Identity module that does not exist yet.
            services.AddTestAuthentication();
        });
    }

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
