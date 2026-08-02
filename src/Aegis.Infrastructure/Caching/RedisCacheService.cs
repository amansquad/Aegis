using System.Text.Json;
using Aegis.Application.Abstractions.Caching;
using Aegis.Application.Abstractions.Multitenancy;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Aegis.Infrastructure.Caching;

/// <summary>Cache configuration bound from the <c>Cache</c> configuration section.</summary>
public sealed class CacheOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Cache";

    /// <summary>Redis connection string.</summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Prefix applied to every key, isolating environments that share a Redis instance.
    /// </summary>
    /// <remarks>
    /// Without this, a staging deployment pointed at the production cache serves production data
    /// to staging users and, worse, poisons production entries with staging writes.
    /// </remarks>
    public string InstanceName { get; set; } = "aegis";

    /// <summary>Default entry lifetime in minutes.</summary>
    public int DefaultExpirationMinutes { get; set; } = 10;

    /// <summary>When false, all operations become no-ops and the handler always runs.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Redis-backed <see cref="ICacheService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every key is tenant-prefixed here, not by callers.</b> A cache key that omits the
/// organization serves one tenant's data to another, and unlike a missing SQL predicate it leaves
/// no trace in any query log. Doing the prefixing in the adapter means no caller can skip it.
/// </para>
/// <para>
/// <b>Nothing here is allowed to fail a request.</b> Cache faults are logged and swallowed so the
/// caller falls through to its source of truth. An API that returns 500 because its optimisation
/// is unavailable has made itself less available than one with no cache at all.
/// </para>
/// </remarks>
public sealed class RedisCacheService(
    IDistributedCache cache,
    IConnectionMultiplexer connectionMultiplexer,
    ITenantContext tenantContext,
    IOptions<CacheOptions> options,
    ILogger<RedisCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly CacheOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class
    {
        if (!_options.Enabled)
        {
            return null;
        }

        try
        {
            var payload = await cache.GetStringAsync(Qualify(key), cancellationToken);

            return payload is null
                ? null
                : JsonSerializer.Deserialize<T>(payload, SerializerOptions);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Cache read failed for {CacheKey}", key);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(value, SerializerOptions);

            var entryOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow =
                    expiration ?? TimeSpan.FromMinutes(_options.DefaultExpirationMinutes),
            };

            await cache.SetStringAsync(Qualify(key), payload, entryOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Cache write failed for {CacheKey}", key);
        }
    }

    /// <inheritdoc />
    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);

        var cached = await GetAsync<T>(key, cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        var created = await factory(cancellationToken);

        // A null result is deliberately not cached. Storing "nothing found" turns a transient
        // absence into a persistent one, the classic symptom being a newly created asset that
        // stays invisible until the entry expires.
        if (created is not null)
        {
            await SetAsync(key, created, expiration, cancellationToken);
        }

        return created;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            await cache.RemoveAsync(Qualify(key), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Cache eviction failed for {CacheKey}", key);
        }
    }

    /// <inheritdoc />
    public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var pattern = $"{Qualify(prefix)}*";

        try
        {
            foreach (var endpoint in connectionMultiplexer.GetEndPoints())
            {
                var server = connectionMultiplexer.GetServer(endpoint);

                // Replicas mirror the primary, so deleting through them is both unnecessary and
                // rejected by Redis.
                if (server.IsReplica || !server.IsConnected)
                {
                    continue;
                }

                var database = connectionMultiplexer.GetDatabase();

                // SCAN, never KEYS. KEYS is O(n) and blocks the entire Redis event loop, so on a
                // production instance a single invalidation stalls every other tenant's requests.
                await foreach (var redisKey in server
                                   .KeysAsync(pattern: pattern, pageSize: 250)
                                   .WithCancellation(cancellationToken))
                {
                    await database.KeyDeleteAsync(redisKey);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Cache prefix eviction failed for {CachePrefix}", prefix);
        }
    }

    /// <summary>
    /// Builds the physical key: <c>{instance}:{tenant}:{key}</c>.
    /// </summary>
    /// <remarks>
    /// Requests with no tenant use a <c>global</c> segment. That segment is still namespaced, so
    /// unauthenticated lookups cannot collide with, or read, any organization's entries.
    /// </remarks>
    private string Qualify(string key)
    {
        var tenantSegment = tenantContext.OrganizationId?.ToString("N") ?? "global";

        return $"{_options.InstanceName}:{tenantSegment}:{key}";
    }
}
