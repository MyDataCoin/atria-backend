using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Confirms the property status contract end to end through the real pipeline + EF in-memory store:
/// GET /properties exposes a lowercase <c>status</c> (draft|open|completed) — including anonymously;
/// publish (Draft->Open) and complete (Open->Completed) are Admin-only; publish on an already-open
/// property is 409 (not idempotent 204); complete on a non-open property is 409.
/// </summary>
public sealed class PropertyStatusFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string PropertiesRoute = "/api/v1/properties";
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";
    private const string RequestOtpRoute = "/api/v1/auth/register/phone/request-otp";
    private const string VerifyOtpRoute = "/api/v1/auth/register/phone/verify-otp";
    private const string DevCode = "333333";

    private readonly AtriaApiFactory _factory;

    public PropertyStatusFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Property_StatusLifecycle_AndRolesAndConflicts()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        // Admin creates a property -> starts as draft.
        var id = await CreatePropertyAsync(admin);

        // Admin GET exposes a lowercase status and no isActive field (drafts are admin-only).
        var anon = _factory.CreateClient();
        var draft = await GetPropertyAsync(admin, id);
        draft.GetProperty("status").GetString().Should().Be("draft");
        draft.TryGetProperty("isActive", out _).Should().BeFalse("the DTO must no longer expose isActive");

        // Completing a draft (not open) -> 409.
        (await admin.PostAsync($"{PropertiesRoute}/{id}/complete", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Publish (Draft -> Open) is Admin-only.
        (await admin.PostAsync($"{PropertiesRoute}/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetPropertyAsync(anon, id)).GetProperty("status").GetString().Should().Be("open");

        // Publishing an already-open property is a CONFLICT (not an idempotent 204).
        (await admin.PostAsync($"{PropertiesRoute}/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Complete (Open -> Completed).
        (await admin.PostAsync($"{PropertiesRoute}/{id}/complete", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetPropertyAsync(anon, id)).GetProperty("status").GetString().Should().Be("completed");

        // Completed is terminal: completing again -> 409.
        (await admin.PostAsync($"{PropertiesRoute}/{id}/complete", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Property_FullLifecycle_DraftComingSoonOpenCompleted()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var anon = _factory.CreateClient();

        var id = await CreatePropertyAsync(admin);
        (await GetPropertyAsync(admin, id)).GetProperty("status").GetString().Should().Be("draft");

        // Announce (Draft -> ComingSoon), Admin only.
        (await admin.PostAsync($"{PropertiesRoute}/{id}/announce", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetPropertyAsync(anon, id)).GetProperty("status").GetString().Should().Be("coming_soon");

        // Announcing again is a conflict (not draft anymore).
        (await admin.PostAsync($"{PropertiesRoute}/{id}/announce", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Publish works from coming_soon (ComingSoon -> Open).
        (await admin.PostAsync($"{PropertiesRoute}/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetPropertyAsync(anon, id)).GetProperty("status").GetString().Should().Be("open");

        // Complete (Open -> Completed).
        (await admin.PostAsync($"{PropertiesRoute}/{id}/complete", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetPropertyAsync(anon, id)).GetProperty("status").GetString().Should().Be("completed");
    }

    [Fact]
    public async Task PublicCatalogue_HidesDrafts_ButShowsComingSoon()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var anon = _factory.CreateClient();

        // One draft (admin-only) and one announced as coming_soon (public).
        var draftId = await CreatePropertyAsync(admin);
        var comingSoonId = await CreatePropertyAsync(admin);
        (await admin.PostAsync($"{PropertiesRoute}/{comingSoonId}/announce", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Anonymous list: contains the coming_soon object, never the draft.
        var anonIds = await ListPropertyIdsAsync(anon);
        anonIds.Should().Contain(comingSoonId).And.NotContain(draftId);

        // Anonymous by-id: draft is 404, coming_soon is visible.
        (await anon.GetAsync($"{PropertiesRoute}/{draftId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetPropertyAsync(anon, comingSoonId)).GetProperty("status").GetString().Should().Be("coming_soon");

        // Admin sees both, including the draft.
        var adminIds = await ListPropertyIdsAsync(admin);
        adminIds.Should().Contain(draftId).And.Contain(comingSoonId);
        (await GetPropertyAsync(admin, draftId)).GetProperty("status").GetString().Should().Be("draft");
    }

    [Fact]
    public async Task Announce_PullsAnOpenPropertyBackToComingSoon()
    {
        // Reproduces the admin report: an already-open property ("Тест 1") must be markable as
        // coming_soon, and GET must then return the lowercase "coming_soon" (not "open").
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var anon = _factory.CreateClient();

        var id = await CreatePropertyAsync(admin);
        (await admin.PostAsync($"{PropertiesRoute}/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetPropertyAsync(anon, id)).GetProperty("status").GetString().Should().Be("open");

        // Announce from open -> coming_soon.
        (await admin.PostAsync($"{PropertiesRoute}/{id}/announce", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetPropertyAsync(anon, id)).GetProperty("status").GetString().Should().Be("coming_soon");
    }

    [Fact]
    public async Task Unannounce_MovesComingSoonBackToDraft_AndHidesFromPublic()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var anon = _factory.CreateClient();

        var id = await CreatePropertyAsync(admin);
        (await admin.PostAsync($"{PropertiesRoute}/{id}/announce", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetPropertyAsync(anon, id)).GetProperty("status").GetString().Should().Be("coming_soon");

        // Unannounce (ComingSoon -> Draft): back to admin-only draft, hidden from the public site.
        (await admin.PostAsync($"{PropertiesRoute}/{id}/unannounce", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetPropertyAsync(admin, id)).GetProperty("status").GetString().Should().Be("draft");
        (await anon.GetAsync($"{PropertiesRoute}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Unannouncing a draft (not coming soon) -> 409.
        (await admin.PostAsync($"{PropertiesRoute}/{id}/unannounce", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Unannounce_RequiresAdminRole()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreatePropertyAsync(admin);
        (await admin.PostAsync($"{PropertiesRoute}/{id}/announce", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var anon = _factory.CreateClient();
        (await anon.PostAsync($"{PropertiesRoute}/{id}/unannounce", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);
        (await investor.PostAsync($"{PropertiesRoute}/{id}/unannounce", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_PersistsCharacteristics_ReturnedInReadModel()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var create = await admin.PostAsJsonAsync(PropertiesRoute, new
        {
            name = "Ala-Too Business Center",
            description = "Class-A office tower",
            address = "Chuy Ave 136, Bishkek",
            totalValue = 5_000_000m,
            tokenPrice = 500m,
            totalTokens = 10_000L,
            currency = "KGS",
            propertyType = "commercial",
            city = "Bishkek",
            yearBuilt = 2019,
            developer = "Ala-Too Development",
            floors = 24,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = createdDoc.RootElement.GetString()!;

        // Publish so the anonymous public site can read the object, then assert every characteristic.
        (await admin.PostAsync($"{PropertiesRoute}/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var anon = _factory.CreateClient();
        var dto = await GetPropertyAsync(anon, id);
        dto.GetProperty("address").GetString().Should().Be("Chuy Ave 136, Bishkek");
        dto.GetProperty("propertyType").GetString().Should().Be("commercial");
        dto.GetProperty("city").GetString().Should().Be("Bishkek");
        dto.GetProperty("yearBuilt").GetInt32().Should().Be(2019);
        dto.GetProperty("developer").GetString().Should().Be("Ala-Too Development");
        dto.GetProperty("floors").GetInt32().Should().Be(24);
    }

    [Fact]
    public async Task PauseAndResume_ToggleSalesPaused_ExposedAnonymously()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var anon = _factory.CreateClient();

        var id = await CreatePropertyAsync(admin);
        (await admin.PostAsync($"{PropertiesRoute}/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Fresh open property: salesPaused is false and visible to the public site.
        (await GetPropertyAsync(anon, id)).GetProperty("salesPaused").GetBoolean().Should().BeFalse();

        // Pause -> salesPaused true (anonymously visible), so the site can block "buy".
        (await admin.PostAsync($"{PropertiesRoute}/{id}/pause", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetPropertyAsync(anon, id)).GetProperty("salesPaused").GetBoolean().Should().BeTrue();

        // Pausing again -> 409.
        (await admin.PostAsync($"{PropertiesRoute}/{id}/pause", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Resume -> salesPaused false.
        (await admin.PostAsync($"{PropertiesRoute}/{id}/resume", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetPropertyAsync(anon, id)).GetProperty("salesPaused").GetBoolean().Should().BeFalse();

        // Resuming when not paused -> 409.
        (await admin.PostAsync($"{PropertiesRoute}/{id}/resume", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PauseAndResume_RequireAdminRole()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreatePropertyAsync(admin);

        var anon = _factory.CreateClient();
        (await anon.PostAsync($"{PropertiesRoute}/{id}/pause", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsync($"{PropertiesRoute}/{id}/resume", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);
        (await investor.PostAsync($"{PropertiesRoute}/{id}/pause", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Announce_RequiresAdminRole()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreatePropertyAsync(admin);

        var anon = _factory.CreateClient();
        (await anon.PostAsync($"{PropertiesRoute}/{id}/announce", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);
        (await investor.PostAsync($"{PropertiesRoute}/{id}/announce", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Publish_RequiresAdminRole()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreatePropertyAsync(admin);

        // Unauthenticated -> 401.
        var anon = _factory.CreateClient();
        (await anon.PostAsync($"{PropertiesRoute}/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Authenticated as an Investor (non-admin) -> 403.
        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);
        (await investor.PostAsync($"{PropertiesRoute}/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<string> CreatePropertyAsync(HttpClient adminClient)
    {
        var create = await adminClient.PostAsJsonAsync(PropertiesRoute, new
        {
            name = "Bishkek Central",
            description = "desc",
            address = "Erkindik 12",
            totalValue = 1_000_000m,
            tokenPrice = 1_000m,
            totalTokens = 1_000L,
            currency = "KGS",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        return doc.RootElement.GetString()!; // body is the new Guid
    }

    private static async Task<List<string>> ListPropertyIdsAsync(HttpClient client)
    {
        var response = await client.GetAsync(PropertiesRoute);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.EnumerateArray().Select(p => p.GetProperty("id").GetString()!).ToList();
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ReadTokenAsync(login));
    }

    private static async Task AuthenticateInvestorAsync(HttpClient client)
    {
        var phone = UniqueKgPhone();
        await client.PostAsJsonAsync(RequestOtpRoute, new { phone });
        var verify = await client.PostAsJsonAsync(VerifyOtpRoute, new { phone, code = DevCode });
        verify.IsSuccessStatusCode.Should().BeTrue();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ReadTokenAsync(verify));
    }

    private static async Task<string?> ReadTokenAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString();
    }

    private static string UniqueKgPhone()
    {
        var digits = new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).ToArray());
        var eight = (digits + "00000000")[..8];
        return $"+9967{eight}";
    }
}
