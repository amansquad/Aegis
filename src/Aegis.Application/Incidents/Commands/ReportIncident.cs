using Aegis.Application.Abstractions.Ai;
using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Messaging;
using Aegis.Domain.Assets.ValueObjects;
using Aegis.Domain.Common;
using Aegis.Domain.Incidents;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aegis.Application.Incidents.Commands;

/// <summary>The outcome of submitting a report.</summary>
/// <param name="IncidentId">The created incident.</param>
/// <param name="Reference">The reference to quote back to the reporter.</param>
/// <param name="Category">The classified category.</param>
/// <param name="Severity">The assessed severity.</param>
/// <param name="Summary">The operational summary.</param>
/// <param name="RequiresReview">True when a dispatcher must confirm before this is acted on.</param>
/// <param name="ClassifiedBy">How the classification was arrived at.</param>
/// <param name="Confidence">Extractor confidence, when a classifier was used.</param>
/// <param name="MatchedAssetCode">The asset the incident was linked to, if one was resolved.</param>
/// <param name="PossibleDuplicateOf">
/// A recent nearby incident of the same category, if one exists. Surfaced rather than acted on.
/// </param>
public sealed record ReportIncidentResult(
    Guid IncidentId,
    string Reference,
    IncidentCategory Category,
    IncidentSeverity Severity,
    string Summary,
    bool RequiresReview,
    ClassificationMethod ClassifiedBy,
    double? Confidence,
    string? MatchedAssetCode,
    string? PossibleDuplicateOf);

/// <summary>Submits a free-text problem report.</summary>
/// <param name="ReportText">The reporter's own words.</param>
/// <param name="Latitude">Where the problem is, when the reporter's device supplied it.</param>
/// <param name="Longitude">Where the problem is, when the reporter's device supplied it.</param>
/// <param name="ReporterName">Optional. Anonymous reports are accepted.</param>
/// <param name="ReporterContact">Optional phone or email for a call back.</param>
public sealed record ReportIncidentCommand(
    string ReportText,
    double? Latitude,
    double? Longitude,
    string? ReporterName,
    string? ReporterContact) : ICommand<ReportIncidentResult>;

/// <summary>Validates <see cref="ReportIncidentCommand"/>.</summary>
public sealed class ReportIncidentCommandValidator : AbstractValidator<ReportIncidentCommand>
{
    /// <summary>Initialises the validator.</summary>
    public ReportIncidentCommandValidator()
    {
        RuleFor(c => c.ReportText)
            .NotEmpty().WithMessage("Describe the problem before submitting.")
            .MinimumLength(10).WithMessage("Please describe the problem in a little more detail.")
            .MaximumLength(8000);

        RuleFor(c => c.Longitude)
            .NotNull()
            .When(c => c.Latitude is not null)
            .WithMessage("A longitude is required when a latitude is supplied.");

        RuleFor(c => c.Latitude)
            .NotNull()
            .When(c => c.Longitude is not null)
            .WithMessage("A latitude is required when a longitude is supplied.");

        RuleFor(c => c.Latitude).InclusiveBetween(-90, 90).When(c => c.Latitude is not null);
        RuleFor(c => c.Longitude).InclusiveBetween(-180, 180).When(c => c.Longitude is not null);

        RuleFor(c => c.ReporterName).MaximumLength(200);
        RuleFor(c => c.ReporterContact).MaximumLength(200);
    }
}

