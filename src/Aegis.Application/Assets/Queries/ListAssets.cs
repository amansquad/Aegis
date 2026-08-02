using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Common.Extensions;
using Aegis.Application.Common.Models;
using Aegis.Application.Messaging;
using Aegis.Domain.Assets;
using Aegis.Domain.Assets.ValueObjects;
using Aegis.Domain.Common;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.Assets.Queries;

/// <summary>An asset as shown in a list or on a map.</summary>
public sealed record AssetListItemDto(
    Guid Id,
    string Code,
    string Name,
    AssetType Type,
    AssetStatus Status,
    AssetCondition Condition,
    AssetCriticality Criticality,
    double? Latitude,
    double? Longitude,
    Guid? ParentAssetId,
    DateOnly? InstalledOn,
    DateTimeOffset? LastInspectedOnUtc,
    DateTimeOffset CreatedOnUtc);

/// <summary>Lists assets in the current organization.</summary>
public sealed record ListAssetsQuery : PaginatedQuery, IQuery<PagedResult<AssetListItemDto>>
{
    /// <summary>Restricts results to one kind of infrastructure.</summary>
    public AssetType? Type { get; init; }

    /// <summary>Restricts results to one operational status.</summary>
    public AssetStatus? Status { get; init; }

    /// <summary>Restricts results to one assessed condition.</summary>
    public AssetCondition? Condition { get; init; }

    /// <summary>Restricts results to one criticality band.</summary>
    public AssetCriticality? Criticality { get; init; }

    /// <summary>Restricts results to the direct children of an asset.</summary>
    public Guid? ParentAssetId { get; init; }

    /// <summary>Latitude of a point to search around.</summary>
    public double? NearLatitude { get; init; }

    /// <summary>Longitude of a point to search around.</summary>
    public double? NearLongitude { get; init; }

    /// <summary>
    /// Radius in metres for a proximity search.
    /// </summary>
    /// <remarks>
    /// The query that makes the map and the incident intake useful: "what is near where this was
    /// reported?". Executed in SQL Server against the geography column so the spatial index does
    /// the work, rather than loading an estate and measuring in memory.
    /// </remarks>
    public double? WithinMetres { get; init; }

    /// <summary>Excludes decommissioned assets, which is usually what an operator wants.</summary>
    public bool ExcludeDecommissioned { get; init; }

    /// <summary>Fields this query can be sorted by.</summary>
    /// <remarks>
    /// Explicit rather than reflected from the DTO, because sorting is applied to the entity before
    /// projection. <c>Code</c> is absent: it is a value object behind a converter, so member access
    /// on it is not translatable.
    /// </remarks>
    public static IReadOnlySet<string> SortableFields { get; } = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        nameof(AssetListItemDto.Id),
        nameof(AssetListItemDto.Name),
        nameof(AssetListItemDto.Type),
        nameof(AssetListItemDto.Status),
        nameof(AssetListItemDto.Condition),
        nameof(AssetListItemDto.Criticality),
        nameof(AssetListItemDto.InstalledOn),
        nameof(AssetListItemDto.LastInspectedOnUtc),
        nameof(AssetListItemDto.CreatedOnUtc),
    };
}

/// <summary>Validates <see cref="ListAssetsQuery"/>.</summary>
public sealed class ListAssetsQueryValidator : AbstractValidator<ListAssetsQuery>
{
    /// <summary>Largest radius a proximity search may request, in metres.</summary>
    /// <remarks>
    /// 200 km. Beyond that the query stops being "near here" and becomes a full scan wearing a
    /// spatial predicate, which is slower than simply listing everything.
    /// </remarks>
    public const double MaxSearchRadiusMetres = 200_000;

