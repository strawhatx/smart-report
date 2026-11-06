namespace SmartReport.Core.Reporting;

/// <summary>How a panel should be drawn.</summary>
public enum ChartKind
{
    /// <summary>Let the data decide. The default, and right most of the time.</summary>
    Auto,

    /// <summary>Ranked horizontal bars on a shared baseline. Valid for anything categorical.</summary>
    Bar,

    /// <summary>Vertical bars. The same comparison as <see cref="Bar"/>, read left to right.</summary>
    Column,

    /// <summary>A line over an ordered axis. Only meaningful when the grouping is time.</summary>
    Line,

    /// <summary>A line with the area beneath it washed in. Time only, and one series at a time.</summary>
    Area,

    /// <summary>One whole split into a few parts, with the total in the middle.</summary>
    Donut,

    /// <summary>No chart — just the figures.</summary>
    Table,
}

/// <summary>
/// Decides what a panel is actually drawn as.
///
/// <para>
/// A requested form is honoured when the data supports it and replaced when it would mislead —
/// a line implies that the points are ordered and that the space between them means something,
/// which is false for a list of carriers. When a request is overridden the reader is told why,
/// rather than quietly getting something they did not ask for.
/// </para>
/// </summary>
public static class ChartChoice
{
    /// <summary>Beyond this many slices a ring stops being readable and ranked bars win.</summary>
    public const int MaxDonutSegments = 6;

    public sealed record Resolution(ChartKind Kind, string? Note);

    public static Resolution Resolve(
        ChartKind requested, bool isTemporal, bool isPartToWhole, int points, int dimensions = 1)
    {
        if (points == 0)
        {
            return new Resolution(ChartKind.Table, null);
        }

        // Two groupings at once cannot go on one axis without flattening one of them into the
        // other — which produces a bar chart with every category repeated per period, and reads
        // as nonsense. The honest form is the figures. (A grouped line, one series per category,
        // would be the right chart, but it needs more colour slots than the palette validates.)
        if (dimensions > 1 && requested is not ChartKind.Table)
        {
            return new Resolution(ChartKind.Table,
                "Grouped by two things at once, so this is shown as figures: a single chart "
                + "would have to flatten one grouping into the other.");
        }

        return requested switch
        {
            ChartKind.Table => new Resolution(ChartKind.Table, null),

            ChartKind.Line when isTemporal => new Resolution(ChartKind.Line, null),
            ChartKind.Line => new Resolution(ChartKind.Bar,
                "Asked for a line, shown as bars: these groups have no order, so a line would "
                + "suggest a trend between them that does not exist."),

            ChartKind.Area when isTemporal => new Resolution(ChartKind.Area, null),
            ChartKind.Area => new Resolution(ChartKind.Bar,
                "Asked for an area chart, shown as bars: the area under a line only means "
                + "something when the axis is ordered."),

            // Vertical rather than horizontal is a presentation preference, not a claim about
            // the data, so it is always honoured.
            ChartKind.Column => new Resolution(ChartKind.Column, null),

            ChartKind.Donut when points <= MaxDonutSegments => new Resolution(ChartKind.Donut, null),
            ChartKind.Donut => new Resolution(ChartKind.Bar,
                $"Asked for a ring, shown as bars: {points} slices is too many to compare by angle."),

            ChartKind.Bar => new Resolution(ChartKind.Bar, null),

            // Auto: the form follows the job the data is doing.
            _ when isTemporal => new Resolution(ChartKind.Line, null),
            _ when isPartToWhole && points <= MaxDonutSegments => new Resolution(ChartKind.Donut, null),
            _ => new Resolution(ChartKind.Bar, null),
        };
    }
}
