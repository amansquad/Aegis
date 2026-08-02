using Aegis.Application.Abstractions.Events;
using Aegis.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aegis.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Harvests domain events from tracked aggregates at save time and parks them for dispatch after
/// the transaction commits.
/// </summary>
/// <remarks>
/// <para>
/// This interceptor deliberately does <em>not</em> dispatch. <c>SaveChangesAsync</c> is called by
/// the handler, but the commit happens later in
/// <see cref="Application.Behaviors.UnitOfWorkBehavior{TRequest,TResponse}"/>, so anything
/// dispatched from <c>SavedChangesAsync</c> would run inside an uncommitted transaction. A
/// notification sent, a cache invalidated or an email queued for a change that then rolls back is
/// worse than a late notification.
/// </para>
/// <para>
/// Events are cleared from their entities as they are collected. EF Core keeps entities tracked
/// for the lifetime of the scoped context, so an aggregate saved twice in one request would
/// otherwise replay its first batch of events on the second save.
/// </para>
/// <para>
/// Collection happens in <c>SavingChanges</c>, before the write, because that is the last point at
/// which the raising entities are guaranteed to still be in the change tracker.
/// </para>
/// </remarks>
public sealed class DomainEventCollectionInterceptor(IDomainEventCollector collector)
    : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Harvest(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Harvest(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Harvest(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var roots = context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        if (roots.Count == 0)
        {
            return;
        }

        var events = roots.SelectMany(r => r.DomainEvents).ToList();

        foreach (var root in roots)
        {
            root.ClearDomainEvents();
        }

        collector.Collect(events);
    }
}
