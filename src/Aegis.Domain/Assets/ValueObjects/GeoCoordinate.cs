using Aegis.Domain.Common;

namespace Aegis.Domain.Assets.ValueObjects;

/// <summary>
/// A point on the Earth's surface, in WGS 84 decimal degrees.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not a NetTopologySuite <c>Point</c>.</b> Aegis.Domain has no package references,
/// and geometry libraries are exactly the kind of dependency that creeps inward: a
/// <c>Point</c> here would put NetTopologySuite in the business model, which would then be
/// unbuildable without it and awkward to unit test. Infrastructure converts this to a
/// <c>Point</c> with SRID 4326 at the mapping boundary, where spatial indexing and distance
/// queries actually happen.
/// </para>
/// <para>
/// The domain's interest in a location is that it is valid, comparable, and expressible — not that
/// it participates in an R-tree. Keeping those concerns apart is what lets an asset's position be
/// reasoned about in a test with no database and no spatial library present.
/// </para>
/// <para>
/// Ordering matters and is a classic source of silent bugs: this type takes latitude first, while
/// GeoJSON, WKT and most mapping APIs take longitude first. Naming both parameters explicitly is
/// the only reliable defence, and swapping them puts an asset in the wrong hemisphere rather than
/// failing loudly.
/// </para>
/// </remarks>
public sealed class GeoCoordinate : ValueObject
{
    /// <summary>The reference system these coordinates are expressed in.</summary>
    /// <remarks>
    /// 4326 is WGS 84, what GPS reports and what web maps expect. Recorded explicitly because a
    /// coordinate without a reference system is not a location, and mixing 4326 with a projected
    /// system such as 3857 silently misplaces everything by a wide margin.
    /// </remarks>
    public const int Wgs84SpatialReferenceId = 4326;

    private GeoCoordinate(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Degrees north of the equator, between -90 and 90.</summary>
    public double Latitude { get; }

    /// <summary>Degrees east of the prime meridian, between -180 and 180.</summary>
    public double Longitude { get; }

    /// <summary>Creates a coordinate, rejecting values outside the valid range.</summary>
    public static Result<GeoCoordinate> Create(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || double.IsNaN(longitude)
            || double.IsInfinity(latitude) || double.IsInfinity(longitude))
        {
            return Result.Failure<GeoCoordinate>(Error.Validation(
                "GeoCoordinate.NotFinite",
                "Latitude and longitude must be finite numbers."));
        }

        // The hint belongs on the latitude message rather than the longitude one. A caller who
        // supplies the pair in GeoJSON or WKT order passes a longitude where latitude is expected,
        // and any longitude beyond 90 degrees trips this check first — so this is where someone
        // debugging a swapped pair actually ends up.
        if (latitude is < -90 or > 90)
        {
            return Result.Failure<GeoCoordinate>(Error.Validation(
                "GeoCoordinate.LatitudeOutOfRange",
                "Latitude must be between -90 and 90 degrees. Note that this type takes " +
                "latitude first, unlike GeoJSON and WKT."));
        }

        if (longitude is < -180 or > 180)
        {
            return Result.Failure<GeoCoordinate>(Error.Validation(
                "GeoCoordinate.LongitudeOutOfRange",
                "Longitude must be between -180 and 180 degrees."));
        }

        return Result.Success(new GeoCoordinate(latitude, longitude));
    }

    /// <summary>Rehydrates a coordinate already validated on the way in.</summary>
    /// <remarks>For EF Core materialisation only.</remarks>
    public static GeoCoordinate FromTrustedSource(double latitude, double longitude) =>
        new(latitude, longitude);

    /// <summary>
    /// Great-circle distance to another coordinate, in metres.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Haversine on a spherical Earth. Accurate to roughly 0.5%, which over a 10 km service area is
    /// about 50 m — fine for "is this report near that hydrant?" and not fine for survey work.
    /// </para>
    /// <para>
    /// Present so the domain can reason about proximity in a unit test without a database. Real
    /// spatial queries — nearest asset, assets within a polygon — run in SQL Server against the
    /// geography column and its spatial index, because filtering thousands of assets in memory to
    /// find the nearest one is the wrong shape of work regardless of the formula's accuracy.
    /// </para>
    /// </remarks>
    public double DistanceInMetresTo(GeoCoordinate other)
    {
        ArgumentNullException.ThrowIfNull(other);

        const double earthRadiusMetres = 6_371_000;

        var lat1 = double.DegreesToRadians(Latitude);
        var lat2 = double.DegreesToRadians(other.Latitude);
        var deltaLat = double.DegreesToRadians(other.Latitude - Latitude);
        var deltaLon = double.DegreesToRadians(other.Longitude - Longitude);

        var a = (Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2))
            + (Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2));

        return earthRadiusMetres * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Latitude:F6}, {Longitude:F6}");
}
