using Aegis.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aegis.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="WorkOrder"/>.</summary>
internal sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("WorkOrders", "workorders");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.OrganizationId).IsRequired();
        builder.Property(w => w.Reference).HasMaxLength(32).IsRequired();
        builder.Property(w => w.Title).HasMaxLength(200).IsRequired();
        builder.Property(w => w.Description).HasMaxLength(4000);
        builder.Property(w => w.CompletionNotes).HasMaxLength(2000);
        builder.Property(w => w.CancellationReason).HasMaxLength(500);

        builder.Property(w => w.Status).HasConversion<int>().IsRequired();
        builder.Property(w => w.Priority).HasConversion<int>().IsRequired();

        // The reference is quoted on paperwork and over the radio, so it must resolve to exactly
        // one work order. Filtered so a record removed in error does not reserve its reference.
        builder
            .HasIndex(w => new { w.OrganizationId, w.Reference })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_WorkOrders_Organization_Reference");

        // Serves the dispatch board: open work for an organization, newest first.
        builder
            .HasIndex(w => new { w.OrganizationId, w.Status, w.CreatedOnUtc })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_WorkOrders_Organization_Status_Created");

        // Serves a technician's own worklist.
        builder
            .HasIndex(w => new { w.OrganizationId, w.AssignedToUserId })
            .HasDatabaseName("IX_WorkOrders_Organization_AssignedTo")
            .HasFilter("[AssignedToUserId] IS NOT NULL");

        // Serves "what work has been done on this asset?", which feeds maintenance history.
        builder
            .HasIndex(w => w.AssetId)
            .HasDatabaseName("IX_WorkOrders_AssetId")
            .HasFilter("[AssetId] IS NOT NULL");

        // Serves the incident-to-work-order link used to close the loop on completion.
        builder
            .HasIndex(w => w.IncidentId)
            .HasDatabaseName("IX_WorkOrders_IncidentId")
            .HasFilter("[IncidentId] IS NOT NULL");

        // Serves the plan-to-work-order link used to close the loop on completion, and the
        // double-dispatch guard that checks for an already-open work order before generating.
        builder
            .HasIndex(w => new { w.MaintenancePlanId, w.Status })
            .HasDatabaseName("IX_WorkOrders_MaintenancePlanId_Status")
            .HasFilter("[MaintenancePlanId] IS NOT NULL");
    }
}
