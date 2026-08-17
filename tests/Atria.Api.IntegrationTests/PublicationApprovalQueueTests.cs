using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Pins the contract the admin panel drives publication through. Publishing is deliberately not a
/// single call: <c>POST /properties/{id}/publish</c> only raises an approval request, and the
/// offering opens when a SECOND administrator approves it. The panel reads the queue to show what
/// is waiting, so the shape of that queue is part of the contract, not an internal detail.
/// </summary>
public sealed class PublicationApprovalQueueTests : IClassFixture<AtriaApiFactory>
{
    private const string PropertiesRoute = "/api/v1/properties";
    private const string PendingRoute = "/api/v1/governance/critical-actions/pending";
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";

    private readonly AtriaApiFactory _factory;

    public PublicationApprovalQueueTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Publishing_queues_an_approval_and_leaves_the_property_where_it_was()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreatePropertyAsync(admin);

        var response = await admin.PostAsync($"{PropertiesRoute}/{id}/publish", null);

        // 202, not 204: nothing was published, a request was accepted for approval. The panel keys
        // its "waiting for a second administrator" message off exactly this.
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var actionId = created.RootElement.GetGuid();
        actionId.Should().NotBeEmpty("the request id is what the panel tracks");

        (await GetPropertyAsync(admin, id)).GetProperty("status").GetString()
            .Should().Be("draft", "a request on its own must never open an offering");
    }

    [Fact]
    public async Task The_pending_queue_exposes_the_request_the_panel_renders()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreatePropertyAsync(admin);
        await admin.PostAsync($"{PropertiesRoute}/{id}/publish", null);

        var pending = await GetJsonAsync(admin, PendingRoute);
        var mine = pending.EnumerateArray()
            .Where(a => a.GetProperty("targetId").GetGuid().ToString() == id)
            .ToList();

        mine.Should().ContainSingle("the panel lists one row per waiting request");
        var action = mine[0];

        // Field names and the enum spelling the panel filters and renders on.
        action.GetProperty("kind").GetString().Should().Be("IssuePublication");
        action.GetProperty("status").GetString().Should().Be("Pending");
        action.GetProperty("id").GetGuid().Should().NotBeEmpty();
        action.GetProperty("requestedByUserId").GetGuid().Should().NotBeEmpty(
            "the panel compares this with the signed-in user to hide approve on your own request");
        action.GetProperty("requestedAtUtc").GetDateTime().Should().BeBefore(DateTime.UtcNow.AddMinutes(1));
        action.GetProperty("expiresAtUtc").GetDateTime().Should().BeAfter(DateTime.UtcNow);
        action.GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_requester_cannot_approve_their_own_request()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreatePropertyAsync(admin);

        var actionId = await GovernanceTestHelpers.RequestPublishAsync(admin, id);

        var selfApproval = await admin.PostAsync(
            $"/api/v1/governance/critical-actions/{actionId}/approve", null);

        selfApproval.IsSuccessStatusCode.Should().BeFalse(
            "four eyes means two people; the panel says so instead of offering a button that fails");
        (await GetPropertyAsync(admin, id)).GetProperty("status").GetString().Should().Be("draft");
    }

    [Fact]
    public async Task A_second_administrator_approving_opens_the_offering_and_clears_the_queue()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await CreatePropertyAsync(admin);

        var actionId = await GovernanceTestHelpers.RequestPublishAsync(admin, id);
        var approver = await GovernanceTestHelpers.ApproverClientAsync(_factory);
        await GovernanceTestHelpers.ApproveAsync(approver, actionId);

        (await GetPropertyAsync(admin, id)).GetProperty("status").GetString().Should().Be("open");

        var pending = await GetJsonAsync(admin, PendingRoute);
        pending.EnumerateArray()
            .Should().NotContain(a => a.GetProperty("targetId").GetGuid().ToString() == id,
                "an approved request leaves the queue the panel renders");
    }

    private static async Task<string> CreatePropertyAsync(HttpClient adminClient)
    {
        var create = await adminClient.PostAsJsonAsync(PropertiesRoute, new
        {
            name = "Borsan Residence, кв. 1",
            totalValue = 83_447.5m,
            tokenPrice = 1_450m,
            totalTokens = 57.55m,
            currency = "KGS",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        return doc.RootElement.GetString()!;
    }

    private static async Task<JsonElement> GetPropertyAsync(HttpClient client, string id)
        => await GetJsonAsync(client, $"{PropertiesRoute}/{id}");

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
