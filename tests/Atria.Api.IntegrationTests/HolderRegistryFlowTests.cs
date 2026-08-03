using System;
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
/// Covers the operator-facing reads the admin screens are built on: the application queue that
/// crosses investor boundaries, and the holder register with its frozen snapshots and CSV export.
/// Both are Admin-only — an investor must not reach either.
/// </summary>
public sealed class HolderRegistryFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string PropertiesRoute = "/api/v1/properties";
    private const string InvestmentsRoute = "/api/v1/investments";
    private const string HoldersRoute = "/api/v1/holders";
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";
    private const string RequestOtpRoute = "/api/v1/auth/register/phone/request-otp";
    private const string VerifyOtpRoute = "/api/v1/auth/register/phone/verify-otp";

    private readonly AtriaApiFactory _factory;

    public HolderRegistryFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Operator_queue_is_readable_by_admins_and_closed_to_investors()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var queue = await admin.GetAsync($"{InvestmentsRoute}?status=Reserved&take=50");
        queue.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await queue.Content.ReadAsStringAsync());
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);
        (await investor.GetAsync(InvestmentsRoute))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "the queue is the one read that crosses investors");
    }

    [Fact]
    public async Task Registry_and_snapshots_are_admin_only()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var propertyId = await CreatePropertyAsync(admin);

        (await admin.GetAsync($"{HoldersRoute}?propertyId={propertyId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);
        (await investor.GetAsync($"{HoldersRoute}?propertyId={propertyId}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var anon = _factory.CreateClient();
        (await anon.GetAsync($"{HoldersRoute}?propertyId={propertyId}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Registry_of_an_unknown_issue_is_a_not_found()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        (await admin.GetAsync($"{HoldersRoute}?propertyId={Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_snapshot_is_taken_listed_opened_and_exported()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var propertyId = await CreatePropertyAsync(admin);
        var cut = DateTime.UtcNow.AddMinutes(-5);

        var created = await admin.PostAsJsonAsync($"{HoldersRoute}/snapshots", new
        {
            propertyId,
            purpose = "Reporting",
            snapshotAtUtc = cut,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var snapshotId = createdDoc.RootElement.GetGuid();

        // Asking again for the same cut returns the snapshot already taken, not a second one.
        var again = await admin.PostAsJsonAsync($"{HoldersRoute}/snapshots", new
        {
            propertyId,
            purpose = "Reporting",
            snapshotAtUtc = cut,
        });
        using var againDoc = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
        againDoc.RootElement.GetGuid().Should().Be(snapshotId);

        // A different purpose on the same cut is a separate, independently auditable snapshot.
        var payout = await admin.PostAsJsonAsync($"{HoldersRoute}/snapshots", new
        {
            propertyId,
            purpose = "Payout",
            snapshotAtUtc = cut,
        });
        using var payoutDoc = JsonDocument.Parse(await payout.Content.ReadAsStringAsync());
        payoutDoc.RootElement.GetGuid().Should().NotBe(snapshotId);

        var list = await admin.GetAsync($"{HoldersRoute}/snapshots?propertyId={propertyId}");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using var listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        listDoc.RootElement.EnumerateArray()
            .Select(s => s.GetProperty("id").GetGuid())
            .Should().Contain(snapshotId);

        var detail = await admin.GetAsync($"{HoldersRoute}/snapshots/{snapshotId}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        using var detailDoc = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        detailDoc.RootElement.GetProperty("snapshot").GetProperty("purpose").GetString().Should().Be("Reporting");
        detailDoc.RootElement.GetProperty("rows").ValueKind.Should().Be(JsonValueKind.Array);

        var export = await admin.GetAsync($"{HoldersRoute}/snapshots/{snapshotId}/export");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        export.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var csv = await export.Content.ReadAsStringAsync();
        csv.Should().Contain("wallet_address,token_count,share,investor_id");

        // Exporting the same snapshot twice hands over identical bytes.
        var again2 = await admin.GetAsync($"{HoldersRoute}/snapshots/{snapshotId}/export");
        (await again2.Content.ReadAsStringAsync()).Should().Be(csv);
    }

    [Fact]
    public async Task A_snapshot_cannot_be_cut_in_the_future()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var propertyId = await CreatePropertyAsync(admin);

        var created = await admin.PostAsJsonAsync($"{HoldersRoute}/snapshots", new
        {
            propertyId,
            purpose = "Payout",
            snapshotAtUtc = DateTime.UtcNow.AddHours(1),
        });

        created.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<string> CreatePropertyAsync(HttpClient adminClient)
    {
        var create = await adminClient.PostAsJsonAsync(PropertiesRoute, new
        {
            name = "Registry Tower",
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

    private static async Task AuthenticateAdminAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(AdminLoginRoute, new
        {
            username = AtriaApiFactory.AdminUsername,
            password = AtriaApiFactory.AdminPassword,
        });
        login.IsSuccessStatusCode.Should().BeTrue();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ReadTokenAsync(login));
    }

    private async Task AuthenticateInvestorAsync(HttpClient client)
    {
        var phone = $"+996{Random.Shared.NextInt64(500_000_000, 999_999_999)}";
        await client.PostAsJsonAsync(RequestOtpRoute, new { phone });
        var verify = await client.PostAsJsonAsync(VerifyOtpRoute, new { phone, code = _factory.Sms.CodeFor(phone) });
        verify.IsSuccessStatusCode.Should().BeTrue();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ReadTokenAsync(verify));
    }

    private static async Task<string?> ReadTokenAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString();
    }
}
