using Aegis.Application.Abstractions.Events;
using Aegis.Domain.Common;

namespace Aegis.Infrastructure.Events;

/// <summary>
/// Scoped, ordered buffer of domain events awaiting post-commit dispatch.
/// </summary>
/// <remarks>
/// Registered as scoped so that each request has its own buffer. A singleton would let one
/// request's events be dispatched under another request's tenant and user, which is both a
/// correctness and an isolation failure.
/// </remarks>
public sealed class DomainEventCollector : IDomainEventCollector
{
    private readonly List<IDomainEvent> _events = [];
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public bool HasPendingEvents
    {
        get
        {
            lock (_gate)
            {
                return _events.Count > 0;
            }
        }
    }

    /// <inheritdoc />
    public void Collect(IEnumerable<IDomainEvent> domainEvents)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        lock (_gate)
        {
            _events.AddRange(domainEvents);
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IDomainEvent> Drain()
    {
        lock (_gate)
        {
            if (_events.Count == 0)
            {
                return [];
            }

            var drained = _events.ToArray();
            _events.Clear();

            return drained;
        }
    }
}
