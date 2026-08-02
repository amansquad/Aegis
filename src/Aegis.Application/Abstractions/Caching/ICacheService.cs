namespace Aegis.Application.Abstractions.Caching;

/// <summary>
/// Distributed cache access, abstracted away from Redis specifics.
/// </summary>
/// <remarks>
/// Keys are always tenant-prefixed by the implementation. A cache key that omits the tenant is a
/// cross-tenant read waiting to happen, and unlike a missing SQL predicate it leaves no trace in
/// the query log — so prefixing is done in the adapter where callers cannot skip it.
/// </remarks>
public interface ICacheService
{
    /// <summary>Retrieves a cached value, or null when absent or expired.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>Stores a value with the supplied expiry, or the configured default when null.</summary>
    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Returns the cached value, invoking <paramref name="factory"/> and caching its result on a miss.
    /// </summary>
    /// <remarks>
    /// The implementation must not cache a null factory result. Caching "nothing found" turns a
    /// transient absence into a persistent one — the classic symptom being a newly created asset
    /// that stays invisible until the TTL expires.
    /// </remarks>
    Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>Removes a single key.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every key under a prefix, for invalidating a whole family of cached queries.
    /// </summary>
    /// <remarks>
    /// Backed by <c>SCAN</c> rather than <c>KEYS</c>: <c>KEYS</c> is O(n) and blocks the entire
    /// Redis event loop, which on a production instance means every other tenant's requests stall
    /// behind one cache invalidation.
    /// </remarks>
    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}
