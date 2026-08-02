using System.Net;
using System.Net.Http.Json;
using Aegis.Api.IntegrationTests.Infrastructure;
using Aegis.Application.Identity.Commands;
using Aegis.Application.Identity.Contracts;
using Aegis.Domain.Identity;
using Aegis.Domain.Organizations;

namespace Aegis.Api.IntegrationTests.Api;

/// <summary>
/// Exercises registration, sign-in, session renewal and sign-out end to end.
/// </summary>
/// <remarks>
/// These run against the real JWT pipeline, the real password hasher and a real database. There is
/// no stub authentication scheme, so a token that works here is a token that works in production.
/// </remarks>
public sealed class AuthenticationFlowTests(AegisWebApplicationFactory factory)
    : IntegrationTestBase(factory)
{
    private static Uri Route(string path) => new(path, UriKind.Relative);

    // ---- Registration ----

    [DockerFact]
    public async Task Registering_creates_the_organization_and_signs_the_administrator_in()
    {
        var tenant = await ProvisionTenantAsync("Northern Water");
        var auth = tenant.Authentication;

        auth.AccessToken.ShouldNotBeNullOrWhiteSpace();
        auth.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        auth.TokenType.ShouldBe("Bearer");
        auth.AccessTokenExpiresOnUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        auth.User.Email.ShouldBe(tenant.Email);
        auth.User.OrganizationId.ShouldBe(tenant.OrganizationId);
        auth.User.Roles.ShouldContain(SystemRoles.Administrator);
    }

    [DockerFact]
    public async Task The_first_administrator_receives_every_permission()
    {
        var tenant = await ProvisionTenantAsync();

        // Otherwise a brand-new tenant cannot administer itself, and needs support intervention
        // to become usable at all.
        tenant.Authentication.User.Permissions.ShouldContain(Permissions.Users.Create);
        tenant.Authentication.User.Permissions.ShouldContain(Permissions.Assets.Decommission);
        tenant.Authentication.User.Permissions.Count.ShouldBe(Permissions.All.Count);
    }

    [DockerFact]
    public async Task Registering_with_a_short_password_is_rejected_with_field_level_detail()
    {
        using var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(Route("/api/v1/auth/register"), new RegisterOrganizationCommand(
            "Tiny Password Co",
            OrganizationKind.Other,
            "Etc/UTC",
            $"short.{Guid.CreateVersion7():N}@aegis.test",
            "short",
            "Ada",
            "Osei"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("Password");
        body.ShouldContain("12 characters");
    }

    [DockerFact]
    public async Task Registering_an_organization_whose_slug_is_taken_is_rejected()
    {
        var first = await ProvisionTenantAsync("Collision Test");

        using var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(Route("/api/v1/auth/register"), new RegisterOrganizationCommand(
            first.OrganizationName,
            OrganizationKind.WaterUtility,
            "Etc/UTC",
            $"other.{Guid.CreateVersion7():N}@aegis.test",
            "correct-horse-battery-staple",
            "Bo",
            "Lin"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Organization.SlugTaken");
    }

    // ---- Sign-in ----

    [DockerFact]
    public async Task Signing_in_with_correct_credentials_issues_a_working_token()
    {
        var tenant = await ProvisionTenantAsync();

        using var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            Route("/api/v1/auth/login"),
            new SignInCommand(tenant.Email, tenant.Password));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var auth = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        auth.ShouldNotBeNull();

        // Proves the token is accepted by the real JWT middleware, not merely well-formed.
        using var authenticated = CreateAuthenticatedClient(auth.AccessToken);
        var probe = await authenticated.PostAsJsonAsync(
            Route("/api/v1/auth/logout"),
            new SignOutCommand(null));

        probe.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [DockerFact]
    public async Task The_email_address_is_matched_case_insensitively()
    {
        // Normalisation happens at construction, so a user who capitalises their address on a phone
        // keyboard still signs in to the account they created.
        var tenant = await ProvisionTenantAsync();

        using var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            Route("/api/v1/auth/login"),
            new SignInCommand(tenant.Email.ToUpperInvariant(), tenant.Password));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [DockerFact]
    public async Task An_unknown_address_and_a_wrong_password_are_indistinguishable()
    {
        // The account enumeration guard. If these two responses differed in any way, the login
        // endpoint would confirm which addresses belong to real customers.
        var tenant = await ProvisionTenantAsync();

        using var client = CreateAnonymousClient();

        var wrongPassword = await client.PostAsJsonAsync(
            Route("/api/v1/auth/login"),
            new SignInCommand(tenant.Email, "definitely-not-the-password"));

        var unknownAddress = await client.PostAsJsonAsync(
            Route("/api/v1/auth/login"),
            new SignInCommand($"nobody.{Guid.CreateVersion7():N}@aegis.test", "definitely-not-the-password"));

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownAddress.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var wrongBody = await wrongPassword.Content.ReadAsStringAsync();
        var unknownBody = await unknownAddress.Content.ReadAsStringAsync();

        wrongBody.ShouldContain("Auth.InvalidCredentials");
        unknownBody.ShouldContain("Auth.InvalidCredentials");
    }

    [DockerFact]
    public async Task Repeated_failures_lock_the_account_and_a_correct_password_no_longer_works()
    {
        var tenant = await ProvisionTenantAsync();

        using var client = CreateAnonymousClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostAsJsonAsync(
                Route("/api/v1/auth/login"),
                new SignInCommand(tenant.Email, "wrong-password-entirely"));
        }

        var afterLockout = await client.PostAsJsonAsync(
            Route("/api/v1/auth/login"),
            new SignInCommand(tenant.Email, tenant.Password));

        afterLockout.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await afterLockout.Content.ReadAsStringAsync()).ShouldContain("Auth.AccountLocked");
    }

    // ---- Session renewal ----

    [DockerFact]
    public async Task Refreshing_returns_a_new_pair_and_retires_the_presented_token()
    {
        var tenant = await ProvisionTenantAsync();

        using var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            Route("/api/v1/auth/refresh"),
            new RefreshSessionCommand(tenant.Authentication.RefreshToken));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var renewed = await response.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        renewed.ShouldNotBeNull();
        renewed.RefreshToken.ShouldNotBe(tenant.Authentication.RefreshToken);
        renewed.User.Id.ShouldBe(tenant.UserId);
    }

    [DockerFact]
    public async Task Replaying_a_rotated_refresh_token_terminates_the_whole_session_chain()
    {
        // The security property that makes refresh token theft survivable. A correct client discards
        // its token the instant it exchanges it, so a second presentation means two parties hold it.
        var tenant = await ProvisionTenantAsync();

        using var client = CreateAnonymousClient();

        var firstRefresh = await client.PostAsJsonAsync(
            Route("/api/v1/auth/refresh"),
            new RefreshSessionCommand(tenant.Authentication.RefreshToken));

        firstRefresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await firstRefresh.Content.ReadFromJsonAsync<AuthenticationResultDto>();
        second.ShouldNotBeNull();

        // The attacker replays the original, already-rotated token.
        var replay = await client.PostAsJsonAsync(
            Route("/api/v1/auth/refresh"),
            new RefreshSessionCommand(tenant.Authentication.RefreshToken));

        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // And the legitimate client's newer token is now dead too. Signing out the real user is a
        // deliberate cost: leaving an attacker with a renewable session is far worse.
        var legitimate = await client.PostAsJsonAsync(
            Route("/api/v1/auth/refresh"),
            new RefreshSessionCommand(second.RefreshToken));

        legitimate.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DockerFact]
    public async Task An_unknown_refresh_token_is_rejected()
    {
        using var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            Route("/api/v1/auth/refresh"),
            new RefreshSessionCommand(Convert.ToBase64String(Guid.CreateVersion7().ToByteArray())));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DockerFact]
    public async Task Signing_out_invalidates_the_refresh_token()
    {
        var (client, tenant) = await CreateTenantClientAsync();

        using (client)
        {
            var signOut = await client.PostAsJsonAsync(
                Route("/api/v1/auth/logout"),
                new SignOutCommand(tenant.Authentication.RefreshToken));

            signOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var anonymous = CreateAnonymousClient();

        var refresh = await anonymous.PostAsJsonAsync(
            Route("/api/v1/auth/refresh"),
            new RefreshSessionCommand(tenant.Authentication.RefreshToken));

        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- Transport-level authorization ----

    [DockerFact]
    public async Task A_protected_endpoint_rejects_an_anonymous_caller()
    {
        // Verifies the fallback policy: an endpoint with no attribute requires authentication, so
        // forgetting [Authorize] fails closed rather than publishing the endpoint.
        using var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            Route("/api/v1/auth/logout"),
            new SignOutCommand(null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DockerFact]
    public async Task A_token_signed_with_the_wrong_key_is_rejected()
    {
        // Guards against the signature ever being accepted without verification, which would let
        // anyone mint a token naming any user in any organization.
        var tenant = await ProvisionTenantAsync();

        var parts = tenant.Authentication.AccessToken.Split('.');
        var forged = $"{parts[0]}.{parts[1]}.{new string('A', parts[2].Length)}";

        using var client = CreateAuthenticatedClient(forged);

        var response = await client.PostAsJsonAsync(Route("/api/v1/auth/logout"), new SignOutCommand(null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DockerFact]
    public async Task Two_tenants_registered_independently_receive_distinct_organizations()
    {
        var first = await ProvisionTenantAsync("Alpha Water");
        var second = await ProvisionTenantAsync("Beta Power");

        first.OrganizationId.ShouldNotBe(second.OrganizationId);
        first.Authentication.User.OrganizationId.ShouldBe(first.OrganizationId);
        second.Authentication.User.OrganizationId.ShouldBe(second.OrganizationId);
    }
}
