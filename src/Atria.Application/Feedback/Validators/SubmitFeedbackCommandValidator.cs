using Atria.Application.Feedback.Commands;
using Atria.Domain.Feedback;
using FluentValidation;

namespace Atria.Application.Feedback.Validators;

/// <summary>
/// Validates the public feedback form. The contact must look like an email or a phone number —
/// a question we cannot answer is worse than no question, and this is the only reason the field exists.
/// </summary>
public sealed class SubmitFeedbackCommandValidator : AbstractValidator<SubmitFeedbackCommand>
{
    public SubmitFeedbackCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(FeedbackRequest.MaxFullNameLength);
        RuleFor(x => x.Message).NotEmpty().MaximumLength(FeedbackRequest.MaxMessageLength);
        RuleFor(x => x.Contact)
            .NotEmpty()
            .MaximumLength(FeedbackRequest.MaxContactLength)
            .Must(LooksLikeEmailOrPhone)
            .WithMessage("Укажите e-mail или номер телефона, чтобы мы могли ответить.");
    }

    // Намеренно грубая проверка: строгая маска телефона отсечёт живых людей с непривычным
    // форматом, а точность здесь не нужна — отвечает всё равно человек.
    private static bool LooksLikeEmailOrPhone(string contact)
    {
        if (string.IsNullOrWhiteSpace(contact)) return false;
        var value = contact.Trim();

        var atIndex = value.IndexOf('@');
        var looksLikeEmail = atIndex > 0
            && atIndex < value.Length - 1
            && value.IndexOf('.', atIndex) > atIndex + 1
            && !value.Contains(' ');

        var digits = value.Count(char.IsDigit);
        var looksLikePhone = digits >= 9 && value.All(c => char.IsDigit(c) || "+()- ".Contains(c));

        return looksLikeEmail || looksLikePhone;
    }
}
