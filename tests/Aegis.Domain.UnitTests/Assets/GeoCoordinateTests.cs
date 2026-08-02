using Aegis.Domain.Assets.ValueObjects;

namespace Aegis.Domain.UnitTests.Assets;

public sealed class GeoCoordinateTests
{
    [Theory]
    [InlineData(51.5074, -0.1278)]
    [InlineData(90, 180)]
    [InlineData(-90, -180)]
    [InlineData(0, 0)]
    public void Accepts_coordinates_within_range(double latitude, double longitude)
    {
        GeoCoordinate.Create(latitude, longitude).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(90.1, 0)]
    [InlineData(-90.1, 0)]
    public void Rejects_latitude_outside_range(double latitude, double longitude)
    {
        GeoCoordinate.Create(latitude, longitude).Error.Code
            .ShouldBe("GeoCoordinate.LatitudeOutOfRange");
    }

    [Theory]
    [InlineData(0, 180.1)]
    [InlineData(0, -180.1)]
    public void Rejects_longitude_outside_range(double latitude, double longitude)
    {
        GeoCoordinate.Create(latitude, longitude).Error.Code
            .ShouldBe("GeoCoordinate.LongitudeOutOfRange");
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    public void Rejects_values_that_are_not_finite(double latitude, double longitude)
    {
        // A NaN latitude passes a naive range check, because every comparison against NaN is false.
        GeoCoordinate.Create(latitude, longitude).Error.Code.ShouldBe("GeoCoordinate.NotFinite");
    }

    [Fact]
    public void A_swapped_pair_outside_latitude_range_is_caught()
    {
        // The commonest coordinate bug there is: this type takes latitude first, GeoJSON and WKT
        // take longitude first. Where the values happen to be within both ranges nothing can catch
        // it, but a longitude beyond 90 lands here.
        var swapped = GeoCoordinate.Create(-122.4194, 37.7749);

        swapped.IsFailure.ShouldBeTrue();
        swapped.Error.Message.ShouldContain("latitude first");
    }

    [Fact]
    public void Coordinates_with_the_same_values_are_equal()
    {
        GeoCoordinate.Create(51.5074, -0.1278).Value
            .ShouldBe(GeoCoordinate.Create(51.5074, -0.1278).Value);
    }

    [Fact]
    public void Distance_between_London_and_Paris_is_about_343_kilometres()
    {
        // A published reference pair. Haversine on a spherical Earth is accurate to roughly 0.5%,
        // so the assertion allows 2% either way rather than pretending to survey precision.
        var london = GeoCoordinate.Create(51.5074, -0.1278).Value;
        var paris = GeoCoordinate.Create(48.8566, 2.3522).Value;

        var metres = london.DistanceInMetresTo(paris);

        metres.ShouldBeInRange(336_000, 350_000);
    }

    [Fact]
    public void Distance_to_the_same_point_is_zero()
    {
        var point = GeoCoordinate.Create(51.5074, -0.1278).Value;

        point.DistanceInMetresTo(GeoCoordinate.Create(51.5074, -0.1278).Value).ShouldBe(0, 0.001);
    }

    [Fact]
    public void Distance_is_symmetric()
    {
        var a = GeoCoordinate.Create(51.5074, -0.1278).Value;
        var b = GeoCoordinate.Create(53.4808, -2.2426).Value;

        a.DistanceInMetresTo(b).ShouldBe(b.DistanceInMetresTo(a), 0.001);
    }
}

public sealed class AssetCodeTests
{
    [Fact]
    public void A_code_is_normalised_to_upper_case()
    {
        // Operators type these by hand, from memory, on a phone in the rain. pmp-nw-0431 and
        // PMP-NW-0431 are the same pump, and a system that disagrees creates a duplicate record.
        AssetCode.Create("  pmp-nw-0431  ").Value.Value.ShouldBe("PMP-NW-0431");
    }

    [Theory]
    [InlineData("PMP-NW-0431")]
    [InlineData("VALVE_12")]
    [InlineData("RD/A1/SEC/004")]
    [InlineData("123456")]
    public void Accepts_codes_operators_actually_use(string candidate)
    {
        AssetCode.Create(candidate).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("PMP NW 0431")]
    [InlineData("PMP#0431")]
    [InlineData("PMP.0431")]
    public void Rejects_characters_that_survive_a_spreadsheet_paste(string candidate)
    {
        AssetCode.Create(candidate).Error.Code.ShouldBe("AssetCode.InvalidCharacters");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_code(string? candidate)
    {
        AssetCode.Create(candidate).Error.Code.ShouldBe("AssetCode.Empty");
    }

    [Fact]
    public void Codes_differing_only_in_case_are_equal()
    {
        AssetCode.Create("pmp-1").Value.ShouldBe(AssetCode.Create("PMP-1").Value);
    }
}
