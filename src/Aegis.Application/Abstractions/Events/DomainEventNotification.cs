using Aegis.Domain.Common;
using MediatR;

namespace Aegis.Application.Abstractions.Events;

/// <summary>
/// Adapts a framework-free <see cref="IDomainEvent"/> into a MediatR notification.
/// </summary>
/// <remarks>
/// <para>
/// This single class is what keeps Aegis.Domain free of package references. Domain events are
/// plain records that know nothing about MediatR; the dispatcher wraps each one here at the
/// boundary, and handlers subscribe to the wrapper.
/// </para>
/// <para>
/// The cost is one extra generic parameter at each handler declaration:
/// <code>
/// internal sealed class NotifyDispatchers
///     : INotificationHandler&lt;DomainEventNotification&lt;IncidentReported&gt;&gt;
/// </code>
/// That is the entire price of being able to swap or drop MediatR without touching the domain
/// model — a trade worth making given MediatR's licence change at version 13.
/// </para>
/// </remarks>
/// <typeparam name="TDomainEvent">The wrapped domain event type.</typeparam>
/// <param name="DomainEvent">The event that occurred.</param>
public sealed record DomainEventNotification<TDomainEvent>(TDomainEvent DomainEvent) : INotification
    where TDomainEvent : IDomainEvent;

/// <summary>
/// Publishes domain events collected by aggregates during a unit of work.
/// </summary>
/// <remarks>
/// Invoked by the persistence layer <em>after</em> the transaction commits. Dispatching before
/// commit would let a notification be sent, a cache invalidated and an audit row written for a
/// change that then rolls back — side effects describing an event that never happened.
/// </remarks>
public interface IDomainEventDispatcher
{
    /// <summary>Publishes the supplied events to their registered handlers.</summary>
    Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default);
}
