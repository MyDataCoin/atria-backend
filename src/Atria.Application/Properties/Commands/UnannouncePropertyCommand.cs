using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Properties.Commands;

/// <summary>Reverses an announcement (ComingSoon -> Draft), hiding the property again. Admin only.</summary>
public sealed record UnannouncePropertyCommand(Guid Id) : IRequest<Result>;
