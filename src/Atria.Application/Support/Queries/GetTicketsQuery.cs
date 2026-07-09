using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Support.Dtos;

namespace Atria.Application.Support.Queries;

/// <summary>
/// Lists support tickets, scoped by the caller's role: an Investor sees only their own, an Admin
/// sees all. The optional <paramref name="Status"/> filter (open|pending|closed) is an admin extra.
/// Message threads are omitted on this route.
/// </summary>
public sealed record GetTicketsQuery(string? Status) : IRequest<Result<IReadOnlyList<TicketDto>>>;
