using Aegis.Domain.Abstractions;
using Aegis.Domain.Common;
using Aegis.Domain.Identity.Events;
using Aegis.Domain.Identity.ValueObjects;

namespace Aegis.Domain.Identity;

/// <summary>Lifecycle state of a user account.</summary>
public enum UserStatus
{
    /// <summary>Created but not yet email-confirmed. Cannot sign in.</summary>
    Pending = 0,

    /// <summary>Normal, usable account.</summary>
    Active = 1,

    /// <summary>Disabled by an administrator. Retained for audit history.</summary>
    Deactivated = 2,
}

/// <summary>
/// A person who can sign in to Aegis on behalf of one organization.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate root for authentication. Its cluster holds the credential, the role assignments
/// and the refresh token chain — everything that must stay consistent when a session changes.
/// Rotating a token while revoking a role, for instance, has to be one atomic operation, and the
/// aggregate boundary is what guarantees it.
/// </para>
/// <para>
/// <b>A user belongs to exactly one organization.</b> Someone who works for two utilities gets two
/// accounts. That is a deliberate simplification: cross-organization membership would mean the
/// tenant could no longer be a signed claim fixed for the session, and every query filter would
/// need a runtime "which tenant am I acting as right now?" decision. The cost of a second account
/// is an extra login; the cost of ambient tenant switching is a class of isolation bug that is very
/// hard to test away.
/// </para>
/// </remarks>
public sealed class User : AggregateRoot<Guid>, ITenantOwned, IAuditableEntity, ISoftDeletable
{
    private readonly List<Guid> _roleIds = [];
    private readonly List<RefreshToken> _refreshTokens = [];

    /// <summary>Parameterless constructor required by EF Core materialisation.</summary>
    private User()
    {
        Email = null!;
        PasswordHash = null!;
        FirstName = string.Empty;
        LastName = string.Empty;
        SecurityStamp = string.Empty;
    }

    private User(
        Guid id,
        Guid organizationId,
        EmailAddress email,
        PasswordHash passwordHash,
        string firstName,
        string lastName) : base(id)
    {
        OrganizationId = organizationId;
        Email = email;
        PasswordHash = passwordHash;
        FirstName = firstName;
        LastName = lastName;
        Status = UserStatus.Pending;
        SecurityStamp = Guid.CreateVersion7().ToString("N");
    }

    /// <inheritdoc />
    public Guid OrganizationId { get; private set; }

    /// <summary>The user's normalised email address, which is also their sign-in identifier.</summary>
    public EmailAddress Email { get; private set; }

    /// <summary>The user's hashed credential.</summary>
    public PasswordHash PasswordHash { get; private set; }

    /// <summary>Given name.</summary>
    public string FirstName { get; private set; }

    /// <summary>Family name.</summary>
    public string LastName { get; private set; }

    /// <summary>Full name for display.</summary>
    public string DisplayName => $"{FirstName} {LastName}".Trim();

    /// <summary>Account lifecycle state.</summary>
    public UserStatus Status { get; private set; }

    /// <summary>True once the user has confirmed their email address.</summary>
    public bool EmailConfirmed { get; private set; }

    /// <summary>Consecutive failed sign-in attempts since the last success.</summary>
    public int FailedSignInAttempts { get; private set; }

    /// <summary>When the current lockout expires, if the account is locked.</summary>
    public DateTimeOffset? LockoutEndsOnUtc { get; private set; }

    /// <summary>When the user last signed in successfully.</summary>
    public DateTimeOffset? LastSignedInOnUtc { get; private set; }

    /// <summary>
    /// Changes whenever the user's security posture does.
    /// </summary>
    /// <remarks>
    /// Embedded in issued access tokens. Because an access token cannot be recalled once issued,
    /// changing this stamp is what lets a password change or a role revocation invalidate tokens
    /// that are still within their lifetime — the token carries the old stamp and no longer matches.
    /// Without it, revoking an administrator's access would not take effect until their current
    /// access token expired.
    /// </remarks>
    public string SecurityStamp { get; private set; }

