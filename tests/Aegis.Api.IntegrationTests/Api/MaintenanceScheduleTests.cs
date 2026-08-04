using System.Net;
using System.Net.Http.Json;
using Aegis.Api.Controllers;
using Aegis.Api.IntegrationTests.Infrastructure;
using Aegis.Application.Assets.Commands;
using Aegis.Application.Common.Models;
using Aegis.Application.Identity.Commands;
using Aegis.Application.Identity.Contracts;
using Aegis.Application.Maintenance.Commands;
using Aegis.Application.Maintenance.Queries;
using Aegis.Application.WorkOrders.Commands;
using Aegis.Application.WorkOrders.Queries;
using Aegis.Domain.Assets;
using Aegis.Domain.Identity;
using Aegis.Domain.WorkOrders;
using Aegis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Api.IntegrationTests.Api;

/// <summary>
/// Covers maintenance plan scheduling and the work orders they generate, end to end.
/// </summary>
/// <remarks>
/// The property most worth guarding here is the same shape as the incident one: completing a work
/// order generated from a plan advances that plan's next due date in the same request, without a
/// dispatcher having to remember a second step.
/// </remarks>
public sealed class MaintenanceScheduleTests(AegisWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static Uri Route(string path) => new(path, UriKind.Relative);

    // ---- Creation ----

    [DockerFact]
    public async Task A_plan_can_be_created_for_an_asset()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var assetId = await RegisterAssetAsync(client);

            var planId = await CreatePlanAsync(
                client,
                new CreateMaintenancePlanCommand(assetId, "Quarterly inspection", null, 90, null));

            var page = await ListAsync(client, "?dueOnly=true");
            page.Items.ShouldContain(p => p.Id == planId);
        }
    }

    [DockerFact]
    public async Task Creating_against_an_unknown_asset_is_rejected()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var response = await client.PostAsJsonAsync(
                Route("/api/v1/maintenance-plans"),
                new CreateMaintenancePlanCommand(Guid.CreateVersion7(), "Inspect", null, 90, null));

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [DockerFact]
    public async Task A_plan_starting_in_the_future_is_not_yet_due()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var assetId = await RegisterAssetAsync(client);

            var planId = await CreatePlanAsync(
                client,
                new CreateMaintenancePlanCommand(
                    assetId, "Annual survey", null, 365, DateTimeOffset.UtcNow.AddDays(60)));

            var page = await ListAsync(client, "?dueOnly=true");
            page.Items.ShouldNotContain(p => p.Id == planId);

            var all = await ListAsync(client);
            all.Items.Single(p => p.Id == planId).IsDue.ShouldBeFalse();
        }
    }

    // ---- Generating work orders ----

    [DockerFact]
    public async Task Generating_dispatches_a_work_order_linked_to_the_plan_and_its_asset()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var assetId = await RegisterAssetAsync(client);

            var planId = await CreatePlanAsync(
                client,
                new CreateMaintenancePlanCommand(assetId, "Quarterly inspection", null, 90, null));

            var workOrderId = await GenerateWorkOrderAsync(client, planId, WorkOrderPriority.Medium);

            var page = await ListWorkOrdersAsync(client, $"?assetId={assetId}");
            var item = page.Items.ShouldHaveSingleItem();
            item.Id.ShouldBe(workOrderId);
            item.Status.ShouldBe(WorkOrderStatus.Draft);
        }
    }

    [DockerFact]
    public async Task Generating_twice_before_the_first_is_closed_is_rejected()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var assetId = await RegisterAssetAsync(client);

            var planId = await CreatePlanAsync(
                client,
                new CreateMaintenancePlanCommand(assetId, "Quarterly inspection", null, 90, null));

            await GenerateWorkOrderAsync(client, planId, WorkOrderPriority.Medium);

            var second = await client.PostAsJsonAsync(
                Route($"/api/v1/maintenance-plans/{planId}/generate-work-order"),
                new GenerateWorkOrderRequest(WorkOrderPriority.Medium));

            second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await second.Content.ReadAsStringAsync()).ShouldContain("MaintenancePlan.WorkOrderAlreadyOpen");
        }
    }

    [DockerFact]
    public async Task An_inactive_plan_cannot_generate_work()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var assetId = await RegisterAssetAsync(client);

            var planId = await CreatePlanAsync(
                client,
                new CreateMaintenancePlanCommand(assetId, "Quarterly inspection", null, 90, null));

            await client.PostAsync(Route($"/api/v1/maintenance-plans/{planId}/deactivate"), content: null);

            var response = await client.PostAsJsonAsync(
                Route($"/api/v1/maintenance-plans/{planId}/generate-work-order"),
                new GenerateWorkOrderRequest(WorkOrderPriority.Medium));

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }
    }

    // ---- The loop-closing behaviour ----

    [DockerFact]
    public async Task Completing_the_generated_work_order_advances_the_plan()
    {
        var (client, tenant) = await CreateTenantClientAsync();

        using (client)
        {
            var assetId = await RegisterAssetAsync(client);

            var planId = await CreatePlanAsync(
                client,
                new CreateMaintenancePlanCommand(assetId, "Quarterly inspection", null, 90, null));

            var workOrderId = await GenerateWorkOrderAsync(client, planId, WorkOrderPriority.Medium);

            var technician = await InviteAndAcceptTechnicianAsync(client, tenant);

            await client.PostAsJsonAsync(
                Route($"/api/v1/work-orders/{workOrderId}/assign"),
                new AssignWorkOrderRequest(technician.User.Id, null));

            var complete = await client.PostAsJsonAsync(
                Route($"/api/v1/work-orders/{workOrderId}/complete"),
                new CompleteWorkOrderRequest("Inspected, no issues found"));

            complete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var page = await ListAsync(client);
            var plan = page.Items.Single(p => p.Id == planId);

            plan.LastCompletedOnUtc.ShouldNotBeNull();
            plan.NextDueOnUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddDays(89));

            // Advanced, so no longer due, and free to generate its next occurrence.
            plan.IsDue.ShouldBeFalse();

            var regenerated = await client.PostAsJsonAsync(
                Route($"/api/v1/maintenance-plans/{planId}/generate-work-order"),
                new GenerateWorkOrderRequest(WorkOrderPriority.Medium));

            regenerated.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    // ---- Activation ----

    [DockerFact]
    public async Task Deactivating_and_reactivating_round_trips()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var assetId = await RegisterAssetAsync(client);

            var planId = await CreatePlanAsync(
                client,
                new CreateMaintenancePlanCommand(assetId, "Quarterly inspection", null, 90, null));

            (await client.PostAsync(Route($"/api/v1/maintenance-plans/{planId}/deactivate"), content: null))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);

            // Deactivating an already-inactive plan is rejected.
            (await client.PostAsync(Route($"/api/v1/maintenance-plans/{planId}/deactivate"), content: null))
                .StatusCode.ShouldBe(HttpStatusCode.Conflict);

            (await client.PostAsync(Route($"/api/v1/maintenance-plans/{planId}/reactivate"), content: null))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
    }

    // ---- Tenant isolation and permissions ----

    [DockerFact]
    public async Task Plans_are_not_visible_across_organizations()
    {
        var (alphaClient, _) = await CreateTenantClientAsync("Alpha Maintenance");
        var (betaClient, _) = await CreateTenantClientAsync("Beta Maintenance");

        using (alphaClient)
        using (betaClient)
        {
            var assetId = await RegisterAssetAsync(alphaClient);

            var planId = await CreatePlanAsync(
                alphaClient,
                new CreateMaintenancePlanCommand(assetId, "Alpha plan", null, 90, null));

            var betaGenerate = await betaClient.PostAsJsonAsync(
                Route($"/api/v1/maintenance-plans/{planId}/generate-work-order"),
                new GenerateWorkOrderRequest(WorkOrderPriority.Medium));

            betaGenerate.StatusCode.ShouldBe(HttpStatusCode.NotFound);

            var betaPage = await ListAsync(betaClient);
            betaPage.Items.ShouldNotContain(p => p.Id == planId);
        }
    }

    [DockerFact]
    public async Task Scheduling_requires_the_schedule_permission()
    {
        var (adminClient, tenant) = await CreateTenantClientAsync();

        using (adminClient)
        {
            // Dispatchers hold maintenance.view but not maintenance.schedule.
            var dispatcher = await InviteAndAcceptAsync(adminClient, tenant, "dispatch", SystemRoles.Dispatcher);

            using var dispatcherClient = CreateAuthenticatedClient(dispatcher.AccessToken);

            var response = await dispatcherClient.PostAsJsonAsync(
                Route("/api/v1/maintenance-plans"),
                new CreateMaintenancePlanCommand(Guid.CreateVersion7(), "Inspect", null, 90, null));

            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    // ---- Helpers ----

    private static async Task<Guid> CreatePlanAsync(HttpClient client, CreateMaintenancePlanCommand command)
    {
        var response = await client.PostAsJsonAsync(Route("/api/v1/maintenance-plans"), command);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Creating the plan failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> GenerateWorkOrderAsync(
        HttpClient client,
        Guid planId,
        WorkOrderPriority priority)
    {
        var response = await client.PostAsJsonAsync(
            Route($"/api/v1/maintenance-plans/{planId}/generate-work-order"),
            new GenerateWorkOrderRequest(priority));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Generating failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<PagedResult<MaintenancePlanListItemDto>> ListAsync(
        HttpClient client,
        string queryString = "")
    {
        var response = await client.GetAsync(Route($"/api/v1/maintenance-plans{queryString}"));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Listing plans failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        return await response.Content.ReadFromJsonAsync<PagedResult<MaintenancePlanListItemDto>>(JsonOptions)
            ?? throw new InvalidOperationException("Listing plans returned no body.");
    }

    private static async Task<PagedResult<WorkOrderListItemDto>> ListWorkOrdersAsync(
        HttpClient client,
        string queryString = "")
    {
        var response = await client.GetAsync(Route($"/api/v1/work-orders{queryString}"));

        return await response.Content.ReadFromJsonAsync<PagedResult<WorkOrderListItemDto>>(JsonOptions)
            ?? throw new InvalidOperationException("Listing work orders returned no body.");
    }

    private static async Task<Guid> RegisterAssetAsync(HttpClient client)
    {
        var code = $"VLV-{Guid.CreateVersion7():N}"[..16].ToUpperInvariant();

        var response = await client.PostAsJsonAsync(
            Route("/api/v1/assets"),
            new RegisterAssetCommand(code, "Junction isolation valve", AssetType.Valve, 51.5080, -0.1281));

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task<AuthenticationResultDto> InviteAndAcceptAsync(
        HttpClient adminClient,
        ProvisionedTenant tenant,
        string localPart,
        string roleName)
    {
        var roleId = await RoleIdAsync(tenant.OrganizationId, roleName);
        var email = $"{localPart}.{Guid.CreateVersion7():N}@aegis.test";

        var invite = await adminClient.PostAsJsonAsync(
            Route("/api/v1/users/invitations"),
            new InviteUserCommand(email, [roleId]));

        if (!invite.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Inviting failed with {(int)invite.StatusCode}: " + await invite.Content.ReadAsStringAsync());
        }

        var invitationId = await invite.Content.ReadFromJsonAsync<Guid>();
        var token = await ResolveTokenAsync(tenant.OrganizationId, invitationId);

        using var anonymous = CreateAnonymousClient();

        var response = await anonymous.PostAsJsonAsync(
            Route("/api/v1/users/invitations/accept"),
            new AcceptInvitationCommand(token, "thistle marmalade quiet lantern", "Given", "Family"));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Accepting failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        return await response.Content.ReadFromJsonAsync<AuthenticationResultDto>(JsonOptions)
            ?? throw new InvalidOperationException("Accepting returned no body.");
    }

    private Task<AuthenticationResultDto> InviteAndAcceptTechnicianAsync(
        HttpClient adminClient,
        ProvisionedTenant tenant) =>
        InviteAndAcceptAsync(adminClient, tenant, "tech", SystemRoles.Technician);

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

    private async Task<string> ResolveTokenAsync(Guid organizationId, Guid invitationId)
    {
        await using var scope = Factory.CreateTenantScope(organizationId);
        var context = scope.ServiceProvider.GetRequiredService<AegisDbContext>();
        var tokenService = scope.ServiceProvider
            .GetRequiredService<Aegis.Application.Abstractions.Security.ITokenService>();

        var known = tokenService.IssueRefreshToken();

        var invitation = await context.Set<UserInvitation>()
            .IgnoreQueryFilters()
            .SingleAsync(i => i.Id == invitationId);

        context.Entry(invitation).Property(nameof(UserInvitation.TokenHash)).CurrentValue = known.Hash;

        await context.SaveChangesAsync();

        return known.Value;
    }
}
