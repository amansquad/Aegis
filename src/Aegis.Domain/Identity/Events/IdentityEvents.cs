using Aegis.Domain.Common;

namespace Aegis.Domain.Identity.Events;

/// <summary>Raised when a new user account is created.</summary>
/// <remarks>
/// Subscribers send the confirmation email and write the onboarding audit entry. Note that the
/// email address travels on the event: a handler that re-read the user would see whatever the
/// address is at handling time, which is not necessarily what it was at registration.
/// </remarks>
public sealed record UserRegistered(Guid UserId, Guid OrganizationId, string Email, string DisplayName)
    : DomainEvent;

/// <summary>Raised when a user authenticates successfully.</summary>
public sealed record UserSignedIn(Guid UserId, Guid OrganizationId, DateTimeOffset SignedInOnUtc)
    : DomainEvent;

/// <summary>
/// Raised when repeated failed sign-in attempts lock an account.
/// </summary>
/// <remarks>
/// Worth notifying on. From the account holder's perspective a lockout they did not cause is the
/// earliest visible evidence of a credential-stuffing attempt against them.
/// </remarks>
public sealed record UserLockedOut(
    Guid UserId,
    Guid OrganizationId,
    int FailedAttempts,
    DateTimeOffset LockoutEndsOnUtc) : DomainEvent;

/// <summary>Raised when a user's password changes, whether by the user or by an administrator.</summary>
public sealed record UserPasswordChanged(Guid UserId, Guid OrganizationId, bool ChangedByAdministrator)
    : DomainEvent;

/// <summary>Raised when a user confirms their email address.</summary>
public sealed record UserEmailConfirmed(Guid UserId, Guid OrganizationId, string Email) : DomainEvent;

/// <summary>Raised when a role is granted to a user.</summary>
public sealed record RoleAssignedToUser(Guid UserId, Guid OrganizationId, Guid RoleId) : DomainEvent;

/// <summary>Raised when a role is withdrawn from a user.</summary>
public sealed record RoleRemovedFromUser(Guid UserId, Guid OrganizationId, Guid RoleId) : DomainEvent;

/// <summary>Raised when an administrator invites someone to join the organization.</summary>
/// <remarks>
/// Carries no token. The event is persisted in the audit trail and logged, and a credential that
/// grants account creation inside a tenant must not travel through either.
/// </remarks>
public sealed record UserInvited(
    Guid InvitationId,
    Guid OrganizationId,
    string Email,
    Guid InvitedBy,
    DateTimeOffset ExpiresOnUtc) : DomainEvent;

/// <summary>Raised when an invitation is accepted and the account created.</summary>
public sealed record InvitationAccepted(
    Guid InvitationId,
    Guid OrganizationId,
    Guid UserId,
    string Email) : DomainEvent;

/// <summary>Raised when an account is deactivated.</summary>
public sealed record UserDeactivated(Guid UserId, Guid OrganizationId, string? Reason) : DomainEvent;

/// <summary>Raised when a deactivated account is restored.</summary>
public sealed record UserReactivated(Guid UserId, Guid OrganizationId) : DomainEvent;

/// <summary>
/// Raised when an already-rotated refresh token is presented again.
/// </summary>
/// <remarks>
/// <para>
/// This is a security event, not a routine one. A correctly behaving client discards a refresh
/// token the moment it exchanges it, so seeing that token again means two parties hold it — the
/// legitimate client and someone who obtained a copy.
/// </para>
/// <para>
/// The response is to revoke the whole descendant chain, which signs out both parties. Signing out
/// a legitimate user is a minor annoyance; leaving an attacker with an indefinitely renewable
/// session is not.
/// </para>
/// </remarks>
public sealed record RefreshTokenReuseDetected(
    Guid UserId,
    Guid OrganizationId,
    string? DetectedFromIpAddress,
    int TokensRevoked) : DomainEvent;
