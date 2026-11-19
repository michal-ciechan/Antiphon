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

    /// <summary>
    /// One case of a batch run (a harness given a comma-separated -Case list runs each case in turn in one process):
    /// the case's block, from "C487 CASE BEGIN name" to "C487 CASE END name EXIT code", is validated as a run of that
    /// case alone would be, with that exit code.
    /// </summary>
    internal static void ValidateBatchCase(ScriptHarnessResult result, string prefix, string caseName, int expectedRows,
        IReadOnlyList<string> requiredRows)
    {
        var output = (result.Stdout + "\n" + result.Stderr).ReplaceLineEndings("\n");
        var begin = output.IndexOf("C487 CASE BEGIN " + caseName + "\n", StringComparison.Ordinal);
        begin.ShouldBeGreaterThanOrEqualTo(0, $"{caseName} never started in the batch\n{output}");
        var endMarker = "C487 CASE END " + caseName + " EXIT ";
        var end = output.IndexOf(endMarker, begin, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(begin, $"{caseName} never finished in the batch\n{output}");
        var codeStart = end + endMarker.Length;
        var codeEnd = output.IndexOf('\n', codeStart);
        var code = output[codeStart..(codeEnd < 0 ? output.Length : codeEnd)].Trim();
        int.TryParse(code, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var exitCode)
            .ShouldBeTrue($"{caseName} exit code '{code}'\n{output}");
        Validate(new ScriptHarnessResult(exitCode, output[begin..end], ""), prefix, caseName, expectedRows, requiredRows);
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
