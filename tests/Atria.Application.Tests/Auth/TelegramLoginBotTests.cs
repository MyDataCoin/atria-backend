using System.Net;
using System.Text;
using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Users;
using Atria.Infrastructure.Notifications;
using Atria.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Atria.Application.Tests.Auth;

public sealed class TelegramLoginBotTests
{
    private const string Phone = "+996700123456";
    private const long UserId = 4242;

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true,"result":{}}""", Encoding.UTF8, "application/json")
            };
        }
    }

    private readonly AtriaDbContext _db = new(
        new DbContextOptionsBuilder<AtriaDbContext>().UseInMemoryDatabase($"tg-{Guid.NewGuid()}").Options,
        new NoOpEncryption());

    private readonly RecordingHandler _http = new();
    private readonly IOtpService _otp = Substitute.For<IOtpService>();

    private TelegramBotClient Client() => new(
        new HttpClient(_http),
        Options.Create(new TelegramBotOptions { Token = "t", Username = "atria_bot", WebhookSecret = "s" }));

    private TelegramBotUpdateHandler Handler() =>
        new(_db, Client(), _otp, NullLogger<TelegramBotUpdateHandler>.Instance);

    private static JsonElement ContactUpdate(long from, long contactOwner, string phone) => JsonDocument.Parse(
        "{\"message\":{\"chat\":{\"id\":" + from + ",\"type\":\"private\"},\"from\":{\"id\":" + from + "},"
        + "\"contact\":{\"phone_number\":\"" + phone + "\",\"user_id\":" + contactOwner + "}}}").RootElement;

    [Fact]
    public async Task Own_contact_links_the_phone_and_requests_a_code()
    {
        _otp.RequestAsync(Phone, null, Arg.Any<CancellationToken>()).Returns(Result.Success());

        await Handler().HandleAsync(ContactUpdate(UserId, UserId, "996700123456"), CancellationToken.None);

        var link = await _db.Set<TelegramLink>().SingleAsync();
        link.Phone.Should().Be(Phone);
        link.ChatId.Should().Be(UserId);
        await _otp.Received(1).RequestAsync(Phone, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Someone_elses_contact_links_nothing()
    {
        await Handler().HandleAsync(ContactUpdate(UserId, 9999, "996700123456"), CancellationToken.None);

        (await _db.Set<TelegramLink>().AnyAsync()).Should().BeFalse();
        await _otp.DidNotReceiveWithAnyArgs().RequestAsync(default!, default, default);
    }

    [Fact]
    public async Task An_unlinked_phone_asks_for_the_bot_instead_of_sending()
    {
        var sender = new TelegramBotOtpSender(_db, Client());

        var send = () => sender.SendCodeAsync(Phone, "123456", 5, CancellationToken.None);

        await send.Should().ThrowAsync<TelegramLinkRequiredException>();
        _http.Bodies.Should().BeEmpty();
    }

    [Fact]
    public async Task A_linked_phone_gets_the_code_in_its_chat()
    {
        _db.Set<TelegramLink>().Add(TelegramLink.Create(Phone, UserId, UserId));
        await _db.SaveChangesAsync();
        var sender = new TelegramBotOtpSender(_db, Client());

        await sender.SendCodeAsync(Phone, "123456", 5, CancellationToken.None);

        _http.Bodies.Should().ContainSingle().Which.Should().Contain("123456").And.Contain($"\"chat_id\":{UserId}");
    }

    [Fact]
    public void Webhook_secret_must_match_exactly()
    {
        TelegramWebhookSecret.Matches("s3cret", "s3cret").Should().BeTrue();
        TelegramWebhookSecret.Matches("s3cre", "s3cret").Should().BeFalse();
        TelegramWebhookSecret.Matches(null, "s3cret").Should().BeFalse();
    }

    private sealed class NoOpEncryption : IEncryptionService
    {
        public string Encrypt(string plaintext) => plaintext;
        public string Decrypt(string ciphertext) => ciphertext;
    }
}
