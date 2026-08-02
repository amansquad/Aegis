using System.Globalization;
using Aegis.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Aegis.Api.Authorization;

/// <summary>
/// Requires the caller to hold a named permission.
/// </summary>
/// <remarks>
/// <para>
/// Used as <c>[HasPermission(Permissions.Assets.Create)]</c>. Deliberately not
/// <c>[Authorize(Roles = "Supervisor")]</c>: roles are a packaging convenience for administrators,
/// and hard-coding one into an endpoint means the first customer who wants a slightly different
/// role needs a code change and a release.
/// </para>
/// <para>
/// Multiple attributes on one action are combined with AND, which is how ASP.NET Core composes
/// authorization filters. Use a single attribute per action unless every listed permission really
/// is required together.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    /// <summary>Prefix identifying a permission policy, to distinguish it from named policies.</summary>
    public const string PolicyPrefix = "perm:";

    /// <summary>Requires the named permission.</summary>
    /// <param name="permission">A constant from <see cref="Domain.Identity.Permissions"/>.</param>
    public HasPermissionAttribute(string permission)
        : base(PolicyPrefix + permission) => Permission = permission;

    /// <summary>The required permission.</summary>
    public string Permission { get; }
}

/// <summary>The authorization requirement carrying the permission to check.</summary>
/// <param name="Permission">The required permission.</param>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>
/// Manufactures a policy per permission on first use.
/// </summary>
/// <remarks>
/// The alternative is registering one policy per permission at startup, which means every new
/// permission needs a matching registration and the two lists drift. A dynamic provider keeps the
/// permission catalogue as the single source of truth: adding a constant and using the attribute is
/// all that is required.
/// </remarks>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    /// <inheritdoc />
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Explicitly registered policies win, so a hand-written policy can still override the
        // convention when something genuinely needs bespoke logic.
        var explicitPolicy = await base.GetPolicyAsync(policyName);

        if (explicitPolicy is not null)
        {
            return explicitPolicy;
        }

        if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var permission = policyName[HasPermissionAttribute.PolicyPrefix.Length..];

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
    }
}

/// <summary>
/// Evaluates a <see cref="PermissionRequirement"/> against the caller's claims.
/// </summary>
/// <remarks>
/// <para>
/// Reads permissions from the token rather than from the database, so authorization costs no round
/// trip. The trade-off is explicit and bounded: a permission withdrawn from a role takes effect
/// when the caller's access token expires, which is capped at fifteen minutes by
/// <c>Jwt:AccessTokenMinutes</c>. Revoking access immediately requires the refresh path, which does
/// re-read from the database.
/// </para>
/// <para>
/// A caller with no organization claim is refused outright. Such a token cannot be scoped to a
/// tenant, so any request it makes would read nothing and write orphaned rows.
/// </para>
/// </remarks>
public sealed class PermissionAuthorizationHandler(ILogger<PermissionAuthorizationHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        var principal = context.User;

        if (principal.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var organization = principal.FindFirst(AegisClaims.OrganizationId)?.Value;

        if (string.IsNullOrWhiteSpace(organization))
        {
            logger.LogWarning(
                "Denying {Permission}: the token carries no organization claim and cannot be " +
                "scoped to a tenant",
                requirement.Permission);

            return Task.CompletedTask;
        }

        if (principal.HasClaim(AegisClaims.Permission, requirement.Permission))
        {
            context.Succeed(requirement);
        }
        else
        {
            logger.LogInformation(
                "Denying {Permission} for user {UserId} in organization {OrganizationId}",
                requirement.Permission,
                principal.FindFirst(AegisClaims.UserId)?.Value,
                organization);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Formats a permission constant as its policy name.</summary>
public static class PermissionPolicy
{
    /// <summary>Returns the policy name for a permission.</summary>
    public static string For(string permission) =>
        string.Create(CultureInfo.InvariantCulture, $"{HasPermissionAttribute.PolicyPrefix}{permission}");
}
