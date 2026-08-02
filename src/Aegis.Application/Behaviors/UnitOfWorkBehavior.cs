using System.Collections.Concurrent;
using Aegis.Application.Abstractions.Events;
using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aegis.Application.Behaviors;

/// <summary>
/// Wraps every command in a database transaction, committing on success and rolling back on
/// failure.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a behaviour and not the handler.</b> A command that writes two aggregates, such as
/// closing an incident and creating the work order that resolves it, must do so atomically. Left
/// to handlers, that means every author remembering to open a transaction, and the one who forgets
/// leaves the database able to reach a state the domain says is impossible.
/// </para>
/// <para>
/// <b>Rollback on a failed Result, not only on an exception.</b> A handler that writes a row and
/// then returns <c>Result.Failure</c> has told the caller nothing happened. Committing anyway
/// would make that a lie.
/// </para>
/// <para>
/// <b>Execution strategy.</b> The transaction runs inside EF Core's retrying execution strategy.
/// Azure SQL closes idle and throttled connections routinely, and a user-initiated transaction
/// combined with connection resiliency throws unless the whole unit is retriable as a block: the
/// well-known "configured execution strategy does not support user-initiated transactions" error.
/// Retries are safe here because the entire transaction is replayed, not a fragment of it.
/// </para>
/// <para>
/// Queries skip this behaviour entirely. A read does not need an explicit transaction, and EF
/// Core's default read-committed behaviour is sufficient.
/// </para>
/// </remarks>
public sealed class UnitOfWorkBehavior<TRequest, TResponse>(
    IAegisDbContext context,
    IDomainEventCollector eventCollector,
    IDomainEventDispatcher eventDispatcher,
    ILogger<UnitOfWorkBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private static readonly ConcurrentDictionary<Type, bool> TransactionalCache = new();

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!RequiresTransaction(typeof(TRequest)))
        {
            var passthrough = await next();
            await DispatchCollectedEventsAsync(cancellationToken);
            return passthrough;
        }

        // An outer transaction already exists, meaning this command was dispatched from another
        // command's handler. Joining it rather than nesting keeps the whole operation atomic, and
        // the outermost behaviour remains responsible for dispatching the collected events.
        if (context.Database.CurrentTransaction is not null)
        {
            return await next();
        }

        var strategy = context.Database.CreateExecutionStrategy();

        var response = await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);

            var inner = await next();

            if (inner.IsFailure)
            {
                await transaction.RollbackAsync(ct);

                logger.LogDebug(
                    "Rolled back {RequestName} after {ErrorCode}",
                    typeof(TRequest).Name,
                    inner.Error.Code);

                // Discard events raised before the failure. They describe changes that have just
                // been rolled back, so acting on them would announce something that never happened.
                eventCollector.Drain();

                return inner;
            }

            await transaction.CommitAsync(ct);

            return inner;
        }, cancellationToken);

        // Dispatched only now, outside and after the committed transaction. Handlers reacting to
        // these events observe data that is durably written, and their own side effects cannot be
        // undone by a rollback that has already been decided.
        if (response.IsSuccess)
        {
            await DispatchCollectedEventsAsync(cancellationToken);
        }

        return response;
    }

    private async Task DispatchCollectedEventsAsync(CancellationToken cancellationToken)
    {
        if (!eventCollector.HasPendingEvents)
        {
            return;
        }

        var events = eventCollector.Drain();

        logger.LogDebug(
            "Dispatching {EventCount} domain event(s) raised by {RequestName}",
            events.Count,
            typeof(TRequest).Name);

        await eventDispatcher.DispatchAsync(events, cancellationToken);
    }

    /// <summary>
    /// Determines whether the request is a command that has not opted out of transactions.
    /// </summary>
    /// <remarks>
    /// The result is cached per request type. The interface walk is pure reflection over a type
    /// that cannot change at runtime, so paying for it on every request would be waste.
    /// </remarks>
    private static bool RequiresTransaction(Type requestType) =>
        TransactionalCache.GetOrAdd(requestType, static type =>
        {
            if (typeof(ITransactionless).IsAssignableFrom(type))
            {
                return false;
            }

            if (typeof(ICommand).IsAssignableFrom(type))
            {
                return true;
            }

            return Array.Exists(
                type.GetInterfaces(),
                i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
        });
}
