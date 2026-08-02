namespace Aegis.Domain.Common;

/// <summary>
/// A fact that has already happened inside the domain, expressed in the language of the business.
/// </summary>
/// <remarks>
/// <para>
/// Naming is past-tense and non-negotiable: <c>WorkOrderCompleted</c>, not <c>CompleteWorkOrder</c>.
/// An event describes something that <em>did</em> occur and therefore cannot be rejected by a
/// handler; a command describes something a caller <em>wants</em> to occur and can be refused.
/// </para>
/// <para>
/// Events are how modules stay decoupled. When an incident is created, the Incidents module does
/// not call the Notifications module — it raises <c>IncidentReported</c>, and whichever modules
/// care (SignalR push, audit log, SLA timer) subscribe independently. Adding a new reaction never
/// requires editing the code that raised the event, which is the Open/Closed Principle applied at
/// the module level rather than the class level.
/// </para>
/// <para>
/// Deliberately <em>not</em> derived from MediatR's <c>INotification</c>. Aegis.Domain has no
/// package references at all, so the domain model cannot be coupled to a mediator library that
/// may be replaced, relicensed, or dropped. Aegis.Application wraps events in
/// <c>DomainEventNotification&lt;T&gt;</c> at the boundary instead — a single adapter class is a
/// small price for a domain that compiles against nothing but the BCL.
/// </para>
/// </remarks>
public interface IDomainEvent
{
    /// <summary>Unique identity of this event occurrence, used for idempotent handling.</summary>
    Guid EventId { get; }

    /// <summary>UTC instant at which the event occurred.</summary>
    DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// Convenience base record supplying the identity and timestamp every domain event needs.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public DateTimeOffset OccurredOnUtc { get; init; } = DateTimeOffset.UtcNow;
}
