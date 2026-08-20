using Atria.Application.Abstractions;
using Atria.Application.Auth.Commands;
using Atria.Application.Common;
using Atria.Domain.Users;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Auth;

/// <summary>
/// Один эндпоинт обслуживает и вход, и регистрацию, но кнопки обещают разное. «Войти» с номером,
/// которого нет в базе, не должен молча заводить аккаунт, а «Регистрация» с существующим номером —
/// делать вид, что записывает человека второй раз. Здесь проверяется именно это различие; без
/// intent поведение обязано остаться прежним (создать при первом обращении).
/// </summary>
public sealed class PhoneAuthIntentTests
{
    private const string Phone = "+996770535395";
    private const string Code = "111111";

    private readonly IOtpService _otp = Substitute.For<IOtpService>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IJwtTokenGenerator _jwt = Substitute.For<IJwtTokenGenerator>();
    private readonly IRefreshTokenStore _refreshTokens = Substitute.For<IRefreshTokenStore>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public PhoneAuthIntentTests()
    {
        _otp.VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jwt.GenerateAccessToken(Arg.Any<Guid>(), Arg.Any<Role>(), Arg.Any<string>())
            .Returns(new AccessToken("access", DateTime.UtcNow.AddMinutes(15)));
        _jwt.GenerateRefreshToken().Returns(new GeneratedRefreshToken("refresh", DateTime.UtcNow.AddDays(30)));
    }

    private VerifyPhoneOtpCommandHandler Sut()
        => new(_otp, _users, _jwt, _refreshTokens, _unitOfWork);

    private void ExistingUser()
    {
        var user = User.CreateFromPhone(Phone, Role.Investor);
        user.MarkPhoneVerified();
        _users.GetByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);
    }

    [Fact]
    public async Task Login_with_an_unknown_number_is_refused_and_creates_nothing()
    {
        _users.GetByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await Sut().Handle(
            new VerifyPhoneOtpCommand(Phone, Code, PhoneAuthIntent.Login), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("auth.phone_not_registered");
        result.Error.Type.Should().Be(ErrorType.NotFound);
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Registration_with_an_existing_number_is_refused()
    {
        ExistingUser();

        var result = await Sut().Handle(
            new VerifyPhoneOtpCommand(Phone, Code, PhoneAuthIntent.Register), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("auth.phone_already_registered");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Login_with_a_known_number_still_signs_in()
    {
        ExistingUser();

        var result = await Sut().Handle(
            new VerifyPhoneOtpCommand(Phone, Code, PhoneAuthIntent.Login), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Registration_with_a_new_number_creates_the_account()
    {
        _users.GetByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await Sut().Handle(
            new VerifyPhoneOtpCommand(Phone, Code, PhoneAuthIntent.Register), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _users.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Без intent — прежнее поведение: неизвестный номер регистрируется на месте.</summary>
    [Fact]
    public async Task Without_an_intent_the_original_create_or_sign_in_behaviour_holds()
    {
        _users.GetByPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await Sut().Handle(new VerifyPhoneOtpCommand(Phone, Code), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _users.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }
}
