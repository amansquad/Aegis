using Aegis.Domain.Abstractions;
using Aegis.Domain.Assets.Events;
using Aegis.Domain.Assets.ValueObjects;
using Aegis.Domain.Common;

namespace Aegis.Domain.Assets;

/// <summary>
/// A piece of physical infrastructure the organization is responsible for.
/// </summary>
/// <remarks>
/// <para>
/// The central aggregate of the platform. Incidents are reported against assets, work orders are
/// raised on assets, maintenance is scheduled from asset condition, and the map draws assets.
/// </para>
/// <para>
/// <b>Hierarchy is a parent reference, not a nested collection.</b> A pump belongs to a pumping
/// station, which belongs to a district. Modelling children as a collection inside the aggregate
/// would mean loading an entire estate to read one pump, and would make the aggregate's
/// transactional boundary the whole network. Each asset is its own aggregate; the parent is
/// referenced by id, exactly as an aggregate should reference another.
/// </para>
/// <para>
/// <b>Never physically deleted.</b> Regulated operators must retain records of what was installed
/// where, and for how long, often for decades after removal. Retirement sets the status to
/// <see cref="AssetStatus.Decommissioned"/>; soft deletion exists only for records created in
/// error.
/// </para>
/// </remarks>
public sealed class Asset : AggregateRoot<Guid>, ITenantOwned, IAuditableEntity, ISoftDeletable
{
    private readonly List<AssetInspection> _inspections = [];

    /// <summary>Parameterless constructor required by EF Core materialisation.</summary>
    private Asset()
    {
        Code = null!;
        Name = string.Empty;
    }

    private Asset(
        Guid id,
        Guid organizationId,
        AssetCode code,
        string name,
        AssetType type,
        GeoCoordinate? location) : base(id)
    {
        OrganizationId = organizationId;
        Code = code;
        Name = name;
        Type = type;
        Location = location;
        Status = AssetStatus.Planned;
        Condition = AssetCondition.Unknown;
        Criticality = AssetCriticality.Medium;
    }

    /// <inheritdoc />
    public Guid OrganizationId { get; private set; }

    /// <summary>The operator-facing identifier, unique within the organization.</summary>
    public AssetCode Code { get; private set; }

    /// <summary>Descriptive name, such as "Northgate Pumping Station — Pump 2".</summary>
    public string Name { get; private set; }

    /// <summary>What kind of infrastructure this is.</summary>
    public AssetType Type { get; private set; }

    /// <summary>Current operational status.</summary>
    public AssetStatus Status { get; private set; }

    /// <summary>Most recently assessed physical condition.</summary>
    public AssetCondition Condition { get; private set; }

    /// <summary>Consequence of failure.</summary>
    public AssetCriticality Criticality { get; private set; }

    /// <summary>
    /// Where the asset is, when that is known.
    /// </summary>
    /// <remarks>
    /// Nullable on purpose. Registries are populated from decades of paper records, and an asset
    /// whose position was never surveyed is a real and common state. Forcing a coordinate would
    /// mean inventing one, and an invented position is worse than an absent one — it puts a crew
    /// in the wrong street with confidence.
    /// </remarks>
    public GeoCoordinate? Location { get; private set; }

    /// <summary>The asset this one is part of, if any.</summary>
    public Guid? ParentAssetId { get; private set; }

    /// <summary>Manufacturer, where recorded.</summary>
    public string? Manufacturer { get; private set; }

    /// <summary>Model designation, where recorded.</summary>
    public string? ModelNumber { get; private set; }

    /// <summary>Manufacturer's serial number, where recorded.</summary>
    public string? SerialNumber { get; private set; }

    /// <summary>When the asset entered service.</summary>
    public DateOnly? InstalledOn { get; private set; }

    /// <summary>Design life in years, used to estimate remaining life.</summary>
    public int? ExpectedLifespanYears { get; private set; }

    /// <summary>When the asset was last inspected.</summary>
    public DateTimeOffset? LastInspectedOnUtc { get; private set; }

