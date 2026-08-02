namespace Aegis.Domain.Common;

/// <summary>
/// Thrown when a domain invariant is violated — a state the model guarantees can never occur.
/// </summary>
/// <remarks>
/// <para>
/// Note the deliberate division of labour with <see cref="Result"/>. A user submitting a work
/// order for a decommissioned asset is an <em>expected</em> rejection and returns
/// <c>Result.Failure</c>. Code constructing an <c>Asset</c> with a null name has bypassed
/// validation that should have run earlier, and that is a bug — it throws.
/// </para>
/// <para>
/// Put differently: <c>Result</c> is for things the user can do wrong; <c>DomainException</c> is
/// for things the programmer got wrong. If this type reaches production logs, the fix is a code
/// change, not a user-facing message.
/// </para>
/// </remarks>
public class DomainException : Exception
{
    /// <summary>Initialises the exception with a stable code and a message.</summary>
    public DomainException(string code, string message) : base(message) => Code = code;

    /// <summary>Initialises the exception with a message only.</summary>
    public DomainException(string message) : base(message) => Code = "Domain.InvariantViolated";

    /// <summary>Initialises the exception with a message and an inner exception.</summary>
    public DomainException(string message, Exception innerException)
        : base(message, innerException) => Code = "Domain.InvariantViolated";

    /// <summary>A stable, dot-delimited identifier for the violated invariant.</summary>
    public string Code { get; }

    /// <summary>Throws when <paramref name="condition"/> is false.</summary>
    public static void Require(bool condition, string code, string message)
    {
        if (!condition)
        {
            throw new DomainException(code, message);
        }
    }

    /// <summary>Throws when the supplied string is null, empty, or whitespace.</summary>
    public static string RequireNotBlank(string? value, string code, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainException(code, message) : value.Trim();
}
