using Aegis.Domain.Abstractions;
using Aegis.Domain.Common;

namespace Aegis.Domain.Identity;

/// <summary>
/// A named bundle of permissions within one organization.
/// </summary>
/// <remarks>
/// <para>
/// Roles exist for administrators, not for code. No handler ever asks "is this user a Supervisor?"
/// — it asks whether they hold <c>workorders.approve</c>. The role is simply how that permission
/// gets granted to fifty people at once, and changing what a Supervisor can do is then a data
/// change rather than a deployment.
/// </para>
/// <para>
/// Roles are tenant-owned, including the seeded ones. Each organization receives its own editable
/// copy, because a water utility's Supervisor is not a road authority's, and a globally shared
/// definition would force one of them into a shape that does not fit their operation.
/// </para>
/// </remarks>
public sealed class Role : AggregateRoot<Guid>, ITenantOwned, IAuditableEntity
{
    private readonly HashSet<string> _permissions = new(StringComparer.Ordinal);

    /// <summary>Parameterless constructor required by EF Core materialisation.</summary>
    private Role()
    {
        Name = string.Empty;
        NormalizedName = string.Empty;
    }

    private Role(Guid id, Guid organizationId, string name, string? description, bool isSystemRole)
        : base(id)
    {
        OrganizationId = organizationId;
        Name = name;
        NormalizedName = name.ToUpperInvariant();
        Description = description;
        IsSystemRole = isSystemRole;
    }

    /// <inheritdoc />
    public Guid OrganizationId { get; private set; }

    /// <summary>Display name, as the administrator typed it.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// Upper-cased name, used for uniqueness and lookup.
    /// </summary>
    /// <remarks>
    /// Stored rather than computed so the unique index can be a plain index on a plain column.
    /// Comparing case-insensitively in every query instead would either need a collation change or
    /// produce a non-sargable predicate that cannot use the index.
    /// </remarks>
    public string NormalizedName { get; private set; }

    /// <summary>Human-readable description of what the role is for.</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// True for the roles seeded with the organization.
    /// </summary>
    /// <remarks>
    /// Protects against deleting the Administrator role and leaving an organization with no way to
    /// administer itself — a state that is easy to reach by accident and requires support
    /// intervention to escape. Their permissions remain editable; only deletion is blocked.
    /// </remarks>
    public bool IsSystemRole { get; private set; }

    /// <summary>Permissions this role confers.</summary>
    public IReadOnlySet<string> Permissions => _permissions;

    /// <inheritdoc />
    public DateTimeOffset CreatedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? ModifiedBy { get; set; }

    /// <summary>Creates a role.</summary>
    public static Result<Role> Create(
        Guid organizationId,
        string? name,
        string? description = null,
        bool isSystemRole = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Role>(Error.Validation("Role.NameRequired", "A role name is required."));
        }

        var trimmed = name.Trim();

        if (trimmed.Length > 64)
        {
            return Result.Failure<Role>(
                Error.Validation("Role.NameTooLong", "A role name cannot exceed 64 characters."));
        }

        return Result.Success(new Role(
            Guid.CreateVersion7(),
            organizationId,
            trimmed,
            description?.Trim(),
            isSystemRole));
    }

    /// <summary>Creates one of the seeded roles with its default permissions.</summary>
    public static Result<Role> CreateSystemRole(Guid organizationId, string name)
    {
        var result = Create(organizationId, name, isSystemRole: true);

        if (result.IsFailure)
        {
            return result;
        }

        if (SystemRoles.DefaultPermissions.TryGetValue(name, out var defaults))
        {
            foreach (var permission in defaults)
            {
                result.Value._permissions.Add(permission);
            }
        }

        return result;
    }

    /// <summary>Grants a permission to the role.</summary>
    /// <remarks>
    /// Rejects unrecognised names. Granting a permission no code checks produces a role that
    /// appears to confer access and does not, which becomes a support ticket nobody can reproduce.
    /// </remarks>
    public Result Grant(string permission)
    {
        if (!Identity.Permissions.IsDefined(permission))
        {
            return Result.Failure(Error.Validation(
                "Role.UnknownPermission",
                $"'{permission}' is not a recognised permission."));
        }

        _permissions.Add(permission);

        return Result.Success();
    }

    /// <summary>Withdraws a permission from the role.</summary>
    public Result Revoke(string permission)
    {
        if (!_permissions.Remove(permission))
        {
            return Result.Failure(Error.NotFound(
                "Role.PermissionNotGranted",
                "The role does not hold this permission."));
        }

        return Result.Success();
    }

    /// <summary>Replaces the role's entire permission set.</summary>
    /// <remarks>
    /// Validates every entry before mutating anything, so a request containing one bad name leaves
    /// the role untouched rather than half-applied.
    /// </remarks>
    public Result SetPermissions(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var requested = permissions.Distinct(StringComparer.Ordinal).ToArray();

        var unknown = requested.Where(p => !Identity.Permissions.IsDefined(p)).ToArray();

        if (unknown.Length > 0)
        {
            return Result.Failure(Error.Validation(
                "Role.UnknownPermission",
                $"Unrecognised permissions: {string.Join(", ", unknown)}."));
        }

        _permissions.Clear();

        foreach (var permission in requested)
        {
            _permissions.Add(permission);
        }

        return Result.Success();
    }

    /// <summary>Renames the role.</summary>
    public Result Rename(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("Role.NameRequired", "A role name is required."));
        }

        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();

        return Result.Success();
    }

    /// <summary>Updates the description.</summary>
    public void Describe(string? description) => Description = description?.Trim();

    /// <summary>True when the role confers the named permission.</summary>
    public bool HasPermission(string permission) => _permissions.Contains(permission);
}
