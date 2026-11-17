using Atria.Domain.Common;
using Atria.Domain.Payments;
using FluentAssertions;

namespace Atria.Domain.Tests.Payments;

public sealed class CryptoPaymentDomainTests
{
    private const string Bep20 = "0x1111111111111111111111111111111111111111";
    private const string Trc20 = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";

    [Fact]
    public void Usdt_amount_is_rounded_up_to_the_cent_and_carries_the_fingerprint()
    {
        // 10 000 som at 87.4355 som/USDT = 114.3700... → 114.38, plus 0.37 fingerprint.
        var invoice = CryptoInvoice.Issue(
            Guid.NewGuid(), Guid.NewGuid(), CryptoPaymentMethod.UsdtBep20, Bep20,
            10_000m, 87.4355m, 37, DateTime.UtcNow.AddDays(3));

        invoice.PayAmount.Should().Be(114.38m + 0.37m);
        invoice.Status.Should().Be(CryptoInvoiceStatus.Pending);
    }

    [Fact]
    public void Kgst_is_one_to_one_with_the_som()
    {
        var invoice = CryptoInvoice.Issue(
            Guid.NewGuid(), Guid.NewGuid(), CryptoPaymentMethod.KgstBep20, Bep20,
            10_000m, 1m, 5, DateTime.UtcNow.AddDays(3));

        invoice.PayAmount.Should().Be(10_000.05m);
    }

    [Fact]
    public void A_paid_invoice_cannot_be_paid_again()
    {
        var invoice = CryptoInvoice.Issue(
            Guid.NewGuid(), Guid.NewGuid(), CryptoPaymentMethod.UsdtTrc20, Trc20,
            10_000m, 87m, 1, DateTime.UtcNow.AddDays(3));
        invoice.MarkPaid("tx1", "Tfrom", DateTime.UtcNow);

        var again = () => invoice.MarkPaid("tx2", "Tfrom", DateTime.UtcNow);

        again.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_method_is_offered_only_when_everything_it_needs_is_set()
    {
        var settings = PaymentSettings.CreateEmpty();
        settings.EnabledMethods().Should().BeEmpty();

        settings.Update(null, Bep20, Trc20, null, Guid.NewGuid());
        settings.EnabledMethods().Should().BeEmpty("USDT needs a rate and KGST needs its contract");

        settings.Update(87.5m, Bep20, null, null, Guid.NewGuid());
        settings.EnabledMethods().Should().Equal(CryptoPaymentMethod.UsdtBep20);

        settings.Update(87.5m, Bep20, Trc20, "0x2222222222222222222222222222222222222222", Guid.NewGuid());
        settings.EnabledMethods().Should().BeEquivalentTo(new[]
        {
            CryptoPaymentMethod.UsdtBep20, CryptoPaymentMethod.UsdtTrc20, CryptoPaymentMethod.KgstBep20
        });
    }

    [Theory]
    [InlineData("0x123", null)]
    [InlineData(null, "0x1111111111111111111111111111111111111111")]
    public void Malformed_wallets_are_refused(string? bep20, string? trc20)
    {
        var settings = PaymentSettings.CreateEmpty();

        var act = () => settings.Update(87m, bep20, trc20, null, Guid.NewGuid());

        act.Should().Throw<DomainException>();
    }
}
