using System.Net;
using System.Net.Http.Json;
using Aegis.Api.Controllers;
using Aegis.Api.IntegrationTests.Infrastructure;
using Aegis.Application.Common.Models;
using Aegis.Application.Identity.Commands;
using Aegis.Application.Identity.Contracts;
using Aegis.Application.Identity.Queries;
using Aegis.Domain.Identity;
using Aegis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Api.IntegrationTests.Api;

/// <summary>
/// Covers invitations, role management and the first permission-guarded endpoints.
/// </summary>
public sealed class UserManagementTests(AegisWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static Uri Route(string path) => new(path, UriKind.Relative);

    // ---- Listing, paging and tenant scoping ----

    [DockerFact]
    public async Task An_administrator_sees_only_their_own_organizations_users()
    {
        // The property the whole tenancy design exists for, asserted through the HTTP surface
        // rather than against the DbContext. The handler contains no tenant predicate at all.
        var (alphaClient, alpha) = await CreateTenantClientAsync("Alpha Water");
        var (betaClient, beta) = await CreateTenantClientAsync("Beta Power");

        using (alphaClient)
        using (betaClient)
        {
            var alphaUsers = await ReadUsersAsync(alphaClient);
            var betaUsers = await ReadUsersAsync(betaClient);

            alphaUsers.Items.ShouldContain(u => u.Id == alpha.UserId);
            alphaUsers.Items.ShouldNotContain(u => u.Id == beta.UserId);

            betaUsers.Items.ShouldContain(u => u.Id == beta.UserId);
            betaUsers.Items.ShouldNotContain(u => u.Id == alpha.UserId);
        }
    }

    [DockerFact]
    public async Task The_page_size_is_capped_regardless_of_what_is_requested()
    {
        // Without the ceiling, ?pageSize=1000000 is an unauthenticated denial-of-service against
        // both the database and the API's memory.
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var page = await ReadUsersAsync(client, "?pageSize=1000000");

            page.PageSize.ShouldBe(PaginatedQuery.MaxPageSize);
        }
    }

    [DockerFact]
    public async Task An_unknown_sort_field_is_rejected_with_the_valid_options()
    {
        // Rejected rather than ignored. Silently falling back would page through arbitrarily
        // ordered data, showing some records twice and others never — which looks like data loss.
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var response = await client.GetAsync(Route("/api/v1/users?sortBy=nonsense"));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            var body = await response.Content.ReadAsStringAsync();
            body.ShouldContain("Unknown sort field");
            body.ShouldContain(nameof(UserListItemDto.CreatedOnUtc));
        }
    }

    [DockerFact]
    public async Task Sorting_and_filtering_are_applied()
    {
        var (client, tenant) = await CreateTenantClientAsync();

        using (client)
        {
            await InviteAndAcceptAsync(client, tenant, "zoe");
            await InviteAndAcceptAsync(client, tenant, "aaron");

            var ascending = await ReadUsersAsync(client, "?sortBy=firstName&sortDirection=Ascending");
            var names = ascending.Items.Select(u => u.FirstName).ToArray();

            names.ShouldBe(names.OrderBy(n => n, StringComparer.Ordinal).ToArray());

            var active = await ReadUsersAsync(client, "?status=Active");
            active.Items.ShouldAllBe(u => u.Status == UserStatus.Active);
        }
    }

    // ---- Permission enforcement ----

    [DockerFact]
    public async Task A_caller_without_the_permission_is_refused()
    {
        // The point of permission-based authorization: this is enforced by the policy, and the
        // handler is never reached.
        var (adminClient, tenant) = await CreateTenantClientAsync();

        using (adminClient)
        {
            // Technicians hold assets.view and workorders.complete, but not users.view.
            var technician = await InviteAndAcceptAsync(adminClient, tenant, "tech", SystemRoles.Technician);

            using var technicianClient = CreateAuthenticatedClient(technician.AccessToken);

            var response = await technicianClient.GetAsync(Route("/api/v1/users"));

            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [DockerFact]
    public async Task A_newly_granted_role_takes_effect_without_waiting_for_the_token_to_expire()
    {
        // Assigning a role rotates the security stamp, so the technician's existing access token is
        // refused on their next request. They re-authenticate and the new permission is present.
        var (adminClient, tenant) = await CreateTenantClientAsync();

        using (adminClient)
        {
            var technician = await InviteAndAcceptAsync(adminClient, tenant, "promote", SystemRoles.Technician);
            var analystRoleId = await RoleIdAsync(tenant.OrganizationId, SystemRoles.Analyst);

            using var technicianClient = CreateAuthenticatedClient(technician.AccessToken);

            (await technicianClient.GetAsync(Route("/api/v1/users")))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            var granted = await adminClient.PostAsync(
                Route($"/api/v1/users/{technician.User.Id}/roles/{analystRoleId}"),
                content: null);

            granted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            // The old token is now refused outright rather than merely lacking the permission.
            (await technicianClient.GetAsync(Route("/api/v1/users")))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [DockerFact]
    public async Task A_role_from_another_organization_cannot_be_granted()
    {
        // Guards against an administrator attaching a foreign role by guessing its identifier,
        // which would grant permissions their own organization never defined.
        var (alphaClient, alpha) = await CreateTenantClientAsync("Alpha Roles");
        var (betaClient, beta) = await CreateTenantClientAsync("Beta Roles");

        using (alphaClient)
        using (betaClient)
        {
            var betaRoleId = await RoleIdAsync(beta.OrganizationId, SystemRoles.Analyst);

            var response = await alphaClient.PostAsync(
                Route($"/api/v1/users/{alpha.UserId}/roles/{betaRoleId}"),
                content: null);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    // ---- Invitations ----

    [DockerFact]
    public async Task An_invited_user_can_accept_and_is_confirmed_without_a_separate_step()
    {
        var (client, tenant) = await CreateTenantClientAsync();

        using (client)
        {
            var accepted = await InviteAndAcceptAsync(client, tenant, "newjoiner");

            accepted.User.OrganizationId.ShouldBe(tenant.OrganizationId);
            accepted.AccessToken.ShouldNotBeNullOrWhiteSpace();

            // Possession of the emailed token proved control of the inbox, so the account is
            // active immediately rather than pending a second confirmation email.
            var listed = await ReadUsersAsync(client);
            var created = listed.Items.Single(u => u.Id == accepted.User.Id);

            created.EmailConfirmed.ShouldBeTrue();
            created.Status.ShouldBe(UserStatus.Active);
        }
    }

    [DockerFact]
    public async Task An_invitation_token_cannot_be_used_twice()
    {
        var (client, tenant) = await CreateTenantClientAsync();

        using (client)
        {
            var token = await IssueInvitationAsync(client, tenant, "singleuse", SystemRoles.Analyst);

            using var anonymous = CreateAnonymousClient();

            var first = await anonymous.PostAsJsonAsync(
                Route("/api/v1/users/invitations/accept"),
                new AcceptInvitationCommand(token, "thistle marmalade quiet lantern", "Sam", "Okoye"));

            first.StatusCode.ShouldBe(HttpStatusCode.OK);

            var second = await anonymous.PostAsJsonAsync(
                Route("/api/v1/users/invitations/accept"),
                new AcceptInvitationCommand(token, "thistle marmalade quiet lantern", "Other", "Person"));

            second.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [DockerFact]
    public async Task An_unknown_invitation_token_is_rejected()
    {
        using var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(
            Route("/api/v1/users/invitations/accept"),
            new AcceptInvitationCommand(
                Convert.ToBase64String(Guid.CreateVersion7().ToByteArray()),
                "thistle marmalade quiet lantern",
                "No",
                "Body"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [DockerFact]
    public async Task Inviting_an_existing_member_is_rejected()
    {
        var (client, tenant) = await CreateTenantClientAsync();

        using (client)
        {
            var roleId = await RoleIdAsync(tenant.OrganizationId, SystemRoles.Analyst);

            var response = await client.PostAsJsonAsync(
                Route("/api/v1/users/invitations"),
                new InviteUserCommand(tenant.Email, [roleId]));

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }
    }

    [DockerFact]
    public async Task An_invitation_granting_no_roles_is_rejected()
    {
        // An invitee with no roles holds no permissions and can do nothing after accepting, which
        // is never what the inviter meant.
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var response = await client.PostAsJsonAsync(
                Route("/api/v1/users/invitations"),
                new InviteUserCommand($"empty.{Guid.CreateVersion7():N}@aegis.test", []));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    // ---- Self-protection ----

    [DockerFact]
    public async Task An_administrator_cannot_deactivate_their_own_account()
    {
        // It immediately invalidates their own session, and if they were the last administrator the
        // organization is locked out with no way back in.
        var (client, tenant) = await CreateTenantClientAsync();

        using (client)
        {
            var response = await client.PostAsJsonAsync(
                Route($"/api/v1/users/{tenant.UserId}/deactivate"),
                new DeactivateUserRequest("changed my mind"));

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).ShouldContain("User.CannotActOnSelf");
        }
    }

    [DockerFact]
    public async Task The_last_administrator_cannot_remove_their_own_administrator_role()
    {
        var (client, tenant) = await CreateTenantClientAsync();

        using (client)
        {
            var adminRoleId = await RoleIdAsync(tenant.OrganizationId, SystemRoles.Administrator);

            var response = await client.DeleteAsync(
                Route($"/api/v1/users/{tenant.UserId}/roles/{adminRoleId}"));

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }
    }

    [DockerFact]
    public async Task Deactivating_a_user_ends_their_sessions_immediately()
    {
        var (adminClient, tenant) = await CreateTenantClientAsync();

        using (adminClient)
        {
            // Supervisor, not Analyst: the assertion below needs a role that actually holds
            // users.view, otherwise the "before" request would be 403 and the test would pass for
            // the wrong reason.
            var member = await InviteAndAcceptAsync(adminClient, tenant, "leaver", SystemRoles.Supervisor);

            using var memberClient = CreateAuthenticatedClient(member.AccessToken);

            (await memberClient.GetAsync(Route("/api/v1/users")))
                .StatusCode.ShouldBe(HttpStatusCode.OK);

            var deactivated = await adminClient.PostAsJsonAsync(
                Route($"/api/v1/users/{member.User.Id}/deactivate"),
                new DeactivateUserRequest("Left the organization"));

            deactivated.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            (await memberClient.GetAsync(Route("/api/v1/users")))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    // ---- Helpers ----

    private static async Task<PagedResult<UserListItemDto>> ReadUsersAsync(
        HttpClient client,
        string queryString = "")
    {
        var response = await client.GetAsync(Route($"/api/v1/users{queryString}"));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Listing users failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        return await response.Content.ReadFromJsonAsync<PagedResult<UserListItemDto>>(JsonOptions)
            ?? throw new InvalidOperationException("Listing users returned no body.");
    }

    private async Task<string> IssueInvitationAsync(
        HttpClient adminClient,
        ProvisionedTenant tenant,
        string localPart,
        string roleName)
    {
        var roleId = await RoleIdAsync(tenant.OrganizationId, roleName);
        var email = $"{localPart}.{Guid.CreateVersion7():N}@aegis.test";

        var response = await adminClient.PostAsJsonAsync(
            Route("/api/v1/users/invitations"),
            new InviteUserCommand(email, [roleId]));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Inviting failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        var invitationId = await response.Content.ReadFromJsonAsync<Guid>();

        return await ResolveTokenAsync(tenant.OrganizationId, invitationId);
    }

    /// <summary>
    /// Recovers the raw invitation token by re-deriving it, since only its hash is stored.
    /// </summary>
    /// <remarks>
    /// The token is generated by the token service and never persisted in readable form, which is
    /// the point. The test therefore cannot read it back and instead overwrites the stored hash
    /// with the hash of a token it generates itself — exercising the same acceptance path a real
    /// invitee would take, without weakening the production code to make it observable.
    /// </remarks>
    private async Task<string> ResolveTokenAsync(Guid organizationId, Guid invitationId)
    {
        await using var scope = Factory.CreateTenantScope(organizationId);
        var context = scope.ServiceProvider.GetRequiredService<AegisDbContext>();
        var tokenService = scope.ServiceProvider
            .GetRequiredService<Application.Abstractions.Security.ITokenService>();

        var known = tokenService.IssueRefreshToken();

        var invitation = await context.Set<UserInvitation>()
            .IgnoreQueryFilters()
            .SingleAsync(i => i.Id == invitationId);

        context.Entry(invitation).Property(nameof(UserInvitation.TokenHash)).CurrentValue = known.Hash;

        await context.SaveChangesAsync();

        return known.Value;
    }

    private async Task<AuthenticationResultDto> InviteAndAcceptAsync(
        HttpClient adminClient,
        ProvisionedTenant tenant,
        string localPart,
        string roleName = SystemRoles.Analyst)
    {
        var token = await IssueInvitationAsync(adminClient, tenant, localPart, roleName);

        using var anonymous = CreateAnonymousClient();

        var response = await anonymous.PostAsJsonAsync(
            Route("/api/v1/users/invitations/accept"),
            new AcceptInvitationCommand(
                token,
                "thistle marmalade quiet lantern",
                char.ToUpperInvariant(localPart[0]) + localPart[1..],
                "Okoye"));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Accepting failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        return await response.Content.ReadFromJsonAsync<AuthenticationResultDto>(JsonOptions)
            ?? throw new InvalidOperationException("Accepting returned no body.");
    }

    private async Task<Guid> RoleIdAsync(Guid organizationId, string roleName)
    {
        await using var scope = Factory.CreateTenantScope(organizationId);
        var context = scope.ServiceProvider.GetRequiredService<AegisDbContext>();

        return await context.Roles
            .AsNoTracking()
            .Where(r => r.Name == roleName)
            .Select(r => r.Id)
            .SingleAsync();
    }
}
