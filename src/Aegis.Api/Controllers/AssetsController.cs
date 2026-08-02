using Aegis.Api.Authorization;
using Aegis.Application.Assets.Commands;
using Aegis.Application.Assets.Queries;
using Aegis.Application.Common.Models;
using Aegis.Domain.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Aegis.Api.Controllers;

/// <summary>The asset registry: pipes, pumps, transformers, roads and everything else.</summary>
/// <remarks>
/// The central resource of the platform. Incidents are reported against assets, work orders are
/// raised on them, and maintenance is scheduled from their condition.
/// </remarks>
[ApiController]
[Route("api/v1/assets")]
public sealed class AssetsController : ApiControllerBase
{
    /// <summary>Lists assets, with filtering, search, paging and proximity search.</summary>
    /// <remarks>
    /// <para>
    /// Supports filtering by type, status, condition, criticality and parent, plus a proximity
    /// search: supply <c>nearLatitude</c>, <c>nearLongitude</c> and <c>withinMetres</c> to find
    /// what is around a point. That query is what makes the map and incident triage useful —
    /// "what is near where this leak was reported?"
    /// </para>
    /// <para>
    /// Results are scoped to the caller's organization by the persistence layer, not by anything
    /// this endpoint does.
    /// </para>
    /// </remarks>
    /// <response code="200">A page of assets.</response>
    /// <response code="400">An unknown sort field, or an incomplete proximity search.</response>
    /// <response code="403">The caller lacks <c>assets.view</c>.</response>
    [HttpGet]
    [HasPermission(Permissions.Assets.View)]
    [ProducesResponseType(typeof(PagedResult<AssetListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> List(
        [FromQuery] ListAssetsQuery query,
        CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    /// <summary>Adds an asset to the registry.</summary>
    /// <response code="200">The asset was registered; its identifier is returned.</response>
    /// <response code="409">The asset code is already in use in this organization.</response>
    [HttpPost]
    [HasPermission(Permissions.Assets.Create)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Register(
        [FromBody] RegisterAssetCommand command,
        CancellationToken cancellationToken) =>
        SendAsync(command, cancellationToken);

    /// <summary>Records a condition inspection against an asset.</summary>
    /// <remarks>
    /// The inspection and the resulting condition change are one operation, so an asset's condition
    /// can never disagree with its most recent inspection. A backdated inspection is stored but
    /// does not overwrite a newer assessment — which is what makes offline sync safe.
    /// </remarks>
    /// <response code="200">The inspection was recorded; its identifier is returned.</response>
    /// <response code="404">No such asset in this organization.</response>
    /// <response code="409">The asset is decommissioned.</response>
    [HttpPost("{assetId:guid}/inspections")]
    [HasPermission(Permissions.Assets.Update)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> RecordInspection(
        Guid assetId,
        [FromBody] RecordInspectionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync(
            new RecordInspectionCommand(
                assetId,
                request.Condition,
                request.InspectedOnUtc,
                request.Notes),
            cancellationToken);

    /// <summary>Permanently retires an asset.</summary>
    /// <remarks>
    /// Retirement, not deletion: the record is retained because operators must be able to say what
    /// was installed where, often decades later. Refused while the asset still contains assets that
    /// are in service.
    /// </remarks>
    /// <response code="204">The asset was decommissioned.</response>
    /// <response code="409">The asset still contains in-service assets, or is already retired.</response>
    [HttpPost("{assetId:guid}/decommission")]
    [HasPermission(Permissions.Assets.Decommission)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Decommission(
        Guid assetId,
        [FromBody] DecommissionAssetRequest? request,
        CancellationToken cancellationToken) =>
        SendAsync(new DecommissionAssetCommand(assetId, request?.Reason), cancellationToken);
}

/// <summary>Body of an inspection request.</summary>
/// <param name="Condition">The assessed condition.</param>
/// <param name="InspectedOnUtc">
/// When the inspection took place. Supplied by the caller rather than taken from the server clock,
/// so an inspection carried out offline is dated when it happened rather than when it synced.
/// </param>
/// <param name="Notes">Observations.</param>
public sealed record RecordInspectionRequest(
    Domain.Assets.AssetCondition Condition,
    DateTimeOffset InspectedOnUtc,
    string? Notes);

/// <summary>Body of a decommission request.</summary>
/// <param name="Reason">Why the asset is being retired.</param>
public sealed record DecommissionAssetRequest(string? Reason);
