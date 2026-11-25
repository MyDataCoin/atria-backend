using System.Text.Json;
using Asp.Versioning;
using Atria.Infrastructure.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Atria.Api.Controllers;

/// <summary>The Telegram login bot: its webhook and the link the site sends people to.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/telegram")]
[AllowAnonymous]
public sealed class TelegramController : ControllerBase
{
    private const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    private readonly TelegramBotOptions _options;

    public TelegramController(IOptions<TelegramBotOptions> options) => _options = options.Value;

    /// <summary>Where to send someone for their login code: the bot's t.me link. 404 when the bot is off.</summary>
    [HttpGet("bot")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Bot()
        => _options.IsConfigured
            ? Ok(new { username = _options.Username!.TrimStart('@'), url = _options.BotUrl })
            : NotFound();

    /// <summary>Telegram's update delivery. Accepted only with the secret header set at registration.</summary>
    /// <remarks>
    /// Always answers 200 once the caller is authenticated: Telegram retries anything else, and a
    /// retry of a contact message would issue a second code.
    /// </remarks>
    [HttpPost("webhook")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Webhook(
        [FromBody] JsonElement update,
        [FromServices] TelegramBotUpdateHandler handler,
        [FromServices] ILogger<TelegramController> logger,
        CancellationToken ct)
    {
        if (!_options.IsConfigured
            || !TelegramWebhookSecret.Matches(Request.Headers[SecretHeader].ToString(), _options.WebhookSecret))
            return Unauthorized();

        try
        {
            await handler.HandleAsync(update, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Telegram update handling failed.");
        }

        return Ok();
    }
}
