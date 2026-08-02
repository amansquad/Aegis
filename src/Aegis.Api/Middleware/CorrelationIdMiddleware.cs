using Aegis.Infrastructure.Security;
using Serilog.Context;

namespace Aegis.Api.Middleware;

/// <summary>
/// Establishes a correlation identifier for the request and attaches it to every log line and to
/// the response.
/// </summary>
/// <remarks>
/// <para>
/// A caller-supplied <c>X-Correlation-Id</c> is honoured so that a single identifier follows an
/// operation across the frontend, this API and anything it calls. Absent one, a value is
/// generated.
/// </para>
/// <para>
/// The header is echoed on the response so that a user reporting a problem can quote an identifier
/// that finds the exact request in the logs. Without it, diagnosing "it failed around 3pm" means
/// searching by timestamp across every instance.
/// </para>
/// <para>
/// The inbound value is length-capped and stripped of control characters. It is written into log
/// output, so accepting it unvalidated is a log-injection and log-forging vector: a crafted value
/// containing newlines can fabricate convincing log entries.
/// </para>
/// </remarks>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const int MaxLength = 64;

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = Resolve(context);

        // TraceIdentifier is what IRequestContext reads, so overwriting it here is what makes the
        // inbound value flow into audit entries and problem responses.
        context.TraceIdentifier = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[RequestContext.CorrelationIdHeader] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string Resolve(HttpContext context)
    {
        var inbound = context.Request.Headers[RequestContext.CorrelationIdHeader].ToString();

        if (string.IsNullOrWhiteSpace(inbound))
        {
            return Guid.CreateVersion7().ToString("N");
        }

        var sanitized = new string(inbound
            .Where(c => !char.IsControl(c) && (char.IsLetterOrDigit(c) || c is '-' or '_'))
            .Take(MaxLength)
            .ToArray());

        return sanitized.Length == 0 ? Guid.CreateVersion7().ToString("N") : sanitized;
    }
}
