using Atria.Application.Abstractions;
using Atria.Application.Auth.Commands;
using Atria.Domain.Users;
using Atria.Infrastructure.Configuration;
using Atria.Infrastructure.Identity;
using Atria.Infrastructure.Persistence;
using Atria.Infrastructure.Persistence.Stores;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Atria.Application.Tests.Auth;

/// <summary>
/// Regression coverage for refresh-token rotation persistence:
/// after a rotation through <see cref="RefreshTokenCommandHandler"/> (which delegates to the
/// internal AuthTokensFactory.IssueAsync), the brand-new refresh token must be COMMITTED so it
/// is actually found by <see cref="IRefreshTokenStore.FindAsync"/>, and it must be stored with
/// the refresh token's OWN lifetime (~RefreshTokenDays in the future) — NOT the much shorter
/// access-token TTL.
///
/// Exercised against a real <see cref="AtriaDbContext"/> (InMemory) + <see cref="UnitOfWork"/> +
/// <see cref="RefreshTokenStore"/> + real <see cref="JwtTokenGenerator"/>, so the issue-store-commit
/// path runs exactly as in production. The user repository is a substitute (an interface suffices).
/// </summary>
public sealed class RefreshTokenRotationPersistenceTests
{
    private static readonly JwtOptions Jwt = new()
    {
        Issuer = "atria-tests",
        Audience = "atria-tests",
        SigningKey = "this-is-a-32-char-minimum-signing-key!!",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 30
    };

    private readonly AtriaDbContext _db = CreateDbContext();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public RefreshTokenRotationPersistenceTests()
        => _clock.UtcNow.Returns(_ => DateTime.UtcNow);

    [Fact]
    public async Task Rotation_commits_new_refresh_token_so_it_is_found_and_has_refresh_lifetime()
    {
        // Arrange — a real active user and a real persisted "old" refresh token to rotate.
        var now = DateTime.UtcNow;
        var user = User.CreateFromPhone("+15551234567", Role.Investor);
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var store = new RefreshTokenStore(_db);
        var uow = new UnitOfWork(_db);
        const string oldToken = "old-refresh-token-value";
        await store.StoreAsync(user.Id, oldToken, now.AddDays(Jwt.RefreshTokenDays), CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);

        var jwt = new JwtTokenGenerator(Microsoft.Extensions.Options.Options.Create(Jwt), _clock);
        var sut = new RefreshTokenCommandHandler(_users, store, jwt, _clock, uow);

        // Act — rotate the old token into a fresh pair.
        var result = await sut.Handle(new RefreshTokenCommand(oldToken), CancellationToken.None);

        // Assert — rotation succeeded and returned a brand-new refresh token.
        result.IsSuccess.Should().BeTrue();
        var newToken = result.Value.RefreshToken;
        newToken.Should().NotBeNullOrEmpty().And.NotBe(oldToken);

        // The new token was actually COMMITTED — FindAsync (which hashes + queries the DB) finds it.
        var found = await store.FindAsync(newToken, CancellationToken.None);
        found.Should().NotBeNull("the rotated refresh token must be durable before being returned");
        found!.UserId.Should().Be(user.Id);
        found.IsRevoked.Should().BeFalse();

        // It is stored with the REFRESH lifetime (~30 days), NOT the 15-minute access TTL.
        var expectedRefreshExpiry = now.AddDays(Jwt.RefreshTokenDays);
        found.ExpiresAtUtc.Should().BeCloseTo(expectedRefreshExpiry, TimeSpan.FromMinutes(5));
        found.ExpiresAtUtc.Should().BeAfter(now.AddDays(Jwt.RefreshTokenDays - 1));
        // Guard against the bug where the access-token TTL leaked into the refresh expiry.
        found.ExpiresAtUtc.Should().BeAfter(now.AddMinutes(Jwt.AccessTokenMinutes + 1));

        // The old token was revoked as part of rotation.
        var oldInfo = await store.FindAsync(oldToken, CancellationToken.None);
        oldInfo!.IsRevoked.Should().BeTrue();
    }

    private static AtriaDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AtriaDbContext>()
            .UseInMemoryDatabase($"refresh-{Guid.NewGuid()}")
            .Options;
        return new AtriaDbContext(options, new NoOpEncryptionService());
    }

    private sealed class NoOpEncryptionService : IEncryptionService
    {
        public string Encrypt(string plaintext) => plaintext;
        public string Decrypt(string ciphertext) => ciphertext;
    }
}
