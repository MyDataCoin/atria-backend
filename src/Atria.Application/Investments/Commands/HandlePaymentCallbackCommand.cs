using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Investments.Commands;

/// <summary>
/// Inbound payment provider webhook. The body is never trusted as a command — the
/// matching Strategy verifies the signature, then it only moves the investment State.
/// </summary>
public sealed record HandlePaymentCallbackCommand(string Provider, WebhookPayload Payload)
    : IRequest<Result>;
