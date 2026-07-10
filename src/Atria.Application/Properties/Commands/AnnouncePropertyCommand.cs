using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Properties.Commands;

/// <summary>Announces a property as "coming soon" (Draft -> ComingSoon). Admin only.</summary>
public sealed record AnnouncePropertyCommand(Guid Id) : IRequest<Result>;
