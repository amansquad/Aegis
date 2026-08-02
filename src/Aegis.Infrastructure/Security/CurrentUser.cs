using System.Security.Claims;
using Aegis.Application.Abstractions.Security;
using Microsoft.AspNetCore.Http;

namespace Aegis.Infrastructure.Security;

/// <summary>
/// Well-known claim types issued by Aegis and read back here.
/// </summary>
/// <remarks>
/// Short names are used deliberately. The SOAP-era URIs that
/// <see cref="ClaimTypes"/> defines (<c>http://schemas.xmlsoap.org/...</c>) add roughly 60 bytes
/// per claim to every request's Authorization header, which on a token carrying a dozen
/// permissions is a meaningful and entirely avoidable overhead.
/// </remarks>
public static class AegisClaims
{
    /// <summary>Subject: the user's identifier.</summary>
    public const string UserId = "sub";

    /// <summary>The user's email address.</summary>
    public const string Email = "email";

    /// <summary>The user's display name.</summary>
    public const string DisplayName = "name";

    /// <summary>The organization the token is scoped to.</summary>
    public const string OrganizationId = "org";

    /// <summary>A role assignment. May appear multiple times.</summary>
    public const string Role = "role";

    /// <summary>A granted permission. May appear multiple times.</summary>
    public const string Permission = "perm";
}

/// <summary>
/// Reads the current identity from the ambient <see cref="HttpContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every member tolerates the absence of an HTTP context. This class is resolved by pipeline
/// behaviours that also run under background jobs, integration tests and the offline sync
/// reconciler, where no request exists — returning nulls and empty collections there is correct,
/// and throwing would make the logging behaviour the thing that breaks a background job.
/// </para>
/// <para>
/// Values are computed per access rather than cached in a field. The instance is scoped, but
/// authentication populates the principal partway through the pipeline, so a value captured in the
/// constructor could be read before the user is known.
/// </para>
/// </remarks>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    /// <inheritdoc />
    public Guid? Id =>
        Guid.TryParse(Principal?.FindFirstValue(AegisClaims.UserId), out var id) ? id : null;

    /// <inheritdoc />
    public string? Email => Principal?.FindFirstValue(AegisClaims.Email);

    /// <inheritdoc />
    public string? DisplayName => Principal?.FindFirstValue(AegisClaims.DisplayName);

    /// <inheritdoc />
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles => ClaimValues(AegisClaims.Role);

    /// <inheritdoc />
    public IReadOnlyCollection<string> Permissions => ClaimValues(AegisClaims.Permission);

    /// <inheritdoc />
    public bool HasPermission(string permission) =>
        Principal?.HasClaim(AegisClaims.Permission, permission) ?? false;

    /// <inheritdoc />
    public bool IsInRole(string role) =>
        Principal?.HasClaim(AegisClaims.Role, role) ?? false;

    private string[] ClaimValues(string claimType) =>
        Principal?.FindAll(claimType).Select(c => c.Value).ToArray() ?? [];
}
