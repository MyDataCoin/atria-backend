using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;

namespace Atria.Application.Investments.Queries;

/// <summary>Lists every investment owned by the current investor.</summary>
public sealed record GetMyInvestmentsQuery : IRequest<Result<IReadOnlyList<InvestmentDto>>>;
