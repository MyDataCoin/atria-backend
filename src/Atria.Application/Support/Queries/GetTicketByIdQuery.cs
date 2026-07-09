using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Support.Dtos;

namespace Atria.Application.Support.Queries;

/// <summary>Fetches a single ticket with its full message thread. Owner or Admin.</summary>
public sealed record GetTicketByIdQuery(Guid Id) : IRequest<Result<TicketDto>>;
