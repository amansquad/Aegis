using System.Globalization;
using System.Security.Cryptography;
using Aegis.Application.Abstractions.Security;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace Aegis.Infrastructure.Security.Hashing;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing with self-describing, upgradeable output.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why PBKDF2 rather than Argon2id.</b> OWASP prefers Argon2id, and this is a considered
/// trade-off rather than an oversight. PBKDF2-HMAC-SHA256 at 600,000 iterations is an explicitly
/// acceptable OWASP configuration, it is FIPS-140 approved (which matters for government and
/// utility procurement, the exact buyers this platform targets), and it needs no third-party
/// dependency in the authentication path. Argon2id's memory-hardness is genuinely better against
/// GPU cracking; when that outweighs the above, <see cref="IPasswordHasher"/> is the single class
/// that changes, and the encoding below already carries the algorithm name to make both coexist.
/// </para>
/// <para>
/// <b>Encoding.</b> <c>PBKDF2-SHA256$iterations$base64salt$base64hash</c>. Self-describing on
/// purpose: a stored credential always states how to verify itself, so parameters can be raised
/// for new and returning users without invalidating everyone else's password at once. A bare hash
/// column with parameters held in configuration cannot do that — changing the config silently
/// breaks every existing credential.
/// </para>
/// </remarks>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    /// <summary>Algorithm identifier written into the encoded hash.</summary>
    private const string AlgorithmName = "PBKDF2-SHA256";

    /// <summary>
    /// Current iteration count, per OWASP guidance for PBKDF2-HMAC-SHA256.
    /// </summary>
    /// <remarks>
    /// Raise this over time. Existing credentials keep verifying against their own recorded count
    /// and are upgraded on next sign-in, which is what <see cref="PasswordVerificationResult.SuccessRehashNeeded"/>
    /// exists to signal.
    /// </remarks>
    private const int CurrentIterations = 600_000;

    /// <summary>Salt length in bytes.</summary>
    /// <remarks>
    /// 128 bits, per NIST SP 800-132. A per-password random salt is what makes precomputed rainbow
    /// tables useless and forces an attacker to attack each credential separately.
    /// </remarks>
    private const int SaltSizeBytes = 16;

    /// <summary>Derived key length in bytes.</summary>
    private const int KeySizeBytes = 32;

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);

        var key = KeyDerivation.Pbkdf2(
            password,
            salt,
            KeyDerivationPrf.HMACSHA256,
            CurrentIterations,
            KeySizeBytes);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{AlgorithmName}${CurrentIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}");
    }

    /// <inheritdoc />
    public PasswordVerificationResult Verify(string password, string encodedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(encodedHash))
        {
            return PasswordVerificationResult.Failed;
        }

        // A malformed stored hash returns Failed rather than throwing. A corrupt row must not turn
        // a sign-in attempt into a 500 that reveals the row is corrupt.
        if (!TryParse(encodedHash, out var iterations, out var salt, out var expectedKey))
        {
            return PasswordVerificationResult.Failed;
        }

        var actualKey = KeyDerivation.Pbkdf2(
            password,
            salt,
            KeyDerivationPrf.HMACSHA256,
            iterations,
            expectedKey.Length);

        // Fixed-time comparison. A short-circuiting comparison leaks how many leading bytes matched,
        // which is enough to reconstruct the digest one byte at a time given enough attempts.
        if (!CryptographicOperations.FixedTimeEquals(actualKey, expectedKey))
        {
            return PasswordVerificationResult.Failed;
        }

        return iterations < CurrentIterations
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }

    private static bool TryParse(
        string encoded,
        out int iterations,
        out byte[] salt,
        out byte[] key)
    {
        iterations = 0;
        salt = [];
        key = [];

        var parts = encoded.Split('$');

        if (parts.Length != 4 || !string.Equals(parts[0], AlgorithmName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out iterations)
            || iterations <= 0)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            key = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length > 0 && key.Length > 0;
    }
}
