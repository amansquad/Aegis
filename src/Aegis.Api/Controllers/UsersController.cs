using Aegis.Api.Authorization;
using Aegis.Application.Common.Models;
using Aegis.Application.Identity.Commands;
using Aegis.Application.Identity.Contracts;
using Aegis.Application.Identity.Queries;
using Aegis.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aegis.Api.Controllers;

/// <summary>User and invitation management within the caller's organization.</summary>
/// <remarks>
/// Every action is guarded by a permission rather than a role. No endpoint asks "is this user an
/// Administrator?" — it asks whether they hold <c>users.create</c>, so an organization can define
/// whatever role structure suits it without any code changing.
/// </remarks>
[ApiController]
[Route("api/v1/users")]
public sealed class UsersController : ApiControllerBase
{
    /// <summary>Lists users in the caller's organization.</summary>
    /// <remarks>
    /// Results are scoped to the caller's organization by the persistence layer, not by anything
    /// written here. Supports paging, free-text search across name and email, filtering by status
    /// or role, and sorting by any scalar field; an unknown sort field is rejected with the list of
    /// valid ones rather than silently ignored.
    /// </remarks>
    /// <response code="200">A page of users.</response>
    /// <response code="403">The caller lacks <c>users.view</c>.</response>
    [HttpGet]
    [HasPermission(Permissions.Users.View)]
    [ProducesResponseType(typeof(PagedResult<UserListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> List(
        [FromQuery] ListUsersQuery query,
        CancellationToken cancellationToken) =>
        SendAsync(query, cancellationToken);

    /// <summary>Invites someone to join the organization.</summary>
    /// <remarks>
    /// Sends a single-use, time-limited link. Presenting that link is what confirms the recipient
    /// controls the address, so there is no separate email confirmation step.
    /// </remarks>
    /// <response code="200">The invitation was issued; the identifier is returned.</response>
    /// <response code="403">The caller lacks <c>users.create</c>.</response>
    /// <response code="409">The address already belongs to a member of this organization.</response>
    [HttpPost("invitations")]
    [HasPermission(Permissions.Users.Create)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Invite(
        [FromBody] InviteUserCommand command,
        CancellationToken cancellationToken) =>
        SendAsync(command, cancellationToken);

    /// <summary>Withdraws a pending invitation.</summary>
    /// <response code="204">The invitation was withdrawn.</response>
    /// <response code="404">No such pending invitation in this organization.</response>
    [HttpDelete("invitations/{invitationId:guid}")]
    [HasPermission(Permissions.Users.Create)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> RevokeInvitation(Guid invitationId, CancellationToken cancellationToken) =>
        SendAsync(new RevokeInvitationCommand(invitationId), cancellationToken);

    /// <summary>Accepts an invitation, creating the account and signing the new user in.</summary>
    /// <remarks>
    /// Anonymous and rate limited: the invitee has no account yet, so the token is the only
    /// credential, and an anonymous endpoint that creates accounts needs a budget.
    /// </remarks>
    /// <response code="200">The account was created and signed in.</response>
    /// <response code="400">The password failed screening, or a field was missing.</response>
    /// <response code="404">The invitation is unknown, expired or already used.</response>
    [HttpPost("invitations/accept")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Registration)]
    [ProducesResponseType(typeof(AuthenticationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> AcceptInvitation(
        [FromBody] AcceptInvitationCommand command,
        CancellationToken cancellationToken) =>
        SendAsync(command, cancellationToken);

    /// <summary>Grants a role to a user.</summary>
    /// <remarks>
    /// Takes effect immediately. The change rotates the user's security stamp, which invalidates
    /// their existing access token rather than waiting for it to expire.
    /// </remarks>
    /// <response code="204">The role was granted.</response>
    /// <response code="404">The user or role does not exist in this organization.</response>
    /// <response code="409">The user already holds the role.</response>
    [HttpPost("{userId:guid}/roles/{roleId:guid}")]
    [HasPermission(Permissions.Users.ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> AssignRole(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken) =>
        SendAsync(new AssignRoleCommand(userId, roleId), cancellationToken);

    /// <summary>Withdraws a role from a user.</summary>
    /// <response code="204">The role was withdrawn.</response>
    /// <response code="409">Refused because it would remove the last administrator.</response>
    [HttpDelete("{userId:guid}/roles/{roleId:guid}")]
    [HasPermission(Permissions.Users.ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> RemoveRole(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken) =>
        SendAsync(new RemoveRoleCommand(userId, roleId), cancellationToken);

    /// <summary>Deactivates a user account and ends all their sessions.</summary>
    /// <response code="204">The account was deactivated.</response>
    /// <response code="409">Refused: an administrator cannot deactivate their own account.</response>
    [HttpPost("{userId:guid}/deactivate")]
    [HasPermission(Permissions.Users.Deactivate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Deactivate(
        Guid userId,
        [FromBody] DeactivateUserRequest? request,
        CancellationToken cancellationToken) =>
        SendAsync(new DeactivateUserCommand(userId, request?.Reason), cancellationToken);

    /// <summary>Restores a deactivated account.</summary>
    /// <response code="204">The account was reactivated.</response>
    [HttpPost("{userId:guid}/reactivate")]
    [HasPermission(Permissions.Users.Deactivate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> Reactivate(Guid userId, CancellationToken cancellationToken) =>
        SendAsync(new ReactivateUserCommand(userId), cancellationToken);
}

/// <summary>Body of a deactivation request.</summary>
/// <param name="Reason">Why the account is being deactivated, recorded in the audit trail.</param>
public sealed record DeactivateUserRequest(string? Reason);
