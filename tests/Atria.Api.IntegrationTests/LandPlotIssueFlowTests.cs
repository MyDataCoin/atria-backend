using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Walks the first real object through the API: a land plot under design, not a building. The data
/// is the one the management company actually sent — 0.72 ha on пр. А.Токомбаева 37/6, cadastre
/// identification code 1-04-13-0033-0135, free of encumbrances.
/// <para>
/// What this pins is that the plot survives the round trip as a plot: hectares stay out of the
/// square-metre field, the construction stage stays out of the placement status, and an unchecked
/// cadastre stays distinguishable from a clean one.
/// </para>
/// </summary>
public sealed class LandPlotIssueFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string PropertiesRoute = "/api/v1/properties";
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";

    private readonly AtriaApiFactory _factory;

    public LandPlotIssueFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task LandPlot_RoundTripsAsAPlotAndKeepsStageApartFromPlacement()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var create = await admin.PostAsJsonAsync(PropertiesRoute, new
        {
            name = "Земельный участок, пр. А.Токомбаева 37/6",
            address = "г. Бишкек, Октябрьский р-н, пр. А.Токомбаева 37/6",
            totalValue = 100_000_000m,
            tokenPrice = 1_000m,
            totalTokens = 100_000L,
            currency = "KGS",
            unitType = "land_plot",
            landAreaHectares = 0.72m,
            landPlotCode = "1-04-13-0033-0135",
            constructionStage = "design",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = created.RootElement.GetString()!;

        var plot = await GetPropertyAsync(admin, id);
        plot.GetProperty("unitType").GetString().Should().Be("land_plot");
        plot.GetProperty("landAreaHectares").GetDecimal().Should().Be(0.72m);
        plot.GetProperty("landPlotCode").GetString().Should().Be("1-04-13-0033-0135");
        plot.GetProperty("constructionStage").GetString().Should().Be("design");

        // Hectares of land are not floor area, so nothing derives a per-share metre figure from them.
        plot.GetProperty("totalAreaSqM").ValueKind.Should().Be(JsonValueKind.Null);
        plot.GetProperty("areaPerTokenSqM").ValueKind.Should().Be(JsonValueKind.Null);

        // Nobody has checked the cadastre yet, and that reads as unknown rather than as "clean".
        plot.GetProperty("isFreeOfEncumbrances").ValueKind.Should().Be(JsonValueKind.Null);

        // The placement opens while the site is still a drawing — the whole reason the two are apart.
        (await admin.PostAsync($"{PropertiesRoute}/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var open = await GetPropertyAsync(admin, id);
        open.GetProperty("status").GetString().Should().Be("open");
        open.GetProperty("constructionStage").GetString().Should().Be("design");
    }

    [Fact]
    public async Task Patch_RecordsTheCadastreCheckAndTheBuildSchedule()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var create = await admin.PostAsJsonAsync(PropertiesRoute, new
        {
            name = "Участок под ЖК",
            totalValue = 100_000_000m,
            tokenPrice = 1_000m,
            totalTokens = 100_000L,
            currency = "KGS",
            unitType = "land_plot",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = created.RootElement.GetString()!;

        var patch = await admin.PatchAsJsonAsync($"{PropertiesRoute}/{id}", new
        {
            landPlotCode = "1-04-13-0033-0135",
            landAreaHectares = 0.72m,
            constructionStage = "under_construction",
            readinessPercent = 15,
            isFreeOfEncumbrances = true,
            encumbranceCheckedAtUtc = "2027-07-22T00:00:00Z",
        });
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var updated = await GetPropertyAsync(admin, id);
        updated.GetProperty("landPlotCode").GetString().Should().Be("1-04-13-0033-0135");
        updated.GetProperty("landAreaHectares").GetDecimal().Should().Be(0.72m);
        updated.GetProperty("constructionStage").GetString().Should().Be("under_construction");
        updated.GetProperty("readinessPercent").GetInt32().Should().Be(15);
        updated.GetProperty("isFreeOfEncumbrances").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Patch_IgnoresAnEncumbranceVerdictSentWithoutItsDate()
    {
        // An all-clear nobody can date is not an all-clear. The pair is applied together or not at all.
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var create = await admin.PostAsJsonAsync(PropertiesRoute, new
        {
            name = "Участок без проверки",
            totalValue = 1_000_000m,
            tokenPrice = 1_000m,
            totalTokens = 1_000L,
            currency = "KGS",
        });
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = created.RootElement.GetString()!;

        var patch = await admin.PatchAsJsonAsync($"{PropertiesRoute}/{id}", new
        {
            isFreeOfEncumbrances = true,
        });
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var updated = await GetPropertyAsync(admin, id);
        updated.GetProperty("isFreeOfEncumbrances").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static async Task<JsonElement> GetPropertyAsync(HttpClient client, string id)
    {
        var response = await client.GetAsync($"{PropertiesRoute}/{id}");
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
        var token = doc.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
