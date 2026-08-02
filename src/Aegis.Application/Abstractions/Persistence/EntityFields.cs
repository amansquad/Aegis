namespace Aegis.Application.Abstractions.Persistence;

/// <summary>
/// Names of collection properties that are mapped through their backing field.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Aggregates expose their collections as <c>IReadOnlyCollection&lt;T&gt;</c>
/// so callers cannot bypass the domain methods that maintain their invariants. EF Core cannot bind
/// a primitive collection to a read-only property — it requires an array or an <c>IList&lt;T&gt;</c>
/// — so these collections are mapped through their private backing field instead.
/// </para>
/// <para>
/// The consequence is that the public property is unmapped and cannot appear in a LINQ query, so a
/// filter such as "users holding this role" must address the mapped field by name via
/// <c>EF.Property</c>. These constants keep that name in one place: a query and a mapping that
/// disagree produce a runtime translation failure on a specific endpoint, which is the kind of
/// break that reaches production because the compiler has nothing to say about it.
/// </para>
/// <para>
/// The alternative — exposing <c>List&lt;T&gt;</c> publicly so EF can map it directly — would let
/// any caller add a role without going through <c>AssignRole</c>, skipping the security stamp
/// rotation that makes a permission change take effect. Encapsulation is worth the indirection here.
/// </para>
/// </remarks>
public static class EntityFields
{
    /// <summary>Backing field for <c>User.RoleIds</c>.</summary>
    public const string UserRoleIds = "_roleIds";

    /// <summary>Backing field for <c>UserInvitation.RoleIds</c>.</summary>
    public const string InvitationRoleIds = "_roleIds";

    /// <summary>Backing field for <c>Role.Permissions</c>.</summary>
    public const string RolePermissions = "_permissions";
}
