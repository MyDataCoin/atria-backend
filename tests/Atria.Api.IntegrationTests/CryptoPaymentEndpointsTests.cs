using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>The operator configures crypto payments and the site sees exactly the methods that are complete.</summary>
public sealed class CryptoPaymentEndpointsTests : IClassFixture<AtriaApiFactory>
{
    private readonly AtriaApiFactory _factory;

    public CryptoPaymentEndpointsTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Saved_settings_turn_on_the_methods_they_complete()
    {
        var admin = await GovernanceTestHelpers.ApproverClientAsync(_factory);

        var save = await admin.PutAsJsonAsync("/api/v1/payments/settings", new
        {
            usdtRateKgs = 87.5m,
            bep20Address = "0x1111111111111111111111111111111111111111",
            trc20Address = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
            kgstContractAddress = (string?)null,
        });
        save.StatusCode.Should().Be(HttpStatusCode.OK);

        var anonymous = _factory.CreateClient();
        var methods = await anonymous.GetAsync("/api/v1/payments/methods");
        methods.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await methods.Content.ReadAsStringAsync());
        var wire = doc.RootElement.EnumerateArray().Select(m => m.GetProperty("method").GetString()).ToList();
        wire.Should().BeEquivalentTo(new[] { "usdt_bep20", "usdt_trc20" }, "KGST has no contract configured");
    }

    [Fact]
    public async Task A_malformed_wallet_is_a_400_not_a_saved_setting()
    {
        var admin = await GovernanceTestHelpers.ApproverClientAsync(_factory);

        var save = await admin.PutAsJsonAsync("/api/v1/payments/settings", new
        {
            usdtRateKgs = 87.5m,
            bep20Address = "not-an-address",
        });

        save.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Settings_are_not_readable_anonymously()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync("/api/v1/payments/settings");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
