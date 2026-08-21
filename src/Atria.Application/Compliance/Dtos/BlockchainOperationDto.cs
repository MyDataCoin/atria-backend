namespace Atria.Application.Compliance.Dtos;

/// <summary>
/// One row of the on-chain operations queue, as an operator needs to read it.
/// </summary>
/// <remarks>
/// The queue is the only window onto what happens after an application is approved. Nobody presses
/// a mint button — shares appear as a consequence of approval — so when a run stalls, this row is
/// where it becomes visible. That is why it carries the failure text and the confirmation depth
/// rather than just a status: "Submitted" alone cannot distinguish a transaction three blocks deep
/// from one that never left the mempool.
/// </remarks>
/// <param name="Id">The operation.</param>
/// <param name="Type">What it does, lowercase: <c>allowlist_add</c> | <c>allowlist_remove</c> | <c>token_allocation</c> | <c>anchor_merkle_root</c> | <c>token_pause</c> | <c>token_unpause</c> | <c>token_burn</c> | <c>token_forced_transfer</c> | <c>collateral_report</c>.</param>
/// <param name="Status">Lowercase lifecycle: <c>created</c> | <c>submitted</c> | <c>confirmed</c> | <c>failed</c>.</param>
/// <param name="PropertyId">Issue the operation belongs to, read out of the payload; <c>null</c> when it carries none.</param>
/// <param name="PropertyName">Name of that issue, or <c>null</c> when it could not be resolved.</param>
/// <param name="InvestorId">Investor the operation is for; <c>null</c> for issue-wide operations.</param>
/// <param name="InvestorName">That investor's full name from KYC, or <c>null</c> when unresolved.</param>
/// <param name="WalletAddress">Address the operation targets; <c>null</c> when it carries none.</param>
/// <param name="TokenCount">Whole shares the operation moves; <c>null</c> when it moves none.</param>
/// <param name="TransactionRef">Transaction hash once submitted; <c>null</c> before that.</param>
/// <param name="ExplorerUrl">Ready-made link to <paramref name="TransactionRef"/> in the network's explorer, or <c>null</c> when there is no hash or no explorer configured.</param>
/// <param name="Confirmations">Confirmations observed at the last reconciliation pass; <c>null</c> before any.</param>
/// <param name="ConfirmationsRequired">Depth the worker waits for before calling it confirmed.</param>
/// <param name="Attempts">How many times submission has been tried.</param>
/// <param name="Error">Why it failed, verbatim; <c>null</c> unless the status is <c>failed</c>.</param>
/// <param name="CreatedAtUtc">When it was queued.</param>
/// <param name="ConfirmedAtUtc">When it settled; <c>null</c> until then.</param>
public sealed record BlockchainOperationDto(
    Guid Id,
    string Type,
    string Status,
    Guid? PropertyId,
    string? PropertyName,
    Guid? InvestorId,
    string? InvestorName,
    string? WalletAddress,
    long? TokenCount,
    string? TransactionRef,
    string? ExplorerUrl,
    long? Confirmations,
    int ConfirmationsRequired,
    int Attempts,
    string? Error,
    DateTime CreatedAtUtc,
    DateTime? ConfirmedAtUtc);
