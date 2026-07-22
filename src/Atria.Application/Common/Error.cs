namespace Atria.Application.Common;

public enum ErrorType
{
    None = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Forbidden = 4,
    Unauthorized = 5,
    Failure = 6,
    ExternalService = 7
}

/// <summary>A structured, safe-to-return error. No internal details leak through this.</summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    /// <summary>Error code returned when a banned account tries to authenticate (403).</summary>
    public const string AccountBannedCode = "auth.account_banned";

    /// <summary>
    /// Generic message used for a banned account when no ban reason was recorded. The API treats a
    /// ban error whose message differs from this as the super-admin's reason (surfaced as banReason).
    /// </summary>
    public const string AccountBannedMessage = "Account is banned.";

    /// <summary>
    /// Builds the 403 error for a banned account, carrying the recorded ban reason as the message
    /// (or the generic fallback when none was set).
    /// </summary>
    public static Error AccountBanned(string? banReason)
        => Forbidden(AccountBannedCode, string.IsNullOrWhiteSpace(banReason) ? AccountBannedMessage : banReason);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);

    /// <summary>A downstream dependency (e.g. the SMS gateway) failed or rejected the request; maps to 502.</summary>
    public static Error ExternalService(string code, string message) => new(code, message, ErrorType.ExternalService);
}
