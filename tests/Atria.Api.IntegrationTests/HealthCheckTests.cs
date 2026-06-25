using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;

namespace Atria.Api.IntegrationTests;

/// <summary>Verifies the liveness probe responds 200 OK with the full host pipeline composed.</summary>
public sealed class HealthCheckTests : IClassFixture<AtriaApiFactory>
{
    private readonly AtriaApiFactory _factory;

    public HealthCheckTests(AtriaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetHealthLive_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/live");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
