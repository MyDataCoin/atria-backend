using Atria.Domain.Common;
using Atria.Domain.Factories;
using Atria.Domain.Investments;
using Atria.Domain.Investments.Events;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

public sealed class InvestmentStateTests
{
    private static Investment NewPendingInvestment()
        => InvestmentFactory.CreateFromApprovedApplication(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1000m, "USD");

    [Fact]
    public void Factory_CreateFromApprovedApplication_ProducesPendingPaymentAndCreatedEvent()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        var investorId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();

        // Act
        var investment = InvestmentFactory.CreateFromApprovedApplication(
            applicationId, investorId, propertyId, 5000m, "USD");

        // Assert
        investment.Status.Should().Be(InvestmentStatus.PendingPayment);
        investment.Amount.Should().Be(5000m);
        investment.Currency.Should().Be("USD");
        investment.Payments.Should().BeEmpty();
        var created = investment.DomainEvents.OfType<InvestmentCreatedEvent>().Single();
        created.InvestmentId.Should().Be(investment.Id);
        created.ApplicationId.Should().Be(applicationId);
        created.InvestorId.Should().Be(investorId);
        created.PropertyId.Should().Be(propertyId);
        created.Amount.Should().Be(5000m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Factory_WhenAmountNotPositive_ThrowsDomainException(decimal amount)
    {
        // Act
        var act = () => InvestmentFactory.CreateFromApprovedApplication(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), amount, "USD");

        // Assert
        act.Should().Throw<DomainException>().WithMessage("*amount must be positive*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Factory_WhenCurrencyMissing_ThrowsDomainException(string currency)
    {
        // Act
        var act = () => InvestmentFactory.CreateFromApprovedApplication(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1000m, currency);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("*Currency is required*");
    }

    [Fact]
    public void ConfirmPayment_FromPending_ActivatesAndRaisesCompletionAndActivationEvents()
    {
        // Arrange
        var investment = NewPendingInvestment();

        // Act
        investment.ConfirmPayment(PaymentProviderType.Stripe, "pi_123", 1000m, "USD");

        // Assert
        investment.Status.Should().Be(InvestmentStatus.Active);
        investment.DomainEvents.Should().ContainSingle(e => e is PaymentCompletedEvent);
        investment.DomainEvents.Should().ContainSingle(e => e is InvestmentActivatedEvent);
    }

    [Fact]
    public void ConfirmPayment_FromPending_AddsCompletedPaymentTransaction()
    {
        // Arrange
        var investment = NewPendingInvestment();

        // Act
        investment.ConfirmPayment(PaymentProviderType.Stripe, "pi_123", 1000m, "USD");

        // Assert
        var payment = investment.Payments.Should().ContainSingle().Subject;
        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.Provider.Should().Be(PaymentProviderType.Stripe);
        payment.ExternalPaymentId.Should().Be("pi_123");
        payment.Amount.Should().Be(1000m);
        payment.Currency.Should().Be("USD");
        payment.InvestmentId.Should().Be(investment.Id);
    }

    [Fact]
    public void ConfirmPayment_CompletedEvent_CarriesExternalPaymentId()
    {
        // Arrange
        var investment = NewPendingInvestment();

        // Act
        investment.ConfirmPayment(PaymentProviderType.Stripe, "pi_xyz", 1000m, "USD");

        // Assert
        var completed = investment.DomainEvents.OfType<PaymentCompletedEvent>().Single();
        completed.InvestmentId.Should().Be(investment.Id);
        completed.InvestorId.Should().Be(investment.InvestorId);
        completed.Amount.Should().Be(1000m);
        completed.ExternalPaymentId.Should().Be("pi_xyz");
    }

    [Fact]
    public void ConfirmPayment_WhenAlreadyActive_Throws()
    {
        // Arrange
        var investment = NewPendingInvestment();
        investment.ConfirmPayment(PaymentProviderType.Stripe, "pi_123", 1000m, "USD");

        // Act
        var act = () => investment.ConfirmPayment(PaymentProviderType.Stripe, "pi_456", 1000m, "USD");

        // Assert
        act.Should().Throw<InvalidStateTransitionException>();
        investment.Status.Should().Be(InvestmentStatus.Active);
        investment.Payments.Should().ContainSingle();
    }

    [Fact]
    public void FailPayment_FromPending_SetsFailedAndRaisesPaymentFailedEvent()
    {
        // Arrange
        var investment = NewPendingInvestment();

        // Act
        investment.FailPayment(PaymentProviderType.Stripe, "card declined");

        // Assert
        investment.Status.Should().Be(InvestmentStatus.Failed);
        var failed = investment.DomainEvents.OfType<PaymentFailedEvent>().Single();
        failed.Reason.Should().Be("card declined");
        var payment = investment.Payments.Should().ContainSingle().Subject;
        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.FailureReason.Should().Be("card declined");
    }

    [Fact]
    public void FailPayment_WhenAlreadyFailed_Throws()
    {
        // Arrange
        var investment = NewPendingInvestment();
        investment.FailPayment(PaymentProviderType.Stripe, "declined");

        // Act
        var act = () => investment.FailPayment(PaymentProviderType.Stripe, "again");

        // Assert
        act.Should().Throw<InvalidStateTransitionException>();
        investment.Status.Should().Be(InvestmentStatus.Failed);
    }

    [Fact]
    public void ConfirmPayment_WhenFailed_Throws()
    {
        // Arrange
        var investment = NewPendingInvestment();
        investment.FailPayment(PaymentProviderType.Stripe, "declined");

        // Act
        var act = () => investment.ConfirmPayment(PaymentProviderType.Stripe, "pi_1", 1000m, "USD");

        // Assert
        act.Should().Throw<InvalidStateTransitionException>();
        investment.Status.Should().Be(InvestmentStatus.Failed);
    }
}