    /// <summary>Roles granted to the user.</summary>
    public IReadOnlyCollection<Guid> RoleIds => _roleIds.AsReadOnly();

    /// <summary>Refresh tokens issued to the user, active and historical.</summary>
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? ModifiedBy { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? DeletedBy { get; set; }

    /// <summary>True when the account is currently locked out.</summary>
    public bool IsLockedOut(DateTimeOffset now) =>
        LockoutEndsOnUtc.HasValue && LockoutEndsOnUtc.Value > now;

    /// <summary>
    /// True when the account is permitted to sign in.
    /// </summary>
    /// <remarks>
    /// Combines every gate in one place so that no caller can check two of the three conditions and
    /// forget the third.
    /// </remarks>
    public bool CanSignIn(DateTimeOffset now) =>
        Status == UserStatus.Active && !IsDeleted && !IsLockedOut(now);

    /// <summary>Registers a new user in the pending state.</summary>
    /// <param name="organizationId">The owning organization.</param>
    /// <param name="email">Validated email address.</param>
    /// <param name="passwordHash">Hash produced by the password hasher.</param>
    /// <param name="firstName">Given name.</param>
    /// <param name="lastName">Family name.</param>
    public static User Register(
        Guid organizationId,
        EmailAddress email,
        PasswordHash passwordHash,
        string firstName,
        string lastName)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(passwordHash);

        var user = new User(
            Guid.CreateVersion7(),
            organizationId,
            email,
            passwordHash,
            DomainException.RequireNotBlank(firstName, "User.FirstNameRequired", "A first name is required."),
            DomainException.RequireNotBlank(lastName, "User.LastNameRequired", "A last name is required."));

        user.RaiseDomainEvent(new UserRegistered(
            user.Id,
            organizationId,
            email.Value,
            user.DisplayName));

        return user;
    }

    /// <summary>
    /// Records a successful sign-in, clearing any accumulated failure count.
    /// </summary>
    public Result RecordSuccessfulSignIn(DateTimeOffset now)
    {
        if (!CanSignIn(now))
        {
            return Result.Failure(Error.Unauthorized(
                "User.CannotSignIn",
                "This account is not permitted to sign in."));
        }

        FailedSignInAttempts = 0;
        LockoutEndsOnUtc = null;
        LastSignedInOnUtc = now;

        RaiseDomainEvent(new UserSignedIn(Id, OrganizationId, now));

        return Result.Success();
    }

    /// <summary>
    /// Records a failed sign-in and locks the account once the threshold is reached.
    /// </summary>
    /// <param name="now">Current time.</param>
    /// <param name="maxAttempts">Failures tolerated before lockout.</param>
    /// <param name="lockoutDuration">How long the lockout lasts.</param>
    /// <remarks>
    /// A temporary lockout rather than a permanent one. Permanent locks make an attacker's failed
    /// guesses into a denial-of-service against the legitimate user: anyone who knows an address
    /// can lock that account out indefinitely. A time-boxed lockout still reduces online guessing
    /// to a negligible rate while healing itself.
    /// </remarks>
    public void RecordFailedSignIn(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        DomainException.Require(
            maxAttempts > 0,
            "User.InvalidLockoutThreshold",
            "The lockout threshold must be positive.");

        // A failure arriving after a lockout has expired starts a fresh count rather than resuming
        // the old one, so the previous lockout is not immediately re-triggered by a single typo.
        if (LockoutEndsOnUtc is not null && LockoutEndsOnUtc <= now)
        {
            FailedSignInAttempts = 0;
            LockoutEndsOnUtc = null;
        }

        FailedSignInAttempts++;

        if (FailedSignInAttempts < maxAttempts)
        {
            return;
        }

        LockoutEndsOnUtc = now.Add(lockoutDuration);

        RaiseDomainEvent(new UserLockedOut(
            Id,
            OrganizationId,
            FailedSignInAttempts,
            LockoutEndsOnUtc.Value));
    }

