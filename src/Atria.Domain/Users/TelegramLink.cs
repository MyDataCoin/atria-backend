using Atria.Domain.Common;

namespace Atria.Domain.Users;

/// <summary>
/// A phone number proven to belong to a Telegram account — the chat the login bot sends codes to.
/// </summary>
/// <remarks>
/// Created only from a contact the account shared about ITSELF (Telegram's contact carries the
/// owner's user id, which must equal the sender's), so a link cannot be made for someone else's
/// number. One link per phone: sharing the number from another account moves it there.
/// </remarks>
public sealed class TelegramLink : AggregateRoot
{
    public const int MaxPhoneLength = 20;

    public string Phone { get; private set; } = null!;
    public long ChatId { get; private set; }
    public long TelegramUserId { get; private set; }

    private TelegramLink() { }

    public static TelegramLink Create(string phone, long chatId, long telegramUserId)
    {
        if (string.IsNullOrWhiteSpace(phone))
            throw new DomainException("A phone number is required.");

        return new TelegramLink
        {
            Id = Guid.NewGuid(),
            Phone = phone,
            ChatId = chatId,
            TelegramUserId = telegramUserId
        };
    }

    public void MoveTo(long chatId, long telegramUserId)
    {
        ChatId = chatId;
        TelegramUserId = telegramUserId;
    }
}
