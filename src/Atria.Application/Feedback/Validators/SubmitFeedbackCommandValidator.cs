using Atria.Application.Feedback.Commands;
using Atria.Domain.Feedback;
using FluentValidation;

namespace Atria.Application.Feedback.Validators;

/// <summary>
/// Validates the public feedback form: имя, почта, телефон и текст вопроса — все обязательны.
/// </summary>
public sealed class SubmitFeedbackCommandValidator : AbstractValidator<SubmitFeedbackCommand>
{
    public SubmitFeedbackCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(FeedbackRequest.MaxFullNameLength);
        RuleFor(x => x.Message).NotEmpty().MaximumLength(FeedbackRequest.MaxMessageLength);

        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(FeedbackRequest.MaxEmailLength)
            .Must(LooksLikeEmail)
            .WithMessage("Укажите корректный e-mail.");

        RuleFor(x => x.Phone)
            .NotEmpty()
            .MaximumLength(FeedbackRequest.MaxPhoneLength)
            .Must(LooksLikePhone)
            .WithMessage("Укажите номер телефона, например +996 700 000 000.");
    }

    // Намеренно грубые проверки: строгая маска отсечёт живых людей с непривычным форматом записи,
    // а точность здесь не нужна — отвечает всё равно человек, глядя на оба контакта сразу.
    private static bool LooksLikeEmail(string email)
    {
        var value = (email ?? string.Empty).Trim();
        var at = value.IndexOf('@');
        return at > 0
            && at < value.Length - 1
            && value.IndexOf('.', at) > at + 1
            && !value.Contains(' ');
    }

    private static bool LooksLikePhone(string phone)
    {
        var value = (phone ?? string.Empty).Trim();
        if (value.Length == 0) return false;
        var digits = value.Count(char.IsDigit);
        return digits >= 9 && value.All(c => char.IsDigit(c) || "+()- ".Contains(c));
    }
}
