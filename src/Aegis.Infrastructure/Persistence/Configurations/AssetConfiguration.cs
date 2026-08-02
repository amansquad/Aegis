using Aegis.Application.Abstractions.Persistence;
using Aegis.Domain.Assets;
using Aegis.Domain.Assets.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aegis.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Asset"/> and its inspection history.</summary>
internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("Assets", "assets");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.OrganizationId).IsRequired();

        builder
            .Property(a => a.Code)
            .HasConversion(code => code.Value, value => AssetCode.FromTrustedSource(value))
            .HasMaxLength(AssetCode.MaxLength)
            .IsRequired();

        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Manufacturer).HasMaxLength(200);
        builder.Property(a => a.ModelNumber).HasMaxLength(100);
        builder.Property(a => a.SerialNumber).HasMaxLength(100);
        builder.Property(a => a.Notes).HasMaxLength(4000);

        builder.Property(a => a.Type).HasConversion<int>().IsRequired();
        builder.Property(a => a.Status).HasConversion<int>().IsRequired();
        builder.Property(a => a.Condition).HasConversion<int>().IsRequired();
        builder.Property(a => a.Criticality).HasConversion<int>().IsRequired();

        // ---- Position ----
        //
        // Mapped as an owned type — two nullable double columns — rather than through a value
        // converter to a geography Point. That choice was forced by a real constraint and is worth
        // recording, because the converter approach is the obvious first attempt.
        //
        // A value-converted property cannot have its members accessed in a query: EF sees the
        // converted column, not the value object, so `a.Location.Latitude` and any spatial method
        // call fail at translation time. An owned type maps each member to its own column, so
        // filtering and projecting on latitude and longitude both translate. EF Core's complex
        // types would be the more natural fit but do not yet support optional values, and an
        // asset's position is genuinely optional — decades of paper records contain assets nobody
        // ever surveyed.
        //
        // The trade-off is that there is no geography column and therefore no spatial index.
        // Proximity is served by an index-assisted bounding box plus an exact great-circle
        // predicate, which is described in ListAssetsQueryHandler. For estates of the size this
        // platform targets that is comfortably sufficient; a geography column maintained alongside
        // these two would be the next step if it ever is not.
        builder.OwnsOne(a => a.Location, location =>
        {
            location.Property(c => c.Latitude).HasColumnName("Latitude").HasPrecision(9, 6);
            location.Property(c => c.Longitude).HasColumnName("Longitude").HasPrecision(9, 6);

            // Serves the bounding-box prefilter that narrows a proximity search before the exact
            // distance predicate runs.
            location.HasIndex(c => new { c.Latitude, c.Longitude })
                .HasDatabaseName("IX_Assets_Latitude_Longitude");
        });

        builder
            .HasMany<AssetInspection>(nameof(Asset.Inspections))
            .WithOne()
            .HasForeignKey("AssetId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(nameof(Asset.Inspections))
            .HasField("_inspections")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Self-reference for the containment hierarchy. NoAction rather than Cascade: deleting a
        // pumping station must not silently delete every pump inside it, and SQL Server rejects
        // cascade on a self-reference anyway because it cannot prove the graph is acyclic.
        builder
            .HasOne<Asset>()
            .WithMany()
            .HasForeignKey(a => a.ParentAssetId)
            .OnDelete(DeleteBehavior.NoAction);

        // The asset code is what operators quote, so it must resolve to exactly one asset within an
        // organization. Filtered on IsDeleted so a record created in error and removed does not
        // permanently reserve its code.
        builder
            .HasIndex(a => new { a.OrganizationId, a.Code })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Assets_Organization_Code");

        // Serves the dominant list query: assets of a type and status within an organization.
        builder
            .HasIndex(a => new { a.OrganizationId, a.Type, a.Status })
            .HasDatabaseName("IX_Assets_Organization_Type_Status");

        // Serves "what is overdue for inspection?", which drives the maintenance module.
        builder
            .HasIndex(a => new { a.OrganizationId, a.LastInspectedOnUtc })
            .HasDatabaseName("IX_Assets_Organization_LastInspected");

        builder
            .HasIndex(a => a.ParentAssetId)
            .HasDatabaseName("IX_Assets_ParentAssetId")
            .HasFilter("[ParentAssetId] IS NOT NULL");
    }
}

/// <summary>Maps <see cref="AssetInspection"/> as a child of the asset aggregate.</summary>
internal sealed class AssetInspectionConfiguration : IEntityTypeConfiguration<AssetInspection>
{
    public void Configure(EntityTypeBuilder<AssetInspection> builder)
    {
        builder.ToTable("AssetInspections", "assets");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.Condition).HasConversion<int>().IsRequired();
        builder.Property(i => i.InspectedOnUtc).IsRequired();
        builder.Property(i => i.InspectedBy).IsRequired();
        builder.Property(i => i.Notes).HasMaxLength(2000);

        // Serves an asset's inspection history, newest first, which is how it is always read.
        builder
            .HasIndex("AssetId", nameof(AssetInspection.InspectedOnUtc))
            .IsDescending(false, true)
            .HasDatabaseName("IX_AssetInspections_Asset_InspectedOn");
    }
}
