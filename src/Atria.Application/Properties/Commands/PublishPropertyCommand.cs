using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Publishes a property's offering, opening it to investors (Draft or ComingSoon -> Open).
/// Admin only, and it takes effect on this call.
/// </summary>
public sealed record PublishPropertyCommand(Guid Id) : IRequest<Result>;
