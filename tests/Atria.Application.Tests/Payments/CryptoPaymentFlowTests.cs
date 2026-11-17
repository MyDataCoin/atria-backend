using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Payments.Commands;
using Atria.Application.Payments.Services;
using Atria.Domain.Factories;
using Atria.Domain.Investments;
using Atria.Domain.Payments;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Payments;

public sealed class CryptoPaymentFlowTests
{
    private const string Bep20 = "0x1111111111111111111111111111111111111111";

    private readonly IInvestmentRepository _investments = Substitute.For<IInvestmentRepository>();
    private readonly ICryptoInvoiceRepository _invoices = Substitute.For<ICryptoInvoiceRepository>();
    private readonly ICryptoDepositRepository _deposits = Substitute.For<ICryptoDepositRepository>();
    private readonly IPaymentSettingsRepository _settings = Substitute.For<IPaymentSettingsRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly DateTime _now = new(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);

    public CryptoPaymentFlowTests()
    {
        _clock.UtcNow.Returns(_now);
        var settings = PaymentSettings.CreateEmpty();
        settings.Update(87.5m, Bep20, null, null, Guid.NewGuid());
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(settings);
        _invoices.GetPendingByInvestmentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CryptoInvoice>());
        _invoices.GetPendingAmountsAsync(Arg.Any<CryptoPaymentMethod>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<decimal>());
        _deposits.GetExistingKeysAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());
    }

    private Investment ReservedFor(Guid investorId)
    {
        var investment = InvestmentFactory.CreateForInvestor(
            investorId, Guid.NewGuid(), 100, "KGS", 100m, _now.AddDays(3), null);
        _investments.GetByIdAsync(investment.Id, Arg.Any<CancellationToken>()).Returns(investment);
        return investment;
    }

    private CreateCryptoInvoiceCommandHandler Sut() =>
        new(_investments, _invoices, _settings, _uow, _currentUser, _clock);

    [Fact]
    public async Task Investor_gets_an_invoice_priced_at_the_operator_rate()
    {
        var investorId = Guid.NewGuid();
        _currentUser.UserId.Returns(investorId);
        var investment = ReservedFor(investorId);

        var result = await Sut().Handle(new CreateCryptoInvoiceCommand(investment.Id, "usdt_bep20"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PayToAddress.Should().Be(Bep20);
        result.Value.RateKgs.Should().Be(87.5m);
        // 10 000 som / 87.5 = 114.2857… → 114.29, plus a fingerprint under one dollar.
        result.Value.PayAmount.Should().BeInRange(114.30m, 115.28m);
        result.Value.ExpiresAtUtc.Should().Be(investment.ReservedUntilUtc);
        await _invoices.Received(1).AddAsync(Arg.Any<CryptoInvoice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Someone_elses_application_reads_as_missing()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());
        var investment = ReservedFor(Guid.NewGuid());

        var result = await Sut().Handle(new CreateCryptoInvoiceCommand(investment.Id, "usdt_bep20"), CancellationToken.None);

        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task A_switched_off_method_is_refused()
    {
        var investorId = Guid.NewGuid();
        _currentUser.UserId.Returns(investorId);
        var investment = ReservedFor(investorId);

        var result = await Sut().Handle(new CreateCryptoInvoiceCommand(investment.Id, "usdt_trc20"), CancellationToken.None);

        result.Error.Code.Should().Be("payments.method_unavailable");
    }

    [Fact]
    public void Fingerprints_avoid_amounts_already_taken()
    {
        var taken = Enumerable.Range(1, 98).Select(c => 100m + c / 100m).ToHashSet();

        var picked = CreateCryptoInvoiceCommandHandler.PickFingerprint(100m, taken);

        picked.Should().NotBeNull();
        taken.Should().NotContain(100m + picked!.Value / 100m);
    }

    private CryptoDepositProcessor Processor() => new(_deposits, _invoices, _investments, _uow);

    [Fact]
    public async Task An_exact_transfer_settles_the_invoice()
    {
        var investment = ReservedFor(Guid.NewGuid());
        var invoice = CryptoInvoice.Issue(
            investment.Id, investment.InvestorId, CryptoPaymentMethod.UsdtBep20, Bep20, 10_000m, 87.5m, 37,
            investment.ReservedUntilUtc);
        _invoices.FindPendingByAmountAsync(CryptoPaymentMethod.UsdtBep20, Bep20, invoice.PayAmount, Arg.Any<CancellationToken>())
            .Returns(invoice);

        var outcome = await Processor().ProcessAsync(CryptoPaymentMethod.UsdtBep20, new[]
        {
            new IncomingTransfer("0xtx:0", "0xtx", "0xfrom", Bep20, invoice.PayAmount, _now)
        }, CancellationToken.None);

        outcome.Should().Be(new CryptoDepositProcessor.Outcome(1, 1));
        invoice.Status.Should().Be(CryptoInvoiceStatus.Paid);
        invoice.TransactionHash.Should().Be("0xtx");
        await _deposits.Received(1).AddAsync(
            Arg.Is<CryptoDeposit>(d => d.InvoiceId == invoice.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_transfer_that_matches_nothing_is_kept_for_the_operator()
    {
        var outcome = await Processor().ProcessAsync(CryptoPaymentMethod.UsdtBep20, new[]
        {
            new IncomingTransfer("0xtx:0", "0xtx", "0xfrom", Bep20, 50m, _now)
        }, CancellationToken.None);

        outcome.Should().Be(new CryptoDepositProcessor.Outcome(1, 0));
        await _deposits.Received(1).AddAsync(
            Arg.Is<CryptoDeposit>(d => d.InvoiceId == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_transfer_already_recorded_is_skipped()
    {
        _deposits.GetExistingKeysAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string> { "0xtx:0" });

        var outcome = await Processor().ProcessAsync(CryptoPaymentMethod.UsdtBep20, new[]
        {
            new IncomingTransfer("0xtx:0", "0xtx", "0xfrom", Bep20, 50m, _now)
        }, CancellationToken.None);

        outcome.Recorded.Should().Be(0);
        await _deposits.DidNotReceive().AddAsync(Arg.Any<CryptoDeposit>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_payment_for_an_application_no_longer_reserved_is_not_credited()
    {
        var investment = ReservedFor(Guid.NewGuid());
        investment.Cancel();
        var invoice = CryptoInvoice.Issue(
            investment.Id, investment.InvestorId, CryptoPaymentMethod.UsdtBep20, Bep20, 10_000m, 87.5m, 37,
            investment.ReservedUntilUtc);
        _invoices.FindPendingByAmountAsync(CryptoPaymentMethod.UsdtBep20, Bep20, invoice.PayAmount, Arg.Any<CancellationToken>())
            .Returns(invoice);

        var outcome = await Processor().ProcessAsync(CryptoPaymentMethod.UsdtBep20, new[]
        {
            new IncomingTransfer("0xtx:0", "0xtx", "0xfrom", Bep20, invoice.PayAmount, _now)
        }, CancellationToken.None);

        outcome.Matched.Should().Be(0);
        invoice.Status.Should().Be(CryptoInvoiceStatus.Pending);
    }
}
