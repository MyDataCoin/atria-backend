using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Compliance.Dtos;
using Atria.Domain.Compliance;
using Atria.Domain.Users;

namespace Atria.Application.Compliance.Queries;

/// <summary>
/// The on-chain operations queue as an operator reads it: what was queued, how far it got, and why
/// it stopped. Admin only.
/// </summary>
/// <remarks>
/// There is no manual mint anywhere in the platform, by design — shares exist only as a consequence
/// of an approved application, so the chain from application to token is auditable. The cost of that
/// is that an operator who approves an application has no direct view of what happens next. This
/// query is that view, and it is the only one: without it a stalled run is invisible until someone
/// reads the worker's logs.
/// </remarks>
/// <param name="Status">Narrow to one status: <c>created</c> | <c>submitted</c> | <c>confirmed</c> | <c>failed</c>. Omit for all.</param>
/// <param name="Limit">How many rows, newest first. Capped at 200.</param>
public sealed record ListBlockchainOperationsQuery(string? Status = null, int Limit = 50)
    : IRequest<Result<IReadOnlyList<BlockchainOperationDto>>>;

public sealed class ListBlockchainOperationsQueryHandler
    : IRequestHandler<ListBlockchainOperationsQuery, Result<IReadOnlyList<BlockchainOperationDto>>>
{
    /// <summary>Hard ceiling on a page, so a mistyped limit cannot pull the whole table.</summary>
    private const int MaxLimit = 200;

    private readonly IBlockchainOperationRepository _operations;
    private readonly IPropertyRepository _properties;
    private readonly IKycRepository _kyc;
    private readonly IChainNetworkResolver _networks;
    private readonly ICurrentUserService _currentUser;

    public ListBlockchainOperationsQueryHandler(
        IBlockchainOperationRepository operations,
        IPropertyRepository properties,
        IKycRepository kyc,
        IChainNetworkResolver networks,
        ICurrentUserService currentUser)
    {
        _operations = operations;
        _properties = properties;
        _kyc = kyc;
        _networks = networks;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<BlockchainOperationDto>>> Handle(
        ListBlockchainOperationsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure<IReadOnlyList<BlockchainOperationDto>>(
                Error.Unauthorized("operations.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure<IReadOnlyList<BlockchainOperationDto>>(
                Error.Forbidden("operations.forbidden", "Only administrators can read the operations queue."));

        BlockchainOperationStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!TryParseStatus(request.Status, out var parsed))
                return Result.Failure<IReadOnlyList<BlockchainOperationDto>>(Error.Validation(
                    "operations.unknownStatus",
                    "Status must be one of: created, submitted, confirmed, failed."));
            status = parsed;
        }

        var limit = Math.Clamp(request.Limit, 1, MaxLimit);
        var operations = await _operations.ListAsync(status, limit, ct);

        // Names are resolved once per distinct id rather than per row: a queue page is mostly the
        // same handful of issues, and a lookup per row would be a query storm on the busiest screen.
        var rows = operations.Select(o => (Operation: o, Payload: ReadPayload(o.Payload))).ToList();
        var propertyNames = await ResolvePropertyNamesAsync(rows.Select(r => r.Payload.PropertyId), ct);
        var investorNames = await ResolveInvestorNamesAsync(rows.Select(r => r.Payload.InvestorId), ct);

        var dtos = rows
            .Select(r => new BlockchainOperationDto(
                r.Operation.Id,
                ToWireType(r.Operation.Type),
                ToWireStatus(r.Operation.Status),
                r.Payload.PropertyId,
                r.Payload.PropertyId is { } pid ? propertyNames.GetValueOrDefault(pid) : null,
                r.Payload.InvestorId,
                r.Payload.InvestorId is { } iid ? investorNames.GetValueOrDefault(iid) : null,
                r.Payload.Wallet,
                r.Payload.TokenCount,
                r.Operation.TransactionRef,
                ExplorerUrlFor(r.Payload.Chain, r.Operation.TransactionRef),
                r.Operation.Confirmations,
                _networks.ConfirmationsRequired,
                r.Operation.Attempts,
                r.Operation.Error,
                r.Operation.CreatedAtUtc,
                r.Operation.ConfirmedAtUtc))
            .ToList();

        return Result.Success<IReadOnlyList<BlockchainOperationDto>>(dtos);
    }

    private async Task<Dictionary<Guid, string>> ResolvePropertyNamesAsync(
        IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var id in ids.OfType<Guid>().Distinct())
        {
            var property = await _properties.GetByIdAsync(id, ct);
            if (property is not null)
                names[id] = property.Name;
        }
        return names;
    }

    private async Task<Dictionary<Guid, string>> ResolveInvestorNamesAsync(
        IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var id in ids.OfType<Guid>().Distinct())
        {
            var profile = await _kyc.GetByUserIdAsync(id, ct);
            if (!string.IsNullOrWhiteSpace(profile?.FullName))
                names[id] = profile.FullName;
        }
        return names;
    }

    /// <summary>A deep link to the transaction, when the network has an explorer configured.</summary>
    private string? ExplorerUrlFor(string? chain, string? transactionRef)
    {
        if (string.IsNullOrWhiteSpace(chain) || string.IsNullOrWhiteSpace(transactionRef))
            return null;

        var explorer = _networks.Resolve(chain)?.ExplorerBaseUrl;
        return string.IsNullOrWhiteSpace(explorer)
            ? null
            : $"{explorer.TrimEnd('/')}/tx/{transactionRef}";
    }

    /// <summary>
    /// The fields the screen needs, pulled out of the operation's payload.
    /// </summary>
    /// <remarks>
    /// The payload is written by the handler that enqueued the operation and its shape differs per
    /// type, so every field is optional and a payload that cannot be parsed yields an empty record
    /// rather than throwing. An operations screen that 500s because one row holds unexpected JSON
    /// would be broken exactly when it is needed.
    /// </remarks>
    private static PayloadFacts ReadPayload(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind is not JsonValueKind.Object)
                return default;

            return new PayloadFacts(
                ReadGuid(root, "propertyId"),
                ReadGuid(root, "investorId"),
                ReadString(root, "wallet") ?? ReadString(root, "address"),
                ReadString(root, "chain"),
                root.TryGetProperty("tokenCount", out var tc) && tc.TryGetInt64(out var count)
                    ? count
                    : null);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static Guid? ReadGuid(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
           && value.ValueKind is JsonValueKind.String
           && Guid.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private readonly record struct PayloadFacts(
        Guid? PropertyId, Guid? InvestorId, string? Wallet, string? Chain, long? TokenCount);

    private static bool TryParseStatus(string wire, out BlockchainOperationStatus status)
    {
        switch (wire.Trim().ToLowerInvariant())
        {
            case "created": status = BlockchainOperationStatus.Created; return true;
            case "submitted": status = BlockchainOperationStatus.Submitted; return true;
            case "confirmed": status = BlockchainOperationStatus.Confirmed; return true;
            case "failed": status = BlockchainOperationStatus.Failed; return true;
            default: status = default; return false;
        }
    }

    private static string ToWireStatus(BlockchainOperationStatus status) => status switch
    {
        BlockchainOperationStatus.Created => "created",
        BlockchainOperationStatus.Submitted => "submitted",
        BlockchainOperationStatus.Confirmed => "confirmed",
        BlockchainOperationStatus.Failed => "failed",
        _ => status.ToString().ToLowerInvariant()
    };

    /// <summary>PascalCase enum name to snake_case, so the wire value survives a new member.</summary>
    private static string ToWireType(BlockchainOperationType type)
        => string.Concat(type.ToString().Select((c, i) =>
            char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
