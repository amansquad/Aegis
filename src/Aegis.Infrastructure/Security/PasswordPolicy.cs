using Aegis.Application.Abstractions.Security;

namespace Aegis.Infrastructure.Security;

/// <summary>
/// Screens passwords for length, known-weak values, and derivation from known context.
/// </summary>
/// <remarks>
/// <para>
/// <b>Honest limitation, stated rather than implied.</b> The banned list below is a few hundred
/// entries. Real credential-stuffing lists run to hundreds of millions, and the effective control
/// is checking against one — via the Have I Been Pwned range API, which uses k-anonymity so only
/// the first five characters of the SHA-1 hash leave the process, or via a locally hosted copy of
/// the corpus for deployments that permit no outbound calls.
/// </para>
/// <para>
/// That is the intended end state and belongs behind this same interface. This implementation
/// catches the passwords that appear at the very top of every breach corpus plus the ones derived
/// from the user's own details, which together are a meaningful fraction of real-world weak
/// choices. It is a floor, not the finished control, and it is documented as such so nobody
/// mistakes a green test for adequate coverage.
/// </para>
/// </remarks>
public sealed class PasswordPolicy : IPasswordPolicy
{
    /// <inheritdoc />
    public int MinimumLength => 12;

    /// <summary>Maximum accepted length.</summary>
    /// <remarks>
    /// Bounded because PBKDF2 cost scales with input length, so an unbounded password is a cheap
    /// way to make the server do expensive work. Generous enough that no genuine passphrase is
    /// affected.
    /// </remarks>
    private const int MaximumLength = 256;

    /// <summary>
    /// Passwords that appear at the top of essentially every breach corpus, plus the
    /// sector-specific ones a utility deployment attracts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stored lowercase and compared case-insensitively: <c>Password123</c> is not meaningfully
    /// stronger than <c>password123</c>, and treating them differently would let a trivial
    /// capitalisation defeat the whole list.
    /// </para>
    /// <para>
    /// <b>The entries that matter are the long ones.</b> Writing the first version of this list
    /// surfaced the point: with a twelve-character minimum, every shorter entry is already rejected
    /// by the length rule and contributes nothing. A banned list mostly full of six- and
    /// eight-character classics looks thorough and screens almost nothing. The short entries are
    /// retained so the list stays correct if the minimum is ever lowered, but the ones doing real
    /// work are the twelve-plus character passwords and passphrases below — which is exactly where
    /// modern breach corpora concentrate, now that length requirements are common.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> BannedPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "password1234", "password12345",
        "passw0rd", "p@ssword", "p@ssw0rd", "passw0rd123", "p@ssw0rd123",
        "123456", "1234567", "12345678", "123456789", "1234567890", "12345678910",
        "qwerty", "qwerty123", "qwertyuiop", "1qaz2wsx", "zaq12wsx", "qazwsxedc",
        "letmein", "letmein123", "welcome", "welcome1", "welcome123", "welcome2024", "welcome2025",
        "admin", "admin123", "administrator", "root", "toor", "guest", "default", "changeme",
        "iloveyou", "monkey", "dragon", "sunshine", "princess", "football", "baseball",
        "abc123", "abcd1234", "aaaaaaaa", "11111111", "00000000", "asdfghjkl",
        "trustno1", "superman", "batman", "master", "shadow", "michael", "jennifer",
        "correcthorsebatterystaple", "correct-horse-battery-staple",

        // Sector-specific. Infrastructure operators inherit a long tradition of shared operational
        // credentials, and these turn up in exactly this kind of deployment.
        "water123", "utility123", "scada123", "operator", "operator123", "control123",
        "maintenance", "maintenance1", "engineer1", "technician1", "supervisor1",
        "aegis", "aegis123", "aegis2025", "infrastructure",

