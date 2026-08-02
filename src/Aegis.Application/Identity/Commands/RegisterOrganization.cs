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
public sealed class RegisterOrganizationCommandValidator : AbstractValidator<RegisterOrganizationCommand>
{
    /// <summary>Minimum password length.</summary>
    /// <remarks>
    /// <para>
    /// Twelve characters, with no composition rules beyond that. NIST SP 800-63B withdrew the
    /// mandatory-symbol advice because it reliably produces <c>Password1!</c> — users satisfy the
    /// checker rather than the intent, and the result is shorter and more predictable than a
    /// passphrase would have been. Length is the property that actually resists offline cracking.
    /// </para>
    /// <para>
    /// Screening against known-breached password lists is the genuinely effective control and is
    /// planned; it belongs in an adapter with a real corpus behind it, not in a regex here.
    /// </para>
    /// </remarks>
    public const int MinimumPasswordLength = 12;

    /// <summary>Initialises the validator.</summary>
    public RegisterOrganizationCommandValidator()
    {
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

        RuleFor(c => c.Password)
            .NotEmpty().WithMessage("A password is required.")
            .MinimumLength(MinimumPasswordLength)
            .WithMessage($"A password must be at least {MinimumPasswordLength} characters.")
            .MaximumLength(256)
            .WithMessage("A password cannot exceed 256 characters.");

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
