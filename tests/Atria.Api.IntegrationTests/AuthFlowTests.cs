using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Exercises the PHONE-ONLY auth flow end to end through the real controller, mediator
/// pipeline (validation + handlers) and the EF in-memory store:
/// request-otp (Kyrgyzstan +996 number) -> verify-otp (fixed dev code) -> 200 OK with an
/// <c>AuthTokensDto</c> (accessToken / refreshToken). There is no email/password path.
/// </summary>
public sealed class AuthFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string RequestOtpRoute = "/api/v1/auth/register/phone/request-otp";
    private const string VerifyOtpRoute = "/api/v1/auth/register/phone/verify-otp";

    // The test host configures Otp:DevFixedCode = 333333 (no SMS sent).
    private const string DevCode = "333333";

    private readonly AtriaApiFactory _factory;

    public AuthFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task RequestThenVerifyOtp_WithKyrgyzNumber_ReturnsToken()
    {
        var client = _factory.CreateClient();
        var phone = UniqueKgPhone();

        var request = await client.PostAsJsonAsync(RequestOtpRoute, new { phone });
        request.IsSuccessStatusCode.Should()
            .BeTrue("requesting an OTP for a valid +996 number should return 2xx, got {0}", request.StatusCode);

        var verify = await client.PostAsJsonAsync(VerifyOtpRoute, new { phone, code = DevCode });
        verify.IsSuccessStatusCode.Should()
            .BeTrue("verifying the correct code should return 2xx, got {0}", verify.StatusCode);

        var accessToken = await ReadAccessTokenAsync(verify);
        accessToken.Should().NotBeNullOrWhiteSpace("a successful verification must return an access token");
    }

    [Fact]
    public async Task VerifyOtp_WithWrongCode_DoesNotIssueToken()
    {
        var client = _factory.CreateClient();
        var phone = UniqueKgPhone();

        await client.PostAsJsonAsync(RequestOtpRoute, new { phone });

        var verify = await client.PostAsJsonAsync(VerifyOtpRoute, new { phone, code = "000000" });

        verify.IsSuccessStatusCode.Should().BeFalse("a wrong OTP must not authenticate");
        verify.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RequestOtp_WithNonKyrgyzNumber_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        // US number — not a +996 KG number, must be rejected by validation.
        var response = await client.PostAsJsonAsync(RequestOtpRoute, new { phone = "+15551234567" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // Distinct valid KG mobile per test run: +996 7XXXXXXXX (9 national digits, first = 7).
    private static string UniqueKgPhone()
    {
        var digits = new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).ToArray());
        var eight = (digits + "00000000")[..8];
        return $"+9967{eight}";
    }

    /// <summary>Reads the <c>accessToken</c> from the AuthTokensDto JSON body (camelCase).</summary>
    private static async Task<string?> ReadAccessTokenAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("accessToken", out var token)
            ? token.GetString()
            : null;
    }
}
