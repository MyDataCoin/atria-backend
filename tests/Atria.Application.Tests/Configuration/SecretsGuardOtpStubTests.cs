using Atria.Infrastructure.Configuration;
using Atria.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Atria.Application.Tests.Configuration;

/// <summary>
/// Production refuses to start while the OTP stub is compiled in; every other environment still runs
/// on it.
/// </summary>
/// <remarks>
/// The stub accepts one fixed code for any phone number, so an instance carrying it into production
/// is an unauthenticated takeover of every account on live domains — and a stubbed instance looks
/// exactly like a real one from outside. It stays on elsewhere on purpose: the SMS sender name is not
/// yet approved, and the bench has to keep working until it is.
/// </remarks>
public sealed class SecretsGuardOtpStubTests
{
    /// <summary>A configuration where every required secret is present and plausible.</summary>
    private static IConfiguration FullyConfigured() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=atria",
            ["Jwt:SigningKey"] = new string('j', 44),
            ["Encryption:Key"] = new string('e', 44),
            ["Otp:HashPepper"] = new string('p', 44),
            ["Didit:ApiKey"] = new string('d', 16),
            ["Didit:WebhookSecret"] = new string('w', 32),
        }).Build();

    private static IHostEnvironment Environment(string name)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }

    [Fact]
    public void Production_refuses_to_start_while_the_stub_is_on()
    {
        // Guards the guard: if the stub is ever switched off, this test is asserting nothing and
        // should be deleted along with it rather than left passing vacuously.
        OtpService.StubEnabled.Should().BeTrue(
            "this test exists only to keep the stub out of production");

        var act = () => SecretsGuard.Validate(FullyConfigured(), Environment(Environments.Production));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OTP stub is enabled*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void Every_other_environment_still_runs_on_the_stub(string environmentName)
    {
        var act = () => SecretsGuard.Validate(FullyConfigured(), Environment(environmentName));

        act.Should().NotThrow();
    }
}
