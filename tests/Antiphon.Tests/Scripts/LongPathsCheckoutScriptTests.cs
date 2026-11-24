using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LongPathsCheckoutScriptTests
{
    // Unit 31 of 2026-11-22-remaining-ps1-to-csharp.md deleted scripts/test-longpaths-checkout.ps1: its last two cases,
    // LP_GuardStderr and LP_PathArithmetic, checked that scripts/test-apphost-main-worktree-guard.ps1 linked a worktree of this
    // checkout with core.longpaths and documented the MAX_PATH sum under its temporary prefix. That driver is gone; its rows run
    // in process (AppHostWorktreeGuardTests) on a one-commit fixture repository, which needs neither, so both cases are dropped.

    // In-process since unit 7 of 2026-11-22-remaining-ps1-to-csharp.md (was the test-longpaths-checkout.ps1 case on
    // scripts/qualification/launch-rc.ps1): qualification-launch with real git on a prepared clone refuses
    // longpaths-not-enabled and names the fix command until the clone opts in, then runs nightly-run under the build slot.
    [Test, Timeout(180_000)]
    public void LP_LaunchRefuses(CancellationToken token)
    {
        using var fixture = Antiphon.Tests.Scripts.Nightly.NightlyFixture.Create("lp-launch");
        var clone = Clone(fixture);
        var head = Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(clone, "rev-parse", "HEAD").Trim();
        var state = fixture.PathOf("state");
        Directory.CreateDirectory(state);
        var config = fixture.PathOf("launch-record.json");
        File.WriteAllText(config, new System.Text.Json.Nodes.JsonObject
        {
            ["checkoutRoot"] = clone, ["expectedSha"] = head, ["nightlyArguments"] = new System.Text.Json.Nodes.JsonArray("-NoSync", "-RunId", "lp-launch", "-StateRoot", state),
        }.ToJsonString());
        var launcher = new Antiphon.Tests.Scripts.Nightly.RecordingLauncher(_ => new Antiphon.Nightly.NightlyLaunchResult { ExitCode = 0 });
        var host = RealGit(fixture, launcher);
        var slots = new List<Antiphon.Nightly.BuildSlotOptions>();
        int Slot(Antiphon.Nightly.BuildSlotOptions options, TextWriter? capture)
        {
            slots.Add(options);
            return 0;
        }
        (int Code, string Text) Launch()
        {
            var before = fixture.Out.ToString().Length;
            var code = Antiphon.Nightly.QualificationLaunch.Run(Antiphon.Nightly.QualificationLaunchOptions.Parse(["--config", config], "lp-launch", "pwsh"), host,
                new System.IO.Abstractions.FileSystem(), Slot);
            return (code, fixture.Out.ToString()[before..]);
        }
        var fix = "git -C " + clone + " config --local core.longpaths true";
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        var bad = Launch();
        rows.Check(bad.Code == 2 && bad.Text.Contains("longpaths-not-enabled", StringComparison.Ordinal) && slots.Count == 0 && launcher.Launches.Count == 0,
            "LP launch refuses longpaths-not-enabled", "exit=" + bad.Code + " " + bad.Text);
        rows.Check(bad.Text.Contains(fix, StringComparison.Ordinal) && fix.Contains("config --local core.longpaths true", StringComparison.Ordinal), "LP launch names the fix command", bad.Text);
        Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(clone, "config", "--local", "core.longpaths", "true");
        var good = Launch();
        rows.Check(good.Code == 0 && good.Text.Contains("build-slot", StringComparison.Ordinal)
            && slots.Count == 1 && slots[0].Command.Contains(Path.Combine(clone, "scripts", "nightly-run.ps1")) && slots[0].Label == "rc-qualification",
            "LP launch with longpaths continues to build-slot", good.Text);
        rows.Check(!good.Text.Contains("longpaths-not-enabled", StringComparison.Ordinal), "LP launch with longpaths does not refuse longpaths", good.Text);
        rows.ShouldAllPass(4, "LP launch refuses longpaths-not-enabled", "LP launch names the fix command", "LP launch with longpaths continues to build-slot",
            "LP launch with longpaths does not refuse longpaths");
    }

    private static string Clone(Antiphon.Tests.Scripts.Nightly.NightlyFixture fixture)
    {
        var clone = fixture.PathOf("checkout");
        Directory.CreateDirectory(clone);
        Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(clone, "init", "-q", "-b", "master");
        Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(clone, "-c", "user.name=lp", "-c", "user.email=lp@example.invalid", "-c", "commit.gpgsign=false", "commit", "--allow-empty", "-q", "-m", "lp");
        return clone;
    }

    private static Antiphon.Nightly.NightlyHost RealGit(Antiphon.Tests.Scripts.Nightly.NightlyFixture fixture, Antiphon.Nightly.INightlyLauncher launcher) => new()
    {
        Clock = TimeProvider.System, Git = Antiphon.Nightly.NightlyNativeGit.Run, Launcher = launcher, Processes = new Antiphon.Tests.Scripts.Nightly.FakeProcessTable(),
        Probe = new Antiphon.Tests.Scripts.Nightly.FakeHostProbe(), Out = fixture.Out, Error = fixture.Error, Trace = TextWriter.Null,
    };

    /// <summary>
    /// In-process since W2-nightly S20 (was the test-longpaths-checkout.ps1 case on the PowerShell run core): a -NoSync -WhatIf
    /// NightlyRun.Run with real git on a prepared clone refuses longpaths-not-enabled and names the fix command until the clone
    /// opts in, then reaches whatif with an empty refusal.
    /// </summary>
    [Test, Timeout(180_000)]
    public void LP_NoSyncRefuses(CancellationToken token)
    {
        const string origin = "https://example.invalid/antiphon.git";
        using var fixture = Antiphon.Tests.Scripts.Nightly.NightlyFixture.Create("lp-nosync");
        var clone = fixture.PathOf("checkout");
        Directory.CreateDirectory(clone);
        Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(clone, "init", "-q", "-b", "master");
        Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(clone, "-c", "user.name=lp", "-c", "user.email=lp@example.invalid", "-c", "commit.gpgsign=false", "commit", "--allow-empty", "-q", "-m", "lp");
        Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(clone, "remote", "add", "origin", origin);
        File.WriteAllText(Path.Combine(clone, ".antiphon-nightly-owned"), "{\"origin\":\"" + origin + "\",\"kind\":\"nightly-clone\"}");
        (Antiphon.Nightly.NightlyRunResult Result, string Text) Run()
        {
            var before = fixture.Out.ToString().Length;
            var result = Antiphon.Nightly.NightlyRun.Run(new Antiphon.Nightly.NightlyRunOptions
            {
                NoSync = true, WhatIf = true, CheckoutRoot = clone, StateRoot = fixture.PathOf("state"), LogRoot = fixture.PathOf("logs"),
                CoordinationRoot = fixture.PathOf("coord"), RemoteUrl = origin, Ref = "master", Trigger = "scheduled", Profile = "nightly", SelectionMode = "Full",
                RunId = "lp" + Guid.NewGuid().ToString("N"),
            }, Antiphon.Tests.Scripts.Nightly.NightlyRunTests.RealGitHost(fixture));
            return (result, fixture.Out.ToString()[before..]);
        }
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        var bad = Run();
        rows.Check(bad.Result.ExitCode == 3 && bad.Result.Refusal == "longpaths-not-enabled" && bad.Text.Contains("longpaths-not-enabled", StringComparison.Ordinal),
            "LP nosync refuses longpaths-not-enabled", "exit=" + bad.Result.ExitCode + " refusal=" + bad.Result.Refusal + " " + bad.Text);
        rows.Check(bad.Text.Contains("config --local core.longpaths true", StringComparison.Ordinal) && bad.Text.Contains("git -C ", StringComparison.Ordinal),
            "LP nosync names the fix command", bad.Text);
        Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(clone, "config", "--local", "core.longpaths", "true");
        var good = Run();
        rows.Check(good.Result.ExitCode == 0 && good.Result.Phase == "whatif", "LP nosync with longpaths reaches whatif",
            "exit=" + good.Result.ExitCode + " phase=" + good.Result.Phase + " " + good.Text);
        rows.Check(good.Result.Refusal == "", "LP nosync with longpaths has an empty refusal", "refusal=" + good.Result.Refusal);
        rows.ShouldAllPass(4, "LP nosync refuses longpaths-not-enabled", "LP nosync names the fix command", "LP nosync with longpaths reaches whatif",
            "LP nosync with longpaths has an empty refusal");
    }

    // In-process since unit 7 (was the test-longpaths-checkout.ps1 case on scripts/qualification/preflight.ps1).
    [Test, Timeout(180_000)]
    public void LP_PreflightRefuses(CancellationToken token)
    {
        using var fixture = Antiphon.Tests.Scripts.Nightly.NightlyFixture.Create("lp-preflight");
        var clone = Clone(fixture);
        var host = RealGit(fixture, new Antiphon.Tests.Scripts.Nightly.RecordingLauncher());
        (int Code, string Text) Preflight()
        {
            var before = fixture.Out.ToString().Length;
            var code = Antiphon.Nightly.QualificationLaunch.Preflight(Antiphon.Nightly.QualificationPreflightOptions.Parse(["--checkout-root", clone]), host);
            return (code, fixture.Out.ToString()[before..]);
        }
        var fix = "git -C " + clone + " config --local core.longpaths true";
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        var bad = Preflight();
        rows.Check(bad.Code == 2 && bad.Text.Contains("longpaths-not-enabled", StringComparison.Ordinal), "LP preflight refuses longpaths-not-enabled", "exit=" + bad.Code + " " + bad.Text);
        rows.Check(bad.Text.Contains(fix, StringComparison.Ordinal) && fix.Contains("config --local core.longpaths true", StringComparison.Ordinal), "LP preflight names the fix command", bad.Text);
        Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(clone, "config", "--local", "core.longpaths", "true");
        var good = Preflight();
        rows.Check(good.Code == 0 && !good.Text.Contains("longpaths-not-enabled", StringComparison.Ordinal), "LP preflight accepts core.longpaths true", "exit=" + good.Code + " " + good.Text);
        rows.ShouldAllPass(3, "LP preflight refuses longpaths-not-enabled", "LP preflight names the fix command", "LP preflight accepts core.longpaths true");
    }

    // In process since unit 13 of 2026-11-22-remaining-ps1-to-csharp.md (was the test-longpaths-checkout.ps1 case on the deleted
    // scripts/release-candidate.ps1): ReleaseCandidate.Run over the fake remote of ReleaseCandidateTests clones a missing
    // release checkout with -c core.longpaths=true, and its WhatIf line names the override.
    [Test, Timeout(180_000)]
    public void LP_CloneFlag(CancellationToken token)
    {
        using var fixture = Antiphon.Tests.Scripts.Nightly.NightlyFixture.Create("lp-clone");
        var git = new FakeCandidateRemote(new string('c', 40));
        git.Refs["rc/2026-11-19"] = new string('a', 40);
        var release = fixture.PathOf("releases");
        var whatIf = ReleaseCandidateCases.Cut(fixture, git, release, fixture.PathOf("checkout"), "rc/2026-11-19",
            o => { o.WhatIf = true; o.RemoteUrl = "https://example.invalid/antiphon.git"; });
        var whatIfText = fixture.Out.ToString();
        var live = ReleaseCandidateCases.Cut(fixture, git, release, fixture.PathOf("live-checkout"), "rc/2026-11-19", o => o.RemoteUrl = "https://example.invalid/antiphon.git");
        var logged = string.Join("\n", git.Calls.Select(call => string.Join("|", call.Arguments)));
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        rows.Check(logged.Contains("clone|-c|core.longpaths=true|", StringComparison.Ordinal) && live.ExitCode == 0, "LP release clone argv passes core.longpaths",
            "exit=" + live.ExitCode + " refusal=" + live.Refusal + " " + logged);
        rows.Check(whatIfText.Contains("WhatIf: would clone -c core.longpaths=true", StringComparison.Ordinal) && whatIf.ExitCode == 0, "LP release whatif names core.longpaths", whatIfText);
        rows.ShouldAllPass(2, "LP release clone argv passes core.longpaths", "LP release whatif names core.longpaths");
    }
}
