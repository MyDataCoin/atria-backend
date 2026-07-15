using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Atria.Domain.Deals;
using Atria.Domain.Realtors;
using Atria.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Exercises GET /api/v1/realtors/stats end to end: it returns a per-realtor row with closed
/// (Successful) and total deal counts, is role-gated (Admin/Compliance only — an investor is
/// forbidden), and requires a token. The in-memory store is shared across tests, so this test
/// seeds realtors with unique ids and asserts only on its own rows.
/// </summary>
public sealed class RealtorStatsFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string StatsRoute = "/api/v1/realtors/stats";
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";
    private const string RequestOtpRoute = "/api/v1/auth/register/phone/request-otp";
    private const string VerifyOtpRoute = "/api/v1/auth/register/phone/verify-otp";
    private const string DevCode = "333333";

    private readonly AtriaApiFactory _factory;

    public RealtorStatsFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Returns_closed_and_total_deal_counts_per_realtor()
    {
        // A realtor with 2 successful + 1 rejected + 1 pending = 2 closed, 4 total.
        var realtorId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        await SeedAsync(db =>
        {
            db.RealtorProfiles.Add(RealtorProfile.Create(
                realtorId, fullName: "Закир Ыдырысов", companyName: "ATRIA Realty"));
            db.Deals.AddRange(
                SuccessfulDeal(realtorId, propertyId),
                SuccessfulDeal(realtorId, propertyId),
                RejectedDeal(realtorId, propertyId),
                Deal.Create(realtorId, propertyId, 5m, DateTime.UtcNow)); // pending
        });

        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var response = await admin.GetAsync(StatsRoute);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = doc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == realtorId.ToString());

        row.GetProperty("fullName").GetString().Should().Be("Закир Ыдырысов");
        row.GetProperty("companyName").GetString().Should().Be("ATRIA Realty");
        row.GetProperty("closedDeals").GetInt32().Should().Be(2);
        row.GetProperty("totalDeals").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task Includes_realtors_with_no_deals_at_zero()
    {
        var realtorId = Guid.NewGuid();
        await SeedAsync(db => db.RealtorProfiles.Add(
            RealtorProfile.Create(realtorId, fullName: "Новичок Без Сделок")));

        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        using var doc = JsonDocument.Parse(await (await admin.GetAsync(StatsRoute)).Content.ReadAsStringAsync());
        var row = doc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == realtorId.ToString());

        row.GetProperty("closedDeals").GetInt32().Should().Be(0);
        row.GetProperty("totalDeals").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Is_forbidden_for_an_investor()
    {
        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);

        (await investor.GetAsync(StatsRoute)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Is_unauthorized_without_a_token()
    {
        (await _factory.CreateClient().GetAsync(StatsRoute))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static Deal SuccessfulDeal(Guid realtorId, Guid propertyId)
    {
        var deal = Deal.Create(realtorId, propertyId, 5m, DateTime.UtcNow);
        deal.MarkSuccessful(Guid.NewGuid());
        return deal;
    }

    private static Deal RejectedDeal(Guid realtorId, Guid propertyId)
    {
        var deal = Deal.Create(realtorId, propertyId, 5m, DateTime.UtcNow);
        deal.Reject();
        return deal;
    }

    private async Task SeedAsync(Action<AtriaDbContext> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtriaDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    private static async Task AuthenticateAdminAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(AdminLoginRoute, new { username = "admin", password = "admin-test-password" });
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
