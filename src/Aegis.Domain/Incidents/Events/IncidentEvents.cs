using Aegis.Domain.Common;

namespace Aegis.Domain.Incidents.Events;

/// <summary>Raised when a report is accepted onto the system.</summary>
/// <remarks>
/// Carries severity and the review flag so a subscriber can decide urgency without loading the
/// incident. A Critical report that also requires review is the one that should be on a
/// dispatcher's screen within seconds, and that decision should not need a database round trip.
/// </remarks>
public sealed record IncidentReported(
    Guid IncidentId,
    Guid OrganizationId,
    string Reference,
    IncidentCategory Category,
    IncidentSeverity Severity,
    bool RequiresReview,
    bool PublicSafetyRisk) : DomainEvent;

/// <summary>Raised when a dispatcher confirms or corrects an incident's classification.</summary>
/// <remarks>
/// Carries both the proposed and the confirmed values. The difference between them is the only
/// measure of how well the extractor is performing, and it is worth capturing from the first day
/// rather than reconstructing later from an audit trail that was not designed for it.
/// </remarks>
public sealed record IncidentTriaged(
    Guid IncidentId,
    Guid OrganizationId,
    IncidentCategory ProposedCategory,
    IncidentCategory ConfirmedCategory,
    IncidentSeverity ProposedSeverity,
    IncidentSeverity ConfirmedSeverity,
    Guid TriagedBy) : DomainEvent;

/// <summary>Raised when an incident is linked to the asset it concerns.</summary>
public sealed record IncidentLinkedToAsset(
    Guid IncidentId,
    Guid OrganizationId,
    Guid AssetId) : DomainEvent;

/// <summary>Raised when the underlying problem has been fixed.</summary>
public sealed record IncidentResolved(
    Guid IncidentId,
    Guid OrganizationId,
    Guid ResolvedBy,
    TimeSpan TimeToResolve) : DomainEvent;

/// <summary>Raised when an incident is closed as a duplicate of another.</summary>
/// <remarks>
/// Worth its own event rather than a status change, because duplicates are the signal that one
/// real problem is generating many calls — which is itself an indicator of how widely something is
/// affecting people.
/// </remarks>
public sealed record IncidentMarkedDuplicate(
    Guid IncidentId,
    Guid OrganizationId,
    Guid OriginalIncidentId) : DomainEvent;

/// <summary>Raised when severity is raised after triage.</summary>
public sealed record IncidentEscalated(
    Guid IncidentId,
    Guid OrganizationId,
    IncidentSeverity FromSeverity,
    IncidentSeverity ToSeverity,
    string? Reason) : DomainEvent;
