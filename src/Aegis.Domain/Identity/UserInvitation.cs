using Aegis.Domain.Abstractions;
using Aegis.Domain.Common;
using Aegis.Domain.Identity.Events;
using Aegis.Domain.Identity.ValueObjects;

namespace Aegis.Domain.Identity;

/// <summary>Lifecycle state of an invitation.</summary>
public enum InvitationStatus
{
    /// <summary>Issued and awaiting acceptance.</summary>
    Pending = 0,

    /// <summary>Accepted; a user account now exists.</summary>
    Accepted = 1,

    /// <summary>Withdrawn by an administrator before acceptance.</summary>
    Revoked = 2,
}

/// <summary>
/// An offer to join an organization, addressed to an email address.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is also the email confirmation mechanism.</b> A separate confirm-your-address step would
/// be redundant: the invitation link is delivered to the address, so presenting the token proves
/// control of that inbox. Accepting therefore creates an already-confirmed account, and there is
/// one flow to build, test and explain rather than two.
/// </para>
/// <para>
/// The token is stored only as a hash, exactly as refresh tokens are. An invitation grants the
/// ability to create an account inside a tenant, so a readable token column would turn a database
/// backup or a single injection flaw into a way to join any customer's organization.
/// </para>
/// </remarks>
public sealed class UserInvitation : AggregateRoot<Guid>, ITenantOwned, IAuditableEntity
{
    private readonly List<Guid> _roleIds = [];

    /// <summary>Parameterless constructor required by EF Core materialisation.</summary>
    private UserInvitation()
    {
        Email = null!;
        TokenHash = string.Empty;
    }

    private UserInvitation(
        Guid id,
        Guid organizationId,
        EmailAddress email,
        string tokenHash,
        Guid invitedBy,
        DateTimeOffset issuedOnUtc,
        DateTimeOffset expiresOnUtc) : base(id)
    {
        OrganizationId = organizationId;
        Email = email;
        TokenHash = tokenHash;
        InvitedBy = invitedBy;
        IssuedOnUtc = issuedOnUtc;
        ExpiresOnUtc = expiresOnUtc;
        Status = InvitationStatus.Pending;
    }

    /// <inheritdoc />
    public Guid OrganizationId { get; private set; }

    /// <summary>The address the invitation was sent to.</summary>
    public EmailAddress Email { get; private set; }

    /// <summary>Hash of the single-use token. The token itself is never stored.</summary>
    public string TokenHash { get; private set; }

    /// <summary>The administrator who issued the invitation.</summary>
    public Guid InvitedBy { get; private set; }

    /// <summary>When the invitation was issued.</summary>
    public DateTimeOffset IssuedOnUtc { get; private set; }

    /// <summary>
    /// When the invitation stops being acceptable.
    /// </summary>
    /// <remarks>
    /// Bounded because an invitation is a standing credential. An unexpiring link sitting in a
    /// mailbox — or in a forwarded thread, or a mailing list archive — is a permanent way into the
    /// organization for anyone who later reads it.
    /// </remarks>
    public DateTimeOffset ExpiresOnUtc { get; private set; }

    /// <summary>Current state.</summary>
    public InvitationStatus Status { get; private set; }

    /// <summary>When the invitation was accepted, if it was.</summary>
    public DateTimeOffset? AcceptedOnUtc { get; private set; }

    /// <summary>The user account created on acceptance.</summary>
    public Guid? AcceptedByUserId { get; private set; }

    /// <summary>Roles the invitee receives on acceptance.</summary>
    public IReadOnlyCollection<Guid> RoleIds => _roleIds.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedOnUtc { get; set; }

    /// <inheritdoc />
    public Guid? ModifiedBy { get; set; }

    /// <summary>True when the invitation can still be accepted.</summary>
    public bool IsAcceptable(DateTimeOffset now) =>
        Status == InvitationStatus.Pending && now < ExpiresOnUtc;

    /// <summary>Issues an invitation.</summary>
    /// <param name="organizationId">The inviting organization.</param>
    /// <param name="email">The address to invite.</param>
    /// <param name="tokenHash">Hash of the generated single-use token.</param>
    /// <param name="invitedBy">The administrator issuing the invitation.</param>
    /// <param name="roleIds">Roles to grant on acceptance.</param>
    /// <param name="issuedOnUtc">Current time.</param>
    /// <param name="lifetime">How long the invitation remains valid.</param>
    public static Result<UserInvitation> Issue(
        Guid organizationId,
        EmailAddress email,
        string tokenHash,
        Guid invitedBy,
        IEnumerable<Guid> roleIds,
        DateTimeOffset issuedOnUtc,
        TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(roleIds);

        DomainException.RequireNotBlank(tokenHash, "Invitation.TokenRequired", "A token hash is required.");

        if (lifetime <= TimeSpan.Zero)
        {
            return Result.Failure<UserInvitation>(Error.Validation(
                "Invitation.InvalidLifetime",
                "An invitation lifetime must be positive."));
        }

        var invitation = new UserInvitation(
            Guid.CreateVersion7(),
            organizationId,
            email,
            tokenHash,
            invitedBy,
            issuedOnUtc,
            issuedOnUtc.Add(lifetime));

        invitation._roleIds.AddRange(roleIds.Distinct());

        invitation.RaiseDomainEvent(new UserInvited(
            invitation.Id,
            organizationId,
            email.Value,
            invitedBy,
            invitation.ExpiresOnUtc));

        return Result.Success(invitation);
    }

    /// <summary>Marks the invitation accepted by a newly created user.</summary>
    public Result Accept(Guid userId, DateTimeOffset now)
    {
        if (Status == InvitationStatus.Accepted)
        {
            return Result.Failure(Error.Conflict(
                "Invitation.AlreadyAccepted",
                "This invitation has already been used."));
        }

        if (Status == InvitationStatus.Revoked)
        {
            return Result.Failure(Error.NotFound(
                "Invitation.Revoked",
                "This invitation is no longer valid."));
        }

        if (now >= ExpiresOnUtc)
        {
            return Result.Failure(Error.NotFound(
                "Invitation.Expired",
                "This invitation has expired. Ask an administrator to send a new one."));
        }

        Status = InvitationStatus.Accepted;
        AcceptedOnUtc = now;
        AcceptedByUserId = userId;

        RaiseDomainEvent(new InvitationAccepted(Id, OrganizationId, userId, Email.Value));

        return Result.Success();
    }

    /// <summary>Withdraws an unaccepted invitation.</summary>
    public Result Revoke()
    {
        if (Status != InvitationStatus.Pending)
        {
            return Result.Failure(Error.Conflict(
                "Invitation.NotPending",
                "Only a pending invitation can be revoked."));
        }

        Status = InvitationStatus.Revoked;

        return Result.Success();
    }
}
