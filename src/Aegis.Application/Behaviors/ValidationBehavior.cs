using Aegis.Application.Common;
using Aegis.Domain.Common;
using FluentValidation;
using MediatR;

namespace Aegis.Application.Behaviors;

/// <summary>
/// Runs every registered <see cref="IValidator{T}"/> for the request and short-circuits with a
/// validation failure before the handler executes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Returns a failure; does not throw.</b> The conventional implementation throws a
/// <c>ValidationException</c> for middleware to catch, which means every invalid request (a
/// routine event on any public API) pays a stack unwind, and the handler's signature gives no
/// hint that validation can reject it. Returning <c>Result.Failure</c> keeps an expected outcome
/// on the expected path.
/// </para>
/// <para>
/// <b>All validators run, not just the first.</b> A form with four bad fields should report four
/// errors, not one per round trip.
/// </para>
/// <para>
/// A request with no registered validator passes straight through, so commands with no rules cost
/// nothing beyond an empty-collection check.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type, constrained to <see cref="Result"/>.</typeparam>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private readonly IValidator<TRequest>[] _validators = validators.ToArray();

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_validators.Length == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToArray();

        if (failures.Length == 0)
        {
            return await next();
        }

        var details = failures
            .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(f => f.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        return ResultFactory.Failure<TResponse>(Error.Validation(details));
    }
}
