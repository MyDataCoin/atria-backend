using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Kyc.Commands;

/// <summary>
/// Inbound provider webhook. The body is NEVER trusted as a command: the matching
/// Strategy verifies the signature and parses it; the parsed decision only moves
/// the profile's State.
/// </summary>
public sealed record HandleKycCallbackCommand(string Provider, WebhookPayload Payload)
    : IRequest<Result>;
