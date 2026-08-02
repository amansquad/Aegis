using System.Security.Claims;
using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Infrastructure.Security;
using Serilog.Context;

namespace Aegis.Api.Middleware;

/// <summary>
/// Establishes the organization scope for the request from the authenticated principal.
/// </summary>
/// <remarks>
/// <para>
/// <b>The tenant comes from the token and from nowhere else.</b> Not from a header, not from a
/// query parameter, not from a route segment. Any of those would let a caller nominate whose data
/// they read, which is a horizontal privilege escalation of the most direct kind. The organization
/// claim is signed as part of the JWT, so changing it requires forging the token.
/// </para>
/// <para>
/// Must run after <c>UseAuthentication</c> — before it, <c>HttpContext.User</c> is an
/// unauthenticated principal and the claim is not there to read.
/// </para>
/// <para>
/// A request without the claim is left with no tenant. Because the EF Core global filters compare
/// against a null organization, such a request sees no rows at all, which is the correct
/// fail-closed outcome for a malformed or legacy token.
/// </para>
/// </remarks>
public sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    ILogger<TenantResolutionMiddleware> logger)
{
    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var claim = context.User.FindFirstValue(AegisClaims.OrganizationId);

            if (Guid.TryParse(claim, out var organizationId) && organizationId != Guid.Empty)
            {
                tenantContext.SetTenant(organizationId);

                using (LogContext.PushProperty("OrganizationId", organizationId))
                {
                    await next(context);
                    return;
                }
            }

            logger.LogWarning(
                "Authenticated request to {Path} carried no usable organization claim; " +
                "queries will be scoped to no tenant and return no data",
                context.Request.Path);
        }

        await next(context);
    }
}
