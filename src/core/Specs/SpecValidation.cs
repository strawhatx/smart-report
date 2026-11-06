namespace SmartReport.Core.Specs;

/// <summary>
/// Why a plan was rejected. Kinds rather than free text, so a caller can tell an unknown key
/// (the planner guessed) from a bad value (the question was unclear) without parsing English.
/// </summary>
public enum SpecProblemKind
{
    UnknownModel,
    UnknownMetric,
    UnknownDimension,
    UnknownFilterField,
    BadOperator,
    BadValue,
    OutOfBounds,
    Empty,
}

/// <param name="Path">
/// Where the problem is, e.g. <c>queries[0].metrics[1]</c>. Precise enough for the planner to
/// act on when the pipeline hands these back for a correction attempt.
/// </param>
/// <param name="Message">Written for the person reading the report, not for a log.</param>
public sealed record SpecProblem(SpecProblemKind Kind, string Path, string Message);

public sealed record SpecValidationResult(ReportSpec? Spec, IReadOnlyList<SpecProblem> Problems)
{
    public bool IsValid => Problems.Count == 0 && Spec is not null;

    public static SpecValidationResult Valid(ReportSpec spec) => new(spec, []);
    public static SpecValidationResult Invalid(IReadOnlyList<SpecProblem> problems) => new(null, problems);

    /// <summary>
    /// A short, user-safe explanation — and the text handed back to the planner on a retry.
    ///
    /// <para>
    /// Carries no schema detail beyond catalog keys (C5). Catalog keys are safe to show: they
    /// are the vocabulary the product is built on, not the physical column names.
    /// </para>
    /// </summary>
    public string Summary => Problems.Count == 0
        ? "Valid."
        : string.Join(" ", Problems.Select(p => p.Message));
}
