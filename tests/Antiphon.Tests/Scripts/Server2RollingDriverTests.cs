using System.Diagnostics;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0849/CARD-0948/CARD-0973, unit 65 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the frozen offline rolling
/// roster of the deleted scripts/test-deploy-server2.ps1 (T-1 to T-24, the C849 host-order oracle and its mutants) and the T-20 cold
/// marker group of scripts/test-deploy-server2-jq.ps1, run against the deploy-server2 command in process on Server2RollingWorld.
/// Each group keeps the driver's assertions. T-20 runs the real cold marker reader (c973-marker-reader.sh over c590-remote.sh) through
/// bash, or WSL on Windows, and needs jq there, as the driver did; without it the group reports the driver's skip, or fails with
/// C983_JQ_REQUIRED when ANTIPHON_REQUIRE_JQ=1 (the drivers' -RequireJq).
/// </summary>
[Category("Unit")]
public sealed class Server2RollingDriverTests
{
    private const string Sha = Server2RollingWorld.Sha;
    private const string RetiredAt = "2026-09-27T10:00:00Z";

    private sealed record Driven(Server2RollingRun Run, Server2RollingWorld World)
    {
        public int Exit => Run.Exit;
        public string Out => Run.Output;
        public JsonObject[] Trace => Run.Trace;
        public JsonObject[] Cases => Run.Cases;
        public JsonObject[] Posts => Run.Posts;
        public JsonObject State => World.State;
        public bool HasCase(string name) => Run.HasCase(name);
        public int IndexOf(JsonObject row) => Run.IndexOf(row);
    }

    private static readonly List<Server2RollingWorld> Worlds = [];

    private static Driven RunC727(string scenario, string phase = "all", JsonObject? set = null, bool tokenPresent = true, string savedDonor = "",
        Action<Server2RollingWorld>? prepare = null)
    {
        var world = new Server2RollingWorld(Server2RollingWorld.DriverState(scenario, phase, set));
        if (!tokenPresent) File.Delete(world.TokenPath);
        prepare?.Invoke(world);
        var args = new List<string> { "--rolling", "--sha", Sha, "--phase", phase, "--wait-idle-minutes", "1" };
        if (savedDonor.Length > 0) args.AddRange(["--saved-donor", savedDonor]);
        var run = world.Run(args.ToArray());
        lock (Worlds) Worlds.Add(world);
        return new Driven(run, world);
    }

    [After(Test)]
    public void Cleanup()
    {
        lock (Worlds)
        {
            foreach (var world in Worlds) world.Dispose();
            Worlds.Clear();
        }
    }

    private static JsonObject Set(params (string Key, JsonNode? Value)[] entries)
    {
        var set = new JsonObject();
        foreach (var (key, value) in entries) set[key] = value;
        return set;
    }

    private static string S(JsonNode? node) => node is JsonValue v ? v.ToString() : "";

    private static bool B(JsonNode? node) => node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static JsonObject FinalTempStatus(Driven t)
    {
        var answer = t.World.RunnerHttp("GET", "http://127.0.0.1:9/api/session-runners/server2-temp/status", new Dictionary<string, string>(), null!);
        answer.Status.ShouldBe(200, "FinalTempStatusFailed");
        return JsonNode.Parse(answer.Body)!.AsObject();
    }

    // CARD-1609 normal retire-temp visits the host twice: preview, then apply. CR/LF, slash direction and a drive prefix are the same
    // target. Order is not.
    private const string HappyHostOrder = "runner-cache-seed:,deploy-temp-runner:,verify-runner-caches:server2-temp,deploy-parent:,verify-runner-caches:server2,retire-temp-containers:,retire-temp-containers:,retire-temp-runner::preview,retire-temp-runner::apply";

    private static string HostTarget(JsonObject c)
    {
        var name = S(c["name"]).Replace("\r", "").Replace("\n", "").Trim();
        var target = S(c["runnerId"]).Replace("\r", "").Replace("\n", "").Trim().Replace('\\', '/');
        var drive = System.Text.RegularExpressions.Regex.Match(target, "^([A-Za-z]):/(.*)$");
        if (drive.Success) target = "/" + drive.Groups[1].Value.ToLowerInvariant() + "/" + drive.Groups[2].Value;
        var step = "";
        if (name == "retire-temp-runner")
        {
            JsonNode? dry = c.ContainsKey("dryRun") ? c["dryRun"] : c["recycle"]?["dryRun"];
            bool? dryRun = dry is JsonValue v && v.TryGetValue<bool>(out var b) ? b
                : dry is JsonValue s && s.TryGetValue<string>(out var text) ? text.Replace("\r", "").Replace("\n", "").Trim() switch { "true" => true, "false" => false, _ => null } : null;
            if (dryRun == true) step = "preview";
            else if (dryRun == false) step = "apply";
        }
        return step.Length > 0 ? name + ":" + target + ":" + step : name + ":" + target;
    }