    /// <summary>Confirms the user's email address and activates the account.</summary>
    public Result ConfirmEmail()
    {
        if (EmailConfirmed)
        {
            return Result.Failure(Error.Conflict(
                "User.EmailAlreadyConfirmed",
                "This email address has already been confirmed."));
        }

        EmailConfirmed = true;

        if (Status == UserStatus.Pending)
        {
            Status = UserStatus.Active;
        }

        RaiseDomainEvent(new UserEmailConfirmed(Id, OrganizationId, Email.Value));

        return Result.Success();
    }

    /// <summary>
    /// Replaces the user's credential and invalidates every existing session.
    /// </summary>
    /// <param name="newHash">The new password hash.</param>
    /// <param name="now">Current time.</param>
    /// <param name="changedByAdministrator">True for an administrative reset.</param>
    /// <remarks>
    /// Rotating the security stamp and revoking refresh tokens is the point of changing a password
    /// after a suspected compromise. Leaving existing sessions alive would mean the attacker keeps
    /// the access they already have, and the user believes they have removed it.
    /// </remarks>
    public void ChangePassword(PasswordHash newHash, DateTimeOffset now, bool changedByAdministrator = false)
    {
        ArgumentNullException.ThrowIfNull(newHash);

        PasswordHash = newHash;
        RotateSecurityStamp();

        FailedSignInAttempts = 0;
        LockoutEndsOnUtc = null;

        RevokeAllRefreshTokens("Password changed", now);

        RaiseDomainEvent(new UserPasswordChanged(Id, OrganizationId, changedByAdministrator));
    }

    /// <summary>Grants a role to the user.</summary>
    public Result AssignRole(Guid roleId)
    {
        if (roleId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("User.InvalidRole", "A role id is required."));
        }

        if (_roleIds.Contains(roleId))
        {
            return Result.Failure(Error.Conflict(
                "User.RoleAlreadyAssigned",
                "The user already holds this role."));
        }

        _roleIds.Add(roleId);

        // A permission change must invalidate tokens that still carry the old permission set,
        // otherwise a revoked capability lingers until the access token expires.
        RotateSecurityStamp();

        RaiseDomainEvent(new RoleAssignedToUser(Id, OrganizationId, roleId));

