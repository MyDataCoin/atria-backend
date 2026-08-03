using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Asks for a property's offering to be published. Admin only. Returns the id of the dual-approval
/// request: the property opens when a second administrator approves it, not on this call.
/// </summary>
public sealed record PublishPropertyCommand(Guid Id) : IRequest<Result<Guid>>;
