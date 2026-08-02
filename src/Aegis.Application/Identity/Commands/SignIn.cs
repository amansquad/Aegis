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

/// <summary>Authenticates a user with an email address and password.</summary>
/// <remarks>
/// <para>
/// <b>Opts out of the ambient transaction, and must.</b> <c>UnitOfWorkBehavior</c> rolls back when
/// a handler returns a failed <see cref="Result"/>, which is normally exactly right — a rejected
/// command should leave nothing behind.
/// </para>
/// <para>
/// Sign-in is the exception. A wrong password returns a failure <em>and</em> must durably record
/// the failed attempt, because that counter is what produces the lockout. Under the ambient
/// transaction the increment is rolled back with the rejection, the counter never advances, and
/// brute-force protection silently does not exist. The integration suite caught precisely this.
/// </para>
/// <para>
/// The handler performs a single <c>SaveChangesAsync</c>, which is atomic in its own right, so
/// nothing is lost by managing persistence here.
/// </para>
/// </remarks>
/// <param name="Email">The user's email address.</param>
/// <param name="Password">The supplied password.</param>
public sealed record SignInCommand(string Email, string Password)
    : ICommand<AuthenticationResultDto>, ITransactionless;

/// <summary>Validates <see cref="SignInCommand"/>.</summary>
/// <remarks>
/// Checks presence only. Applying the registration password policy here would reject a
/// legitimately-set older password and, worse, would let an attacker infer the policy — and
/// therefore the search space — without holding an account.
/// </remarks>
public sealed class SignInCommandValidator : AbstractValidator<SignInCommand>
{
    /// <summary>Initialises the validator.</summary>
    public SignInCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(EmailAddress.MaxLength);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(256);
    }
}

/// <summary>Handles <see cref="SignInCommand"/>.</summary>
internal sealed class SignInCommandHandler(
    IAegisDbContext context,
    ITenantContext tenantContext,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IRequestContext requestContext,
    TimeProvider timeProvider)
    : ICommandHandler<SignInCommand, AuthenticationResultDto>
{
    /// <summary>Failed attempts tolerated before the account is locked.</summary>
    private const int MaxFailedAttempts = 5;

    /// <summary>How long a lockout lasts.</summary>
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// A structurally valid hash of a password nobody holds.
    /// </summary>
    /// <remarks>
    /// Verified against when no user matches, so that the unknown-address path costs the same key
    /// derivation as the wrong-password path. Without it the response time distinguishes the two
    /// regardless of the identical message, and account enumeration proceeds by stopwatch.
    /// </remarks>
    private const string DummyHash =
        "PBKDF2-SHA256$600000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    /// <inheritdoc />
    public async Task<Result<AuthenticationResultDto>> Handle(
        SignInCommand request,
        CancellationToken cancellationToken)
    {
        var email = EmailAddress.Create(request.Email);

        if (email.IsFailure)
        {
            // A malformed address is reported as bad credentials, not as a validation error. The
            // login endpoint should reveal nothing about which inputs are even well-formed.
            return Result.Failure<AuthenticationResultDto>(IdentityErrors.InvalidCredentials);
        }

        // Compared as the value object, not as `u.Email.Value`. The property is mapped through a
        // value converter, so EF Core translates equality on the whole value into a comparison of
        // the converted column; reaching into `.Value` is not translatable and throws at runtime.
        var address = email.Value;

        // No tenant is established at sign-in — the caller has no token yet — so the global filter
        // would exclude every user. The lookup is by email, which is unique platform-wide and is
        // itself what identifies the tenant here.
        var user = await context.Users
            .IgnoreQueryFilters()
            .Include(u => u.RefreshTokens)
            .SingleOrDefaultAsync(u => u.Email == address && !u.IsDeleted, cancellationToken);

        if (user is null)
        {
            passwordHasher.Verify(request.Password, DummyHash);
            return Result.Failure<AuthenticationResultDto>(IdentityErrors.InvalidCredentials);
        }

        var now = timeProvider.GetUtcNow();

        if (user.IsLockedOut(now))
        {
            return Result.Failure<AuthenticationResultDto>(IdentityErrors.AccountLocked);
        }

        var verification = passwordHasher.Verify(request.Password, user.PasswordHash.Value);

        if (verification == PasswordVerificationResult.Failed)
        {
            user.RecordFailedSignIn(now, MaxFailedAttempts, LockoutDuration);
            await context.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthenticationResultDto>(
                user.IsLockedOut(now) ? IdentityErrors.AccountLocked : IdentityErrors.InvalidCredentials);
        }

        if (!user.CanSignIn(now))
        {
            return Result.Failure<AuthenticationResultDto>(IdentityErrors.AccountNotActive);
        }

        var organization = await context.Organizations
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(o => o.Id == user.OrganizationId, cancellationToken);

        if (organization is null || !organization.IsOperational)
        {
            return Result.Failure<AuthenticationResultDto>(IdentityErrors.OrganizationSuspended);
        }

        tenantContext.SetTenant(user.OrganizationId);

        // The only moment the plaintext is available, so it is the only moment an outdated hash can
        // be upgraded. Skipping this means the iteration count can never rise without a mass reset.
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.ChangePassword(
                PasswordHash.FromEncoded(passwordHasher.Hash(request.Password)),
                now);
        }

        var refreshToken = tokenService.IssueRefreshToken();

        user.AddRefreshToken(Domain.Identity.RefreshToken.Issue(
            refreshToken.Hash,
            now,
            tokenService.RefreshTokenLifetime,
            requestContext.IpAddress));

        var signIn = user.RecordSuccessfulSignIn(now);

        if (signIn.IsFailure)
        {
            return Result.Failure<AuthenticationResultDto>(signIn.Error);
        }

        // Housekeeping on a natural cadence. Without it the token collection grows unbounded and is
        // loaded in full on every subsequent sign-in.
        user.PruneRefreshTokens(now, TimeSpan.FromDays(30));

        await context.SaveChangesAsync(cancellationToken);

        var (roleNames, permissions) = await ResolveAuthorizationAsync(user, cancellationToken);
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
                roleNames,
                permissions)));
    }

    /// <summary>
    /// Resolves the user's effective role names and the union of their permissions.
    /// </summary>
    /// <remarks>
    /// Permissions are unioned across roles rather than intersected: roles grant capability, they
    /// do not constrain it. A user holding both Dispatcher and Analyst can do everything either
    /// role allows, which is what an administrator assigning both intends.
    /// </remarks>
    private async Task<(string[] Roles, string[] Permissions)> ResolveAuthorizationAsync(
        User user,
        CancellationToken cancellationToken)
    {
        if (user.RoleIds.Count == 0)
        {
            return ([], []);
        }

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

        return (roles.Select(r => r.Name).ToArray(), permissions);
    }
}
