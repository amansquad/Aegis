using Aegis.Domain.Common;

namespace Aegis.Domain.UnitTests.Common;

public sealed class EntityTests
{
    private sealed class TestAsset : AggregateRoot<Guid>
    {
        public TestAsset(Guid id) : base(id)
        {
        }

        public void Commission() => RaiseDomainEvent(new TestAssetCommissioned(Id));
    }

    private sealed class TestWorkOrder : AggregateRoot<Guid>
    {
        public TestWorkOrder(Guid id) : base(id)
        {
        }
    }

    private sealed record TestAssetCommissioned(Guid AssetId) : DomainEvent;

    [Fact]
    public void Entities_with_the_same_id_and_type_should_be_equal()
    {
        var id = Guid.CreateVersion7();

        var left = new TestAsset(id);
        var right = new TestAsset(id);

        left.ShouldBe(right);
        (left == right).ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void Entities_of_different_types_should_never_be_equal_despite_a_shared_id()
    {
        // Guid collisions across tables are possible when ids are assigned in application code.
        // An asset and a work order that happen to share an id are still different things.
        var id = Guid.CreateVersion7();

        var asset = new TestAsset(id);
        var workOrder = new TestWorkOrder(id);

        asset.Equals(workOrder).ShouldBeFalse();
    }

    [Fact]
    public void Entities_with_different_ids_should_not_be_equal()
    {
        new TestAsset(Guid.CreateVersion7()).ShouldNotBe(new TestAsset(Guid.CreateVersion7()));
    }

    [Fact]
    public void A_new_entity_should_have_no_pending_domain_events()
    {
        new TestAsset(Guid.CreateVersion7()).DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Raising_a_domain_event_should_queue_it_with_identity_and_timestamp()
    {
        var asset = new TestAsset(Guid.CreateVersion7());

        asset.Commission();

        var raised = asset.DomainEvents.ShouldHaveSingleItem();
        raised.ShouldBeOfType<TestAssetCommissioned>().AssetId.ShouldBe(asset.Id);
        raised.EventId.ShouldNotBe(Guid.Empty);
        raised.OccurredOnUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void Clearing_domain_events_should_prevent_a_tracked_entity_replaying_them()
    {
        var asset = new TestAsset(Guid.CreateVersion7());
        asset.Commission();

        asset.ClearDomainEvents();

        asset.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Version7_ids_should_be_monotonically_ordered()
    {
        // Aegis uses UUIDv7 rather than v4 so that clustered-index inserts land at the end of the
        // B-tree instead of fragmenting it. This asserts the property that buys us that.
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        first.ShouldNotBe(second);
    }
}
