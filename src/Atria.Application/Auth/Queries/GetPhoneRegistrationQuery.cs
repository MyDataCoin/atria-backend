using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Auth.Queries;

/// <summary>Есть ли уже аккаунт на этом номере.</summary>
/// <param name="Registered">
/// true — номер занят, ему нужен вход; false — на него можно регистрироваться.
/// </param>
public sealed record PhoneRegistrationDto(bool Registered);

/// <summary>Проверка номера ДО отправки кода: та развилка, на которой человек выбирает дверь.</summary>
/// <remarks>
/// <para>
/// Это сознательный компромисс, и он стоит того, чтобы назвать его вслух: эндпоинт отвечает на
/// вопрос «есть ли у вас аккаунт на этот номер» кому угодно и без всякого кода. Перебором номеров
/// +996 (их порядка десяти миллионов) через него можно составить список тех, кто у нас
/// зарегистрирован. Раньше та же проверка стояла ПОСЛЕ ввода кода — и справочником не была, потому
/// что требовала владения телефоном.
/// </para>
/// <para>
/// Так решено ради интерфейса: человек, нажавший «Войти» с незнакомым номером, должен узнать об этом
/// сразу, а не после кода. Что осталось на защите: частотный лимит на этот путь (см. throttledPaths
/// в Program.cs), ответ ровно из одного булева поля — ни имени, ни статуса, ни даты, — и проверка
/// intent на самом verify-otp, которая держится независимо от этой.
/// </para>
/// </remarks>
public sealed record GetPhoneRegistrationQuery(string Phone) : IRequest<Result<PhoneRegistrationDto>>;

/// <summary>Отвечает по нормализованному номеру, забанен аккаунт или нет — не различает.</summary>
public sealed class GetPhoneRegistrationQueryHandler
    : IRequestHandler<GetPhoneRegistrationQuery, Result<PhoneRegistrationDto>>
{
    private readonly IUserRepository _users;

    public GetPhoneRegistrationQueryHandler(IUserRepository users) => _users = users;

    public async Task<Result<PhoneRegistrationDto>> Handle(
        GetPhoneRegistrationQuery request, CancellationToken ct)
    {
        // Та же канонизация, что и на запросе кода: иначе «0700…» и «+996700…» дали бы разные ответы
        // об одном и том же номере.
        var phone = KyrgyzPhone.Normalize(request.Phone);
        var user = await _users.GetByPhoneAsync(phone, ct);

        return Result.Success(new PhoneRegistrationDto(user is not null));
    }
}