    /// <summary>When the asset was retired.</summary>
    public DateTimeOffset? DecommissionedOnUtc { get; private set; }

    /// <summary>Free-text description or operational notes.</summary>
    public string? Notes { get; private set; }

    /// <summary>Inspection history, oldest first.</summary>
    public IReadOnlyCollection<AssetInspection> Inspections => _inspections.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? ModifiedBy { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? DeletedBy { get; set; }

    /// <summary>True when the asset is in service or awaiting maintenance.</summary>
    public bool IsInService =>
        Status is AssetStatus.Operational or AssetStatus.UnderMaintenance or AssetStatus.Faulted;

    /// <summary>
    /// Estimated age in years at the supplied instant, when the installation date is known.
    /// </summary>
    public double? AgeInYears(DateTimeOffset now) =>
        InstalledOn is null
            ? null
            : (now - new DateTimeOffset(InstalledOn.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
                .TotalDays / 365.25;

    /// <summary>
    /// Proportion of design life consumed, where both installation date and design life are known.
    /// </summary>
    /// <remarks>
    /// A crude first input to replacement planning, and deliberately uncapped: a value above 1.0
    /// means the asset is running beyond its design life, which is exactly what a planner needs to
    /// see rather than have clamped away.
    /// </remarks>
    public double? LifeConsumed(DateTimeOffset now)
    {
        var age = AgeInYears(now);

        return age is null || ExpectedLifespanYears is null or <= 0
            ? null
            : age.Value / ExpectedLifespanYears.Value;
    }

    /// <summary>Adds an asset to the registry.</summary>
    public static Result<Asset> Register(
        Guid organizationId,
        AssetCode code,
        string? name,
        AssetType type,
        GeoCoordinate? location = null)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Asset>(Error.Validation(
                "Asset.NameRequired",
                "An asset name is required."));
        }

        var trimmed = name.Trim();

        if (trimmed.Length > 200)
        {
            return Result.Failure<Asset>(Error.Validation(
                "Asset.NameTooLong",
                "An asset name cannot exceed 200 characters."));
        }

        var asset = new Asset(Guid.CreateVersion7(), organizationId, code, trimmed, type, location);

        asset.RaiseDomainEvent(new AssetRegistered(asset.Id, organizationId, code.Value, type));

        return Result.Success(asset);
    }

    /// <summary>Updates descriptive and specification details.</summary>
    public Result UpdateDetails(
        string? name,
        AssetCriticality criticality,
        string? manufacturer,
        string? modelNumber,
        string? serialNumber,
        DateOnly? installedOn,
        int? expectedLifespanYears,
        string? notes,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("Asset.NameRequired", "An asset name is required."));
        }

        if (expectedLifespanYears is <= 0 or > 200)
        {
            return Result.Failure(Error.Validation(
                "Asset.InvalidLifespan",
                "Expected lifespan must be between 1 and 200 years."));
        }

        // A future installation date is legitimate for a Planned asset and nonsense for an
        // operational one, so the check is against the status rather than the calendar alone.
        if (installedOn is { } installed
            && Status != AssetStatus.Planned
            && installed > DateOnly.FromDateTime(now.UtcDateTime))
        {
            return Result.Failure(Error.Validation(
                "Asset.FutureInstallationDate",
                "An in-service asset cannot have a future installation date."));
        }

        Name = name.Trim();
        Criticality = criticality;
        Manufacturer = manufacturer?.Trim();
        ModelNumber = modelNumber?.Trim();
        SerialNumber = serialNumber?.Trim();
        InstalledOn = installedOn;
        ExpectedLifespanYears = expectedLifespanYears;
        Notes = notes?.Trim();

