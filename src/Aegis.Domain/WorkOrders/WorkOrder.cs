using Aegis.Domain.Abstractions;
using Aegis.Domain.Common;
using Aegis.Domain.WorkOrders.Events;

namespace Aegis.Domain.WorkOrders;

/// <summary>
/// A unit of dispatched field work: fix this, at this asset, by this person.
/// </summary>
/// <remarks>
/// <para>
/// <b>Priority is set by the dispatcher, not copied from the incident.</b> A work order created
/// from a Critical incident does not automatically become a Critical work order — dispatch is a
/// planning decision informed by severity, made by a person who also knows crew availability and
/// what else is already scheduled. Collapsing the two scales into one would remove exactly the
/// judgement a dispatcher exists to apply.
/// </para>
/// <para>
/// <b>Never physically deleted, for the same reason as everything else in this registry.</b>
/// Regulated operators must be able to say what work was done, on what, and by whom, often years
/// later. Cancellation records that a work order was withdrawn; it does not erase that it existed.
/// </para>
/// </remarks>
public sealed class WorkOrder : AggregateRoot<Guid>, ITenantOwned, IAuditableEntity, ISoftDeletable
{
    /// <summary>Parameterless constructor required by EF Core materialisation.</summary>
    private WorkOrder()
    {
        Reference = string.Empty;
        Title = string.Empty;
    }

    private WorkOrder(
        Guid id,
        Guid organizationId,
        string reference,
        string title,
        string? description,
        WorkOrderPriority priority,
        Guid? assetId,
        Guid? incidentId,
        Guid? maintenancePlanId) : base(id)
    {
        OrganizationId = organizationId;
        Reference = reference;
        Title = title;
        Description = description;
        Priority = priority;
        AssetId = assetId;
        IncidentId = incidentId;
        MaintenancePlanId = maintenancePlanId;
        Status = WorkOrderStatus.Draft;
    }

    /// <inheritdoc />
    public Guid OrganizationId { get; private set; }

    /// <summary>
    /// The reference quoted on paperwork and over the radio, such as <c>WO-2026-4F2A91C3B7A1</c>.
    /// </summary>
    /// <remarks>
    /// Derived from the identifier's random tail rather than a per-organization counter — the same
    /// choice made for incidents, and for the same two reasons: a sequential counter needs a
    /// database sequence that becomes a write-contention point on the busiest insert path, and a
    /// counter shared across tenants would let one customer infer another's work order volume from
    /// the gaps in their own references.
    /// </remarks>
    public string Reference { get; private set; }

    /// <summary>Short description of the work, such as "Replace failed isolation valve".</summary>
    public string Title { get; private set; }

    /// <summary>Fuller detail, instructions, or context for the assigned technician.</summary>
    public string? Description { get; private set; }

    /// <summary>Where this sits in its execution lifecycle.</summary>
    public WorkOrderStatus Status { get; private set; }

    /// <summary>How urgently this needs doing, as judged by whoever dispatched it.</summary>
    public WorkOrderPriority Priority { get; private set; }

    /// <summary>The asset this work concerns, when there is one.</summary>
    public Guid? AssetId { get; private set; }

    /// <summary>
    /// The incident this work resolves, when it originated from one.
    /// </summary>
    /// <remarks>
    /// Not every work order traces back to a reported incident — plenty of maintenance is
    /// scheduled from an asset's condition rather than a member of the public's report — so this
    /// is optional rather than a required parent.
    /// </remarks>
    public Guid? IncidentId { get; private set; }

    /// <summary>
    /// The maintenance plan this work order was generated from, when it was.
    /// </summary>
    /// <remarks>
    /// Not every work order comes from a schedule — plenty are dispatched directly against an
    /// asset or in response to an incident — so this is optional in exactly the same way
    /// <see cref="IncidentId"/> is. Completing a work order that carries this advances the plan's
    /// next due date, the same loop-closing behaviour incidents already get.
    /// </remarks>
    public Guid? MaintenancePlanId { get; private set; }

    /// <summary>The technician currently responsible, once assigned.</summary>
    public Guid? AssignedToUserId { get; private set; }

    /// <summary>When the work is planned for, once scheduled.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>When work actually began.</summary>
    public DateTimeOffset? StartedOnUtc { get; private set; }

    /// <summary>When the work was completed.</summary>
    public DateTimeOffset? CompletedOnUtc { get; private set; }

    /// <summary>What was done, recorded on completion.</summary>
    public string? CompletionNotes { get; private set; }

    /// <summary>Why the work order was withdrawn, when it was.</summary>
    public string? CancellationReason { get; private set; }

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

    /// <summary>True while the work order is still active in some form.</summary>
    public bool IsOpen => Status is WorkOrderStatus.Draft or WorkOrderStatus.Scheduled
        or WorkOrderStatus.InProgress;

