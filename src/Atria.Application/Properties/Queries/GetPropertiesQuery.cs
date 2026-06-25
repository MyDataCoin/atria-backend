using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;

namespace Atria.Application.Properties.Queries;

/// <summary>Lists all properties in the catalogue.</summary>
public sealed record GetPropertiesQuery : IRequest<Result<IReadOnlyList<PropertyDto>>>;
