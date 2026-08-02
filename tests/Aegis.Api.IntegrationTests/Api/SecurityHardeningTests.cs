using System.Net;
using System.Net.Http.Json;
using Aegis.Api.IntegrationTests.Infrastructure;
using Aegis.Application.Identity.Commands;
using Aegis.Domain.Identity;
using Aegis.Domain.Organizations;
using Aegis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Api.IntegrationTests.Api;

/// <summary>
/// Covers the controls that bound the damage of a compromised or stale credential.
/// </summary>
public sealed class SecurityHardeningTests(AegisWebApplicationFactory factory)
    : IntegrationTestBase(factory)
{
    private static Uri Route(string path) => new(path, UriKind.Relative);

    // ---- Security stamp validation ----

    [DockerFact]
    public async Task An_access_token_stops_working_the_moment_the_account_is_deactivated()
    {
        // The hole this closes: without stamp validation a deactivated administrator keeps full
        // access until their access token expires, which is up to fifteen minutes of authority
        // someone has explicitly revoked.
        var (client, tenant) = await CreateTenantClientAsync();

        using (client)
        {
            // The token works before the change.
            var before = await client.PostAsJsonAsync(Route("/api/v1/auth/logout"), new SignOutCommand(null));
            before.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            await DeactivateAsync(tenant.OrganizationId, tenant.UserId);

            // The same token, unexpired and correctly signed, is now refused.
            var after = await client.PostAsJsonAsync(Route("/api/v1/auth/logout"), new SignOutCommand(null));
            after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [DockerFact]
    public async Task An_access_token_stops_working_when_the_password_changes()
    {
        // The reason anyone changes a password after a suspected compromise: the attacker's live
        // session must end, not merely their ability to sign in again.
        var (client, tenant) = await CreateTenantClientAsync();

        using (client)
        {
            (await client.PostAsJsonAsync(Route("/api/v1/auth/logout"), new SignOutCommand(null)))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);

            await ChangePasswordAsync(tenant.OrganizationId, tenant.UserId);

            (await client.PostAsJsonAsync(Route("/api/v1/auth/logout"), new SignOutCommand(null)))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [DockerFact]
    public async Task Revocation_is_immediate_rather_than_eventually_consistent()
    {
        // Proves the cache is evicted by the domain event rather than merely expiring. A TTL-based
        // implementation would still pass the tests above given enough delay, and would quietly
        // leave a window whose length nobody could state precisely.
        var (client, tenant) = await CreateTenantClientAsync();

        using (client)
        {
            // Warms the stamp cache.
            await client.PostAsJsonAsync(Route("/api/v1/auth/logout"), new SignOutCommand(null));

            await DeactivateAsync(tenant.OrganizationId, tenant.UserId);

            // No delay at all between the change and the next request.
            var immediately = await client.PostAsJsonAsync(
                Route("/api/v1/auth/logout"),
                new SignOutCommand(null));

            immediately.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [DockerFact]
    public async Task A_token_carrying_no_security_stamp_is_refused()
    {
        // Fail closed. Accepting a stamp-less token would leave a bypass for exactly the tokens
        // this check exists to catch.
        var tenant = await ProvisionTenantAsync();

        await using var scope = Factory.CreateTenantScope(tenant.OrganizationId);
        var stampService = scope.ServiceProvider
            .GetRequiredService<Application.Abstractions.Security.ISecurityStampService>();

        (await stampService.IsCurrentAsync(tenant.UserId, null)).ShouldBeFalse();
        (await stampService.IsCurrentAsync(tenant.UserId, "")).ShouldBeFalse();
        (await stampService.IsCurrentAsync(tenant.UserId, "not-the-current-stamp")).ShouldBeFalse();
    }

    [DockerFact]
    public async Task An_unknown_user_never_validates()
    {
        await using var scope = Factory.CreateTenantScope(organizationId: null);
        var stampService = scope.ServiceProvider
            .GetRequiredService<Application.Abstractions.Security.ISecurityStampService>();

        (await stampService.IsCurrentAsync(Guid.CreateVersion7(), "anything")).ShouldBeFalse();
    }

    // ---- Password screening ----

    [DockerFact]
    public async Task Registration_rejects_a_known_breached_password()
    {
        using var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(Route("/api/v1/auth/register"), new RegisterOrganizationCommand(
            $"Breached {Guid.CreateVersion7():N}"[..24],
            OrganizationKind.WaterUtility,
            "Etc/UTC",
            $"weak.{Guid.CreateVersion7():N}@aegis.test",
            "password1234",
            "Ada",
            "Osei"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("breached");
    }

    [DockerFact]
    public async Task Registration_rejects_a_password_built_from_the_organization_name()
    {
        using var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(Route("/api/v1/auth/register"), new RegisterOrganizationCommand(
            "Riverside Water Board",
            OrganizationKind.WaterUtility,
            "Etc/UTC",
            $"ctx.{Guid.CreateVersion7():N}@aegis.test",
            "riverside water 2025",
            "Ada",
            "Osei"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("organization");
    }

    // ---- Helpers ----

    private async Task DeactivateAsync(Guid organizationId, Guid userId)
    {
        await using var scope = Factory.CreateTenantScope(organizationId);
        var context = scope.ServiceProvider.GetRequiredService<AegisDbContext>();
        var dispatcher = scope.ServiceProvider
            .GetRequiredService<Application.Abstractions.Events.IDomainEventDispatcher>();
        var collector = scope.ServiceProvider
            .GetRequiredService<Application.Abstractions.Events.IDomainEventCollector>();

        var user = await context.Users.SingleAsync(u => u.Id == userId);
        user.Deactivate(DateTimeOffset.UtcNow, "Integration test");

        await context.SaveChangesAsync();

        // Dispatched explicitly because this bypasses the MediatR pipeline, which is what normally
        // drains the collector after the transaction commits.
        await dispatcher.DispatchAsync(collector.Drain());
    }

    private async Task ChangePasswordAsync(Guid organizationId, Guid userId)
    {
        await using var scope = Factory.CreateTenantScope(organizationId);
        var context = scope.ServiceProvider.GetRequiredService<AegisDbContext>();
        var hasher = scope.ServiceProvider
            .GetRequiredService<Application.Abstractions.Security.IPasswordHasher>();
        var dispatcher = scope.ServiceProvider
            .GetRequiredService<Application.Abstractions.Events.IDomainEventDispatcher>();
        var collector = scope.ServiceProvider
            .GetRequiredService<Application.Abstractions.Events.IDomainEventCollector>();

        var user = await context.Users.Include(u => u.RefreshTokens).SingleAsync(u => u.Id == userId);

        user.ChangePassword(
            Domain.Identity.ValueObjects.PasswordHash.FromEncoded(hasher.Hash("a different passphrase entirely")),
            DateTimeOffset.UtcNow);

        await context.SaveChangesAsync();
        await dispatcher.DispatchAsync(collector.Drain());
    }
}
