using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Drives the investor support-ticket flow end to end through the real controller, mediator
/// pipeline and EF in-memory store: authenticate (phone OTP) -> open a ticket -> list -> fetch ->
/// reply -> close. Also pins the shared wire contract the dashboards depend on (lowercase
/// <c>status</c> / <c>author</c>, embedded message thread, <c>...Utc</c> timestamps).
/// </summary>
public sealed class SupportTicketsFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string RequestOtpRoute = "/api/v1/auth/register/phone/request-otp";
    private const string VerifyOtpRoute = "/api/v1/auth/register/phone/verify-otp";
    private const string TicketsRoute = "/api/v1/support/tickets";
    private const string DevCode = "333333";

    private readonly AtriaApiFactory _factory;

    public SupportTicketsFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Investor_CanOpenListReplyAndCloseTicket()
    {
        var client = _factory.CreateClient();
        await AuthenticateInvestorAsync(client);

        // Open a ticket -> 201 with an "open" status and the seeded investor message.
        var create = await client.PostAsJsonAsync(TicketsRoute, new
        {
            subject = "KYC upload fails",
            category = "KYC",
            body = "The document upload keeps failing.",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var root = created.RootElement;
        var id = root.GetProperty("id").GetString();
        id.Should().NotBeNullOrWhiteSpace();
        root.GetProperty("status").GetString().Should().Be("open");
        root.GetProperty("category").GetString().Should().Be("KYC");
        root.GetProperty("createdAtUtc").GetString().Should().NotBeNullOrWhiteSpace();
        var firstMessage = root.GetProperty("messages").EnumerateArray().Single();
        firstMessage.GetProperty("author").GetString().Should().Be("investor");
        firstMessage.GetProperty("body").GetString().Should().Be("The document upload keeps failing.");

        // List -> the new ticket is present (scoped to this investor).
        var list = await client.GetAsync(TicketsRoute);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using var listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        listDoc.RootElement.EnumerateArray()
            .Select(t => t.GetProperty("id").GetString())
            .Should().Contain(id);

        // Fetch by id -> full thread.
        var detail = await client.GetAsync($"{TicketsRoute}/{id}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        using var detailDoc = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        detailDoc.RootElement.GetProperty("messages").GetArrayLength().Should().Be(1);

        // Investor reply -> 201 message, still "open".
        var reply = await client.PostAsJsonAsync($"{TicketsRoute}/{id}/messages", new { body = "Any update?" });
        reply.StatusCode.Should().Be(HttpStatusCode.Created);
        using var replyDoc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        replyDoc.RootElement.GetProperty("author").GetString().Should().Be("investor");

        // Close -> 204; a second close is a 409 conflict.
        var close = await client.PostAsync($"{TicketsRoute}/{id}/close", content: null);
        close.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var closeAgain = await client.PostAsync($"{TicketsRoute}/{id}/close", content: null);
        closeAgain.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Replying to a closed ticket is rejected.
        var replyClosed = await client.PostAsJsonAsync($"{TicketsRoute}/{id}/messages", new { body = "reopen?" });
        replyClosed.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Tickets_RequireAuthentication()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(TicketsRoute);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task AuthenticateInvestorAsync(HttpClient client)
    {
        var phone = UniqueKgPhone();
        await client.PostAsJsonAsync(RequestOtpRoute, new { phone });
        var verify = await client.PostAsJsonAsync(VerifyOtpRoute, new { phone, code = DevCode });
        verify.IsSuccessStatusCode.Should().BeTrue("phone OTP verification should authenticate the investor");

        using var document = JsonDocument.Parse(await verify.Content.ReadAsStringAsync());
        var accessToken = document.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private static string UniqueKgPhone()
    {
        var digits = new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).ToArray());
        var eight = (digits + "00000000")[..8];
        return $"+9967{eight}";
    }
}
