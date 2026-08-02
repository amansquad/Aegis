using System.Security.Claims;
using System.Text.Encodings.Web;
using Aegis.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only authentication that builds a principal from request headers.
/// </summary>
/// <remarks>
/// <para>
/// Lets tests assert on tenant scoping and authorization before the Identity module exists, and
/// keeps every later test from paying a login round trip it is not actually testing.
/// </para>
/// <para>
/// This is registered only by <see cref="AegisWebApplicationFactory"/> through
/// <c>ConfigureTestServices</c>, and lives in the test assembly. It cannot reach a deployed build:
/// the production host has no reference to this project.
/// </para>
/// </remarks>
public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The scheme name used by the test host.</summary>
    public const string SchemeName = "IntegrationTest";

    /// <summary>Header supplying the user id to authenticate as.</summary>
    public const string UserIdHeader = "X-Test-UserId";

    /// <summary>Header supplying the organization claim.</summary>
    public const string OrganizationHeader = "X-Test-OrganizationId";

    /// <summary>Header supplying a comma-delimited permission list.</summary>
    public const string PermissionsHeader = "X-Test-Permissions";

    /// <summary>Header supplying a comma-delimited role list.</summary>
    public const string RolesHeader = "X-Test-Roles";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No user header means an anonymous request. Returning NoResult rather than Fail lets
        // [AllowAnonymous] endpoints behave exactly as they do in production.
        if (!Request.Headers.TryGetValue(UserIdHeader, out var userId) ||
            string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(AegisClaims.UserId, userId.ToString()),
            new(AegisClaims.Email, $"{userId}@aegis.test"),
            new(AegisClaims.DisplayName, "Integration Test User"),
        };

        if (Request.Headers.TryGetValue(OrganizationHeader, out var organizationId) &&
            !string.IsNullOrWhiteSpace(organizationId))
        {
            claims.Add(new Claim(AegisClaims.OrganizationId, organizationId.ToString()));
        }

        AddDelimited(claims, RolesHeader, AegisClaims.Role);
        AddDelimited(claims, PermissionsHeader, AegisClaims.Permission);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

        return Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    private void AddDelimited(List<Claim> claims, string header, string claimType)
    {
        if (!Request.Headers.TryGetValue(header, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        foreach (var value in raw.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries |
                                                        StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim(claimType, value));
        }
    }
}

/// <summary>Registers the test authentication scheme.</summary>
public static class TestAuthenticationExtensions
{
    /// <summary>Adds header-driven authentication for integration tests.</summary>
    public static IServiceCollection AddTestAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.SchemeName,
                _ => { });

        return services;
    }
}
