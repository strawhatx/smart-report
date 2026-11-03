namespace SmartReport.Core.Specs;

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

/// <param name="Path">Where the problem is, e.g. <c>queries[0].metrics[1]</c>.</param>
public sealed record SpecProblem(SpecProblemKind Kind, string Path, string Message);

public sealed record SpecValidationResult(ReportSpec? Spec, IReadOnlyList<SpecProblem> Problems)
{
    public bool IsValid => Problems.Count == 0 && Spec is not null;

    public static SpecValidationResult Valid(ReportSpec spec) => new(spec, []);
    public static SpecValidationResult Invalid(IReadOnlyList<SpecProblem> problems) => new(null, problems);

    /// <summary>A short, user-safe explanation. Carries no schema detail beyond catalog keys (C5).</summary>
    public string Summary => Problems.Count == 0
        ? "Valid."
        : string.Join(" ", Problems.Select(p => p.Message));
}
