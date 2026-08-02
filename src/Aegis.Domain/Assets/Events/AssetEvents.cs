using Aegis.Domain.Common;

namespace Aegis.Domain.Assets.Events;

/// <summary>Raised when an asset is added to the registry.</summary>
public sealed record AssetRegistered(
    Guid AssetId,
    Guid OrganizationId,
    string AssetCode,
    AssetType Type) : DomainEvent;

/// <summary>
/// Raised when an inspection changes an asset's assessed condition.
/// </summary>
/// <remarks>
/// The trigger for predictive maintenance. A pump moving from Fair to Poor is the signal that
/// should schedule work before it fails, which is the entire difference between this platform and
/// a spreadsheet of inspection dates.
/// </remarks>
public sealed record AssetConditionChanged(
    Guid AssetId,
    Guid OrganizationId,
    AssetCondition PreviousCondition,
    AssetCondition CurrentCondition,
    AssetCriticality Criticality) : DomainEvent;

/// <summary>Raised when an inspection is recorded, whether or not the condition changed.</summary>
public sealed record AssetInspected(
    Guid AssetId,
    Guid OrganizationId,
    Guid InspectionId,
    AssetCondition Condition,
    DateTimeOffset InspectedOnUtc) : DomainEvent;

/// <summary>Raised when an asset's operational status changes.</summary>
public sealed record AssetStatusChanged(
    Guid AssetId,
    Guid OrganizationId,
    AssetStatus PreviousStatus,
    AssetStatus CurrentStatus) : DomainEvent;

/// <summary>Raised when an asset is permanently retired.</summary>
public sealed record AssetDecommissioned(
    Guid AssetId,
    Guid OrganizationId,
    string AssetCode,
    string? Reason) : DomainEvent;

/// <summary>Raised when an asset is physically relocated.</summary>
/// <remarks>
/// Worth an event of its own because a moved asset invalidates anything derived from its previous
/// position — cached map tiles, proximity-based incident matching, and the route a crew was given.
/// </remarks>
public sealed record AssetRelocated(
    Guid AssetId,
    Guid OrganizationId,
    double Latitude,
    double Longitude) : DomainEvent;
