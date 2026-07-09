using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Properties.Commands;

/// <summary>Completes a property's offering (Open -> Completed). Admin only.</summary>
public sealed record CompletePropertyCommand(Guid Id) : IRequest<Result>;
