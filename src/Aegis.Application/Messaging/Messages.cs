using Aegis.Domain.Common;
using MediatR;

namespace Aegis.Application.Messaging;

/// <summary>A request that changes state and returns no value.</summary>
/// <remarks>
/// <para>
/// Commands and queries are separated at the type level rather than by naming convention, because
/// pipeline behaviours need to tell them apart. Only commands are wrapped in a transaction; only
/// queries are eligible for caching. With a single <c>IRequest</c> marker those behaviours would
/// have to guess from the class name.
/// </para>
/// <para>
/// Every command returns <see cref="Result"/> rather than <c>void</c> or a raw value, so expected
/// business failures travel back through the normal return path instead of as exceptions.
/// </para>
/// </remarks>
public interface ICommand : IRequest<Result>;

/// <summary>A request that changes state and returns a value.</summary>
/// <typeparam name="TResponse">The value produced on success.</typeparam>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

/// <summary>A read-only request.</summary>
/// <typeparam name="TResponse">The value produced on success.</typeparam>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

/// <summary>Handles a <see cref="ICommand"/>.</summary>
/// <typeparam name="TCommand">The command type.</typeparam>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

/// <summary>Handles a <see cref="ICommand{TResponse}"/>.</summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResponse">The value produced on success.</typeparam>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

/// <summary>Handles an <see cref="IQuery{TResponse}"/>.</summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResponse">The value produced on success.</typeparam>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;

/// <summary>
/// Opts a query into distributed caching by the caching pipeline behaviour.
/// </summary>
/// <remarks>
/// Opt-in rather than opt-out, and only ever applied to queries. Caching by default would
/// eventually cache something that must not be cached — a permission check, a live gauge reading —
/// and that class of bug is unusually hard to reproduce, because it depends on request ordering.
/// </remarks>
public interface ICacheableQuery
{
    /// <summary>
    /// Cache key for this specific request, excluding the tenant prefix, which the cache adapter
    /// adds. Must incorporate every parameter that affects the result — a key that ignores the
    /// page number serves page 1 for every page.
    /// </summary>
    string CacheKey { get; }

    /// <summary>How long the entry stays valid. Null uses the configured default.</summary>
    TimeSpan? Expiration { get; }
}

/// <summary>
/// Opts a command out of the automatic ambient transaction.
/// </summary>
/// <remarks>
/// Needed by the rare command whose work cannot sit inside a transaction — for example one that
/// calls a long-running external service, where holding a SQL transaction open for the duration
/// would escalate locks and stall unrelated writers.
/// </remarks>
public interface ITransactionless;
