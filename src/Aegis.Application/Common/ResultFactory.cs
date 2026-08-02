using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Aegis.Domain.Common;

namespace Aegis.Application.Common;

/// <summary>
/// Constructs a failed <see cref="Result"/> or <see cref="Result{TValue}"/> when the concrete type
/// is known only as a generic parameter.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline behaviours are generic over <c>TResponse</c> and must be able to short-circuit — the
/// validation behaviour needs to return a failure without ever invoking the handler. It cannot
/// call <c>Result.Failure&lt;T&gt;</c> directly, because at compile time it only knows
/// <c>TResponse : Result</c>.
/// </para>
/// <para>
/// The reflection cost is paid once per closed response type: the <c>Failure&lt;T&gt;</c> call is
/// compiled into a delegate and cached, so the steady-state cost is a dictionary lookup and a
/// delegate invocation rather than a reflective call per request.
/// </para>
/// </remarks>
public static class ResultFactory
{
    private static readonly ConcurrentDictionary<Type, Func<Error, Result>> FailureFactories = new();
    private static readonly ConcurrentDictionary<Type, Func<object, Result>> SuccessFactories = new();

    private static readonly MethodInfo GenericFailureMethod = typeof(Result)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(m => m.Name == nameof(Result.Failure) && m.IsGenericMethodDefinition);

    private static readonly MethodInfo GenericSuccessMethod = typeof(Result)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(m => m.Name == nameof(Result.Success) && m.IsGenericMethodDefinition);

    /// <summary>
    /// Creates a failed result of the requested type.
    /// </summary>
    /// <typeparam name="TResult">Either <see cref="Result"/> or a closed <see cref="Result{TValue}"/>.</typeparam>
    /// <param name="error">The failure to carry.</param>
    public static TResult Failure<TResult>(Error error)
        where TResult : Result
    {
        var responseType = typeof(TResult);

        if (responseType == typeof(Result))
        {
            return (TResult)Result.Failure(error);
        }

        var factory = FailureFactories.GetOrAdd(responseType, static type =>
        {
            var valueType = type.GetGenericArguments()[0];
            var closedMethod = GenericFailureMethod.MakeGenericMethod(valueType);

            var errorParameter = Expression.Parameter(typeof(Error), "error");
            var call = Expression.Call(closedMethod, errorParameter);

            return Expression
                .Lambda<Func<Error, Result>>(Expression.Convert(call, typeof(Result)), errorParameter)
                .Compile();
        });

        return (TResult)factory(error);
    }

    /// <summary>
    /// Returns the value type of a closed <see cref="Result{TValue}"/>, or null for a plain
    /// <see cref="Result"/>.
    /// </summary>
    /// <remarks>
    /// Used by the caching behaviour to work out what type to deserialise a cached entry into.
    /// </remarks>
    public static Type? GetValueType(Type resultType) =>
        resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Result<>)
            ? resultType.GetGenericArguments()[0]
            : null;

    /// <summary>
    /// Creates a successful result of the requested type wrapping <paramref name="value"/>.
    /// </summary>
    /// <typeparam name="TResult">A closed <see cref="Result{TValue}"/>.</typeparam>
    /// <param name="value">The value to wrap. Must be assignable to the result's value type.</param>
    public static TResult Success<TResult>(object value)
        where TResult : Result
    {
        var factory = SuccessFactories.GetOrAdd(typeof(TResult), static type =>
        {
            var valueType = GetValueType(type)
                ?? throw new InvalidOperationException(
                    $"{type.Name} is not a Result<T> and cannot carry a value.");

            var closedMethod = GenericSuccessMethod.MakeGenericMethod(valueType);

            var valueParameter = Expression.Parameter(typeof(object), "value");
            var call = Expression.Call(closedMethod, Expression.Convert(valueParameter, valueType));

            return Expression
                .Lambda<Func<object, Result>>(Expression.Convert(call, typeof(Result)), valueParameter)
                .Compile();
        });

        return (TResult)factory(value);
    }
}
