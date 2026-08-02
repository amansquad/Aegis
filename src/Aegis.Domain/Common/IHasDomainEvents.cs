namespace Aegis.Domain.Common;

/// <summary>
/// Exposes an entity's pending domain events without reference to its identifier type.
/// </summary>
/// <remarks>
/// <see cref="Entity{TId}"/> is generic, so the persistence layer cannot query the change tracker
/// for "anything that raises events" through it — <c>ChangeTracker.Entries&lt;Entity&lt;Guid&gt;&gt;()</c>
/// would silently miss any aggregate keyed by something other than <see cref="Guid"/>. This
/// non-generic interface gives the interceptor one stable type to look for, so an aggregate keyed
/// by a strongly-typed id or a composite key still has its events collected.
/// </remarks>
public interface IHasDomainEvents
{
    /// <summary>Events raised during the current unit of work, in the order raised.</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Clears the pending event list once the events have been collected for dispatch.</summary>
    void ClearDomainEvents();
}
