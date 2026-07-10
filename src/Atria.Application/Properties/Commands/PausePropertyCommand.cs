using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Properties.Commands;

/// <summary>Pauses new purchases for a property (SalesPaused = true). Admin only.</summary>
public sealed record PausePropertyCommand(Guid Id) : IRequest<Result>;
