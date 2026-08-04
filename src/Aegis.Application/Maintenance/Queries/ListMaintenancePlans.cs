using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Common.Extensions;
using Aegis.Application.Common.Models;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using Aegis.Domain.Maintenance;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.Maintenance.Queries;

/// <summary>A maintenance plan as shown in the maintenance schedule.</summary>
public sealed record MaintenancePlanListItemDto(
    Guid Id,
    string Reference,
    Guid AssetId,
    string Title,
    int FrequencyDays,
    DateTimeOffset NextDueOnUtc,
    DateTimeOffset? LastCompletedOnUtc,
    bool IsActive,
    bool IsDue,
    DateTimeOffset CreatedOnUtc);

/// <summary>Lists maintenance plans in the current organization.</summary>
public sealed record ListMaintenancePlansQuery : PaginatedQuery, IQuery<PagedResult<MaintenancePlanListItemDto>>
{
    /// <summary>Restricts results to plans servicing one asset.</summary>
    public Guid? AssetId { get; init; }

    /// <summary>Restricts results to active plans.</summary>
    public bool ActiveOnly { get; init; }

    /// <summary>Restricts results to plans that are currently due.</summary>
    public bool DueOnly { get; init; }

    /// <summary>Fields this query can be sorted by.</summary>
    public static IReadOnlySet<string> SortableFields { get; } = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        nameof(MaintenancePlanListItemDto.Id),
        nameof(MaintenancePlanListItemDto.Reference),
        nameof(MaintenancePlanListItemDto.NextDueOnUtc),
        nameof(MaintenancePlanListItemDto.LastCompletedOnUtc),
        nameof(MaintenancePlanListItemDto.CreatedOnUtc),
    };
}

/// <summary>Validates <see cref="ListMaintenancePlansQuery"/>.</summary>
public sealed class ListMaintenancePlansQueryValidator : AbstractValidator<ListMaintenancePlansQuery>
{
    /// <summary>Initialises the validator.</summary>
    public ListMaintenancePlansQueryValidator()
    {
        RuleFor(q => q.SortBy)
            .Must(sortBy => sortBy is null || ListMaintenancePlansQuery.SortableFields.Contains(sortBy))
            .WithMessage(_ =>
                "Unknown sort field. Valid values: " +
                string.Join(", ", ListMaintenancePlansQuery.SortableFields.OrderBy(f => f, StringComparer.Ordinal)));

        RuleFor(q => q.SearchTerm).MaximumLength(200);
    }
}

/// <summary>Handles <see cref="ListMaintenancePlansQuery"/>.</summary>
internal sealed class ListMaintenancePlansQueryHandler(IAegisDbContext context, TimeProvider timeProvider)
    : IQueryHandler<ListMaintenancePlansQuery, PagedResult<MaintenancePlanListItemDto>>
{
    /// <inheritdoc />
    public async Task<Result<PagedResult<MaintenancePlanListItemDto>>> Handle(
        ListMaintenancePlansQuery request,
        CancellationToken cancellationToken)
    {
        var search = request.SearchTerm?.Trim();
        var now = timeProvider.GetUtcNow();

        var query = context.Set<MaintenancePlan>()
            .AsNoTracking()
            .WhereIfNotNull(request.AssetId, p => p.AssetId == request.AssetId)
            .WhereIf(request.ActiveOnly, p => p.IsActive)
            .WhereIf(request.DueOnly, p => p.IsActive && p.NextDueOnUtc <= now)
            .WhereIfNotBlank(
                search,
                p => EF.Functions.Like(p.Title, $"%{search}%")
                    || EF.Functions.Like(p.Reference, $"%{search}%"));

        // Soonest due first: a maintenance schedule is read top-down as "what needs doing next."
        var sorted = query
            .ApplySort(request.SortBy, request.SortDirection, p => p.NextDueOnUtc)
            .ThenByDescending(p => p.Id);

        var projected = sorted.Select(p => new MaintenancePlanListItemDto(
            p.Id,
            p.Reference,
            p.AssetId,
            p.Title,
            p.FrequencyDays,
            p.NextDueOnUtc,
            p.LastCompletedOnUtc,
            p.IsActive,
            p.IsActive && p.NextDueOnUtc <= now,
            p.CreatedOnUtc));

        var page = await projected.ToPagedResultAsync(request, cancellationToken);

        return Result.Success(page);
    }
}
