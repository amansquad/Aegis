namespace Aegis.Application.Abstractions.Security;

/// <summary>
/// Answers whether an access token's embedded security stamp still matches the user's current one.
/// </summary>
/// <remarks>
/// <para>
/// An access token is verified from its signature alone and cannot be recalled, so on its own a
/// revoked role or a deactivated account stays effective until the token expires. Checking the
/// stamp on every request closes that window — but a database read per request would undo the main
/// reason access tokens are self-contained.
/// </para>
/// <para>
/// The resolution is cache-aside with <em>explicit eviction</em> rather than a short expiry. A TTL
/// would mean revocation takes effect somewhere between now and the TTL, which is a worse contract
/// than either extreme: slower than immediate, and impossible to state precisely to a security
/// reviewer. Evicting the entry when the stamp rotates makes revocation immediate and the steady
/// state a single cache read.
/// </para>
/// </remarks>
public interface ISecurityStampService
{
    /// <summary>Returns true when the supplied stamp matches the user's current one.</summary>
    /// <param name="userId">The user the token names.</param>
    /// <param name="stamp">The stamp embedded in the token.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> IsCurrentAsync(Guid userId, string? stamp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the cached stamp for a user, so the next request re-reads it.
    /// </summary>
    /// <remarks>
    /// Called from domain event handlers whenever a user's security posture changes. Missing a call
    /// here means a revoked capability keeps working until the token expires, so the events are
    /// subscribed to rather than the call sites being edited one by one.
    /// </remarks>
    Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default);
}
