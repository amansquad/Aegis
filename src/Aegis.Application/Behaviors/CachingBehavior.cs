using System.Text.Json;
using Aegis.Application.Abstractions.Caching;
using Aegis.Application.Common;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Aegis.Application.Behaviors;

/// <summary>
/// Serves queries that opt in via <see cref="ICacheableQuery"/> from the distributed cache.
/// </summary>
/// <remarks>
/// <para>
/// <b>Opt-in, and queries only.</b> A command must never be served from cache, and caching by
/// default would eventually cache something that must not be: a permission set, a live gauge
/// reading. Bugs of that shape depend on request ordering and are correspondingly miserable to
/// reproduce.
/// </para>
/// <para>
/// <b>Only successes are cached.</b> Caching a failure means a transient fault (a dropped
/// connection, a moment of contention) is replayed to every caller until the entry expires,
/// converting a blip into an outage.
/// </para>
/// <para>
/// <b>Cache faults are never fatal.</b> If Redis is unreachable the behaviour logs and falls
/// through to the handler. A cache is an optimisation; an API that returns 500 because its
/// optimisation is down has made itself less available than if it had no cache at all.
/// </para>
/// <para>
/// Tenant isolation is handled by the cache adapter, which prefixes every key with the current
/// organization. Callers cannot construct an unprefixed key.
/// </para>
/// </remarks>
public sealed class CachingBehavior<TRequest, TResponse>(
    ICacheService cache,
    ILogger<CachingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICacheableQuery cacheable)
        {
            return await next();
        }

        var valueType = ResultFactory.GetValueType(typeof(TResponse));

        // A plain Result carries no value, so there is nothing to cache.
        if (valueType is null)
        {
            return await next();
        }

        var key = cacheable.CacheKey;

        try
        {
            var cached = await cache.GetAsync<string>(key, cancellationToken);

            if (cached is not null)
            {
                var value = JsonSerializer.Deserialize(cached, valueType, SerializerOptions);

                if (value is not null)
                {
                    logger.LogDebug("Cache hit for {CacheKey}", key);
                    return ResultFactory.Success<TResponse>(value);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Cache read failed for {CacheKey}; falling through to the handler",
                key);
        }

        var response = await next();

        if (response.IsFailure)
        {
            return response;
        }

        try
        {
            var valueProperty = typeof(TResponse).GetProperty(nameof(Result<object>.Value));
            var resolved = valueProperty?.GetValue(response);

            if (resolved is not null)
            {
                var serialized = JsonSerializer.Serialize(resolved, valueType, SerializerOptions);
                await cache.SetAsync(key, serialized, cacheable.Expiration, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Cache write failed for {CacheKey}", key);
        }

        return response;
    }
}
