using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atria.Application.Abstractions;
using Atria.Domain.Users;
using Atria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Notifications;

/// <summary>Login bot settings (section <c>TelegramBot</c>). All three secrets come from the server .env.</summary>
public sealed class TelegramBotOptions
{
    public const string SectionName = "TelegramBot";

    /// <summary>Token from @BotFather.</summary>
    public string? Token { get; init; }

    /// <summary>The bot's @username, without the @ — the site links people to it.</summary>
    public string? Username { get; init; }

    /// <summary>Random string Telegram echoes in a header so the webhook knows the call is Telegram's.</summary>
    public string? WebhookSecret { get; init; }

    /// <summary>Public origin of this API, where Telegram delivers updates.</summary>
    public string PublicBaseUrl { get; init; } = "https://api.atria.kg";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Token)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(WebhookSecret);

    public string BotUrl => $"https://t.me/{Username?.TrimStart('@')}";
}

/// <summary>The phone has no Telegram chat yet: the person must open the bot and share the number first.</summary>
public sealed class TelegramLinkRequiredException : Exception
{
    public TelegramLinkRequiredException() : base("The phone is not linked to the login bot yet.") { }
}

/// <summary>Minimal Bot API client: just what the login bot sends.</summary>
public sealed class TelegramBotClient
{
    private readonly HttpClient _http;
    private readonly TelegramBotOptions _options;

    public TelegramBotClient(HttpClient http, IOptions<TelegramBotOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public Task SendMessageAsync(long chatId, string html, object? replyMarkup, CancellationToken ct)
        => CallAsync("sendMessage", new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["text"] = html,
            ["parse_mode"] = "HTML",
            ["reply_markup"] = replyMarkup,
        }, ct);

    public Task SetWebhookAsync(string url, string secret, CancellationToken ct)
        => CallAsync("setWebhook", new Dictionary<string, object?>
        {
            ["url"] = url,
            ["secret_token"] = secret,
            ["allowed_updates"] = new[] { "message" },
        }, ct);

    private async Task CallAsync(string method, Dictionary<string, object?> body, CancellationToken ct)
    {
        var payload = body.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);
        using var response = await _http.PostAsJsonAsync(
            $"https://api.telegram.org/bot{_options.Token}/{method}", payload, ct);

        if (!response.IsSuccessStatusCode)
        {
            // The URL carries the token, so never include it; the description is Telegram's own text.
            var description = await response.Content.ReadAsStringAsync(ct);
            throw new SmsGatewayException((int)response.StatusCode, $"Telegram Bot API {method} failed: {Trim(description)}");
        }
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;
}

/// <summary>Sends login codes to the chat linked to the phone; refuses (link required) when there is none.</summary>
public sealed class TelegramBotOtpSender : IOtpCodeSender
{
    private readonly AtriaDbContext _db;
    private readonly TelegramBotClient _bot;

    public TelegramBotOtpSender(AtriaDbContext db, TelegramBotClient bot)
    {
        _db = db;
        _bot = bot;
    }

    public async Task SendCodeAsync(string phone, string code, int ttlMinutes, CancellationToken ct)
    {
        var link = await _db.Set<TelegramLink>().AsNoTracking().FirstOrDefaultAsync(l => l.Phone == phone, ct)
                   ?? throw new TelegramLinkRequiredException();

        await _bot.SendMessageAsync(
            link.ChatId,
            $"Код входа в ATRIA: <b>{code}</b>\nДействует {ttlMinutes} мин. Никому его не сообщайте — сотрудники ATRIA код не спрашивают.",
            null,
            ct);
    }
}

/// <summary>
/// Handles the login bot's incoming messages: greets, asks for the number, links a shared contact
/// and sends the code right away.
/// </summary>
public sealed class TelegramBotUpdateHandler
{
    private static readonly object ShareNumberKeyboard = new
    {
        keyboard = new[] { new[] { new { text = "Поделиться номером", request_contact = true } } },
        resize_keyboard = true,
        one_time_keyboard = true,
    };

    private static readonly object RemoveKeyboard = new { remove_keyboard = true };

    private readonly AtriaDbContext _db;
    private readonly TelegramBotClient _bot;
    private readonly IOtpService _otp;
    private readonly ILogger<TelegramBotUpdateHandler> _logger;

