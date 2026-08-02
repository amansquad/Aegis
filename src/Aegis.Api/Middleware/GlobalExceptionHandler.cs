using Aegis.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Api.Middleware;

/// <summary>
/// Converts unhandled exceptions into RFC 7807 problem responses.
/// </summary>
/// <remarks>
/// <para>
/// Reaching this handler means something genuinely went wrong. Expected business failures travel
/// back as <see cref="Result"/> and never get here, so anything caught in this class is either a
/// bug or an infrastructure fault, and both are logged at <c>Error</c>.
/// </para>
/// <para>
/// <b>Exception detail is never returned to the client.</b> Stack traces name internal types,
/// file paths and library versions, which is reconnaissance handed to an attacker for free. The
/// client receives a generic message and a trace identifier; the operator gets the full detail in
/// the logs. The trace id is what connects the two when a user reports a failure.
/// </para>
/// <para>
/// Implemented as <see cref="IExceptionHandler"/>, the ASP.NET Core 8+ abstraction, rather than as
/// custom middleware, so it composes with the built-in exception handling pipeline instead of
/// duplicating it.
/// </para>
/// </remarks>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        // A cancelled request is the client hanging up, not a fault. Logging it as an error trains
        // operators to ignore the error log; returning 499 records it without alarming anyone.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request {Path} was cancelled by the client", httpContext.Request.Path);

            httpContext.Response.StatusCode = 499;
            return true;
        }

        var (statusCode, title, errorCode) = Classify(exception);

        logger.LogError(
            exception,
            "Unhandled {ExceptionType} on {Method} {Path} (trace {TraceId})",
            exception.GetType().Name,
            httpContext.Request.Method,
            httpContext.Request.Path,
            httpContext.TraceIdentifier);

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = httpContext.Request.Path,
        };

        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        // Detail is exposed only outside production, where it saves a developer a trip to the log
        // aggregator and cannot leak to a real user.
        if (!environment.IsProduction())
        {
            problem.Extensions["exception"] = exception.GetType().FullName;
            problem.Detail = exception.Message;
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }

    /// <summary>
    /// Maps an exception type onto a status code, title and stable error code.
    /// </summary>
    /// <remarks>
    /// The titles are deliberately generic. "An unexpected error occurred" tells an attacker
    /// nothing, and tells a legitimate user everything they can act on, which is to quote the
    /// trace id when reporting it.
    /// </remarks>
    private static (int StatusCode, string Title, string ErrorCode) Classify(Exception exception) =>
        exception switch
        {
            // A domain invariant was violated, which means validation upstream failed to prevent
            // an impossible state. The user cannot fix it, so it is reported as a server fault
            // even though the trigger arrived over HTTP.
            DomainException domain => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.",
                domain.Code),

            // Optimistic concurrency: someone else changed the row first. This is genuinely
            // actionable by the client, which should re-read and retry.
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "The record was modified by another user. Reload and try again.",
                "Persistence.ConcurrencyConflict"),

            DbUpdateException => (
                StatusCodes.Status409Conflict,
                "The change conflicts with existing data.",
                "Persistence.UpdateFailed"),

            UnauthorizedAccessException => (
                StatusCodes.Status403Forbidden,
                "You do not have permission to perform this action.",
                "Auth.Forbidden"),

            TimeoutException => (
                StatusCodes.Status504GatewayTimeout,
                "The operation timed out.",
                "Request.Timeout"),

            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.",
                "Server.Unhandled"),
        };
}