    private static string HostOrder(IEnumerable<JsonObject> cases) => string.Join(",", cases.Select(HostTarget));

    private static JsonObject Visit(string name, string runnerId = "", bool? dryRun = null)
    {
        var o = new JsonObject { ["name"] = name, ["runnerId"] = runnerId };
        if (dryRun.HasValue) o["dryRun"] = dryRun.Value;
        return o;
    }

    [Test]
    public void C849_host_order_oracle_and_its_mutants()
    {
        // Same ordered visits as T-1. A Windows-shaped copy and two order mutants stay on this function, so the live comparison
        // cannot ignore them.
        JsonObject[] Logical() =>
        [
            Visit("runner-cache-seed"), Visit("deploy-temp-runner"), Visit("verify-runner-caches", "server2-temp"), Visit("deploy-parent"),
            Visit("verify-runner-caches", "server2"), Visit("retire-temp-containers"), Visit("retire-temp-containers"),
            Visit("retire-temp-runner", dryRun: true), Visit("retire-temp-runner", dryRun: false),
        ];
        HostOrder(Logical()).ShouldBe(HappyHostOrder, "C849_ORACLE logical list");
        var shaped = Logical().Select(v => new JsonObject
        {
            ["name"] = S(v["name"]) + "\r", ["runnerId"] = S(v["runnerId"]).Replace('/', '\\') + "\r", ["dryRun"] = v["dryRun"]?.DeepClone(),
        }).ToArray();
        foreach (var v in shaped) if (v["dryRun"] == null) v.Remove("dryRun");
        HostOrder(shaped).ShouldBe(HappyHostOrder, "C849_ORACLE newline or separator");
        var windowsPath = new JsonObject { ["name"] = "verify-runner-caches\r", ["runnerId"] = "C:\\runner\\server2\r" };
        var linuxPath = new JsonObject { ["name"] = "verify-runner-caches", ["runnerId"] = "/c/runner/server2" };
        HostTarget(windowsPath).ShouldBe(HostTarget(linuxPath), "C849_ORACLE path form");
        HostTarget(linuxPath).ShouldBe("verify-runner-caches:/c/runner/server2", "C849_ORACLE path form");
        var swapped = Logical();
        (swapped[0], swapped[1]) = (swapped[1], swapped[0]);
        HostOrder(swapped).ShouldNotBe(HappyHostOrder, "C849_ORACLE swapped hosts stayed green");
        HostOrder(Logical().SkipLast(1)).ShouldNotBe(HappyHostOrder, "C849_ORACLE dropped target stayed green");
        var l = Logical();
        HostOrder(new[] { l[0], l[1], l[3], l[2] }.Concat(l.Skip(4))).ShouldNotBe(HappyHostOrder, "C849_ORACLE reorder stayed green");
        var previewSwap = Logical().Take(7).Append(Visit("retire-temp-runner", dryRun: false)).Append(Visit("retire-temp-runner", dryRun: true));
        HostOrder(previewSwap).ShouldNotBe(HappyHostOrder, "C849_ORACLE preview/apply swap stayed green");
    }

    [Test]
    public void T01_happy_all_phases_and_T06_output_custody()
    {
        var t1 = RunC727("happy");
        var c = t1.Cases;
        var p = t1.Posts;
        t1.Exit.ShouldBe(0, "T-1 exit: " + t1.Out);
        HostOrder(c).ShouldBe(HappyHostOrder, "T-1 host order/targets");
        p.Length.ShouldBe(5, "T-1 post count");
        (S(p[0]["runnerId"]) == "server2-temp" && S(p[0]["suffix"]) == "/drain" && S(p[0]["body"]!["redirectTo"]) == "server2"
            && !B(p[0]["body"]!["retireWhenIdle"]) && t1.IndexOf(p[0]) < t1.IndexOf(c[0])).ShouldBeTrue("T-1 hold before seed");
        (S(p[1]["runnerId"]) == "server2-temp" && S(p[1]["suffix"]) == "/drain/clear" && t1.IndexOf(p[1]) > t1.IndexOf(c[2])).ShouldBeTrue("T-1 temp verify before clear");
        (S(p[2]["runnerId"]) == "server2" && S(p[2]["suffix"]) == "/drain" && S(p[2]["body"]!["redirectTo"]) == "server2-temp" && !B(p[2]["body"]!["retireWhenIdle"])).ShouldBeTrue("T-1 main drain");
        (S(p[3]["runnerId"]) == "server2" && S(p[3]["suffix"]) == "/drain/clear" && t1.IndexOf(p[3]) > t1.IndexOf(c[4])).ShouldBeTrue("T-1 main verify before clear");
        (S(p[4]["runnerId"]) == "server2-temp" && S(p[4]["suffix"]) == "/drain" && S(p[4]["body"]!["redirectTo"]) == "server2" && B(p[4]["body"]!["retireWhenIdle"])).ShouldBeTrue("T-1 retirement drain");
        // T-6: the operator token never reaches the output or the trace; the command's sources and the fakes are ASCII.
        t1.Out.ShouldNotContain(Server2RollingWorld.Token, Case.Sensitive, "T-6 token custody");
        string.Join("\n", t1.Trace.Select(x => x.ToJsonString())).ShouldNotContain(Server2RollingWorld.Token, Case.Sensitive, "T-6 token custody");
        foreach (var file in Directory.GetFiles(t1.World.EvidenceRoot, "*", SearchOption.AllDirectories))
            File.ReadAllText(file).ShouldNotContain(Server2RollingWorld.Token, Case.Sensitive, "T-6 token custody: " + file);
        foreach (var source in new[] { "Server2RollingDeploy.cs", "Server2RollingCensus.cs", "Server2RollingPs.cs", "Server2RollingOs.cs" })
            File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", source)).ShouldAllBe(b => b <= 127, "T-6 ASCII " + source);
    }

