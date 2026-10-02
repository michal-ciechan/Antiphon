using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace Antiphon.Tests.Checkpoints;

internal static class CheckpointRepeatFixture
{
    internal sealed record ScriptResult(int ExitCode, string Text, string[] Calls, string? Evidence, string? Trx);

    public static string WriteTemplate(string root, int requested = 5, int? failingOrdinal = null)
    {
        var ns = XNamespace.Get("http://microsoft.com/schemas/VisualStudio/TeamTest/2010");
        var rows = from ordinal in Enumerable.Range(0, requested)
                   from method in new[] { "alpha", "beta", "gamma" }
                   select (Ordinal: ordinal, Method: method, Outcome: failingOrdinal == ordinal && method == "alpha" ? "Failed" : "Passed");
        var cases = rows.ToList();
        var doc = new XDocument(new XElement(ns + "TestRun",
            new XElement(ns + "Results", cases.Select((item, index) =>
            {
                var key = "Antiphon.Tests.Checkpoints.Sample.0.0." + item.Method + ".0.0";
                var nativeId = key + "." + item.Ordinal;
                var marker = JsonSerializer.Serialize(new
                {
                    Version = 1, Requested = requested, Nonce = "__NONCE__", NativeId = nativeId,
                    Ordinal = item.Ordinal, CaseKey = key, Class = "Antiphon.Tests.Checkpoints.Sample",
                    Method = item.Method, HostPid = 12345,
                });
                return new XElement(ns + "UnitTestResult",
                    new XAttribute("testId", "T" + index),
                    new XAttribute("executionId", "E" + index),
                    new XAttribute("testName", "display collision"),
                    new XAttribute("outcome", item.Outcome),
                    new XAttribute("duration", "00:00:00.001"),
                    new XElement(ns + "Output",
                        new XElement(ns + "StdOut", "C885_REPEAT_START " + marker + "\nC885_REPEAT_END " + marker + "\n"),
                        item.Outcome == "Failed" ? new XElement(ns + "ErrorInfo", new XElement(ns + "Message", "deliberate-repeat-failure")) : null));
            })),
            new XElement(ns + "TestDefinitions", cases.Select((item, index) =>
                new XElement(ns + "UnitTest", new XAttribute("id", "T" + index),
                    new XElement(ns + "TestMethod", new XAttribute("className", "Antiphon.Tests.Checkpoints.Sample"),
                        new XAttribute("name", item.Method))))),
            new XElement(ns + "ResultSummary", new XElement(ns + "Counters",
                new XAttribute("total", cases.Count), new XAttribute("executed", cases.Count),
                new XAttribute("passed", cases.Count(item => item.Outcome == "Passed")),
                new XAttribute("failed", cases.Count(item => item.Outcome == "Failed"))))));
        var path = Path.Combine(root, "template.trx");
        doc.Save(path);
        return path;
    }

    public static string WriteShim(string root)
    {
        var path = Path.Combine(root, "shim.ps1");
        File.WriteAllText(path, """
            $ErrorActionPreference = 'Stop'
            [System.IO.File]::AppendAllText($env:C885_SHIM_LOG, ($args -join ' ') + [Environment]::NewLine)
            if ($args[0] -eq 'build') { exit 0 }
            $index = [array]::IndexOf($args, '--results-directory')
            if ($index -lt 0) { exit 36 }
            $directory = $args[$index + 1]
            [System.IO.Directory]::CreateDirectory($directory) | Out-Null
            $text = [System.IO.File]::ReadAllText($env:C885_TRX_TEMPLATE).Replace('__NONCE__', $env:ANTIPHON_CHECKPOINT_NONCE)
            [System.IO.File]::WriteAllText([System.IO.Path]::Combine($directory, 'run.trx'), $text)
            exit 0
            """);
        return path;
    }

    public static async Task<ScriptResult> RunScriptAsync(string root, int repeat, string template,
        CancellationToken cancellationToken, bool noBuild = false, string? expectedSha = null,
        string? property = null)
    {
        var shim = WriteShim(root);
        var calls = Path.Combine(root, "calls.log");
        var results = Path.Combine(root, "results");
        var script = Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "run-checkpoint.ps1");
        var psi = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = CheckpointFixtures.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "-NoProfile", "-File", script, "-Name", "CP-1", "-Project",
                     "tests/Antiphon.Checkpoints.RepeatHost", "-OutputPath", "bin-c885-script/", "-Filter",
                     "/*/*/SampleTests*/*", "-MinExecuted", "3", "-Repeat", repeat.ToString(),
                     "-ResultsRoot", results, "-DotnetShim", shim, "-NoSlot" })
            psi.ArgumentList.Add(argument);
        if (noBuild) psi.ArgumentList.Add("-NoBuild");
        if (expectedSha is not null)
        {
            psi.ArgumentList.Add("-ExpectedSourceSha");
            psi.ArgumentList.Add(expectedSha);
        }
        if (property is not null)
        {
            psi.ArgumentList.Add("-MsBuildProperty");
            psi.ArgumentList.Add(property);
        }
        psi.Environment["C885_SHIM_LOG"] = calls;
        psi.Environment["C885_TRX_TEMPLATE"] = template;
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var dirs = Directory.Exists(results) ? Directory.GetDirectories(results, "CP-1-*") : [];
        var directory = dirs.OrderByDescending(item => item).FirstOrDefault();
        return new ScriptResult(process.ExitCode, await stdout + "\n" + await stderr,
            File.Exists(calls) ? File.ReadAllLines(calls) : [],
            directory is null ? null : Path.Combine(directory, "source.json"),
            directory is null ? null : Path.Combine(directory, "run.trx"));
    }
}
