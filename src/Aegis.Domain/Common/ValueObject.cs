namespace Aegis.Domain.Common;

/// <summary>
/// Base class for value objects — concepts defined entirely by their attributes, with no identity
/// and no independent lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// A <c>GeoCoordinate</c> of 51.5074°N, 0.1278°W is that coordinate; there is no "which one" to
/// ask. Contrast an <c>Asset</c>, which remains the same asset after it is relocated, renamed and
/// refurbished. Getting this distinction right is what stops a domain model from degenerating into
/// a bag of primitives.
/// </para>
/// <para>
/// Value objects in Aegis carry validation with them: <c>GeoCoordinate</c> rejects a latitude of
/// 91, <c>EmailAddress</c> rejects a malformed address. Once constructed, an instance is provably
/// valid, so no downstream code needs a defensive check. This is the Primitive Obsession fix —
/// and it is the difference between validating latitude once and validating it in fourteen places.
/// </para>
/// <para>
/// Instances are immutable. A "change" produces a new instance.
/// </para>
/// </remarks>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>
    /// Yields the components that define this value's identity, in a stable order.
    /// </summary>
    /// <remarks>
    /// Include every field that participates in equality and nothing else. A field omitted here
    /// makes two materially different values compare equal; a volatile field included here makes
    /// two identical values compare unequal.
    /// </remarks>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    /// <inheritdoc />
    public bool Equals(ValueObject? other)
    {
        if (other is null || other.GetType() != GetType())
        {
            return false;
        }

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);

        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
