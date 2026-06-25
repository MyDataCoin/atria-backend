using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>
/// Exercises the register -> login HTTP flow end to end through the real controller,
/// mediator pipeline (validation + handlers), and the EF in-memory store. The success
/// response is <c>200 OK</c> with an <c>AuthTokensDto</c> body (accessToken / refreshToken).
/// </summary>
public sealed class AuthFlowTests : IClassFixture<AtriaApiFactory>
{
    private const string RegisterRoute = "/api/v1/auth/register";
    private const string LoginRoute = "/api/v1/auth/login";

    private readonly AtriaApiFactory _factory;

    public AuthFlowTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_WithUniqueCredentials_ReturnsSuccessAndToken()
    {
        // Arrange
        var client = _factory.CreateClient();
        var email = UniqueEmail();
        const string password = "Sup3r-Str0ng-Passw0rd!";

        // Act
        var response = await client.PostAsJsonAsync(
            RegisterRoute,
            new { email, password, firstName = "Ada", lastName = "Lovelace" });

        // Assert
        response.IsSuccessStatusCode.Should()
            .BeTrue("registration of a unique email should return 2xx, got {0}", response.StatusCode);

        var accessToken = await ReadAccessTokenAsync(response);
        accessToken.Should().NotBeNullOrWhiteSpace("a successful registration must return an access token");
    }

    [Fact]
    public async Task RegisterThenLogin_WithSameCredentials_ReturnsToken()
    {
        // Arrange
        var client = _factory.CreateClient();
        var email = UniqueEmail();
        const string password = "Sup3r-Str0ng-Passw0rd!";

        var register = await client.PostAsJsonAsync(
            RegisterRoute,
            new { email, password, firstName = "Grace", lastName = "Hopper" });
        register.IsSuccessStatusCode.Should()
            .BeTrue("seed registration should succeed, got {0}", register.StatusCode);

        // Act
        var login = await client.PostAsJsonAsync(LoginRoute, new { email, password });

        // Assert
        login.IsSuccessStatusCode.Should()
            .BeTrue("login with the just-registered credentials should return 2xx, got {0}", login.StatusCode);

        var accessToken = await ReadAccessTokenAsync(login);
        accessToken.Should().NotBeNullOrWhiteSpace("a successful login must return an access token");
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var email = UniqueEmail();
        const string password = "Sup3r-Str0ng-Passw0rd!";

        var register = await client.PostAsJsonAsync(
            RegisterRoute,
            new { email, password, firstName = (string?)null, lastName = (string?)null });
        register.IsSuccessStatusCode.Should()
            .BeTrue("seed registration should succeed, got {0}", register.StatusCode);

        // Act
        var login = await client.PostAsJsonAsync(LoginRoute, new { email, password = "WrongPassword!1" });

        // Assert
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@atria.tests";

    /// <summary>Reads the <c>accessToken</c> from the AuthTokensDto JSON body (camelCase by default).</summary>
    private static async Task<string?> ReadAccessTokenAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("accessToken", out var token)
            ? token.GetString()
            : null;
    }
}
