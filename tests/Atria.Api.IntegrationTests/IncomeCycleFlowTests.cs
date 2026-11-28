using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Atria.Domain.Factories;
using Atria.Domain.Holders;
using Atria.Domain.Kyc;
using Atria.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// The whole income cycle over real HTTP, with every step taken by the role that takes it in the
/// panels: the accountant reports what the object earned, the admin confirms it, cuts the register
/// and computes the dividend, the super admin gives the second approval, the admin records the
/// payment — and the investor then sees the money in their cabinet and on their tax statement draft.
/// <para>
/// The purchase itself (KYC, reservation, payment, mint) is a different flow and is seeded here; what
/// this test is about is that income declared by the company reaches the right holder, in the right
/// amount, past both people who have to agree to it.
/// </para>
/// </summary>
public sealed class IncomeCycleFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";
    private const string RequestOtpRoute = "/api/v1/auth/register/phone/request-otp";
    private const string VerifyOtpRoute = "/api/v1/auth/register/phone/verify-otp";

    private readonly AtriaApiFactory _factory;

    public IncomeCycleFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Income_reported_by_the_accountant_reaches_the_holder_past_two_approvals()
    {
        var accountant = await StaffAsync(AtriaApiFactory.AccountantUsername, AtriaApiFactory.AccountantPassword);
        var admin = await StaffAsync(AtriaApiFactory.AdminUsername, AtriaApiFactory.AdminPassword);
        var superAdmin = await StaffAsync(AtriaApiFactory.SuperAdminUsername, AtriaApiFactory.SuperAdminPassword);
        var lawyer = await StaffAsync(AtriaApiFactory.LawyerUsername, AtriaApiFactory.LawyerPassword);

        // An issue of 1 000 shares, of which one investor holds 10 — one percent of the issue.
        var propertyId = await CreateIssueAsync(admin);
        var (investor, investorId) = await InvestorAsync();
        await SeedHoldingAsync(investorId, propertyId, tokens: 10, wallet: "0xAbC0000000000000000000000000000000000001");

        // 1. The accountant reports the period: 200 000 earned, 50 000 spent — 150 000 net.
        var report = await accountant.PostAsJsonAsync("/api/v1/operating-periods", new
        {
            propertyId,
            startUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            endUtc = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
            grossRevenue = 200_000m,
            operatingExpenses = 50_000m,
            note = "III квартал",
            lines = new[]
            {
                new { kind = "revenue", label = "Аренда", amount = 200_000m },
                new { kind = "expense", label = "Эксплуатация", amount = 50_000m },
            },
        });
        report.StatusCode.Should().Be(HttpStatusCode.Created, await report.Content.ReadAsStringAsync());
        var periodId = await ReadGuidAsync(report);

        // The accountant cannot confirm their own figures; the admin can.
        (await accountant.PostAsync($"/api/v1/operating-periods/{periodId}/confirm", null))
            .IsSuccessStatusCode.Should().BeFalse("the reporter may not confirm their own figures");
        (await admin.PostAsync($"/api/v1/operating-periods/{periodId}/confirm", null))
            .IsSuccessStatusCode.Should().BeTrue();

        // 2. The admin cuts the register for a payout. The holder is on it, allowlisted.
        var cut = await admin.PostAsJsonAsync("/api/v1/holders/snapshots", new
        {
            propertyId,
            purpose = "Payout",
            snapshotAtUtc = DateTime.UtcNow.AddMinutes(-1),
        });
        cut.StatusCode.Should().Be(HttpStatusCode.Created, await cut.Content.ReadAsStringAsync());
        var snapshotId = await ReadGuidAsync(cut);

        // 3. Declaring the whole net income would hand the issuer's 99% to the one holder — refused.
        var tooMuch = await admin.PostAsJsonAsync("/api/v1/payouts", new
        {
            snapshotId, kind = "Dividend", method = "BankTransfer", declaredAmount = 150_000m,
            currency = "KGS", note = "Дивиденды за III квартал", operatingPeriodId = periodId,
        });
        tooMuch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await tooMuch.Content.ReadAsStringAsync()).Should().Contain("500,00 KGS");

        // Their one percent — 1 500 — is accepted.
        var created = await admin.PostAsJsonAsync("/api/v1/payouts", new
        {
            snapshotId, kind = "Dividend", method = "BankTransfer", declaredAmount = 1_500m,
            currency = "KGS", note = "Дивиденды за III квартал", operatingPeriodId = periodId,
        });
        created.IsSuccessStatusCode.Should().BeTrue(await created.Content.ReadAsStringAsync());
        var runId = await ReadGuidAsync(created);

        // Nothing can be paid on a draft.
        var itemId = await SingleItemIdAsync(admin, runId);
        (await admin.PostAsJsonAsync($"/api/v1/payouts/{runId}/items/{itemId}/settle", new { settlementReference = "ДЕМО-1" }))
            .IsSuccessStatusCode.Should().BeFalse("a distribution is not open for payment before the second approval");

        // 4. Two people: the admin asks, cannot approve their own request; the super admin approves.
        var request = await admin.PostAsJsonAsync("/api/v1/governance/critical-actions", new
        {
            kind = "PayoutRun", targetId = runId, reason = "Дивиденды за III квартал",
        });
        request.IsSuccessStatusCode.Should().BeTrue(await request.Content.ReadAsStringAsync());
        var actionId = await ReadGuidAsync(request);

        (await admin.PostAsync($"/api/v1/governance/critical-actions/{actionId}/approve", null))
            .IsSuccessStatusCode.Should().BeFalse("nobody approves their own request");
        var approved = await superAdmin.PostAsync($"/api/v1/governance/critical-actions/{actionId}/approve", null);
        approved.IsSuccessStatusCode.Should().BeTrue(await approved.Content.ReadAsStringAsync());

        // 5. The admin records the payment.
        var settled = await admin.PostAsJsonAsync(
            $"/api/v1/payouts/{runId}/items/{itemId}/settle", new { settlementReference = "ДЕМО-1" });
        settled.IsSuccessStatusCode.Should().BeTrue(await settled.Content.ReadAsStringAsync());

        // The admin's list (the overview card reads it) and the lawyer's read-only view agree.
        foreach (var reader in new[] { admin, lawyer })
        {
            var runs = await reader.GetAsync($"/api/v1/payouts?propertyId={propertyId}");
            runs.StatusCode.Should().Be(HttpStatusCode.OK);
            using var runsDoc = JsonDocument.Parse(await runs.Content.ReadAsStringAsync());
            var run = runsDoc.RootElement.EnumerateArray().Single();
            run.GetProperty("kind").GetString().Should().Be("Dividend");
            run.GetProperty("paidAmount").GetDecimal().Should().Be(1_500m);
        }

        // The period is now spent: it cannot fund a second dividend.
        var period = await admin.GetAsync($"/api/v1/operating-periods/{periodId}");
        using (var periodDoc = JsonDocument.Parse(await period.Content.ReadAsStringAsync()))
            periodDoc.RootElement.GetProperty("status").GetString().Should().BeEquivalentTo("Distributed");

        // 6. The investor sees the money — and only theirs.
        var mine = await investor.GetAsync("/api/v1/payouts/me");
        mine.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var mineDoc = JsonDocument.Parse(await mine.Content.ReadAsStringAsync()))
        {
            var line = mineDoc.RootElement.EnumerateArray().Single();
            line.GetProperty("amount").GetDecimal().Should().Be(1_500m);
            line.GetProperty("status").GetString().Should().Be("Paid");
            line.GetProperty("settlementReference").GetString().Should().Be("ДЕМО-1");
            line.GetProperty("paidAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        }

        // ...and on the draft of this year's tax statement, with the property named.
        var draft = await investor.GetAsync($"/api/v1/tax-statements/draft/{DateTime.UtcNow.Year}/document");
        draft.StatusCode.Should().Be(HttpStatusCode.OK, await draft.Content.ReadAsStringAsync());
        var html = await draft.Content.ReadAsStringAsync();
        // Russian number formatting: a non-breaking space between thousands.
        html.Should().Contain("ЧЕРНОВИК").And.Contain("Демо-объект").And.Contain("1 500,00");
    }

    private async Task<HttpClient> StaffAsync(string username, string password)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(AdminLoginRoute, new { username, password });
        login.IsSuccessStatusCode.Should().BeTrue($"{username} is seeded for tests");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ReadTokenAsync(login));
        return client;
    }

    private async Task<(HttpClient Client, Guid UserId)> InvestorAsync()
    {
        var client = _factory.CreateClient();
        var phone = $"+996{Random.Shared.NextInt64(500_000_000, 999_999_999)}";
        await client.PostAsJsonAsync(RequestOtpRoute, new { phone });
        var verify = await client.PostAsJsonAsync(VerifyOtpRoute, new { phone, code = _factory.Sms.CodeFor(phone) });
        verify.IsSuccessStatusCode.Should().BeTrue();
        var token = await ReadTokenAsync(verify);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var sub = jwt.Claims.First(c => c.Type is "sub" or "nameid"
            || c.Type == System.Security.Claims.ClaimTypes.NameIdentifier).Value;
        return (client, Guid.Parse(sub));
    }

    // What a completed purchase leaves behind: verified identity, an active placement, and the
    // shares in the register under the investor's wallet.
    private async Task SeedHoldingAsync(Guid investorId, string propertyId, long tokens, string wallet)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtriaDbContext>();
        var property = Guid.Parse(propertyId);
        var activatedAt = DateTime.UtcNow.AddDays(-30);

        var kyc = KycProfile.Create(investorId);
        kyc.Submit(KycProviderType.Manual, "session", null, null, "Демо Инвестор", null, null);
        kyc.Approve();
        db.KycProfiles.Add(kyc);

        var investment = InvestmentFactory.CreateForInvestor(
            investorId, property, tokens, "KGS", pricePerToken: 1_000m, reservedUntilUtc: activatedAt.AddDays(3));
        investment.SetTokenTarget(wallet, "0xcontract");
        investment.Approve(activatedAt);
        db.Investments.Add(investment);

        db.HolderPositions.Add(HolderPosition.Create(
            property, wallet, tokens, investorId, isAllowlisted: true, HolderSource.OurRecords, activatedAt));

        await db.SaveChangesAsync();
    }

    private static async Task<string> CreateIssueAsync(HttpClient admin)
    {
        var create = await admin.PostAsJsonAsync("/api/v1/properties", new
        {
            name = "Демо-объект",
            totalValue = 1_000_000m,
            tokenPrice = 1_000m,
            totalTokens = 1_000L,
            currency = "KGS",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        return doc.RootElement.GetString()!;
    }

    private static async Task<string> SingleItemIdAsync(HttpClient admin, Guid runId)
    {
        var detail = await admin.GetAsync($"/api/v1/payouts/{runId}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().ContainSingle("one holder is on the register");
        items[0].GetProperty("amount").GetDecimal().Should().Be(1_500m);
        return items[0].GetProperty("id").GetString()!;
    }

    private static async Task<Guid> ReadGuidAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.ValueKind == JsonValueKind.String
            ? doc.RootElement.GetGuid()
            : doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string> ReadTokenAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString()!;
    }
}
