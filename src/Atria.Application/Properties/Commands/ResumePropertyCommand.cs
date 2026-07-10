using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Properties.Commands;

/// <summary>Resumes purchases for a paused property (SalesPaused = false). Admin only.</summary>
public sealed record ResumePropertyCommand(Guid Id) : IRequest<Result>;
