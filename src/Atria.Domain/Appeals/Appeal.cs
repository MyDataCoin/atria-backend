using Atria.Domain.Common;

namespace Atria.Domain.Appeals;

/// <summary>
/// A ban appeal submitted from the "you are blocked" screen by a banned admin/realtor who cannot log
/// in. Anonymous (no token): the account is referenced only by the <see cref="Username"/> they tried
/// to sign in with. Read by the super admin.
/// </summary>
public sealed class Appeal : AggregateRoot
{
    /// <summary>Max length of an appeal message.</summary>
    public const int MaxMessageLength = 4000;

    /// <summary>The username the sender tried to log in with (best-effort link to an account).</summary>
    public string Username { get; private set; } = null!;

    /// <summary>The appeal text.</summary>
    public string Message { get; private set; } = null!;

    // private ctor: creation only through the factory method
    private Appeal() { }

    /// <summary>Creates an appeal. Username may be empty (sender left it blank); message is required.</summary>
    public static Appeal Create(string? username, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new DomainException("Appeal message is required.");
        if (message.Length > MaxMessageLength)
            throw new DomainException($"Appeal message must be at most {MaxMessageLength} characters.");

        return new Appeal
        {
            Id = Guid.NewGuid(),
            Username = (username ?? string.Empty).Trim(),
            Message = message.Trim()
        };
    }
}
