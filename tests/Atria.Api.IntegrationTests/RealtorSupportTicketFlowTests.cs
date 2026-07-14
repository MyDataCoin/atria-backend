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
/// Drives the realtor help-desk flow end to end: a realtor opens a support ticket (it lands in the
/// shared admin desk), the admin sees it tagged with the author's role <c>realtor</c> and replies,
/// and the reply is visible to the realtor on their own ticket. Also pins the role gate — a realtor
/// only sees their own tickets, never another author's.
/// </summary>
public sealed class RealtorSupportTicketFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string TicketsRoute = "/api/v1/support/tickets";
    private const string RealtorLoginRoute = "/api/v1/auth/realtor/login";
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";
    private const string RequestOtpRoute = "/api/v1/auth/register/phone/request-otp";
    private const string VerifyOtpRoute = "/api/v1/auth/register/phone/verify-otp";
    private const string DevCode = "333333";
    private const string RealtorUserId = "22222222-2222-2222-2222-222222222222";

    private readonly AtriaApiFactory _factory;

    public RealtorSupportTicketFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Realtor_OpensTicket_AdminSeesRoleAndReplies_RealtorSeesReply()
    {
        var realtor = _factory.CreateClient();
        await AuthenticateRealtorAsync(realtor);

        // Realtor opens a ticket -> 201, open, seeded with the first client message.
        var create = await realtor.PostAsJsonAsync(TicketsRoute, new
        {
            subject = "Referral link not working",
            category = "Deals",
            body = "My referral link returns 404.",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var ticketId = createdDoc.RootElement.GetProperty("id").GetString()!;
        createdDoc.RootElement.GetProperty("status").GetString().Should().Be("open");

        // Realtor sees it in their own list.
        var mine = await realtor.GetAsync(TicketsRoute);
        mine.StatusCode.Should().Be(HttpStatusCode.OK);
        using var mineDoc = JsonDocument.Parse(await mine.Content.ReadAsStringAsync());
        mineDoc.RootElement.EnumerateArray().Select(t => t.GetProperty("id").GetString())
            .Should().Contain(ticketId);

        // Admin sees the same ticket in the shared desk, tagged with the author's role = realtor.
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var adminView = await admin.GetAsync($"{TicketsRoute}/{ticketId}");
        adminView.StatusCode.Should().Be(HttpStatusCode.OK);
        using var adminDoc = JsonDocument.Parse(await adminView.Content.ReadAsStringAsync());
        var investorBlock = adminDoc.RootElement.GetProperty("investor");
        investorBlock.GetProperty("id").GetString().Should().Be(RealtorUserId);
        investorBlock.GetProperty("role").GetString().Should().Be("realtor");
        // A realtor has no KYC name, so the admin panel shows the fixed realtor label.
        investorBlock.GetProperty("fullName").GetString().Should().Be("Риелтор");

        // The realtor's own message in the admin thread is labelled with that same name.
        var clientMsg = adminDoc.RootElement.GetProperty("messages").EnumerateArray()
            .First(m => m.GetProperty("author").GetString() == "investor");
        clientMsg.GetProperty("authorName").GetString().Should().Be("Риелтор");

        // Admin replies -> the reply is authored as support and moves the ticket to pending.
        var reply = await admin.PostAsJsonAsync($"{TicketsRoute}/{ticketId}/messages",
            new { body = "Fixed on our side, please retry." });
        reply.StatusCode.Should().Be(HttpStatusCode.Created);
        using var replyDoc = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        replyDoc.RootElement.GetProperty("author").GetString().Should().Be("support");

        // Realtor sees the admin's reply on their own ticket.
        var afterReply = await realtor.GetAsync($"{TicketsRoute}/{ticketId}");
        afterReply.StatusCode.Should().Be(HttpStatusCode.OK);
        using var afterDoc = JsonDocument.Parse(await afterReply.Content.ReadAsStringAsync());
        afterDoc.RootElement.GetProperty("status").GetString().Should().Be("pending");
        var authors = afterDoc.RootElement.GetProperty("messages").EnumerateArray()
            .Select(m => m.GetProperty("author").GetString()).ToList();
        authors.Should().Contain("investor").And.Contain("support");
    }

    [Fact]
    public async Task Realtor_CannotSeeAnotherAuthorsTicket()
    {
        // An investor opens a ticket.
        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);
        var create = await investor.PostAsJsonAsync(TicketsRoute, new
        {
            subject = "Payment stuck", category = "Payments", body = "Card declined.",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var investorTicketId = createdDoc.RootElement.GetProperty("id").GetString()!;

        // The realtor must not be able to read it (reported as not found, existence not leaked).
        var realtor = _factory.CreateClient();
        await AuthenticateRealtorAsync(realtor);
        (await realtor.GetAsync($"{TicketsRoute}/{investorTicketId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // ...and it never shows up in the realtor's own list.
        var mine = await realtor.GetAsync(TicketsRoute);
        using var mineDoc = JsonDocument.Parse(await mine.Content.ReadAsStringAsync());
        mineDoc.RootElement.EnumerateArray().Select(t => t.GetProperty("id").GetString())
            .Should().NotContain(investorTicketId);
    }

    private static async Task AuthenticateRealtorAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(RealtorLoginRoute, new { username = "realtor", password = "realtor-test-password" });
        login.IsSuccessStatusCode.Should().BeTrue("static realtor login should be enabled in tests");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ReadTokenAsync(login));
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
        var digits = new string(System.Guid.NewGuid().ToString("N").Where(char.IsDigit).ToArray());
        var eight = (digits + "00000000")[..8];
        return $"+9967{eight}";
    }
}
