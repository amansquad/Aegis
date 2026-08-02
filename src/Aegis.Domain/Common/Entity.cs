namespace Aegis.Domain.Common;

/// <summary>
/// Base class for all domain entities.
/// </summary>
/// <remarks>
/// <para>
/// Entities are distinguished by identity, not by attribute values: two assets with identical
/// serial numbers, locations and install dates are still two different assets. Equality is
/// therefore defined solely by <see cref="Id"/> and concrete runtime type.
/// </para>
/// <para>
/// Entities also collect <see cref="IDomainEvent"/> instances. Events are raised inside domain
/// methods but are <em>not</em> dispatched there — the persistence layer publishes them after
/// <c>SaveChangesAsync</c> succeeds. This guarantees that no side effect (notification, audit
/// entry, cache invalidation) can fire for a transaction that later rolls back.
/// </para>
/// </remarks>
/// <typeparam name="TId">The identifier type. Must be non-nullable.</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Initialises a new entity with the supplied identity.</summary>
    protected Entity(TId id) => Id = id;

    /// <summary>Parameterless constructor required by EF Core materialisation.</summary>
    /// <remarks>
    /// EF Core sets <see cref="Id"/> reflectively when rehydrating from the database, so the
    /// null-forgiving operator here is accurate rather than a suppression of a real problem.
    /// </remarks>
    protected Entity() => Id = default!;

    /// <summary>The entity's unique identity. Immutable once assigned.</summary>
    public TId Id { get; protected init; }

    /// <summary>
    /// Domain events raised by this entity during the current unit of work, in the order raised.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Records a domain event to be dispatched once the unit of work commits.</summary>
    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>
    /// Clears the pending event list. Called by the persistence layer after dispatch so that a
    /// long-lived tracked entity cannot replay the same event twice.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <inheritdoc />
    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Proxy-safe: EF Core lazy-loading proxies are subclasses, so comparing GetType()
        // directly would report a proxy and its entity as unequal. Comparing in both
        // directions via IsInstanceOfType tolerates that.
        return GetType().IsInstanceOfType(other)
            && other.GetType().IsInstanceOfType(this)
            && EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
