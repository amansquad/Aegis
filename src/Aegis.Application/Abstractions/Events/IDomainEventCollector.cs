using Aegis.Domain.Common;

namespace Aegis.Application.Abstractions.Events;

/// <summary>
/// Scoped buffer holding domain events raised during the current request, awaiting dispatch.
/// </summary>
/// <remarks>
/// <para>
/// This exists to solve a genuine ordering problem. Domain events must not be dispatched until the
/// transaction commits, otherwise a rollback leaves notifications sent and audit rows written for
/// a change that never happened. But <c>SaveChangesAsync</c> is called by the handler, while the
/// commit happens in <see cref="Behaviors.UnitOfWorkBehavior{TRequest,TResponse}"/> after the
/// handler returns. An EF Core <c>SavedChangesAsync</c> interceptor therefore fires <em>before</em>
/// the commit, not after it.
/// </para>
/// <para>
/// Splitting collection from dispatch resolves that: the persistence interceptor harvests events
/// from tracked entities at save time and parks them here, and the unit-of-work behaviour drains
/// and dispatches them once the transaction has actually committed.
/// </para>
/// <para>
/// <b>Known limitation.</b> If the process dies between commit and dispatch, those events are
/// lost. Eliminating that window requires a transactional outbox: persist the events as rows in
/// the same transaction, then have a background processor deliver them at least once. That is the
/// correct end state and is planned; this buffer is the honest intermediate step, and it is
/// documented as such rather than presented as complete.
/// </para>
/// </remarks>
public interface IDomainEventCollector
{
    /// <summary>Adds events to the buffer, preserving the order in which they were raised.</summary>
    void Collect(IEnumerable<IDomainEvent> domainEvents);

    /// <summary>Removes and returns every buffered event.</summary>
    IReadOnlyCollection<IDomainEvent> Drain();

    /// <summary>True when the buffer holds at least one event.</summary>
    bool HasPendingEvents { get; }
}
