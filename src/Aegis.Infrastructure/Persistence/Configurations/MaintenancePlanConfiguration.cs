using Aegis.Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aegis.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="MaintenancePlan"/>.</summary>
internal sealed class MaintenancePlanConfiguration : IEntityTypeConfiguration<MaintenancePlan>
{
    public void Configure(EntityTypeBuilder<MaintenancePlan> builder)
    {
        builder.ToTable("MaintenancePlans", "maintenance");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.OrganizationId).IsRequired();
        builder.Property(p => p.Reference).HasMaxLength(32).IsRequired();
        builder.Property(p => p.AssetId).IsRequired();
        builder.Property(p => p.Title).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(4000);
        builder.Property(p => p.FrequencyDays).IsRequired();

        // The reference is quoted on the maintenance schedule, so it must resolve to exactly one
        // plan. Filtered so a plan removed in error does not reserve its reference.
        builder
            .HasIndex(p => new { p.OrganizationId, p.Reference })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_MaintenancePlans_Organization_Reference");

        // Serves the maintenance schedule: active, due-soonest-first, for an organization.
        builder
            .HasIndex(p => new { p.OrganizationId, p.IsActive, p.NextDueOnUtc })
            .HasDatabaseName("IX_MaintenancePlans_Organization_Active_NextDue");

        // Serves "what is scheduled for this asset?", surfaced from the asset's own detail view.
        builder
            .HasIndex(p => p.AssetId)
            .HasDatabaseName("IX_MaintenancePlans_AssetId");
    }
}
