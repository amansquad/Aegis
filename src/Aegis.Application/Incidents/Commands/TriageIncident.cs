using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Abstractions.Security;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using Aegis.Domain.Incidents;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.Incidents.Commands;

/// <summary>Errors shared by the incident commands.</summary>
internal static class IncidentErrors
{
    /// <summary>The incident does not exist, or belongs to another organization.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "Incident.NotFound",
        "The incident was not found.");
}

/// <summary>Confirms or corrects an incident's classification.</summary>
/// <param name="IncidentId">The incident to triage.</param>
/// <param name="Category">The confirmed category.</param>
/// <param name="Severity">The confirmed severity.</param>
/// <param name="Summary">An optional corrected summary.</param>
/// <param name="AssetId">Optionally link the incident to an asset at the same time.</param>
public sealed record TriageIncidentCommand(
    Guid IncidentId,
    IncidentCategory Category,
    IncidentSeverity Severity,
    string? Summary,
    Guid? AssetId) : ICommand;

/// <summary>Validates <see cref="TriageIncidentCommand"/>.</summary>
public sealed class TriageIncidentCommandValidator : AbstractValidator<TriageIncidentCommand>
{
    /// <summary>Initialises the validator.</summary>
    public TriageIncidentCommandValidator()
    {
        RuleFor(c => c.IncidentId).NotEmpty();
        RuleFor(c => c.Category).IsInEnum();
        RuleFor(c => c.Severity).IsInEnum();
        RuleFor(c => c.Summary).MaximumLength(500);
    }
}

/// <summary>Handles <see cref="TriageIncidentCommand"/>.</summary>
internal sealed class TriageIncidentCommandHandler(
    IAegisDbContext context,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<TriageIncidentCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(TriageIncidentCommand request, CancellationToken cancellationToken)
    {
        var incident = await context.Set<Incident>()
            .SingleOrDefaultAsync(i => i.Id == request.IncidentId, cancellationToken);

        if (incident is null)
        {
            return Result.Failure(IncidentErrors.NotFound);
        }

        if (request.AssetId is { } assetId)
        {
            // Checked against assets visible to this tenant, so a dispatcher cannot attach another
            // organization's asset by guessing its identifier.
            var assetExists = await context.Assets.AnyAsync(a => a.Id == assetId, cancellationToken);

            if (!assetExists)
            {
                return Result.Failure(Error.NotFound(
                    "Asset.NotFound",
                    "The asset was not found in this organization."));
            }

            incident.LinkToAsset(assetId);
        }

        var triaged = incident.Triage(
            request.Category,
            request.Severity,
            request.Summary,
            currentUser.Id ?? Guid.Empty,
            timeProvider.GetUtcNow());

        if (triaged.IsFailure)
        {
            return triaged;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Records that the underlying problem has been fixed.</summary>
/// <param name="IncidentId">The incident to resolve.</param>
/// <param name="Notes">What was done about it.</param>
public sealed record ResolveIncidentCommand(Guid IncidentId, string? Notes) : ICommand;

/// <summary>Handles <see cref="ResolveIncidentCommand"/>.</summary>
internal sealed class ResolveIncidentCommandHandler(
    IAegisDbContext context,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<ResolveIncidentCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(ResolveIncidentCommand request, CancellationToken cancellationToken)
    {
        var incident = await context.Set<Incident>()
            .SingleOrDefaultAsync(i => i.Id == request.IncidentId, cancellationToken);

        if (incident is null)
        {
            return Result.Failure(IncidentErrors.NotFound);
        }

        var resolved = incident.Resolve(
            request.Notes,
            currentUser.Id ?? Guid.Empty,
            timeProvider.GetUtcNow());

        if (resolved.IsFailure)
        {
            return resolved;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Closes an incident as the same problem as another.</summary>
/// <param name="IncidentId">The duplicate.</param>
/// <param name="OriginalIncidentId">The incident it duplicates.</param>
public sealed record MarkDuplicateCommand(Guid IncidentId, Guid OriginalIncidentId) : ICommand;

/// <summary>Handles <see cref="MarkDuplicateCommand"/>.</summary>
internal sealed class MarkDuplicateCommandHandler(IAegisDbContext context)
    : ICommandHandler<MarkDuplicateCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(MarkDuplicateCommand request, CancellationToken cancellationToken)
    {
        var incident = await context.Set<Incident>()
            .SingleOrDefaultAsync(i => i.Id == request.IncidentId, cancellationToken);

        if (incident is null)
        {
            return Result.Failure(IncidentErrors.NotFound);
        }

        // The original must exist and be visible to this tenant. Without the check a dispatcher
        // could point a duplicate at an identifier from another organization, producing a link
        // that no query in this tenant could ever follow.
        var originalExists = await context.Set<Incident>()
            .AnyAsync(i => i.Id == request.OriginalIncidentId, cancellationToken);

        if (!originalExists)
        {
            return Result.Failure(Error.NotFound(
                "Incident.OriginalNotFound",
                "The incident this duplicates was not found."));
        }

        var marked = incident.MarkDuplicateOf(request.OriginalIncidentId);

        if (marked.IsFailure)
        {
            return marked;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
