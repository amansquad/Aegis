using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Application.Abstractions.Security;
using Aegis.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Aegis.Application.Behaviors;

/// <summary>
/// Emits a structured log entry for the start and completion of every request.
/// </summary>
/// <remarks>
/// <para>
/// The outermost behaviour, so its scope encloses everything else. Every log line written anywhere
/// inside the request (by validation, by the handler, by EF Core) inherits the request name, user
/// and tenant from the pushed scope. That is what turns "an error occurred" into "an error
/// occurred handling <c>CreateWorkOrderCommand</c> for user X in organization Y", without any
/// individual log call having to repeat that context.
/// </para>
/// <para>
/// Failures are logged at <c>Warning</c>, not <c>Error</c>. A rejected command is the system
/// working correctly; reserving <c>Error</c> for genuine faults is what keeps an alert on the
/// error rate meaningful rather than something the on-call rota learns to ignore.
/// </para>
/// <para>
/// Note that the request object itself is never logged. Commands carry passwords, tokens and
/// personal data, and a log sink is a far easier target than a database.
/// </para>
/// </remarks>
public sealed class RequestLoggingBehavior<TRequest, TResponse>(
    ILogger<RequestLoggingBehavior<TRequest, TResponse>> logger,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestName"] = requestName,
            ["UserId"] = currentUser.Id,
            ["OrganizationId"] = tenantContext.OrganizationId,
        });

        logger.LogInformation("Handling {RequestName}", requestName);

        try
        {
            var response = await next();

            if (response.IsSuccess)
            {
                logger.LogInformation("Handled {RequestName} successfully", requestName);
            }
            else
            {
                logger.LogWarning(
                    "{RequestName} failed with {ErrorCode}: {ErrorMessage}",
                    requestName,
                    response.Error.Code,
                    response.Error.Message);
            }

            return response;
        }
        catch (Exception exception)
        {
            // Logged and rethrown. The global exception handler owns the HTTP response; this
            // behaviour exists only to attach the request scope before the stack unwinds past it.
            logger.LogError(
                exception,
                "{RequestName} threw an unhandled {ExceptionType}",
                requestName,
                exception.GetType().Name);

            throw;
        }
    }
}
