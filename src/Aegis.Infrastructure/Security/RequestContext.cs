using Aegis.Application.Abstractions.Requests;
using Microsoft.AspNetCore.Http;

namespace Aegis.Infrastructure.Security;

/// <summary>
/// Reads transport-level request details from the ambient <see cref="HttpContext"/>.
/// </summary>
/// <remarks>
/// Falls back to a generated correlation id outside an HTTP context, so a background job still
/// produces audit entries whose activity can be grouped and traced.
/// </remarks>
public sealed class RequestContext(IHttpContextAccessor httpContextAccessor) : IRequestContext
{
    /// <summary>Header carrying a caller-supplied correlation id.</summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly string _fallbackCorrelationId = Guid.CreateVersion7().ToString("N");

    /// <inheritdoc />
    public string CorrelationId
    {
        get
        {
            var context = httpContextAccessor.HttpContext;

            if (context is null)
            {
                return _fallbackCorrelationId;
            }

            // TraceIdentifier is set per request by Kestrel, and the correlation middleware
            // overwrites it with any inbound X-Correlation-Id so that one identifier spans the
            // frontend, this API and everything it calls.
            return context.TraceIdentifier;
        }
    }

    /// <inheritdoc />
    public string? IpAddress
    {
        get
        {
            var context = httpContextAccessor.HttpContext;

            if (context is null)
            {
                return null;
            }

            // RemoteIpAddress is populated by ForwardedHeaders middleware when running behind a
            // reverse proxy. Reading X-Forwarded-For directly here would trust a client-supplied
            // header, letting any caller forge the address recorded in the audit trail.
            return context.Connection.RemoteIpAddress?.ToString();
        }
    }

    /// <inheritdoc />
    public string? UserAgent
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();

            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            // Bounded to the audit column width. A hostile client can send a multi-kilobyte user
            // agent, and truncating here keeps that from failing the save that carries it.
            return value.Length > 512 ? value[..512] : value;
        }
    }
}
