using Aegis.Domain.Common;

namespace Aegis.Domain.Identity;

/// <summary>
/// A single issued refresh token, stored as a hash.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only the hash is persisted.</b> A refresh token is a bearer credential with a long lifetime,
/// so storing it in readable form makes a database backup, a rogue query or an SQL injection
/// equivalent to handing out live sessions. Hashing means a stolen table yields nothing usable —
/// the same reasoning that applies to passwords, for the same reason.
/// </para>
/// <para>
/// A child entity of the <see cref="User"/> aggregate: tokens have no meaning apart from their
/// user, are always loaded and saved with them, and are never referenced from outside.
/// </para>
/// </remarks>
public sealed class RefreshToken : Entity<Guid>
{
    /// <summary>Parameterless constructor required by EF Core materialisation.</summary>
    private RefreshToken() => TokenHash = string.Empty;

    private RefreshToken(
        Guid id,
        string tokenHash,
        DateTimeOffset issuedOnUtc,
        DateTimeOffset expiresOnUtc,
        string? issuedToIpAddress) : base(id)
    {
        TokenHash = tokenHash;
        IssuedOnUtc = issuedOnUtc;
        ExpiresOnUtc = expiresOnUtc;
        IssuedToIpAddress = issuedToIpAddress;
    }

    /// <summary>Hash of the issued token. The token itself is never stored.</summary>
    public string TokenHash { get; private set; }

    /// <summary>When the token was issued.</summary>
    public DateTimeOffset IssuedOnUtc { get; private set; }

    /// <summary>When the token stops being accepted.</summary>
    public DateTimeOffset ExpiresOnUtc { get; private set; }

    /// <summary>When the token was revoked, if it was.</summary>
    public DateTimeOffset? RevokedOnUtc { get; private set; }

    /// <summary>IP address the token was issued to, for forensics.</summary>
    public string? IssuedToIpAddress { get; private set; }

    /// <summary>
    /// Hash of the token that superseded this one during rotation.
    /// </summary>
    /// <remarks>
    /// This link is what makes reuse detection possible. Presenting a rotated token identifies the
    /// exact point at which a session was cloned, and following the chain forward finds every
    /// descendant that must now be revoked.
    /// </remarks>
    public string? ReplacedByTokenHash { get; private set; }

    /// <summary>Why the token was revoked, for the audit trail.</summary>
    public string? RevocationReason { get; private set; }

    /// <summary>True once the expiry has passed.</summary>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresOnUtc;

    /// <summary>True when the token can still be exchanged.</summary>
    public bool IsActive(DateTimeOffset now) => RevokedOnUtc is null && !IsExpired(now);

    /// <summary>
    /// True when the token was rotated away rather than merely revoked.
    /// </summary>
    /// <remarks>
    /// Distinguishes the two reasons a token is inactive. A token replaced during normal rotation
    /// and then presented again indicates theft; a token revoked by an explicit sign-out does not.
    /// Treating both identically would raise a security alert every time a user logs out.
    /// </remarks>
    public bool WasRotated => ReplacedByTokenHash is not null;

    /// <summary>Issues a new refresh token.</summary>
    public static RefreshToken Issue(
        string tokenHash,
        DateTimeOffset issuedOnUtc,
        TimeSpan lifetime,
        string? issuedToIpAddress)
    {
        DomainException.RequireNotBlank(tokenHash, "RefreshToken.HashRequired", "A token hash is required.");

        DomainException.Require(
            lifetime > TimeSpan.Zero,
            "RefreshToken.InvalidLifetime",
            "A refresh token lifetime must be positive.");

        return new RefreshToken(
            Guid.CreateVersion7(),
            tokenHash,
            issuedOnUtc,
            issuedOnUtc.Add(lifetime),
            issuedToIpAddress);
    }

    /// <summary>Marks the token as rotated, recording its successor.</summary>
    internal void Rotate(string replacementTokenHash, DateTimeOffset now)
    {
        Revoke("Rotated", now);
        ReplacedByTokenHash = replacementTokenHash;
    }

    /// <summary>Revokes the token, if it is not already revoked.</summary>
    internal void Revoke(string reason, DateTimeOffset now)
    {
        // Idempotent. Revoking an entire chain visits tokens that may already be revoked, and
        // overwriting the original timestamp would destroy the forensic record of when a session
        // actually ended.
        if (RevokedOnUtc is not null)
        {
            return;
        }

        RevokedOnUtc = now;
        RevocationReason = reason;
    }
}
