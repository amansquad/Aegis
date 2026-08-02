namespace Aegis.Application.Abstractions.Multitenancy;

/// <summary>
/// The organization (tenant) whose data the current operation may see.
/// </summary>
/// <remarks>
/// <para>
/// This is the most security-critical abstraction in the codebase. Its value feeds the EF Core
/// global query filter applied to every <see cref="Domain.Abstractions.ITenantOwned"/> entity, so
/// a wrong value here is a cross-tenant data disclosure, not a bug report.
/// </para>
/// <para>
/// <b>Fail-closed by design.</b> When <see cref="OrganizationId"/> is null — an unauthenticated
/// request, or a token missing the organization claim — the filter compiles to
/// <c>WHERE OrganizationId = NULL</c>, which matches no rows. The system returns nothing rather
/// than everything. This is the correct direction to fail, and it is worth stating explicitly
/// because the opposite convention (null means "no filter") appears in a lot of sample code and
/// turns a missing claim into a full data breach.
/// </para>
/// </remarks>
public interface ITenantContext
{
    /// <summary>The current tenant, or null when none has been established.</summary>
    Guid? OrganizationId { get; }

    /// <summary>True when a tenant has been established for this scope.</summary>
    bool HasTenant { get; }

    /// <summary>
    /// Returns the current tenant, or throws when none is established.
    /// </summary>
    /// <remarks>
    /// Used by command handlers that write tenant-owned rows, where proceeding without a tenant
    /// would produce an orphaned record no query could ever return.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No tenant is established for this scope.</exception>
    Guid RequireOrganizationId();

    /// <summary>
    /// Establishes the tenant for the current scope.
    /// </summary>
    /// <remarks>
    /// Called by the tenant resolution middleware from the JWT's organization claim, and by
    /// background jobs that process one tenant at a time. Deliberately not callable from a
    /// handler: a handler that can change its own tenant can read another tenant's data.
    /// </remarks>
    /// <param name="organizationId">The tenant to scope subsequent work to.</param>
    void SetTenant(Guid organizationId);
}
