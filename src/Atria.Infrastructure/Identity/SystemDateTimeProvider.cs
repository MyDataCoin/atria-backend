using Atria.Application.Abstractions;

namespace Atria.Infrastructure.Identity;

/// <summary>Wall-clock implementation of <see cref="IDateTimeProvider"/> using UTC.</summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