    [Test]
    [Arguments("missing", "TempRunnerStatusMissing")]
    [Arguments("ineligible", "TempRunnerNotEligible")]
    public void T02_missing_or_ineligible_temp(string scenario, string diagnosis)
    {
        var t = RunC727(scenario, "deploy-temp");
        (t.Exit == 2 && t.Out.Contains(diagnosis)).ShouldBeTrue("T-2 " + scenario + " verdict: " + t.Out);
        (scenario != "missing" || t.Cases.Length == 0).ShouldBeTrue("T-2 missing no host");
        (t.Posts.Length == 0 || (scenario == "ineligible" && !t.Posts.Any(x => S(x["runnerId"]) == "server2") && !t.HasCase("verify-runner-caches")))
            .ShouldBeTrue("T-2 no unsafe admission");
    }

    [Test]
    public void T03_main_busy_and_T04_drain_temp_rerun_and_T05_missing_token()
    {
        var t = RunC727("busy", "drain-old", Set(("tempDeployed", true)));
        (t.Exit == 2 && t.Out.Contains("OldRunnerStillBusy")).ShouldBeTrue("T-3 busy verdict");
        t.HasCase("deploy-parent").ShouldBeFalse("T-3 no replacement");
        (t.Posts.Length == 1 && S(t.Posts[0]["suffix"]) == "/drain").ShouldBeTrue("T-3 hold retained");
        t = RunC727("rerun", "drain-temp", Set(("tempDeployed", true), ("oldDeployed", true), ("tempDraining", true), ("tempRetiredAt", RetiredAt)));
        t.Exit.ShouldBe(0, "T-4 exit: " + t.Out);
        t.Posts.ShouldBeEmpty("T-4 no duplicate post");
        t = RunC727("happy", "deploy-temp", tokenPresent: false);
        (t.Exit == 2 && t.Out.Contains("OperatorTokenMissing")).ShouldBeTrue("T-5 token refusal");
        t.Trace.ShouldBeEmpty("T-5 no activity");
    }

    [Test]
    [Arguments("sessions")]
    [Arguments("runnerSessions")]
    [Arguments("queuedTasks")]
    public void T07_busy_temp_seed(string field)
    {
        var t = RunC727("busy-temp-" + field, "deploy-temp", Set(("faultRunner", "server2-temp"), ("faultField", field), ("faultKind", "value"), ("faultValue", 1)));
        (t.Exit == 2 && t.Out.Contains("RunnerBusy")).ShouldBeTrue("T-7 verdict");
        (t.HasCase("runner-cache-seed") || t.HasCase("deploy-temp-runner")).ShouldBeFalse("T-7 no seed");
        t.Posts.ShouldBeEmpty("T-7 no clear/drain");
    }

