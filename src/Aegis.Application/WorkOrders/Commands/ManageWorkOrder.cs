using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Abstractions.Security;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using Aegis.Domain.Incidents;
using Aegis.Domain.Maintenance;
using Aegis.Domain.WorkOrders;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.WorkOrders.Commands;

/// <summary>Assigns or reassigns a technician.</summary>
/// <param name="WorkOrderId">The work order to assign.</param>
/// <param name="UserId">The technician responsible.</param>
/// <param name="ScheduledFor">When the work is planned, if known.</param>
public sealed record AssignWorkOrderCommand(
    Guid WorkOrderId,
    Guid UserId,
    DateTimeOffset? ScheduledFor) : ICommand;

/// <summary>Validates <see cref="AssignWorkOrderCommand"/>.</summary>
public sealed class AssignWorkOrderCommandValidator : AbstractValidator<AssignWorkOrderCommand>
{
    /// <summary>Initialises the validator.</summary>
    public AssignWorkOrderCommandValidator()
    {
        RuleFor(c => c.WorkOrderId).NotEmpty();
        RuleFor(c => c.UserId).NotEmpty();
    }
}

/// <summary>Handles <see cref="AssignWorkOrderCommand"/>.</summary>
internal sealed class AssignWorkOrderCommandHandler(IAegisDbContext context)
    : ICommandHandler<AssignWorkOrderCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(AssignWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await context.Set<WorkOrder>()
            .SingleOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);

        if (workOrder is null)
        {
            return Result.Failure(WorkOrderErrors.NotFound);
        }

        // Checked against users visible to this tenant, so a dispatcher cannot assign a work order
        // to an account from another organization by guessing its identifier.
        var userExists = await context.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken);

        if (!userExists)
        {
            return Result.Failure(Error.NotFound("User.NotFound", "The user was not found in this organization."));
        }

        var assigned = workOrder.Assign(request.UserId, request.ScheduledFor);

        if (assigned.IsFailure)
        {
            return assigned;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Marks a work order as underway.</summary>
/// <param name="WorkOrderId">The work order to start.</param>
public sealed record StartWorkOrderCommand(Guid WorkOrderId) : ICommand;

/// <summary>Handles <see cref="StartWorkOrderCommand"/>.</summary>
internal sealed class StartWorkOrderCommandHandler(
    IAegisDbContext context,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<StartWorkOrderCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(StartWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await context.Set<WorkOrder>()
            .SingleOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);

        if (workOrder is null)
        {
            return Result.Failure(WorkOrderErrors.NotFound);
        }

        var started = workOrder.Start(currentUser.Id ?? Guid.Empty, timeProvider.GetUtcNow());

        if (started.IsFailure)
        {
            return started;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Records that the work is done.</summary>
/// <param name="WorkOrderId">The work order to complete.</param>
/// <param name="Notes">What was done.</param>
public sealed record CompleteWorkOrderCommand(Guid WorkOrderId, string? Notes) : ICommand;

/// <summary>Validates <see cref="CompleteWorkOrderCommand"/>.</summary>
public sealed class CompleteWorkOrderCommandValidator : AbstractValidator<CompleteWorkOrderCommand>
{
    /// <summary>Initialises the validator.</summary>
    public CompleteWorkOrderCommandValidator() =>
        RuleFor(c => c.Notes).MaximumLength(2000);
}

/// <summary>Handles <see cref="CompleteWorkOrderCommand"/>.</summary>
/// <remarks>
/// <para>
/// <b>This is where the loop closes — twice over.</b> When the work order traces back to a
/// reported incident, completing it also resolves that incident, in the same transaction. When it
/// traces back to a maintenance plan instead, completing it advances that plan's next due date.
/// A dispatcher fixing a burst main, or a technician finishing a scheduled inspection, should not
/// have to remember a second step to tell the thing that asked for the work that it is done.
/// </para>
/// <para>
/// A work order can trace back to at most one of the two — a plan-generated work order has no
/// incident, and vice versa — so both branches below are independent, not competing.
/// </para>
/// <para>
/// Neither closure is implied in reverse: resolving an incident directly, or advancing a plan with
/// no completed work order, stays entirely valid for cases that never needed a dispatch.
/// </para>
/// </remarks>
internal sealed class CompleteWorkOrderCommandHandler(
    IAegisDbContext context,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<CompleteWorkOrderCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(CompleteWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await context.Set<WorkOrder>()
            .SingleOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);

        if (workOrder is null)
        {
            return Result.Failure(WorkOrderErrors.NotFound);
        }

        var now = timeProvider.GetUtcNow();
        var completedBy = currentUser.Id ?? Guid.Empty;

        var completed = workOrder.Complete(request.Notes, completedBy, now);

        if (completed.IsFailure)
        {
            return completed;
        }

        if (workOrder.IncidentId is { } incidentId)
        {
            var incident = await context.Set<Incident>()
                .SingleOrDefaultAsync(i => i.Id == incidentId, cancellationToken);

            // An incident that has already been closed some other way — resolved directly,
            // rejected, marked a duplicate — is left alone. Resolve() would simply reject a
            // closed incident, and that rejection is not this command's concern to report.
            if (incident is { IsOpen: true })
            {
                incident.Resolve(
                    $"Resolved via work order {workOrder.Reference}.",
                    completedBy,
                    now);
            }
        }

        if (workOrder.MaintenancePlanId is { } maintenancePlanId)
        {
            var plan = await context.Set<MaintenancePlan>()
                .SingleOrDefaultAsync(p => p.Id == maintenancePlanId, cancellationToken);

            plan?.Advance(now);
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Withdraws a work order without completing it.</summary>
/// <param name="WorkOrderId">The work order to cancel.</param>
/// <param name="Reason">Why, for the record.</param>
public sealed record CancelWorkOrderCommand(Guid WorkOrderId, string? Reason) : ICommand;

/// <summary>Handles <see cref="CancelWorkOrderCommand"/>.</summary>
internal sealed class CancelWorkOrderCommandHandler(IAegisDbContext context)
    : ICommandHandler<CancelWorkOrderCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(CancelWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await context.Set<WorkOrder>()
            .SingleOrDefaultAsync(w => w.Id == request.WorkOrderId, cancellationToken);

        if (workOrder is null)
        {
            return Result.Failure(WorkOrderErrors.NotFound);
        }

        var cancelled = workOrder.Cancel(request.Reason);

        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
