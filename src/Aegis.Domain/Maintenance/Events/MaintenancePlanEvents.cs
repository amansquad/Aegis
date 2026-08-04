using Aegis.Domain.Common;

namespace Aegis.Domain.Maintenance.Events;

/// <summary>Raised when a recurring maintenance plan is created for an asset.</summary>
public sealed record MaintenancePlanCreated(
    Guid MaintenancePlanId,
    Guid OrganizationId,
    Guid AssetId,
    int FrequencyDays,
    DateTimeOffset NextDueOnUtc) : DomainEvent;

/// <summary>
/// Raised when a plan's schedule advances after the work it generated was completed.
/// </summary>
public sealed record MaintenancePlanAdvanced(
    Guid MaintenancePlanId,
    Guid OrganizationId,
    Guid AssetId,
    DateTimeOffset CompletedOnUtc,
    DateTimeOffset NextDueOnUtc) : DomainEvent;

/// <summary>Raised when a plan is taken out of, or put back into, rotation.</summary>
public sealed record MaintenancePlanActivationChanged(
    Guid MaintenancePlanId,
    Guid OrganizationId,
    bool IsActive) : DomainEvent;
