using Atria.Domain.Applications;
using Atria.Domain.Common;
using Atria.Domain.Kyc;

namespace Atria.Domain.Factories;

/// <summary>
/// Factory Method: guarantees a valid initial <see cref="InvestorApplication"/> by
/// enforcing creation invariants (approved KYC + positive amount) before producing a draft.
/// </summary>
public static class InvestorApplicationFactory
{
    public static InvestorApplication Create(
        Guid investorId, Guid propertyId, decimal amount, KycStatus investorKycStatus)
    {
        if (investorKycStatus != KycStatus.Approved)
            throw new DomainException("KYC must be approved before creating an investment application.");

        if (amount <= 0)
            throw new DomainException("Investment amount must be positive.");

        return InvestorApplication.CreateDraft(investorId, propertyId, amount);
    }
}
