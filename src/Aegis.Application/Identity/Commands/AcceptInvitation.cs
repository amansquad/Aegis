using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Abstractions.Requests;
using Aegis.Application.Abstractions.Security;
using Aegis.Application.Identity.Contracts;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using Aegis.Domain.Identity;
using Aegis.Domain.Identity.ValueObjects;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.Identity.Commands;

/// <summary>Accepts an invitation, creating the account and signing the new user in.</summary>
/// <remarks>
/// Anonymous by necessity: the invitee has no account yet, so there is nothing to authenticate
/// against. The token is the credential.
/// </remarks>
/// <param name="Token">The single-use invitation token from the emailed link.</param>
/// <param name="Password">The password the invitee chooses.</param>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
public sealed record AcceptInvitationCommand(
    string Token,
    string Password,
    string FirstName,
    string LastName) : ICommand<AuthenticationResultDto>;

/// <summary>Validates <see cref="AcceptInvitationCommand"/>.</summary>
public sealed class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    /// <summary>Initialises the validator.</summary>
    public AcceptInvitationCommandValidator(IPasswordPolicy passwordPolicy)
    {
        ArgumentNullException.ThrowIfNull(passwordPolicy);

        RuleFor(c => c.Token).NotEmpty().MaximumLength(512);
        RuleFor(c => c.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.LastName).NotEmpty().MaximumLength(100);

        RuleFor(c => c.Password)
            .NotEmpty()
            .Custom((password, context) =>
            {
                if (string.IsNullOrEmpty(password))
                {
                    return;
                }

                var command = context.InstanceToValidate;

                // The invitee's email is not screened against here because it is not in the
                // request — it comes from the invitation. The handler cannot re-run validation, so
                // this is a known, narrow gap: a password built from the invited address passes.
                // Closing it properly means screening inside the handler once the address is known.
                var screening = passwordPolicy.Screen(password, command.FirstName, command.LastName);

                if (!screening.IsAcceptable)
                {
                    context.AddFailure(nameof(AcceptInvitationCommand.Password), screening.Reason!);
                }
            });
    }
}

/// <summary>Handles <see cref="AcceptInvitationCommand"/>.</summary>
internal sealed class AcceptInvitationCommandHandler(
    IAegisDbContext context,
    ITenantContext tenantContext,
    IPasswordHasher passwordHasher,
    IPasswordPolicy passwordPolicy,
    ITokenService tokenService,
    IRequestContext requestContext,
    TimeProvider timeProvider) : ICommandHandler<AcceptInvitationCommand, AuthenticationResultDto>
{
    /// <inheritdoc />
    public async Task<Result<AuthenticationResultDto>> Handle(
        AcceptInvitationCommand request,
        CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.HashRefreshToken(request.Token);

        // No tenant established: the invitee has no token and no account. The invitation itself
        // identifies the organization, which is why the filter must be bypassed here.
        var invitation = await context.Set<UserInvitation>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

        // One error for unknown, expired and revoked alike. Distinguishing them would let someone
        // holding a random token learn whether it ever existed.
        if (invitation is null)
        {
            return Result.Failure<AuthenticationResultDto>(Error.NotFound(
                "Invitation.NotFound",
                "This invitation link is not valid. Ask an administrator to send a new one."));
        }

        var now = timeProvider.GetUtcNow();

        if (!invitation.IsAcceptable(now))
        {
            return Result.Failure<AuthenticationResultDto>(Error.NotFound(
                "Invitation.NotFound",
                "This invitation link is not valid. Ask an administrator to send a new one."));
        }

        // Screened again now that the invited address is known, closing the gap the validator
        // cannot reach: a password built from the invitee's own email address.
        var screening = passwordPolicy.Screen(request.Password, invitation.Email.Value);

        if (!screening.IsAcceptable)
        {
            return Result.Failure<AuthenticationResultDto>(Error.Validation(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [nameof(AcceptInvitationCommand.Password)] = [screening.Reason!],
                }));
        }

        tenantContext.SetTenant(invitation.OrganizationId);

        var organization = await context.Organizations
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(o => o.Id == invitation.OrganizationId, cancellationToken);

        if (organization is null || !organization.IsOperational)
        {
            return Result.Failure<AuthenticationResultDto>(IdentityErrors.OrganizationSuspended);
        }

        // Guards the race in which the same address is invited twice and both links are opened, or
        // an account is created by another route between issuing and accepting.
        var address = invitation.Email;

        var alreadyExists = await context.Users
            .IgnoreQueryFilters()
            .AnyAsync(
                u => u.Email == address && u.OrganizationId == invitation.OrganizationId,
                cancellationToken);

        if (alreadyExists)
        {
            return Result.Failure<AuthenticationResultDto>(IdentityErrors.EmailAlreadyRegistered);
        }

        var user = User.RegisterFromInvitation(
            invitation.OrganizationId,
            invitation.Email,
            PasswordHash.FromEncoded(passwordHasher.Hash(request.Password)),
            request.FirstName,
            request.LastName,
            invitation.RoleIds);

        var accepted = invitation.Accept(user.Id, now);

        if (accepted.IsFailure)
        {
            return Result.Failure<AuthenticationResultDto>(accepted.Error);
        }

        var refreshToken = tokenService.IssueRefreshToken();

        user.AddRefreshToken(RefreshToken.Issue(
            refreshToken.Hash,
            now,
            tokenService.RefreshTokenLifetime,
            requestContext.IpAddress));

        user.RecordSuccessfulSignIn(now);

        context.Users.Add(user);

        await context.SaveChangesAsync(cancellationToken);

        var roleIds = user.RoleIds.ToArray();

        var roles = await context.Roles
            .AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .ToListAsync(cancellationToken);

        var permissions = roles
            .SelectMany(r => r.Permissions)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        var accessToken = tokenService.IssueAccessToken(user, permissions);

        return Result.Success(new AuthenticationResultDto(
            accessToken.Value,
            refreshToken.Value,
            accessToken.ExpiresOnUtc,
            AuthenticationResultDto.BearerTokenType,
            new AuthenticatedUserDto(
                user.Id,
                user.Email.Value,
                user.DisplayName,
                organization.Id,
                organization.Name,
                roles.Select(r => r.Name).ToArray(),
                permissions)));
    }
}
