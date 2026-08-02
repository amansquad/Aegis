using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Abstractions.Security;
using Aegis.Application.Identity.Contracts;
using Aegis.Application.Messaging;
using Aegis.Domain.Common;
using Aegis.Domain.Identity;
using Aegis.Domain.Identity.ValueObjects;
using Aegis.Domain.Organizations;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.Identity.Commands;

/// <summary>
/// Registers a new organization together with its first administrator.
/// </summary>
/// <remarks>
/// Self-service sign-up. Organization and administrator are created in one command because neither
/// is usable alone: an organization with no members cannot be administered, and a user must belong
/// to an organization before any tenant-owned row can be written for them. Splitting them would
/// create a window in which a half-provisioned tenant exists.
/// </remarks>
/// <param name="OrganizationName">Display name of the organization.</param>
/// <param name="Kind">Kind of infrastructure operated.</param>
/// <param name="TimeZoneId">Time zone identifier for the operating region.</param>
/// <param name="Email">The administrator's email address.</param>
/// <param name="Password">The administrator's chosen password.</param>
/// <param name="FirstName">The administrator's given name.</param>
/// <param name="LastName">The administrator's family name.</param>
public sealed record RegisterOrganizationCommand(
    string OrganizationName,
    OrganizationKind Kind,
    string TimeZoneId,
    string Email,
    string Password,
    string FirstName,
    string LastName) : ICommand<AuthenticationResultDto>;

/// <summary>Validates <see cref="RegisterOrganizationCommand"/>.</summary>
/// <remarks>
/// Password screening runs here rather than in the handler so that a weak password comes back as a
/// field-level validation error the form can render beside the input, alongside any other problems
/// with the request, rather than as a lone rejection after everything else has been accepted.
/// </remarks>
public sealed class RegisterOrganizationCommandValidator : AbstractValidator<RegisterOrganizationCommand>
{
    /// <summary>Initialises the validator.</summary>
    /// <param name="passwordPolicy">Screens the proposed password.</param>
    public RegisterOrganizationCommandValidator(IPasswordPolicy passwordPolicy)
    {
        ArgumentNullException.ThrowIfNull(passwordPolicy);

        RuleFor(c => c.OrganizationName)
            .NotEmpty().WithMessage("An organization name is required.")
            .MaximumLength(200);

        RuleFor(c => c.TimeZoneId)
            .NotEmpty().WithMessage("A time zone is required.");

        RuleFor(c => c.Email)
            .NotEmpty().WithMessage("An email address is required.")
            .MaximumLength(EmailAddress.MaxLength)
            .Must(email => EmailAddress.Create(email).IsSuccess)
            .WithMessage("The email address is not in a valid format.");

        // Screened against the caller's own details as well as the banned list, so a password built
        // from the organization name or the administrator's email is rejected. Anyone targeting
        // this specific person already knows all three.
        RuleFor(c => c.Password)
            .NotEmpty().WithMessage("A password is required.")
            .Custom((password, context) =>
            {
                if (string.IsNullOrEmpty(password))
                {
                    return;
                }

                var command = context.InstanceToValidate;

                var screening = passwordPolicy.Screen(
                    password,
                    command.Email,
                    command.OrganizationName,
                    command.FirstName,
                    command.LastName);

                if (!screening.IsAcceptable)
                {
                    context.AddFailure(nameof(RegisterOrganizationCommand.Password), screening.Reason!);
                }
            });

        RuleFor(c => c.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.LastName).NotEmpty().MaximumLength(100);
    }
}

/// <summary>Handles <see cref="RegisterOrganizationCommand"/>.</summary>
internal sealed class RegisterOrganizationCommandHandler(
    IAegisDbContext context,
    ITenantContext tenantContext,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider)
    : ICommandHandler<RegisterOrganizationCommand, AuthenticationResultDto>
{
    /// <inheritdoc />
    public async Task<Result<AuthenticationResultDto>> Handle(
        RegisterOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        var email = EmailAddress.Create(request.Email);

        if (email.IsFailure)
        {
            return Result.Failure<AuthenticationResultDto>(email.Error);
        }

        var organization = Organization.Register(
            request.OrganizationName,
            request.Kind,
            request.TimeZoneId);

        if (organization.IsFailure)
        {
            return Result.Failure<AuthenticationResultDto>(organization.Error);
        }

        // IgnoreQueryFilters is correct and necessary here: slugs are unique platform-wide, and at
        // this point no tenant is established, so the filter would hide every existing row and let
        // a duplicate through. One of the few legitimate uses of the escape hatch.
        var slugTaken = await context.Organizations
            .IgnoreQueryFilters()
            .AnyAsync(o => o.Slug == organization.Value.Slug, cancellationToken);

        if (slugTaken)
        {
            return Result.Failure<AuthenticationResultDto>(IdentityErrors.SlugAlreadyTaken);
        }

        context.Organizations.Add(organization.Value);

        // Establishes the tenant for the rest of this unit of work, so the persistence interceptor
        // stamps the new user and roles with the organization that is being created in the same
        // transaction.
        tenantContext.SetTenant(organization.Value.Id);

        var roles = SeedSystemRoles(organization.Value.Id);

        var administrator = User.Register(
            organization.Value.Id,
            email.Value,
            PasswordHash.FromEncoded(passwordHasher.Hash(request.Password)),
            request.FirstName,
            request.LastName);

        // The first user of a new organization is confirmed immediately. There is nobody else who
        // could confirm them, and leaving the account pending would lock the tenant out of itself.
        administrator.ConfirmEmail();

        var administratorRole = roles.Single(r => r.Name == SystemRoles.Administrator);
        administrator.AssignRole(administratorRole.Id);

        context.Users.Add(administrator);

        var now = timeProvider.GetUtcNow();
        var refreshToken = tokenService.IssueRefreshToken();

        administrator.AddRefreshToken(Domain.Identity.RefreshToken.Issue(
            refreshToken.Hash,
            now,
            tokenService.RefreshTokenLifetime,
            issuedToIpAddress: null));

        administrator.RecordSuccessfulSignIn(now);

        await context.SaveChangesAsync(cancellationToken);

        var permissions = administratorRole.Permissions.ToArray();
        var accessToken = tokenService.IssueAccessToken(administrator, permissions);

        return Result.Success(new AuthenticationResultDto(
            accessToken.Value,
            refreshToken.Value,
            accessToken.ExpiresOnUtc,
            AuthenticationResultDto.BearerTokenType,
            new AuthenticatedUserDto(
                administrator.Id,
                administrator.Email.Value,
                administrator.DisplayName,
                organization.Value.Id,
                organization.Value.Name,
                [SystemRoles.Administrator],
                permissions)));
    }

    /// <summary>
    /// Creates the organization's own copy of the seeded roles.
    /// </summary>
    /// <remarks>
    /// Per-organization copies rather than shared global rows, because a water utility's idea of a
    /// Supervisor is not a road authority's. Each tenant can then edit its roles without affecting
    /// anyone else.
    /// </remarks>
    private List<Role> SeedSystemRoles(Guid organizationId)
    {
        var roles = new List<Role>();

        foreach (var name in SystemRoles.DefaultPermissions.Keys)
        {
            var role = Role.CreateSystemRole(organizationId, name);

            if (role.IsSuccess)
            {
                roles.Add(role.Value);
                context.Roles.Add(role.Value);
            }
        }

        return roles;
    }
}
