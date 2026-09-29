using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Shouldly;

namespace Antiphon.Tests.Scripts;

internal static class ScriptHarness
{
    internal static Task RunHarnessCaseAsync(string harness, string prefix, string caseName, int expectedRows, params string[] requiredRows) =>
        RunHarnessCaseAsync(harness, prefix, caseName, expectedRows, requiredRows,
            ScriptHarnessOptions.Default, CancellationToken.None);

    internal static Task RunHarnessCaseAsync(string harness, string prefix, string caseName, int expectedRows,
        IReadOnlyList<string> requiredRows, ScriptHarnessOptions options, CancellationToken cancellationToken)
    {
        var script = options.ScriptPath ?? Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", harness);
        return ScriptHarnessProcess.RunAsync(harness, prefix, caseName, script, options, cancellationToken,
            result => Validate(result, prefix, caseName, expectedRows, requiredRows));
    }

    private static void Validate(ScriptHarnessResult result, string prefix, string caseName, int expectedRows,
        IReadOnlyList<string> requiredRows)
    {
        var output = result.Stdout + result.Stderr;
        result.ExitCode.ShouldBe(0, output);
        output.ShouldContain("C487 HARNESS EXIT CODE: 0", Case.Sensitive, output);
        var lines = output.ReplaceLineEndings("\n").Split('\n');
        lines.ShouldNotContain(line => line.StartsWith("FAIL ", StringComparison.Ordinal), output);
        var passed = lines.Where(line => line.StartsWith("PASS " + prefix + " ", StringComparison.Ordinal)).Select(line => line[5..]).ToList();
        passed.Count.ShouldBe(expectedRows, $"{caseName} named assertion inventory\n{output}");
        foreach (var row in requiredRows)
            passed.ShouldContain(row, $"{caseName} must assert '{row}'\n{output}");
        Regex.IsMatch(output, $@"C487: {expectedRows} passed, 0 failed, {expectedRows} rows").ShouldBeTrue(output);
    }
}
