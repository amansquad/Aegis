namespace Aegis.Application.Abstractions.Security;

/// <summary>
/// The identity on whose behalf the current operation is running.
/// </summary>
/// <remarks>
/// <para>
/// Implemented in Infrastructure over <c>IHttpContextAccessor</c>. The abstraction exists so that
/// handlers can be unit-tested with a substituted user, and so that non-HTTP entry points —
/// background jobs, the offline sync reconciler, database seeding — can supply a system identity
/// without faking an HTTP context.
/// </para>
/// <para>
/// Authorization decisions are made against <see cref="Permissions"/>, not <see cref="Roles"/>.
/// Roles are a packaging convenience for administrators; permissions are what code checks. The
/// difference matters the first time a customer asks for "a dispatcher who can also approve
/// budgets" — with role checks scattered through handlers that requires a code change, with
/// permission checks it is a configuration change.
/// </para>
/// </remarks>
public interface ICurrentUser
{
    /// <summary>The authenticated user's identifier, or null when unauthenticated.</summary>
    Guid? Id { get; }

    /// <summary>The authenticated user's email address, or null when unauthenticated.</summary>
    string? Email { get; }

    /// <summary>The authenticated user's display name, or null when unauthenticated.</summary>
    string? DisplayName { get; }

    /// <summary>True when the request carries a valid authenticated principal.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Roles assigned to the user. Present for display and coarse filtering only.</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>Fine-grained permissions granted to the user. The basis of every access decision.</summary>
    IReadOnlyCollection<string> Permissions { get; }

    /// <summary>Returns true when the user holds the named permission.</summary>
    bool HasPermission(string permission);

    /// <summary>Returns true when the user holds the named role.</summary>
    bool IsInRole(string role);
}