    /// <summary>Initialises the validator.</summary>
    public ListAssetsQueryValidator()
    {
        RuleFor(q => q.SortBy)
            .Must(sortBy => sortBy is null || ListAssetsQuery.SortableFields.Contains(sortBy))
            .WithMessage(_ =>
                "Unknown sort field. Valid values: " +
                string.Join(", ", ListAssetsQuery.SortableFields.OrderBy(f => f, StringComparer.Ordinal)));

        RuleFor(q => q.SearchTerm).MaximumLength(200);

        // A proximity search needs all three parts. Two of them describe a point with no radius,
        // or a radius around nowhere.
        RuleFor(q => q.WithinMetres)
            .NotNull()
            .When(q => q.NearLatitude is not null || q.NearLongitude is not null)
            .WithMessage("A search radius is required when searching near a point.");

        RuleFor(q => q.NearLatitude)
            .NotNull()
            .When(q => q.WithinMetres is not null)
            .WithMessage("A latitude is required for a proximity search.");

        RuleFor(q => q.NearLongitude)
            .NotNull()
            .When(q => q.WithinMetres is not null)
            .WithMessage("A longitude is required for a proximity search.");

        RuleFor(q => q.WithinMetres)
            .InclusiveBetween(1, MaxSearchRadiusMetres)
            .When(q => q.WithinMetres is not null);

        RuleFor(q => q.NearLatitude)
            .InclusiveBetween(-90, 90)
            .When(q => q.NearLatitude is not null);

        RuleFor(q => q.NearLongitude)
            .InclusiveBetween(-180, 180)
            .When(q => q.NearLongitude is not null);
    }
}

