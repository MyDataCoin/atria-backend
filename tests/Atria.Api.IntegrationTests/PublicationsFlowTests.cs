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
/// Drives the news feed end to end: an admin publishes a general item and a property-scoped one, the
/// feed returns them newest-first with the denormalized property name, filters (propertyId /
/// generalOnly / type) and paging work, publishing is Admin-only, and edit/delete apply.
/// </summary>
public sealed class PublicationsFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string PublicationsRoute = "/api/v1/publications";
    private const string PropertiesRoute = "/api/v1/properties";
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";
    private const string RequestOtpRoute = "/api/v1/auth/register/phone/request-otp";
    private const string VerifyOtpRoute = "/api/v1/auth/register/phone/verify-otp";
    private const string DevCode = "333333";

    private readonly AtriaApiFactory _factory;

    public PublicationsFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Admin_publishes_property_item_and_it_appears_in_the_feed_with_property_name()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var propertyId = await CreatePropertyAsync(admin);

        var create = await admin.PostAsJsonAsync(PublicationsRoute, new
        {
            type = "financial_report",
            title = "Квартальный отчёт Q2 2026",
            body = "Полный текст отчёта…",
            propertyId,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        using var createdDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var root = createdDoc.RootElement;
        var id = root.GetProperty("id").GetString()!;
        root.GetProperty("type").GetString().Should().Be("financial_report");
        root.GetProperty("status").GetString().Should().Be("published");
        root.GetProperty("propertyId").GetString().Should().Be(propertyId);
        root.GetProperty("propertyName").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("attachments").GetArrayLength().Should().Be(0);

        // Anonymous feed sees it, with the joined property name.
        var anon = _factory.CreateClient();
        var item = await FindInFeedAsync(anon, $"{PublicationsRoute}?propertyId={propertyId}", id);
        item.GetProperty("propertyName").GetString().Should().NotBeNullOrWhiteSpace();

        // ...and by id with the full body.
        var byId = await anon.GetAsync($"{PublicationsRoute}/{id}");
        byId.StatusCode.Should().Be(HttpStatusCode.OK);
        using var byIdDoc = JsonDocument.Parse(await byId.Content.ReadAsStringAsync());
        byIdDoc.RootElement.GetProperty("body").GetString().Should().Be("Полный текст отчёта…");
    }

    [Fact]
    public async Task General_news_has_no_property_and_is_reachable_via_generalOnly()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var create = await admin.PostAsJsonAsync(PublicationsRoute, new
        {
            type = "general_news",
            title = "Платформа обновилась",
            body = "Общая новость платформы.",
            propertyId = (Guid?)null,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = createdDoc.RootElement.GetProperty("id").GetString()!;
        createdDoc.RootElement.GetProperty("propertyId").ValueKind.Should().Be(JsonValueKind.Null);
        createdDoc.RootElement.GetProperty("propertyName").ValueKind.Should().Be(JsonValueKind.Null);

        // generalOnly=true returns it; every item on that page has no property.
        var anon = _factory.CreateClient();
        var response = await anon.GetAsync($"{PublicationsRoute}?generalOnly=true&pageSize=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("id").GetString()).Should().Contain(id);
        items.Should().OnlyContain(i => i.GetProperty("propertyId").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Feed_is_paged_and_reports_totals()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        await PublishGeneralAsync(admin, "Новость A");
        await PublishGeneralAsync(admin, "Новость B");

        var response = await admin.GetAsync($"{PublicationsRoute}?page=1&pageSize=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("items").GetArrayLength().Should().Be(1);
        root.GetProperty("page").GetInt32().Should().Be(1);
        root.GetProperty("pageSize").GetInt32().Should().Be(1);
        root.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        root.GetProperty("totalPages").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Unknown_type_is_rejected_on_create_and_on_filter()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        (await admin.PostAsJsonAsync(PublicationsRoute, new
        {
            type = "not_a_type", title = "T", body = "B", propertyId = (Guid?)null,
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await admin.GetAsync($"{PublicationsRoute}?type=not_a_type"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Publishing_requires_the_Admin_role()
    {
        var body = new { type = "general_news", title = "T", body = "B", propertyId = (Guid?)null };

        var anon = _factory.CreateClient();
        (await anon.PostAsJsonAsync(PublicationsRoute, body))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor);
        (await investor.PostAsJsonAsync(PublicationsRoute, body))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_with_a_missing_property_is_404()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        (await admin.PostAsJsonAsync(PublicationsRoute, new
        {
            type = "news_release", title = "T", body = "B", propertyId = Guid.NewGuid(),
        })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_can_edit_and_delete_a_publication()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var id = await PublishGeneralAsync(admin, "Опечатка в заголовке");

        // PATCH: only the supplied fields change.
        var patch = await admin.PatchAsJsonAsync($"{PublicationsRoute}/{id}",
            new { title = "Исправленный заголовок", type = (string?)null, body = (string?)null });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        using var patched = JsonDocument.Parse(await patch.Content.ReadAsStringAsync());
        patched.RootElement.GetProperty("title").GetString().Should().Be("Исправленный заголовок");

        // DELETE: gone from the feed.
        (await admin.DeleteAsync($"{PublicationsRoute}/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync($"{PublicationsRoute}/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.DeleteAsync($"{PublicationsRoute}/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<string> PublishGeneralAsync(HttpClient admin, string title)
    {
        var create = await admin.PostAsJsonAsync(PublicationsRoute, new
        {
            type = "general_news", title, body = "Текст.", propertyId = (Guid?)null,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> FindInFeedAsync(HttpClient client, string url, string id)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement.GetProperty("items").EnumerateArray()
            .FirstOrDefault(i => i.GetProperty("id").GetString() == id);
        item.ValueKind.Should().NotBe(JsonValueKind.Undefined, "the published item must be in the feed");
        return JsonDocument.Parse(item.GetRawText()).RootElement;
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