        return Result.Success();
    }

    /// <summary>Moves the asset to a new position.</summary>
    public Result Relocate(GeoCoordinate location)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (Status == AssetStatus.Decommissioned)
        {
            return Result.Failure(Error.Conflict(
                "Asset.Decommissioned",
                "A decommissioned asset cannot be relocated."));
        }

        if (Location == location)
        {
            // Not an error, but not an event either. Raising one would invalidate map caches and
            // re-run proximity matching for a change that did not happen.
            return Result.Success();
        }

        Location = location;

        RaiseDomainEvent(new AssetRelocated(Id, OrganizationId, location.Latitude, location.Longitude));

        return Result.Success();
    }

    /// <summary>Places the asset under a parent, or detaches it when null is supplied.</summary>
    public Result Reparent(Guid? parentAssetId)
    {
        // The only structural invariant that can be checked without loading the tree. Deeper cycles
        // — A under B under A — need the ancestor chain and are therefore checked in the handler,
        // where the repository is available.
        if (parentAssetId == Id)
        {
            return Result.Failure(Error.Validation(
                "Asset.SelfParent",
                "An asset cannot be its own parent."));
        }

        ParentAssetId = parentAssetId;

        return Result.Success();
    }

    /// <summary>Changes the operational status.</summary>
    public Result ChangeStatus(AssetStatus status)
    {
        if (Status == AssetStatus.Decommissioned)
        {
            return Result.Failure(Error.Conflict(
                "Asset.Decommissioned",
                "A decommissioned asset cannot return to service. Register a replacement instead."));
        }

        if (status == AssetStatus.Decommissioned)
        {
            return Result.Failure(Error.Validation(
                "Asset.UseDecommission",
                "Use the decommission operation to retire an asset."));
        }

        if (Status == status)
        {
            return Result.Success();
        }

        var previous = Status;
        Status = status;

        RaiseDomainEvent(new AssetStatusChanged(Id, OrganizationId, previous, status));

        return Result.Success();
    }

    /// <summary>
    /// Records an inspection and updates the asset's assessed condition.
    /// </summary>
    /// <remarks>
    /// The inspection and the resulting condition change are one operation precisely because they
    /// must not diverge. An asset whose condition disagrees with its most recent inspection cannot
    /// be explained to an auditor.
    /// </remarks>
    public Result<AssetInspection> RecordInspection(
        AssetCondition condition,
        DateTimeOffset inspectedOnUtc,
        Guid inspectedBy,
        string? notes,
        DateTimeOffset now)
    {
        if (Status == AssetStatus.Decommissioned)
        {
            return Result.Failure<AssetInspection>(Error.Conflict(
                "Asset.Decommissioned",
                "A decommissioned asset cannot be inspected."));
        }

        var inspection = AssetInspection.Record(condition, inspectedOnUtc, inspectedBy, notes, now);

        if (inspection.IsFailure)
        {
            return inspection;
        }

        _inspections.Add(inspection.Value);

        RaiseDomainEvent(new AssetInspected(
            Id,
            OrganizationId,
            inspection.Value.Id,
            condition,
            inspectedOnUtc));

        // Only the most recent inspection sets the current condition. A backdated inspection —
        // paper records being digitised, or an offline device syncing late — must not overwrite a
        // newer assessment with an older one.
        if (LastInspectedOnUtc is null || inspectedOnUtc >= LastInspectedOnUtc)
        {
            LastInspectedOnUtc = inspectedOnUtc;

            if (Condition != condition)
            {
                var previous = Condition;
                Condition = condition;

                RaiseDomainEvent(new AssetConditionChanged(
                    Id,
                    OrganizationId,
                    previous,
                    condition,
                    Criticality));
            }
        }

        return inspection;
    }

    /// <summary>Permanently retires the asset.</summary>
    public Result Decommission(DateTimeOffset now, string? reason)
    {
        if (Status == AssetStatus.Decommissioned)
        {
            return Result.Failure(Error.Conflict(
                "Asset.AlreadyDecommissioned",
                "This asset has already been decommissioned."));
        }

        Status = AssetStatus.Decommissioned;
        DecommissionedOnUtc = now;

        RaiseDomainEvent(new AssetDecommissioned(Id, OrganizationId, Code.Value, reason?.Trim()));

        return Result.Success();
    }
}