    [Test]
    public void T08_held_fresh_temp_and_T09_retired_temp_reactivation()
    {
        var t = RunC727("held-fresh", "deploy-temp", Set(("tempDraining", true)));
        var c = t.Cases;
        var p = t.Posts;
        t.Exit.ShouldBe(0, "T-8 exit: " + t.Out);
        (c.Length == 3 && S(c[0]["name"]) == "runner-cache-seed" && S(c[1]["name"]) == "deploy-temp-runner").ShouldBeTrue("T-8 seed/start");
        (S(c[2]["name"]) == "verify-runner-caches" && S(c[2]["runnerId"]) == "server2-temp" && S(c[2]["sourceSha"]) == Sha).ShouldBeTrue("T-8 verify target");
        (p.Length == 2 && S(p[0]["suffix"]) == "/drain" && !B(p[0]["body"]!["retireWhenIdle"]) && S(p[1]["suffix"]) == "/drain/clear"
            && t.IndexOf(p[1]) > t.IndexOf(c[2])).ShouldBeTrue("T-8 hold until verified");
        p.Any(x => S(x["runnerId"]) == "server2").ShouldBeFalse("T-8 no main post");
        t = RunC727("retired", "deploy-temp", Set(("tempDraining", true), ("tempRetiredAt", RetiredAt), ("tempContainer", false)));
        c = t.Cases;
        p = t.Posts;
        t.Exit.ShouldBe(0, "T-9 exit: " + t.Out);
        string.Join(",", c.Select(x => S(x["name"]))).ShouldBe("runner-cache-seed,deploy-temp-runner,verify-runner-caches", "T-9 seed/replacement/verify");
        (p.Length == 3 && S(p[0]["suffix"]) == "/drain/clear" && S(p[1]["suffix"]) == "/drain" && !B(p[1]["body"]!["retireWhenIdle"])
            && S(p[2]["suffix"]) == "/drain/clear").ShouldBeTrue("T-9 rearm and final clear");
        (t.IndexOf(p[2]) > t.IndexOf(c[2])).ShouldBeTrue("T-9 clear after verify");
        p.Any(x => S(x["runnerId"]) == "server2").ShouldBeFalse("T-9 main untouched");
        t = RunC727("retired-container-present", "deploy-temp", Set(("tempDraining", true), ("tempRetiredAt", RetiredAt), ("tempContainer", true), ("tempOffline", true)));
        (t.Exit == 2 && t.Out.Contains("TempContainersRemain")).ShouldBeTrue("T-9 leftover container refused");
        t.Cases.ShouldBeEmpty("T-9 no replacement after host refusal");
        (t.Posts.Length == 0 && t.State["tempRetiredAt"] != null).ShouldBeTrue("T-9 retirement held after host refusal");
    }

    [Test]
    [Arguments("main")]
    [Arguments("temp")]
    public void T10_same_sha_verification(string variant)
    {
        var set = variant == "main" ? Set(("oldDeployed", true), ("oldDraining", true)) : Set(("tempDeployed", true));
        var t = RunC727("same-" + variant, variant == "main" ? "redeploy-old" : "deploy-temp", set);
        var c = t.Cases;
        var p = t.Posts;
        t.Exit.ShouldBe(0, "T-10 exit: " + t.Out);
        (c.Length == 1 && S(c[0]["name"]) == "verify-runner-caches" && S(c[0]["runnerId"]) == (variant == "main" ? "server2" : "server2-temp")).ShouldBeTrue("T-10 only verify");
        S(c[0]["sourceSha"]).ShouldBe(Sha, "T-10 SHA");
        (p.Length == (variant == "main" ? 1 : 0) && (variant != "main" || (S(p[0]["suffix"]) == "/drain/clear" && t.IndexOf(p[0]) > t.IndexOf(c[0])))).ShouldBeTrue("T-10 clear gate");
    }

    [Test]
    [Arguments("main")]
    [Arguments("temp")]
    public void T11_smoke_red(string variant)
    {
        var set = variant == "main" ? Set(("oldDraining", true), ("failVerify", "server2")) : Set(("failVerify", "server2-temp"));
        var t = RunC727("smoke-red-" + variant, variant == "main" ? "redeploy-old" : "deploy-temp", set);
        (t.Exit == 2 && t.Out.Contains("HostCaseFailed verify-runner-caches")).ShouldBeTrue("T-11 verdict: " + t.Out);
        t.Posts.Any(x => S(x["suffix"]) == "/drain/clear").ShouldBeFalse("T-11 held");
        (variant != "temp" || !t.Posts.Any(x => S(x["runnerId"]) == "server2")).ShouldBeTrue("T-11 no next phase");
    }

    [Test]
    [Arguments("deploy-temp")]
    [Arguments("redeploy-old")]
    [Arguments("retire-temp")]
    public void T12_unknown_counters(string phase)
    {
        var runner = phase == "redeploy-old" ? "server2" : "server2-temp";
        foreach (var field in new[] { "sessions", "runnerSessions", "queuedTasks" })
        foreach (var kind in new[] { "omitted", "null" })
        {
            var set = phase == "redeploy-old" ? Set(("oldDraining", true))
                : phase == "retire-temp" ? Set(("tempDraining", true), ("tempRetiredAt", RetiredAt), ("tempRunning", true)) : Set();
            set["faultRunner"] = runner; set["faultField"] = field; set["faultKind"] = kind;
            if (phase == "deploy-temp") set["tempContainer"] = true;
            set["faultValue"] = null;
            var label = "T-12 " + phase + " " + field + " " + kind;
            var t = RunC727("unknown-" + phase + "-" + field + "-" + kind, phase, set);
            (t.Exit == 2 && t.Out.Contains("RunnerCounterUnknown")).ShouldBeTrue(label + " verdict: " + t.Out);
            t.Cases.Any(x => S(x["name"]) is "runner-cache-seed" or "deploy-temp-runner" or "deploy-parent" or "retire-temp-runner").ShouldBeFalse(label + " no destructive host");
            t.Posts.Any(x => S(x["suffix"]) == "/drain/clear" || S(x["runnerId"]) == "server2").ShouldBeFalse(label + " no clear/main drain");
        }
        if (phase != "deploy-temp") return;
        var g = RunC727("unknown-deploy-temp-runnerSessions-garbage", "deploy-temp",
            Set(("faultRunner", "server2-temp"), ("faultField", "runnerSessions"), ("faultKind", "value"), ("faultValue", "garbage")));
        (g.Exit == 2 && g.Out.Contains("RunnerCounterUnknown")).ShouldBeTrue("T-12 live garbage verdict");
        (g.HasCase("runner-cache-seed") || g.HasCase("deploy-temp-runner")).ShouldBeFalse("T-12 live garbage no host");
        g.Posts.ShouldBeEmpty("T-12 live garbage no post");
    }

