using Aegis.Domain.Common;

namespace Aegis.Domain.UnitTests.Common;

public sealed class ValueObjectTests
{
    private sealed class Coordinate : ValueObject
    {
        public Coordinate(double latitude, double longitude)
        {
            DomainException.Require(
                latitude is >= -90 and <= 90,
                "Coordinate.LatitudeOutOfRange",
                "Latitude must be between -90 and 90 degrees.");

            DomainException.Require(
                longitude is >= -180 and <= 180,
                "Coordinate.LongitudeOutOfRange",
                "Longitude must be between -180 and 180 degrees.");

            Latitude = latitude;
            Longitude = longitude;
        }

        public double Latitude { get; }

        public double Longitude { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Latitude;
            yield return Longitude;
        }
    }

    private sealed class Money : ValueObject
    {
        public Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        public decimal Amount { get; }

        public string Currency { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    [Fact]
    public void Value_objects_with_identical_components_should_be_equal()
    {
        var left = new Coordinate(51.5074, -0.1278);
        var right = new Coordinate(51.5074, -0.1278);

        left.ShouldBe(right);
        (left == right).ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void Value_objects_differing_in_any_component_should_not_be_equal()
    {
        new Coordinate(51.5074, -0.1278).ShouldNotBe(new Coordinate(51.5074, -0.1279));
    }

    [Fact]
    public void Value_objects_of_different_types_should_not_be_equal()
    {
        // Structural comparison alone would call these equal, since both reduce to (double, double)
        // or (decimal, string). Type is part of identity.
        new Money(10m, "GBP").Equals(new Coordinate(10, 0)).ShouldBeFalse();
    }

    [Fact]
    public void Component_order_should_be_significant()
    {
        new Coordinate(51.5074, -0.1278).ShouldNotBe(new Coordinate(-0.1278, 51.5074));
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public void Construction_should_reject_coordinates_outside_the_valid_range(double lat, double lon)
    {
        // The whole point of the value object: an instance that exists is an instance that is
        // valid, so no downstream code needs to re-check the range.
        Should.Throw<DomainException>(() => new Coordinate(lat, lon));
    }

    [Fact]
    public void Comparison_against_null_should_be_false_rather_than_throw()
    {
        var coordinate = new Coordinate(0, 0);

        coordinate.Equals(null).ShouldBeFalse();
        (coordinate == null).ShouldBeFalse();
        (null == coordinate).ShouldBeFalse();
    }
}
