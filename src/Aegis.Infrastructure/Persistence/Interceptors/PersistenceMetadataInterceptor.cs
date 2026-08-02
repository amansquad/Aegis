using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Application.Abstractions.Security;
using Aegis.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aegis.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps tenant ownership and audit fields, and converts physical deletes into logical ones,
/// immediately before every save.
/// </summary>
/// <remarks>
/// <para>
/// All three concerns live in one interceptor because they share a single reason to change: how
/// Aegis records persistence metadata. Splitting them would also introduce an ordering hazard,
/// since the soft-delete conversion must run before the audit stamp so that a logical delete is
/// recorded as a modification of the row it actually performs.
/// </para>
/// <para>
/// The value of putting this below the handler is that it cannot be bypassed by forgetting. A
/// developer writing <c>context.Assets.Add(asset)</c> gets tenant ownership, a creation timestamp
/// and an author without knowing any of it happens.
/// </para>
/// </remarks>
public sealed class PersistenceMetadataInterceptor(
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Apply(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Apply(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var userId = currentUser.Id;

        // ToList: the soft-delete conversion mutates entry state, and mutating entries while
        // enumerating the change tracker throws.
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            ApplySoftDelete(entry, userId, now);
            ApplyTenant(entry);
            ApplyAudit(entry, userId, now);
        }
    }

    /// <summary>
    /// Rewrites a delete into an update that sets the deletion flag.
    /// </summary>
    /// <remarks>
    /// Regulated operators are typically required to retain records of assets and interventions
    /// for years, and a work order deleted by mistake is evidence destroyed. Converting here means
    /// even a direct <c>Remove</c> call cannot physically delete the row.
    /// </remarks>
    private static void ApplySoftDelete(EntityEntry entry, Guid? userId, DateTimeOffset now)
    {
        if (entry.State != EntityState.Deleted || entry.Entity is not ISoftDeletable deletable)
        {
            return;
        }

        entry.State = EntityState.Modified;

        deletable.IsDeleted = true;
        deletable.DeletedOnUtc = now;
        deletable.DeletedBy = userId;
    }

    /// <summary>
    /// Assigns tenant ownership on insert, and refuses to let it change afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reassigning <c>OrganizationId</c> on an existing row would move data between tenants, which
    /// is never a legitimate outcome of an ordinary update. Resetting the property to its original
    /// value turns a would-be breach into a silently ignored no-op rather than trusting every
    /// future handler not to attempt it.
    /// </para>
    /// <para>
    /// Rows inserted with no tenant established keep <see cref="Guid.Empty"/>. Because the global
    /// query filter compares against the current tenant, such a row is invisible to every ordinary
    /// query, so the failure is contained rather than leaking across organizations.
    /// </para>
    /// </remarks>
    private void ApplyTenant(EntityEntry entry)
    {
        if (entry.Entity is not ITenantOwned)
        {
            return;
        }

        var property = entry.Property(nameof(ITenantOwned.OrganizationId));

        switch (entry.State)
        {
            case EntityState.Added:
                if (Equals(property.CurrentValue, Guid.Empty) && tenantContext.HasTenant)
                {
                    property.CurrentValue = tenantContext.OrganizationId;
                }

                break;

            case EntityState.Modified:
                if (property.IsModified)
                {
                    property.CurrentValue = property.OriginalValue;
                    property.IsModified = false;
                }

                break;

            default:
                break;
        }
    }

    private static void ApplyAudit(EntityEntry entry, Guid? userId, DateTimeOffset now)
    {
        if (entry.Entity is not IAuditableEntity auditable)
        {
            return;
        }

        switch (entry.State)
        {
            case EntityState.Added:
                auditable.CreatedOnUtc = now;
                auditable.CreatedBy = userId;
                break;

            case EntityState.Modified:
                auditable.ModifiedOnUtc = now;
                auditable.ModifiedBy = userId;

                // Creation metadata is immutable. Clearing the modified flags stops a client that
                // round-trips a full DTO from rewriting who created the record.
                entry.Property(nameof(IAuditableEntity.CreatedOnUtc)).IsModified = false;
                entry.Property(nameof(IAuditableEntity.CreatedBy)).IsModified = false;
                break;

            default:
                break;
        }
    }
}