    [Test]
    [Arguments("runnerSessions", "redeploy-old")]
    [Arguments("runnerSessions", "retire-temp")]
    [Arguments("queuedTasks", "redeploy-old")]
    [Arguments("queuedTasks", "retire-temp")]
    public void T13_T14_busy_counters_hold_replacement(string field, string phase)
    {
        var number = field == "runnerSessions" ? 13 : 14;
        var set = phase == "redeploy-old" ? Set(("oldDraining", true)) : Set(("tempDraining", true), ("tempRetiredAt", RetiredAt));
        set["faultRunner"] = phase == "redeploy-old" ? "server2" : "server2-temp"; set["faultField"] = field; set["faultKind"] = "value"; set["faultValue"] = 1;
        var t = RunC727("busy-" + phase + "-" + field, phase, set);
        (t.Exit == 2 && t.Out.Contains("RunnerBusy")).ShouldBeTrue("T-" + number + " " + phase + " verdict: " + t.Out);
        t.Cases.Any(x => S(x["name"]) is "deploy-parent" or "retire-temp-runner").ShouldBeFalse("T-" + number + " no replacement");
        t.Posts.Any(x => S(x["suffix"]) == "/drain/clear").ShouldBeFalse("T-" + number + " no clear");
    }

    [Test]
    [Arguments("sessions", "RunnerBusy")]
    [Arguments("retiredAt", "TempRunnerNotRetired")]
    [Arguments("draining", "TempRunnerNotRetired")]
    public void T15_retirement_proof(string variant, string diagnosis)
    {
        var set = Set(("tempDraining", true), ("tempRetiredAt", RetiredAt));
        if (variant == "sessions") { set["faultRunner"] = "server2-temp"; set["faultField"] = "sessions"; set["faultKind"] = "value"; set["faultValue"] = 1; }
        if (variant == "retiredAt") set["tempRetiredAt"] = null;
        if (variant == "draining") set["tempDraining"] = false;
        var t = RunC727("retire-" + variant, "retire-temp", set);
        (t.Exit == 2 && t.Out.Contains(diagnosis)).ShouldBeTrue("T-15 verdict: " + t.Out);
        t.HasCase("retire-temp-runner").ShouldBeFalse("T-15 no retire");
        t.Posts.ShouldBeEmpty("T-15 hold unchanged");
    }

    [Test]
    [Arguments("temp-conflict")]
    [Arguments("main-conflict")]
    [Arguments("temp-503")]
    [Arguments("main-503")]
    public void T16_conflicts_and_unavailable(string variant)
    {
        var temp = variant.StartsWith("temp", StringComparison.Ordinal);
        var set = temp ? Set(("tempDraining", true)) : Set(("oldDraining", true));
        set["faultRunner"] = temp ? "server2-temp" : "server2";
        if (variant.EndsWith("503", StringComparison.Ordinal)) set["faultKind"] = "503";
        else { set["faultField"] = "redirectTo"; set["faultKind"] = "value"; set["faultValue"] = "wrong-runner"; }
        var t = RunC727(variant, temp ? "deploy-temp" : "redeploy-old", set);
        var diagnosis = variant.EndsWith("503", StringComparison.Ordinal) ? "RunnerApiFailed" : temp ? "TempRunnerDrainConflict" : "OldRunnerDrainConflict";
        (t.Exit == 2 && t.Out.Contains(diagnosis)).ShouldBeTrue("T-16 verdict: " + t.Out);
        t.Cases.Any(x => S(x["name"]) is "runner-cache-seed" or "deploy-temp-runner" or "deploy-parent" or "retire-temp-runner").ShouldBeFalse("T-16 no destructive host");
        t.Posts.ShouldBeEmpty("T-16 hold unchanged");
    }

