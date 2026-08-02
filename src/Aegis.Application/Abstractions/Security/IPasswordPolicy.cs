namespace Aegis.Application.Abstractions.Security;

/// <summary>The outcome of screening a proposed password.</summary>
/// <param name="IsAcceptable">True when the password may be used.</param>
/// <param name="Reason">Why it was rejected, phrased for the end user. Null on success.</param>
public readonly record struct PasswordScreeningResult(bool IsAcceptable, string? Reason)
{
    /// <summary>An accepted password.</summary>
    public static PasswordScreeningResult Accepted() => new(true, null);

    /// <summary>A rejected password, with a reason the user can act on.</summary>
    public static PasswordScreeningResult Rejected(string reason) => new(false, reason);
}

/// <summary>
/// Screens proposed passwords against the checks that actually reduce risk.
/// </summary>
/// <remarks>
/// <para>
/// Composition rules — one uppercase, one digit, one symbol — are deliberately absent. NIST SP
/// 800-63B withdrew that advice because it reliably produces <c>Password1!</c>: users satisfy the
/// checker rather than the intent, and the result is shorter and more predictable than the
/// passphrase they would otherwise have chosen. Length plus screening is what remains effective.
/// </para>
/// <para>
/// The checks here are the ones that map to how accounts are actually compromised: reuse of a
/// known-breached password (credential stuffing), and passwords derived from information an
/// attacker already has about the target.
/// </para>
/// </remarks>
public interface IPasswordPolicy
{
    /// <summary>Minimum acceptable length.</summary>
    int MinimumLength { get; }

    /// <summary>
    /// Screens a password, optionally against context an attacker would already know.
    /// </summary>
    /// <param name="password">The proposed password.</param>
    /// <param name="context">
    /// Values the attacker plausibly knows — the user's email, name, organization. A password
    /// derived from any of them is guessable by someone targeting this specific person, which is
    /// exactly the threat a strong-looking but personal password fails to address.
    /// </param>
    PasswordScreeningResult Screen(string password, params string?[] context);
}
