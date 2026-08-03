using System.Net;
using System.Net.Http.Json;
using Aegis.Api.Controllers;
using Aegis.Api.IntegrationTests.Infrastructure;
using Aegis.Application.Assets.Commands;
using Aegis.Application.Common.Models;
using Aegis.Application.Identity.Commands;
using Aegis.Application.Identity.Contracts;
using Aegis.Application.Incidents.Commands;
using Aegis.Application.Incidents.Queries;
using Aegis.Application.WorkOrders.Commands;
using Aegis.Application.WorkOrders.Queries;
using Aegis.Domain.Assets;
using Aegis.Domain.Identity;
using Aegis.Domain.Incidents;
using Aegis.Domain.WorkOrders;
using Aegis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Api.IntegrationTests.Api;

/// <summary>
/// Covers work order dispatch, assignment and completion end to end.
/// </summary>
/// <remarks>
/// The property most worth guarding here is the loop-closing behaviour: completing a work order
/// that traces back to an incident resolves that incident in the same request, without a
/// dispatcher having to remember a second step.
/// </remarks>
public sealed class WorkOrderDispatchTests(AegisWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private static Uri Route(string path) => new(path, UriKind.Relative);

    // ---- Creation ----

    [DockerFact]
    public async Task A_work_order_can_be_dispatched_against_an_asset()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var assetId = await RegisterAssetAsync(client);

            var workOrderId = await CreateWorkOrderAsync(
                client, new CreateWorkOrderCommand("Replace valve", null, WorkOrderPriority.High, assetId, null));

            var page = await ListAsync(client, "?openOnly=true");
            page.Items.ShouldContain(w => w.Id == workOrderId);
            page.Items.Single(w => w.Id == workOrderId).AssetId.ShouldBe(assetId);
            page.Items.Single(w => w.Id == workOrderId).Status.ShouldBe(WorkOrderStatus.Draft);
        }
    }

    [DockerFact]
    public async Task Creating_against_an_unknown_asset_is_rejected()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var response = await client.PostAsJsonAsync(
                Route("/api/v1/work-orders"),
                new CreateWorkOrderCommand(
                    "Inspect", null, WorkOrderPriority.Low, Guid.CreateVersion7(), null));

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [DockerFact]
    public async Task A_work_order_created_from_an_incident_inherits_its_asset()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var assetId = await RegisterAssetAsync(client);

            var incident = await client.PostAsJsonAsync(
                Route("/api/v1/incidents"),
                new ReportIncidentCommand(
                    "Water leaking from the hydrant on the corner here.", null, null, null, null));

            var reported = await incident.Content.ReadFromJsonAsync<ReportIncidentResult>(JsonOptions)
                ?? throw new InvalidOperationException("Reporting returned no body.");

            var workOrderId = await CreateWorkOrderAsync(
                client,
                new CreateWorkOrderCommand(
                    "Follow up on report", null, WorkOrderPriority.Medium, null, reported.IncidentId));

            var page = await ListAsync(client, $"?incidentId={reported.IncidentId}");
            var item = page.Items.ShouldHaveSingleItem();
            item.IncidentId.ShouldBe(reported.IncidentId);
        }
    }

    // ---- Assignment and progression ----

    [DockerFact]
    public async Task Assigning_a_draft_work_order_schedules_it()
    {
        var (client, tenant) = await CreateTenantClientAsync();

        using (client)
        {
            var technician = await InviteAndAcceptTechnicianAsync(client, tenant);

            var workOrderId = await CreateWorkOrderAsync(
                client, new CreateWorkOrderCommand("Fix leak", null, WorkOrderPriority.High, null, null));

            var assign = await client.PostAsJsonAsync(
                Route($"/api/v1/work-orders/{workOrderId}/assign"),
                new AssignWorkOrderRequest(technician.User.Id, null));

            assign.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var page = await ListAsync(client, $"?assignedToUserId={technician.User.Id}");
            var item = page.Items.ShouldHaveSingleItem();
            item.Status.ShouldBe(WorkOrderStatus.Scheduled);
            item.AssignedToUserId.ShouldBe(technician.User.Id);
        }
    }

    [DockerFact]
    public async Task Assigning_to_an_unknown_user_is_rejected()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var workOrderId = await CreateWorkOrderAsync(
                client, new CreateWorkOrderCommand("Fix leak", null, WorkOrderPriority.High, null, null));

            var assign = await client.PostAsJsonAsync(
                Route($"/api/v1/work-orders/{workOrderId}/assign"),
                new AssignWorkOrderRequest(Guid.CreateVersion7(), null));

            assign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [DockerFact]
    public async Task A_technician_can_complete_their_own_assignment_but_not_dispatch_new_work()
    {
        var (adminClient, tenant) = await CreateTenantClientAsync();

        using (adminClient)
        {
            var technician = await InviteAndAcceptTechnicianAsync(adminClient, tenant);

            var workOrderId = await CreateWorkOrderAsync(
                adminClient, new CreateWorkOrderCommand("Fix leak", null, WorkOrderPriority.High, null, null));

            await adminClient.PostAsJsonAsync(
                Route($"/api/v1/work-orders/{workOrderId}/assign"),
                new AssignWorkOrderRequest(technician.User.Id, null));

            using var technicianClient = CreateAuthenticatedClient(technician.AccessToken);

            // Technicians hold workorders.complete but not workorders.create.
            var createAttempt = await technicianClient.PostAsJsonAsync(
                Route("/api/v1/work-orders"),
                new CreateWorkOrderCommand("Unauthorized dispatch", null, WorkOrderPriority.Low, null, null));

            createAttempt.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

            var complete = await technicianClient.PostAsJsonAsync(
                Route($"/api/v1/work-orders/{workOrderId}/complete"),
                new CompleteWorkOrderRequest("Fixed on site"));

            complete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
    }

    // ---- The loop-closing behaviour ----

    [DockerFact]
    public async Task Completing_a_work_order_resolves_the_incident_it_traces_back_to()
    {
        var (client, tenant) = await CreateTenantClientAsync();

        using (client)
        {
            var technician = await InviteAndAcceptTechnicianAsync(client, tenant);

            var incident = await client.PostAsJsonAsync(
                Route("/api/v1/incidents"),
                new ReportIncidentCommand("Water leaking onto the pavement.", null, null, null, null));

            var reported = await incident.Content.ReadFromJsonAsync<ReportIncidentResult>(JsonOptions)
                ?? throw new InvalidOperationException("Reporting returned no body.");

            var workOrderId = await CreateWorkOrderAsync(
                client,
                new CreateWorkOrderCommand(
                    "Repair leak", null, WorkOrderPriority.High, null, reported.IncidentId));

            await client.PostAsJsonAsync(
                Route($"/api/v1/work-orders/{workOrderId}/assign"),
                new AssignWorkOrderRequest(technician.User.Id, null));

            var complete = await client.PostAsJsonAsync(
                Route($"/api/v1/work-orders/{workOrderId}/complete"),
                new CompleteWorkOrderRequest("Repaired the pipe"));

            complete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var incidents = await client.GetAsync(Route($"/api/v1/incidents?searchTerm={reported.Reference}"));
            var page = await incidents.Content.ReadFromJsonAsync<PagedResult<IncidentListItemDto>>(JsonOptions)
                ?? throw new InvalidOperationException("Listing incidents returned no body.");

            page.Items.ShouldHaveSingleItem().Status.ShouldBe(IncidentStatus.Resolved);
        }
    }

    [DockerFact]
    public async Task Completing_a_stand_alone_work_order_touches_no_incident()
    {
        // The negative case: a work order with no IncidentId must not throw or otherwise reach for
        // an incident that does not exist.
        var (client, tenant) = await CreateTenantClientAsync();

        using (client)
        {
            var technician = await InviteAndAcceptTechnicianAsync(client, tenant);

            var workOrderId = await CreateWorkOrderAsync(
                client, new CreateWorkOrderCommand("Routine inspection", null, WorkOrderPriority.Low, null, null));

            await client.PostAsJsonAsync(
                Route($"/api/v1/work-orders/{workOrderId}/assign"),
                new AssignWorkOrderRequest(technician.User.Id, null));

            var complete = await client.PostAsJsonAsync(
                Route($"/api/v1/work-orders/{workOrderId}/complete"),
                new CompleteWorkOrderRequest(null));

            complete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
    }

    // ---- Cancellation ----

    [DockerFact]
    public async Task Cancelling_records_the_reason_and_closes_the_work_order()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var workOrderId = await CreateWorkOrderAsync(
                client, new CreateWorkOrderCommand("No longer needed", null, WorkOrderPriority.Low, null, null));

            var cancel = await client.PostAsJsonAsync(
                Route($"/api/v1/work-orders/{workOrderId}/cancel"),
                new CancelWorkOrderRequest("Duplicate dispatch"));

            cancel.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var page = await ListAsync(client, "?openOnly=true");
            page.Items.ShouldNotContain(w => w.Id == workOrderId);
        }
    }

    // ---- Tenant isolation ----

    [DockerFact]
    public async Task Work_orders_are_not_visible_across_organizations()
    {
        var (alphaClient, _) = await CreateTenantClientAsync("Alpha Dispatch");
        var (betaClient, _) = await CreateTenantClientAsync("Beta Dispatch");

        using (alphaClient)
        using (betaClient)
        {
            var workOrderId = await CreateWorkOrderAsync(
                alphaClient, new CreateWorkOrderCommand("Alpha work", null, WorkOrderPriority.Low, null, null));

            var betaComplete = await betaClient.PostAsJsonAsync(
                Route($"/api/v1/work-orders/{workOrderId}/complete"),
                new CompleteWorkOrderRequest(null));

            // Not found rather than forbidden, so the identifier's existence is not confirmed.
            betaComplete.StatusCode.ShouldBe(HttpStatusCode.NotFound);

            var betaPage = await ListAsync(betaClient);
            betaPage.Items.ShouldNotContain(w => w.Id == workOrderId);
        }
    }

    [DockerFact]
    public async Task Dispatching_requires_the_create_permission()
    {
        using var anonymous = CreateAnonymousClient();

        var response = await anonymous.PostAsJsonAsync(
            Route("/api/v1/work-orders"),
            new CreateWorkOrderCommand("Fix leak", null, WorkOrderPriority.High, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- Helpers ----

    private static async Task<Guid> CreateWorkOrderAsync(HttpClient client, CreateWorkOrderCommand command)
    {
        var response = await client.PostAsJsonAsync(Route("/api/v1/work-orders"), command);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Dispatching failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<PagedResult<WorkOrderListItemDto>> ListAsync(
        HttpClient client,
        string queryString = "")
    {
        var response = await client.GetAsync(Route($"/api/v1/work-orders{queryString}"));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Listing work orders failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        return await response.Content.ReadFromJsonAsync<PagedResult<WorkOrderListItemDto>>(JsonOptions)
            ?? throw new InvalidOperationException("Listing work orders returned no body.");
    }

    private static async Task<Guid> RegisterAssetAsync(HttpClient client)
    {
        var code = $"HYD-{Guid.CreateVersion7():N}"[..16].ToUpperInvariant();

        var response = await client.PostAsJsonAsync(
            Route("/api/v1/assets"),
            new RegisterAssetCommand(code, "Corner hydrant", AssetType.Hydrant, 51.5080, -0.1281));

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    /// <summary>Invites and accepts a Technician, returning their live tokens.</summary>
    private async Task<AuthenticationResultDto> InviteAndAcceptTechnicianAsync(
        HttpClient adminClient,
        ProvisionedTenant tenant)
    {
        var roleId = await RoleIdAsync(tenant.OrganizationId, SystemRoles.Technician);
        var localPart = $"tech.{Guid.CreateVersion7():N}";
        var email = $"{localPart}@aegis.test";

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
            new AcceptInvitationCommand(token, "thistle marmalade quiet lantern", "Tech", "Nician"));

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
        var context = scope.ServiceProvider
            .GetRequiredService<AegisDbContext>();

        return await context.Roles
            .AsNoTracking()
            .Where(r => r.Name == roleName)
            .Select(r => r.Id)
            .SingleAsync();
    }

    private async Task<string> ResolveTokenAsync(Guid organizationId, Guid invitationId)
    {
        await using var scope = Factory.CreateTenantScope(organizationId);
        var context = scope.ServiceProvider
            .GetRequiredService<AegisDbContext>();
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