    [Test]
    public void T17_saved_donor_transport_and_T24_saved_donor_prerequisite()
    {
        var retired = Set(("tempDraining", true), ("tempRetiredAt", RetiredAt), ("tempContainer", false), ("tempOffline", true));
        var t = RunC727("retired-saved", "deploy-temp", retired, savedDonor: "/home/mc/runner-cache-donor/temp-runner-cache.tar");
        (t.Exit == 2 && t.Out.Contains("TempSavedDonorRequiresMaintenance")).ShouldBeTrue("T-17 saved phase names prerequisite");
        t.HasCase("runner-cache-seed").ShouldBeFalse("T-17 no seed before maintenance");
        (t.Posts.Length == 0 && !t.HasCase("deploy-temp-runner")).ShouldBeTrue("T-17 no deployment or clear");
        var noSource = (JsonObject)retired.DeepClone();
        foreach (var (key, value) in Set(("faultRunner", "server2"), ("faultField", "sessions"), ("faultKind", "value"), ("faultValue", 1), ("oldDraining", false)))
            noSource[key] = value?.DeepClone();
        t = RunC727("retired-no-source", "deploy-temp", noSource);
        var seed = t.Cases.Where(x => S(x["name"]) == "runner-cache-seed").ToArray();
        (t.Exit == 0 && seed.Length == 1 && S(seed[0]["savedDonor"]).Length == 0).ShouldBeTrue("T-17 source never guessed: " + t.Out);
        t.HasCase("deploy-temp-runner").ShouldBeTrue("T-17 marker reuse remains possible");
        (!t.Posts.Any(x => S(x["runnerId"]) == "server2") && !t.HasCase("deploy-parent")).ShouldBeTrue("T-17 cold marker reuse leaves busy main untouched");
        t = RunC727("saved-prerequisite", "deploy-temp", savedDonor: "/fixture/saved.tar");
        (t.Exit == 2 && t.Out.Contains("TempSavedDonorRequiresMaintenance")).ShouldBeTrue("T-24 saved donor explicit maintenance prerequisite");
        t.Cases.ShouldBeEmpty("T-24 no seed");
        t.Posts.ShouldBeEmpty("T-24 no state mutation");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void T18_retired_start_registration(bool alreadyAtSha)
    {
        var t = RunC727("retired-start-" + alreadyAtSha, "deploy-temp",
            Set(("tempDraining", true), ("tempRetiredAt", RetiredAt), ("tempContainer", false), ("tempOffline", true), ("tempDeployed", alreadyAtSha)));
        var c = t.Cases;
        var p = t.Posts;
        t.Exit.ShouldBe(0, "T-18 retired registration succeeds: " + t.Out);
        string.Join(",", c.Select(x => S(x["name"]))).ShouldBe("runner-cache-seed,deploy-temp-runner,verify-runner-caches", "T-18 retired host order");
        (p.Length == 3 && S(p[0]["suffix"]) == "/drain/clear" && S(p[1]["suffix"]) == "/drain" && S(p[1]["body"]!["redirectTo"]) == "server2"
            && !B(p[1]["body"]!["retireWhenIdle"]) && S(p[2]["suffix"]) == "/drain/clear").ShouldBeTrue("T-18 retired clear and safe hold");
        (t.IndexOf(p[0]) < t.IndexOf(p[1]) && t.IndexOf(p[1]) < t.IndexOf(c[0]) && t.IndexOf(p[2]) > t.IndexOf(c[2])).ShouldBeTrue("T-18 retired order and verification gate");
        (S(p[0]["body"]!["reason"]).Contains("CARD-0948") && B(t.State["tempDeployed"]) && !B(t.State["tempDraining"]) && t.State["tempRetiredAt"] == null)
            .ShouldBeTrue("T-18 retired evidence and accepting state");
        var final = FinalTempStatus(t);
        (B(final["dispatchEligible"]) && B(final["acceptingNewWork"]) && S(final["buildVersion"]) == Sha && !B(final["draining"]) && final["retiredAt"] == null)
            .ShouldBeTrue("T-18 retired final status and SHA");
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, false, false)]
    [Arguments(true, true, false)]
    [Arguments(true, true, true)]
    public void T19_cleared_offline_start(bool draining, bool retireWhenIdle, bool alreadyAtSha)
    {
        var t = RunC727($"cleared-offline-{draining}-{retireWhenIdle}-{alreadyAtSha}", "deploy-temp",
            Set(("tempContainer", false), ("tempOffline", true), ("tempDraining", draining), ("tempRetireWhenIdle", retireWhenIdle), ("tempDeployed", alreadyAtSha)));
        var c = t.Cases;
        var p = t.Posts;
        t.Exit.ShouldBe(0, "T-19 cleared offline registration succeeds: " + t.Out);
        string.Join(",", c.Select(x => S(x["name"]))).ShouldBe("runner-cache-seed,deploy-temp-runner,verify-runner-caches", "T-19 cleared host order");
        var needsHold = !draining || retireWhenIdle;
        var clear = needsHold ? p[1] : p[0];
        (p.Length == (needsHold ? 2 : 1) && (!needsHold || (S(p[0]["suffix"]) == "/drain" && S(p[0]["body"]!["redirectTo"]) == "server2"
            && !B(p[0]["body"]!["retireWhenIdle"]))) && S(clear["suffix"]) == "/drain/clear").ShouldBeTrue("T-19 cleared safe hold");
        ((!needsHold || t.IndexOf(p[0]) < t.IndexOf(c[0])) && t.IndexOf(clear) > t.IndexOf(c[2])).ShouldBeTrue("T-19 cleared verification gate");
        (B(t.State["tempDeployed"]) && !B(t.State["tempDraining"]) && t.State["tempRetiredAt"] == null).ShouldBeTrue("T-19 cleared accepting state");
        var final = FinalTempStatus(t);
        (B(final["dispatchEligible"]) && B(final["acceptingNewWork"]) && S(final["buildVersion"]) == Sha && !B(final["draining"]) && final["retiredAt"] == null)
            .ShouldBeTrue("T-19 cleared final status and SHA");
    }

