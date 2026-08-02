using System.Text.RegularExpressions;
using Aegis.Domain.Common;

namespace Aegis.Domain.Identity.ValueObjects;

/// <summary>
/// A validated, normalised email address.
/// </summary>
/// <remarks>
/// <para>
/// Exists so that "is this a valid email?" is answered exactly once, at construction. Every
/// downstream consumer receives an instance that is provably well-formed, which is the whole
/// argument against passing addresses around as <see cref="string"/>.
/// </para>
/// <para>
/// <b>Normalisation matters more than validation here.</b> Addresses are compared for uniqueness
/// and used to look up accounts, and the domain part is case-insensitive by RFC 5321. Storing
/// <c>Alice@Utility.gov</c> and <c>alice@utility.gov</c> as distinct users creates two accounts one
/// person will use interchangeably, then wonder why their permissions vanished. Normalising to
/// lowercase on construction makes the unique index do the right thing without every query having
/// to remember a case-insensitive collation.
/// </para>
/// </remarks>
public sealed partial class EmailAddress : ValueObject
{
    /// <summary>Maximum length, matching the practical limit in RFC 5321.</summary>
    public const int MaxLength = 254;

    private EmailAddress(string value) => Value = value;

    /// <summary>The normalised address.</summary>
    public string Value { get; }

    /// <summary>The portion before the <c>@</c>.</summary>
    public string LocalPart => Value[..Value.IndexOf('@', StringComparison.Ordinal)];

    /// <summary>The portion after the <c>@</c>.</summary>
    public string Domain => Value[(Value.IndexOf('@', StringComparison.Ordinal) + 1)..];

    /// <summary>
    /// Creates an address, returning a failure rather than throwing for invalid input.
    /// </summary>
    /// <remarks>
    /// A user typing a malformed address is an expected outcome, not an exceptional one, so this
    /// returns <see cref="Result{TValue}"/> and the error travels back through validation.
    /// </remarks>
    public static Result<EmailAddress> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<EmailAddress>(
                Error.Validation("Email.Empty", "An email address is required."));
        }

        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length > MaxLength)
        {
            return Result.Failure<EmailAddress>(
                Error.Validation("Email.TooLong", $"An email address cannot exceed {MaxLength} characters."));
        }

        if (!EmailPattern().IsMatch(normalized))
        {
            return Result.Failure<EmailAddress>(
                Error.Validation("Email.Invalid", "The email address is not in a valid format."));
        }

        return Result.Success(new EmailAddress(normalized));
    }

    /// <summary>
    /// Rehydrates an address already validated on the way in, without re-running validation.
    /// </summary>
    /// <remarks>
    /// For EF Core materialisation only. A row in the database was validated when it was written,
    /// and re-validating on every read would mean a tightened rule silently makes existing accounts
    /// unloadable rather than merely un-creatable.
    /// </remarks>
    public static EmailAddress FromTrustedSource(string value) => new(value);

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>
    /// Pragmatic address pattern.
    /// </summary>
    /// <remarks>
    /// Deliberately not RFC 5322 compliant. The full grammar permits comments, quoted strings and
    /// nested parentheses, and a regex implementing it is both unreadable and a well-known
    /// catastrophic-backtracking risk. This accepts what real mail systems accept; the only
    /// authoritative check is sending a confirmation message, which registration does anyway.
    /// The 250 ms timeout is a belt-and-braces guard against pathological input.
    /// </remarks>
    [GeneratedRegex(
        @"^[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z]{2,}$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex EmailPattern();
}
