using Aegis.Domain.Common;

namespace Aegis.Domain.Identity.ValueObjects;

/// <summary>
/// An opaque, already-hashed password credential.
/// </summary>
/// <remarks>
/// <para>
/// <b>The domain deliberately cannot hash or verify a password.</b> Key derivation needs a crypto
/// library, and <c>Aegis.Domain</c> has no package references at all. More usefully, the algorithm
/// is an operational decision that changes over time — iteration counts rise with hardware, and
/// PBKDF2 should eventually become Argon2id — so it belongs in an adapter behind
/// <c>IPasswordHasher</c> where it can change without touching the model.
/// </para>
/// <para>
/// The encoded value carries its own algorithm and parameters, so a stored hash always describes
/// how to verify itself. That is what makes rehashing on login possible: the verifier can detect a
/// credential produced under weaker parameters and transparently upgrade it while the user's plain
/// password is momentarily in hand. Without self-describing hashes, changing parameters means
/// forcing a password reset on every user at once.
/// </para>
/// <para>
/// The type exists rather than a bare <see cref="string"/> so that a hash cannot be assigned where
/// a display name is expected, and so that <see cref="ToString"/> can refuse to reveal it.
/// </para>
/// </remarks>
public sealed class PasswordHash : ValueObject
{
    private PasswordHash(string value) => Value = value;

    /// <summary>The encoded hash, including its algorithm identifier and parameters.</summary>
    public string Value { get; }

    /// <summary>Wraps an encoded hash produced by the password hasher.</summary>
    /// <exception cref="DomainException">The value is blank.</exception>
    public static PasswordHash FromEncoded(string? encoded) =>
        new(DomainException.RequireNotBlank(
            encoded,
            "PasswordHash.Empty",
            "A password hash cannot be empty. This indicates the hasher failed rather than that " +
            "the user supplied a bad password."));

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <summary>Returns a redacted placeholder.</summary>
    /// <remarks>
    /// Overridden precisely so that an accidental interpolation into a log line, an exception
    /// message or a debugger watch does not write the credential somewhere it will outlive the
    /// request. Logs are copied, shipped and retained far more casually than databases are.
    /// </remarks>
    public override string ToString() => "[REDACTED]";
}
