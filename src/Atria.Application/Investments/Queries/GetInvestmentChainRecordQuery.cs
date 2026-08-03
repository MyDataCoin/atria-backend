using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;

namespace Atria.Application.Investments.Queries;

/// <summary>
/// The on-chain record of one investment, as the holder sees it in their account (owner or Admin).
/// </summary>
public sealed record GetInvestmentChainRecordQuery(Guid InvestmentId)
    : IRequest<Result<InvestmentChainRecordDto>>;
