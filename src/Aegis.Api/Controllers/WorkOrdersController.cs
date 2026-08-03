using Aegis.Api.Authorization;
using Aegis.Application.Common.Models;
using Aegis.Application.WorkOrders.Commands;
using Aegis.Application.WorkOrders.Queries;
using Aegis.Domain.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Aegis.Api.Controllers;

/// <summary>Work order dispatch, assignment and completion.</summary>
/// <remarks>
/// Completing a work order that traces back to a reported incident resolves that incident in the
/// same transaction, so a dispatcher closing out a job does not need a second step to tell the
/// person who reported it that it is done.
/// </remarks>
[ApiController]
[Route("api/v1/work-orders")]
public sealed class WorkOrdersController : ApiControllerBase
{
    /// <summary>Lists work orders, with filtering, search and paging.</summary>
    /// <response code="200">A page of work orders.</response>
    /// <response code="403">The caller lacks <c>workorders.view</c>.</response>
    [HttpGet]
    [HasPermission(Permissions.WorkOrders.View)]
    [ProducesResponseType(typeof(PagedResult<WorkOrderListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> List(
        [FromQuery] ListWorkOrdersQuery query,
        CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    /// <summary>Dispatches a new work order, optionally against an asset or from an incident.</summary>
    /// <response code="200">The work order was created; its identifier is returned.</response>
    /// <response code="404">The referenced asset or incident does not exist in this organization.</response>
    [HttpPost]
    [HasPermission(Permissions.WorkOrders.Create)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Create(
        [FromBody] CreateWorkOrderCommand command,
        CancellationToken cancellationToken) =>
        SendAsync(command, cancellationToken);

    /// <summary>Assigns or reassigns a technician.</summary>
    /// <response code="204">The work order was assigned.</response>
    /// <response code="404">No such work order, or no such user, in this organization.</response>
    /// <response code="409">The work order has already been closed.</response>
    [HttpPost("{workOrderId:guid}/assign")]
    [HasPermission(Permissions.WorkOrders.Assign)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Assign(
        Guid workOrderId,
        [FromBody] AssignWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAsync(
            new AssignWorkOrderCommand(workOrderId, request.UserId, request.ScheduledFor),
            cancellationToken);

    /// <summary>Marks a work order as underway.</summary>
    /// <response code="204">The work order was started.</response>
    /// <response code="409">The work order is not assigned, or cannot be started from its current state.</response>
    [HttpPost("{workOrderId:guid}/start")]
    [HasPermission(Permissions.WorkOrders.Assign)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Start(Guid workOrderId, CancellationToken cancellationToken) =>
        SendAsync(new StartWorkOrderCommand(workOrderId), cancellationToken);

    /// <summary>Records that the work is done.</summary>
    /// <remarks>
    /// When this work order resolves an incident, that incident is resolved in the same
    /// transaction.
    /// </remarks>
    /// <response code="204">The work order was completed.</response>
    /// <response code="409">The work order is not assigned, or is not open.</response>
    [HttpPost("{workOrderId:guid}/complete")]
    [HasPermission(Permissions.WorkOrders.Complete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Complete(
        Guid workOrderId,
        [FromBody] CompleteWorkOrderRequest? request,
        CancellationToken cancellationToken) =>
        SendAsync(new CompleteWorkOrderCommand(workOrderId, request?.Notes), cancellationToken);

    /// <summary>Withdraws a work order without completing it.</summary>
    /// <response code="204">The work order was cancelled.</response>
    /// <response code="409">The work order is not open.</response>
    [HttpPost("{workOrderId:guid}/cancel")]
    [HasPermission(Permissions.WorkOrders.Assign)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Cancel(
        Guid workOrderId,
        [FromBody] CancelWorkOrderRequest? request,
        CancellationToken cancellationToken) =>
        SendAsync(new CancelWorkOrderCommand(workOrderId, request?.Reason), cancellationToken);
}

/// <summary>Body of an assignment request.</summary>
/// <param name="UserId">The technician responsible.</param>
/// <param name="ScheduledFor">When the work is planned, if known.</param>
public sealed record AssignWorkOrderRequest(Guid UserId, DateTimeOffset? ScheduledFor);

/// <summary>Body of a completion request.</summary>
/// <param name="Notes">What was done.</param>
public sealed record CompleteWorkOrderRequest(string? Notes);

/// <summary>Body of a cancellation request.</summary>
/// <param name="Reason">Why the work order was withdrawn.</param>
public sealed record CancelWorkOrderRequest(string? Reason);