    /// <summary>Creates a new work order in draft.</summary>
    public static Result<WorkOrder> Create(
        Guid organizationId,
        string? title,
        string? description,
        WorkOrderPriority priority,
        Guid? assetId,
        Guid? incidentId,
        DateTimeOffset now,
        Guid? maintenancePlanId = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Result.Failure<WorkOrder>(Error.Validation(
                "WorkOrder.TitleRequired",
                "A title is required."));
        }

        var trimmedTitle = title.Trim();

        if (trimmedTitle.Length > 200)
        {
            return Result.Failure<WorkOrder>(Error.Validation(
                "WorkOrder.TitleTooLong",
                "A title cannot exceed 200 characters."));
        }

        if (description is { Length: > 4000 })
        {
            return Result.Failure<WorkOrder>(Error.Validation(
                "WorkOrder.DescriptionTooLong",
                "A description cannot exceed 4000 characters."));
        }

        var id = Guid.CreateVersion7();

        var workOrder = new WorkOrder(
            id,
            organizationId,
            BuildReference(id, now),
            trimmedTitle,
            description?.Trim(),
            priority,
            assetId,
            incidentId,
            maintenancePlanId);

        workOrder.RaiseDomainEvent(new WorkOrderCreated(
            id, organizationId, workOrder.Reference, priority, assetId, incidentId, maintenancePlanId));

        return Result.Success(workOrder);
    }

    /// <summary>
    /// Assigns or reassigns a technician, scheduling the work if it is still in draft.
    /// </summary>
    /// <remarks>
    /// Reassignment is permitted at any point before completion — a technician calling in sick is
    /// routine, and forcing a dispatcher to cancel and recreate the work order to hand it to
    /// someone else would make an ordinary event needlessly disruptive to the record.
    /// </remarks>
    public Result Assign(Guid userId, DateTimeOffset? scheduledFor = null)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("WorkOrder.InvalidUser", "A technician is required."));
        }

        if (Status is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled)
        {
            return Result.Failure(Error.Conflict(
                "WorkOrder.NotOpen",
                "This work order has been closed and cannot be assigned."));
        }

        var previous = AssignedToUserId;

        AssignedToUserId = userId;
        ScheduledFor = scheduledFor ?? ScheduledFor;

        if (Status == WorkOrderStatus.Draft)
        {
            Status = WorkOrderStatus.Scheduled;
        }

        RaiseDomainEvent(new WorkOrderAssigned(Id, OrganizationId, userId, previous));

        return Result.Success();
    }

    /// <summary>Marks work as underway.</summary>
    public Result Start(Guid startedBy, DateTimeOffset now)
    {
        if (AssignedToUserId is null)
        {
            return Result.Failure(Error.Conflict(
                "WorkOrder.NotAssigned",
                "A work order must be assigned before it can be started."));
        }

        if (Status is not (WorkOrderStatus.Scheduled or WorkOrderStatus.InProgress))
        {
            return Result.Failure(Error.Conflict(
                "WorkOrder.CannotStart",
                "Only a scheduled work order can be started."));
        }

        if (Status == WorkOrderStatus.InProgress)
        {
            return Result.Success();
        }

        Status = WorkOrderStatus.InProgress;
        StartedOnUtc = now;

        RaiseDomainEvent(new WorkOrderStarted(Id, OrganizationId, startedBy));

        return Result.Success();
    }

    /// <summary>
    /// Records that the work is done.
    /// </summary>
    /// <remarks>
    /// Completable from Scheduled directly, without an intervening Start — a five-minute job a
    /// technician finishes on arrival should not need two round trips to close out, and the
    /// completion timestamp is the fact that actually matters for the record.
    /// </remarks>
    public Result Complete(string? notes, Guid completedBy, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return Result.Failure(Error.Conflict(
                "WorkOrder.NotOpen",
                "This work order is not open."));
        }

        if (AssignedToUserId is null)
        {
            return Result.Failure(Error.Conflict(
                "WorkOrder.NotAssigned",
                "A work order must be assigned before it can be completed."));
        }

        Status = WorkOrderStatus.Completed;
        CompletedOnUtc = now;
        CompletionNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        var elapsed = now - (StartedOnUtc ?? CreatedOnUtc);

        RaiseDomainEvent(new WorkOrderCompleted(
            Id, OrganizationId, completedBy, AssetId, IncidentId, MaintenancePlanId, elapsed));

        return Result.Success();
    }

    /// <summary>Withdraws the work order without completing it.</summary>
    public Result Cancel(string? reason)
    {
        if (!IsOpen)
        {
            return Result.Failure(Error.Conflict(
                "WorkOrder.NotOpen",
                "Only an open work order can be cancelled."));
        }

        Status = WorkOrderStatus.Cancelled;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        RaiseDomainEvent(new WorkOrderCancelled(Id, OrganizationId, CancellationReason));

        return Result.Success();
    }

    private static string BuildReference(Guid id, DateTimeOffset now)
    {
        var hex = id.ToString("N");

        return $"WO-{now:yyyy}-{hex[^12..].ToUpperInvariant()}";
    }
}
