using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Walks a reporting period end to end: the accountant reports what the object earned, someone else
/// confirms it, and only then can a dividend be declared from it.
/// <para>
/// The rule worth having a real HTTP test for is the split: the person who reports the figures may
/// not be the person who confirms them. The management company asked for it in so many words, and it
/// is the kind of rule that quietly stops holding the moment someone wires up a new endpoint — so it
/// is exercised here with two genuinely different accounts rather than mocked identities.
/// </para>
/// </summary>
public sealed class OperatingPeriodFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string PeriodsRoute = "/api/v1/operating-periods";
    private const string PropertiesRoute = "/api/v1/properties";
    private const string AdminLoginRoute = "/api/v1/auth/admin/login";

    private static readonly DateTime Start = new(2027, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    private readonly AtriaApiFactory _factory;

    public OperatingPeriodFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task An_accountant_reports_a_period_and_someone_else_confirms_it()
    {
        var admin = await ClientAsync(AtriaApiFactory.AdminUsername, AtriaApiFactory.AdminPassword);
        var accountant = await ClientAsync(
            AtriaApiFactory.AccountantUsername, AtriaApiFactory.AccountantPassword);

        var propertyId = await CreateIssueAsync(admin);
        var periodId = await ReportAsync(accountant, propertyId, 500_000m, 120_000m);

        var draft = await GetPeriodAsync(accountant, periodId);
        draft.GetProperty("status").GetString().Should().Be("draft");
        draft.GetProperty("netIncome").GetDecimal().Should().Be(380_000m);

        // Confirmed by the admin — a different person from the accountant who reported it.
        (await admin.PostAsync($"{PeriodsRoute}/{periodId}/confirm", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var confirmed = await GetPeriodAsync(accountant, periodId);
        confirmed.GetProperty("status").GetString().Should().Be("confirmed");
        confirmed.GetProperty("confirmedByUserId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task The_person_who_reported_the_figures_cannot_confirm_them()
    {
        var admin = await ClientAsync(AtriaApiFactory.AdminUsername, AtriaApiFactory.AdminPassword);
        var accountant = await ClientAsync(
            AtriaApiFactory.AccountantUsername, AtriaApiFactory.AccountantPassword);

        var propertyId = await CreateIssueAsync(admin);
        var periodId = await ReportAsync(accountant, propertyId, 300_000m, 50_000m);

        var response = await accountant.PostAsync($"{PeriodsRoute}/{periodId}/confirm", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var stillDraft = await GetPeriodAsync(accountant, periodId);
        stillDraft.GetProperty("status").GetString().Should().Be("draft");
    }

    [Fact]
    public async Task A_confirmed_period_can_no_longer_be_revised()
    {
        var admin = await ClientAsync(AtriaApiFactory.AdminUsername, AtriaApiFactory.AdminPassword);
        var accountant = await ClientAsync(
            AtriaApiFactory.AccountantUsername, AtriaApiFactory.AccountantPassword);

        var propertyId = await CreateIssueAsync(admin);
        var periodId = await ReportAsync(accountant, propertyId, 400_000m, 100_000m);

        // While it is a draft, correcting the figures is ordinary work.
        (await accountant.PatchAsJsonAsync($"{PeriodsRoute}/{periodId}", new
        {
            grossRevenue = 450_000m,
            operatingExpenses = 100_000m,
            note = "уточнено",
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await admin.PostAsync($"{PeriodsRoute}/{periodId}/confirm", null);

        var afterConfirm = await accountant.PatchAsJsonAsync($"{PeriodsRoute}/{periodId}", new
        {
            grossRevenue = 900_000m,
            operatingExpenses = 0m,
        });

        afterConfirm.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var unchanged = await GetPeriodAsync(accountant, periodId);
        unchanged.GetProperty("netIncome").GetDecimal().Should().Be(350_000m);
    }

    [Fact]
    public async Task The_same_period_cannot_be_reported_twice_for_one_issue()
    {
        var admin = await ClientAsync(AtriaApiFactory.AdminUsername, AtriaApiFactory.AdminPassword);
        var accountant = await ClientAsync(
            AtriaApiFactory.AccountantUsername, AtriaApiFactory.AccountantPassword);

        var propertyId = await CreateIssueAsync(admin);
        await ReportAsync(accountant, propertyId, 100_000m, 10_000m);

        var duplicate = await accountant.PostAsJsonAsync(PeriodsRoute, new
        {
            propertyId,
            startUtc = Start,
            endUtc = End,
            grossRevenue = 999_000m,
            operatingExpenses = 0m,
        });

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_lawyer_reads_the_figures_but_cannot_report_or_confirm_them()
    {
        // Auditor is read-only across the platform, which is exactly what the lawyer's account needs
        // and exactly what it must not exceed.
        var admin = await ClientAsync(AtriaApiFactory.AdminUsername, AtriaApiFactory.AdminPassword);
        var accountant = await ClientAsync(
            AtriaApiFactory.AccountantUsername, AtriaApiFactory.AccountantPassword);
        var lawyer = await ClientAsync(AtriaApiFactory.LawyerUsername, AtriaApiFactory.LawyerPassword);

        var propertyId = await CreateIssueAsync(admin);
        var periodId = await ReportAsync(accountant, propertyId, 200_000m, 40_000m);

        // Reads.
        var read = await lawyer.GetAsync($"{PeriodsRoute}/{periodId}");
        read.StatusCode.Should().Be(HttpStatusCode.OK);

        // Writes nothing.
        (await lawyer.PostAsJsonAsync(PeriodsRoute, new
        {
            propertyId,
            startUtc = Start.AddYears(-1),
            endUtc = End.AddYears(-1),
            grossRevenue = 1_000m,
            operatingExpenses = 0m,
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await lawyer.PostAsync($"{PeriodsRoute}/{periodId}/confirm", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_lawyer_reads_the_audit_journal()
    {
        // The journal is the record of who did what, which is what an auditor's account exists to
        // look at. Read-only either way — the endpoint has no write side.
        var lawyer = await ClientAsync(AtriaApiFactory.LawyerUsername, AtriaApiFactory.LawyerPassword);

        var response = await lawyer.GetAsync("/api/v1/audit?pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_accountant_cannot_read_the_audit_journal()
    {
        // Deliberately closed to Finance: an accountant works from the figures, not from everyone
        // else's actions.
        var accountant = await ClientAsync(
            AtriaApiFactory.AccountantUsername, AtriaApiFactory.AccountantPassword);

        var response = await accountant.GetAsync("/api/v1/audit?pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_investor_cannot_reach_the_figures_at_all()
    {
        var anon = _factory.CreateClient();

        (await anon.GetAsync($"{PeriodsRoute}?propertyId={Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<string> ReportAsync(
        HttpClient client, string propertyId, decimal revenue, decimal expenses)
    {
        var response = await client.PostAsJsonAsync(PeriodsRoute, new
        {
            propertyId,
            startUtc = Start,
            endUtc = End,
            grossRevenue = revenue,
            operatingExpenses = expenses,
            note = "IV квартал",
            lines = new[]
            {
                new { kind = "revenue", label = "Аренда 1 этаж", amount = revenue },
                new { kind = "expense", label = "Коммунальные", amount = expenses },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetString()!;
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

    private static async Task<JsonElement> GetPeriodAsync(HttpClient client, string id)
    {
        var response = await client.GetAsync($"{PeriodsRoute}/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private async Task<HttpClient> ClientAsync(string username, string password)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(AdminLoginRoute, new { username, password });
        login.IsSuccessStatusCode.Should().BeTrue($"{username} should be able to log in");
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
