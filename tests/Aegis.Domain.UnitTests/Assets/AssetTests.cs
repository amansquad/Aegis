using Aegis.Domain.Assets;
using Aegis.Domain.Assets.Events;
using Aegis.Domain.Assets.ValueObjects;

namespace Aegis.Domain.UnitTests.Assets;

public sealed class AssetTests
{
    private static readonly Guid Organization = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Inspector = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private static Asset Register(string code = "PMP-NW-0431")
    {
        var asset = Asset.Register(
            Organization,
            AssetCode.Create(code).Value,
            "Northgate Pumping Station - Pump 2",
            AssetType.Pump).Value;

        asset.ClearDomainEvents();

        return asset;
    }

    [Fact]
    public void A_new_asset_starts_planned_with_unknown_condition()
    {
        var asset = Asset.Register(
            Organization,
            AssetCode.Create("PMP-1").Value,
            "Pump 1",
            AssetType.Pump).Value;

        asset.Status.ShouldBe(AssetStatus.Planned);
        asset.Condition.ShouldBe(AssetCondition.Unknown);
        asset.Criticality.ShouldBe(AssetCriticality.Medium);
        asset.DomainEvents.OfType<AssetRegistered>().ShouldHaveSingleItem();
    }

    [Fact]
    public void An_asset_may_be_registered_without_a_position()
    {
        // Registries are populated from decades of paper records. An asset nobody ever surveyed is
        // a real state, and inventing a coordinate would be worse than admitting the gap.
        Register().Location.ShouldBeNull();
    }

    // ---- Inspections ----

