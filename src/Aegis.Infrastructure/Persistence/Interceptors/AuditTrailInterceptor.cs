using System.Text.Json;
using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Application.Abstractions.Requests;
using Aegis.Application.Abstractions.Security;
using Aegis.Domain.Abstractions;
using Aegis.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aegis.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes an <see cref="AuditTrailEntry"/> for every insert, update and logical delete.
/// </summary>
/// <remarks>
/// <para>
/// Registered after <see cref="PersistenceMetadataInterceptor"/> so that it observes stamped
/// values and already-converted soft deletes. Because that conversion turns a delete into a
/// modification, a logical delete is recognised here by the transition of <c>IsDeleted</c> from
/// false to true rather than by entity state.
/// </para>
/// <para>
/// <b>Only tenant-owned entities are audited.</b> Framework and lookup tables produce noise
/// without answering any question an auditor asks.
/// </para>
/// <para>
/// <b>Property values are captured, not entity references.</b> Serialising the entity itself would
/// walk navigation properties into unbounded object graphs and would capture lazily-loaded state
/// as a side effect of auditing.
/// </para>
/// </remarks>
public sealed class AuditTrailInterceptor(
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IRequestContext requestContext,
    TimeProvider timeProvider) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Property names never written to the audit trail.
    /// </summary>
    /// <remarks>
    /// Two distinct reasons. Secrets (<c>PasswordHash</c>, <c>RefreshToken</c>) must never be
    /// copied into a second table, where they would survive a password reset and widen the blast
    /// radius of any read access to audit data. Bookkeeping columns (<c>Version</c>,
    /// <c>ModifiedOnUtc</c>) change on every write and would bury the fields that matter.
    /// </remarks>
    private static readonly HashSet<string> ExcludedProperties = new(StringComparer.Ordinal)
    {
        "PasswordHash",
        "PasswordSalt",
        "RefreshToken",
        "RefreshTokenHash",
        "SecurityStamp",
        "Version",
        "ModifiedOnUtc",
        "ModifiedBy",
    };

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Capture(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Capture(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void Capture(DbContext? context)
    {
        if (context is null || !tenantContext.HasTenant)
        {
            return;
        }

        var organizationId = tenantContext.OrganizationId!.Value;
        var now = timeProvider.GetUtcNow();

        var candidates = context.ChangeTracker
            .Entries()
            .Where(e => e.Entity is ITenantOwned)
            .Where(e => e.Entity is not AuditTrailEntry)
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();

        var entries = new List<AuditTrailEntry>(candidates.Count);

        foreach (var entry in candidates)
        {
            var audit = BuildEntry(entry, organizationId, now);

            if (audit is not null)
            {
                entries.Add(audit);
            }
        }

        // Added after the loop so that the newly added audit rows are not themselves enumerated.
        foreach (var entry in entries)
        {
            context.Add(entry);
        }
    }

    private AuditTrailEntry? BuildEntry(EntityEntry entry, Guid organizationId, DateTimeOffset now)
    {
        var action = ResolveAction(entry);

        var properties = entry.Properties
            .Where(p => !ExcludedProperties.Contains(p.Metadata.Name))
            .ToList();

        var changed = action == AuditAction.Created
            ? []
            : properties.Where(p => p.IsModified).ToList();

        // An update that touched nothing meaningful is not worth a row.
        if (action != AuditAction.Created && changed.Count == 0)
        {
            return null;
        }

        var oldValues = action == AuditAction.Created
            ? null
            : Serialize(changed.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue));

        var newValues = Serialize(
            (action == AuditAction.Created ? properties : changed)
            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));

        var changedColumns = action == AuditAction.Created
            ? null
            : string.Join(',', changed.Select(p => p.Metadata.Name));

        var entityId = string.Join(
            ',',
            entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).Select(p => p.CurrentValue));

        return AuditTrailEntry
            .Record(organizationId, entry.Entity.GetType().Name, entityId, action, now)
            .WithActor(currentUser.Id, currentUser.Email)
            .WithChanges(oldValues, newValues, changedColumns)
            .WithRequestContext(
                requestContext.CorrelationId,
                requestContext.IpAddress,
                requestContext.UserAgent);
    }

    /// <summary>
    /// Classifies the change, recognising a converted soft delete as a deletion rather than an
    /// update.
    /// </summary>
    private static AuditAction ResolveAction(EntityEntry entry)
    {
        if (entry.State == EntityState.Added)
        {
            return AuditAction.Created;
        }

        if (entry.Entity is not ISoftDeletable)
        {
            return AuditAction.Updated;
        }

        var deletedFlag = entry.Property(nameof(ISoftDeletable.IsDeleted));

        var justDeleted = deletedFlag.IsModified
            && deletedFlag.OriginalValue is false
            && deletedFlag.CurrentValue is true;

        return justDeleted ? AuditAction.Deleted : AuditAction.Updated;
    }

    private static string? Serialize(Dictionary<string, object?> values) =>
        values.Count == 0 ? null : JsonSerializer.Serialize(values, SerializerOptions);
}
