using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using Aegis.Domain.Maintenance;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.Maintenance.Commands;

/// <summary>Errors shared by the maintenance plan commands.</summary>
internal static class MaintenancePlanErrors
{
    /// <summary>The plan does not exist, or belongs to another organization.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "MaintenancePlan.NotFound",
        "The maintenance plan was not found.");
}

/// <summary>Creates a recurring maintenance schedule for an asset.</summary>
/// <param name="AssetId">The asset this plan services.</param>
/// <param name="Title">Short description of the work.</param>
/// <param name="Description">Fuller detail for whoever carries out the work.</param>
/// <param name="FrequencyDays">How often the work recurs, in days.</param>
/// <param name="StartingOn">When the plan is first due. Due immediately if omitted.</param>
public sealed record CreateMaintenancePlanCommand(
    Guid AssetId,
    string Title,
    string? Description,
    int FrequencyDays,
    DateTimeOffset? StartingOn) : ICommand<Guid>;

/// <summary>Validates <see cref="CreateMaintenancePlanCommand"/>.</summary>
public sealed class CreateMaintenancePlanCommandValidator : AbstractValidator<CreateMaintenancePlanCommand>
{
    /// <summary>Initialises the validator.</summary>
    public CreateMaintenancePlanCommandValidator()
    {
        RuleFor(c => c.AssetId).NotEmpty();
        RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(4000);
        RuleFor(c => c.FrequencyDays).InclusiveBetween(1, 3650);
    }
}

/// <summary>Handles <see cref="CreateMaintenancePlanCommand"/>.</summary>
internal sealed class CreateMaintenancePlanCommandHandler(
    IAegisDbContext context,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : ICommandHandler<CreateMaintenancePlanCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(CreateMaintenancePlanCommand request, CancellationToken cancellationToken)
    {
        var assetExists = await context.Assets.AnyAsync(a => a.Id == request.AssetId, cancellationToken);

        if (!assetExists)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "Asset.NotFound",
                "The asset was not found in this organization."));
        }

        var plan = MaintenancePlan.Create(
            tenantContext.RequireOrganizationId(),
            request.AssetId,
            request.Title,
            request.Description,
            request.FrequencyDays,
            request.StartingOn,
            timeProvider.GetUtcNow());

        if (plan.IsFailure)
        {
            return Result.Failure<Guid>(plan.Error);
        }

        context.Set<MaintenancePlan>().Add(plan.Value);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(plan.Value.Id);
    }
}
