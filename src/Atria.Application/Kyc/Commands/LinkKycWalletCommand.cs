using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Kyc.Commands;

/// <summary>
/// The current investor links their crypto wallet to their own KYC profile after
/// verification. The address is the token-allocation destination.
/// </summary>
public sealed record LinkKycWalletCommand(string WalletAddress) : IRequest<Result>;
