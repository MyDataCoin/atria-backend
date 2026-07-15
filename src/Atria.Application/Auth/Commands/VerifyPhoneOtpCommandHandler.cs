using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Auth.Commands;

/// <summary>
/// Verifies the OTP; on success, ensures an Investor account exists for the phone
/// (creating + marking it phone-verified on first login), then issues tokens.
/// </summary>
public sealed class VerifyPhoneOtpCommandHandler : IRequestHandler<VerifyPhoneOtpCommand, Result<AuthTokensDto>>
{
    private readonly IOtpService _otp;
    private readonly IUserRepository _users;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;

    public VerifyPhoneOtpCommandHandler(
        IOtpService otp,
        IUserRepository users,
        IJwtTokenGenerator jwt,
        IRefreshTokenStore refreshTokens,
        IUnitOfWork unitOfWork)
    {
        _otp = otp;
        _users = users;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AuthTokensDto>> Handle(VerifyPhoneOtpCommand request, CancellationToken ct)
    {
        // Same canonicalization as the request step so the stored code/user is found.
        var phone = KyrgyzPhone.Normalize(request.Phone);

        var verification = await _otp.VerifyAsync(phone, request.Code, ct);
        if (verification.IsFailure)
            return Result.Failure<AuthTokensDto>(verification.Error);

        var user = await _users.GetByPhoneAsync(phone, ct);
        if (user is null)
        {
            // First login from this number: create the Investor and mark it verified.
            user = User.CreateFromPhone(phone, Role.Investor);
            user.MarkPhoneVerified();
            await _users.AddAsync(user, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        else if (user.IsBanned)
        {
            // A banned account gets no token even with a valid OTP.
            return Result.Failure<AuthTokensDto>(
                Error.Forbidden("auth.account_banned", "This account has been blocked."));
        }
        else if (!user.IsPhoneVerified)
        {
            // Existing account that wasn't verified yet (e.g. created via another flow).
            user.MarkPhoneVerified();
            _users.Update(user);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        var tokens = await AuthTokensFactory.IssueAsync(user, _jwt, _refreshTokens, _unitOfWork, ct);
        return Result.Success(tokens);
    }
}
