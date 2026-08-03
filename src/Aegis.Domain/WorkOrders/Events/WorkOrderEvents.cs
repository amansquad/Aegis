using Aegis.Domain.Common;

namespace Aegis.Domain.WorkOrders.Events;

/// <summary>Raised when a work order is created.</summary>
public sealed record WorkOrderCreated(
    Guid WorkOrderId,
    Guid OrganizationId,
    string Reference,
    WorkOrderPriority Priority,
    Guid? AssetId,
    Guid? IncidentId) : DomainEvent;

/// <summary>Raised when a technician is assigned.</summary>
/// <remarks>
/// Carries the previous assignee, if any, so a subscriber (a notification, say) can tell a
/// reassignment from a first assignment without a second lookup.
/// </remarks>
public sealed record WorkOrderAssigned(
    Guid WorkOrderId,
    Guid OrganizationId,
    Guid AssignedToUserId,
    Guid? PreviouslyAssignedToUserId) : DomainEvent;

/// <summary>Raised when work begins.</summary>
public sealed record WorkOrderStarted(Guid WorkOrderId, Guid OrganizationId, Guid StartedBy) : DomainEvent;

/// <summary>Raised when work is completed.</summary>
/// <remarks>
/// Carries the asset and incident it concerned so a subscriber can react — updating an asset's
/// maintenance history, or resolving the incident that raised the request — without loading the
/// work order again.
/// </remarks>
public sealed record WorkOrderCompleted(
    Guid WorkOrderId,
    Guid OrganizationId,
    Guid CompletedBy,
    Guid? AssetId,
    Guid? IncidentId,
    TimeSpan TimeToComplete) : DomainEvent;

/// <summary>Raised when a work order is withdrawn without being completed.</summary>
public sealed record WorkOrderCancelled(
    Guid WorkOrderId,
    Guid OrganizationId,
    string? Reason) : DomainEvent;
