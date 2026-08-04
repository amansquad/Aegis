using Aegis.Domain.Maintenance;
using Aegis.Domain.Maintenance.Events;

namespace Aegis.Domain.UnitTests.Maintenance;

public sealed class MaintenancePlanTests
{
    private static readonly Guid Organization = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AssetId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);

    private static MaintenancePlan CreatePlan(int frequencyDays = 90, DateTimeOffset? startingOn = null)
    {
        var plan = MaintenancePlan.Create(
            Organization,
            AssetId,
            "Quarterly valve inspection",
            "Check for corrosion and confirm free movement",
            frequencyDays,
            startingOn,
            Now).Value;

        plan.ClearDomainEvents();

        return plan;
    }

    // ---- Creation ----

    [Fact]
    public void A_new_plan_is_active_and_due_immediately_by_default()
    {
        var plan = CreatePlan();

        plan.IsActive.ShouldBeTrue();
        plan.NextDueOnUtc.ShouldBe(Now);
        plan.LastCompletedOnUtc.ShouldBeNull();
    }

    [Fact]
    public void A_starting_date_pushes_the_first_due_date_out()
    {
        var startingOn = Now.AddDays(30);

        var plan = CreatePlan(startingOn: startingOn);

        plan.NextDueOnUtc.ShouldBe(startingOn);
    }

    [Fact]
    public void The_reference_carries_the_year()
    {
        CreatePlan().Reference.ShouldStartWith("MP-2026-");
    }

    [Fact]
    public void References_are_unique_across_plans_created_together()
    {
        var references = Enumerable.Range(0, 200)
            .Select(_ => CreatePlan().Reference)
            .ToArray();

        references.Distinct(StringComparer.Ordinal).Count().ShouldBe(references.Length);
    }

    [Fact]
    public void An_empty_asset_is_rejected()
    {
        MaintenancePlan.Create(Organization, Guid.Empty, "Inspect", null, 90, null, Now)
            .Error.Code.ShouldBe("MaintenancePlan.InvalidAsset");
    }

    [Fact]
    public void A_blank_title_is_rejected()
    {
        MaintenancePlan.Create(Organization, AssetId, "  ", null, 90, null, Now)
            .Error.Code.ShouldBe("MaintenancePlan.TitleRequired");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3651)]
    public void A_frequency_outside_one_to_thirty_six_fifty_days_is_rejected(int frequencyDays)
    {
        MaintenancePlan.Create(Organization, AssetId, "Inspect", null, frequencyDays, null, Now)
            .Error.Code.ShouldBe("MaintenancePlan.InvalidFrequency");
    }

    [Fact]
    public void Creation_raises_an_event_carrying_the_schedule()
    {
        var plan = MaintenancePlan.Create(
            Organization, AssetId, "Inspect", null, 90, null, Now).Value;

        var raised = plan.DomainEvents.OfType<MaintenancePlanCreated>().ShouldHaveSingleItem();
        raised.AssetId.ShouldBe(AssetId);
        raised.FrequencyDays.ShouldBe(90);
        raised.NextDueOnUtc.ShouldBe(Now);
    }

    // ---- Due status ----

    [Fact]
    public void An_active_plan_past_its_due_date_is_due()
    {
        CreatePlan().IsDue(Now.AddDays(1)).ShouldBeTrue();
    }

    [Fact]
    public void An_active_plan_before_its_due_date_is_not_due()
    {
        var plan = CreatePlan(startingOn: Now.AddDays(30));

        plan.IsDue(Now).ShouldBeFalse();
    }

    [Fact]
    public void An_inactive_plan_is_never_due_however_overdue_it_is()
    {
        var plan = CreatePlan();
        plan.Deactivate();

        plan.IsDue(Now.AddYears(1)).ShouldBeFalse();
    }

    // ---- Advancing ----

    [Fact]
    public void Advancing_rolls_the_due_date_forward_from_the_actual_completion_date()
    {
        // The rule this test exists to pin down: a plan completed early or late is due again
        // relative to when the work actually happened, not relative to the old schedule -- so
        // drift never compounds in either direction.
        var plan = CreatePlan(frequencyDays: 90);
        var completedFiveDaysEarly = Now.AddDays(-5);

        plan.Advance(completedFiveDaysEarly);

        plan.LastCompletedOnUtc.ShouldBe(completedFiveDaysEarly);
        plan.NextDueOnUtc.ShouldBe(completedFiveDaysEarly.AddDays(90));
    }

    [Fact]
    public void Advancing_raises_an_event_with_the_new_due_date()
    {
        var plan = CreatePlan(frequencyDays: 30);

        plan.Advance(Now);

        var raised = plan.DomainEvents.OfType<MaintenancePlanAdvanced>().ShouldHaveSingleItem();
        raised.AssetId.ShouldBe(AssetId);
        raised.CompletedOnUtc.ShouldBe(Now);
        raised.NextDueOnUtc.ShouldBe(Now.AddDays(30));
    }

    // ---- Activation ----

    [Fact]
    public void Deactivating_takes_the_plan_out_of_rotation()
    {
        var plan = CreatePlan();

        plan.Deactivate().IsSuccess.ShouldBeTrue();

        plan.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Deactivating_an_already_inactive_plan_is_rejected()
    {
        var plan = CreatePlan();
        plan.Deactivate();

        plan.Deactivate().Error.Code.ShouldBe("MaintenancePlan.AlreadyInactive");
    }

    [Fact]
    public void Reactivating_puts_the_plan_back_into_rotation()
    {
        var plan = CreatePlan();
        plan.Deactivate();

        plan.Reactivate().IsSuccess.ShouldBeTrue();

        plan.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Reactivating_an_already_active_plan_is_rejected()
    {
        CreatePlan().Reactivate().Error.Code.ShouldBe("MaintenancePlan.AlreadyActive");
    }
}
