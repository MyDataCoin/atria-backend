using System;
using System.Collections.Generic;
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
/// Proves the audit journal is written by the SERVER, inside the commands themselves: each of the
/// seven audited actions lands in GET /audit with an actor, a ready-to-render summary and a
/// severity — including a ticket opened by an investor (the actor is not always staff). Also covers
/// paging, the severity filter, and the Admin/Compliance gate.
/// </summary>
public sealed class AuditJournalFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string AuditRoute = "/api/v1/audit";
    private const string PropertiesRoute = "/api/v1/properties";
    private const string PublicationsRoute = "/api/v1/publications";
    private const string TicketsRoute = "/api/v1/support/tickets";
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";
    private const string RequestOtpRoute = "/api/v1/auth/register/phone/request-otp";
    private const string VerifyOtpRoute = "/api/v1/auth/register/phone/verify-otp";
    private const string DevCode = "333333";

    private readonly AtriaApiFactory _factory;

    public AuditJournalFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Property_lifecycle_actions_are_audited_with_actor_summary_and_severity()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var propertyId = await CreatePropertyAsync(admin);

        // PATCH -> PropertyUpdated
        (await admin.PatchAsJsonAsync($"{PropertiesRoute}/{propertyId}", new { name = "Переименованный объект" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // announce -> PropertyAnnounced, publish -> PropertyPublished
        (await admin.PostAsync($"{PropertiesRoute}/{propertyId}/announce", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PostAsync($"{PropertiesRoute}/{propertyId}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var entries = await ReadJournalAsync(admin, $"{AuditRoute}?entityId={propertyId}&pageSize=100");

        entries.Select(e => e.GetProperty("eventType").GetString())
            .Should().Contain(new[] { "PropertyCreated", "PropertyUpdated", "PropertyAnnounced", "PropertyPublished" });

        var created = entries.First(e => e.GetProperty("eventType").GetString() == "PropertyCreated");
        created.GetProperty("entityType").GetString().Should().Be("Property");
        created.GetProperty("actorName").GetString().Should().Be("Администратор");
        created.GetProperty("severity").GetString().Should().Be("success");
        created.GetProperty("summary").GetString().Should().Contain("Создан объект");
        created.GetProperty("userId").ValueKind.Should().NotBe(JsonValueKind.Null, "the action is attributable");

        entries.First(e => e.GetProperty("eventType").GetString() == "PropertyPublished")
            .GetProperty("summary").GetString().Should().Contain("опубликован");
    }

    [Fact]
    public async Task Publishing_a_news_item_is_audited()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var create = await admin.PostAsJsonAsync(PublicationsRoute, new
        {
            type = "financial_report",
            title = "Квартальный отчёт Q2 2026",
            body = "Текст.",
            propertyId = (Guid?)null,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var publicationId = createdDoc.RootElement.GetProperty("id").GetString()!;

        var entries = await ReadJournalAsync(admin, $"{AuditRoute}?entityId={publicationId}");
        var entry = entries.Single(e => e.GetProperty("eventType").GetString() == "PublicationPublished");
        entry.GetProperty("entityType").GetString().Should().Be("Publication");
        entry.GetProperty("actorName").GetString().Should().Be("Администратор");
        entry.GetProperty("summary").GetString().Should().Contain("Квартальный отчёт Q2 2026");
    }

    [Fact]
    public async Task Ticket_opened_by_an_investor_is_audited_to_that_investor_not_an_admin()
    {
        // The ticket is opened by an INVESTOR — the journal must attribute it to them, not to staff.
        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);

        var create = await investor.PostAsJsonAsync(TicketsRoute, new
        {
            subject = "Не проходит оплата", category = "Платежи", body = "Карта отклонена.",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var ticketId = createdDoc.RootElement.GetProperty("id").GetString()!;

        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var opened = (await ReadJournalAsync(admin, $"{AuditRoute}?entityId={ticketId}"))
            .Single(e => e.GetProperty("eventType").GetString() == "TicketOpened");
        opened.GetProperty("entityType").GetString().Should().Be("SupportTicket");
        // An unverified investor has no KYC name, so the role label stands in — but it is NOT an admin.
        opened.GetProperty("actorName").GetString().Should().Be("Инвестор");
        opened.GetProperty("severity").GetString().Should().Be("warning", "an inbound ticket needs attention");
        opened.GetProperty("summary").GetString().Should().Contain("Не проходит оплата");

        // Closing it is audited too.
        (await investor.PostAsync($"{TicketsRoute}/{ticketId}/close", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var closed = (await ReadJournalAsync(admin, $"{AuditRoute}?entityId={ticketId}"))
            .Single(e => e.GetProperty("eventType").GetString() == "TicketClosed");
        closed.GetProperty("severity").GetString().Should().Be("success");
        closed.GetProperty("summary").GetString().Should().Contain("закрыт");
    }

    [Fact]
    public async Task Journal_is_paged_and_filterable_by_severity()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        await CreatePropertyAsync(admin);
        await CreatePropertyAsync(admin);

        var response = await admin.GetAsync($"{AuditRoute}?page=1&pageSize=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("items").GetArrayLength().Should().Be(1);
        root.GetProperty("pageSize").GetInt32().Should().Be(1);
        root.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        // Severity filter returns only that level.
        var successOnly = await ReadJournalAsync(admin, $"{AuditRoute}?severity=success&pageSize=100");
        successOnly.Should().OnlyContain(e => e.GetProperty("severity").GetString() == "success");

        // An unknown severity is rejected rather than silently ignored.
        (await admin.GetAsync($"{AuditRoute}?severity=nonsense"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Journal_is_Admin_or_Compliance_only()
    {
        (await _factory.CreateClient().GetAsync(AuditRoute))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);
        (await investor.GetAsync(AuditRoute)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<List<JsonElement>> ReadJournalAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => JsonDocument.Parse(e.GetRawText()).RootElement)
            .ToList();
    }

    private static async Task<string> CreatePropertyAsync(HttpClient adminClient)
    {
        var create = await adminClient.PostAsJsonAsync(PropertiesRoute, new
        {
            name = "Osh Commercial Plaza",
            description = "desc",
            address = "Kurmanjan Datka St 45, Osh",
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
