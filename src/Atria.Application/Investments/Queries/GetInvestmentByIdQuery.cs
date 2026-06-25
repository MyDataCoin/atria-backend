using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;

namespace Atria.Application.Investments.Queries;

/// <summary>Fetches a single investment by id (owner or Admin only).</summary>
public sealed record GetInvestmentByIdQuery(Guid Id) : IRequest<Result<InvestmentDto>>;
