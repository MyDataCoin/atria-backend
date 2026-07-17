using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Atria.Domain.Realtors;
using Atria.Domain.Users;
using Atria.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Exercises the super-admin feature end to end against DB-only credential accounts: login via the
/// shared admin endpoint (by username) yields a token whose role contains "super"; ban/unban and
/// password reset/restore are gated to SuperAdmin; banning refuses login (investor OTP) and surfaces
/// as blocked in the overview/stats reads; a reset changes the stored hash; each action is journalled.
/// </summary>
public sealed class SuperAdminFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";
    private const string UsersRoute = "/api/v1/users";
    private const string StatsRoute = "/api/v1/realtors/stats";
    private const string RequestOtpRoute = "/api/v1/auth/register/phone/request-otp";
    private const string VerifyOtpRoute = "/api/v1/auth/register/phone/verify-otp";
    private const string DevCode = "333333";

    private readonly AtriaApiFactory _factory;

    public SuperAdminFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Super_admin_login_issues_a_token_whose_role_contains_super()
    {
        var token = await LoginAsync(_factory.CreateClient(), AtriaApiFactory.SuperAdminUsername, AtriaApiFactory.SuperAdminPassword);

        RoleFromJwt(token).Should().ContainEquivalentOf("super", "the frontend routes *super* roles (case-insensitive) to the super-admin app");
    }

    [Fact]
    public async Task Login_with_a_wrong_password_is_unauthorized()
    {
        var login = await _factory.CreateClient().PostAsJsonAsync(
            AdminLoginRoute, new { username = AtriaApiFactory.SuperAdminUsername, password = "nope" });

        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Regular_admin_is_forbidden_from_super_admin_actions()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        (await admin.PostAsync($"{UsersRoute}/{Guid.NewGuid()}/ban", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Super_admin_can_read_the_admin_reporting_surfaces()
    {
        var superAdmin = _factory.CreateClient();
        await AuthenticateSuperAdminAsync(superAdmin);

        // The super-admin panel reads these to decide who to moderate; all must be 200, not 403.
        (await superAdmin.GetAsync(UsersRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await superAdmin.GetAsync(StatsRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await superAdmin.GetAsync("/api/v1/audit")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Banning_an_investor_refuses_login_and_shows_blocked_in_overview()
    {
        var phone = UniqueKgPhone();
        var investor = _factory.CreateClient();
        await AuthenticateInvestorAsync(investor, phone);
        var investorId = await UserIdByPhoneAsync(phone);

        var superAdmin = _factory.CreateClient();
        await AuthenticateSuperAdminAsync(superAdmin);

        (await superAdmin.PostAsync($"{UsersRoute}/{investorId}/ban", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The banned investor can no longer complete OTP login.
        await investor.PostAsJsonAsync(RequestOtpRoute, new { phone });
        var verify = await investor.PostAsJsonAsync(VerifyOtpRoute, new { phone, code = DevCode });
        verify.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        BlockedInOverview(await ReadOverviewAsync(), investorId).Should().BeTrue();

        // Unban restores login.
        (await superAdmin.PostAsync($"{UsersRoute}/{investorId}/unban", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AuthenticateInvestorAsync(_factory.CreateClient(), phone); // no throw => login works again

        await AuditContainsAsync(investorId, "UserBanned", "UserUnbanned");
    }

    [Fact]
    public async Task Banning_a_realtor_shows_blocked_in_stats()
    {
        var realtorId = await SeedRealtorAsync("blocked-realtor", "Заблокированный Риелтор");

        var superAdmin = _factory.CreateClient();
        await AuthenticateSuperAdminAsync(superAdmin);

        (await superAdmin.PostAsync($"{UsersRoute}/{realtorId}/ban", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        using var statsDoc = JsonDocument.Parse(await (await admin.GetAsync(StatsRoute)).Content.ReadAsStringAsync());
        statsDoc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == realtorId.ToString())
            .GetProperty("blocked").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Resetting_a_realtor_password_changes_the_stored_hash_and_flags_a_reset()
    {
        var realtorId = await SeedRealtorAsync("reset-realtor", "Риелтор Для Сброса");

        var superAdmin = _factory.CreateClient();
        await AuthenticateSuperAdminAsync(superAdmin);

        var reset = await superAdmin.PostAsJsonAsync($"{UsersRoute}/{realtorId}/password/reset", new { });
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await reset.Content.ReadAsStringAsync());
        var temp = doc.RootElement.GetProperty("temporaryPassword").GetString();
        temp.Should().NotBeNullOrWhiteSpace();

        var (verifiesTemp, mustReset) = await InspectPasswordAsync(realtorId, temp!);
        verifiesTemp.Should().BeTrue("the stored hash now matches the temp password");
        mustReset.Should().BeTrue();

        // Restore clears the forced-reset flag (a second restore is a 409 — nothing pending).
        (await superAdmin.PostAsync($"{UsersRoute}/{realtorId}/password/restore", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await superAdmin.PostAsync($"{UsersRoute}/{realtorId}/password/restore", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        await AuditContainsAsync(realtorId, "PasswordReset", "PasswordRestored");
    }

    [Fact]
    public async Task Resetting_an_investor_password_is_a_conflict()
    {
        var phone = UniqueKgPhone();
        await AuthenticateInvestorAsync(_factory.CreateClient(), phone);
        var investorId = await UserIdByPhoneAsync(phone);

        var superAdmin = _factory.CreateClient();
        await AuthenticateSuperAdminAsync(superAdmin);

        (await superAdmin.PostAsJsonAsync($"{UsersRoute}/{investorId}/password/reset", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Ban_of_a_missing_user_is_not_found()
    {
        var superAdmin = _factory.CreateClient();
        await AuthenticateSuperAdminAsync(superAdmin);

        (await superAdmin.PostAsync($"{UsersRoute}/{Guid.NewGuid()}/ban", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- helpers ---

    private static bool BlockedInOverview(JsonElement overview, Guid userId)
        => overview.EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == userId.ToString())
            .GetProperty("blocked").GetBoolean();

    private async Task<JsonElement> ReadOverviewAsync()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var json = await (await admin.GetAsync(UsersRoute)).Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var login = await client.PostAsJsonAsync(AdminLoginRoute, new { username, password });
        login.IsSuccessStatusCode.Should().BeTrue($"credential login for {username} should succeed");
        return (await ReadTokenAsync(login))!;
    }

    private async Task AuthenticateSuperAdminAsync(HttpClient client)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await LoginAsync(client, AtriaApiFactory.SuperAdminUsername, AtriaApiFactory.SuperAdminPassword));

    private async Task AuthenticateAdminAsync(HttpClient client)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await LoginAsync(client, AtriaApiFactory.AdminUsername, AtriaApiFactory.AdminPassword));

    private static async Task AuthenticateInvestorAsync(HttpClient client, string phone)
    {
        await client.PostAsJsonAsync(RequestOtpRoute, new { phone });
        var verify = await client.PostAsJsonAsync(VerifyOtpRoute, new { phone, code = DevCode });
        verify.IsSuccessStatusCode.Should().BeTrue();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ReadTokenAsync(verify));
    }

    private async Task<Guid> UserIdByPhoneAsync(string phone)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtriaDbContext>();
        return (await db.Users.AsNoTracking().FirstAsync(u => u.PhoneNumber == phone)).Id;
    }

    // Seeds a realtor credential user (unique username) + its realtor profile, returning the user id.
    private async Task<Guid> SeedRealtorAsync(string username, string fullName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtriaDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Atria.Application.Abstractions.IPasswordHasher>();

        var user = User.CreateServiceAccount(username, Role.Realtor, hasher.Hash("seeded-pass"));
        db.Users.Add(user);
        db.RealtorProfiles.Add(RealtorProfile.Create(user.Id, fullName, companyName: "ATRIA"));
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<(bool VerifiesTemp, bool MustReset)> InspectPasswordAsync(Guid userId, string temp)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtriaDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Atria.Application.Abstractions.IPasswordHasher>();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        return (hasher.Verify(temp, user.PasswordHash!), user.MustResetPassword);
    }

    private async Task AuditContainsAsync(Guid entityId, params string[] eventTypes)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtriaDbContext>();
        var logged = await db.AuditLogEntries.AsNoTracking()
            .Where(e => e.EntityId == entityId)
            .Select(e => e.EventType)
            .ToListAsync();
        logged.Should().Contain(eventTypes);
    }

    private static async Task<string?> ReadTokenAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString();
    }

    private static string RoleFromJwt(string jwt)
    {
        var payload = jwt.Split('.')[1];
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')
            .Replace('-', '+').Replace('_', '/');
        using var doc = JsonDocument.Parse(Convert.FromBase64String(padded));
        return doc.RootElement.GetProperty("role").GetString()!;
    }

    private static string UniqueKgPhone()
    {
        var digits = new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).ToArray());
        var eight = (digits + "00000000")[..8];
        return $"+9967{eight}";
    }
}
