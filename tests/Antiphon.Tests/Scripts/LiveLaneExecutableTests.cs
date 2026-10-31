using System.Diagnostics;
using System.Text.Json;
using Antiphon.TestSupport.LiveClaude;
using Antiphon.Tests.Agents;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit"), Timeout(30_000)]
public class LiveLaneExecutableTests
{
    private static LiveClaudeExecutableCases Fixture()
        => new(LiveClaudeCore.C680, (_, r, c, l) => ClaudeDialogWatchdogCanaryTests.PrepareExecutable(r, c, l), "claude.exe");
    private const string Caller = "tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs";

    [Test]
    public void C680UsesAttestedExecutable()
    {
        using var f = Fixture();
        f.Selection("V-4", legacy: false);
        var executable = ClaudeDialogWatchdogCanaryTests.PrepareExecutable(f.Request, f.Context,
            () => throw new InvalidOperationException("PATH consulted"));
        var probe = ClaudeDialogWatchdogCanaryTests.CreateVersionProbe(executable);
        var spec = ClaudeDialogWatchdogCanaryTests.CreateLaunchSpec(executable, ["--version"], new(), "owned-cwd", Guid.NewGuid(), DateTime.UtcNow);
        if (probe.FileName != f.Context.Image.Path || spec.Exe != f.Context.Image.Path)
            throw new InvalidOperationException("C1545 V-4/owned actual C680 probe/start spec uses attested image");
        var source = LiveClaudeExecutableAudit.Read(Caller);
        LiveClaudeExecutableAudit.C680(source);
        LiveClaudeExecutableAudit.Reject(s => LiveClaudeExecutableAudit.Builder(s, "CreateVersionProbe", 0), source,
            "new(executable.RequirePath(), \"--version\")", "new(Resolve(\"claude.exe\"), \"--version\")", "PC-19");
        LiveClaudeExecutableAudit.Reject(s => LiveClaudeExecutableAudit.Builder(s, "CreateLaunchSpec", 2), source,
            "AgentKind.ClaudeCode, executable.RequirePath(), args", "AgentKind.ClaudeCode, Resolve(\"claude.exe\"), args", "PC-20");
    }

    [Test]
    public void C680RefusesMissingContext()
    {
        using var f = Fixture();
        f.Admission("V-5");
        var source = LiveClaudeExecutableAudit.Read(Caller);
        LiveClaudeExecutableAudit.Reject(LiveClaudeExecutableAudit.C680, source,
            "var executable = RequireExecutable();", "var executable = LiveClaudeExecutable.Resolve(\"non-core-manual\", null, null, () => Resolve(\"claude.exe\"));", "PC-21");
        LiveClaudeExecutableMutants.Run("V-5");
    }

    [Test]
    public void C680ImageMismatchRefuses()
    {
        using var f = Fixture();
        f.Images("V-6");
        var source = LiveClaudeExecutableAudit.Read(Caller);
        LiveClaudeExecutableAudit.Reject(LiveClaudeExecutableAudit.C680, source,
            "executable.RequireNativeObservation();", ";", "PC-30");
    }

    [Test]
    public void S1cOutcomeFeedsS1bClassifier()
    {
        var root = LiveClaudeExecutableAudit.Root();
        var classifier = Path.Combine(root, "scripts", "lib", "live-lane-outcome.ps1");
        var jsonl = "{\"type\":\"user\",\"sessionId\":\"session-s1c\",\"uuid\":\"prompt-1\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"s1c provider probe\"}]}}\n"
            + "{\"type\":\"assistant\",\"sessionId\":\"session-s1c\",\"uuid\":\"error-1\",\"parentUuid\":\"prompt-1\",\"error\":\"rate_limit\",\"isApiErrorMessage\":true,\"message\":{\"id\":\"api-1\",\"content\":[{\"type\":\"text\",\"text\":\"limited\"}]}}\n";
        var cases = new (string Name, Dictionary<string, object?> Row, int Exit, string Reason)[]
        {
            ("premise-absent", LiveClaudeOutcome.PremiseAbsent(), 20, "premise-absent"),
            ("provider-error", LiveClaudeOutcome.ProviderError(jsonl), 20, "provider-outage"),
            ("assertion", LiveClaudeOutcome.AssertionFailure(), 1, "assertion-failed"),
            ("missing-setup", LiveClaudeOutcome.MissingSetup(), 2, "row-invalid")
        };
        var payload = cases.Select(c => new Dictionary<string, object?>
        {
            ["name"] = c.Name,
            ["childRunId"] = "run-s1c",
            ["rawChildExit"] = 0,
            ["request"] = new Dictionary<string, object?>
            {
                ["Lane"] = "live-claude",
                ["Attempt"] = 1,
                ["Source"] = "s1c",
                ["Uids"] = new[] { LiveClaudeOutcome.Uid }
            },
            ["terminal"] = new Dictionary<string, object?>
            {
                ["SchemaVersion"] = 1,
                ["RunId"] = "run-s1c",
                ["Lane"] = "live-claude",
                ["Attempt"] = 1,
                ["Source"] = "s1c",
                ["Build"] = "Succeeded",
                ["Platform"] = "Fixture",
                ["Authority"] = "diagnostic",
                ["Complete"] = true,
                ["Outcome"] = "Passed",
                ["Evidence"] = "Complete",
                ["InputBefore"] = "input-s1c",
                ["InputAfter"] = "input-s1c",
                ["ExecutableBefore"] = "exe-s1c",
                ["ExecutableAfter"] = "exe-s1c",
                ["Rows"] = new[] { c.Row }
            }
        }).ToArray();
        var dir = Directory.CreateTempSubdirectory("c1545-outcome-").FullName;
        try
        {
            var payloadPath = Path.Combine(dir, "cases.json");
            var scriptPath = Path.Combine(dir, "classify.ps1");
            File.WriteAllText(payloadPath, JsonSerializer.Serialize(payload));
            File.WriteAllText(scriptPath, """
                $ErrorActionPreference = 'Stop'
                . $env:C1545_CLASSIFIER
                $payload = Get-Content -Raw $env:C1545_PAYLOAD | ConvertFrom-Json -AsHashtable -Depth 20
                foreach ($case in $payload) {
                    $verdict = Get-LiveLaneOutcome -Request $case.request -Terminal $case.terminal -ChildRunId $case.childRunId -RawChildExit ([int]$case.rawChildExit) -PostProbe $null
                    Write-Output ($case.name + ' ' + $verdict.ExitCode + ' ' + $verdict.Reason)
                }
                """);
            var start = new ProcessStartInfo("pwsh", $"-NoProfile -File \"{scriptPath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.Environment["C1545_CLASSIFIER"] = classifier;
            start.Environment["C1545_PAYLOAD"] = payloadPath;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("C1545 outcome classifier did not start");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("C1545 outcome classifier exit " + process.ExitCode + " " + stderr);
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var expected in cases)
            {
                var line = expected.Name + " " + expected.Exit + " " + expected.Reason;
                if (!lines.Contains(line)) throw new InvalidOperationException("C1545 outcome " + line + " missing from " + stdout);
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}
