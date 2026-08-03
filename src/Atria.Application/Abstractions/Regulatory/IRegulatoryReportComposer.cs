using Atria.Application.Common;
using Atria.Domain.Regulatory;

namespace Atria.Application.Abstractions;

/// <summary>The generated content of a notification, plus what it was drawn from.</summary>
/// <param name="ContentJson">The document, as JSON. Rendered to the filing format on export.</param>
/// <param name="HolderSnapshotId">
/// The frozen register the figures came from, where one applies — so what was filed can be traced
/// back to a snapshot rather than to live data that has since moved.
/// </param>
public sealed record ComposedReport(string ContentJson, Guid? HolderSnapshotId = null);

/// <summary>
/// Builds one kind of notification out of data the platform already holds. Composing is separate from
/// the obligation itself: the deadline exists whether or not the content can be produced yet, and a
/// report can be regenerated until it is filed.
/// </summary>
public interface IRegulatoryReportComposer
{
    RegulatoryReportKind Kind { get; }

    Task<Result<ComposedReport>> ComposeAsync(RegulatoryReport report, CancellationToken ct);
}
