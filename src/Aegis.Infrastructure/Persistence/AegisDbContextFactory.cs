using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Infrastructure.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aegis.Infrastructure.Persistence;

/// <summary>
/// Builds an <see cref="AegisDbContext"/> for the <c>dotnet ef</c> tooling.
/// </summary>
/// <remarks>
/// <para>
/// Without this, EF tooling boots the whole API host to obtain a context, which means adding a
/// migration requires a working connection string, a reachable Redis, and every other startup
/// dependency to be satisfied. Since the application deliberately carries no connection string in
/// source control, that would make migrations impossible to author without local secrets set up.
/// </para>
/// <para>
/// The placeholder connection string is never connected to. <c>migrations add</c> and
/// <c>migrations script</c> only need the provider to translate the model into DDL; a live server
/// is required solely by <c>database update</c>, and that path supplies a real connection through
/// <see cref="ConnectionStringVariable"/>.
/// </para>
/// </remarks>
public sealed class AegisDbContextFactory : IDesignTimeDbContextFactory<AegisDbContext>
{
    /// <summary>Environment variable supplying a real connection string when one is needed.</summary>
    public const string ConnectionStringVariable = "AEGIS_MIGRATIONS_CONNECTION";

    private const string PlaceholderConnectionString =
        "Server=(localdb)\\aegis-design-time;Database=Aegis;Trusted_Connection=True";

    /// <inheritdoc />
    public AegisDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringVariable)
            ?? PlaceholderConnectionString;

        var options = new DbContextOptionsBuilder<AegisDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "dbo");
                sql.UseNetTopologySuite();
            })
            .Options;

        // A tenant-free context. Global query filters compile against a null organization, which
        // is correct here: migration scaffolding reads the model's shape, never its rows.
        ITenantContext tenantContext = new TenantContext();

        return new AegisDbContext(options, tenantContext);
    }
}
