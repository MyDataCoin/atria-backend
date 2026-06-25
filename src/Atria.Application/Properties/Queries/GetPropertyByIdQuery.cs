using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;

namespace Atria.Application.Properties.Queries;

/// <summary>Fetches a single property by id.</summary>
public sealed record GetPropertyByIdQuery(Guid Id) : IRequest<Result<PropertyDto>>;
