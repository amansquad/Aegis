using System.Collections.Concurrent;
using Aegis.Application.Abstractions.Events;
using Aegis.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Aegis.Infrastructure.Events;

/// <summary>
/// Publishes domain events through MediatR, wrapping each in
/// <see cref="DomainEventNotification{TDomainEvent}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The wrapper type must be closed over the event's runtime type, not its static type: an event
/// held as <see cref="IDomainEvent"/> would otherwise be published as
/// <c>DomainEventNotification&lt;IDomainEvent&gt;</c>, which no handler subscribes to, and the
/// events would vanish without error. The closed type is built once per event type and cached.
/// </para>
/// <para>
/// <b>Events are dispatched sequentially and a failing handler stops the batch.</b> Ordering is
/// meaningful — an incident's work order should exist before the notification announcing it — and
/// swallowing handler exceptions would hide a broken subscriber indefinitely. Handlers whose
/// failure genuinely should not block the rest belong on a queue, not in this pipeline.
/// </para>
/// </remarks>
public sealed class DomainEventDispatcher(
    IPublisher publisher,
    ILogger<DomainEventDispatcher> logger) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Func<IDomainEvent, INotification>> Wrappers = new();

    /// <inheritdoc />
    public async Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (var domainEvent in domainEvents)
        {
            var notification = Wrap(domainEvent);

            logger.LogDebug(
                "Dispatching {DomainEvent} ({EventId})",
                domainEvent.GetType().Name,
                domainEvent.EventId);

            await publisher.Publish(notification, cancellationToken);
        }
    }

    private static INotification Wrap(IDomainEvent domainEvent)
    {
        var factory = Wrappers.GetOrAdd(domainEvent.GetType(), static eventType =>
        {
            var wrapperType = typeof(DomainEventNotification<>).MakeGenericType(eventType);

            return e => (INotification)Activator.CreateInstance(wrapperType, e)!;
        });

        return factory(domainEvent);
    }
}
