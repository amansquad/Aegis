using System.Net;
using System.Net.Http.Json;
using Aegis.Api.Controllers;
using Aegis.Api.IntegrationTests.Infrastructure;
using Aegis.Application.Assets.Commands;
using Aegis.Application.Assets.Queries;
using Aegis.Application.Common.Models;
using Aegis.Domain.Assets;

namespace Aegis.Api.IntegrationTests.Api;

/// <summary>
/// Covers the asset registry end to end, including the proximity query.
/// </summary>
/// <remarks>
/// The proximity tests matter most. That query composes a bounding box with an exact great-circle
/// predicate written as translatable arithmetic, and whether SQL Server actually accepts it is not
/// something reasoning can settle.
/// </remarks>
public sealed class AssetRegistryTests(AegisWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    // Real landmarks, so the distances are checkable against any map.
    private const double TrafalgarSquareLatitude = 51.5080;
    private const double TrafalgarSquareLongitude = -0.1281;

    // Roughly 500 m from Trafalgar Square — measured, not assumed. The first version of this test
    // called it 1.1 km and asserted against a 500 m radius, which put the assertion exactly on the
    // boundary it was meant to be testing. The margins below are deliberately wide on both sides.
    private const double CoventGardenLatitude = 51.5117;
    private const double CoventGardenLongitude = -0.1240;

    // Roughly 262 km away.
    private const double ManchesterLatitude = 53.4808;
    private const double ManchesterLongitude = -2.2426;

    private static Uri Route(string path) => new(path, UriKind.Relative);

    [DockerFact]
    public async Task An_asset_can_be_registered_and_listed()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var code = UniqueCode("PMP");
            await RegisterAsync(client, code, "Northgate Pump 2", AssetType.Pump);

            var page = await ListAsync(client, $"?searchTerm={code}");

            var asset = page.Items.ShouldHaveSingleItem();
            asset.Code.ShouldBe(code);
            asset.Type.ShouldBe(AssetType.Pump);
            asset.Status.ShouldBe(AssetStatus.Planned);
            asset.Condition.ShouldBe(AssetCondition.Unknown);
        }
    }

    [DockerFact]
    public async Task An_asset_code_is_normalised_and_must_be_unique_within_the_organization()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var code = UniqueCode("VLV");
            await RegisterAsync(client, code, "Isolation valve", AssetType.Valve);

            // Lower case, which normalisation should collapse onto the existing code.
            var duplicate = await client.PostAsJsonAsync(
                Route("/api/v1/assets"),
                NewAsset(code.ToLowerInvariant(), "Another valve", AssetType.Valve));

            duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await duplicate.Content.ReadAsStringAsync()).ShouldContain("Asset.DuplicateCode");
        }
    }

    [DockerFact]
    public async Task The_same_asset_code_may_exist_in_two_organizations()
    {
        // Codes are unique per organization, not platform-wide. Two utilities both having a
        // "PMP-001" is entirely normal and must not collide.
        var (alphaClient, _) = await CreateTenantClientAsync("Alpha Assets");
        var (betaClient, _) = await CreateTenantClientAsync("Beta Assets");

        using (alphaClient)
        using (betaClient)
        {
            var code = UniqueCode("SHARED");

            await RegisterAsync(alphaClient, code, "Alpha pump", AssetType.Pump);
            await RegisterAsync(betaClient, code, "Beta pump", AssetType.Pump);

            (await ListAsync(alphaClient, $"?searchTerm={code}")).Items.Count.ShouldBe(1);
            (await ListAsync(betaClient, $"?searchTerm={code}")).Items.Count.ShouldBe(1);
        }
    }

    [DockerFact]
    public async Task Assets_are_not_visible_across_organizations()
    {
        var (alphaClient, _) = await CreateTenantClientAsync("Alpha Isolation");
        var (betaClient, _) = await CreateTenantClientAsync("Beta Isolation");

        using (alphaClient)
        using (betaClient)
        {
            var code = UniqueCode("PRIVATE");
            await RegisterAsync(alphaClient, code, "Alpha only", AssetType.Transformer);

            (await ListAsync(betaClient, $"?searchTerm={code}")).Items.ShouldBeEmpty();
        }
    }

    // ---- Proximity ----

    [DockerFact]
    public async Task A_proximity_search_returns_assets_inside_the_radius()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var nearCode = UniqueCode("NEAR");
            var farCode = UniqueCode("FAR");

            await RegisterAsync(
                client, nearCode, "Covent Garden hydrant", AssetType.Hydrant,
                CoventGardenLatitude, CoventGardenLongitude);

            await RegisterAsync(
                client, farCode, "Manchester hydrant", AssetType.Hydrant,
                ManchesterLatitude, ManchesterLongitude);

            var within2Km = await ListAsync(
                client,
                $"?nearLatitude={TrafalgarSquareLatitude}&nearLongitude={TrafalgarSquareLongitude}" +
                "&withinMetres=2000&pageSize=100");

            var codes = within2Km.Items.Select(a => a.Code).ToArray();

            codes.ShouldContain(nearCode);
            codes.ShouldNotContain(farCode);
        }
    }

    [DockerFact]
    public async Task A_proximity_search_excludes_assets_just_outside_the_radius()
    {
        // Covent Garden is about 500 m from Trafalgar Square, so a 200 m radius must exclude it.
        // A bounding-box-only implementation would still admit it, since 200 m of latitude spans
        // well past the point once the box is squared off at the corners.
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var code = UniqueCode("EDGE");

            await RegisterAsync(
                client, code, "Covent Garden hydrant", AssetType.Hydrant,
                CoventGardenLatitude, CoventGardenLongitude);

            var within200m = await ListAsync(
                client,
                $"?nearLatitude={TrafalgarSquareLatitude}&nearLongitude={TrafalgarSquareLongitude}" +
                "&withinMetres=200&pageSize=100");

            within200m.Items.Select(a => a.Code).ShouldNotContain(code);
        }
    }

    [DockerFact]
    public async Task A_proximity_search_excludes_assets_with_no_recorded_position()
    {
        // Treating a missing position as distance zero would put every unsurveyed asset at the top
        // of every proximity search, which is the opposite of useful.
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var code = UniqueCode("NOPOS");
            await RegisterAsync(client, code, "Unsurveyed valve", AssetType.Valve);

            var nearby = await ListAsync(
                client,
                $"?nearLatitude={TrafalgarSquareLatitude}&nearLongitude={TrafalgarSquareLongitude}" +
                "&withinMetres=50000&pageSize=100");

            nearby.Items.Select(a => a.Code).ShouldNotContain(code);
        }
    }

    [DockerFact]
    public async Task An_incomplete_proximity_search_is_rejected()
    {
        // A point with no radius, or a radius around nowhere, is not a query anyone meant to write.
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var response = await client.GetAsync(
                Route($"/api/v1/assets?nearLatitude={TrafalgarSquareLatitude}"));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [DockerFact]
    public async Task A_position_is_stored_and_returned_without_axis_confusion()
    {
        // The commonest spatial bug there is. Round-tripping through the API and the database
        // catches a swap anywhere in the conversion chain.
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var code = UniqueCode("AXIS");

            await RegisterAsync(
                client, code, "Trafalgar Square sensor", AssetType.Sensor,
                TrafalgarSquareLatitude, TrafalgarSquareLongitude);

            var asset = (await ListAsync(client, $"?searchTerm={code}")).Items.ShouldHaveSingleItem();

            asset.Latitude.ShouldNotBeNull();
            asset.Longitude.ShouldNotBeNull();
            asset.Latitude.Value.ShouldBe(TrafalgarSquareLatitude, 0.0001);
            asset.Longitude.Value.ShouldBe(TrafalgarSquareLongitude, 0.0001);
        }
    }

    // ---- Inspections ----

    [DockerFact]
    public async Task Recording_an_inspection_updates_the_assets_condition()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var code = UniqueCode("INSP");
            var assetId = await RegisterAsync(client, code, "Pump under review", AssetType.Pump);

            var response = await client.PostAsJsonAsync(
                Route($"/api/v1/assets/{assetId}/inspections"),
                new RecordInspectionRequest(
                    AssetCondition.Poor,
                    DateTimeOffset.UtcNow.AddHours(-1),
                    "Visible corrosion on the casing"));

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var asset = (await ListAsync(client, $"?searchTerm={code}")).Items.ShouldHaveSingleItem();

            asset.Condition.ShouldBe(AssetCondition.Poor);
            asset.LastInspectedOnUtc.ShouldNotBeNull();
        }
    }

    [DockerFact]
    public async Task A_future_dated_inspection_is_rejected()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var assetId = await RegisterAsync(client, UniqueCode("FUT"), "Pump", AssetType.Pump);

            var response = await client.PostAsJsonAsync(
                Route($"/api/v1/assets/{assetId}/inspections"),
                new RecordInspectionRequest(
                    AssetCondition.Good,
                    DateTimeOffset.UtcNow.AddDays(1),
                    null));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    // ---- Decommissioning ----

    [DockerFact]
    public async Task A_parent_cannot_be_decommissioned_while_it_contains_assets_in_service()
    {
        // Otherwise the registry describes a retired station containing operational pumps, and the
        // pumps are orphaned under a parent nobody maintains.
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var stationId = await RegisterAsync(client, UniqueCode("STN"), "Pump station", AssetType.Site);

            var childResponse = await client.PostAsJsonAsync(
                Route("/api/v1/assets"),
                NewAsset(UniqueCode("CHILD"), "Pump 1", AssetType.Pump) with { ParentAssetId = stationId });

            childResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var decommission = await client.PostAsJsonAsync(
                Route($"/api/v1/assets/{stationId}/decommission"),
                new DecommissionAssetRequest("Site closed"));

            decommission.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await decommission.Content.ReadAsStringAsync()).ShouldContain("Asset.HasActiveChildren");
        }
    }

    [DockerFact]
    public async Task A_decommissioned_asset_remains_in_the_registry()
    {
        // Retirement, not deletion. Operators must be able to say what was installed where, often
        // decades after removal.
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var code = UniqueCode("RETIRE");
            var assetId = await RegisterAsync(client, code, "Old transformer", AssetType.Transformer);

            var response = await client.PostAsJsonAsync(
                Route($"/api/v1/assets/{assetId}/decommission"),
                new DecommissionAssetRequest("Replaced under the 2026 programme"));

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var listed = (await ListAsync(client, $"?searchTerm={code}")).Items.ShouldHaveSingleItem();
            listed.Status.ShouldBe(AssetStatus.Decommissioned);

            // And is hidden from the default operational view.
            var operational = await ListAsync(client, $"?searchTerm={code}&excludeDecommissioned=true");
            operational.Items.ShouldBeEmpty();
        }
    }

    // ---- Authorization ----

    [DockerFact]
    public async Task Listing_assets_requires_the_view_permission()
    {
        using var anonymous = CreateAnonymousClient();

        (await anonymous.GetAsync(Route("/api/v1/assets")))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [DockerFact]
    public async Task An_unknown_sort_field_is_rejected_with_the_valid_options()
    {
        var (client, _) = await CreateTenantClientAsync();

        using (client)
        {
            var response = await client.GetAsync(Route("/api/v1/assets?sortBy=nonsense"));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).ShouldContain("Unknown sort field");
        }
    }

    // ---- Helpers ----

    private static string UniqueCode(string prefix) =>
        $"{prefix}-{Guid.CreateVersion7():N}"[..20].ToUpperInvariant();

    private static RegisterAssetCommand NewAsset(
        string code,
        string name,
        AssetType type,
        double? latitude = null,
        double? longitude = null) =>
        new(code, name, type, latitude, longitude);

    private static async Task<Guid> RegisterAsync(
        HttpClient client,
        string code,
        string name,
        AssetType type,
        double? latitude = null,
        double? longitude = null)
    {
        var response = await client.PostAsJsonAsync(
            Route("/api/v1/assets"),
            NewAsset(code, name, type, latitude, longitude));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Registering asset failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<PagedResult<AssetListItemDto>> ListAsync(
        HttpClient client,
        string queryString = "")
    {
        var response = await client.GetAsync(Route($"/api/v1/assets{queryString}"));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Listing assets failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());
        }

        return await response.Content.ReadFromJsonAsync<PagedResult<AssetListItemDto>>(JsonOptions)
            ?? throw new InvalidOperationException("Listing assets returned no body.");
    }
}
