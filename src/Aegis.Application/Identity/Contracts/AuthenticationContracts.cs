namespace Aegis.Application.Identity.Contracts;

/// <summary>The signed-in user, as returned to a client.</summary>
/// <param name="Id">User identifier.</param>
/// <param name="Email">Email address.</param>
/// <param name="DisplayName">Full name for display.</param>
/// <param name="OrganizationId">The organization the session is scoped to.</param>
/// <param name="OrganizationName">Display name of that organization.</param>
/// <param name="Roles">Role names held, for display in the UI.</param>
/// <param name="Permissions">
/// Effective permissions. Sent so the client can hide actions the user cannot perform.
/// </param>
/// <remarks>
/// Sending permissions to the client is a usability measure, never an enforcement one. A hidden
/// button is a courtesy; the server re-checks every permission on every request, because anything
/// the browser decides is a decision an attacker also controls.
/// </remarks>
public sealed record AuthenticatedUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    Guid OrganizationId,
    string OrganizationName,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);

/// <summary>A successful authentication.</summary>
/// <param name="AccessToken">Short-lived signed JWT.</param>
/// <param name="RefreshToken">Opaque long-lived token, exchangeable for a new pair.</param>
/// <param name="AccessTokenExpiresOnUtc">When the access token stops being accepted.</param>
/// <param name="TokenType">Always <c>Bearer</c>. Present so clients can build the header generically.</param>
/// <param name="User">The signed-in user.</param>
public sealed record AuthenticationResultDto(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresOnUtc,
    string TokenType,
    AuthenticatedUserDto User)
{
    /// <summary>The only token type this API issues.</summary>
    public const string BearerTokenType = "Bearer";
}
