using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Exercises the realtor role end to end through the real pipeline + EF in-memory store: static
/// realtor login issues a Realtor token; a realtor creates a referral deal against an OPEN property
/// (getting a pending deal + shareable link); the dashboard exposes the investor headline count and
/// the full property catalogue; the referral link resolves publicly; and the deal endpoints are
/// role-gated (investors are forbidden). Also checks the by-token resolution never leaks commission.
/// </summary>
public sealed class RealtorDealFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string PropertiesRoute = "/api/v1/properties";
    private const string DealsRoute = "/api/v1/deals";
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";
    private const string RealtorLoginRoute = "/api/v1/auth/realtor/login";
    private const string RequestOtpRoute = "/api/v1/auth/register/phone/request-otp";
    private const string VerifyOtpRoute = "/api/v1/auth/register/phone/verify-otp";
    private const string DevCode = "333333";

    private readonly AtriaApiFactory _factory;

    public RealtorDealFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Realtor_CreatesDeal_ForOpenProperty_AndReferralResolvesPublicly()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var propertyId = await CreateOpenPropertyAsync(admin);

        var realtor = _factory.CreateClient();
        await AuthenticateRealtorAsync(realtor);

        // Create a deal (pending, with a link that lives 14 days and a shareable URL).
        var create = await realtor.PostAsJsonAsync(DealsRoute, new { propertyId, commissionPercent = 5m });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        using var dealDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var deal = dealDoc.RootElement;
        deal.GetProperty("status").GetString().Should().Be("pending");
        deal.GetProperty("commissionPercent").GetDecimal().Should().Be(5m);
        var token = deal.GetProperty("referralToken").GetString()!;
        token.Should().NotBeNullOrWhiteSpace();
        deal.GetProperty("referralUrl").GetString().Should().Contain($"ref={token}");

        // The deal shows up in the realtor's own list.
        var mine = await realtor.GetAsync($"{DealsRoute}/me");
        mine.StatusCode.Should().Be(HttpStatusCode.OK);
        using var mineDoc = JsonDocument.Parse(await mine.Content.ReadAsStringAsync());
        mineDoc.RootElement.EnumerateArray()
            .Select(d => d.GetProperty("referralToken").GetString())
            .Should().Contain(token);

        // Public by-token resolution: exposes the property + redeemable flag, NEVER the commission.
        var anon = _factory.CreateClient();
        var resolve = await anon.GetAsync($"{DealsRoute}/by-token/{token}");
        resolve.StatusCode.Should().Be(HttpStatusCode.OK);
        using var resolveDoc = JsonDocument.Parse(await resolve.Content.ReadAsStringAsync());
        resolveDoc.RootElement.GetProperty("propertyId").GetString().Should().Be(propertyId);
        resolveDoc.RootElement.GetProperty("isRedeemable").GetBoolean().Should().BeTrue();
        resolveDoc.RootElement.TryGetProperty("commissionPercent", out _)
            .Should().BeFalse("the public resolution must never leak the realtor's commission");

        // Unknown token -> 404.
        (await anon.GetAsync($"{DealsRoute}/by-token/not-a-real-token"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreatingDeal_ForNonOpenProperty_Is404()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var draftId = await CreatePropertyAsync(admin); // stays draft

        var realtor = _factory.CreateClient();
        await AuthenticateRealtorAsync(realtor);

        (await realtor.PostAsJsonAsync(DealsRoute, new { propertyId = draftId, commissionPercent = 5m }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DealEndpoints_AreRealtorOnly()
    {
        var anon = _factory.CreateClient();
        (await anon.PostAsJsonAsync(DealsRoute, new { propertyId = System.Guid.NewGuid(), commissionPercent = 5m }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);
        (await investor.PostAsJsonAsync(DealsRoute, new { propertyId = System.Guid.NewGuid(), commissionPercent = 5m }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await investor.GetAsync($"{DealsRoute}/me")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await investor.GetAsync($"{DealsRoute}/investor-count")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dashboard_ExposesInvestorCount_AndFullPropertyCatalogue()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        // A draft property: realtors (like admins) must see it in their catalogue.
        var draftId = await CreatePropertyAsync(admin);

        // Register an investor so the count is at least one.
        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);

        var realtor = _factory.CreateClient();
        await AuthenticateRealtorAsync(realtor);

        var count = await realtor.GetAsync($"{DealsRoute}/investor-count");
        count.StatusCode.Should().Be(HttpStatusCode.OK);
        using var countDoc = JsonDocument.Parse(await count.Content.ReadAsStringAsync());
        countDoc.RootElement.GetInt32().Should().BeGreaterThanOrEqualTo(1);

        var props = await realtor.GetAsync($"{DealsRoute}/properties");
        props.StatusCode.Should().Be(HttpStatusCode.OK);
        using var propsDoc = JsonDocument.Parse(await props.Content.ReadAsStringAsync());
        propsDoc.RootElement.EnumerateArray()
            .Select(p => p.GetProperty("id").GetString())
            .Should().Contain(draftId, "the realtor home page shows every property, including drafts");
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
        return doc.RootElement.GetString()!;
    }

    private static async Task<string> CreateOpenPropertyAsync(HttpClient adminClient)
    {
        var id = await CreatePropertyAsync(adminClient);
        (await adminClient.PostAsync($"{PropertiesRoute}/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        return id;
    }

    private static async Task AuthenticateAdminAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(AdminLoginRoute, new { username = "admin", password = "admin-test-password" });
        login.IsSuccessStatusCode.Should().BeTrue("static admin login should be enabled in tests");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ReadTokenAsync(login));
    }

    private static async Task AuthenticateRealtorAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(RealtorLoginRoute, new { username = "realtor", password = "realtor-test-password" });
        login.IsSuccessStatusCode.Should().BeTrue("static realtor login should be enabled in tests");
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
        var digits = new string(System.Guid.NewGuid().ToString("N").Where(char.IsDigit).ToArray());
        var eight = (digits + "00000000")[..8];
        return $"+9967{eight}";
    }
}
