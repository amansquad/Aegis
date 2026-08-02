namespace Aegis.Domain.Abstractions;

/// <summary>
/// Applied to entities whose rows belong to exactly one organization (tenant).
/// </summary>
/// <remarks>
/// <para>
/// This is the single most security-critical interface in the codebase. Implementing it opts an
/// entity into an EF Core global query filter (<c>WHERE OrganizationId = @current</c>) applied to
/// every query, and into automatic stamping of <see cref="OrganizationId"/> on insert.
/// </para>
/// <para>
/// The point is that tenant isolation becomes impossible to forget rather than merely documented.
/// A developer writing <c>_db.Assets.Where(a =&gt; a.Status == Active)</c> gets tenant-scoped
/// results without knowing tenancy exists. An architecture test asserts that every entity in a
/// tenant-owned module implements this interface, so a new entity cannot silently opt out.
/// </para>
/// </remarks>
public interface ITenantOwned
{
    /// <summary>The owning organization. Assigned on insert and never modified thereafter.</summary>
    Guid OrganizationId { get; }
}

/// <summary>
/// Applied to entities that record who created and last modified them, and when.
/// </summary>
/// <remarks>
/// Populated centrally by a <c>SaveChanges</c> interceptor from <c>ICurrentUser</c> and
/// <c>IDateTimeProvider</c>. Handlers never set these fields — anything a handler can forget to
/// set, it eventually will.
/// </remarks>
public interface IAuditableEntity
{
    /// <summary>UTC instant the row was inserted.</summary>
    DateTimeOffset CreatedOnUtc { get; set; }

    /// <summary>The user who inserted the row. Null for system- or seed-generated rows.</summary>
    Guid? CreatedBy { get; set; }

    /// <summary>UTC instant of the most recent update. Null if never updated.</summary>
    DateTimeOffset? ModifiedOnUtc { get; set; }

    /// <summary>The user who last updated the row.</summary>
    Guid? ModifiedBy { get; set; }
}

/// <summary>
/// Applied to entities that are never physically deleted.
/// </summary>
/// <remarks>
/// <para>
/// Regulated infrastructure operators are typically required to retain records of assets and
/// interventions for years, and a work order deleted by mistake is evidence destroyed. Deletion
/// therefore sets a flag; a global query filter hides flagged rows from every ordinary query.
/// </para>
/// <para>
/// The cost is honest and worth stating: soft-deleted rows still occupy unique indexes, so those
/// indexes are declared as filtered indexes (<c>WHERE IsDeleted = 0</c>) to let a serial number be
/// reused once its asset is retired.
/// </para>
/// </remarks>
public interface ISoftDeletable
{
    /// <summary>True once the entity has been logically deleted.</summary>
    bool IsDeleted { get; set; }

    /// <summary>UTC instant of logical deletion.</summary>
    DateTimeOffset? DeletedOnUtc { get; set; }

    /// <summary>The user who performed the logical deletion.</summary>
    Guid? DeletedBy { get; set; }
}
