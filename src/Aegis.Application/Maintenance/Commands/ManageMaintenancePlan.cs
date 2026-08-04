using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using Aegis.Domain.Maintenance;
using Aegis.Domain.WorkOrders;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.Maintenance.Commands;

/// <summary>Takes a plan out of rotation.</summary>
/// <param name="MaintenancePlanId">The plan to deactivate.</param>
public sealed record DeactivateMaintenancePlanCommand(Guid MaintenancePlanId) : ICommand;

/// <summary>Handles <see cref="DeactivateMaintenancePlanCommand"/>.</summary>
internal sealed class DeactivateMaintenancePlanCommandHandler(IAegisDbContext context)
    : ICommandHandler<DeactivateMaintenancePlanCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(DeactivateMaintenancePlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await context.Set<MaintenancePlan>()
            .SingleOrDefaultAsync(p => p.Id == request.MaintenancePlanId, cancellationToken);

        if (plan is null)
        {
            return Result.Failure(MaintenancePlanErrors.NotFound);
        }

        var deactivated = plan.Deactivate();

        if (deactivated.IsFailure)
        {
            return deactivated;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Puts a deactivated plan back into rotation.</summary>
/// <param name="MaintenancePlanId">The plan to reactivate.</param>
public sealed record ReactivateMaintenancePlanCommand(Guid MaintenancePlanId) : ICommand;

/// <summary>Handles <see cref="ReactivateMaintenancePlanCommand"/>.</summary>
internal sealed class ReactivateMaintenancePlanCommandHandler(IAegisDbContext context)
    : ICommandHandler<ReactivateMaintenancePlanCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(ReactivateMaintenancePlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await context.Set<MaintenancePlan>()
            .SingleOrDefaultAsync(p => p.Id == request.MaintenancePlanId, cancellationToken);

        if (plan is null)
        {
            return Result.Failure(MaintenancePlanErrors.NotFound);
        }

        var reactivated = plan.Reactivate();

        if (reactivated.IsFailure)
        {
            return reactivated;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Generates a work order from a plan, dispatching the next occurrence of its scheduled work.
/// </summary>
/// <param name="MaintenancePlanId">The plan to generate from.</param>
/// <param name="Priority">
/// How urgently this occurrence needs doing — a dispatch decision, not copied from the plan, for
/// the same reason a work order's priority is never copied from an incident's severity.
/// </param>
public sealed record GenerateWorkOrderFromPlanCommand(
    Guid MaintenancePlanId,
    WorkOrderPriority Priority) : ICommand<Guid>;

/// <summary>Validates <see cref="GenerateWorkOrderFromPlanCommand"/>.</summary>
public sealed class GenerateWorkOrderFromPlanCommandValidator
    : AbstractValidator<GenerateWorkOrderFromPlanCommand>
{
    /// <summary>Initialises the validator.</summary>
    public GenerateWorkOrderFromPlanCommandValidator()
    {
        RuleFor(c => c.MaintenancePlanId).NotEmpty();
        RuleFor(c => c.Priority).IsInEnum();
    }
}

/// <summary>Handles <see cref="GenerateWorkOrderFromPlanCommand"/>.</summary>
/// <remarks>
/// Generation is a deliberate, manual action rather than a background job triggered the moment a
/// plan becomes due — this codebase has no job runner yet, and bolting a scheduler on purely to
/// automate this one step would be a larger commitment than the feature warrants on its own. A
/// dispatcher can generate a plan's work order any time, due or not; the queue simply surfaces due
/// plans first so this is rarely more than a click on the plan that most needs it.
/// </remarks>
internal sealed class GenerateWorkOrderFromPlanCommandHandler(
    IAegisDbContext context,
    TimeProvider timeProvider) : ICommandHandler<GenerateWorkOrderFromPlanCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(
        GenerateWorkOrderFromPlanCommand request,
        CancellationToken cancellationToken)
    {
        var plan = await context.Set<MaintenancePlan>()
            .SingleOrDefaultAsync(p => p.Id == request.MaintenancePlanId, cancellationToken);

        if (plan is null)
        {
            return Result.Failure<Guid>(MaintenancePlanErrors.NotFound);
        }

        if (!plan.IsActive)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "MaintenancePlan.NotActive",
                "An inactive plan cannot generate work."));
        }

        // Guards against the double-click: without this, generating twice in a row before the
        // first work order is even assigned would dispatch the same occurrence of the same job
        // twice, and nothing about completing one would tell a technician the other still exists.
        var openStatuses = new[] { WorkOrderStatus.Draft, WorkOrderStatus.Scheduled, WorkOrderStatus.InProgress };

        var alreadyOpen = await context.Set<WorkOrder>()
            .AnyAsync(
                w => w.MaintenancePlanId == plan.Id && openStatuses.Contains(w.Status),
                cancellationToken);

        if (alreadyOpen)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "MaintenancePlan.WorkOrderAlreadyOpen",
                "This plan already has an open work order."));
        }

        var workOrder = WorkOrder.Create(
            plan.OrganizationId,
            plan.Title,
            plan.Description,
            request.Priority,
            plan.AssetId,
            incidentId: null,
            timeProvider.GetUtcNow(),
            maintenancePlanId: plan.Id);

        if (workOrder.IsFailure)
        {
            return Result.Failure<Guid>(workOrder.Error);
        }

        context.Set<WorkOrder>().Add(workOrder.Value);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(workOrder.Value.Id);
    }
}
