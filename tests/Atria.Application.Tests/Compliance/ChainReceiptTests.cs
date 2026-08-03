using Atria.Application.Abstractions;
using FluentAssertions;

namespace Atria.Application.Tests.Compliance;

/// <summary>
/// The three outcomes the reconciliation distinguishes. Collapsing any two of them is how a share
/// ends up counted in the registry without existing on chain.
/// </summary>
public sealed class ChainReceiptTests
{
    private const int Required = 15;

    private static bool IsSettled(ChainReceipt? receipt)
        => receipt is { Succeeded: true } && receipt.Confirmations >= Required;

    private static bool HasFailed(ChainReceipt? receipt)
        => receipt is { Succeeded: false };

    [Fact]
    public void No_receipt_means_not_mined_yet_and_settles_nothing()
    {
        IsSettled(null).Should().BeFalse();
        HasFailed(null).Should().BeFalse("an absent receipt is silence, not a verdict");
    }

    [Fact]
    public void A_reverted_transaction_is_a_failure_however_deeply_it_is_buried()
    {
        var reverted = new ChainReceipt(Succeeded: false, BlockNumber: 100, Confirmations: 5_000);

        HasFailed(reverted).Should().BeTrue();
        IsSettled(reverted).Should().BeFalse("it consumed gas and changed nothing");
    }

    [Fact]
    public void A_mined_transaction_is_not_settled_until_it_is_deep_enough()
    {
        var justMined = new ChainReceipt(Succeeded: true, BlockNumber: 100, Confirmations: 1);
        var nearlyThere = new ChainReceipt(Succeeded: true, BlockNumber: 100, Confirmations: Required - 1);

        IsSettled(justMined).Should().BeFalse("a single block can be undone by a reorg");
        IsSettled(nearlyThere).Should().BeFalse();
    }

    [Fact]
    public void At_the_required_depth_it_is_settled()
    {
        var settled = new ChainReceipt(Succeeded: true, BlockNumber: 100, Confirmations: Required);

        IsSettled(settled).Should().BeTrue();
    }
}
