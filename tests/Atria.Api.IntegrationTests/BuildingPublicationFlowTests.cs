using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Publication is a single administrator's call and takes effect immediately — there is no approval
/// queue in front of it. A building publishes all its units at once, so an admin opens the whole
/// object instead of clicking through every apartment and garage.
/// </summary>
public sealed class BuildingPublicationFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string BuildingsRoute = "/api/v1/buildings";
    private const string PropertiesRoute = "/api/v1/properties";
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";
    private const string PendingRoute = "/api/v1/governance/critical-actions/pending";

    private readonly AtriaApiFactory _factory;

    public BuildingPublicationFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Publishing_a_unit_opens_it_on_the_call_without_any_approval()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var buildingId = await CreateBuildingAsync(admin);
        var unitId = await CreateUnitAsync(admin, buildingId, "Апартамент 1");

        var response = await admin.PostAsync($"{PropertiesRoute}/{unitId}/publish", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "publishing is done, not requested");
        (await GetJsonAsync(admin, $"{PropertiesRoute}/{unitId}"))
            .GetProperty("status").GetString().Should().Be("open");

        // Nothing waits for a second pair of eyes: publication is not a critical action any more.
        // Scoped to THIS unit on purpose — the approval queue is global and other suites legitimately
        // leave their own requests in it (investor blocks, payout runs), so asserting the queue is
        // empty would make this test fail on whatever else happened to run first.
        (await GetJsonAsync(admin, PendingRoute)).EnumerateArray()
            .Should().NotContain(a => a.GetProperty("targetId").GetGuid().ToString() == unitId,
                "publishing must not queue an approval");
    }

    [Fact]
    public async Task Publishing_a_building_opens_every_unit_inside_it()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var anon = _factory.CreateClient();

        var buildingId = await CreateBuildingAsync(admin);
        var flat = await CreateUnitAsync(admin, buildingId, "2-комнатный апартамент");
        var garage = await CreateUnitAsync(admin, buildingId, "Гараж Г-4");

        // Один из юнитов заранее объявлен «скоро в продаже» — публикация здания должна забрать оба.
        (await admin.PostAsync($"{PropertiesRoute}/{garage}/announce", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await admin.PostAsync($"{BuildingsRoute}/{buildingId}/publish", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("published").GetInt32().Should().Be(2);
        body.RootElement.GetProperty("alreadyOpen").GetInt32().Should().Be(0);
        body.RootElement.GetProperty("skipped").GetInt32().Should().Be(0);

        foreach (var id in new[] { flat, garage })
        {
            (await GetJsonAsync(anon, $"{PropertiesRoute}/{id}"))
                .GetProperty("status").GetString().Should().Be("open");
        }
    }

    [Fact]
    public async Task Publishing_a_building_again_is_not_an_error_and_counts_what_was_already_open()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var buildingId = await CreateBuildingAsync(admin);
        await CreateUnitAsync(admin, buildingId, "Апартамент 1");
        await CreateUnitAsync(admin, buildingId, "Апартамент 2");

        await admin.PostAsync($"{BuildingsRoute}/{buildingId}/publish", null);
        var second = await admin.PostAsync($"{BuildingsRoute}/{buildingId}/publish", null);

        second.StatusCode.Should().Be(HttpStatusCode.OK,
            "an admin repeating the action must not be met with a failure");
        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("published").GetInt32().Should().Be(0);
        body.RootElement.GetProperty("alreadyOpen").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Publishing_an_empty_building_is_refused()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var buildingId = await CreateBuildingAsync(admin);

        (await admin.PostAsync($"{BuildingsRoute}/{buildingId}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "there is nothing to open");
    }

    [Fact]
    public async Task Publishing_a_building_requires_the_admin_role()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var buildingId = await CreateBuildingAsync(admin);
        await CreateUnitAsync(admin, buildingId, "Апартамент 1");

        var anon = _factory.CreateClient();
        (await anon.PostAsync($"{BuildingsRoute}/{buildingId}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<string> CreateBuildingAsync(HttpClient adminClient)
    {
        var create = await adminClient.PostAsJsonAsync(BuildingsRoute, new
        {
            name = "ЖК Ала-Тоо, блок B",
            city = "Бишкек",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        return doc.RootElement.GetString()!;
    }

    private static async Task<string> CreateUnitAsync(HttpClient adminClient, string buildingId, string name)
    {
        var create = await adminClient.PostAsJsonAsync(PropertiesRoute, new
        {
            name,
            totalValue = 83_447.5m,
            tokenPrice = 1_450m,
            totalTokens = 57.55m,
            currency = "KGS",
            buildingId,
            unitType = "apartment",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        return doc.RootElement.GetString()!;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string route)
    {
        var response = await client.GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task AuthenticateAdminAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(AdminLoginRoute, new
        {
            username = "admin",
            password = "admin-test-password",
        });
        login.IsSuccessStatusCode.Should().BeTrue("static admin login should be enabled in tests");
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", doc.RootElement.GetProperty("accessToken").GetString());
    }
}
