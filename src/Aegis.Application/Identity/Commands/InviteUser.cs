using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Application.Abstractions.Notifications;
using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Abstractions.Security;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using Aegis.Domain.Identity;
using Aegis.Domain.Identity.ValueObjects;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aegis.Application.Identity.Commands;

/// <summary>Invitation settings, bound from the <c>Invitations</c> configuration section.</summary>
public sealed class InvitationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Invitations";

    /// <summary>How long an invitation remains acceptable, in days.</summary>
    /// <remarks>
    /// Bounded because an invitation link is a standing credential. Left in a mailbox, a forwarded
    /// thread or a mailing list archive, an unexpiring link is a permanent way into the tenant for
    /// anyone who later reads it.
    /// </remarks>
    public int LifetimeDays { get; set; } = 7;

    /// <summary>Base URL the invitee is sent to, with the token appended.</summary>
    public string AcceptUrlTemplate { get; set; } = "https://localhost:3000/accept-invitation";
}

/// <summary>Invites someone to join the current organization.</summary>
/// <param name="Email">The address to invite.</param>
/// <param name="RoleIds">Roles the invitee receives on acceptance.</param>
public sealed record InviteUserCommand(string Email, IReadOnlyCollection<Guid> RoleIds)
    : ICommand<Guid>;

/// <summary>Validates <see cref="InviteUserCommand"/>.</summary>
public sealed class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
{
    /// <summary>Initialises the validator.</summary>
    public InviteUserCommandValidator()
    {
        RuleFor(c => c.Email)
            .NotEmpty().WithMessage("An email address is required.")
            .MaximumLength(EmailAddress.MaxLength)
            .Must(email => EmailAddress.Create(email).IsSuccess)
            .WithMessage("The email address is not in a valid format.");

        RuleFor(c => c.RoleIds)
            .NotNull()
            .Must(roles => roles.Count > 0)
            .WithMessage("At least one role must be granted, otherwise the invitee can do nothing.")
            .Must(roles => roles.Count <= 20)
            .WithMessage("An invitation cannot grant more than 20 roles.");
    }
}

/// <summary>Handles <see cref="InviteUserCommand"/>.</summary>
internal sealed class InviteUserCommandHandler(
    IAegisDbContext context,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ITokenService tokenService,
    IEmailSender emailSender,
    IOptions<InvitationOptions> options,
    TimeProvider timeProvider) : ICommandHandler<InviteUserCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(InviteUserCommand request, CancellationToken cancellationToken)
    {
        var email = EmailAddress.Create(request.Email);

        if (email.IsFailure)
        {
            return Result.Failure<Guid>(email.Error);
        }

        var organizationId = tenantContext.RequireOrganizationId();
        var address = email.Value;

        // Scoped by the global query filter, so this only sees the current organization. The same
        // address may legitimately hold an account at another operator.
        var alreadyAMember = await context.Users.AnyAsync(u => u.Email == address, cancellationToken);

        if (alreadyAMember)
        {
            return Result.Failure<Guid>(IdentityErrors.EmailAlreadyRegistered);
        }

        var roleIds = request.RoleIds.Distinct().ToArray();

        // Verified against roles visible to this tenant, so an administrator cannot grant a role
        // belonging to another organization by guessing its identifier.
        var knownRoleCount = await context.Roles
            .CountAsync(r => roleIds.Contains(r.Id), cancellationToken);

        if (knownRoleCount != roleIds.Length)
        {
            return Result.Failure<Guid>(Error.Validation(
                "Invitation.UnknownRole",
                "One or more of the specified roles does not exist in this organization."));
        }

        var now = timeProvider.GetUtcNow();

        // Reuses the refresh token generator: both need an opaque, high-entropy, single-use value
        // whose hash is what gets stored. Inventing a second scheme would mean a second chance to
        // get the entropy or the hashing wrong.
        var token = tokenService.IssueRefreshToken();

        // Any outstanding invitation for this address is withdrawn first, so a resend cannot leave
        // two live links to the same inbox.
        var superseded = await context.Set<UserInvitation>()
            .Where(i => i.Email == address && i.Status == InvitationStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var previous in superseded)
        {
            previous.Revoke();
        }

        var invitation = UserInvitation.Issue(
            organizationId,
            address,
            token.Hash,
            currentUser.Id ?? Guid.Empty,
            roleIds,
            now,
            TimeSpan.FromDays(options.Value.LifetimeDays));

        if (invitation.IsFailure)
        {
            return Result.Failure<Guid>(invitation.Error);
        }

        context.Set<UserInvitation>().Add(invitation.Value);

        await context.SaveChangesAsync(cancellationToken);

        // Sent after the save so an email cannot advertise an invitation that failed to persist.
        // The reverse ordering produces a link that returns "not found" to a confused recipient.
        await emailSender.SendAsync(
            BuildInvitationEmail(address.Value, token.Value, options.Value, invitation.Value.ExpiresOnUtc),
            cancellationToken);

        return Result.Success(invitation.Value.Id);
    }

    private static EmailMessage BuildInvitationEmail(
        string recipient,
        string token,
        InvitationOptions options,
        DateTimeOffset expiresOnUtc) =>
        new(
            recipient,
            "You have been invited to Aegis",
            $"""
             You have been invited to join an organization on Aegis.

             Accept the invitation: {options.AcceptUrlTemplate}?token={Uri.EscapeDataString(token)}

             This link expires on {expiresOnUtc:u} and can be used once.
             If you were not expecting this invitation, ignore this message.
             """);
}

/// <summary>Withdraws a pending invitation.</summary>
/// <param name="InvitationId">The invitation to withdraw.</param>
public sealed record RevokeInvitationCommand(Guid InvitationId) : ICommand;

/// <summary>Handles <see cref="RevokeInvitationCommand"/>.</summary>
internal sealed class RevokeInvitationCommandHandler(IAegisDbContext context)
    : ICommandHandler<RevokeInvitationCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(RevokeInvitationCommand request, CancellationToken cancellationToken)
    {
        // Tenant-filtered, so an invitation belonging to another organization is simply not found
        // rather than reported as forbidden — which would confirm it exists.
        var invitation = await context.Set<UserInvitation>()
            .SingleOrDefaultAsync(i => i.Id == request.InvitationId, cancellationToken);

        if (invitation is null)
        {
            return Result.Failure(Error.NotFound(
                "Invitation.NotFound",
                "The invitation was not found."));
        }

        var revoked = invitation.Revoke();

        if (revoked.IsFailure)
        {
            return revoked;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
