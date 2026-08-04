using Aegis.Domain.Abstractions;
using Aegis.Domain.Common;
using Aegis.Domain.Maintenance.Events;

namespace Aegis.Domain.Maintenance;

/// <summary>
/// A recurring service schedule for one asset: do this again every so many days.
/// </summary>
/// <remarks>
/// <para>
/// <b>Generates work orders; does not do the work itself.</b> A plan has no assignee, no status
/// beyond active/inactive, and nothing a technician marks complete. When it is due, it produces a
/// <c>WorkOrder</c> through the ordinary dispatch path — the same assignment, start and completion
/// machinery already built for incident-driven work handles maintenance work too, rather than a
/// second, parallel completion flow that would inevitably drift from the first.
/// </para>
/// <para>
/// <b>The next due date advances from the actual completion date, not the old due date.</b> A
/// ninety-day plan completed five days early does not inherit that five-day drift forever; it is
/// simply due again in ninety days from when the work was actually done. Advancing from the
/// original due date would let a chronically-early or chronically-late crew slowly walk the
/// schedule away from what "every ninety days" is meant to mean.
/// </para>
/// </remarks>
public sealed class MaintenancePlan : AggregateRoot<Guid>, ITenantOwned, IAuditableEntity, ISoftDeletable
{
    /// <summary>Parameterless constructor required by EF Core materialisation.</summary>
    private MaintenancePlan()
    {
        Reference = string.Empty;
        Title = string.Empty;
    }

    private MaintenancePlan(
        Guid id,
        Guid organizationId,
        string reference,
        Guid assetId,
        string title,
        string? description,
        int frequencyDays,
        DateTimeOffset nextDueOnUtc) : base(id)
    {
        OrganizationId = organizationId;
        Reference = reference;
        AssetId = assetId;
        Title = title;
        Description = description;
        FrequencyDays = frequencyDays;
        NextDueOnUtc = nextDueOnUtc;
        IsActive = true;
    }

    /// <inheritdoc />
    public Guid OrganizationId { get; private set; }

    /// <summary>The reference quoted in the maintenance schedule, such as <c>MP-2026-4F2A91C3B7A1</c>.</summary>
    public string Reference { get; private set; }

    /// <summary>The asset this plan services.</summary>
    public Guid AssetId { get; private set; }

    /// <summary>Short description of the work, such as "Quarterly valve inspection".</summary>
    public string Title { get; private set; }

    /// <summary>Fuller detail or instructions for whoever carries out the work.</summary>
    public string? Description { get; private set; }

    /// <summary>How often the work recurs.</summary>
    public int FrequencyDays { get; private set; }

    /// <summary>When this plan is next due.</summary>
    public DateTimeOffset NextDueOnUtc { get; private set; }

    /// <summary>When a work order generated from this plan was last completed.</summary>
    public DateTimeOffset? LastCompletedOnUtc { get; private set; }

    /// <summary>Whether this plan is currently in rotation.</summary>
    public bool IsActive { get; private set; }

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

    /// <summary>True when this plan is active and its due date has arrived.</summary>
    public bool IsDue(DateTimeOffset asOf) => IsActive && NextDueOnUtc <= asOf;

    /// <summary>Creates a new plan, due immediately unless a later starting date is given.</summary>
    public static Result<MaintenancePlan> Create(
        Guid organizationId,
        Guid assetId,
        string? title,
        string? description,
        int frequencyDays,
        DateTimeOffset? startingOn,
        DateTimeOffset now)
    {
        if (assetId == Guid.Empty)
        {
            return Result.Failure<MaintenancePlan>(Error.Validation(
                "MaintenancePlan.InvalidAsset",
                "An asset is required."));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return Result.Failure<MaintenancePlan>(Error.Validation(
                "MaintenancePlan.TitleRequired",
                "A title is required."));
        }

        var trimmedTitle = title.Trim();

        if (trimmedTitle.Length > 200)
        {
            return Result.Failure<MaintenancePlan>(Error.Validation(
                "MaintenancePlan.TitleTooLong",
                "A title cannot exceed 200 characters."));
        }

        if (description is { Length: > 4000 })
        {
            return Result.Failure<MaintenancePlan>(Error.Validation(
                "MaintenancePlan.DescriptionTooLong",
                "A description cannot exceed 4000 characters."));
        }

        // Above 3,650 days (ten years) is almost certainly a typo — days entered where months or a
        // one-off date were meant — rather than a genuine decade-long service interval.
        if (frequencyDays is < 1 or > 3650)
        {
            return Result.Failure<MaintenancePlan>(Error.Validation(
                "MaintenancePlan.InvalidFrequency",
                "The frequency must be between 1 and 3650 days."));
        }

        var id = Guid.CreateVersion7();
        var nextDueOnUtc = startingOn ?? now;

        var plan = new MaintenancePlan(
            id,
            organizationId,
            BuildReference(id, now),
            assetId,
            trimmedTitle,
            description?.Trim(),
            frequencyDays,
            nextDueOnUtc);

        plan.RaiseDomainEvent(new MaintenancePlanCreated(id, organizationId, assetId, frequencyDays, nextDueOnUtc));

        return Result.Success(plan);
    }

    /// <summary>Advances the schedule after a work order this plan generated was completed.</summary>
    public void Advance(DateTimeOffset completedOn)
    {
        LastCompletedOnUtc = completedOn;
        NextDueOnUtc = completedOn.AddDays(FrequencyDays);

        RaiseDomainEvent(new MaintenancePlanAdvanced(Id, OrganizationId, AssetId, completedOn, NextDueOnUtc));
    }

    /// <summary>Takes the plan out of rotation. Work already generated from it is unaffected.</summary>
    public Result Deactivate()
    {
        if (!IsActive)
        {
            return Result.Failure(Error.Conflict(
                "MaintenancePlan.AlreadyInactive",
                "This plan is already inactive."));
        }

        IsActive = false;
        RaiseDomainEvent(new MaintenancePlanActivationChanged(Id, OrganizationId, IsActive));

        return Result.Success();
    }

    /// <summary>Puts a deactivated plan back into rotation.</summary>
    public Result Reactivate()
    {
        if (IsActive)
        {
            return Result.Failure(Error.Conflict(
                "MaintenancePlan.AlreadyActive",
                "This plan is already active."));
        }

        IsActive = true;
        RaiseDomainEvent(new MaintenancePlanActivationChanged(Id, OrganizationId, IsActive));

        return Result.Success();
    }

    private static string BuildReference(Guid id, DateTimeOffset now)
    {
        var hex = id.ToString("N");

        return $"MP-{now:yyyy}-{hex[^12..].ToUpperInvariant()}";
    }
}