/// <summary>Handles <see cref="ListAssetsQuery"/>.</summary>
internal sealed class ListAssetsQueryHandler(IAegisDbContext context)
    : IQueryHandler<ListAssetsQuery, PagedResult<AssetListItemDto>>
{
    /// <inheritdoc />
    public async Task<Result<PagedResult<AssetListItemDto>>> Handle(
        ListAssetsQuery request,
        CancellationToken cancellationToken)
    {
        var search = request.SearchTerm?.Trim();

        var query = context.Assets
            .AsNoTracking()
            .WhereIfNotNull(request.Type, a => a.Type == request.Type)
            .WhereIfNotNull(request.Status, a => a.Status == request.Status)
            .WhereIfNotNull(request.Condition, a => a.Condition == request.Condition)
            .WhereIfNotNull(request.Criticality, a => a.Criticality == request.Criticality)
            .WhereIfNotNull(request.ParentAssetId, a => a.ParentAssetId == request.ParentAssetId)
            .WhereIf(
                request.ExcludeDecommissioned,
                a => a.Status != AssetStatus.Decommissioned);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            var exactCode = AssetCode.Create(search);

            // Name matches partially; code matches exactly, because Code is a value object behind a
            // converter and EF cannot translate member access on it. In practice an operator
            // searching by code types the whole code, so the limitation costs little — but it is a
            // limitation, not a design choice, and is recorded as such.
            query = exactCode.IsSuccess
                ? query.Where(a =>
                    EF.Functions.Like(a.Name, pattern) || a.Code == exactCode.Value)
                : query.Where(a => EF.Functions.Like(a.Name, pattern));
        }

        if (request.NearLatitude is { } latitude
            && request.NearLongitude is { } longitude
            && request.WithinMetres is { } radius)
        {
            var origin = GeoCoordinate.Create(latitude, longitude);

            if (origin.IsFailure)
            {
                return Result.Failure<PagedResult<AssetListItemDto>>(origin.Error);
            }

            // Two stages, both executed in SQL.
            //
            // First a bounding box, which the (Latitude, Longitude) index can serve — a cheap
            // range scan that discards almost everything. Then an exact great-circle predicate
            // over what survives, so the result is a true circle rather than the square the box
            // describes. Running only the box would quietly include assets up to 41% further away
            // than requested at the corners.
            //
            // The Haversine expression is written in terms of Math.Sin, Math.Cos and Math.Atan2,
            // all of which the SQL Server provider translates, so no rows are materialised to be
            // filtered in memory.
            //
            // Assets with no recorded position are excluded rather than treated as distance zero,
            // which would put every unsurveyed asset at the top of every proximity search.
            const double earthRadiusMetres = 6_371_000;
            const double degreesPerMetreLatitude = 1.0 / 111_320.0;

            var latitudeDelta = radius * degreesPerMetreLatitude;

            // Longitude degrees shrink towards the poles, so the box must widen by 1/cos(latitude).
            // Clamped because that factor diverges at the poles and would produce an infinite box.
            var cosLatitude = Math.Max(Math.Cos(double.DegreesToRadians(latitude)), 0.01);
            var longitudeDelta = latitudeDelta / cosLatitude;

            var minLatitude = latitude - latitudeDelta;
            var maxLatitude = latitude + latitudeDelta;
            var minLongitude = longitude - longitudeDelta;
            var maxLongitude = longitude + longitudeDelta;

            var originLatitudeRadians = double.DegreesToRadians(latitude);

            query = query.Where(a =>
                a.Location != null
                && a.Location.Latitude >= minLatitude
                && a.Location.Latitude <= maxLatitude
                && a.Location.Longitude >= minLongitude
                && a.Location.Longitude <= maxLongitude
                && earthRadiusMetres * 2 * Math.Atan2(
                    Math.Sqrt(
                        (Math.Sin((a.Location.Latitude - latitude) * Math.PI / 360)
                            * Math.Sin((a.Location.Latitude - latitude) * Math.PI / 360))
                        + (Math.Cos(originLatitudeRadians)
                            * Math.Cos(a.Location.Latitude * Math.PI / 180)
                            * Math.Sin((a.Location.Longitude - longitude) * Math.PI / 360)
                            * Math.Sin((a.Location.Longitude - longitude) * Math.PI / 360))),
                    Math.Sqrt(
                        1 - ((Math.Sin((a.Location.Latitude - latitude) * Math.PI / 360)
                            * Math.Sin((a.Location.Latitude - latitude) * Math.PI / 360))
                        + (Math.Cos(originLatitudeRadians)
                            * Math.Cos(a.Location.Latitude * Math.PI / 180)
                            * Math.Sin((a.Location.Longitude - longitude) * Math.PI / 360)
                            * Math.Sin((a.Location.Longitude - longitude) * Math.PI / 360))))) <= radius);
        }

        var sorted = query
            .ApplySort(request.SortBy, request.SortDirection, a => a.CreatedOnUtc)
            .ThenByDescending(a => a.Id);

        // Code is projected whole rather than as a.Code.Value: it is mapped through a value
        // converter, so member access on it is not translatable. The string is taken after
        // materialisation.
        var projected = sorted.Select(a => new AssetRow(
            a.Id,
            a.Code,
            a.Name,
            a.Type,
            a.Status,
            a.Condition,
            a.Criticality,
            a.Location == null ? null : a.Location.Latitude,
            a.Location == null ? null : a.Location.Longitude,
            a.ParentAssetId,
            a.InstalledOn,
            a.LastInspectedOnUtc,
            a.CreatedOnUtc));

        var page = await projected.ToPagedResultAsync(request, cancellationToken);

        var items = page.Items
            .Select(a => new AssetListItemDto(
                a.Id,
                a.Code.Value,
                a.Name,
                a.Type,
                a.Status,
                a.Condition,
                a.Criticality,
                a.Latitude,
                a.Longitude,
                a.ParentAssetId,
                a.InstalledOn,
                a.LastInspectedOnUtc,
                a.CreatedOnUtc))
            .ToArray();

        return Result.Success(
            new PagedResult<AssetListItemDto>(items, page.Page, page.PageSize, page.TotalCount));
    }

    /// <summary>The shape read from the database, before the code value object is unwrapped.</summary>
    private sealed record AssetRow(
        Guid Id,
        AssetCode Code,
        string Name,
        AssetType Type,
        AssetStatus Status,
        AssetCondition Condition,
        AssetCriticality Criticality,
        double? Latitude,
        double? Longitude,
        Guid? ParentAssetId,
        DateOnly? InstalledOn,
        DateTimeOffset? LastInspectedOnUtc,
        DateTimeOffset CreatedOnUtc);
}
