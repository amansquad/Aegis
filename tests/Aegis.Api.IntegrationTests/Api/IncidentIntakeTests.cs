using System.Net;
using System.Net.Http.Json;
using Aegis.Api.Controllers;
using Aegis.Api.IntegrationTests.Infrastructure;
using Aegis.Application.Assets.Commands;
using Aegis.Application.Common.Models;
using Aegis.Application.Incidents.Commands;
using Aegis.Application.Incidents.Queries;
using Aegis.Domain.Assets;
using Aegis.Domain.Incidents;

namespace Aegis.Api.IntegrationTests.Api;

/// <summary>
/// Covers natural-language intake end to end.
/// </summary>
/// <remarks>
/// These run against the rule-based extractor, because CI has no API key. That is deliberate rather
/// than a compromise: it means the properties asserted here are the ones that must hold whichever
/// classifier is active — the report is preserved, review is required when it should be, the asset
/// comes from our own data, and duplicates are surfaced rather than merged.
/// </remarks>
public sealed class IncidentIntakeTests(AegisWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    // Real coordinates so the proximity arithmetic is checkable against a map.
    private const double TrafalgarLatitude = 51.5080;
    private const double TrafalgarLongitude = -0.1281;

    private static Uri Route(string path) => new(path, UriKind.Relative);

    [DockerFact]
    public async Task A_plain_text_report_becomes_a_structured_incident()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var result = await ReportAsync(
                client,
                "There is water gushing up through the pavement outside 14 Northgate Road.");

            result.Reference.ShouldStartWith("INC-");
            result.Category.ShouldBe(IncidentCategory.Leak);
            result.Summary.ShouldNotBeNullOrWhiteSpace();

            // The rule-based extractor is active in CI, so review is mandatory whatever it scored.
            result.ClassifiedBy.ShouldBe(ClassificationMethod.Heuristic);
            result.RequiresReview.ShouldBeTrue();
        }
    }

    [DockerFact]
    public async Task The_reporters_own_words_are_preserved_alongside_the_classification()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            const string words = "Brown water coming out of the tap since this morning, tastes odd.";

            await ReportAsync(client, words);

            // Searching the report text finds it, which is only possible because the original was
            // stored rather than replaced by the summary.
            var page = await ListAsync(client, "?searchTerm=tastes odd");

            page.Items.ShouldNotBeEmpty();
            page.Items[0].Category.ShouldBe(IncidentCategory.WaterQuality);
        }
    }

    [DockerFact]
    public async Task A_report_describing_danger_is_flagged_and_escalated()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var result = await ReportAsync(
                client,
                "Strong smell of gas near the pumping station and the ground has collapsed.");

            result.RequiresReview.ShouldBeTrue();

            var page = await ListAsync(client, "?safetyRiskOnly=true");

            page.Items.ShouldContain(i => i.Reference == result.Reference);
            page.Items.Single(i => i.Reference == result.Reference).PublicSafetyRisk.ShouldBeTrue();
        }
    }

    // ---- Asset resolution comes from our own data ----

    [DockerFact]
    public async Task A_report_near_an_asset_is_linked_to_it()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var code = $"HYD-{Guid.CreateVersion7():N}"[..16].ToUpperInvariant();

            await client.PostAsJsonAsync(
                Route("/api/v1/assets"),
                new RegisterAssetCommand(
                    code, "Trafalgar hydrant", AssetType.Hydrant,
                    TrafalgarLatitude, TrafalgarLongitude));

            var result = await ReportAsync(
                client,
                "Water leaking from the hydrant on the corner here.",
                TrafalgarLatitude,
                TrafalgarLongitude);

            result.MatchedAssetCode.ShouldBe(code);
        }
    }

    [DockerFact]
    public async Task An_asset_code_quoted_in_a_report_is_only_a_hint_and_must_resolve_locally()
    {
        // The security property. A reporter can write any code they like; it is looked up through
        // the ordinary tenant-scoped query, so one that does not exist here matches nothing.
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var result = await ReportAsync(
                client,
                "Leak at hydrant HYD-ZZ-999999 which I am sure is yours.");

            result.MatchedAssetCode.ShouldBeNull();
        }
    }

    [DockerFact]
    public async Task An_asset_belonging_to_another_organization_is_never_matched()
    {
        var (alphaClient, _) = await CreateTenantClientAsync("Alpha Incidents");
        var (betaClient, _) = await CreateTenantClientAsync("Beta Incidents");

        using (alphaClient)
        using (betaClient)
        {
            var code = $"PMP-{Guid.CreateVersion7():N}"[..16].ToUpperInvariant();

            // Registered by Alpha, at a position Beta will report from.
            await alphaClient.PostAsJsonAsync(
                Route("/api/v1/assets"),
                new RegisterAssetCommand(
                    code, "Alpha pump", AssetType.Pump, TrafalgarLatitude, TrafalgarLongitude));

            var result = await ReportAsync(
                betaClient,
                $"Leak at pump {code}, water everywhere.",
                TrafalgarLatitude,
                TrafalgarLongitude);

            // Neither the quoted code nor the position may reach across the tenant boundary.
            result.MatchedAssetCode.ShouldBeNull();
        }
    }

    // ---- Duplicates are surfaced, not merged ----

    [DockerFact]
    public async Task A_second_nearby_report_of_the_same_category_surfaces_the_first()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var first = await ReportAsync(
                client,
                "Burst main flooding the road here.",
                TrafalgarLatitude,
                TrafalgarLongitude);

            var second = await ReportAsync(
                client,
                "Water pouring across the street, looks like a burst.",
                TrafalgarLatitude + 0.0005,
                TrafalgarLongitude);

            second.PossibleDuplicateOf.ShouldBe(first.Reference);

            // Surfaced, not acted on. Both incidents exist until a dispatcher decides.
            var page = await ListAsync(client, "?openOnly=true&pageSize=100");
            page.Items.ShouldContain(i => i.Reference == first.Reference);
            page.Items.ShouldContain(i => i.Reference == second.Reference);
        }
    }

    [DockerFact]
    public async Task A_distant_report_is_not_treated_as_a_duplicate()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            await ReportAsync(client, "Burst main flooding the road.", TrafalgarLatitude, TrafalgarLongitude);

            // Manchester.
            var distant = await ReportAsync(
                client, "Burst main flooding the road.", 53.4808, -2.2426);

            distant.PossibleDuplicateOf.ShouldBeNull();
        }
    }

    // ---- Triage ----

    [DockerFact]
    public async Task Triage_confirms_the_classification_and_retains_what_was_proposed()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var reported = await ReportAsync(client, "Water leaking onto the pavement.");

            var triage = await client.PostAsJsonAsync(
                Route($"/api/v1/incidents/{reported.IncidentId}/triage"),
                new TriageIncidentRequest(
                    IncidentCategory.Blockage,
                    IncidentSeverity.High,
                    "Confirmed: drain blockage causing surface water",
                    null));

            triage.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var page = await ListAsync(client, $"?searchTerm={reported.Reference}");
            var incident = page.Items.ShouldHaveSingleItem();

            incident.Status.ShouldBe(IncidentStatus.Triaged);
            incident.Category.ShouldBe(IncidentCategory.Blockage);
            incident.Severity.ShouldBe(IncidentSeverity.High);

            // Triage makes the classification human-owned, so review is no longer required.
            incident.ClassifiedBy.ShouldBe(ClassificationMethod.Manual);
            incident.RequiresReview.ShouldBeFalse();
        }
    }

    [DockerFact]
    public async Task An_incident_can_be_resolved_and_cannot_then_be_triaged()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var reported = await ReportAsync(client, "Leaking stop tap in the footpath.");

            var resolve = await client.PostAsJsonAsync(
                Route($"/api/v1/incidents/{reported.IncidentId}/resolve"),
                new ResolveIncidentRequest("Tap replaced"));

            resolve.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var triage = await client.PostAsJsonAsync(
                Route($"/api/v1/incidents/{reported.IncidentId}/triage"),
                new TriageIncidentRequest(IncidentCategory.Leak, IncidentSeverity.Low, null, null));

            triage.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }
    }

    [DockerFact]
    public async Task An_incident_from_another_organization_is_not_found()
    {
        var (alphaClient, _) = await CreateTenantClientAsync("Alpha Triage");
        var (betaClient, _) = await CreateTenantClientAsync("Beta Triage");

        using (alphaClient)
        using (betaClient)
        {
            var reported = await ReportAsync(alphaClient, "Water leaking onto the pavement.");

            var triage = await betaClient.PostAsJsonAsync(
                Route($"/api/v1/incidents/{reported.IncidentId}/triage"),
                new TriageIncidentRequest(IncidentCategory.Leak, IncidentSeverity.Low, null, null));

            // Not found rather than forbidden: reporting "forbidden" would confirm the identifier
            // names a real incident somewhere on the platform.
            triage.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [DockerFact]
    public async Task Incidents_are_not_visible_across_organizations()
    {
        var (alphaClient, _) = await CreateTenantClientAsync("Alpha Visibility");
        var (betaClient, _) = await CreateTenantClientAsync("Beta Visibility");

        using (alphaClient)
        using (betaClient)
        {
            var reported = await ReportAsync(alphaClient, "Blocked drain overflowing here.");

            var betaPage = await ListAsync(betaClient, $"?searchTerm={reported.Reference}");

            betaPage.Items.ShouldBeEmpty();
        }
    }

    [DockerFact]
    public async Task A_report_that_is_too_short_to_act_on_is_rejected()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var response = await client.PostAsJsonAsync(
                Route("/api/v1/incidents"),
                new ReportIncidentCommand("leak", null, null, null, null));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [DockerFact]
    public async Task Reporting_requires_the_report_permission()
    {
        using var anonymous = CreateAnonymousClient();

        var response = await anonymous.PostAsJsonAsync(
            Route("/api/v1/incidents"),
            new ReportIncidentCommand("Water leaking onto the pavement.", null, null, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- Helpers ----

    private static async Task<ReportIncidentResult> ReportAsync(
        HttpClient client,
        string reportText,
        double? latitude = null,
        double? longitude = null)
    {
        var response = await client.PostAsJsonAsync(
            Route("/api/v1/incidents"),
            new ReportIncidentCommand(reportText, latitude, longitude, null, null));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Reporting failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        return await response.Content.ReadFromJsonAsync<ReportIncidentResult>(JsonOptions)
            ?? throw new InvalidOperationException("Reporting returned no body.");
    }

    private static async Task<PagedResult<IncidentListItemDto>> ListAsync(
        HttpClient client,
        string queryString = "")
    {
        var response = await client.GetAsync(Route($"/api/v1/incidents{queryString}"));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Listing incidents failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        return await response.Content.ReadFromJsonAsync<PagedResult<IncidentListItemDto>>(JsonOptions)
            ?? throw new InvalidOperationException("Listing incidents returned no body.");
    }
}
