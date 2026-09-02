using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Covers the placement window over the API: scheduling it, extending it, and declaring a placement
/// unsubscribed. These are the two answers the management company chose for an offering that reaches
/// its date short of its target ("возврат и продление"), and which one runs is always a human
/// decision — nothing here happens automatically.
/// </summary>
public sealed class PlacementWindowFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string PropertiesRoute = "/api/v1/properties";
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";

    private static readonly DateTime Opens = new(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Closes = new(2027, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly AtriaApiFactory _factory;

    public PlacementWindowFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Scheduling_a_placement_exposes_the_window_and_the_target()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreateIssueAsync(admin);

        var scheduled = await admin.PostAsJsonAsync($"{PropertiesRoute}/{id}/placement", new
        {
            opensAtUtc = Opens,
            closesAtUtc = Closes,
            targetAmount = 750_000m,
        });
        scheduled.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var issue = await GetPropertyAsync(admin, id);
        issue.GetProperty("placementOpensAtUtc").GetDateTime().Should().Be(Opens);
        issue.GetProperty("placementClosesAtUtc").GetDateTime().Should().Be(Closes);
        issue.GetProperty("targetAmount").GetDecimal().Should().Be(750_000m);

        // Nothing has been placed, so the target is plainly not met and nothing was raised.
        issue.GetProperty("raisedAmount").GetDecimal().Should().Be(0m);
        issue.GetProperty("isTargetMet").GetBoolean().Should().BeFalse();
        issue.GetProperty("placementExtensionCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task An_inverted_window_is_refused()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreateIssueAsync(admin);

        var response = await admin.PostAsJsonAsync($"{PropertiesRoute}/{id}/placement", new
        {
            opensAtUtc = Closes,
            closesAtUtc = Opens,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Extending_moves_the_date_and_counts_the_extension()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreateIssueAsync(admin);

        await admin.PostAsJsonAsync($"{PropertiesRoute}/{id}/placement", new
        {
            opensAtUtc = Opens,
            closesAtUtc = Closes,
            targetAmount = 750_000m,
        });

        var extended = await admin.PostAsJsonAsync($"{PropertiesRoute}/{id}/placement/extend", new
        {
            newClosesAtUtc = Closes.AddMonths(2),
            reason = "целевая сумма не собрана, продлеваем",
        });
        extended.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var issue = await GetPropertyAsync(admin, id);
        issue.GetProperty("placementClosesAtUtc").GetDateTime().Should().Be(Closes.AddMonths(2));
        issue.GetProperty("placementExtensionCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Extending_backwards_is_a_conflict()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreateIssueAsync(admin);

        await admin.PostAsJsonAsync($"{PropertiesRoute}/{id}/placement", new
        {
            opensAtUtc = Opens,
            closesAtUtc = Closes,
        });

        var response = await admin.PostAsJsonAsync($"{PropertiesRoute}/{id}/placement/extend", new
        {
            newClosesAtUtc = Closes.AddDays(-1),
            reason = "назад во времени",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Extending_requires_a_reason()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreateIssueAsync(admin);

        await admin.PostAsJsonAsync($"{PropertiesRoute}/{id}/placement", new
        {
            opensAtUtc = Opens,
            closesAtUtc = Closes,
        });

        var response = await admin.PostAsJsonAsync($"{PropertiesRoute}/{id}/placement/extend", new
        {
            newClosesAtUtc = Closes.AddMonths(1),
            reason = "   ",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unsubscribed_placement_is_completed_not_invalidated()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreateIssueAsync(admin);

        (await admin.PostAsync($"{PropertiesRoute}/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var unwound = await admin.PostAsJsonAsync($"{PropertiesRoute}/{id}/placement/unsubscribed", new
        {
            reason = "целевая сумма не собрана к дате закрытия",
        });
        unwound.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var issue = await GetPropertyAsync(admin, id);
        issue.GetProperty("status").GetString().Should().Be("completed");
        issue.GetProperty("salesPaused").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Placement_endpoints_are_admin_only()
    {
        var anon = _factory.CreateClient();
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreateIssueAsync(admin);

        (await anon.PostAsJsonAsync($"{PropertiesRoute}/{id}/placement", new { targetAmount = 1m }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync($"{PropertiesRoute}/{id}/placement/unsubscribed", new { reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<string> CreateIssueAsync(HttpClient adminClient)
    {
        var create = await adminClient.PostAsJsonAsync(PropertiesRoute, new
        {
            name = "ЖК на Токомбаева",
            totalValue = 1_000_000m,
            tokenPrice = 1_000m,
            totalTokens = 1_000L,
            currency = "KGS",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        return doc.RootElement.GetString()!;
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