        return Result.Success();
    }

    /// <summary>Withdraws a role from the user.</summary>
    public Result RemoveRole(Guid roleId)
    {
        if (!_roleIds.Remove(roleId))
        {
            return Result.Failure(Error.NotFound(
                "User.RoleNotAssigned",
                "The user does not hold this role."));
        }

        RotateSecurityStamp();

        RaiseDomainEvent(new RoleRemovedFromUser(Id, OrganizationId, roleId));

        return Result.Success();
    }

    /// <summary>Disables the account and ends every session.</summary>
    public Result Deactivate(DateTimeOffset now, string? reason = null)
    {
        if (Status == UserStatus.Deactivated)
        {
            return Result.Failure(Error.Conflict(
                "User.AlreadyDeactivated",
                "This account is already deactivated."));
        }

        Status = UserStatus.Deactivated;
        RotateSecurityStamp();
        RevokeAllRefreshTokens(reason ?? "Account deactivated", now);

        RaiseDomainEvent(new UserDeactivated(Id, OrganizationId, reason));

        return Result.Success();
    }

    /// <summary>Restores a deactivated account.</summary>
    public Result Reactivate()
    {
        if (Status != UserStatus.Deactivated)
        {
            return Result.Failure(Error.Conflict(
                "User.NotDeactivated",
                "Only a deactivated account can be reactivated."));
        }

        // Returns to Pending when the address was never confirmed, so reactivation cannot be used
        // to bypass the confirmation step the account never completed.
        Status = EmailConfirmed ? UserStatus.Active : UserStatus.Pending;
        FailedSignInAttempts = 0;
        LockoutEndsOnUtc = null;

        RaiseDomainEvent(new UserReactivated(Id, OrganizationId));

        return Result.Success();
    }

    /// <summary>Adds a newly issued refresh token to the user's chain.</summary>
    public void AddRefreshToken(RefreshToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        _refreshTokens.Add(token);
    }

    /// <summary>
    /// Exchanges an active refresh token for a new one, detecting replay of a rotated token.
    /// </summary>
    /// <param name="presentedTokenHash">Hash of the token the client presented.</param>
    /// <param name="replacementTokenHash">Hash of the token to issue.</param>
    /// <param name="now">Current time.</param>
    /// <param name="lifetime">Lifetime of the replacement token.</param>
    /// <param name="ipAddress">Requesting IP address.</param>
    /// <remarks>
    /// <para>
    /// Rotation on every use is what bounds the damage of a leaked refresh token: a stolen token is
    /// useful only until the legitimate client next refreshes.
    /// </para>
    /// <para>
    /// Reuse detection is the other half. A correctly behaving client discards a token the moment it
    /// exchanges it, so seeing an already-rotated token means two parties hold it. The response is
    /// to revoke the entire chain, signing out both. Signing out a legitimate user is a minor
    /// annoyance; leaving an attacker with an indefinitely renewable session is not.
    /// </para>
    /// </remarks>
    public Result<RefreshToken> RotateRefreshToken(
        string presentedTokenHash,
        string replacementTokenHash,
        DateTimeOffset now,
        TimeSpan lifetime,
        string? ipAddress)
    {
        var presented = _refreshTokens.SingleOrDefault(t =>
            string.Equals(t.TokenHash, presentedTokenHash, StringComparison.Ordinal));

        if (presented is null)
        {
            return Result.Failure<RefreshToken>(Error.Unauthorized(
                "RefreshToken.NotFound",
                "The refresh token is not recognised."));
        }

        if (presented.WasRotated)
        {
            var revoked = RevokeAllRefreshTokens("Refresh token reuse detected", now);

            RaiseDomainEvent(new RefreshTokenReuseDetected(Id, OrganizationId, ipAddress, revoked));

            return Result.Failure<RefreshToken>(Error.Unauthorized(
                "RefreshToken.Reused",
                "This session has been terminated for security reasons. Please sign in again."));
        }

        if (!presented.IsActive(now))
        {
            return Result.Failure<RefreshToken>(Error.Unauthorized(
                "RefreshToken.Inactive",
                "The refresh token has expired or been revoked."));
        }

        if (!CanSignIn(now))
        {
            return Result.Failure<RefreshToken>(Error.Unauthorized(
                "User.CannotSignIn",
                "This account is not permitted to sign in."));
        }

        var replacement = RefreshToken.Issue(replacementTokenHash, now, lifetime, ipAddress);

        presented.Rotate(replacementTokenHash, now);
        _refreshTokens.Add(replacement);

        return Result.Success(replacement);
    }

    /// <summary>Revokes every active refresh token, returning how many were revoked.</summary>
    public int RevokeAllRefreshTokens(string reason, DateTimeOffset now)
    {
        var revoked = 0;

        foreach (var token in _refreshTokens.Where(t => t.IsActive(now)))
        {
            token.Revoke(reason, now);
            revoked++;
        }

        return revoked;
    }

    /// <summary>
    /// Discards revoked and expired tokens older than the retention window.
    /// </summary>
    /// <remarks>
    /// Called by a maintenance job. Without pruning, the token collection grows without bound and
    /// is loaded in full on every sign-in, so an account used daily for two years eventually makes
    /// its own authentication slow. History is kept for a window because reuse detection and
    /// incident forensics both need to look backwards.
    /// </remarks>
    public int PruneRefreshTokens(DateTimeOffset now, TimeSpan retention)
    {
        var cutoff = now - retention;

        return _refreshTokens.RemoveAll(t =>
            !t.IsActive(now) && t.IssuedOnUtc < cutoff);
    }

    private void RotateSecurityStamp() => SecurityStamp = Guid.CreateVersion7().ToString("N");
}
