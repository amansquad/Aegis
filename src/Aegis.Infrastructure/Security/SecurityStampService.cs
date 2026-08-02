using Aegis.Application.Abstractions.Security;
using Aegis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Aegis.Infrastructure.Security;

/// <summary>
/// Validates security stamps with a cache-aside read and explicit eviction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Uses <see cref="IDistributedCache"/> directly rather than <c>ICacheService</c>, deliberately.</b>
/// <c>ICacheService</c> prefixes every key with the current organization, which is exactly right for
/// business data. It is wrong here: stamp validation runs during token validation, <em>before</em>
/// the tenant middleware has established an organization, so a read would land under the "global"
/// prefix while an eviction from inside a request would land under the tenant's. The two would
/// never meet, and revocation would silently never take effect.
/// </para>
/// <para>
/// User identifiers are globally unique, so a tenant-independent key namespace is correct here
/// rather than merely convenient.
/// </para>
/// </remarks>
public sealed class SecurityStampService(
    IDistributedCache cache,
    AegisDbContext context,
    ILogger<SecurityStampService> logger) : ISecurityStampService
{
    /// <summary>
    /// Backstop expiry.
    /// </summary>
    /// <remarks>
    /// Correctness comes from eviction, not from this. The TTL exists only so that an entry cannot
    /// outlive its user indefinitely if an eviction is ever lost — a Redis failover mid-write, for
    /// instance. Twelve hours is short enough to bound that and long enough that it plays no part
    /// in normal operation.
    /// </remarks>
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12),
    };

    /// <inheritdoc />
    public async Task<bool> IsCurrentAsync(
        Guid userId,
        string? stamp,
        CancellationToken cancellationToken = default)
    {
        // A token with no stamp claim predates this check or was not issued by us. Rejected rather
        // than waved through: accepting it would leave a bypass for exactly the tokens the check
        // exists to catch.
        if (string.IsNullOrWhiteSpace(stamp))
        {
            return false;
        }

        var current = await ReadCurrentStampAsync(userId, cancellationToken);

        // No stamp resolvable means no such user, or one that has been hard-deleted. Fail closed.
        if (current is null)
        {
            return false;
        }

        return string.Equals(current, stamp, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(KeyFor(userId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Logged at Warning rather than swallowed silently: a failed eviction means a revoked
            // capability keeps working until the backstop expiry, which an operator should see.
            logger.LogWarning(
                exception,
                "Failed to evict the cached security stamp for {UserId}; a revoked capability may " +
                "remain effective until the backstop expiry",
                userId);
        }
    }

    private async Task<string?> ReadCurrentStampAsync(Guid userId, CancellationToken cancellationToken)
    {
        var key = KeyFor(userId);

        try
        {
            var cached = await cache.GetStringAsync(key, cancellationToken);

            if (!string.IsNullOrEmpty(cached))
            {
                return cached;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Falls through to the database. A cache outage must degrade performance, not
            // authentication.
            logger.LogWarning(exception, "Security stamp cache read failed for {UserId}", userId);
        }

        // IgnoreQueryFilters because no tenant is established during token validation. The lookup
        // is by primary key, so this cannot return another organization's row.
        var stamp = await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.Id == userId && !u.IsDeleted)
            .Select(u => u.SecurityStamp)
            .SingleOrDefaultAsync(cancellationToken);

        if (stamp is null)
        {
            return null;
        }

        try
        {
            await cache.SetStringAsync(key, stamp, CacheOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Security stamp cache write failed for {UserId}", userId);
        }

        return stamp;
    }

    private static string KeyFor(Guid userId) => $"aegis:sstamp:{userId:N}";
}
