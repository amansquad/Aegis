namespace Aegis.Domain.Abstractions;

/// <summary>
/// Marks the entity that <em>is</em> a tenant, as opposed to one that belongs to a tenant.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ITenantOwned"/> entities carry an <c>OrganizationId</c> column and are filtered on
/// it. The organization row itself has no such column: its own primary key is the tenant boundary.
/// </para>
/// <para>
/// Without this distinction the organizations table would be the single table in the schema with no
/// isolation at all, so any authenticated user could enumerate every customer of the platform —
/// their names, their sizes, and the fact that they are customers. That is a data leak even though
/// no operational record is exposed. The filter applied for this marker is
/// <c>e =&gt; e.Id == CurrentTenantId</c>.
/// </para>
/// </remarks>
public interface ITenantRoot
{
    /// <summary>The organization's identifier, which is also the tenant boundary.</summary>
    Guid Id { get; }
}
