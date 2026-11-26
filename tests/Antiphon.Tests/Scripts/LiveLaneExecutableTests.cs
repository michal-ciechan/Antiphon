using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.LiveLane;
using Antiphon.TestSupport.LiveClaude;
using Antiphon.LiveClaude.Tests;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit"), Timeout(30_000)]
[ParallelLimiter<ProcessSpawnLimit>]
public class LiveLaneExecutableTests
{
    private static LiveClaudeExecutableCases Fixture()
        => new(LiveClaudeCore.C680, (_, r, c, l) => ClaudeDialogWatchdogCanaryTests.PrepareExecutable(r, c, l), "claude.exe");

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
    }

    [Test]
    public void C680RefusesMissingContext()
    {
        using var f = Fixture();
        f.Admission("V-5");
        LiveClaudeExecutableMutants.Run("V-5");
    }

    [Test]
    public void C680ImageMismatchRefuses()
    {
        using var f = Fixture();
        f.Images("V-6");
    }

    private static string? TryEmitSetupSkip(string reason)
    {
        try { throw new LiveClaudeRefusal(reason); }
        catch (LiveClaudeRefusal e) when (LiveClaudeOutcome.SetupStatus(e.Reason) is string setup)
        {
            return LiveClaudeEvidence.SetupSkip(setup);
        }
        catch (LiveClaudeRefusal)
        {
            return null;
        }
    }

    private static Dictionary<string, object?> EmittedSkip(string reason, string setup)
    {
        var emitted = TryEmitSetupSkip(reason) ?? throw new InvalidOperationException("C1545 outcome " + reason + " did not emit a setup skip");
        if (emitted != "20 " + setup)
            throw new InvalidOperationException("C1545 outcome " + reason + " emitted " + emitted);
        return LiveClaudeOutcome.Skipped(emitted[3..]);
    }

    [Test]
    public void S1cOutcomeFeedsS1bClassifier()
    {
        foreach (var silent in new[]
        {
            "provider-error", "transcript-partial", "turn-unconfirmed", "bridge-changed-submit",
            "trust-unavailable", "menu-unobserved", "menu-not-dismissed", "recipient-blocked"
        })
            if (TryEmitSetupSkip(silent) is { } skip)
                throw new InvalidOperationException("C1545 outcome " + silent + " became " + skip);
        var jsonl = "{\"type\":\"user\",\"sessionId\":\"session-s1c\",\"uuid\":\"prompt-1\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"s1c provider probe\"}]}}\n"
            + "{\"type\":\"assistant\",\"sessionId\":\"session-s1c\",\"uuid\":\"error-1\",\"parentUuid\":\"prompt-1\",\"error\":\"rate_limit\",\"isApiErrorMessage\":true,\"message\":{\"id\":\"api-1\",\"content\":[{\"type\":\"text\",\"text\":\"limited\"}]}}\n";
        var overloaded = jsonl.Replace("\"error\":\"rate_limit\"", "\"error\":\"overloaded\"", StringComparison.Ordinal);
        var mismatched = jsonl.Replace("\"parentUuid\":\"prompt-1\"", "\"parentUuid\":\"other-parent\"", StringComparison.Ordinal);
        try
        {
            LiveClaudeOutcome.ProviderError(mismatched);
            throw new InvalidOperationException("C1545 outcome mismatched request was accepted");
        }
        catch (InvalidOperationException e) when (e.Message.Contains("request link", StringComparison.Ordinal))
        {
        }
        var cases = new (string Name, Dictionary<string, object?> Row, int Exit, string Reason)[]
        {
            ("premise-absent", EmittedSkip("premise-absent", "StartupDefaultAbsent"), 20, "premise-absent"),
            ("bridge-unestablished", EmittedSkip("bridge-unestablished", "BridgeNotEstablished"), 20, "setup-unclassified"),
            ("bridge-unobserved", EmittedSkip("bridge-unobserved", "BridgeNotEstablished"), 20, "setup-unclassified"),
            ("trust-uncleared", EmittedSkip("trust-uncleared", "TrustNotCleared"), 20, "setup-unclassified"),
            ("tui-unready", EmittedSkip("tui-unready", "ReadyTimeout"), 20, "setup-unclassified"),
            ("settings-unobserved", EmittedSkip("settings-unobserved", "ReadyTimeout"), 20, "setup-unclassified"),
            ("provider-error", LiveClaudeOutcome.ProviderError(jsonl), 20, "provider-outage"),
            ("overloaded", LiveClaudeOutcome.ProviderError(overloaded), 20, "provider-outage"),
            ("assertion", LiveClaudeOutcome.AssertionFailure(), 1, "assertion-failed"),
            ("missing-setup", LiveClaudeOutcome.MissingSetup(), 2, "row-invalid")
        };
        // W2 slice S21: the classifier is Antiphon.LiveLane's LiveLaneOutcome (scripts/lib/live-lane-outcome.ps1 is deleted),
        // called in process. Each emitted row still reaches it as JSON, as the terminal evidence a checkpoint run writes.
        var request = new LiveLaneRequest { Lane = "live-claude", Attempt = 1, Source = "s1c", Uids = [LiveClaudeOutcome.Uid] };
        foreach (var c in cases)
        {
            var terminal = JsonNode.Parse(JsonSerializer.Serialize(new Dictionary<string, object?>
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
            }));
            var verdict = LiveLaneOutcome.Evaluate(request, terminal, "run-s1c", 0, null);
            if (verdict.ExitCode != c.Exit || verdict.Reason != c.Reason)
                throw new InvalidOperationException("C1545 outcome " + c.Name + " " + c.Exit + " " + c.Reason + " was " + verdict.ExitCode + " " + verdict.Reason);
        }
    }
}