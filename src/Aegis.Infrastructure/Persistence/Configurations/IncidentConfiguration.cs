using Aegis.Domain.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aegis.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Incident"/>.</summary>
internal sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("Incidents", "incidents");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.OrganizationId).IsRequired();

        builder.Property(i => i.Reference).HasMaxLength(32).IsRequired();

        // nvarchar(max). The reporter's words are kept exactly as submitted, and truncating them
        // would make the record misleading in precisely the situations it matters most.
        builder.Property(i => i.ReportText).HasColumnType("nvarchar(max)").IsRequired();

        builder.Property(i => i.Summary).HasMaxLength(500).IsRequired();
        builder.Property(i => i.LocationHint).HasMaxLength(300);
        builder.Property(i => i.ResolutionNotes).HasMaxLength(2000);

        builder.Property(i => i.ReporterName).HasMaxLength(200);
        builder.Property(i => i.ReporterContact).HasMaxLength(200);

        builder.Property(i => i.Category).HasConversion<int>().IsRequired();
        builder.Property(i => i.Severity).HasConversion<int>().IsRequired();
        builder.Property(i => i.Status).HasConversion<int>().IsRequired();
        builder.Property(i => i.ClassificationMethod).HasConversion<int>().IsRequired();
        builder.Property(i => i.ProposedCategory).HasConversion<int>();
        builder.Property(i => i.ProposedSeverity).HasConversion<int>();

        // Same owned-type mapping as Asset, and for the same reason: a value-converted property
        // cannot have its members accessed in a query, and the duplicate-detection bounding box
        // filters on latitude and longitude directly.
        builder.OwnsOne(i => i.Location, location =>
        {
            location.Property(c => c.Latitude).HasColumnName("Latitude").HasPrecision(9, 6);
            location.Property(c => c.Longitude).HasColumnName("Longitude").HasPrecision(9, 6);

            location.HasIndex(c => new { c.Latitude, c.Longitude })
                .HasDatabaseName("IX_Incidents_Latitude_Longitude");
        });

        // The reference is quoted over the phone and on the radio, so it must resolve to exactly
        // one incident. Filtered so a report removed in error does not reserve its reference.
        builder
            .HasIndex(i => new { i.OrganizationId, i.Reference })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Incidents_Organization_Reference");

        // Serves the triage queue: open incidents for an organization, newest first.
        builder
            .HasIndex(i => new { i.OrganizationId, i.Status, i.ReportedOnUtc })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_Incidents_Organization_Status_Reported");

        // Serves duplicate detection, which filters by category over a recent time window.
        builder
            .HasIndex(i => new { i.OrganizationId, i.Category, i.ReportedOnUtc })
            .HasDatabaseName("IX_Incidents_Organization_Category_Reported");

        // Serves "what has been reported against this asset?", which drives condition review.
        builder
            .HasIndex(i => i.AssetId)
            .HasDatabaseName("IX_Incidents_AssetId")
            .HasFilter("[AssetId] IS NOT NULL");
    }
}
