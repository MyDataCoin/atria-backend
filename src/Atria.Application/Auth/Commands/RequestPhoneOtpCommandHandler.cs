using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Auth.Commands;

/// <summary>Delegates rate-limiting, generation, hashing and sending to the OTP service.</summary>
public sealed class RequestPhoneOtpCommandHandler : IRequestHandler<RequestPhoneOtpCommand, Result>
{
    private readonly IOtpService _otp;

    public RequestPhoneOtpCommandHandler(IOtpService otp) => _otp = otp;

    public Task<Result> Handle(RequestPhoneOtpCommand request, CancellationToken ct)
        => _otp.RequestAsync(request.Phone.Trim(), request.IpAddress, ct);
}
