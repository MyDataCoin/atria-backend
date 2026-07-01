using Atria.Application.Kyc.Commands;
using Atria.Domain.Compliance;
using FluentValidation;

namespace Atria.Application.Kyc.Validators;

/// <summary>Validates <see cref="LinkKycWalletCommand"/> — the wallet address is required and well-formed.</summary>
public sealed class LinkKycWalletCommandValidator : AbstractValidator<LinkKycWalletCommand>
{
    public LinkKycWalletCommandValidator()
    {
        // Same rule as SubmitKycCommandValidator; here the address is mandatory. It flows to the
        // on-chain allowlist + token allocation, so a malformed/wrong-chain string is rejected.
        RuleFor(x => x.WalletAddress)
            .NotEmpty().WithMessage("WalletAddress is required.")
            .Must(WalletAddress.IsValid)
            .WithMessage("WalletAddress must be a valid 0x-prefixed 40-hex-character address.")
            .When(x => !string.IsNullOrEmpty(x.WalletAddress));
    }
}
