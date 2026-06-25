using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Auth.Commands;

/// <summary>Requests a one-time SMS code for phone-based registration / login.</summary>
public sealed record RequestPhoneOtpCommand(string Phone, string? IpAddress)
    : IRequest<Result>;
