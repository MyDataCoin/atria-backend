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

        // Anonymous GET /properties exposes a lowercase status and no isActive field.
        var anon = _factory.CreateClient();
        var draft = await GetPropertyAsync(anon, id);
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
