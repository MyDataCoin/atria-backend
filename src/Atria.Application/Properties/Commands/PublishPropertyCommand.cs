using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Properties.Commands;

/// <summary>Publishes a property's offering, opening it to investors. Admin only.</summary>
public sealed record PublishPropertyCommand(Guid Id) : IRequest<Result>;
