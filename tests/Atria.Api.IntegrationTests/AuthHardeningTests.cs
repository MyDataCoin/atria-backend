using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Regression cover for the authentication findings of the 2026-08-04 review. Each test pins the
/// behaviour that was missing, so the specific mistake cannot come back unnoticed.
/// </summary>
public sealed class AuthHardeningTests : IClassFixture<AuthHardeningTests.LockoutFactory>
{
    /// <summary>
    /// The shared factory raises the lockout threshold to 1000 so that test classes sharing one
    /// seeded admin row do not lock each other out. Driving 1000 BCrypt verifications to prove the
    /// lockout works would take minutes, so this class runs its own host at a realistic threshold.
    /// </summary>
    public sealed class LockoutFactory : AtriaApiFactory
    {
        public const int MaxFailedLogins = 4;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Auth:Lockout:MaxFailedLogins"] = MaxFailedLogins.ToString(),
                    ["Auth:Lockout:LockoutMinutes"] = "15",
                }));
        }
    }

    private readonly LockoutFactory _factory;

    public AuthHardeningTests(LockoutFactory factory) => _factory = factory;

    /// <summary>
    /// C-3. The throttle list named "/api/v1/auth/login" — a route that does not exist — so the real
    /// credential logins matched nothing and accepted unlimited password attempts. This asserts the
    /// routes the limiter must cover are the routes the controller actually exposes.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/auth/admin/login")]
    [InlineData("/api/v1/auth/realtor/login")]
    [InlineData("/api/v1/auth/refresh")]
    [InlineData("/api/v1/auth/register/phone/request-otp")]
    [InlineData("/api/v1/appeals")]
    public void Throttled_routes_are_routes_that_exist(string route)
    {
        // The limiter matches by path prefix, so a throttled entry that no controller serves is a
        // silent hole. Anything that 404s here is not being protected by the rule that names it.
        using var client = _factory.CreateClient();

        var response = client.PostAsJsonAsync(route, new { }).GetAwaiter().GetResult();

        response.StatusCode.Should().NotBe(
            HttpStatusCode.NotFound,
            $"the rate limiter throttles '{route}', which only protects something if the route exists");
    }

    /// <summary>
    /// C-3. Wrong passwords must count against the ACCOUNT, not merely against the caller's address:
    /// credential stuffing from a pool of addresses never trips a per-IP window.
    /// </summary>
    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account_out()
    {
        using var superAdmin = _factory.CreateClient();
        var (username, _) = await RegisterRealtorAsync(superAdmin);

        using var attacker = _factory.CreateClient();

        for (var i = 0; i < LockoutFactory.MaxFailedLogins; i++)
        {
            var wrong = await attacker.PostAsJsonAsync(
                "/api/v1/auth/realtor/login", new { username, password = "definitely-not-it" });
            wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var withTheRealPassword = await attacker.PostAsJsonAsync(
            "/api/v1/auth/realtor/login", new { username, password = SeededRealtorPassword });

        withTheRealPassword.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "the account is locked, so even the correct password must be refused until it lifts");
    }

    /// <summary>
    /// M-12. A signed, unexpired token must stop working the moment the account behind it is banned.
    /// Without the security-stamp check a ban only took effect when the token ran out.
    /// </summary>
    [Fact]
    public async Task Banning_an_account_invalidates_its_already_issued_access_token()
    {
        using var superAdmin = _factory.CreateClient();
        var (username, realtorId) = await RegisterRealtorAsync(superAdmin);

        // The realtor signs in and holds a token that is valid by every ordinary measure.
        using var realtor = _factory.CreateClient();
        var login = await realtor.PostAsJsonAsync(
            "/api/v1/auth/realtor/login", new { username, password = SeededRealtorPassword });
        login.IsSuccessStatusCode.Should().BeTrue();

        var token = (await login.Content.ReadFromJsonAsync<TokenPair>())!.AccessToken;
        realtor.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var beforeBan = await realtor.GetAsync("/api/v1/realtor/me");
        beforeBan.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);

        // Ban them. The token in their hands has not expired and its signature is still good.
        (await superAdmin.PostAsync($"/api/v1/users/{realtorId}/ban", null))
            .IsSuccessStatusCode.Should().BeTrue();

        var afterBan = await realtor.GetAsync("/api/v1/realtor/me");

        afterBan.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "a ban must end the session now, not when the access token happens to expire");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private const string SeededRealtorPassword = "temp-password-1234";

    private sealed record TokenPair(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken);

    /// <summary>Registers a throwaway realtor so a test never locks or bans a shared seeded account.</summary>
    private async Task<(string Username, string Id)> RegisterRealtorAsync(HttpClient superAdmin)
    {
        await AuthenticateAsync(superAdmin, AtriaApiFactory.SuperAdminUsername, AtriaApiFactory.SuperAdminPassword);

        var username = "hardening-" + Guid.NewGuid().ToString("N")[..8];
        var created = await superAdmin.PostAsJsonAsync("/api/v1/realtors", new
        {
            username,
            password = SeededRealtorPassword,
            fullName = "Hardening Test"
        });
        created.IsSuccessStatusCode.Should().BeTrue();

        // The endpoint answers with the created realtor; the id is either the payload itself or a
        // property on it depending on the shape, so accept both rather than guessing.
        var payload = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var id = payload.ValueKind == System.Text.Json.JsonValueKind.String
            ? payload.GetString()
            : payload.GetProperty("id").GetString();
        id.Should().NotBeNullOrEmpty("the registration response must identify the new realtor");

        return (username, id!);
    }

    private static async Task AuthenticateAsync(HttpClient client, string username, string password)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/admin/login", new { username, password });
        login.IsSuccessStatusCode.Should().BeTrue($"credential login for {username} should succeed");

        var tokens = await login.Content.ReadFromJsonAsync<TokenPair>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens!.AccessToken);
    }

}
