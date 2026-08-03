using Aegis.Api.Authorization;
using Aegis.Application.Common.Models;
using Aegis.Application.Incidents.Commands;
using Aegis.Application.Incidents.Queries;
using Aegis.Domain.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Aegis.Api.Controllers;

/// <summary>Incident intake and triage.</summary>
/// <remarks>
/// The natural-language intake endpoint is the platform's headline capability: a report written in
/// plain words becomes a structured, located, asset-linked incident. What the classifier proposes
/// is always visible alongside what a human confirmed, so nobody has to take the machine's word
/// for it.
/// </remarks>
[ApiController]
[Route("api/v1/incidents")]
public sealed class IncidentsController : ApiControllerBase
{
    /// <summary>Lists incidents, with filtering, search and paging.</summary>
    /// <remarks>
    /// Reporter contact details are deliberately excluded from this projection. A triage queue is
    /// read on shared screens and during shift handovers, and a member of the public's phone number
    /// does not belong on a wall display.
    /// </remarks>
    /// <response code="200">A page of incidents.</response>
    /// <response code="403">The caller lacks <c>incidents.view</c>.</response>
    [HttpGet]
    [HasPermission(Permissions.Incidents.View)]
    [ProducesResponseType(typeof(PagedResult<IncidentListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> List(
        [FromQuery] ListIncidentsQuery query,
        CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    /// <summary>Submits a free-text problem report.</summary>
    /// <remarks>
    /// <para>
    /// The report is classified, located and matched to an asset automatically, and the response
    /// says which of those the system was confident about. Anything below the confidence threshold,
    /// and anything describing danger to people, is flagged for a dispatcher to confirm before it
    /// is acted on.
    /// </para>
    /// <para>
    /// If the classifier is unavailable the report is still accepted, unclassified, for manual
    /// triage. Losing a member of the public's report because a language model was down would be
    /// an indefensible failure.
    /// </para>
    /// </remarks>
    /// <response code="200">The report was accepted. The response carries the reference to quote back.</response>
    /// <response code="400">The report was empty or too short to act on.</response>
    [HttpPost]
    [HasPermission(Permissions.Incidents.Report)]
    [ProducesResponseType(typeof(ReportIncidentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<IActionResult> Report(
        [FromBody] ReportIncidentCommand command,
        CancellationToken cancellationToken) =>
        SendAsync(command, cancellationToken);

    /// <summary>Confirms or corrects an incident's classification.</summary>
    /// <remarks>
    /// What the classifier proposed is retained after a correction, so the gap between proposal and
    /// confirmation stays measurable. That difference is the only honest measure of whether the
    /// classifier is any good, and it cannot be reconstructed later if only the final value is kept.
    /// </remarks>
    /// <response code="204">The classification was confirmed.</response>
    /// <response code="404">No such incident in this organization.</response>
    /// <response code="409">The incident has already been closed.</response>
    [HttpPost("{incidentId:guid}/triage")]
    [HasPermission(Permissions.Incidents.Triage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Triage(
        Guid incidentId,
        [FromBody] TriageIncidentRequest request,
        CancellationToken cancellationToken) =>
        SendAsync(
            new TriageIncidentCommand(
                incidentId,
                request.Category,
                request.Severity,
                request.Summary,
                request.AssetId),
            cancellationToken);

    /// <summary>Records that the underlying problem has been fixed.</summary>
    /// <response code="204">The incident was resolved.</response>
    /// <response code="409">The incident is not open.</response>
    [HttpPost("{incidentId:guid}/resolve")]
    [HasPermission(Permissions.Incidents.Close)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Resolve(
        Guid incidentId,
        [FromBody] ResolveIncidentRequest? request,
        CancellationToken cancellationToken) =>
        SendAsync(new ResolveIncidentCommand(incidentId, request?.Notes), cancellationToken);

    /// <summary>Closes an incident as the same problem as another.</summary>
    /// <remarks>
    /// Always a human decision. One burst main generates dozens of calls, but two leaks on the same
    /// street in the same hour is entirely possible, and auto-merging the second would lose a real
    /// problem. Intake surfaces a candidate; a dispatcher confirms it here.
    /// </remarks>
    /// <response code="204">The incident was closed as a duplicate.</response>
    /// <response code="404">Either incident was not found in this organization.</response>
    [HttpPost("{incidentId:guid}/duplicate-of/{originalIncidentId:guid}")]
    [HasPermission(Permissions.Incidents.Triage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> MarkDuplicate(
        Guid incidentId,
        Guid originalIncidentId,
        CancellationToken cancellationToken) =>
        SendAsync(new MarkDuplicateCommand(incidentId, originalIncidentId), cancellationToken);
}

/// <summary>Body of a triage request.</summary>
/// <param name="Category">The confirmed category.</param>
/// <param name="Severity">The confirmed severity.</param>
/// <param name="Summary">An optional corrected summary.</param>
/// <param name="AssetId">Optionally link the incident to an asset at the same time.</param>
public sealed record TriageIncidentRequest(
    Domain.Incidents.IncidentCategory Category,
    Domain.Incidents.IncidentSeverity Severity,
    string? Summary,
    Guid? AssetId);

/// <summary>Body of a resolve request.</summary>
/// <param name="Notes">What was done about the problem.</param>
public sealed record ResolveIncidentRequest(string? Notes);
