using Atria.Application.Deals.Commands;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Atria.Application.Deals.Validators;

/// <summary>Validates <see cref="CreateDealCommand"/> inputs.</summary>
public sealed class CreateDealCommandValidator : AbstractValidator<CreateDealCommand>
{
    public CreateDealCommandValidator(IOptions<DealCommissionOptions> commission)
    {
        var max = commission.Value.MaxPercent;

        RuleFor(x => x.PropertyId).NotEmpty();

        // The realtor names their own commission and the deal settles by itself on the first
        // purchase through the link, so this bound is the only thing standing between a referral
        // link and a realtor booking the whole investment as commission. See DealCommissionOptions.
        RuleFor(x => x.CommissionPercent)
            .InclusiveBetween(0m, max)
            .WithMessage($"CommissionPercent must be between 0 and {max}.");
    }
}
