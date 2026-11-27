using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 65 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the cases of the deleted output witness
/// scripts/fixtures/deploy-server2-output-witness.ps1 that need no real transport, in process. HoldLift and HoldLiftRefused
/// (CARD-0948, tasks bb0cd0ec): the two deploy-temp server2-temp /drain/clear requests, the retired placeholder's reactivation and the
/// deployment hold lift, against a fake runner HTTP layer and a fake retirement census. AuditDiagnosis (CARD-1609/CARD-1689): which
/// host audit refusals cross into the wrapper's HostCaseFailed line, from the case directory or the exchange the host ran. The
/// witness's real-transport cases (HostJq, Census) are Linux tests with a fake ssh: HostJqPrerequisiteScriptTests and
/// VolumeRetirementContentTests.
/// </summary>
[Category("Unit")]
public sealed class Server2RollingWitnessTests
{
    private const string Sha = Server2RollingWorld.Sha;
    private const string NoVolumes = "{\"volumes\":[],\"journals\":[]}\n";
    private const string FourVolumes = "{\"volumes\":[\"antiphon-runner-temp_work\",\"antiphon-runner-temp_runner-tmp\",\"antiphon-runner-temp_dind-data\",\"antiphon-runner-temp_runner-state\"],\"journals\":[]}\n";

    // The witness's fake HTTP (status.json answers GET, each POST is recorded) and fake ssh (reply answers the census read).
    private sealed class HoldLiftWorld : IServer2RollingWorld
    {
        public string Status = "";
        public string Census = NoVolumes;
        public int Posts;

        public static string StatusText(string retiredAt, string buildVersion) => new JsonObject
        {
            ["runnerId"] = "server2-temp", ["buildVersion"] = buildVersion, ["available"] = true, ["dispatchEligible"] = true,
            ["acceptingNewWork"] = false, ["draining"] = true, ["redirectTo"] = "server2", ["retireWhenIdle"] = false,
            ["retiredAt"] = retiredAt.Length > 0 ? retiredAt : null,
        }.ToJsonString();

        public Server2RollingHttpAnswer RunnerHttp(string method, string url, IReadOnlyDictionary<string, string> headers, string body)
        {
            if (method == "POST") { Posts++; return new(200, ""); }
            return new(200, Status);
        }

        public Server2RollingChild TempRetirementRead(Server2RollingCall call) => new(0, Census, "");

        private sealed class Held : IServer2RollingLock { public void Release() { } }

        public IServer2RollingLock EnterRolloutLock() => new Held();

        public Server2RollingChild HostJq(Server2RollingCall call) => throw new InvalidOperationException("no host jq");
        public Server2RollingHttpAnswer ReadHttp(string path, string url, IReadOnlyDictionary<string, string> headers) => throw new InvalidOperationException("no read");
        public Server2RollingChild RecycleDiscover(Server2RollingCall call) => throw new InvalidOperationException("no discovery");
        public Server2RollingChild TempProjectCensus(Server2RollingCall call) => throw new InvalidOperationException("no census");
        public int HostCase(string hostCase, string manifestPath, TextWriter output, TextWriter error) => throw new InvalidOperationException("no case");
        public Server2RollingChild Git(string repo, IReadOnlyList<string> arguments) => throw new InvalidOperationException("no git");
    }

    private static string Clear(HoldLiftWorld world, string label, bool holdLift, string census)
    {
        world.Census = census;
        using var root = new Server2RollingWorld();
        var host = new NightlyHost
        {
            Clock = root.Clock, Git = (_, _) => throw new InvalidOperationException(), Launcher = new Nightly.RecordingLauncher(_ => throw new InvalidOperationException()),
            Processes = new NightlyProcessTable(), Probe = new NightlyHostProbe(), Out = TextWriter.Null, Error = TextWriter.Null, Trace = TextWriter.Null,
            Variables = name => name == "ANTIPHON_API" ? "http://127.0.0.1:9" : null,
        };
        string result;
        try
        {
            Server2RollingDeploy.ProbeRunnerRequest("POST", "server2-temp", "/drain/clear",
                new PlainObject(("reason", holdLift ? "CARD-0849 cache verification passed" : "CARD-0948 retired temp placeholder reactivation")), holdLift, Sha,
                root.Root, host, new System.IO.Abstractions.FileSystem(), new Server2RollingSeams { World = world });
            result = "ok";
        }
        catch (Server2RollingRefusal e) { result = "threw:" + e.Message; }
        return "OUTPUT " + label + " " + result + " posts=" + world.Posts;
    }

