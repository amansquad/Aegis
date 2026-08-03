using Aegis.Domain.Assets;
using Aegis.Domain.Auditing;
using Aegis.Domain.Identity;
using Aegis.Domain.Incidents;
using Aegis.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Aegis.Application.Abstractions.Persistence;

/// <summary>
/// The persistence port. Handlers depend on this rather than on the concrete
/// <c>AegisDbContext</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>On the EF Core dependency.</b> This interface exposes <see cref="DbSet{TEntity}"/>, which
/// means Aegis.Application references <c>Microsoft.EntityFrameworkCore</c>. That is a deliberate,
/// documented compromise rather than an oversight.
/// </para>
/// <para>
/// The purist alternative — exposing <c>IQueryable&lt;T&gt;</c> — costs <c>Include</c>,
/// <c>AsNoTracking</c>, <c>ExecuteUpdateAsync</c>, and every async terminal operator
/// (<c>ToListAsync</c> and friends are EF Core extension methods, not LINQ). Recovering those
/// would mean hand-rolling an async query abstraction whose only purpose is to avoid naming EF
/// Core. The abstraction would be larger than the thing it hides.
/// </para>
/// <para>
/// What actually matters is preserved: no SQL Server provider, no connection strings, no
/// migrations, and no <c>DbContext</c> base class in this project. The layer depends on a query
/// model, not on a database. Aegis.Domain remains free of even this.
/// </para>
/// <para>
/// <b>Why no generic repository.</b> <c>DbContext</c> is already a Unit of Work and
/// <c>DbSet&lt;T&gt;</c> is already a repository. Wrapping them in
/// <c>IGenericRepository&lt;T&gt;</c> adds a forwarding layer and, more damagingly, tends to force
/// materialisation at the boundary — filtering and paging then happen in memory over rows the
/// database should never have sent. Dedicated repositories are introduced only for aggregates
/// whose reconstitution is non-trivial.
/// </para>
/// </remarks>
public interface IAegisDbContext
{
    /// <summary>
    /// The append-only audit trail.
    /// </summary>
    /// <remarks>
    /// Exposed for querying activity history. Entries are written by the persistence interceptor,
    /// never by handler code, so nothing should ever call <c>Add</c> on this set.
    /// </remarks>
    DbSet<AuditTrailEntry> AuditTrail { get; }

    /// <summary>Tenants. Filtered so an organization can only ever see itself.</summary>
    DbSet<Organization> Organizations { get; }

    /// <summary>User accounts within the current organization.</summary>
    DbSet<User> Users { get; }

    /// <summary>Roles defined by the current organization.</summary>
    DbSet<Role> Roles { get; }

    /// <summary>Infrastructure assets belonging to the current organization.</summary>
    DbSet<Asset> Assets { get; }

    /// <summary>Reported problems belonging to the current organization.</summary>
    DbSet<Incident> Incidents { get; }

    /// <summary>
    /// Escape hatch for raw SQL, transactions and provider-specific operations.
    /// </summary>
    /// <remarks>
    /// Present because a small number of legitimate operations — an explicit transaction spanning
    /// two aggregates, a bulk spatial update — cannot be expressed through the change tracker.
    /// Uses of this member should be rare and reviewed.
    /// </remarks>
    DatabaseFacade Database { get; }

    /// <summary>
    /// Commits all tracked changes as a single transaction.
    /// </summary>
    /// <remarks>
    /// Interceptors run around this call: audit and tenant stamping before the write, domain event
    /// dispatch after it commits. A handler simply calls this and gets all of that behaviour.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the <see cref="DbSet{TEntity}"/> for the supplied entity type.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    DbSet<TEntity> Set<TEntity>() where TEntity : class;
}
