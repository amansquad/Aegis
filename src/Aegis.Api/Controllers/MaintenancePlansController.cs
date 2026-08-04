using Aegis.Api.Authorization;
using Aegis.Application.Common.Models;
using Aegis.Application.Maintenance.Commands;
using Aegis.Application.Maintenance.Queries;
using Aegis.Domain.Identity;
using Aegis.Domain.WorkOrders;
using Microsoft.AspNetCore.Mvc;

namespace Aegis.Api.Controllers;

/// <summary>Recurring maintenance schedules and the work orders they generate.</summary>
/// <remarks>
/// A plan generates work orders through the ordinary dispatch path — see
/// <see cref="WorkOrdersController"/> — rather than through a second completion flow. Completing a
/// work order that traces back to a plan advances that plan's next due date in the same
/// transaction, the same loop-closing behaviour incidents already get.
/// </remarks>
[ApiController]
[Route("api/v1/maintenance-plans")]
public sealed class MaintenancePlansController : ApiControllerBase
{
    /// <summary>Lists maintenance plans, with filtering, search and paging.</summary>
    /// <response code="200">A page of maintenance plans.</response>
    /// <response code="403">The caller lacks <c>maintenance.view</c>.</response>
    [HttpGet]
    [HasPermission(Permissions.Maintenance.View)]
    [ProducesResponseType(typeof(PagedResult<MaintenancePlanListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> List(
        [FromQuery] ListMaintenancePlansQuery query,
        CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    /// <summary>Creates a recurring maintenance schedule for an asset.</summary>
    /// <response code="200">The plan was created; its identifier is returned.</response>
    /// <response code="404">The referenced asset does not exist in this organization.</response>
    [HttpPost]
    [HasPermission(Permissions.Maintenance.Schedule)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Create(
        [FromBody] CreateMaintenancePlanCommand command,
        CancellationToken cancellationToken) =>
        SendAsync(command, cancellationToken);

    /// <summary>Generates a work order for the next occurrence of a plan's scheduled work.</summary>
    /// <response code="200">The work order was created; its identifier is returned.</response>
    /// <response code="409">The plan is inactive, or already has an open work order.</response>
    [HttpPost("{maintenancePlanId:guid}/generate-work-order")]
    [HasPermission(Permissions.Maintenance.Schedule)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> GenerateWorkOrder(
        Guid maintenancePlanId,
        [FromBody] GenerateWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        SendAsync(
            new GenerateWorkOrderFromPlanCommand(maintenancePlanId, request.Priority),
            cancellationToken);

    /// <summary>Takes a plan out of rotation.</summary>
    /// <response code="204">The plan was deactivated.</response>
    /// <response code="409">The plan is already inactive.</response>
    [HttpPost("{maintenancePlanId:guid}/deactivate")]
    [HasPermission(Permissions.Maintenance.Configure)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Deactivate(Guid maintenancePlanId, CancellationToken cancellationToken) =>
        SendAsync(new DeactivateMaintenancePlanCommand(maintenancePlanId), cancellationToken);

    /// <summary>Puts a deactivated plan back into rotation.</summary>
    /// <response code="204">The plan was reactivated.</response>
    /// <response code="409">The plan is already active.</response>
    [HttpPost("{maintenancePlanId:guid}/reactivate")]
    [HasPermission(Permissions.Maintenance.Configure)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Reactivate(Guid maintenancePlanId, CancellationToken cancellationToken) =>
        SendAsync(new ReactivateMaintenancePlanCommand(maintenancePlanId), cancellationToken);
}

/// <summary>Body of a work-order-generation request.</summary>
/// <param name="Priority">How urgently this occurrence needs doing.</param>
public sealed record GenerateWorkOrderRequest(WorkOrderPriority Priority);
