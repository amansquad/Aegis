using Aegis.Api.Extensions;
using Aegis.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Aegis.Api.Controllers;

/// <summary>
/// Base class for Aegis API controllers.
/// </summary>
/// <remarks>
/// <para>
/// Controllers in this codebase do exactly three things: bind the request, send it through
/// MediatR, and translate the <see cref="Result"/> into an HTTP response. Any action longer than
/// a few lines has business logic in it, and that logic belongs in a handler where it can be unit
/// tested without a web server.
/// </para>
/// <para>
/// The mediator is resolved lazily from the request services rather than injected through a
/// constructor, so that derived controllers need no constructor boilerplate at all.
/// </para>
/// </remarks>
[ApiController]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _mediator;

    /// <summary>The MediatR sender used to dispatch commands and queries.</summary>
    protected ISender Mediator =>
        _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    /// <summary>Sends a command and returns <c>204 No Content</c> on success.</summary>
    protected async Task<IActionResult> SendAsync(
        IRequest<Result> command,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(command, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>Sends a request and returns <c>200 OK</c> with its value on success.</summary>
    protected async Task<IActionResult> SendAsync<T>(
        IRequest<Result<T>> request,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(request, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Sends a creation command and returns <c>201 Created</c> with a <c>Location</c> header.
    /// </summary>
    /// <param name="command">The creation command.</param>
    /// <param name="actionName">Action that serves the created resource.</param>
    /// <param name="routeValues">Builds route values from the created resource.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    protected async Task<IActionResult> SendCreatedAsync<T>(
        IRequest<Result<T>> command,
        string actionName,
        Func<T, object> routeValues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routeValues);

        var result = await Mediator.Send(command, cancellationToken);

        return result.IsSuccess
            ? result.ToCreatedResult(this, actionName, routeValues(result.Value))
            : result.ToActionResult(this);
    }
}
