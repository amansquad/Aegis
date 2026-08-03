using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using Aegis.Domain.Incidents;
using Aegis.Domain.WorkOrders;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.WorkOrders.Commands;

/// <summary>Errors shared by the work order commands.</summary>
internal static class WorkOrderErrors
{
    /// <summary>The work order does not exist, or belongs to another organization.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "WorkOrder.NotFound",
        "The work order was not found.");
}

/// <summary>Dispatches a new work order.</summary>
/// <param name="Title">Short description of the work.</param>
/// <param name="Description">Fuller detail for the assigned technician.</param>
/// <param name="Priority">How urgently this needs doing.</param>
/// <param name="AssetId">The asset this concerns, if any.</param>
/// <param name="IncidentId">The incident this resolves, if it originated from one.</param>
public sealed record CreateWorkOrderCommand(
    string Title,
    string? Description,
    WorkOrderPriority Priority,
    Guid? AssetId,
    Guid? IncidentId) : ICommand<Guid>;

/// <summary>Validates <see cref="CreateWorkOrderCommand"/>.</summary>
public sealed class CreateWorkOrderCommandValidator : AbstractValidator<CreateWorkOrderCommand>
{
    /// <summary>Initialises the validator.</summary>
    public CreateWorkOrderCommandValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(4000);
        RuleFor(c => c.Priority).IsInEnum();
    }
}

/// <summary>Handles <see cref="CreateWorkOrderCommand"/>.</summary>
internal sealed class CreateWorkOrderCommandHandler(
    IAegisDbContext context,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : ICommandHandler<CreateWorkOrderCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(CreateWorkOrderCommand request, CancellationToken cancellationToken)
    {
        if (request.AssetId is { } assetId)
        {
            var assetExists = await context.Assets.AnyAsync(a => a.Id == assetId, cancellationToken);

            if (!assetExists)
            {
                return Result.Failure<Guid>(Error.NotFound(
                    "Asset.NotFound",
                    "The asset was not found in this organization."));
            }
        }

        Incident? incident = null;

        if (request.IncidentId is { } incidentId)
        {
            incident = await context.Set<Incident>()
                .SingleOrDefaultAsync(i => i.Id == incidentId, cancellationToken);

            if (incident is null)
            {
                return Result.Failure<Guid>(Error.NotFound(
                    "Incident.NotFound",
                    "The incident was not found in this organization."));
            }
        }

        var workOrder = WorkOrder.Create(
            tenantContext.RequireOrganizationId(),
            request.Title,
            request.Description,
            request.Priority,
            // An asset id supplied directly is trusted (it was just checked above); one inherited
            // from the incident is only ever what that incident already resolved through its own
            // tenant-scoped lookup, so this never lets an unrelated asset in through the back door.
            request.AssetId ?? incident?.AssetId,
            request.IncidentId,
            timeProvider.GetUtcNow());

        if (workOrder.IsFailure)
        {
            return Result.Failure<Guid>(workOrder.Error);
        }

        context.Set<WorkOrder>().Add(workOrder.Value);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(workOrder.Value.Id);
    }
}
