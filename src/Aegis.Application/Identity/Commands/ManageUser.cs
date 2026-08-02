using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Abstractions.Security;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using Aegis.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.Identity.Commands;

/// <summary>Errors shared by the user management commands.</summary>
internal static class UserManagementErrors
{
    /// <summary>The user does not exist, or belongs to another organization.</summary>
    /// <remarks>
    /// One error for both. Because the query is tenant-filtered, a user in another organization
    /// simply is not found — and reporting "forbidden" instead would confirm that the identifier
    /// names a real account somewhere on the platform.
    /// </remarks>
    public static readonly Error UserNotFound = Error.NotFound(
        "User.NotFound",
        "The user was not found.");

    /// <summary>An administrator attempted an action on their own account that would lock them out.</summary>
    public static readonly Error CannotActOnSelf = Error.Conflict(
        "User.CannotActOnSelf",
        "You cannot perform this action on your own account.");
}

/// <summary>Grants a role to a user.</summary>
/// <param name="UserId">The user to modify.</param>
/// <param name="RoleId">The role to grant.</param>
public sealed record AssignRoleCommand(Guid UserId, Guid RoleId) : ICommand;

/// <summary>Handles <see cref="AssignRoleCommand"/>.</summary>
internal sealed class AssignRoleCommandHandler(IAegisDbContext context)
    : ICommandHandler<AssignRoleCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(AssignRoleCommand request, CancellationToken cancellationToken)
    {
        var user = await context.Users.SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserManagementErrors.UserNotFound);
        }

        // Checked against roles visible to this tenant, so an administrator cannot attach another
        // organization's role by guessing its identifier.
        var roleExists = await context.Roles.AnyAsync(r => r.Id == request.RoleId, cancellationToken);

        if (!roleExists)
        {
            return Result.Failure(Error.NotFound("Role.NotFound", "The role was not found."));
        }

        var assigned = user.AssignRole(request.RoleId);

        if (assigned.IsFailure)
        {
            return assigned;
        }

        // The aggregate rotated the security stamp and raised RoleAssignedToUser, so the cached
        // stamp is evicted after commit and the user's existing access token stops working on
        // their next request. The new permission takes effect immediately rather than in fifteen
        // minutes, and so would a withdrawal.
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Withdraws a role from a user.</summary>
/// <param name="UserId">The user to modify.</param>
/// <param name="RoleId">The role to withdraw.</param>
public sealed record RemoveRoleCommand(Guid UserId, Guid RoleId) : ICommand;

/// <summary>Handles <see cref="RemoveRoleCommand"/>.</summary>
internal sealed class RemoveRoleCommandHandler(IAegisDbContext context, ICurrentUser currentUser)
    : ICommandHandler<RemoveRoleCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(RemoveRoleCommand request, CancellationToken cancellationToken)
    {
        var user = await context.Users.SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserManagementErrors.UserNotFound);
        }

        // Guards the most common way an organization locks itself out: the sole administrator
        // removing their own administrator role. Recovering from that needs support intervention,
        // so it is worth one explicit check.
        if (user.Id == currentUser.Id && await IsLastAdministratorRoleAsync(request, cancellationToken))
        {
            return Result.Failure(Error.Conflict(
                "User.CannotRemoveOwnLastAdminRole",
                "You cannot remove your own administrator role while you are the only administrator."));
        }

        var removed = user.RemoveRole(request.RoleId);

        if (removed.IsFailure)
        {
            return removed;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private async Task<bool> IsLastAdministratorRoleAsync(
        RemoveRoleCommand request,
        CancellationToken cancellationToken)
    {
        var role = await context.Roles
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);

        if (role is null || !string.Equals(role.Name, SystemRoles.Administrator, StringComparison.Ordinal))
        {
            return false;
        }

        var administratorCount = await context.Users
            .AsNoTracking()
            .CountAsync(
                u => EF.Property<List<Guid>>(u, EntityFields.UserRoleIds).Contains(request.RoleId)
                    && u.Status == UserStatus.Active,
                cancellationToken);

        return administratorCount <= 1;
    }
}

/// <summary>Deactivates a user account.</summary>
/// <param name="UserId">The user to deactivate.</param>
/// <param name="Reason">Why, for the audit trail.</param>
public sealed record DeactivateUserCommand(Guid UserId, string? Reason) : ICommand;

/// <summary>Handles <see cref="DeactivateUserCommand"/>.</summary>
internal sealed class DeactivateUserCommandHandler(
    IAegisDbContext context,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<DeactivateUserCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        // Self-deactivation is refused outright. It is never what an administrator means to do, it
        // immediately invalidates their own session, and if they were the last administrator the
        // organization is locked out with no way back in.
        if (request.UserId == currentUser.Id)
        {
            return Result.Failure(UserManagementErrors.CannotActOnSelf);
        }

        var user = await context.Users
            .Include(u => u.RefreshTokens)
            .SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserManagementErrors.UserNotFound);
        }

        var deactivated = user.Deactivate(timeProvider.GetUtcNow(), request.Reason);

        if (deactivated.IsFailure)
        {
            return deactivated;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Restores a deactivated user account.</summary>
/// <param name="UserId">The user to reactivate.</param>
public sealed record ReactivateUserCommand(Guid UserId) : ICommand;

/// <summary>Handles <see cref="ReactivateUserCommand"/>.</summary>
internal sealed class ReactivateUserCommandHandler(IAegisDbContext context)
    : ICommandHandler<ReactivateUserCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(ReactivateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await context.Users.SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(UserManagementErrors.UserNotFound);
        }

        var reactivated = user.Reactivate();

        if (reactivated.IsFailure)
        {
            return reactivated;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
