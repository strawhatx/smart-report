using SmartReport.Core.Policy;
using SmartReport.Core.Reporting;

namespace SmartReport.Core.Abstractions;

/// <summary>One point the report makes, in the reader's terms.</summary>
public sealed record Finding(string Title, string Detail);

/// <summary>
/// Describes a finished report in prose.
///
/// <para>
/// Separate from <see cref="IReportPlanner"/> on purpose, because it is a different trust
/// question. The planner never sees data — it picks catalog keys. A narrator <i>does</i> see the
/// figures that came back, so that it can say "UPS Ground accounts for about half of your late
/// shipments" rather than restating the chart titles.
/// </para>
/// <para>
/// What it sees is bounded by what the report already contains: aggregated results the asker was
/// authorized for, never raw rows and never anything the policy engine withheld. It cannot widen
/// the report, because by the time it runs the queries have already been compiled, authorized and
/// executed — it is given the output, not the ability to ask for more.
/// </para>
/// </summary>
public interface IReportNarrator
{
    /// <summary>True when this narrator will actually produce anything.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// One line saying who wrote the summary and whether the figures left the machine.
    ///
    /// <para>
    /// Shown under the summary, always. This step is the only one that sends data anywhere, so
    /// the reader should not have to go and read a configuration file to find that out.
    /// </para>
    /// </summary>
    string Disclosure { get; }

    /// <summary>
    /// Returns an empty list rather than throwing when narration is unavailable: the report is
    /// the product, and prose about it is a bonus that must not be able to fail the request.
    /// </summary>
    Task<IReadOnlyList<Finding>> DescribeAsync(
        string question,
        ReportDocument document,
        Principal principal,
        CancellationToken cancellationToken);
}
