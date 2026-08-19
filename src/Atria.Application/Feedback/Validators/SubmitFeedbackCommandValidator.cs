using Atria.Application.Feedback.Commands;
using Atria.Domain.Feedback;
using Atria.Domain.Users;
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
            // Тот же разбор номера, что и при регистрации: платформа кыргызстанская, и телефон,
            // по которому нельзя перезвонить, ничем не лучше пустого поля.
            .Must(KyrgyzPhone.IsValid)
            .WithMessage($"Укажите номер в формате {KyrgyzPhone.Example}.");
    }

    // Проверка почты намеренно грубая: строгая маска отсекает живых людей с непривычным адресом,
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
}
