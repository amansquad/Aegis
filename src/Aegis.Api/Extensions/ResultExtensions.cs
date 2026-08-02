using Aegis.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Aegis.Api.Extensions;

/// <summary>
/// Translates a domain <see cref="Result"/> into an HTTP response.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place in the codebase that knows how a business failure maps to a status code.
/// Concentrating it here is what lets Domain and Application stay free of HTTP: a handler returns
/// <c>Error.NotFound</c> because the asset does not exist, not because it wants a 404, and if the
/// transport ever changes to gRPC or a message queue only this file needs rewriting.
/// </para>
/// <para>
/// Every failure is rendered as RFC 7807 <c>ProblemDetails</c>, so clients parse one error shape
/// for every endpoint rather than discovering a new one per controller.
/// </para>
/// </remarks>
public static class ResultExtensions
{
    /// <summary>Converts a valueless result into <c>204 No Content</c> or a problem response.</summary>
    public static IActionResult ToActionResult(this Result result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);

        return result.IsSuccess
            ? controller.NoContent()
            : Problem(result.Error, controller);
    }

    /// <summary>Converts a value-bearing result into <c>200 OK</c> or a problem response.</summary>
    public static IActionResult ToActionResult<T>(this Result<T> result, ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);

        return result.IsSuccess
            ? controller.Ok(result.Value)
            : Problem(result.Error, controller);
    }

    /// <summary>
    /// Converts a creation result into <c>201 Created</c> with a <c>Location</c> header, or a
    /// problem response.
    /// </summary>
    /// <param name="result">The creation outcome.</param>
    /// <param name="controller">The calling controller.</param>
    /// <param name="actionName">Action that serves the created resource.</param>
    /// <param name="routeValues">Route values identifying the created resource.</param>
    public static IActionResult ToCreatedResult<T>(
        this Result<T> result,
        ControllerBase controller,
        string actionName,
        object routeValues)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(controller);

        return result.IsSuccess
            ? controller.CreatedAtAction(actionName, routeValues, result.Value)
            : Problem(result.Error, controller);
    }

    /// <summary>Maps an <see cref="Error"/> onto its HTTP status code.</summary>
    public static int ToStatusCode(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.External => StatusCodes.Status502BadGateway,
            ErrorType.Failure => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest,
        };
    }

    private static IActionResult Problem(Error error, ControllerBase controller)
    {
        // Validation failures use ValidationProblemDetails so the field-level dictionary lands in
        // the standard "errors" member that every HTTP client library already understands.
        if (error.Type == ErrorType.Validation && error.Details is { Count: > 0 })
        {
            var validationProblem = new ValidationProblemDetails(
                error.Details.ToDictionary(kv => kv.Key, kv => kv.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = error.Message,
                Type = "https://datatracker.ietf.org/doc/html/rfc9110#name-400-bad-request",
                Instance = controller.HttpContext.Request.Path,
            };

            validationProblem.Extensions["errorCode"] = error.Code;
            validationProblem.Extensions["traceId"] = controller.HttpContext.TraceIdentifier;

            return controller.BadRequest(validationProblem);
        }

        var statusCode = error.ToStatusCode();

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = error.Message,
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = controller.HttpContext.Request.Path,
        };

        // The stable, machine-readable code clients should branch on. The title is prose and may
        // be reworded or localised; this must not be.
        problem.Extensions["errorCode"] = error.Code;
        problem.Extensions["traceId"] = controller.HttpContext.TraceIdentifier;

        return controller.StatusCode(statusCode, problem);
    }
}
