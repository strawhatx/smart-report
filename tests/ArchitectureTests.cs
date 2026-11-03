using SmartReport.Core.Reporting;
using SmartReport.Core.Sql;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// Guards the layering the central claim rests on.
///
/// <para>
/// "The model never writes SQL" is only checkable because the layer that enforces it cannot reach
/// a database, an HTTP client, or a configuration file. That is a property of the project's
/// references, not of anyone's good intentions — so it is asserted here rather than trusted.
/// </para>
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] AllowedPrefixes =
        ["System.", "System", "netstandard", "mscorlib", "SmartReport.Core"];

    [Fact]
    public void Core_depends_on_nothing_but_the_base_class_library()
    {
        var referenced = typeof(SqlCompiler).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !AllowedPrefixes.Any(p => name.Equals(p, StringComparison.Ordinal)
                                                     || name.StartsWith(p, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(referenced.Length == 0,
            "src/core must stay free of third-party and project dependencies, because the "
            + "guarantee it enforces is only credible if it has nothing else to reach for. Found: "
            + string.Join(", ", referenced));
    }

    [Fact]
    public void The_pipeline_lives_in_core_so_the_order_of_the_steps_is_not_a_web_concern()
    {
        // Plan → validate → authorize → compile → execute is the guarantee. If this type ever
        // moves back into the web project, the order becomes presentation code.
        Assert.Equal(typeof(SqlCompiler).Assembly, typeof(ReportPipeline).Assembly);
    }

    [Fact]
    public void Core_knows_nothing_about_the_planner_implementations()
    {
        var coreTypes = typeof(SqlCompiler).Assembly.GetTypes().Select(t => t.FullName ?? "");

        Assert.DoesNotContain(coreTypes, name => name.Contains("Ollama", StringComparison.Ordinal));
        Assert.DoesNotContain(coreTypes, name => name.Contains("Scripted", StringComparison.Ordinal));
    }
}
