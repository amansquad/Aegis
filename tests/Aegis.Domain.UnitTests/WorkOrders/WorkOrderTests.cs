using Aegis.Domain.WorkOrders;
using Aegis.Domain.WorkOrders.Events;

namespace Aegis.Domain.UnitTests.WorkOrders;

public sealed class WorkOrderTests
{
    private static readonly Guid Organization = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Technician = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Dispatcher = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AssetId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid IncidentId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);

    private static WorkOrder CreateDraft(Guid? assetId = null, Guid? incidentId = null)
    {
        var workOrder = WorkOrder.Create(
            Organization,
            "Replace failed isolation valve",
            "Reported leaking at the junction",
            WorkOrderPriority.High,
            assetId,
            incidentId,
            Now).Value;

        // CreatedOnUtc is normally stamped by the persistence interceptor on save, not by the
        // domain constructor. Set here to stand in for that, since these tests never touch EF Core.
        workOrder.CreatedOnUtc = Now;

        workOrder.ClearDomainEvents();

        return workOrder;
    }

    private static WorkOrder CreateAssigned()
    {
        var workOrder = CreateDraft(AssetId, IncidentId);
        workOrder.Assign(Technician);
        workOrder.ClearDomainEvents();

        return workOrder;
    }

    // ---- Creation ----

    [Fact]
    public void A_new_work_order_starts_in_draft()
    {
        var workOrder = CreateDraft();

        workOrder.Status.ShouldBe(WorkOrderStatus.Draft);
        workOrder.IsOpen.ShouldBeTrue();
        workOrder.AssignedToUserId.ShouldBeNull();
    }

    [Fact]
    public void The_reference_is_quotable_and_carries_the_year()
    {
        CreateDraft().Reference.ShouldStartWith("WO-2026-");
    }

    [Fact]
    public void References_are_unique_across_work_orders_created_together()
    {
        // The same bug class the incident reference had: taking the timestamp head of a UUIDv7
        // rather than its random tail would make every work order created in the same millisecond
        // collide. Guarded here the same way, by actually creating many in a tight loop.
        var references = Enumerable.Range(0, 200)
            .Select(_ => CreateDraft().Reference)
            .ToArray();

        references.Distinct(StringComparer.Ordinal).Count().ShouldBe(references.Length);
    }

    [Fact]
    public void A_blank_title_is_rejected()
    {
        WorkOrder.Create(Organization, "  ", null, WorkOrderPriority.Low, null, null, Now)
            .Error.Code.ShouldBe("WorkOrder.TitleRequired");
    }

    [Fact]
    public void Creation_raises_an_event_carrying_the_linked_asset_and_incident()
    {
        var workOrder = WorkOrder.Create(
            Organization, "Fix it", null, WorkOrderPriority.Critical, AssetId, IncidentId, Now).Value;

        var raised = workOrder.DomainEvents.OfType<WorkOrderCreated>().ShouldHaveSingleItem();
        raised.AssetId.ShouldBe(AssetId);
        raised.IncidentId.ShouldBe(IncidentId);
        raised.Priority.ShouldBe(WorkOrderPriority.Critical);
    }

    // ---- Assignment ----

    [Fact]
    public void Assigning_a_draft_work_order_schedules_it()
    {
        var workOrder = CreateDraft();

        workOrder.Assign(Technician).IsSuccess.ShouldBeTrue();

        workOrder.Status.ShouldBe(WorkOrderStatus.Scheduled);
        workOrder.AssignedToUserId.ShouldBe(Technician);
    }

    [Fact]
    public void An_empty_user_id_is_rejected()
    {
        CreateDraft().Assign(Guid.Empty).Error.Code.ShouldBe("WorkOrder.InvalidUser");
    }

    [Fact]
    public void Reassignment_is_permitted_and_reported_against_the_previous_assignee()
    {
        // A technician calling in sick is routine. Forcing a cancel-and-recreate to hand the work
        // to someone else would make an ordinary event needlessly disruptive to the record.
        var workOrder = CreateAssigned();
        var replacement = Guid.CreateVersion7();

        var result = workOrder.Assign(replacement);

        result.IsSuccess.ShouldBeTrue();
        workOrder.AssignedToUserId.ShouldBe(replacement);

        var raised = workOrder.DomainEvents.OfType<WorkOrderAssigned>().ShouldHaveSingleItem();
        raised.PreviouslyAssignedToUserId.ShouldBe(Technician);
        raised.AssignedToUserId.ShouldBe(replacement);
    }

    [Fact]
    public void A_closed_work_order_cannot_be_assigned()
    {
        var workOrder = CreateAssigned();
        workOrder.Complete(null, Dispatcher, Now);

        workOrder.Assign(Guid.CreateVersion7()).Error.Code.ShouldBe("WorkOrder.NotOpen");
    }

    // ---- Starting ----

    [Fact]
    public void An_unassigned_work_order_cannot_be_started()
    {
        CreateDraft().Start(Technician, Now).Error.Code.ShouldBe("WorkOrder.NotAssigned");
    }

    [Fact]
    public void Starting_records_who_and_when()
    {
        var workOrder = CreateAssigned();

        workOrder.Start(Technician, Now).IsSuccess.ShouldBeTrue();

        workOrder.Status.ShouldBe(WorkOrderStatus.InProgress);
        workOrder.StartedOnUtc.ShouldBe(Now);
    }

    [Fact]
    public void Starting_an_already_started_work_order_is_a_harmless_no_op()
    {
        var workOrder = CreateAssigned();
        workOrder.Start(Technician, Now);
        workOrder.ClearDomainEvents();

        var result = workOrder.Start(Technician, Now.AddHours(1));

        result.IsSuccess.ShouldBeTrue();
        workOrder.StartedOnUtc.ShouldBe(Now); // Unchanged by the second call.
        workOrder.DomainEvents.ShouldBeEmpty();
    }

    // ---- Completion ----

    [Fact]
    public void An_unassigned_work_order_cannot_be_completed()
    {
        CreateDraft().Complete(null, Dispatcher, Now).Error.Code.ShouldBe("WorkOrder.NotAssigned");
    }

    [Fact]
    public void Completion_is_possible_directly_from_scheduled_without_an_intervening_start()
    {
        // A five-minute job a technician finishes on arrival should not need two round trips to
        // close out. The completion timestamp is the fact that matters for the record.
        var workOrder = CreateAssigned();

        workOrder.Complete("Fixed on arrival", Dispatcher, Now).IsSuccess.ShouldBeTrue();

        workOrder.Status.ShouldBe(WorkOrderStatus.Completed);
        workOrder.CompletionNotes.ShouldBe("Fixed on arrival");
    }

    [Fact]
    public void Completion_measures_elapsed_time_from_when_work_actually_started()
    {
        var workOrder = CreateAssigned();
        workOrder.Start(Technician, Now);
        workOrder.ClearDomainEvents();

        workOrder.Complete(null, Dispatcher, Now.AddHours(3));

        var raised = workOrder.DomainEvents.OfType<WorkOrderCompleted>().ShouldHaveSingleItem();
        raised.TimeToComplete.ShouldBe(TimeSpan.FromHours(3));
        raised.AssetId.ShouldBe(AssetId);
        raised.IncidentId.ShouldBe(IncidentId);
    }

    [Fact]
    public void Completion_without_a_start_measures_from_creation()
    {
        var workOrder = CreateAssigned();

        workOrder.Complete(null, Dispatcher, Now.AddHours(1));

        workOrder.DomainEvents.OfType<WorkOrderCompleted>()
            .ShouldHaveSingleItem().TimeToComplete.ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public void A_completed_work_order_cannot_be_completed_again()
    {
        var workOrder = CreateAssigned();
        workOrder.Complete(null, Dispatcher, Now);

        workOrder.Complete(null, Dispatcher, Now.AddHours(1)).Error.Code.ShouldBe("WorkOrder.NotOpen");
    }

    // ---- Cancellation ----

    [Fact]
    public void Cancelling_records_the_reason()
    {
        var workOrder = CreateDraft();

        workOrder.Cancel("Duplicate dispatch").IsSuccess.ShouldBeTrue();

        workOrder.Status.ShouldBe(WorkOrderStatus.Cancelled);
        workOrder.CancellationReason.ShouldBe("Duplicate dispatch");
        workOrder.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void A_completed_work_order_cannot_be_cancelled()
    {
        var workOrder = CreateAssigned();
        workOrder.Complete(null, Dispatcher, Now);

        workOrder.Cancel(null).Error.Code.ShouldBe("WorkOrder.NotOpen");
    }

    [Fact]
    public void An_in_progress_work_order_can_still_be_cancelled()
    {
        var workOrder = CreateAssigned();
        workOrder.Start(Technician, Now);

        workOrder.Cancel("Job no longer needed").IsSuccess.ShouldBeTrue();
        workOrder.Status.ShouldBe(WorkOrderStatus.Cancelled);
    }
}