        // Twelve characters and longer: the entries that actually do work, since anything shorter
        // is already refused by the length rule.
        "password1234!", "password@123", "passw0rd1234", "p@ssword1234", "p@ssw0rd1234",
        "qwerty123456", "qwertyuiop123", "1qaz2wsx3edc", "1q2w3e4r5t6y", "zaqxswcdevfr",
        "iloveyou1234", "welcome123456", "letmein123456", "trustno1234567",
        "administrator1", "administrator123", "supervisor123", "operator1234",
        "changeme1234", "changemenow123", "defaultpassword", "temporarypass1",
        "123456789012", "1234567890123", "111111111111", "000000000000",
        "abcdefghijkl", "abcd1234efgh", "asdfghjkl1234",
        "monkeybusiness1", "superman1234", "dragonfly1234", "sunshine1234",
        "footballfan123", "baseball12345", "princess12345",
        "correcthorsebatterystaple", "correct horse battery staple",
        "thequickbrownfox", "letmeinplease123", "iamthebestuser1",
        "companyname123", "welcometothejungle", "nopasswordhere",

        // Sector-specific and long. Operators inherit a long tradition of shared credentials, and
        // these are the shapes that survive a length policy.
        "watertreatment1", "wastewater1234", "pumpstation123", "substation1234",
        "scadaoperator1", "scadapassword1", "controlroom123", "maintenance123",
        "engineering123", "technician123", "fieldworker123", "utilityadmin1",
        "infrastructure1", "aegisplatform1", "aegisadmin123",
    };

    /// <inheritdoc />
    public PasswordScreeningResult Screen(string password, params string?[] context)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return PasswordScreeningResult.Rejected("A password is required.");
        }

        if (password.Length < MinimumLength)
        {
            return PasswordScreeningResult.Rejected(
                $"A password must be at least {MinimumLength} characters. A memorable phrase of " +
                "several words is both stronger and easier to type than a short complex string.");
        }

        if (password.Length > MaximumLength)
        {
            return PasswordScreeningResult.Rejected(
                $"A password cannot exceed {MaximumLength} characters.");
        }

        if (BannedPasswords.Contains(password))
        {
            return PasswordScreeningResult.Rejected(
                "This password appears in published lists of breached passwords and would be " +
                "guessed almost immediately. Choose something else.");
        }

        // Strips separators before re-checking, so "p-a-s-s-w-o-r-d-1-2-3" does not slip past a
        // list that only contains the unseparated form.
        var condensed = new string(password.Where(char.IsLetterOrDigit).ToArray());

        if (condensed.Length > 0 && BannedPasswords.Contains(condensed))
        {
            return PasswordScreeningResult.Rejected(
                "This password is a lightly disguised version of a commonly breached password. " +
                "Choose something else.");
        }

        if (IsSingleRepeatedCharacter(password))
        {
            return PasswordScreeningResult.Rejected(
                "A password cannot be a single repeated character.");
        }

        foreach (var value in context)
        {
            if (ContainsContext(password, value))
            {
                return PasswordScreeningResult.Rejected(
                    "A password must not contain your name, email address or organization name. " +
                    "Anyone targeting you already knows those.");
            }
        }

        return PasswordScreeningResult.Accepted();
    }

    private static bool IsSingleRepeatedCharacter(string password) =>
        password.Distinct().Count() == 1;

    /// <summary>
    /// Determines whether a password embeds a meaningful fragment of known context.
    /// </summary>
    /// <remarks>
    /// Fragments shorter than four characters are ignored. Rejecting them would fail a password for
    /// containing a common short word that happens to appear in an organization's name, which
    /// frustrates users without denying an attacker anything.
    /// </remarks>
    private static bool ContainsContext(string password, string? context)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return false;
        }

        // An email address is split so that "ada.osei@northern-water.gov" screens its local part
        // and its domain separately, rather than only matching the whole address nobody would type.
        var fragments = context
            .Split(['@', '.', '-', '_', ' ', '+'], StringSplitOptions.RemoveEmptyEntries)
            .Where(fragment => fragment.Length >= 4);

        return fragments.Any(fragment =>
            password.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}
