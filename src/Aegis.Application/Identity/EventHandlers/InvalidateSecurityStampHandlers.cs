using Aegis.Application.Abstractions.Events;
using Aegis.Application.Abstractions.Security;
using Aegis.Domain.Identity.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Aegis.Application.Identity.EventHandlers;

/// <summary>
/// Evicts a user's cached security stamp whenever their security posture changes.
/// </summary>
/// <remarks>
/// <para>
/// The first genuine subscribers in the system, and a good demonstration of why domain events earn
/// their keep. The alternative is a call to <c>ISecurityStampService.InvalidateAsync</c> beside
/// every mutation that rotates the stamp — in the sign-in handler, the role assignment handler, the
/// deactivation handler, and every future one. Each is easy to write and equally easy to omit, and
/// omitting one means a revoked capability keeps working with no visible symptom.
/// </para>
/// <para>
/// Subscribing to the events instead makes the aggregate the single source of truth: anything that
/// rotates the stamp raises an event, and the eviction follows without the author of that code
/// needing to know a cache exists.
/// </para>
/// <para>
/// These run after the transaction commits, so the cache is never evicted for a change that is
/// subsequently rolled back — which would cause a needless database read rather than a correctness
/// problem, but is still wasted work.
/// </para>
/// </remarks>
internal sealed class InvalidateSecurityStampHandlers(
    ISecurityStampService securityStampService,
    ILogger<InvalidateSecurityStampHandlers> logger)
    : INotificationHandler<DomainEventNotification<UserPasswordChanged>>,
        INotificationHandler<DomainEventNotification<RoleAssignedToUser>>,
        INotificationHandler<DomainEventNotification<RoleRemovedFromUser>>,
        INotificationHandler<DomainEventNotification<UserDeactivated>>,
        INotificationHandler<DomainEventNotification<RefreshTokenReuseDetected>>
{
    /// <inheritdoc />
    public Task Handle(
        DomainEventNotification<UserPasswordChanged> notification,
        CancellationToken cancellationToken) =>
        InvalidateAsync(notification.DomainEvent.UserId, "password changed", cancellationToken);

    /// <inheritdoc />
    public Task Handle(
        DomainEventNotification<RoleAssignedToUser> notification,
        CancellationToken cancellationToken) =>
        InvalidateAsync(notification.DomainEvent.UserId, "role assigned", cancellationToken);

    /// <inheritdoc />
    public Task Handle(
        DomainEventNotification<RoleRemovedFromUser> notification,
        CancellationToken cancellationToken) =>
        InvalidateAsync(notification.DomainEvent.UserId, "role removed", cancellationToken);

    /// <inheritdoc />
    public Task Handle(
        DomainEventNotification<UserDeactivated> notification,
        CancellationToken cancellationToken) =>
        InvalidateAsync(notification.DomainEvent.UserId, "account deactivated", cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Token reuse does not itself rotate the stamp, but it does mean a session is known to be
    /// compromised. Evicting forces the next request from any surviving access token to re-read
    /// state that has just been revoked.
    /// </remarks>
    public Task Handle(
        DomainEventNotification<RefreshTokenReuseDetected> notification,
        CancellationToken cancellationToken) =>
        InvalidateAsync(notification.DomainEvent.UserId, "refresh token reuse detected", cancellationToken);

    private async Task InvalidateAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Invalidating cached security stamp for {UserId} ({Reason})",
            userId,
            reason);

        await securityStampService.InvalidateAsync(userId, cancellationToken);
    }
}
