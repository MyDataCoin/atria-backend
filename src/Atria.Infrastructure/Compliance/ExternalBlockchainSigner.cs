using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// <see cref="IBlockchainSigner"/> that delegates signing+submission to an EXTERNAL
/// signer/custody service (BlockchainOptions.SignerUrl). The private key NEVER lives
/// in this process or in config — we only build a request and read back the result.
/// </summary>
public sealed class ExternalBlockchainSigner : IBlockchainSigner
{
    private readonly HttpClient _httpClient;
    private readonly BlockchainOptions _options;
    private readonly ILogger<ExternalBlockchainSigner> _logger;

    public ExternalBlockchainSigner(
        HttpClient httpClient,
        IOptions<BlockchainOptions> options,
        ILogger<ExternalBlockchainSigner> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SigningResult> SignAndSubmitAsync(SigningRequest request, CancellationToken ct)
    {
        // Build the signing request for the external custody service. We send the
        // unsigned payload + chain context; the signer holds the key and submits.
        // NOTE: critical operations (allowlist/token ops) are designed for multisig
        // on the signer side — the policy/threshold is enforced there, not here.
        var body = new SignAndSubmitRequest(
            OperationType: request.OperationType,
            UnsignedPayload: request.UnsignedPayload,
            // Both come from the operation, which resolved them from the issue. There is no global
            // fallback on purpose: one would send an operation to the wrong contract silently.
            ChainId: request.ChainId
                     ?? throw new InvalidOperationException(
                         $"Operation {request.OperationType} has no chain id; the issue's network must be resolved before signing."),
            TokenContractAddress: request.TokenContractAddress);

        var endpoint = new Uri(new Uri(_options.SignerUrl), "sign-and-submit");

        _logger.LogInformation(
            "Submitting {OperationType} to external signer on chain {ChainId}.",
            request.OperationType, body.ChainId);

        // Serialize once: the bytes that are signed must be the bytes that are sent, or the
        // signature covers a different request than the one custody receives.
        var payloadJson = JsonSerializer.Serialize(body);

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };

        AuthenticateRequest(message, payloadJson);

        using var response = await _httpClient.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SignAndSubmitResponse>(cancellationToken: ct)
                     ?? throw new JsonException("External signer returned an empty response.");

        return new SigningResult(result.SignedPayload, result.SubmissionReference);
    }

    /// <summary>
    /// Signs the outgoing request so custody can tell a genuine instruction from anything else that
    /// happens to be able to reach it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sending an unauthenticated POST and relying on the signer being on a private network makes
    /// network placement the whole of the access control. It is not enough for an endpoint whose job
    /// is to create shares out of nothing: a compromised neighbouring pod or a server-side request
    /// forgery elsewhere in the cluster reaches it just as easily as we do.
    /// </para>
    /// <para>
    /// The scheme is HMAC-SHA256 over <c>timestamp.nonce.body</c>, sent alongside the timestamp and
    /// nonce. Covering the body means the recipient address and amount cannot be altered in flight;
    /// the timestamp lets custody bound replays, and the nonce lets it reject exact repeats inside
    /// that window.
    /// </para>
    /// </remarks>
    private void AuthenticateRequest(HttpRequestMessage message, string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(_options.SignerSharedSecret))
        {
            // Loud, because an unsigned mint instruction is not a normal operating state.
            _logger.LogWarning(
                "Blockchain:SignerSharedSecret is not configured — the request to custody is "
                + "unauthenticated and anything able to reach the signer can impersonate this API.");
            return;
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        var signature = Convert.ToHexString(HMACSHA256.HashData(
            Convert.FromBase64String(_options.SignerSharedSecret),
            Encoding.UTF8.GetBytes($"{timestamp}.{nonce}.{payloadJson}")));

        message.Headers.TryAddWithoutValidation("X-Atria-Timestamp", timestamp);
        message.Headers.TryAddWithoutValidation("X-Atria-Nonce", nonce);
        message.Headers.TryAddWithoutValidation("X-Atria-Signature", signature);
    }

    // Wire DTOs for the external signer contract.
    private sealed record SignAndSubmitRequest(
        string OperationType,
        string UnsignedPayload,
        string ChainId,
        string? TokenContractAddress);

    private sealed record SignAndSubmitResponse(string SignedPayload, string? SubmissionReference);
}
