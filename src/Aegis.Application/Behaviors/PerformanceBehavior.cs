using System.Diagnostics;
using Aegis.Application.Abstractions.Security;
using Aegis.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Aegis.Application.Behaviors;

/// <summary>
/// Times every request and warns when one exceeds the slow-request threshold.
/// </summary>
/// <remarks>
/// <para>
/// Performance problems in a system like this rarely announce themselves. A query returning in
/// 40 ms against seed data takes 4 s once an organization has 80,000 assets, and nobody notices
/// until a user complains. A warning at a fixed threshold turns that into a log search anyone can
/// run, on the day it starts rather than the week someone reports it.
/// </para>
/// <para>
/// The threshold is deliberately not configurable per request. A single number across the whole
/// API keeps the signal comparable, and a handler that legitimately needs longer than 500 ms is
/// usually a handler that should be doing its work in the background.
/// </para>
/// </remarks>
public sealed class PerformanceBehavior<TRequest, TResponse>(
    ILogger<PerformanceBehavior<TRequest, TResponse>> logger,
    ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    /// <summary>Requests slower than this are logged as warnings.</summary>
    private const int SlowRequestThresholdMilliseconds = 500;

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var timestamp = Stopwatch.GetTimestamp();

        var response = await next();

        var elapsed = Stopwatch.GetElapsedTime(timestamp);

        if (elapsed.TotalMilliseconds > SlowRequestThresholdMilliseconds)
        {
            logger.LogWarning(
                "Slow request: {RequestName} took {ElapsedMilliseconds} ms for user {UserId}",
                typeof(TRequest).Name,
                (long)elapsed.TotalMilliseconds,
                currentUser.Id);
        }

        return response;
    }
}
