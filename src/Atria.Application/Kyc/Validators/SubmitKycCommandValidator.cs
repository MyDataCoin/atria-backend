using Atria.Application.Kyc.Commands;
using Atria.Domain.Compliance;
using Atria.Domain.Kyc;
using FluentValidation;

namespace Atria.Application.Kyc.Validators;

/// <summary>Validates <see cref="SubmitKycCommand"/> input shape.</summary>
public sealed class SubmitKycCommandValidator : AbstractValidator<SubmitKycCommand>
{
    public SubmitKycCommandValidator()
    {
        // Explicit allow-list, not IsInEnum(): an investor may only start a session with a real
        // external provider. KycProviderType.Manual is a back-office state (its "session id" is
        // the profile id and it has no signature), so letting a caller pick it hands them a
        // self-serve route into the manual review flow.
        RuleFor(x => x.Provider)
            .Must(p => p is KycProviderType.Didit or KycProviderType.SumSub)
            .WithMessage("Provider must be one of: Didit, SumSub.");

        // A wallet, if supplied, must be a well-formed address — it flows to the on-chain
        // allowlist + token allocation, so a malformed/wrong-chain string must be rejected here.
        RuleFor(x => x.WalletAddress!)
            .Must(WalletAddress.IsValid)
            .WithMessage("WalletAddress must be a valid 0x-prefixed 40-hex-character address.")
            .When(x => !string.IsNullOrEmpty(x.WalletAddress));

        RuleFor(x => x.FullName)
            .MaximumLength(256)
            .When(x => !string.IsNullOrEmpty(x.FullName));

        RuleFor(x => x.DocumentNumber)
            .MaximumLength(128)
            .When(x => !string.IsNullOrEmpty(x.DocumentNumber));

        RuleFor(x => x.Nationality)
            .MaximumLength(128)
            .When(x => !string.IsNullOrEmpty(x.Nationality));
    }
}
