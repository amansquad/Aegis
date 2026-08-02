using Aegis.Domain.Identity;
using Aegis.Domain.Identity.ValueObjects;
using Aegis.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aegis.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Organization"/>.</summary>
internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations", "identity");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.Name).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Slug).HasMaxLength(100).IsRequired();
        builder.Property(o => o.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(o => o.ContactEmail).HasMaxLength(EmailAddress.MaxLength);

        builder.Property(o => o.Kind).HasConversion<int>().IsRequired();
        builder.Property(o => o.Status).HasConversion<int>().IsRequired();

        // Unique platform-wide, not per tenant: the slug appears in URLs and must resolve to
        // exactly one organization.
        builder
            .HasIndex(o => o.Slug)
            .IsUnique()
            .HasDatabaseName("UX_Organizations_Slug");
    }
}

/// <summary>Maps <see cref="User"/> and its owned refresh token collection.</summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", "identity");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.OrganizationId).IsRequired();

        // The value object is stored as a plain column rather than an owned type. A conversion
        // keeps the column indexable and comparable in LINQ, which an owned entity would complicate
        // for no benefit on a single-property value.
        builder
            .Property(u => u.Email)
            .HasConversion(email => email.Value, value => EmailAddress.FromTrustedSource(value))
            .HasMaxLength(EmailAddress.MaxLength)
            .IsRequired();

        builder
            .Property(u => u.PasswordHash)
            .HasConversion(hash => hash.Value, value => PasswordHash.FromEncoded(value))
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.SecurityStamp).HasMaxLength(64).IsRequired();
        builder.Property(u => u.Status).HasConversion<int>().IsRequired();

        // DisplayName is computed from first and last name, so it must not be persisted.
        builder.Ignore(u => u.DisplayName);

        // Role assignments as a join table. The backing field is a private List<Guid>, so the
        // collection is mapped as a primitive collection owned by the user.
        builder
            .PrimitiveCollection<List<Guid>>("_roleIds")
            .HasColumnName("RoleIds")
            .HasField("_roleIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(u => u.RoleIds);

        // The navigation is named after the public property, not the backing field, so that LINQ
        // such as `u.RefreshTokens.Any(t => t.TokenHash == hash)` translates to SQL. Mapping it
        // under the field name instead leaves the property unmapped, and the refresh lookup then
        // fails at translation time rather than at build time.
        builder
            .HasMany<RefreshToken>(nameof(User.RefreshTokens))
            .WithOne()
            .HasForeignKey("UserId")
            .OnDelete(DeleteBehavior.Cascade);

        // Reads and writes go through the private list, since the property is read-only and returns
        // a fresh wrapper on each access.
        builder.Navigation(nameof(User.RefreshTokens))
            .HasField("_refreshTokens")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Email is unique per organization, and filtered so that a soft-deleted account releases
        // its address for reuse. An unfiltered unique index would make a deleted user permanently
        // block their own address from ever being registered again.
        builder
            .HasIndex(u => new { u.OrganizationId, u.Email })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Users_Organization_Email");

        // Sign-in looks a user up by address alone, before any tenant is known, so this index
        // serves the hottest query in the identity module.
        builder
            .HasIndex(u => u.Email)
            .HasDatabaseName("IX_Users_Email");
    }
}

/// <summary>Maps <see cref="RefreshToken"/> as a child of the user aggregate.</summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens", "identity");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        // Base64 of a SHA-256 digest is 44 characters. Sized exactly rather than generously, so a
        // value of the wrong shape fails loudly at the database rather than being silently stored.
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);
        builder.Property(t => t.RevocationReason).HasMaxLength(200);
        builder.Property(t => t.IssuedToIpAddress).HasMaxLength(45);

        // The refresh endpoint's only lookup. Without this index every refresh scans the table,
        // which grows with every sign-in the platform has ever served.
        builder
            .HasIndex(t => t.TokenHash)
            .IsUnique()
            .HasDatabaseName("UX_RefreshTokens_TokenHash");
    }
}

/// <summary>Maps <see cref="Role"/>.</summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles", "identity");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.OrganizationId).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(64).IsRequired();
        builder.Property(r => r.NormalizedName).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(500);

        // Permissions as a primitive collection rather than a join table. They are read as a whole
        // set on every sign-in and never queried individually, so a separate table would add a join
        // to the hottest path in exchange for a normalisation nothing benefits from.
        builder
            .PrimitiveCollection<List<string>>("_permissions")
            .HasColumnName("Permissions")
            .HasField("_permissions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(r => r.Permissions);

        builder
            .HasIndex(r => new { r.OrganizationId, r.NormalizedName })
            .IsUnique()
            .HasDatabaseName("UX_Roles_Organization_Name");
    }
}
