namespace Aegis.Application.Abstractions.Security;

/// <summary>The outcome of verifying a supplied password against a stored hash.</summary>
public enum PasswordVerificationResult
{
    /// <summary>The password does not match.</summary>
    Failed = 0,

    /// <summary>The password matches.</summary>
    Success = 1,

    /// <summary>
    /// The password matches, but the stored hash uses parameters weaker than current policy.
    /// </summary>
    /// <remarks>
    /// The caller should rehash and persist while the plaintext is still in hand. This is the only
    /// moment it is available, and it is what allows the iteration count to be raised over time
    /// without forcing a password reset on every existing account at once.
    /// </remarks>
    SuccessRehashNeeded = 2,
}

/// <summary>
/// Derives and verifies password hashes.
/// </summary>
/// <remarks>
/// An adapter, not domain logic. Key derivation parameters are an operational decision that must
/// change as hardware improves, so isolating them here means raising the work factor — or moving
/// from PBKDF2 to Argon2id — touches one class and no business code.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Derives a self-describing hash from a plaintext password.</summary>
    /// <returns>An encoded string carrying its own algorithm, parameters and salt.</returns>
    string Hash(string password);

    /// <summary>Verifies a supplied password against a stored hash in constant time.</summary>
    PasswordVerificationResult Verify(string password, string encodedHash);
}
