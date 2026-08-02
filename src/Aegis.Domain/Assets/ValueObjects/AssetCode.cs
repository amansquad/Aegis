using System.Text.RegularExpressions;
using Aegis.Domain.Common;

namespace Aegis.Domain.Assets.ValueObjects;

/// <summary>
/// The human-facing identifier an operator uses for an asset, such as <c>PMP-NW-0431</c>.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from the primary key, and both are needed. The <see cref="Guid"/> is what the system
/// uses; this is what is stencilled on the pump, written on the work order, and spoken over the
/// radio. Asking a field technician to read out a GUID is not a workable design.
/// </para>
/// <para>
/// Normalised to upper case because operators type these by hand, from memory, on a phone in the
/// rain. <c>pmp-nw-0431</c> and <c>PMP-NW-0431</c> are the same asset, and a system that disagrees
/// creates a duplicate record for one physical pump.
/// </para>
/// </remarks>
public sealed partial class AssetCode : ValueObject
{
    /// <summary>Maximum length.</summary>
    public const int MaxLength = 50;

    private AssetCode(string value) => Value = value;

    /// <summary>The normalised code.</summary>
    public string Value { get; }

    /// <summary>Creates a code, rejecting values that cannot be read back reliably.</summary>
    public static Result<AssetCode> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<AssetCode>(Error.Validation(
                "AssetCode.Empty",
                "An asset code is required."));
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length > MaxLength)
        {
            return Result.Failure<AssetCode>(Error.Validation(
                "AssetCode.TooLong",
                $"An asset code cannot exceed {MaxLength} characters."));
        }

        // Letters, digits, hyphens, underscores and slashes only. Excludes whitespace and
        // punctuation that survive a copy-paste from a spreadsheet and then make two visually
        // identical codes compare unequal.
        if (!CodePattern().IsMatch(normalized))
        {
            return Result.Failure<AssetCode>(Error.Validation(
                "AssetCode.InvalidCharacters",
                "An asset code may contain only letters, digits, hyphens, underscores and slashes."));
        }

        return Result.Success(new AssetCode(normalized));
    }

    /// <summary>Rehydrates a code already validated on the way in.</summary>
    public static AssetCode FromTrustedSource(string value) => new(value);

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    [GeneratedRegex(@"^[A-Z0-9\-_/]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex CodePattern();
}
