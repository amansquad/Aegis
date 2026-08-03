using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Common.Extensions;
using Aegis.Application.Common.Models;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using Aegis.Domain.Incidents;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.Incidents.Queries;

/// <summary>An incident as shown in the triage queue.</summary>
/// <remarks>
/// The reporter's contact details are deliberately absent. A queue is read on shared screens and
/// in shift handovers, and a member of the public's phone number does not belong on a wall display
/// — it is available on the individual incident to whoever has reason to open it.
/// </remarks>
public sealed record IncidentListItemDto(
    Guid Id,
    string Reference,
    string Summary,
    IncidentCategory Category,
    IncidentSeverity Severity,
    IncidentStatus Status,
    bool PublicSafetyRisk,
    bool RequiresReview,
    ClassificationMethod ClassifiedBy,
    double? Confidence,
    string? LocationHint,
    double? Latitude,
    double? Longitude,
    Guid? AssetId,
    DateTimeOffset ReportedOnUtc,
    DateTimeOffset? ResolvedOnUtc);

/// <summary>Lists incidents in the current organization.</summary>
public sealed record ListIncidentsQuery : PaginatedQuery, IQuery<PagedResult<IncidentListItemDto>>
{
    /// <summary>Restricts results to one lifecycle state.</summary>
    public IncidentStatus? Status { get; init; }

    /// <summary>Restricts results to one category.</summary>
    public IncidentCategory? Category { get; init; }

    /// <summary>Restricts results to one severity.</summary>
    public IncidentSeverity? Severity { get; init; }

    /// <summary>Restricts results to incidents concerning one asset.</summary>
    public Guid? AssetId { get; init; }

    /// <summary>Restricts results to incidents still being handled.</summary>
    public bool OpenOnly { get; init; }

    /// <summary>Restricts results to those a dispatcher has not yet confirmed.</summary>
    public bool AwaitingTriageOnly { get; init; }

    /// <summary>Restricts results to reports describing danger to people.</summary>
    public bool SafetyRiskOnly { get; init; }

    /// <summary>Fields this query can be sorted by.</summary>
    public static IReadOnlySet<string> SortableFields { get; } = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        nameof(IncidentListItemDto.Id),
        nameof(IncidentListItemDto.Reference),
        nameof(IncidentListItemDto.Category),
        nameof(IncidentListItemDto.Severity),
        nameof(IncidentListItemDto.Status),
        nameof(IncidentListItemDto.ReportedOnUtc),
        nameof(IncidentListItemDto.ResolvedOnUtc),
    };
}

/// <summary>Validates <see cref="ListIncidentsQuery"/>.</summary>
public sealed class ListIncidentsQueryValidator : AbstractValidator<ListIncidentsQuery>
{
    /// <summary>Initialises the validator.</summary>
    public ListIncidentsQueryValidator()
    {
        RuleFor(q => q.SortBy)
            .Must(sortBy => sortBy is null || ListIncidentsQuery.SortableFields.Contains(sortBy))
            .WithMessage(_ =>
                "Unknown sort field. Valid values: " +
                string.Join(", ", ListIncidentsQuery.SortableFields.OrderBy(f => f, StringComparer.Ordinal)));

        RuleFor(q => q.SearchTerm).MaximumLength(200);
    }
}

/// <summary>Handles <see cref="ListIncidentsQuery"/>.</summary>
internal sealed class ListIncidentsQueryHandler(IAegisDbContext context)
    : IQueryHandler<ListIncidentsQuery, PagedResult<IncidentListItemDto>>
{
    private static readonly IncidentStatus[] OpenStatuses =
    [
        IncidentStatus.Reported,
        IncidentStatus.Triaged,
        IncidentStatus.InProgress,
    ];

    /// <inheritdoc />
    public async Task<Result<PagedResult<IncidentListItemDto>>> Handle(
        ListIncidentsQuery request,
        CancellationToken cancellationToken)
    {
        var search = request.SearchTerm?.Trim();

        var query = context.Set<Incident>()
            .AsNoTracking()
            .WhereIfNotNull(request.Status, i => i.Status == request.Status)
            .WhereIfNotNull(request.Category, i => i.Category == request.Category)
            .WhereIfNotNull(request.Severity, i => i.Severity == request.Severity)
            .WhereIfNotNull(request.AssetId, i => i.AssetId == request.AssetId)
            .WhereIf(request.OpenOnly, i => OpenStatuses.Contains(i.Status))
            .WhereIf(request.AwaitingTriageOnly, i => i.Status == IncidentStatus.Reported)
            .WhereIf(request.SafetyRiskOnly, i => i.PublicSafetyRisk)
            .WhereIfNotBlank(
                search,
                i => EF.Functions.Like(i.Summary, $"%{search}%")
                    || EF.Functions.Like(i.Reference, $"%{search}%")
                    || EF.Functions.Like(i.ReportText, $"%{search}%"));

        // Newest first by default: a triage queue is read from the top, and the thing that just
        // arrived is the thing most likely to still be happening.
        var sorted = query
            .ApplySort(request.SortBy, request.SortDirection, i => i.ReportedOnUtc)
            .ThenByDescending(i => i.Id);

        var projected = sorted.Select(i => new IncidentListItemDto(
            i.Id,
            i.Reference,
            i.Summary,
            i.Category,
            i.Severity,
            i.Status,
            i.PublicSafetyRisk,
            // Recomputed in SQL rather than calling the aggregate's method, which cannot be
            // translated. The rule is duplicated here and covered by a test that compares the two,
            // so a change to one without the other fails the build rather than silently diverging.
            i.ClassificationMethod != ClassificationMethod.Manual
                && (i.PublicSafetyRisk
                    || i.ClassificationMethod == ClassificationMethod.Heuristic
                    || i.ClassificationConfidence == null
                    || i.ClassificationConfidence < 0.85),
            i.ClassificationMethod,
            i.ClassificationConfidence,
            i.LocationHint,
            i.Location == null ? null : i.Location.Latitude,
            i.Location == null ? null : i.Location.Longitude,
            i.AssetId,
            i.ReportedOnUtc,
            i.ResolvedOnUtc));

        var page = await projected.ToPagedResultAsync(request, cancellationToken);

        return Result.Success(page);
    }
}