    [Test]
    public void T21_retirement_race_and_clear_gap()
    {
        var t = RunC727("retire-race", "deploy-temp", Set(("tempDraining", true), ("tempRetireWhenIdle", true)));
        (t.Exit == 2 && t.Out.Contains("TempRunnerRetiredDuringHold")).ShouldBeTrue("T-21 retirement stamp refuses before seed: " + t.Out);
        (t.Cases.Length == 0 && t.State["tempRetiredAt"] != null).ShouldBeTrue("T-21 race no build or seed");
        (B(t.State["tempDraining"]) && !B(t.State["tempRetireWhenIdle"])).ShouldBeTrue("T-21 race holds admission");
        var retired = Set(("tempDraining", true), ("tempRetiredAt", RetiredAt), ("tempContainer", false));
        var old = (JsonObject)retired.DeepClone();
        old["oldDraining"] = true;
        t = RunC727("old-invalid", "deploy-temp", old);
        (t.Exit == 2 && t.Out.Contains("OldRunnerRedirectNotEligible")).ShouldBeTrue("T-21 invalid redirect refuses");
        (t.Posts.Length == 0 && t.State["tempRetiredAt"] != null).ShouldBeTrue("T-21 no retirement clear");
        t.Cases.ShouldBeEmpty("T-21 invalid redirect no host mutation");
        t = RunC727("hold-post-fails", "deploy-temp", retired);
        (t.Exit == 2 && t.Out.Contains("RunnerApiFailed")).ShouldBeTrue("T-21 fallible hold refuses");
        (!B(t.State["tempContainer"]) && t.Cases.Length == 0).ShouldBeTrue("T-21 clear gap no container to register");
        var census = t.Trace.Where(x => S(x["kind"]) == "census").ToArray();
        var clearPosts = t.Trace.Where(x => S(x["kind"]) == "http" && S(x["method"]) == "POST" && S(x["suffix"]) == "/drain/clear").ToArray();
        (census.Length == 3 && clearPosts.Length == 1 && S(census[0]["name"]) == "temp-project-absent"
            && census.Count(x => S(x["name"]) == "temp-retirement-clearable") == 2
            && t.IndexOf(census[0]) < t.IndexOf(clearPosts[0]) && t.IndexOf(census[2]) < t.IndexOf(clearPosts[0])).ShouldBeTrue("T-21 absence checked before clear");
    }

    [Test]
    [Arguments("existing", "TempContainersRemain")]
    [Arguments("census-failed", "TempContainerCensusUnavailable")]
    public void T22_host_absence_preflight(string variant, string diagnosis)
    {
        var t = RunC727(variant, "deploy-temp", Set(("tempDraining", true), ("tempRetiredAt", RetiredAt), ("tempContainer", variant == "existing"), ("tempOffline", true)));
        (t.Exit == 2 && t.Out.Contains(diagnosis)).ShouldBeTrue("T-22 named refusal: " + t.Out);
        (t.Posts.Length == 0 && t.State["tempRetiredAt"] != null).ShouldBeTrue("T-22 keeps retirement");
        t.Cases.ShouldBeEmpty("T-22 no seed or replacement");
    }

    [Test]
    public void T23_same_sha_online_recovery()
    {
        var t = RunC727("recovering", "deploy-temp", Set(("tempDeployed", true), ("tempContainer", true)));
        t.Exit.ShouldBe(0, "T-23 same SHA recovery waits successfully: " + t.Out);
        (t.Cases.Length == 1 && S(t.Cases[0]["name"]) == "verify-runner-caches").ShouldBeTrue("T-23 no reseed or replacement");
        t.Posts.ShouldBeEmpty("T-23 no admission mutation during recovery");
    }

