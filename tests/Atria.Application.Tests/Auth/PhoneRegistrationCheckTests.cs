using Atria.Application.Abstractions;
using Atria.Application.Auth.Queries;
using Atria.Domain.Users;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Auth;

/// <summary>
/// Проверка «занят ли номер», по которой сайт разводит вход и регистрацию ещё до отправки кода.
/// Ответ обязан не зависеть от того, как человек набрал номер, и не содержать ничего, кроме факта.
/// </summary>
public sealed class PhoneRegistrationCheckTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private GetPhoneRegistrationQueryHandler Sut() => new(_users);

    [Fact]
    public async Task Unknown_number_is_not_registered()
    {
        _users.GetByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await Sut().Handle(
            new GetPhoneRegistrationQuery("+996770535395"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Registered.Should().BeFalse();
    }

    [Theory]
    [InlineData("+996770535395")]
    [InlineData("996770535395")]
    [InlineData("0770535395")]
    [InlineData("770 53 53 95")]
    public async Task The_answer_does_not_depend_on_how_the_number_was_typed(string typed)
    {
        // Репозиторий отвечает только на канонический вид — если бы запрос уходил как набрано,
        // «0770…» читалось бы как чужой номер и человека отправляли бы регистрироваться заново.
        _users.GetByPhoneAsync("+996770535395", Arg.Any<CancellationToken>())
            .Returns(User.CreateFromPhone("+996770535395", Role.Investor));

        var result = await Sut().Handle(new GetPhoneRegistrationQuery(typed), CancellationToken.None);

        result.Value.Registered.Should().BeTrue();
    }
}
