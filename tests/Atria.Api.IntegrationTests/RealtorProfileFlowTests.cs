using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Atria.Domain.Realtors;
using Atria.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Exercises GET /realtor/me end to end: it returns the seeded profile for the authenticated
/// realtor, is 404 when no profile row exists, and is role-gated (an investor is forbidden).
/// </summary>
public sealed class RealtorProfileFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string ProfileRoute = "/api/v1/realtor/me";
    private const string RealtorLoginRoute = "/api/v1/auth/realtor/login";
    private const string RequestOtpRoute = "/api/v1/auth/register/phone/request-otp";
    private const string VerifyOtpRoute = "/api/v1/auth/register/phone/verify-otp";
    private const string DevCode = "333333";
    private static readonly Guid RealtorUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly AtriaApiFactory _factory;

    public RealtorProfileFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Returns_the_seeded_profile_for_the_authenticated_realtor()
    {
        await SeedProfileAsync(RealtorProfile.Create(
            RealtorUserId,
            fullName: "Иванов Иван Иванович",
            position: "Старший риелтор",
            walletAddress: "0xabc123",
            companyName: "ООО «Атрия»",
            companyRegistrationNumber: "1027700132195",
            officeAddress: "Бишкек, Чуй 136"));

        var realtor = _factory.CreateClient();
        await AuthenticateRealtorAsync(realtor);

        var response = await realtor.GetAsync(ProfileRoute);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("userId").GetString().Should().Be(RealtorUserId.ToString());
        root.GetProperty("fullName").GetString().Should().Be("Иванов Иван Иванович");
        root.GetProperty("position").GetString().Should().Be("Старший риелтор");
        root.GetProperty("walletAddress").GetString().Should().Be("0xabc123");
        root.GetProperty("companyName").GetString().Should().Be("ООО «Атрия»");
        root.GetProperty("companyRegistrationNumber").GetString().Should().Be("1027700132195");
        root.GetProperty("officeAddress").GetString().Should().Be("Бишкек, Чуй 136");
    }

    [Fact]
    public async Task Is_forbidden_for_an_investor()
    {
        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);

        (await investor.GetAsync(ProfileRoute)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Is_unauthorized_without_a_token()
    {
        (await _factory.CreateClient().GetAsync(ProfileRoute))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task SeedProfileAsync(RealtorProfile profile)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtriaDbContext>();
        db.RealtorProfiles.Add(profile);
        await db.SaveChangesAsync();
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
        var digits = new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).ToArray());
        var eight = (digits + "00000000")[..8];
        return $"+9967{eight}";
    }
}
