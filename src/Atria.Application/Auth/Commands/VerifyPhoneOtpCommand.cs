using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;

namespace Atria.Application.Auth.Commands;

/// <summary>Which of the two doors the person came through.</summary>
/// <remarks>
/// One endpoint serves both, because both are the same act — prove you hold the number — and the
/// account is created on first use. But the two buttons promise different things: «Войти» to a number
/// nobody registered should say so instead of quietly creating an account, and «Регистрация» with a
/// number that already exists should send the person to sign in instead of pretending to enrol them
/// again. <see cref="Any"/> keeps the original create-or-sign-in behaviour for callers that do not
/// care (scripts, the integration suite, any client written before this existed).
/// </remarks>
public enum PhoneAuthIntent
{
    /// <summary>Create the account if it is missing, sign in if it exists.</summary>
    Any = 0,

    /// <summary>Sign in only: an unknown number is refused, and nothing is created.</summary>
    Login = 1,

    /// <summary>Register only: a number that already has an account is refused.</summary>
    Register = 2
}

/// <summary>Verifies the SMS code; creates the Investor account on first use and returns tokens.</summary>
public sealed record VerifyPhoneOtpCommand(
    string Phone, string Code, PhoneAuthIntent Intent = PhoneAuthIntent.Any)
    : IRequest<Result<AuthTokensDto>>;
