using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Covers the blocked-user flow: a banned credential account gets a distinct 403 "banned" on login
/// (a wrong password stays 401), and can submit an anonymous appeal that the super admin then reads.
/// </summary>
public sealed class BanAppealFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";
    private const string RealtorsRoute = "/api/v1/realtors";
    private const string AppealsRoute = "/api/v1/appeals";
    private const string UsersRoute = "/api/v1/users";

    private readonly AtriaApiFactory _factory;

    public BanAppealFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Banned_login_returns_403_banned_while_wrong_password_stays_401()
    {
        var superAdmin = _factory.CreateClient();
        await AuthenticateSuperAdminAsync(superAdmin);

        var (username, id) = await RegisterRealtorAsync(superAdmin);

        // Wrong password before any ban → 401.
        (await Login(username, "totally-wrong")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await superAdmin.PostAsync($"{UsersRoute}/{id}/ban", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Correct password but banned → 403 with a ban marker.
        var banned = await Login(username, "temp1234");
        banned.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await banned.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("title").GetString().Should().Contain("banned");
        root.GetProperty("reason").GetString().Should().Be("banned");

        // Wrong password on a banned account is still an indistinguishable 401 (ban not leaked).
        (await Login(username, "still-wrong")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ban_reason_is_surfaced_on_the_403_login_response()
    {
        var superAdmin = _factory.CreateClient();
        await AuthenticateSuperAdminAsync(superAdmin);
        var (username, id) = await RegisterRealtorAsync(superAdmin);

        const string reason = "Нарушение правил платформы";
        (await superAdmin.PostAsJsonAsync($"{UsersRoute}/{id}/ban", new { reason }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var banned = await Login(username, "temp1234");
        banned.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await banned.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("reason").GetString().Should().Be("banned");
        root.GetProperty("banReason").GetString().Should().Be(reason);
        // The reason also lands in detail (frontend reads banReason first, else detail).
        root.GetProperty("detail").GetString().Should().Be(reason);
    }

    [Fact]
    public async Task Ban_without_a_reason_omits_ban_reason_on_the_403()
    {
        var superAdmin = _factory.CreateClient();
        await AuthenticateSuperAdminAsync(superAdmin);
        var (username, id) = await RegisterRealtorAsync(superAdmin);

        (await superAdmin.PostAsync($"{UsersRoute}/{id}/ban", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var banned = await Login(username, "temp1234");
        banned.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await banned.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("reason").GetString().Should().Be("banned");
        doc.RootElement.TryGetProperty("banReason", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Anonymous_appeal_is_recorded_and_super_admin_can_read_it()
    {
        var username = "appellant-" + Guid.NewGuid().ToString("N")[..8];

        // Anonymous POST — no token.
        var submit = await _factory.CreateClient().PostAsJsonAsync(
            AppealsRoute, new { username, message = "Считаю блокировку ошибкой, прошу разобраться." });
        submit.StatusCode.Should().Be(HttpStatusCode.Created);

        var superAdmin = _factory.CreateClient();
        await AuthenticateSuperAdminAsync(superAdmin);

        var list = await superAdmin.GetAsync(AppealsRoute);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var mine = doc.RootElement.EnumerateArray()
            .Single(a => a.GetProperty("username").GetString() == username);
        mine.GetProperty("message").GetString().Should().Be("Считаю блокировку ошибкой, прошу разобраться.");
    }

    [Fact]
    public async Task Appeal_resolves_full_name_from_a_registered_realtor()
    {
        var superAdmin = _factory.CreateClient();
        await AuthenticateSuperAdminAsync(superAdmin);
        var (username, _) = await RegisterRealtorAsync(superAdmin);

        (await _factory.CreateClient().PostAsJsonAsync(AppealsRoute, new { username, message = "Разблокируйте." }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        using var doc = JsonDocument.Parse(await (await superAdmin.GetAsync(AppealsRoute)).Content.ReadAsStringAsync());
        doc.RootElement.EnumerateArray()
            .First(a => a.GetProperty("username").GetString() == username)
            .GetProperty("fullName").GetString().Should().Be("Марат Урманов");
    }

    [Fact]
    public async Task Appeal_with_empty_message_is_bad_request()
    {
        (await _factory.CreateClient().PostAsJsonAsync(AppealsRoute, new { username = "x", message = "" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Appeals_list_is_forbidden_for_a_regular_admin()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        (await admin.GetAsync(AppealsRoute)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Appeals_list_is_unauthorized_without_a_token()
    {
        (await _factory.CreateClient().GetAsync(AppealsRoute)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // --- helpers ---

    private async Task<(string Username, string Id)> RegisterRealtorAsync(HttpClient superAdmin)
    {
        var username = "marat-" + Guid.NewGuid().ToString("N")[..8];
        var create = await superAdmin.PostAsJsonAsync(RealtorsRoute, new
        {
            username, password = "temp1234", fullName = "Марат Урманов", companyName = "ATRIA Realty"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        return (username, doc.RootElement.GetProperty("id").GetString()!);
    }

    private Task<HttpResponseMessage> Login(string username, string password)
        => _factory.CreateClient().PostAsJsonAsync(AdminLoginRoute, new { username, password });

    private async Task AuthenticateSuperAdminAsync(HttpClient client)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await LoginToken(AtriaApiFactory.SuperAdminUsername, AtriaApiFactory.SuperAdminPassword));

    private async Task AuthenticateAdminAsync(HttpClient client)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await LoginToken(AtriaApiFactory.AdminUsername, AtriaApiFactory.AdminPassword));

    private async Task<string> LoginToken(string username, string password)
    {
        var login = await _factory.CreateClient().PostAsJsonAsync(AdminLoginRoute, new { username, password });
        login.IsSuccessStatusCode.Should().BeTrue($"login for {username} should succeed");
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString()!;
    }
}
