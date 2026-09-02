using System.ComponentModel.DataAnnotations;

namespace Atria.Application.Properties;

/// <summary>
/// How the placement sweep is paced: the background pass that opens an offering when its scheduled
/// start arrives and closes it when its end does.
/// </summary>
public sealed class PlacementSweepOptions
{
    public const string SectionName = "PlacementSweep";

    /// <summary>How often the sweep looks for placements due to open or close.</summary>
    [Range(1, 1440)]
    public int SweepIntervalMinutes { get; init; } = 5;

    /// <summary>
    /// Maximum issues moved per sweep, so a large backlog cannot block one unit of work. The rest are
    /// picked up on the next tick.
    /// </summary>
    [Range(1, 1000)]
    public int SweepBatchSize { get; init; } = 100;

    /// <summary>The sweep interval as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan SweepInterval => TimeSpan.FromMinutes(SweepIntervalMinutes);
}