    public TelegramBotUpdateHandler(
        AtriaDbContext db, TelegramBotClient bot, IOtpService otp, ILogger<TelegramBotUpdateHandler> logger)
    {
        _db = db;
        _bot = bot;
        _otp = otp;
        _logger = logger;
    }

    public async Task HandleAsync(JsonElement update, CancellationToken ct)
    {
        if (!update.TryGetProperty("message", out var message)
            || !message.TryGetProperty("chat", out var chat)
            || !message.TryGetProperty("from", out var from))
            return;

        var chatId = chat.GetProperty("id").GetInt64();
        var fromId = from.GetProperty("id").GetInt64();

        // Codes go to private chats only — never into a group someone added the bot to.
        if (chat.TryGetProperty("type", out var type) && type.GetString() != "private")
            return;

        if (!message.TryGetProperty("contact", out var contact))
        {
            await _bot.SendMessageAsync(chatId,
                "Здравствуйте! Это бот входа в <b>ATRIA</b>.\n\nНажмите «Поделиться номером» внизу — пришлю код для входа на сайт.",
                ShareNumberKeyboard, ct);
            return;
        }

        // Telegram puts the contact owner's id on the contact. Only a number shared about oneself
        // proves ownership; a forwarded or typed-in contact of someone else does not.
        var ownerId = contact.TryGetProperty("user_id", out var uid) ? uid.GetInt64() : 0;
        if (ownerId != fromId)
        {
            await _bot.SendMessageAsync(chatId,
                "Нужен <b>ваш</b> номер — нажмите кнопку «Поделиться номером» внизу.", ShareNumberKeyboard, ct);
            return;
        }

        var raw = contact.GetProperty("phone_number").GetString() ?? string.Empty;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (!PhoneNumbers.TryNormalize("+" + digits, out var phone))
        {
            await _bot.SendMessageAsync(chatId,
                "Не удалось распознать номер. Напишите нам на atria.kg — разберёмся.", RemoveKeyboard, ct);
            return;
        }

        var link = await _db.Set<TelegramLink>().FirstOrDefaultAsync(l => l.Phone == phone, ct);
        if (link is null)
            await _db.Set<TelegramLink>().AddAsync(TelegramLink.Create(phone, chatId, fromId), ct);
        else
            link.MoveTo(chatId, fromId);
        await _db.SaveChangesAsync(ct);

        await _bot.SendMessageAsync(chatId, "Номер подтверждён. Отправляю код…", RemoveKeyboard, ct);

        var result = await _otp.RequestAsync(phone, null, ct);
        if (result.IsFailure)
        {
            _logger.LogInformation("Bot code request refused: {Code}.", result.Error.Code);
            await _bot.SendMessageAsync(chatId,
                result.Error.Code == "otp.rate_limited"
                    ? "Слишком много кодов за час. Попробуйте чуть позже."
                    : "Не получилось отправить код. Попробуйте ещё раз через минуту.",
                null, ct);
        }
    }
}

/// <summary>Points the bot's webhook at this API on startup — idempotent, so every deploy re-asserts it.</summary>
public sealed class TelegramBotWebhookRegistrar : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelegramBotOptions _options;
    private readonly ILogger<TelegramBotWebhookRegistrar> _logger;

    public TelegramBotWebhookRegistrar(
        IServiceScopeFactory scopeFactory, IOptions<TelegramBotOptions> options, ILogger<TelegramBotWebhookRegistrar> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsConfigured)
            return;

        for (var attempt = 1; attempt <= 5 && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var bot = scope.ServiceProvider.GetRequiredService<TelegramBotClient>();
                await bot.SetWebhookAsync(
                    $"{_options.PublicBaseUrl.TrimEnd('/')}/api/v1/telegram/webhook", _options.WebhookSecret!, stoppingToken);
                _logger.LogInformation("Telegram login bot webhook registered.");
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Registering the Telegram bot webhook failed (attempt {Attempt}).", attempt);
                await Task.Delay(TimeSpan.FromSeconds(10 * attempt), stoppingToken);
            }
        }
    }
}

public static class TelegramWebhookSecret
{
    /// <summary>Constant-time comparison of the header Telegram sent against the configured secret.</summary>
    public static bool Matches(string? presented, string? expected)
        => !string.IsNullOrEmpty(presented) && !string.IsNullOrEmpty(expected)
           && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected));
}
