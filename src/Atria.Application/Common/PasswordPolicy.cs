using FluentValidation;

namespace Atria.Application.Common;

/// <summary>
/// The password strength policy for credential accounts (Admin/Realtor/SuperAdmin): at least six
/// characters with an upper-case letter, a lower-case letter, a digit and a special character.
/// </summary>
/// <remarks>
/// Kept in one place because the same rule has to hold in three flows that are easy to let drift
/// apart: creating an admin, a super-admin password reset, and the forced change the account makes
/// on first sign-in. A rule enforced at only two of the three is not enforced at all.
/// </remarks>
public static class PasswordPolicy
{
    public const int MinLength = 6;

    /// <summary>Human-readable rule text; reused verbatim in the validation messages and the UI hint.</summary>
    public const string Description =
        "Пароль должен содержать минимум 6 символов, заглавную и строчную букву, цифру и спецсимвол.";

    public static IRuleBuilderOptions<T, string> ApplyPasswordPolicy<T>(
        this IRuleBuilder<T, string> rule)
        => rule
            .NotEmpty().WithMessage(Description)
            .MinimumLength(MinLength).WithMessage(Description)
            .MaximumLength(128)
            .Matches("[A-ZА-ЯЁ]").WithMessage(Description)
            .Matches("[a-zа-яё]").WithMessage(Description)
            .Matches("[0-9]").WithMessage(Description)
            .Matches(@"[^A-Za-zА-Яа-яЁё0-9]").WithMessage(Description);
}
