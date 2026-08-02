using Aegis.Application.Abstractions.Multitenancy;

namespace Aegis.Infrastructure.Multitenancy;

/// <summary>
/// Scoped, mutable holder for the current organization.
/// </summary>
/// <remarks>
/// <para>
/// Registered as scoped, so one instance exists per request and per background job execution. It
/// is populated by the tenant resolution middleware from the JWT's organization claim before any
/// handler runs.
/// </para>
/// <para>
/// <b>Write-once.</b> Once set, the tenant cannot be changed for the lifetime of the scope. A
/// scope whose tenant can be reassigned mid-request is a scope in which a handler could read one
/// organization's data and write it into another's, and no amount of care in individual handlers
/// makes that safe. Reassignment throws rather than being silently ignored, because a caller
/// attempting it has misunderstood something that matters.
/// </para>
/// </remarks>
public sealed class TenantContext : ITenantContext
{
    private Guid? _organizationId;

    /// <inheritdoc />
    public Guid? OrganizationId => _organizationId;

    /// <inheritdoc />
    public bool HasTenant => _organizationId.HasValue;

    /// <inheritdoc />
    public Guid RequireOrganizationId() =>
        _organizationId ?? throw new InvalidOperationException(
            "No organization is established for the current scope. A tenant-owned write cannot " +
            "proceed without one, because the resulting row would be invisible to every query.");

    /// <inheritdoc />
    public void SetTenant(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "An empty organization id cannot be used as a tenant.",
                nameof(organizationId));
        }

        if (_organizationId.HasValue && _organizationId.Value != organizationId)
        {
            throw new InvalidOperationException(
                "The tenant for this scope has already been established and cannot be changed.");
        }

        _organizationId = organizationId;
    }
}
