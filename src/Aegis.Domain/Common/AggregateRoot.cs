namespace Aegis.Domain.Common;

/// <summary>
/// Marks an entity as the root of an aggregate — the only member of its cluster that outside
/// code is permitted to hold a reference to.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate is the consistency boundary. Everything inside it is saved in one transaction
/// and its invariants always hold; anything outside is referenced by id and reached in a
/// separate transaction. In Aegis, <c>Asset</c>, <c>WorkOrder</c>, <c>Incident</c>,
/// <c>Organization</c> and <c>User</c> are aggregate roots. An asset's inspection records are
/// <em>inside</em> the asset aggregate (they have no meaning without it); the technician
/// assigned to a work order is referenced by <c>UserId</c> only.
/// </para>
/// <para>
/// The practical payoff: repositories and <c>DbSet</c> exposure are restricted to aggregate
/// roots, which is what keeps a "quick fix" from writing to a child entity behind its parent's
/// back and silently breaking an invariant.
/// </para>
/// </remarks>
/// <typeparam name="TId">The identifier type.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    /// <summary>Initialises a new aggregate root with the supplied identity.</summary>
    protected AggregateRoot(TId id) : base(id)
    {
    }

    /// <summary>Parameterless constructor required by EF Core materialisation.</summary>
    protected AggregateRoot()
    {
    }

    /// <summary>
    /// Optimistic concurrency token, mapped to SQL Server <c>rowversion</c>.
    /// </summary>
    /// <remarks>
    /// Two dispatchers editing the same work order is a realistic scenario, and last-write-wins
    /// would silently discard one of them. With this token EF Core raises
    /// <c>DbUpdateConcurrencyException</c> instead, which the API surfaces as HTTP 409.
    /// </remarks>
    public byte[]? Version { get; private set; }
}
