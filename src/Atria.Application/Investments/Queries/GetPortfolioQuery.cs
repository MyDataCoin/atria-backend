using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;

namespace Atria.Application.Investments.Queries;

/// <summary>Aggregated portfolio totals for the current investor.</summary>
public sealed record GetPortfolioQuery : IRequest<Result<PortfolioDto>>;
