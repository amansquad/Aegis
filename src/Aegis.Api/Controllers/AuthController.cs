using Aegis.Application.Identity.Commands;
using Aegis.Application.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aegis.Api.Controllers;

/// <summary>Registration, sign-in, session renewal and sign-out.</summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ApiControllerBase
{
    /// <summary>Registers a new organization and its first administrator.</summary>
    /// <remarks>
    /// Anonymous by necessity: this is how a tenant comes into existence, so there is nobody who
    /// could yet be authenticated. In a deployment where sign-up is invitation-only, this endpoint
    /// is the one to disable.
    /// </remarks>
    /// <response code="200">The organization was created and the administrator signed in.</response>
    /// <response code="400">The request failed validation.</response>
    /// <response code="409">The organization identifier or email address is already taken.</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthenticationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Register(
        [FromBody] RegisterOrganizationCommand command,
        CancellationToken cancellationToken) =>
        SendAsync(command, cancellationToken);

    /// <summary>Authenticates with an email address and password.</summary>
    /// <remarks>
    /// Every failure returns the same message whether the address is unknown, the password is
    /// wrong, or the account is unconfirmed. Distinguishing them would turn this endpoint into an
    /// account enumeration oracle.
    /// </remarks>
    /// <response code="200">Authentication succeeded.</response>
    /// <response code="401">The credentials were rejected, or the account is locked.</response>
    /// <response code="403">The organization has been suspended.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthenticationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> SignIn(
        [FromBody] SignInCommand command,
        CancellationToken cancellationToken) =>
        SendAsync(command, cancellationToken);

    /// <summary>Exchanges a refresh token for a new access and refresh token pair.</summary>
    /// <remarks>
    /// The presented token is rotated: it is retired and a replacement issued. Presenting an
    /// already-rotated token is treated as evidence the session has been cloned, and revokes the
    /// entire chain.
    /// </remarks>
    /// <response code="200">The session was renewed.</response>
    /// <response code="401">The refresh token was unknown, expired, revoked, or replayed.</response>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthenticationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Refresh(
        [FromBody] RefreshSessionCommand command,
        CancellationToken cancellationToken) =>
        SendAsync(command, cancellationToken);

    /// <summary>Ends the caller's sessions.</summary>
    /// <remarks>
    /// Always succeeds, even for an unrecognised token. Sign-out is not a place to tell a caller
    /// whether a token was real, and a client tidying up after itself should not receive an error.
    /// </remarks>
    /// <response code="204">Sessions were revoked.</response>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public Task<IActionResult> SignOut(
        [FromBody] SignOutCommand command,
        CancellationToken cancellationToken) =>
        SendAsync(command, cancellationToken);
}