    // ---- T-20: the cold marker survives the parent's image cleanup (CARD-0927/CARD-0973) ----

    private static string? MarkerShell()
    {
        // Probe the shell that actually executes the marker reader, once per run (the driver's Test-C973Jq).
        try
        {
            var start = OperatingSystem.IsWindows() ? new ProcessStartInfo("wsl", ["-e", "bash", "-c", "command -v jq"]) : new ProcessStartInfo("bash", ["-c", "command -v jq"]);
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using var probe = Process.Start(start)!;
            probe.StandardOutput.ReadToEnd();
            probe.StandardError.ReadToEnd();
            if (!probe.WaitForExit(30000)) { probe.Kill(true); return null; }
            return probe.ExitCode == 0 ? (OperatingSystem.IsWindows() ? "wsl" : "bash") : null;
        }
        catch (Exception) { return null; }
    }

    private static string Linux(string shell, string path)
    {
        if (shell != "wsl") return path;
        var start = new ProcessStartInfo("wsl", ["-e", "wslpath", "-u", path]) { RedirectStandardOutput = true };
        using var p = Process.Start(start)!;
        var text = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("C973 WSL fixture path conversion failed");
        return text;
    }

    // The real cold reader and the Docker boundary fake: deploy-parent retires the seed image after its ready check.
    private static Func<string, JsonObject, int> Reader(string shell) => (hostCase, state) =>
    {
        var reader = hostCase == "deploy-parent" ? "case_deploy_parent" : "case_verify_runner_caches";
        var paths = new[]
        {
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "fixtures", "c973-marker-reader.sh"),
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "c590-remote.sh"),
            S(state["markerPath"]),
        }.Select(p => Linux(shell, p)).ToArray();
        var args = new List<string>();
        if (shell == "wsl") args.AddRange(["-e", "bash"]);
        args.AddRange([paths[0], paths[1], reader, paths[2], B(state["seedImageAvailable"]) ? "1" : "0", "1", S(state["markerVariant"])]);
        var start = new ProcessStartInfo(shell, args) { RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(start)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    };

    [Test]
    public void T20_cold_marker_survives_parent_image_cleanup()
    {
        var shell = MarkerShell();
        // ANTIPHON_REQUIRE_JQ=1 is the drivers' -RequireJq (CARD-0983): on a jq-expected host an unavailable probe fails, not skips.
        if (shell == null && Environment.GetEnvironmentVariable("ANTIPHON_REQUIRE_JQ") == "1")
            throw new InvalidOperationException("C983_JQ_REQUIRED jq-unavailable: ANTIPHON_REQUIRE_JQ=1 requires jq in the marker-reader shell; T-20 cannot be skipped.");
        if (shell == null)
        {
            Console.WriteLine("C973_SKIPPED jq-missing: T-20 marker-reader groups need jq (CARD-0927)");
            return;
        }
        Driven Marker(string scenario, JsonObject set)
        {
            return RunC727(scenario, "redeploy-old", set, prepare: world =>
            {
                var marker = Path.Combine(world.Root, "seed-accepted");
                File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "fixtures", "c973-cold-seed-accepted.txt"), marker);
                world.State["markerPath"] = marker;
                if (!set.ContainsKey("markerVariant")) world.State["markerVariant"] = "valid";
                if (!set.ContainsKey("seedImageAvailable")) world.State["seedImageAvailable"] = true;
                world.MarkerReader = Reader(shell);
            });
        }
        var t = Marker("cold-marker-pruned", Set(("oldDraining", true)));
        t.Exit.ShouldBe(0, "T-20 pruned cold seed image verifies before admission: " + t.Out);
        var markers = t.Trace.Where(x => S(x["kind"]) == "marker").ToArray();
        (markers.Length == 2 && B(markers[0]["seedImageAvailable"]) && !B(markers[1]["seedImageAvailable"]) && S(markers[1]["markerKind"]) == "cold"
            && S(markers[1]["exit"]) == "0").ShouldBeTrue("T-20 parent prunes seed image before real cold verification");
        t.Posts.Count(x => S(x["suffix"]) == "/drain/clear").ShouldBe(1, "T-20 verified old admission clears");
        foreach (var variant in new[] { "missing", "malformed", "foreign-marker" })
        {
            t = Marker("cold-" + variant, Set(("oldDraining", true), ("oldDeployed", true), ("seedImageAvailable", false), ("markerVariant", variant)));
            (t.Exit == 2 && t.Out.Contains("HostCaseFailed verify-runner-caches exit=2")).ShouldBeTrue("T-20 " + variant + " refuses: " + t.Out);
            (t.Posts.Length == 0 && B(t.State["oldDraining"])).ShouldBeTrue("T-20 " + variant + " holds drain");
        }
    }
}
