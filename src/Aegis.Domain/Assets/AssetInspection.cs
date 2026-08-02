using Aegis.Domain.Common;

namespace Aegis.Domain.Assets;

/// <summary>
/// A single condition assessment of an asset at a point in time.
/// </summary>
/// <remarks>
/// <para>
/// A child of the <see cref="Asset"/> aggregate: an inspection has no meaning apart from the asset
/// it assessed, and the two are always saved together so that an asset's current condition can
/// never disagree with the inspection that set it.
/// </para>
/// <para>
/// Immutable once written. A revised assessment is a new inspection, not an edit — the history of
/// what an inspector believed and when is the raw material for every deterioration model the
/// platform will later build, and an editable record makes that history unreliable.
/// </para>
/// </remarks>
public sealed class AssetInspection : Entity<Guid>
{
    /// <summary>Parameterless constructor required by EF Core materialisation.</summary>
    private AssetInspection()
    {
    }

    private AssetInspection(
        Guid id,
        AssetCondition condition,
        DateTimeOffset inspectedOnUtc,
        Guid inspectedBy,
        string? notes) : base(id)
    {
        Condition = condition;
        InspectedOnUtc = inspectedOnUtc;
        InspectedBy = inspectedBy;
        Notes = notes;
    }

    /// <summary>The condition assessed.</summary>
    public AssetCondition Condition { get; private set; }

    /// <summary>
    /// When the inspection took place.
    /// </summary>
    /// <remarks>
    /// Supplied by the caller rather than taken from the clock, because a technician working
    /// offline records an inspection in the field and syncs it hours later. Stamping it at sync
    /// time would misdate every offline inspection, which is most of them.
    /// </remarks>
    public DateTimeOffset InspectedOnUtc { get; private set; }

    /// <summary>The user who carried out the inspection.</summary>
    public Guid InspectedBy { get; private set; }

    /// <summary>Free-text observations.</summary>
    public string? Notes { get; private set; }

    /// <summary>Records an inspection.</summary>
    /// <param name="condition">The assessed condition.</param>
    /// <param name="inspectedOnUtc">When the inspection took place.</param>
    /// <param name="inspectedBy">The inspecting user.</param>
    /// <param name="notes">Optional observations.</param>
    /// <param name="now">Current time, used to reject future-dated inspections.</param>
    internal static Result<AssetInspection> Record(
        AssetCondition condition,
        DateTimeOffset inspectedOnUtc,
        Guid inspectedBy,
        string? notes,
        DateTimeOffset now)
    {
        if (condition == AssetCondition.Unknown)
        {
            return Result.Failure<AssetInspection>(Error.Validation(
                "Inspection.ConditionRequired",
                "An inspection must record an assessed condition."));
        }

        // A small tolerance rather than a hard comparison. Field devices drift, and rejecting an
        // inspection because a technician's tablet is ninety seconds fast would block real work
        // for no benefit.
        if (inspectedOnUtc > now.AddMinutes(5))
        {
            return Result.Failure<AssetInspection>(Error.Validation(
                "Inspection.FutureDated",
                "An inspection cannot be dated in the future."));
        }

        if (notes is { Length: > 2000 })
        {
            return Result.Failure<AssetInspection>(Error.Validation(
                "Inspection.NotesTooLong",
                "Inspection notes cannot exceed 2000 characters."));
        }

        return Result.Success(new AssetInspection(
            Guid.CreateVersion7(),
            condition,
            inspectedOnUtc,
            inspectedBy,
            notes?.Trim()));
    }
}