    [Fact]
    public void Recording_an_inspection_updates_the_assessed_condition()
    {
        var asset = Register();

        var result = asset.RecordInspection(AssetCondition.Poor, Now, Inspector, "Corrosion", Now);

        result.IsSuccess.ShouldBeTrue();
        asset.Condition.ShouldBe(AssetCondition.Poor);
        asset.LastInspectedOnUtc.ShouldBe(Now);
        asset.Inspections.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_condition_change_raises_an_event_carrying_criticality()
    {
        // Criticality travels on the event so a subscriber can decide urgency without loading the
        // asset: Poor on a Critical main is an emergency, Poor on a Low spur is a work item.
        var asset = Register();
        asset.UpdateDetails("Pump 2", AssetCriticality.Critical, null, null, null, null, null, null, Now);
        asset.ClearDomainEvents();

        asset.RecordInspection(AssetCondition.VeryPoor, Now, Inspector, null, Now);

        var raised = asset.DomainEvents.OfType<AssetConditionChanged>().ShouldHaveSingleItem();
        raised.PreviousCondition.ShouldBe(AssetCondition.Unknown);
        raised.CurrentCondition.ShouldBe(AssetCondition.VeryPoor);
        raised.Criticality.ShouldBe(AssetCriticality.Critical);
    }

    [Fact]
    public void An_inspection_that_confirms_the_condition_raises_no_condition_change()
    {
        var asset = Register();
        asset.RecordInspection(AssetCondition.Good, Now, Inspector, null, Now);
        asset.ClearDomainEvents();

        asset.RecordInspection(AssetCondition.Good, Now.AddDays(30), Inspector, null, Now.AddDays(30));

        asset.DomainEvents.OfType<AssetConditionChanged>().ShouldBeEmpty();
        asset.DomainEvents.OfType<AssetInspected>().ShouldHaveSingleItem();
    }

    [Fact]
    public void A_backdated_inspection_is_stored_but_does_not_overwrite_a_newer_assessment()
    {
        // This is what makes offline sync safe. A technician's tablet uploads yesterday's
        // inspection today; it must not undo an assessment made in the meantime.
        var asset = Register();
        asset.RecordInspection(AssetCondition.Poor, Now, Inspector, "Current", Now);

        asset.RecordInspection(AssetCondition.Good, Now.AddDays(-30), Inspector, "Old paper record", Now);

        asset.Condition.ShouldBe(AssetCondition.Poor);
        asset.LastInspectedOnUtc.ShouldBe(Now);
        asset.Inspections.Count.ShouldBe(2);
    }

    [Fact]
    public void A_future_dated_inspection_is_rejected()
    {
        var result = Register().RecordInspection(
            AssetCondition.Good,
            Now.AddDays(1),
            Inspector,
            null,
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Inspection.FutureDated");
    }

    [Fact]
    public void A_small_clock_skew_is_tolerated()
    {
        // Field devices drift. Rejecting an inspection because a tablet is ninety seconds fast
        // would block real work for no benefit.
        Register()
            .RecordInspection(AssetCondition.Good, Now.AddMinutes(2), Inspector, null, Now)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void An_inspection_must_record_an_actual_condition()
    {
        Register()
            .RecordInspection(AssetCondition.Unknown, Now, Inspector, null, Now)
            .Error.Code.ShouldBe("Inspection.ConditionRequired");
    }

    // ---- Lifecycle ----

    [Fact]
    public void A_decommissioned_asset_cannot_return_to_service()
    {
        var asset = Register();
        asset.Decommission(Now, "Replaced");

        asset.ChangeStatus(AssetStatus.Operational).Error.Code.ShouldBe("Asset.Decommissioned");
        asset.RecordInspection(AssetCondition.Good, Now, Inspector, null, Now)
            .Error.Code.ShouldBe("Asset.Decommissioned");
        asset.Relocate(GeoCoordinate.Create(51.5, -0.12).Value)
            .Error.Code.ShouldBe("Asset.Decommissioned");
    }

    [Fact]
    public void Decommissioning_twice_is_rejected()
    {
        var asset = Register();
        asset.Decommission(Now, null);

        asset.Decommission(Now, null).Error.Code.ShouldBe("Asset.AlreadyDecommissioned");
    }

    [Fact]
    public void Status_cannot_be_set_to_decommissioned_directly()
    {
        // Retirement carries a timestamp and an event, so it must go through the dedicated
        // operation rather than a status assignment that would skip both.
        Register().ChangeStatus(AssetStatus.Decommissioned).Error.Code.ShouldBe("Asset.UseDecommission");
    }

    [Fact]
    public void An_asset_cannot_be_its_own_parent()
    {
        var asset = Register();

        asset.Reparent(asset.Id).Error.Code.ShouldBe("Asset.SelfParent");
    }

    [Fact]
    public void Relocating_to_the_same_position_raises_no_event()
    {
        // Raising one would invalidate map caches and re-run proximity matching for a change that
        // did not happen.
        var asset = Register();
        var position = GeoCoordinate.Create(51.5074, -0.1278).Value;
        asset.Relocate(position);
        asset.ClearDomainEvents();

        asset.Relocate(GeoCoordinate.Create(51.5074, -0.1278).Value);

        asset.DomainEvents.ShouldBeEmpty();
    }

    // ---- Lifespan arithmetic ----

    [Fact]
    public void Life_consumed_is_uncapped_so_overdue_assets_are_visible()
    {
        // Clamping at 1.0 would hide exactly the assets a replacement planner needs to find.
        var asset = Register();
        asset.UpdateDetails(
            "Pump 2",
            AssetCriticality.High,
            null, null, null,
            new DateOnly(1990, 1, 1),
            expectedLifespanYears: 20,
            null,
            Now);

        var consumed = asset.LifeConsumed(Now);

        consumed.ShouldNotBeNull();
        consumed.Value.ShouldBeGreaterThan(1.5);
    }

    [Fact]
    public void Life_consumed_is_unknown_without_both_inputs()
    {
        var asset = Register();
        asset.UpdateDetails("Pump 2", AssetCriticality.Low, null, null, null, null, null, null, Now);

        asset.AgeInYears(Now).ShouldBeNull();
        asset.LifeConsumed(Now).ShouldBeNull();
    }

    [Fact]
    public void An_in_service_asset_cannot_have_a_future_installation_date()
    {
        var asset = Register();
        asset.ChangeStatus(AssetStatus.Operational);

        var result = asset.UpdateDetails(
            "Pump 2",
            AssetCriticality.Medium,
            null, null, null,
            DateOnly.FromDateTime(Now.UtcDateTime).AddDays(30),
            null, null,
            Now);

        result.Error.Code.ShouldBe("Asset.FutureInstallationDate");
    }

    [Fact]
    public void A_planned_asset_may_have_a_future_installation_date()
    {
        // Which is the whole point of the Planned status.
        Register().UpdateDetails(
            "Pump 2",
            AssetCriticality.Medium,
            null, null, null,
            DateOnly.FromDateTime(Now.UtcDateTime).AddDays(30),
            null, null,
            Now).IsSuccess.ShouldBeTrue();
    }
}