    // Task bb0cd0ec: deploy-temp lifts its own deployment hold after deploy-temp-runner has created the four temp volumes. Only the
    // reactivation clear of a retired placeholder runs the volume/journal check; the hold lift reaches its POST, and the reactivation
    // clear still refuses while the volumes are present.
    [Test]
    public void Hold_lift_clear_after_deploy_is_not_refused_for_its_own_volumes()
    {
        var world = new HoldLiftWorld();
        var rows = new List<string>();
        // Retired start: no temp volumes yet. deploy-temp-runner then creates all four.
        world.Status = HoldLiftWorld.StatusText("2026-10-30T00:45:02Z", new string('b', 40));
        rows.Add(Clear(world, "reactivate", false, NoVolumes));
        world.Status = HoldLiftWorld.StatusText("", Sha);
        rows.Add(Clear(world, "holdlift", true, FourVolumes));
        world.Status = HoldLiftWorld.StatusText("2026-10-30T00:45:02Z", new string('b', 40));
        rows.Add(Clear(world, "reactivate-retained", false, FourVolumes));
        rows.ToArray().ShouldBe(new[]
        {
            "OUTPUT reactivate ok posts=1",
            "OUTPUT holdlift ok posts=2",
            "OUTPUT reactivate-retained threw:TempVolumesRetained antiphon-runner-temp_work posts=2",
        }, string.Join("\n", rows));
    }

    // Task bb0cd0ec: the hold lift skips the volume check only for the runner this run deployed. A retired runner or another build
    // refuses under the lock before the POST.
    [Test]
    public void Hold_lift_clear_refuses_a_retired_or_other_build_runner()
    {
        var world = new HoldLiftWorld();
        var rows = new List<string>();
        world.Status = HoldLiftWorld.StatusText("2026-11-03T09:45:00Z", Sha);
        rows.Add(Clear(world, "holdlift-retired", true, FourVolumes));
        world.Status = HoldLiftWorld.StatusText("", new string('b', 40));
        rows.Add(Clear(world, "holdlift-other-build", true, FourVolumes));
        rows.ToArray().ShouldBe(new[]
        {
            "OUTPUT holdlift-retired threw:TempRunnerRetiredDuringHold posts=0",
            "OUTPUT holdlift-other-build threw:TempHoldLiftBuildMismatch posts=0",
        }, string.Join("\n", rows));
    }

    // CARD-1609/CARD-1689: a host audit refusal crosses into the wrapper's refusal only when it is a safe diagnosis (a redacted or a
    // named missing executable, never free text or a credential-like name), whether the host wrote it in the case directory or in
    // the exchange it ran (retire-temp-runner has no case directory).
    [Test]
    public async Task Audit_diagnosis_crosses_only_when_safe()
    {
        const string prefix = "RecycleGitAuditUnknown audit check=worktree-status status=127 repo=/tmp/fixture stderr=";
        var failures = new List<string>();
        var checks = 0;
        foreach (var (label, value, safe) in new[]
                 {
                     ("plus", "missing-executable:helper++", true), ("redacted", "redacted", true),
                     ("free-text", "password x", false), ("credential", "missing-executable:credential+helper", false),
                 })
        {
            var diagnosis = prefix + value;
            checks++;
            if (RollingPs.TestC1008SafeAuditDiagnosis(diagnosis) != safe) failures.Add("inventory/" + label);
            foreach (var layout in new[] { "case", "exchange" })
            {
                using var f = new C1008WrapperFixture();
                f.World.CaseOverride = (hostCase, manifest) =>
                {
                    if (hostCase != "retire-temp-runner") return null;
                    var root = JsonNode.Parse(File.ReadAllText(manifest))!["evidenceRoot"]!.GetValue<string>();
                    var dir = Path.Combine(root, layout == "exchange" ? "exchange-fixture" : hostCase);
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "c590-result.json"), new JsonObject { ["accepted"] = false, ["diagnosis"] = diagnosis }.ToJsonString());
                    return 2;
                };
                var run = await f.Run("retire-temp");
                checks++;
                var expected = "Rolling deploy stopped: HostCaseFailed retire-temp-runner exit=2" + (safe ? " diagnosis=" + diagnosis : "");
                var line = run.Output.Replace("\r\n", "\n").Split('\n').FirstOrDefault(x => x.StartsWith("Rolling deploy stopped: ", StringComparison.Ordinal)) ?? "";
                if (run.Exit != 2 || line != expected) failures.Add(layout + "/" + label + ": " + line);
            }
        }
        failures.ShouldBeEmpty("Audit diagnosis mismatches: " + string.Join(", ", failures));
        checks.ShouldBe(12, "OUTPUT audit-diagnosis checks=12 passed=12");
    }
}
