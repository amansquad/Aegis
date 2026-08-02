using Aegis.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aegis.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="AuditTrailEntry"/> to the <c>AuditTrail</c> table.
/// </summary>
/// <remarks>
/// Mapping lives in a configuration class rather than in <c>OnModelCreating</c> so that each
/// module owns its own schema. When a module is eventually extracted into a service, its
/// configurations move with it as a unit.
/// </remarks>
internal sealed class AuditTrailEntryConfiguration : IEntityTypeConfiguration<AuditTrailEntry>
{
    public void Configure(EntityTypeBuilder<AuditTrailEntry> builder)
    {
        builder.ToTable("AuditTrail", "audit");

        builder.HasKey(e => e.Id);

        // Ids are UUIDv7 and therefore time-ordered, so a clustered index on the key appends
        // rather than fragmenting. On a write-heavy append-only table that is the difference
        // between sequential inserts and constant page splits.
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.OrganizationId).IsRequired();

        builder.Property(e => e.EntityName).HasMaxLength(128).IsRequired();
        builder.Property(e => e.EntityId).HasMaxLength(128).IsRequired();

        // Stored as its integer value. Persisting the enum name would make renaming a member a
        // silent data migration.
        builder.Property(e => e.Action).HasConversion<int>().IsRequired();

        builder.Property(e => e.OccurredOnUtc).IsRequired();

        builder.Property(e => e.UserEmail).HasMaxLength(256);
        builder.Property(e => e.CorrelationId).HasMaxLength(64);
        builder.Property(e => e.IpAddress).HasMaxLength(45); // IPv6 with an IPv4 tail.
        builder.Property(e => e.UserAgent).HasMaxLength(512);
        builder.Property(e => e.ChangedColumns).HasMaxLength(2048);

        // nvarchar(max): change payloads are unbounded by nature, and truncating one would make
        // the entry misleading rather than merely incomplete.
        builder.Property(e => e.OldValues).HasColumnType("nvarchar(max)");
        builder.Property(e => e.NewValues).HasColumnType("nvarchar(max)");

        // Serves the dominant query: "recent activity for this organization", newest first.
        builder
            .HasIndex(e => new { e.OrganizationId, e.OccurredOnUtc })
            .HasDatabaseName("IX_AuditTrail_Organization_OccurredOn")
            .IsDescending(false, true);

        // Serves "history of this specific record", the second most common question.
        builder
            .HasIndex(e => new { e.OrganizationId, e.EntityName, e.EntityId })
            .HasDatabaseName("IX_AuditTrail_Organization_Entity");

        // Serves "show me everything that happened during that one request", which is how an
        // incident investigation actually proceeds.
        builder
            .HasIndex(e => e.CorrelationId)
            .HasDatabaseName("IX_AuditTrail_CorrelationId")
            .HasFilter("[CorrelationId] IS NOT NULL");
    }
}
