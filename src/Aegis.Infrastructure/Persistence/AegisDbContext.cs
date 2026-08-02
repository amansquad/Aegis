using System.Linq.Expressions;
using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Application.Abstractions.Persistence;
using Aegis.Domain.Abstractions;
using Aegis.Domain.Auditing;
using Aegis.Domain.Identity;
using Aegis.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Aegis.Infrastructure.Persistence;

/// <summary>
/// The Aegis persistence context.
/// </summary>
/// <remarks>
/// Deliberately thin. Entity mapping lives in <see cref="IEntityTypeConfiguration{TEntity}"/>
/// classes discovered by assembly scan, and behaviour on save lives in interceptors. A context
/// that accumulates mapping code becomes a thousand-line file that every module has to touch,
/// which is precisely the coupling the module structure exists to prevent.
/// </remarks>
public sealed class AegisDbContext : DbContext, IAegisDbContext
{
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises the context.</summary>
    /// <param name="options">Provider and connection configuration.</param>
    /// <param name="tenantContext">Supplies the organization scoping every tenant-owned query.</param>
    public AegisDbContext(DbContextOptions<AegisDbContext> options, ITenantContext tenantContext)
        : base(options) => _tenantContext = tenantContext;

    /// <inheritdoc />
    public DbSet<AuditTrailEntry> AuditTrail => Set<AuditTrailEntry>();

    /// <inheritdoc />
    public DbSet<Organization> Organizations => Set<Organization>();

    /// <inheritdoc />
    public DbSet<User> Users => Set<User>();

    /// <inheritdoc />
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>
    /// The tenant applied by the global query filters.
    /// </summary>
    /// <remarks>
    /// Exposed as an instance property rather than read from <see cref="ITenantContext"/> inside
    /// the filter lambda for a specific reason: EF Core caches the compiled model, but re-evaluates
    /// a filter's reference to a context member on every query. Capturing the tenant value into a
    /// local at model-build time would instead bake the first request's tenant into the cached
    /// model, and every later request would silently read that tenant's data.
    /// </remarks>
    public Guid? CurrentTenantId => _tenantContext.OrganizationId;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AegisDbContext).Assembly);

        ApplyGlobalQueryFilters(modelBuilder);
        ApplyConcurrencyTokens(modelBuilder);
        ApplyDecimalPrecision(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Attaches tenant-scoping and soft-delete filters to every entity that opts in through a
    /// marker interface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applied by reflection over the model rather than declared per entity, because a filter that
    /// must be remembered for each new entity is a filter that will eventually be forgotten — and
    /// the consequence of forgetting the tenant predicate is cross-tenant data disclosure.
    /// </para>
    /// <para>
    /// The composed predicate reads
    /// <c>e =&gt; e.OrganizationId == CurrentTenantId &amp;&amp; !e.IsDeleted</c>, with each clause
    /// present only when the entity implements the corresponding interface.
    /// </para>
    /// <para>
    /// <b>Fail-closed.</b> When no tenant is established, <see cref="CurrentTenantId"/> is null and
    /// the predicate matches no rows. An unauthenticated caller sees nothing rather than
    /// everything, which is the correct direction for this failure to point.
    /// </para>
    /// </remarks>
    private void ApplyGlobalQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            // Owned types are filtered through their owner and cannot carry a filter themselves.
            if (entityType.IsOwned())
            {
                continue;
            }

            var isTenantOwned = typeof(ITenantOwned).IsAssignableFrom(clrType);
            var isTenantRoot = typeof(ITenantRoot).IsAssignableFrom(clrType);
            var isSoftDeletable = typeof(ISoftDeletable).IsAssignableFrom(clrType);

            if (!isTenantOwned && !isTenantRoot && !isSoftDeletable)
            {
                continue;
            }

            var parameter = Expression.Parameter(clrType, "e");
            Expression? predicate = null;

            var currentTenant = Expression.Property(
                Expression.Constant(this),
                nameof(CurrentTenantId));

            if (isTenantOwned)
            {
                // e.OrganizationId == this.CurrentTenantId
                var organizationId = Expression.Property(parameter, nameof(ITenantOwned.OrganizationId));

                predicate = Expression.Equal(
                    Expression.Convert(organizationId, typeof(Guid?)),
                    currentTenant);
            }
            else if (isTenantRoot)
            {
                // e.Id == this.CurrentTenantId
                //
                // The organization row is not owned by a tenant; its own key *is* the tenant. Left
                // unfiltered it would be the one table in the schema with no isolation, letting any
                // authenticated user enumerate every customer of the platform.
                var id = Expression.Property(parameter, nameof(ITenantRoot.Id));

                predicate = Expression.Equal(
                    Expression.Convert(id, typeof(Guid?)),
                    currentTenant);
            }

            if (isSoftDeletable)
            {
                // !e.IsDeleted
                var notDeleted = Expression.Not(
                    Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted)));

                predicate = predicate is null
                    ? notDeleted
                    : Expression.AndAlso(predicate, notDeleted);
            }

            modelBuilder
                .Entity(clrType)
                .HasQueryFilter(Expression.Lambda(predicate!, parameter));
        }
    }

    /// <summary>
    /// Maps the aggregate root concurrency token onto SQL Server's <c>rowversion</c>.
    /// </summary>
    /// <remarks>
    /// Two dispatchers editing the same work order is an ordinary occurrence, and last-write-wins
    /// would silently discard one of them. With the token mapped, EF Core raises
    /// <c>DbUpdateConcurrencyException</c>, which the API translates into HTTP 409 so the client
    /// can re-read and retry.
    /// </remarks>
    private static void ApplyConcurrencyTokens(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var versionProperty = entityType.FindProperty("Version");

            if (versionProperty is not null && versionProperty.ClrType == typeof(byte[]))
            {
                versionProperty.IsConcurrencyToken = true;
                versionProperty.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                versionProperty.SetColumnType("rowversion");
            }
        }
    }

    /// <summary>
    /// Gives every decimal an explicit precision.
    /// </summary>
    /// <remarks>
    /// SQL Server's default for an unconfigured decimal is <c>decimal(18,2)</c>, which silently
    /// truncates. For monetary values that is a rounding error in someone's budget report; for a
    /// sensor reading it is lost precision nobody notices until the trend line is wrong.
    /// <c>(18,4)</c> covers both currency and engineering quantities.
    /// </remarks>
    private static void ApplyDecimalPrecision(ModelBuilder modelBuilder)
    {
        foreach (var property in modelBuilder.Model
                     .GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            if (property.GetColumnType() is null && property.GetPrecision() is null)
            {
                property.SetPrecision(18);
                property.SetScale(4);
            }
        }
    }
}
