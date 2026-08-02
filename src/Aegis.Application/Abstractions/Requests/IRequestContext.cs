namespace Aegis.Application.Abstractions.Requests;

/// <summary>
/// Ambient details of the transport-level request that triggered the current operation.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="Security.ICurrentUser"/> because the two answer different
/// questions: <c>ICurrentUser</c> is "who", this is "from where, and under which trace". Audit
/// entries need both, and background jobs have the second without the first.
/// </remarks>
public interface IRequestContext
{
    /// <summary>
    /// Identifier correlating every log line, audit entry and downstream call for one request.
    /// </summary>
    /// <remarks>
    /// Taken from the inbound <c>X-Correlation-Id</c> header when present so that a trace spans
    /// the frontend, the API and any service it calls; generated when absent. Echoed back on the
    /// response so a user reporting a problem can quote an identifier that finds the exact logs.
    /// </remarks>
    string CorrelationId { get; }

    /// <summary>Client IP address, or null outside an HTTP context.</summary>
    string? IpAddress { get; }

    /// <summary>Client user agent, or null outside an HTTP context.</summary>
    string? UserAgent { get; }
}
