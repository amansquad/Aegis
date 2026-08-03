using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Common.Extensions;
using Aegis.Application.Common.Models;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using Aegis.Domain.WorkOrders;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.WorkOrders.Queries;

/// <summary>A work order as shown in the dispatch board.</summary>
public sealed record WorkOrderListItemDto(
    Guid Id,
    string Reference,
    string Title,
    WorkOrderStatus Status,
    WorkOrderPriority Priority,
    Guid? AssetId,
    Guid? IncidentId,
    Guid? AssignedToUserId,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? StartedOnUtc,
    DateTimeOffset? CompletedOnUtc,
    DateTimeOffset CreatedOnUtc);

/// <summary>Lists work orders in the current organization.</summary>
public sealed record ListWorkOrdersQuery : PaginatedQuery, IQuery<PagedResult<WorkOrderListItemDto>>
{
    /// <summary>Restricts results to one lifecycle state.</summary>
    public WorkOrderStatus? Status { get; init; }

    /// <summary>Restricts results to one priority.</summary>
    public WorkOrderPriority? Priority { get; init; }

    /// <summary>Restricts results to work concerning one asset.</summary>
    public Guid? AssetId { get; init; }

    /// <summary>Restricts results to work originating from one incident.</summary>
    public Guid? IncidentId { get; init; }

    /// <summary>Restricts results to one technician's assignments.</summary>
    public Guid? AssignedToUserId { get; init; }

    /// <summary>Restricts results to work orders still active in some form.</summary>
    public bool OpenOnly { get; init; }

    /// <summary>Restricts results to draft work orders awaiting a technician.</summary>
    public bool UnassignedOnly { get; init; }

    /// <summary>Fields this query can be sorted by.</summary>
    public static IReadOnlySet<string> SortableFields { get; } = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        nameof(WorkOrderListItemDto.Id),
        nameof(WorkOrderListItemDto.Reference),
        nameof(WorkOrderListItemDto.Status),
        nameof(WorkOrderListItemDto.Priority),
        nameof(WorkOrderListItemDto.ScheduledFor),
        nameof(WorkOrderListItemDto.CreatedOnUtc),
        nameof(WorkOrderListItemDto.CompletedOnUtc),
    };
}

/// <summary>Validates <see cref="ListWorkOrdersQuery"/>.</summary>
public sealed class ListWorkOrdersQueryValidator : AbstractValidator<ListWorkOrdersQuery>
{
    /// <summary>Initialises the validator.</summary>
    public ListWorkOrdersQueryValidator()
    {
        RuleFor(q => q.SortBy)
            .Must(sortBy => sortBy is null || ListWorkOrdersQuery.SortableFields.Contains(sortBy))
            .WithMessage(_ =>
                "Unknown sort field. Valid values: " +
                string.Join(", ", ListWorkOrdersQuery.SortableFields.OrderBy(f => f, StringComparer.Ordinal)));

        RuleFor(q => q.SearchTerm).MaximumLength(200);
    }
}

/// <summary>Handles <see cref="ListWorkOrdersQuery"/>.</summary>
internal sealed class ListWorkOrdersQueryHandler(IAegisDbContext context)
    : IQueryHandler<ListWorkOrdersQuery, PagedResult<WorkOrderListItemDto>>
{
    private static readonly WorkOrderStatus[] OpenStatuses =
    [
        WorkOrderStatus.Draft,
        WorkOrderStatus.Scheduled,
        WorkOrderStatus.InProgress,
    ];

    /// <inheritdoc />
    public async Task<Result<PagedResult<WorkOrderListItemDto>>> Handle(
        ListWorkOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var search = request.SearchTerm?.Trim();

        var query = context.Set<WorkOrder>()
            .AsNoTracking()
            .WhereIfNotNull(request.Status, w => w.Status == request.Status)
            .WhereIfNotNull(request.Priority, w => w.Priority == request.Priority)
            .WhereIfNotNull(request.AssetId, w => w.AssetId == request.AssetId)
            .WhereIfNotNull(request.IncidentId, w => w.IncidentId == request.IncidentId)
            .WhereIfNotNull(request.AssignedToUserId, w => w.AssignedToUserId == request.AssignedToUserId)
            .WhereIf(request.OpenOnly, w => OpenStatuses.Contains(w.Status))
            .WhereIf(request.UnassignedOnly, w => w.Status == WorkOrderStatus.Draft)
            .WhereIfNotBlank(
                search,
                w => EF.Functions.Like(w.Title, $"%{search}%")
                    || EF.Functions.Like(w.Reference, $"%{search}%"));

        // Newest first: a dispatch board is read from the top, and what was just created is what
        // most needs a technician assigned to it.
        var sorted = query
            .ApplySort(request.SortBy, request.SortDirection, w => w.CreatedOnUtc)
            .ThenByDescending(w => w.Id);

        var projected = sorted.Select(w => new WorkOrderListItemDto(
            w.Id,
            w.Reference,
            w.Title,
            w.Status,
            w.Priority,
            w.AssetId,
            w.IncidentId,
            w.AssignedToUserId,
            w.ScheduledFor,
            w.StartedOnUtc,
            w.CompletedOnUtc,
            w.CreatedOnUtc));

        var page = await projected.ToPagedResultAsync(request, cancellationToken);

        return Result.Success(page);
    }
}
