using System.Diagnostics.CodeAnalysis;

namespace Aegis.Domain.Common;

/// <summary>
/// The outcome of an operation that is expected to fail some of the time.
/// </summary>
/// <remarks>
/// <para>
/// "Asset not found", "serial number already registered" and "work order already closed" are
/// ordinary business outcomes, not exceptional conditions. Modelling them as exceptions costs a
/// stack-unwind on a routine path, hides the failure mode from the method signature, and pushes
/// control flow into <c>catch</c> blocks far from the decision.
/// </para>
/// <para>
/// A method returning <c>Result&lt;Asset&gt;</c> declares in its signature that failure is part of
/// its contract, and the compiler will not let the caller reach <c>Value</c> without acknowledging
/// it. Exceptions remain reserved for genuine faults — a dropped SQL connection, a bug — which are
/// caught once by the global exception middleware.
/// </para>
/// </remarks>
public class Result
{
    /// <summary>Initialises a result. Use the static factory methods instead.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the success flag and error are inconsistent — a successful result carrying an
    /// error, or a failed result carrying none. This is a programming error, so it throws.
    /// </exception>
    protected Result(bool isSuccess, Error error)
    {
        switch (isSuccess)
        {
            case true when error != Error.None:
                throw new InvalidOperationException("A successful result cannot carry an error.");
            case false when error == Error.None:
                throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>The failure description, or <see cref="Common.Error.None"/> on success.</summary>
    public Error Error { get; }

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>Creates a failed result.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Creates a successful result carrying a value.</summary>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    /// <summary>Creates a failed result of the given value type.</summary>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);

    /// <summary>
    /// Returns a successful result if <paramref name="value"/> is non-null, otherwise the supplied
    /// error. Removes the repetitive null-check-then-return-NotFound block from query handlers.
    /// </summary>
    public static Result<TValue> Ensure<TValue>(TValue? value, Error error)
        where TValue : class =>
        value is not null ? Success(value) : Failure<TValue>(error);
}

/// <summary>
/// The outcome of an operation that produces a value when it succeeds.
/// </summary>
/// <typeparam name="TValue">The type produced on success.</typeparam>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    /// <summary>Initialises a value result. Use the static factory methods on <see cref="Result"/>.</summary>
    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>The produced value.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the result is a failure.</exception>
    [NotNull]
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Cannot access {nameof(Value)} of a failed result. Error: {Error.Code}.");

    /// <summary>
    /// Implicitly lifts a value into a successful result, so handlers can <c>return asset;</c>
    /// rather than <c>return Result.Success(asset);</c>. A null value yields a failure, which
    /// prevents an accidental <c>return null!</c> from producing a "successful" empty result.
    /// </summary>
    public static implicit operator Result<TValue>(TValue? value) => value is not null
        ? Success(value)
        : Failure<TValue>(new Error("Value.Null", "A null value was supplied.", ErrorType.Failure));
}
