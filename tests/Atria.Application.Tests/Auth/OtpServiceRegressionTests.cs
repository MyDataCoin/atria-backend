using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Atria.Infrastructure.Identity;
using Atria.Infrastructure.Persistence;
using Atria.Infrastructure.Persistence.Stores;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Atria.Application.Tests.Auth;

/// <summary>
/// Regression coverage for <see cref="OtpService"/> persistence bugs:
/// (1) <see cref="OtpService.RequestAsync"/> must COMMIT the generated code so a later
///     <see cref="OtpService.VerifyAsync"/> with the correct code can find and accept it; and
/// (2) each wrong attempt must be PERSISTED so the MaxAttempts lockout actually accumulates
///     across separate verify calls (anti-brute-force) and returns the locked error.
///
/// Exercised end-to-end against a real <see cref="AtriaDbContext"/> (InMemory) +
/// <see cref="UnitOfWork"/> + <see cref="OtpCodeStore"/>. The real <see cref="BcryptPasswordHasher"/>
/// is used so the hashed/stored code round-trips exactly as in production. The SMS sender is a
/// substitute (and is also used to capture the plaintext code the service generated, since the
/// service never returns it).
/// </summary>
public sealed class OtpServiceRegressionTests
{
    private const string Phone = "+15551234567";

    private static readonly OtpOptions Options = new()
    {
        Length = 6,
        TtlMinutes = 5,
        MaxAttempts = 5,
        RequestsPerHour = 100
    };

    private readonly AtriaDbContext _db = CreateDbContext();
    private readonly ISmsSender _sms = Substitute.For<ISmsSender>();
    private readonly IPasswordHasher _hasher = new BcryptPasswordHasher();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public OtpServiceRegressionTests()
        => _clock.UtcNow.Returns(_ => DateTime.UtcNow);

    private OtpService CreateSut()
    {
        var store = new OtpCodeStore(_db);
        var uow = new UnitOfWork(_db);
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<OtpService>.Instance;
        return new OtpService(store, _sms, _hasher, _clock, uow, logger, Microsoft.Extensions.Options.Options.Create(Options));
    }

    [Fact]
    public async Task RequestAsync_persists_code_so_VerifyAsync_with_correct_code_succeeds()
    {
        // Arrange — capture the plaintext code from the SMS message the service sends.
        var sut = CreateSut();
        string? sentMessage = null;
        await _sms.SendAsync(Phone, Arg.Do<string>(m => sentMessage = m), Arg.Any<CancellationToken>());

        // Act — request issues + commits a code.
        var requestResult = await sut.RequestAsync(Phone, ipAddress: null, CancellationToken.None);

        // Assert — request succeeded and the code row is durably committed.
        requestResult.IsSuccess.Should().BeTrue();
        (await _db.OtpCodes.AsNoTracking().CountAsync(o => o.Phone == Phone)).Should().Be(1);

        var code = ExtractCode(sentMessage);

        // Act — verify with the correct code on the SAME persisted state.
        var verifyResult = await sut.VerifyAsync(Phone, code, CancellationToken.None);

        // Assert — proves the code was persisted by RequestAsync and is single-use.
        verifyResult.IsSuccess.Should().BeTrue();
        (await _db.OtpCodes.AsNoTracking().FirstAsync(o => o.Phone == Phone)).Consumed.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyAsync_locks_out_after_MaxAttempts_wrong_codes_because_attempts_are_persisted()
    {
        // Arrange
        var sut = CreateSut();
        await sut.RequestAsync(Phone, ipAddress: null, CancellationToken.None);

        // Act — exhaust the allowed attempts with wrong codes. Each call returns the generic
        // "invalid" error, but the attempt counter must be persisted across the calls.
        for (var i = 0; i < Options.MaxAttempts; i++)
        {
            var wrong = await sut.VerifyAsync(Phone, "000000", CancellationToken.None);
            wrong.IsSuccess.Should().BeFalse();
            wrong.Error.Code.Should().Be("otp.invalid");
        }

        // The attempts were genuinely persisted (proves IncrementAttempts is committed).
        (await _db.OtpCodes.AsNoTracking().FirstAsync(o => o.Phone == Phone)).Attempts
            .Should().Be(Options.MaxAttempts);

        // Assert — the next verify is now locked out, NOT merely "invalid".
        var locked = await sut.VerifyAsync(Phone, "000000", CancellationToken.None);
        locked.IsSuccess.Should().BeFalse();
        locked.Error.Code.Should().Be("otp.locked");
    }

    private static string ExtractCode(string? message)
    {
        message.Should().NotBeNull("the OTP service must send the code via SMS");
        // The message also contains the TTL minutes; the code is the first 6-digit run.
        var match = System.Text.RegularExpressions.Regex.Match(message!, @"\b(\d{6})\b");
        match.Success.Should().BeTrue($"expected a 6-digit code in '{message}'");
        return match.Groups[1].Value;
    }

    private static AtriaDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AtriaDbContext>()
            .UseInMemoryDatabase($"otp-{Guid.NewGuid()}")
            .Options;
        return new AtriaDbContext(options, new NoOpEncryptionService());
    }

    /// <summary>No-op encryption so the InMemory context can build the KYC value converters.</summary>
    private sealed class NoOpEncryptionService : IEncryptionService
    {
        public string Encrypt(string plaintext) => plaintext;
        public string Decrypt(string ciphertext) => ciphertext;
    }
}
