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

        var verify = await client.PostAsJsonAsync(VerifyOtpRoute, new { phone, code = _factory.Sms.CodeFor(phone) });
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

    /// <summary>
    /// The browser clients never see the refresh token — it lives in the HttpOnly cookie — so they
    /// POST an empty <c>{}</c> body and rely on the cookie. That must reach the controller.
    /// </summary>
    /// <remarks>
    /// Regression: <c>RefreshTokenRequest</c> declared <c>string RefreshToken</c>, and under
    /// &lt;Nullable&gt;enable&lt;/Nullable&gt; MVC infers an implicit [Required] from the non-nullable
    /// reference type. A <c>{}</c> body then failed model validation with 400 BEFORE the action ran,
    /// so the cookie was never read and every transparent refresh died — sessions could not survive
    /// a reload. The 401-vs-400 distinction is the whole point: 400 means the body was rejected,
    /// 401 would mean the cookie was read and refused.
    /// </remarks>
    [Fact]
    public async Task Refresh_WithEmptyJsonBody_UsesTheCookieInsteadOfRejectingTheBody()
    {
        var client = _factory.CreateClient();
        var phone = UniqueKgPhone();

        await client.PostAsJsonAsync(RequestOtpRoute, new { phone });
        var verify = await client.PostAsJsonAsync(VerifyOtpRoute, new { phone, code = _factory.Sms.CodeFor(phone) });
        verify.IsSuccessStatusCode.Should().BeTrue("the OTP flow must authenticate before refresh can be tested");

        var refreshCookie = RefreshCookieFrom(verify);
        refreshCookie.Should().NotBeNull("a successful login must set the refresh cookie");

        // Exactly what the dashboards send: an empty JSON object plus the cookie, no token in the body.
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Cookie", refreshCookie);

        var refreshed = await client.SendAsync(request);

        refreshed.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "an empty body must not be rejected by model validation — the cookie carries the token");
        (await ReadAccessTokenAsync(refreshed)).Should().NotBeNullOrWhiteSpace("refresh must issue a new access token");
    }

    /// <summary>Extracts the <c>atria_refresh</c> Set-Cookie value as a "name=value" pair, or null.</summary>
    private static string? RefreshCookieFrom(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            return null;

        return cookies
            .Select(c => c.Split(';')[0].Trim())
            .FirstOrDefault(c => c.StartsWith("atria_refresh=", StringComparison.Ordinal));
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