/// <summary>Handles <see cref="ReportIncidentCommand"/>.</summary>
/// <remarks>
/// The order of work here is the design. Extraction is attempted first and is allowed to fail;
/// asset resolution and duplicate detection then run against our own data, never against anything
/// the model asserted. An incident is always created, because losing a member of the public's
/// report because a language model was unavailable would be an indefensible failure mode.
/// </remarks>
internal sealed class ReportIncidentCommandHandler(
    IAegisDbContext context,
    ITenantContext tenantContext,
    IIncidentExtractor extractor,
    TimeProvider timeProvider,
    ILogger<ReportIncidentCommandHandler> logger)
    : ICommandHandler<ReportIncidentCommand, ReportIncidentResult>
{
    /// <summary>How far from a report to look for the asset it concerns.</summary>
    /// <remarks>
    /// 150 m. Wide enough to cover the usual gap between a phone's fix and the buried main it is
    /// standing over; narrow enough that it does not sweep in the next street's hydrant.
    /// </remarks>
    private const double AssetSearchRadiusMetres = 150;

    /// <summary>How long a nearby incident of the same category counts as a possible duplicate.</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromHours(12);

    /// <summary>How far apart two reports can be and still describe the same problem.</summary>
    private const double DuplicateRadiusMetres = 250;

    /// <inheritdoc />
    public async Task<Result<ReportIncidentResult>> Handle(
        ReportIncidentCommand request,
        CancellationToken cancellationToken)
    {
        var organizationId = tenantContext.RequireOrganizationId();
        var now = timeProvider.GetUtcNow();

        GeoCoordinate? location = null;

        if (request.Latitude is { } latitude && request.Longitude is { } longitude)
        {
            var coordinate = GeoCoordinate.Create(latitude, longitude);

            if (coordinate.IsFailure)
            {
                return Result.Failure<ReportIncidentResult>(coordinate.Error);
            }

            location = coordinate.Value;
        }

        // Extraction is best-effort. A model outage must not lose a report, so a failure falls
        // through to an unclassified incident that a dispatcher will triage by hand — which is
        // exactly what would have happened before any of this existed.
        var extraction = await extractor.ExtractAsync(request.ReportText, cancellationToken);

        var proposal = extraction.IsSuccess ? extraction.Value : null;

        if (extraction.IsFailure)
        {
            logger.LogWarning(
                "Falling back to manual classification: {ErrorCode} {ErrorMessage}",
                extraction.Error.Code,
                extraction.Error.Message);
        }

        var incident = Incident.Report(
            organizationId,
            request.ReportText,
            proposal?.Summary,
            proposal?.Category ?? IncidentCategory.Other,
            proposal?.Severity ?? IncidentSeverity.Moderate,
            proposal?.Method ?? ClassificationMethod.Manual,
            proposal?.Confidence,
            proposal?.PublicSafetyRisk ?? false,
            proposal?.LocationHint,
            now);

        if (incident.IsFailure)
        {
            return Result.Failure<ReportIncidentResult>(incident.Error);
        }

        incident.Value.RecordReporter(request.ReporterName, request.ReporterContact);

        if (location is not null)
        {
            incident.Value.SetLocation(location);
        }

        var matchedAsset = await ResolveAssetAsync(proposal, location, cancellationToken);

        if (matchedAsset is not null)
        {
            incident.Value.LinkToAsset(matchedAsset.Id);
        }

        var duplicate = await FindPossibleDuplicateAsync(
            incident.Value.Category,
            location,
            now,
            cancellationToken);

        context.Set<Incident>().Add(incident.Value);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new ReportIncidentResult(
            incident.Value.Id,
            incident.Value.Reference,
            incident.Value.Category,
            incident.Value.Severity,
            incident.Value.Summary,
            incident.Value.RequiresReview(),
            incident.Value.ClassificationMethod,
            incident.Value.ClassificationConfidence,
            matchedAsset?.Code.Value,
            duplicate));
    }

    /// <summary>
    /// Resolves the asset an incident concerns, from our own data.
    /// </summary>
    /// <remarks>
    /// A quoted asset code is tried first and is only ever a lookup — a code belonging to another
    /// organization, or invented outright, simply fails to match because the query is
    /// tenant-scoped. Position is the fallback, using the same proximity predicate the registry
    /// uses. Nothing the model returned is treated as an identity.
    /// </remarks>
    private async Task<Domain.Assets.Asset?> ResolveAssetAsync(
        ExtractedIncident? proposal,
        GeoCoordinate? location,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(proposal?.AssetCodeHint))
        {
            var code = AssetCode.Create(proposal.AssetCodeHint);

            if (code.IsSuccess)
            {
                var quoted = code.Value;

                var byCode = await context.Assets
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Code == quoted, cancellationToken);

                if (byCode is not null)
                {
                    return byCode;
                }
            }
        }

        if (location is null)
        {
            return null;
        }

        // Bounding box first so the index does the work, then exact distance. Same two-stage shape
        // as the registry's proximity search, and for the same reason.
        var latitudeDelta = AssetSearchRadiusMetres / 111_320.0;
        var cosLatitude = Math.Max(Math.Cos(double.DegreesToRadians(location.Latitude)), 0.01);
        var longitudeDelta = latitudeDelta / cosLatitude;

        var minLatitude = location.Latitude - latitudeDelta;
        var maxLatitude = location.Latitude + latitudeDelta;
        var minLongitude = location.Longitude - longitudeDelta;
        var maxLongitude = location.Longitude + longitudeDelta;

        var candidates = await context.Assets
            .AsNoTracking()
            .Where(a => a.Status != Domain.Assets.AssetStatus.Decommissioned)
            .Where(a =>
                a.Location != null
                && a.Location.Latitude >= minLatitude
                && a.Location.Latitude <= maxLatitude
                && a.Location.Longitude >= minLongitude
                && a.Location.Longitude <= maxLongitude)
            .Take(50)
            .ToListAsync(cancellationToken);

        // Exact distance in memory over at most fifty rows the index already narrowed. Doing the
        // final ordering here rather than in SQL keeps the query simple, and fifty haversines is
        // not work worth optimising.
        return candidates
            .Select(a => new { Asset = a, Distance = location.DistanceInMetresTo(a.Location!) })
            .Where(x => x.Distance <= AssetSearchRadiusMetres)
            .OrderBy(x => x.Distance)
            .Select(x => x.Asset)
            .FirstOrDefault();
    }

    /// <summary>
    /// Looks for a recent nearby incident of the same category.
    /// </summary>
    /// <remarks>
    /// <b>Surfaced, never acted on.</b> One burst main generates dozens of calls, and merging them
    /// automatically would be wrong often enough to matter: two leaks on the same street in the
    /// same hour is unusual but entirely possible, and auto-closing the second would lose a real
    /// problem. A dispatcher decides; this only puts the candidate in front of them.
    /// </remarks>
    private async Task<string?> FindPossibleDuplicateAsync(
        IncidentCategory category,
        GeoCoordinate? location,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (location is null)
        {
            return null;
        }

        var since = now - DuplicateWindow;

        var latitudeDelta = DuplicateRadiusMetres / 111_320.0;
        var cosLatitude = Math.Max(Math.Cos(double.DegreesToRadians(location.Latitude)), 0.01);
        var longitudeDelta = latitudeDelta / cosLatitude;

        var candidates = await context.Set<Incident>()
            .AsNoTracking()
            .Where(i => i.Category == category)
            .Where(i => i.ReportedOnUtc >= since)
            .Where(i => i.Status != IncidentStatus.Duplicate && i.Status != IncidentStatus.Rejected)
            .Where(i =>
                i.Location != null
                && i.Location.Latitude >= location.Latitude - latitudeDelta
                && i.Location.Latitude <= location.Latitude + latitudeDelta
                && i.Location.Longitude >= location.Longitude - longitudeDelta
                && i.Location.Longitude <= location.Longitude + longitudeDelta)
            .OrderByDescending(i => i.ReportedOnUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(i => location.DistanceInMetresTo(i.Location!) <= DuplicateRadiusMetres)
            .Select(i => i.Reference)
            .FirstOrDefault();
    }
}
