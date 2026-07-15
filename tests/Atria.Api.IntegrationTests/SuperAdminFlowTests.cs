using System;
using System.Collections.Generic;
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
/// Exercises the super-admin feature end to end: login via the shared admin endpoint yields a token
/// whose role contains "super"; ban/unban and password reset/restore are gated to SuperAdmin;
/// banning refuses login (investor OTP) and surfaces as blocked in the overview/stats reads; a reset
/// returns a working temporary password and disables the old one; each action is journalled.
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
        var client = _factory.CreateClient();
        var token = await LoginSuperAdminAsync(client);

        RoleFromJwt(token).Should().ContainEquivalentOf("super", "the frontend routes *super* roles (case-insensitive) to the super-admin app");
    }

    [Fact]
    public async Task Regular_admin_is_forbidden_from_super_admin_actions()
    {
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);

        var response = await admin.PostAsync($"{UsersRoute}/{Guid.NewGuid()}/ban", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Banning_an_investor_refuses_login_and_shows_blocked_in_overview()
    {
        // A verified investor exists after one OTP login.
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

        // The overview reflects blocked:true.
        BlockedInOverview(await ReadOverviewAsync(superAdmin), investorId).Should().BeTrue();

        // Unban restores login.
        (await superAdmin.PostAsync($"{UsersRoute}/{investorId}/unban", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AuthenticateInvestorAsync(_factory.CreateClient(), phone); // no throw => login works again

        await AuditContainsAsync(investorId, "UserBanned", "UserUnbanned");
    }

    [Fact]
    public async Task Banning_a_realtor_shows_blocked_in_stats()
    {
        // A distinct realtor (its own id) so this test does not clobber the shared static-login realtor.
        var realtorId = Guid.NewGuid();
        await SeedRealtorProfileAsync(realtorId, "Заблокированный Риелтор");

        var superAdmin = _factory.CreateClient();
        await AuthenticateSuperAdminAsync(superAdmin);

        (await superAdmin.PostAsync($"{UsersRoute}/{realtorId}/ban", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // stats is Admin/Compliance gated — read it as an admin.
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        using var statsDoc = JsonDocument.Parse(await (await admin.GetAsync(StatsRoute)).Content.ReadAsStringAsync());
        var row = statsDoc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == realtorId.ToString());
        row.GetProperty("blocked").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Resetting_a_realtor_password_changes_the_stored_hash_and_flags_a_reset()
    {
        // A DISTINCT credential-login realtor (own id) so we never touch the shared static-login
        // realtor other suites depend on. Verifying against the stored hash exercises the exact
        // behaviour login relies on, without needing a per-user login route.
        var realtorId = Guid.NewGuid();
        await SeedRealtorServiceAccountAsync(realtorId, "old-password");

        var superAdmin = _factory.CreateClient();
        await AuthenticateSuperAdminAsync(superAdmin);

        var reset = await superAdmin.PostAsJsonAsync($"{UsersRoute}/{realtorId}/password/reset", new { });
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await reset.Content.ReadAsStringAsync());
        var temp = doc.RootElement.GetProperty("temporaryPassword").GetString();
        temp.Should().NotBeNullOrWhiteSpace();

        // The stored hash now matches the temp password, not the old one, and a reset is flagged.
        var (hashVerifiesTemp, hashVerifiesOld, mustReset) = await InspectPasswordAsync(realtorId, temp!, "old-password");
        hashVerifiesTemp.Should().BeTrue();
        hashVerifiesOld.Should().BeFalse();
        mustReset.Should().BeTrue();

        // Restore clears the forced-reset flag (a second restore is a 409 — nothing pending).
        (await superAdmin.PostAsync($"{UsersRoute}/{realtorId}/password/restore", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await superAdmin.PostAsync($"{UsersRoute}/{realtorId}/password/restore", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        await AuditContainsAsync(realtorId, "PasswordReset", "PasswordRestored");
    }

    private async Task<(bool VerifiesNew, bool VerifiesOld, bool MustReset)> InspectPasswordAsync(
        Guid userId, string newPassword, string oldPassword)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtriaDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Atria.Application.Abstractions.IPasswordHasher>();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        return (
            hasher.Verify(newPassword, user.PasswordHash!),
            hasher.Verify(oldPassword, user.PasswordHash!),
            user.MustResetPassword);
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

    private async Task<JsonElement> ReadOverviewAsync(HttpClient superAdmin)
    {
        // /users overview is Admin/Compliance gated — read it as an admin.
        var admin = _factory.CreateClient();
        await AuthenticateAdminAsync(admin);
        var json = await (await admin.GetAsync(UsersRoute)).Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private async Task<string> LoginSuperAdminAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(
            AdminLoginRoute, new { username = "superadmin", password = "superadmin-test-password" });
        login.IsSuccessStatusCode.Should().BeTrue("super-admin login should be enabled in tests");
        return (await ReadTokenAsync(login))!;
    }

    private async Task AuthenticateSuperAdminAsync(HttpClient client)
        => client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await LoginSuperAdminAsync(client));

    private static async Task AuthenticateAdminAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync(AdminLoginRoute, new { username = "admin", password = "admin-test-password" });
        login.IsSuccessStatusCode.Should().BeTrue("static admin login should be enabled in tests");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ReadTokenAsync(login));
    }

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
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.PhoneNumber == phone);
        return user.Id;
    }

    private async Task SeedRealtorProfileAsync(Guid realtorId, string fullName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtriaDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Atria.Application.Abstractions.IPasswordHasher>();
        if (!await db.RealtorProfiles.AnyAsync(p => p.UserId == realtorId))
            db.RealtorProfiles.Add(RealtorProfile.Create(realtorId, fullName, companyName: "ATRIA"));
        if (!await db.Users.AnyAsync(u => u.Id == realtorId))
            db.Users.Add(User.CreateServiceAccount(realtorId, Role.Realtor, hasher.Hash("seeded")));
        await db.SaveChangesAsync();
    }

    private async Task SeedRealtorServiceAccountAsync(Guid realtorId, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtriaDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Atria.Application.Abstractions.IPasswordHasher>();
        if (!await db.Users.AnyAsync(u => u.Id == realtorId))
        {
            db.Users.Add(User.CreateServiceAccount(realtorId, Role.Realtor, hasher.Hash(password)));
            await db.SaveChangesAsync();
        }
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
