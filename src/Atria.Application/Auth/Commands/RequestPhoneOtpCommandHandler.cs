using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Auth.Commands;

/// <summary>Delegates rate-limiting, generation, hashing and sending to the OTP service.</summary>
public sealed class RequestPhoneOtpCommandHandler : IRequestHandler<RequestPhoneOtpCommand, Result>
{
    private readonly IOtpService _otp;

    public RequestPhoneOtpCommandHandler(IOtpService otp) => _otp = otp;

    public Task<Result> Handle(RequestPhoneOtpCommand request, CancellationToken ct)
        // Canonicalize to +996XXXXXXXXX so the OTP bucket/lookup is stable regardless of input form.
        => _otp.RequestAsync(KyrgyzPhone.Normalize(request.Phone), request.IpAddress, ct);
}
