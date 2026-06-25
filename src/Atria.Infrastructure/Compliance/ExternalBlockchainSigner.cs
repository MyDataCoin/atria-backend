using System.Net.Http.Json;
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
            ChainId: request.ChainId ?? _options.ChainId,
            TokenContractAddress: _options.TokenContractAddress);

        var endpoint = new Uri(new Uri(_options.SignerUrl), "sign-and-submit");

        _logger.LogInformation(
            "Submitting {OperationType} to external signer on chain {ChainId}.",
            request.OperationType, body.ChainId);

        using var response = await _httpClient.PostAsJsonAsync(endpoint, body, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SignAndSubmitResponse>(cancellationToken: ct)
                     ?? throw new JsonException("External signer returned an empty response.");

        return new SigningResult(result.SignedPayload, result.SubmissionReference);
    }

    // Wire DTOs for the external signer contract.
    private sealed record SignAndSubmitRequest(
        string OperationType,
        string UnsignedPayload,
        string ChainId,
        string TokenContractAddress);

    private sealed record SignAndSubmitResponse(string SignedPayload, string? SubmissionReference);
}
