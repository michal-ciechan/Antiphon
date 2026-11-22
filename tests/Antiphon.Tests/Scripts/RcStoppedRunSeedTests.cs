using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Nightly.Seed;
using static Antiphon.Tests.Scripts.Nightly.SeedWorld;

namespace Antiphon.Tests.Scripts;

// W2-nightly S14: the cases of scripts/test-nightly-stopped-seed.ps1, in-process with the same names and the same one row each
// ("SS <case>.contract", its condition and detail). The seed export, reader, ingress, resolver, schedule and merge are the
// compiled core's. The operator commands run in-process through their handler (NightlySeedCommands.Run with the CLI argv, W2-nightly
// S26): antiphon-scripts export-nightly-stopped-run-seed (SiblingTimingFromSummary) and merge-nightly-seeds (the CLI legs of
// CumulativeMergeUnion and CumulativeMergePrintsDigest). The run core is C# since W2-nightly S20 (NightlyRun.Run in-process in
// PublicHopsForwardAcknowledgement); only its Windows PowerShell 5.1 launcher, scripts/nightly-run.ps1, is launched, against the echo CLI.
// The seed statics (the link cache, the Windows identity switch, the read barrier) are process-wide: one case at a time.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel("nightly-history-statics")]
public sealed class RcStoppedRunSeedTests
{
    private const string RunId = "d47848cce896471db1cc8ca346707cad";
    private const string RunName = "2026-10-30-0824-d47848cc";
    private static readonly string A40 = new('a', 40);
    private static readonly string C40 = new('c', 40);

    private static async Task Case(string name, Func<SeedWorld, Task<(bool Ok, string Detail)>> body)
    {
        var rows = new NightlyCaseRows();
        using (var world = SeedWorld.Create(name))
        {
            try
            {
                world.CommonExport();
                var (ok, detail) = await body(world);
                rows.Check(ok, "SS " + name + ".contract", detail);
            }
            catch (Exception ex)
            {
                rows.Check(false, "SS " + name + ".contract", Message(ex) + "\n" + ex);
            }
        }
        rows.ShouldAllPass(1);
    }

    private static Task Case(string name, Func<SeedWorld, (bool Ok, string Detail)> body) => Case(name, w => Task.FromResult(body(w)));

    /// <summary>The source of the export-nightly-stopped-run-seed and merge-nightly-seeds commands (their synopsis).</summary>
    private static string SeedCommandSource => Path.Combine(Repo, "scripts", "lib", "Antiphon.Nightly", "NightlySeedCommands.cs");

    private static bool Eq(string? a, string b) => string.Equals(a ?? "", b, StringComparison.OrdinalIgnoreCase);

    private static object? P(object? o, string name) => PsValue.Prop(o, name);

    private static object[] Objs(params object[] items) => items;

    private static object[] Strs(params string[] items) => items.Cast<object>().ToArray();

    private static string Stamp(DateTime value) => value.ToString("o", CultureInfo.InvariantCulture);

    private static object? Row(object doc, string suite, string chunk) =>
        Arr(doc, "chunks").FirstOrDefault(c => S(c, "suite") == suite && S(c, "chunk") == chunk);

    private static bool HasClass(object? row, string cls) => Arr(row, "classes").Any(c => PsValue.Str(c) == cls);

    // A seed in the exporter's shape, read back through the production reader.
    private static object WriteSeed(string path, string runId, string stamp, object[] rows, object[] failed)
    {
        var payload = Rec(("sourceObservedAtUtc", stamp), ("chunks", rows), ("failedTests", failed));
        WriteJson(path, Envelope(runId, "rc-synthetic-old", C40, stamp, payload));
        return StoppedSeedFiles.Read(path, "");
    }

    private static PlainObject SeedRow(string suite, string chunk, string[] classes, bool completed, bool started, string? last) =>
        Rec(("suite", suite), ("chunk", chunk), ("classes", Strs(classes)), ("completed", completed), ("started", started), ("lastRunAtUtc", last));

    private static PlainObject Failure(string suite, string chunk, string cls, string method, string outcome) => MergeFail(suite, chunk, cls, method, outcome);

    private static PlainObject Node(string uid, string cls, string method, string type, params string[] categories) =>
        Rec(("Uid", uid), ("ClassName", cls), ("Method", method), ("Type", type), ("Categories", Strs(categories)));

    private static PlainObject SuiteOf(string id, object[] chunks, object[] nodes) =>
        Rec(("id", id), ("chunks", chunks), ("nodes", nodes), ("exclusions", Array.Empty<object>()));

    [Test, Timeout(300_000)]
    public Task BuilderReadOnly(CancellationToken token) => Case(nameof(BuilderReadOnly), w =>
    {
        var doc = StoppedSeedFiles.Read(w.SeedPath, "");
        var bad = Path.Combine(w.Run, "forbidden.json");
        var refused = Has(Refusal(() => StoppedSeedFiles.Export(w.Run, bad, "", "", "", "")), "output-inside-source");
        var probePath = Path.Combine(w.Run, "owner.json");
        var probeBytes = File.ReadAllBytes(probePath);
        var probeTime = File.GetLastWriteTimeUtc(probePath);
        var probeBefore = w.Snapshot();
        var changed = (byte[])probeBytes.Clone();
        changed[0] ^= 1;
        bool detectsBytes;
        try
        {
            File.WriteAllBytes(probePath, changed);
            File.SetLastWriteTimeUtc(probePath, probeTime);
            detectsBytes = probeBefore != w.Snapshot();
        }
        finally
        {
            File.WriteAllBytes(probePath, probeBytes);
            File.SetLastWriteTimeUtc(probePath, probeTime);
        }
        var chunks = Arr(doc, "chunks");
        var ok = detectsBytes && w.Before == w.After && w.Before == w.Snapshot() && refused
            && chunks.Count(c => S(c, "suite") == "antiphon" && S(c, "chunk") == "profile-4" && HasClass(c, "Synthetic.antiphon.Torn")) == 1
            && chunks.Count(c => T(c, "completed")) == 11 && chunks.Count(c => !T(c, "completed")) == 10;
        return (ok, "input bytes/mtimes, closed versus torn and never-started, output containment");
    });

    [Test, Timeout(300_000)]
    public Task FailedFirstMapping(CancellationToken token) => Case(nameof(FailedFirstMapping), w =>
    {
        var input = w.Ingress(accept: true);
        var schedule = w.Schedule(input);
        var ids = Ids(schedule);
        var history = P(input, "History")!;
        var unmapped = Copy(history);
        var resolved = PriorFailureHistory.Resolve(unmapped, w.Suites, "Full", null, null);
        // The pure order must actually use known ages, independently of canonical position.
        var hours = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["antiphon"] = 1, ["session-runner"] = 5, ["pty-host"] = 2, ["agents-pty"] = 4, ["e2e"] = 3 };
        foreach (var row in Arr(history, "chunks"))
            if (Regex.IsMatch(S(row, "chunk"), "(group|split)-2$", RegexOptions.IgnoreCase))
                ((PlainObject)row)["lastRunAtUtc"] = Stamp(w.Now.AddHours(-(hours.TryGetValue(S(row, "suite"), out var h) ? h : 0)));
        var aged = w.Schedule(input);
        var oldest = J(Range(Arr(aged, "entries").Select(e => S(e, "suite")).ToArray(), 0, 4));
        // Each lane resumes at its torn chunk, then its tail (new-New), then wraps to new-Never.
        var ok = oldest == "session-runner,agents-pty,e2e,pty-host,antiphon" && J(Range(ids, 0, 4)) == "new-Red,new-Red,new-Red,new-Red,new-Red"
            && J(Range(ids, 20, 24)) == "new-Passed,new-Passed,new-Passed,new-Passed,new-Passed" && J(Range(ids, 5, 9)) == "new-Torn,new-Torn,new-Torn,new-Torn,new-Torn"
            && J(Range(ids, 10, 13)) == "new-New,new-Never,new-New,new-Never" && Arr(history, "members").Length == 6
            && Arr(resolved, "members").Length == 5 && Arr(resolved, "unresolved").Length == 1;
        return (ok, J(ids));
    });

    [Test, Timeout(300_000)]
    public Task AcknowledgementRequired(CancellationToken token) => Case(nameof(AcknowledgementRequired), w =>
    {
        var missing = w.Ingress();
        var wrong = w.Ingress(accept: true, source: "rc-wrong");
        var accepted = w.Ingress(accept: true);
        var schedule = w.Schedule(missing);
        var ok = P(missing, "History") == null && Eq(S(missing, "FallbackReason"), "seed-acknowledgement-required") && P(wrong, "History") == null
            && Eq(S(wrong, "FallbackReason"), "seed-source-candidate-mismatch") && P(accepted, "History") != null && Eq(Ids(schedule).FirstOrDefault(), "new-Passed");
        return (ok, "explicit acknowledgement and exact source candidate binding");
    });

    [Test, Timeout(300_000)]
    public Task HashMustVerify(CancellationToken token) => Case(nameof(HashMustVerify), w =>
    {
        var seed = ReadJson(w.SeedPath);
        seed["payload"] = PsValue.Str(seed["payload"]).Replace("Synthetic.antiphon.Red", "Synthetic.antiphon.Other");
        var bad = w.PathOf("tampered.json");
        WriteJson(bad, seed);
        var received = w.Ingress(bad, accept: true);
        var ok = P(received, "History") == null && Eq(S(received, "FallbackReason"), "seed-hash-mismatch") && Eq(Ids(w.Schedule(received)).FirstOrDefault(), "new-Passed");
        return (ok, S(received, "FallbackReason"));
    });

    [Test, Timeout(300_000)]
    public Task TimeoutIsFailure(CancellationToken token) => Case(nameof(TimeoutIsFailure), w =>
    {
        var seed = StoppedSeedFiles.Read(w.SeedPath, "");
        var failed = Arr(seed, "failedTests");
        var ok = failed.Length == 6
            && failed.Count(f => Eq(S(f, "outcome"), "Timeout") && Eq(S(f, "className"), "Synthetic.pty_host.Red") && Eq(S(f, "method"), "Run") && Eq(S(f, "chunk"), "all-group-2")) == 1
            && failed.Count(f => Eq(S(f, "outcome"), "Aborted")) == 1;
        return (ok, "Failed/Error/Timeout/Aborted must retain class, method, suite and old chunk");
    });

    [Test, Timeout(300_000)]
    public Task ScheduleConservesAllChunks(CancellationToken token) => Case(nameof(ScheduleConservesAllChunks), w =>
    {
        var input = w.Ingress(accept: true);
        var ok = true;
        object schedule = null!;
        for (var trial = 0; trial < 64; trial++)
        {
            var random = new Random(1514 + trial);
            foreach (var suite in w.Suites) ((PlainObject)suite)["chunks"] = Arr(suite, "chunks").OrderBy(_ => random.Next()).ToArray();
            schedule = w.Schedule(input);
            var expected = Canonical(w.Suites).OrderBy(k => k, StringComparer.Ordinal);
            var actual = Keys(schedule).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            ok = ok && J(expected) == J(actual) && actual.Length == 25;
        }
        // Execute the same canonical objects through the production serial boundary (NightlyDispatch.RunPrepared) with
        // recording steps in place of the suite runner's.
        var trace = new List<string>();
        var reds = 0;
        var outcome = "green";
        var steps = new NightlyDispatchSteps
        {
            RunChunk = (suiteState, chunk) =>
            {
                trace.Add(S(suiteState, "id") + "/" + S(chunk, "id"));
                if (Eq(S(chunk, "id"), "new-New"))
                {
                    reds++;
                    outcome = NightlyTestsPlan.Outcome(outcome, "TESTS");
                }
            },
            Complete = _ => { },
        };
        var state = new NightlyRunState();
        NightlyDispatch.RunPrepared(state, steps, schedule);
        var directOk = trace.Count == 25 && reds == 5 && Eq(outcome, "TESTS");
        // Consume the validated ingress through the real coordinator too. Dropping its PreviousRun argument must lose
        // unfinished/passed order and go red.
        steps.Prepare = (suiteId, _) => w.Suites.FirstOrDefault(s => S(s, "id") == suiteId)!;
        state.Sha = B40;
        state.Attempt = "1";
        state.InvocationId = "synthetic-coordinator";
        state.LogRoot = w.Root;
        trace.Clear();
        reds = 0;
        outcome = "green";
        NightlyDispatch.Coordinate(state, steps, w.Suites.Select(s => (object)S(s, "id")).ToArray(), null, null, "", P(input, "History"), P(input, "PreviousRun"), "");
        var traced = trace.ToArray();
        var coordinatorOrder = J(Range(traced, 0, 4).Select(t => t.Split('/')[1])) == "new-Red,new-Red,new-Red,new-Red,new-Red"
            && J(Range(traced, 20, 24).Select(t => t.Split('/')[1])) == "new-Passed,new-Passed,new-Passed,new-Passed,new-Passed";
        return (ok && directOk && coordinatorOrder && trace.Count == 25 && reds == 5 && Eq(outcome, "TESTS"),
            "64 random permutations and fresh red on previously unseen work; no inherited green/skip credit");
    });

    [Test, Timeout(300_000)]
    public Task InvalidSeedFallsBack(CancellationToken token) => Case(nameof(InvalidSeedFallsBack), w =>
    {
        var old = ReadJson(w.SeedPath);
        old["createdAtUtc"] = Stamp(w.Now.AddDays(-15));
        old["contentHash"] = StoppedSeedFiles.ContentHash(old);
        var stale = w.PathOf("stale.json");
        WriteJson(stale, old);
        var received = w.Ingress(stale, accept: true);
        var ancient = ReadJson(w.SeedPath);
        var body = NightlyJson.ParseText(PsValue.Str(ancient["payload"]), 1024, false) as PlainObject ?? throw new InvalidOperationException("payload");
        body["sourceObservedAtUtc"] = Stamp(w.Now.AddDays(-15));
        ancient["payload"] = NightlyJson.Text(body, 64);
        ancient["contentHash"] = StoppedSeedFiles.ContentHash(ancient);
        var ancientPath = w.PathOf("ancient.json");
        WriteJson(ancientPath, ancient);
        var oldSource = w.Ingress(ancientPath, accept: true);
        var malformed = w.PathOf("malformed.json");
        File.WriteAllText(malformed, "{");
        var bad = w.Ingress(malformed, accept: true);
        var good = w.Ingress(accept: true);
        var rankedBefore = PriorFailureHistory.Resolve(P(good, "History"), w.Suites, "Full", null, null);
        var wanted = Canonical(w.Suites);
        foreach (var suite in w.Suites)
            foreach (var chunk in Arr(suite, "chunks")) ((PlainObject)chunk)["classes"] = Strs("Synthetic.Gone.Class");
        var unmapped = Sched(w.Suites, Arr(rankedBefore, "members"), Arr(rankedBefore, "unresolved"), P(good, "PreviousRun"), B40, w.Now);
        var unmappedOriginal = J(Keys(unmapped)) == J(wanted) && Arr(unmapped, "entries").Any(e => T(e, "priorFailure"));
        var ok = Eq(S(oldSource, "FallbackReason"), "seed-stale") && P(bad, "History") == null && Arr(w.Schedule(bad), "entries").Length == 25
            && Eq(S(received, "FallbackReason"), "seed-stale") && P(received, "History") == null && Eq(S(unmapped, "orderingReason"), "seed-plan-unmapped")
            && unmappedOriginal && Arr(unmapped, "entries").Length == 25;
        return (ok, "stale or wholly unmappable seed restores complete original plan");
    });

    /// <summary>
    /// The public hops forward the seed acknowledgement. W2-nightly S20: the run core is C# (NightlyRun.Run in-process on the
    /// C487-shaped NightlyRunWorld): a synced run's self-update hop and a -NoSync run's tests hop both carry
    /// -AcceptCrossCandidateSeed / --accept-cross-candidate-seed and the seed source; the real launcher (scripts/nightly-run.ps1,
    /// Windows PowerShell 5.1, against the echo CLI) hands the nightly-run command the seed path, the acknowledgement and the
    /// source; the nightly-tests parser reads them back; the run kept its own copy of the seed under the state's history.
    /// </summary>
    [Test, Timeout(300_000)]
    public Task PublicHopsForwardAcknowledgement(CancellationToken token) => Case(nameof(PublicHopsForwardAcknowledgement), async w =>
    {
        using var world = new NightlyRunWorld("public-hops") { Git = new FakeGit(new string('b', 40)) };
        Directory.CreateDirectory(Path.Combine(world.Clone, "scripts"));
        File.WriteAllText(Path.Combine(world.Clone, "scripts", "nightly-run.ps1"), "# the clone's launcher\n");
        void Seed(NightlyRunOptions o)
        {
            o.Trigger = "manual";
            o.PriorFailuresPath = w.SeedPath;
            o.AcceptCrossCandidateSeed = true;
            o.SeedSourceCandidateId = "rc-synthetic-old";
        }
        world.Run(Seed);
        var selfHop = world.Launcher.Launches.FirstOrDefault(NightlyRunHops.IsSelfHop)?.Arguments.ToArray() ?? [];
        var selfOk = selfHop.Contains("-AcceptCrossCandidateSeed") && Array.IndexOf(selfHop, "-SeedSourceCandidateId") is var s and >= 0 && selfHop[s + 1] == "rc-synthetic-old";
        world.Run(o => { Seed(o); o.NoSync = true; o.RunId = "synthetic-new-nosync"; });
        var executor = world.Launcher.Launches.LastOrDefault(NightlyRunHops.IsTestsHop);
        var tests = executor == null ? null : NightlyRunHops.TestsOptions(executor);
        var executorOk = tests is { AcceptCrossCandidateSeed: true, SeedSourceCandidateId: "rc-synthetic-old" };
        var echoed = await NightlyRunHops.LauncherEcho(Path.Combine(Repo, "scripts", "nightly-run.ps1"),
            ["-PriorFailuresPath", w.SeedPath, "-AcceptCrossCandidateSeed", "-SeedSourceCandidateId", "rc-synthetic-old", "-StateRoot", w.PathOf("launcher-state")], w.Root);
        var received = echoed.Options;
        var parsed = NightlyTestsOptions.Parse(["--prior-failures-path", w.SeedPath, "--accept-cross-candidate-seed", "--seed-source-candidate-id", "rc-synthetic-old"]);
        var wrappers = received is { AcceptCrossCandidateSeed: true, SeedSourceCandidateId: "rc-synthetic-old" } && received.PriorFailuresPath == w.SeedPath
            && parsed.AcceptCrossCandidateSeed && parsed.SeedSourceCandidateId == "rc-synthetic-old" && parsed.PriorFailuresPath == w.SeedPath;
        var history = Directory.Exists(Path.Combine(world.State, "history"));
        return (selfOk && executorOk && wrappers && history,
            "sync=" + B(selfOk) + " nosync=" + B(executorOk) + " wrappers=" + B(wrappers) + " history=" + B(history) + " hop=" + string.Join(" ", selfHop)
            + "\nlauncher exit=" + echoed.ExitCode + " " + echoed.Stdout + echoed.Stderr + "\n" + world.Fixture.Out + world.Fixture.Error);
    });

    [Test, Timeout(300_000)]
    public Task SiblingTimingFromSummary(CancellationToken token) => Case(nameof(SiblingTimingFromSummary), w =>
    {
        var layout = SiblingLayout(w.PathOf("logs"), RunId, RunName);
        WriteSeedSummary(layout.Run, layout.Timing, RunId, A40);
        var outPath = w.PathOf("sibling-seed.json");
        var before = SnapshotTree(layout.Run, layout.Timing);
        // antiphon-scripts export-nightly-stopped-run-seed, in-process through its handler.
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = NightlySeedCommands.Run([NightlySeedCommands.ExportStoppedRunSeed, "--run-directory", layout.Run, "--output-path", outPath,
            "--source-candidate-id", "rc-synthetic-old", "--source-sha", A40], stdout, stderr);
        var run = (Code: code, Text: stdout.ToString() + stderr);
        var exitOk = run.Code == 0 && File.Exists(outPath);
        var after = SnapshotTree(layout.Run, layout.Timing);
        var torn = false;
        if (exitOk)
        {
            var doc = StoppedSeedFiles.Read(outPath, "");
            torn = Arr(doc, "chunks").Count(c => S(c, "suite") == "antiphon" && S(c, "chunk") == "profile-4" && HasClass(c, "Synthetic.antiphon.Torn")) == 1
                && S(doc, "sourceRunId") == RunId;
        }
        var plansInRun = Directory.GetFiles(layout.Run, "groups-planned*.json").Length;
        var ok = exitOk && torn && plansInRun == 0 && before == after;
        return (ok, "operator export from the run directory discovers summary timingRoot; exit=" + run.Code + (exitOk ? "" : "\n" + run.Text));
    });

    [Test, Timeout(300_000)]
    public Task StoppedTimingRootAndRefusals(CancellationToken token) => Case(nameof(StoppedTimingRootAndRefusals), w =>
    {
        var explicitLayout = SiblingLayout(w.PathOf("explicit-logs"), RunId, RunName);
        File.Delete(Path.Combine(explicitLayout.Run, "summary.json"));
        var explicitOut = w.PathOf("explicit-seed.json");
        bool explicitOk;
        try
        {
            StoppedSeedFiles.Export(explicitLayout.Run, explicitOut, "", "rc-synthetic-old", A40, explicitLayout.Timing);
            var explicitDoc = StoppedSeedFiles.Read(explicitOut, "");
            explicitOk = S(explicitDoc, "sourceRunId") == RunId && Arr(explicitDoc, "chunks").Length > 0;
        }
        catch (Exception) { explicitOk = false; }
        var auto = SiblingLayout(w.PathOf("auto-logs"), RunId, RunName);
        File.Delete(Path.Combine(auto.Run, "owner.json"));
        var autoOut = w.PathOf("auto-seed.json");
        bool autoOk;
        try
        {
            StoppedSeedFiles.Export(auto.Run, autoOut, "", "rc-synthetic-old", A40, "");
            autoOk = S(StoppedSeedFiles.Read(autoOut, ""), "sourceRunId") == RunId;
        }
        catch (Exception) { autoOk = false; }
        bool RefuseTiming(Action<Layout> arrange, string like)
        {
            var id = Guid.NewGuid().ToString("N");
            var lay = SiblingLayout(w.PathOf("refuse-" + id), RunId, RunName);
            arrange(lay);
            var message = Refusal(() => StoppedSeedFiles.Export(lay.Run, w.PathOf("refuse-" + id + ".json"), "", "rc-synthetic-old", A40, ""));
            return Has(message, like);
        }
        var outside = RefuseTiming(lay =>
        {
            var elsewhere = w.PathOf("outside-timing-" + lay.RunId);
            Directory.CreateDirectory(elsewhere);
            WriteSeedSummary(lay.Run, elsewhere, lay.RunId, A40);
        }, "seed-timing-outside");
        var symlink = RefuseTiming(lay =>
        {
            var real = Path.Combine(Path.GetDirectoryName(lay.Timing)!, "timing-real-" + lay.RunId);
            Directory.Move(lay.Timing, real);
            Directory.CreateSymbolicLink(lay.Timing, real);
            WriteSeedSummary(lay.Run, lay.Timing, lay.RunId, A40);
        }, "evidence-link");
        var other = RefuseTiming(lay =>
        {
            var wrong = Path.Combine(Path.GetDirectoryName(lay.Timing)!, "timing-ffffffffffffffffffffffffffffffff");
            Directory.CreateDirectory(wrong);
            WriteSeedSummary(lay.Run, wrong, lay.RunId, A40);
        }, "seed-timing-run-mismatch");
        var directLayout = SiblingLayout(w.PathOf("direct-link-logs"), RunId, RunName);
        var realTiming = Path.Combine(Path.GetDirectoryName(directLayout.Timing)!, "timing-real-" + RunId);
        Directory.Move(directLayout.Timing, realTiming);
        Directory.CreateSymbolicLink(directLayout.Timing, realTiming);
        var directMessage = Refusal(() => StoppedSeedFiles.TimingRoot(directLayout.Run, directLayout.Timing, RunId));
        var directOk = Has(directMessage, "evidence-link");
        return (explicitOk && autoOk && outside && symlink && other && directOk,
            "explicit=" + B(explicitOk) + " auto=" + B(autoOk) + " outside=" + B(outside) + " symlink=" + B(symlink) + " other=" + B(other) + " direct=" + directMessage);
    });

    [Test, Timeout(300_000)]
    public Task ChunkUnfinishedTolerance(CancellationToken token) => Case(nameof(ChunkUnfinishedTolerance), w =>
    {
        object? Group(string outPath, string chunk) => Row(StoppedSeedFiles.Read(outPath, ""), "antiphon", chunk);
        var timing = Path.Combine(w.Run, "antiphon-all-group-1.timing.json");
        bool lockedOk = false;
        string lockedDetail;
        using (File.Open(timing, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try
            {
                var lockedOut = w.PathOf("locked-seed.json");
                StoppedSeedFiles.Export(w.Run, lockedOut, "", "", "", "");
                var row = Group(lockedOut, "all-group-1");
                lockedOk = !T(row, "completed") && Has(S(row, "unfinishedReason"), "timing");
                lockedDetail = S(row, "unfinishedReason");
            }
            catch (Exception ex) { lockedDetail = Message(ex); }
        }
        var trx = Path.Combine(w.Run, "antiphon-all-group-2.trx");
        var trxBytes = File.ReadAllBytes(trx);
        StoppedSeedFiles.ReadBarrier = path =>
        {
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(trx), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                File.AppendAllText(path, " ");
        };
        bool changedOk = false;
        string changedDetail;
        try
        {
            var changedOut = w.PathOf("changed-seed.json");
            StoppedSeedFiles.Export(w.Run, changedOut, "", "", "", "");
            var doc = StoppedSeedFiles.Read(changedOut, "");
            var row = Row(doc, "antiphon", "all-group-2");
            var kept = Arr(doc, "failedTests").Count(f => S(f, "suite") == "antiphon" && S(f, "chunk") == "all-group-2");
            changedOk = !T(row, "completed") && Has(S(row, "unfinishedReason"), "trx") && kept == 0;
            changedDetail = S(row, "unfinishedReason") + " kept=" + kept;
        }
        catch (Exception ex) { changedDetail = Message(ex); }
        finally
        {
            StoppedSeedFiles.ReadBarrier = null;
            File.WriteAllBytes(trx, trxBytes);
        }
        var passed = Path.Combine(w.Run, "antiphon-all-group-1.trx");
        var passedText = File.ReadAllText(passed);
        var open = passedText.Replace("<ResultSummary outcome=\"Completed\"><Counters total=\"1\" executed=\"1\" /></ResultSummary>", "");
        bool openOk = false;
        string openDetail;
        try
        {
            File.WriteAllText(passed, open);
            var openOut = w.PathOf("open-seed.json");
            StoppedSeedFiles.Export(w.Run, openOut, "", "", "", "");
            var row = Group(openOut, "all-group-1");
            openOk = !T(row, "completed");
            openDetail = "completed=" + S(row, "completed");
        }
        catch (Exception ex) { openDetail = Message(ex); }
        finally { File.WriteAllText(passed, passedText); }

        var denied = Path.Combine(w.Run, "antiphon-all-group-2.timing.json");
        var custody = new SeedCustody();
        bool deniedOk = false;
        string deniedDetail = "";
        Action? deniedRestore = null;
        try
        {
            deniedRestore = custody.EnterUnreadableTiming(denied);
            if (deniedRestore != null)
            {
                try
                {
                    var deniedOut = w.PathOf("denied-seed.json");
                    StoppedSeedFiles.Export(w.Run, deniedOut, "", "", "", "");
                    var row = Group(deniedOut, "all-group-2");
                    deniedOk = !T(row, "completed") && Has(S(row, "unfinishedReason"), "timing");
                    deniedDetail = S(row, "unfinishedReason") + " branch=" + custody.Branch;
                }
                catch (Exception ex) { deniedDetail = Message(ex); }
            }
            else
            {
                deniedOk = custody.Branch == "windows-acl" && custody.NotApplicable >= 1 && custody.Note == SeedCustody.NotApplicableLine;
                deniedDetail = "not-applicable count=" + custody.NotApplicable;
            }
        }
        finally { deniedRestore?.Invoke(); }
        bool windowsOk = true, naOk = true;
        string windowsDetail = "native-windows", naDetail = "native-windows";
        if (custody.Branch == "unix-mode" && !OperatingSystem.IsWindows())
        {
            var savedCalls = custody.UnixModeCalls;
            custody.WindowsBranchRan = false;
            custody.UnixModesSupported = false;
            custody.WindowsAcl = target =>
            {
                if (OperatingSystem.IsWindows()) return null;
                var prior = File.GetUnixFileMode(target);
                File.SetUnixFileMode(target, UnixFileMode.None);
                return () => File.SetUnixFileMode(target, prior);
            };
            Action? windowsRestore = null;
            try
            {
                windowsRestore = custody.EnterUnreadableTiming(denied);
                if (!custody.WindowsBranchRan) throw new InvalidOperationException("windows-branch-did-not-run");
                if (custody.UnixModeCalls != savedCalls) throw new InvalidOperationException("unix-mode-used-on-windows-branch");
                if (custody.Branch != "windows-acl") throw new InvalidOperationException("windows-branch-not-selected");
                if (windowsRestore == null) throw new InvalidOperationException("windows-custody-not-applied");
                var windowsOut = w.PathOf("windows-denied-seed.json");
                StoppedSeedFiles.Export(w.Run, windowsOut, "", "", "", "");
                var row = Group(windowsOut, "all-group-2");
                windowsOk = !T(row, "completed") && Has(S(row, "unfinishedReason"), "timing");
                windowsDetail = S(row, "unfinishedReason");
            }
            catch (Exception ex) { windowsOk = false; windowsDetail = Message(ex); }
            finally
            {
                windowsRestore?.Invoke();
                custody.UnixModesSupported = null;
                custody.WindowsAcl = null;
            }
            custody.WindowsBranchRan = false;
            var naBefore = custody.NotApplicable;
            custody.UnixModesSupported = false;
            Action? naRestore = null;
            var naThrew = false;
            var naPrinted = false;
            // The not-applicable line as the host prints it (Write-Host, captured with 6>&1 in the driver).
            var captured = new StringWriter();
            var host = custody.Host;
            try
            {
                custody.Host = captured;
                naRestore = custody.EnterUnreadableTiming(denied);
                naPrinted = captured.ToString().ReplaceLineEndings("\n").Split('\n').Contains(SeedCustody.NotApplicableLine);
            }
            catch (Exception ex) { naThrew = true; naDetail = Message(ex); }
            finally
            {
                custody.Host = host;
                naRestore?.Invoke();
                custody.UnixModesSupported = null;
            }
            naOk = !naThrew && naPrinted && custody.WindowsBranchRan && custody.NotApplicable == naBefore + 1 && custody.Note == SeedCustody.NotApplicableLine;
            if (!naThrew) naDetail = "printed=" + B(naPrinted) + " count=" + custody.NotApplicable;
        }
        var tamper = SiblingLayout(w.PathOf("tamper-logs"), RunId, RunName);
        WriteSeedSummary(tamper.Run, tamper.Timing, "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", A40);
        var tamperMessage = Refusal(() => StoppedSeedFiles.Export(tamper.Run, w.PathOf("tamper-seed.json"), "", "rc-synthetic-old", A40, ""));
        var tamperOk = Has(tamperMessage, "seed-timing-run-mismatch") || Has(tamperMessage, "seed-plan-run-mismatch") || Has(tamperMessage, "seed-summary");
        return (lockedOk && changedOk && openOk && deniedOk && tamperOk && windowsOk && naOk,
            "locked=" + lockedDetail + "; changed=" + changedDetail + "; open=" + openDetail + "; denied=" + deniedDetail + "; tamper=" + tamperMessage
            + "; windows=" + windowsDetail + "; na=" + naDetail);
    });

    [Test, Timeout(300_000)]
    public Task LinkCacheRefusesReplacement(CancellationToken token) => Case(nameof(LinkCacheRefusesReplacement), w =>
    {
        var fileLink = Path.Combine(w.Run, "linked-file.txt");
        File.CreateSymbolicLink(fileLink, Path.Combine(w.Run, "owner.json"));
        string fileMessage;
        try { fileMessage = Refusal(() => StoppedSeedFiles.Export(w.Run, w.PathOf("file-link-seed.json"), "", "", "", "")); }
        finally { File.Delete(fileLink); }
        var parent = w.PathOf("link-parent");
        var real = Path.Combine(parent, "real-run");
        CopyFiles(FixtureDirectory, real);
        var linkedRun = Path.Combine(parent, "linked-run");
        Directory.CreateSymbolicLink(linkedRun, real);
        var parentMessage = Refusal(() => StoppedSeedFiles.Export(linkedRun, w.PathOf("parent-link-seed.json"), "", "", "", ""));
        var swap = w.PathOf("swap-dir");
        Directory.CreateDirectory(swap);
        File.WriteAllText(Path.Combine(swap, "a.txt"), "stable");
        HistoryLinkCache.AssertNoLink(swap);
        var held = w.PathOf("swap-real");
        Directory.Move(swap, held);
        Directory.CreateSymbolicLink(swap, held);
        var swapMessage = Refusal(() => HistoryLinkCache.AssertNoLink(Path.Combine(swap, "a.txt")));
        return (Has(fileMessage, "evidence-link") && Has(parentMessage, "evidence-link") && Has(swapMessage, "evidence-link"),
            "file=" + fileMessage + "; parent=" + parentMessage + "; swap=" + swapMessage);
    });

    [Test, Timeout(300_000)]
    public Task DigestGuardLeavesSeedOrder(CancellationToken token) => Case(nameof(DigestGuardLeavesSeedOrder), w =>
    {
        var input = w.Ingress(accept: true);
        var seed = P(input, "PreviousRun")!;
        var kept = w.Schedule(input);
        var cross = Sched(w.Suites, previous: seed, candidate: C40, now: w.Now);
        var bare = Copy(seed);
        bare.Remove("seedKind");
        var members = Arr(PriorFailureHistory.Resolve(seed, w.Suites, "Full", null, null), "members");
        var canonicalOrder = J(Canonical(w.Suites));
        bool Original(object schedule) => Arr(schedule, "entries").Any(e => T(e, "priorFailure")) && J(Keys(schedule)) == canonicalOrder;
        var missingMarker = Sched(w.Suites, members, previous: bare, candidate: B40, now: w.Now);
        var foreign = Copy(seed);
        foreign["seedKind"] = "operator-seed-forged";
        var foreignSchedule = Sched(w.Suites, members, previous: foreign, candidate: B40, now: w.Now);
        object[] tiny =
        [
            SuiteOf("antiphon", Objs(Rec(("id", "Ordinary"), ("classes", Strs("C.B")), ("nodes", Array.Empty<object>())),
                Rec(("id", "Priority"), ("classes", Strs("C.A")), ("nodes", Array.Empty<object>()))), []),
        ];
        var plain = Sched(tiny, now: w.Now);
        object[] forgedChunks =
        [
            Rec(("suite", "antiphon"), ("chunk", "seed-ordinary"), ("classes", Strs("C.B")), ("completed", true), ("started", true), ("lastRunAtUtc", Stamp(w.Now.AddHours(-2)))),
            Rec(("suite", "antiphon"), ("chunk", "seed-priority"), ("classes", Strs("C.A")), ("completed", true), ("started", true), ("lastRunAtUtc", Stamp(w.Now.AddHours(-3)))),
        ];
        object[] forgedFailed =
        [
            Rec(("suite", "antiphon"), ("chunk", "seed-ordinary"), ("className", "C.B"), ("method", "Run"), ("outcome", "Failed")),
            Rec(("suite", "antiphon"), ("chunk", "seed-ordinary"), ("className", "C.B"), ("method", "RunAgain"), ("outcome", "Failed")),
        ];
        var normal = Rec(("schemaVersion", 1), ("liveness", "closed"), ("sha", B40), ("completedAt", Stamp(w.Now.AddHours(-1))), ("canonicalPlanDigest", S(plain, "canonicalPlanDigest")),
            ("seedKind", "operator-seed-from-stopped-run"), ("sourceSha", B40), ("sourceRunId", "synthetic-forged"), ("sourceObservedAtUtc", Stamp(w.Now.AddHours(-2))),
            ("suites", Objs(Rec(("id", "antiphon"), ("chunk", "Priority"), ("result", "FAIL"), ("exitCode", 1)), Rec(("id", "antiphon"), ("chunk", "Ordinary"), ("result", "pass"), ("exitCode", 0)))),
            ("chunks", forgedChunks), ("failedTests", forgedFailed));
        if (normal["chunks"] is not object[] { Length: >= 2 } || normal["failedTests"] is not object[]) throw new InvalidOperationException("forged normal record lost its arrays");
        var forged = Sched(tiny, previous: normal, candidate: B40, now: w.Now);
        var stripped = Copy(normal);
        stripped.Remove("canonicalPlanDigest");
        var forgedMissing = Sched(tiny, previous: stripped, candidate: B40, now: w.Now);
        var noSha = Copy(seed);
        noSha.Remove("sourceSha");
        var shaSchedule = Sched(w.Suites, members, previous: noSha, candidate: B40, now: w.Now);
        var noRun = Copy(seed);
        noRun.Remove("sourceRunId");
        var runSchedule = Sched(w.Suites, members, previous: noRun, candidate: B40, now: w.Now);
        var hit = Arr(seed, "chunks").First(c => Arr(c, "classes").Length > 0);
        object[] regrouped =
        [
            SuiteOf("antiphon",
                Objs(Rec(("id", "all-group-1"), ("classes", Strs("Synthetic.antiphon.Passed", "Synthetic.antiphon.Never")), ("nodes", Array.Empty<object>())),
                    Rec(("id", "held-red"), ("classes", Strs("Synthetic.antiphon.Red")), ("nodes", Array.Empty<object>()))),
                Objs(Node("Synthetic.antiphon.Passed", "Synthetic.antiphon.Passed", "Run", "Passed"), Node("Synthetic.antiphon.Never", "Synthetic.antiphon.Never", "Run", "Never"),
                    Node("Synthetic.antiphon.Red", "Synthetic.antiphon.Red", "Run", "Red"))),
        ];
        var regroupMembers = Arr(PriorFailureHistory.Resolve(seed, regrouped, "Full", null, null), "members");
        var regroup = Sched(regrouped, regroupMembers, previous: seed, candidate: B40, now: w.Now);
        object[] same = [SuiteOf(S(hit, "suite"), Objs(Rec(("id", S(hit, "chunk")), ("classes", Arr(hit, "classes")), ("nodes", Array.Empty<object>()))), [])];
        var sameSchedule = Sched(same, previous: seed, candidate: B40, now: w.Now);
        string R(object schedule) => S(schedule, "orderingReason");
        string First(object schedule) => Ids(schedule).FirstOrDefault() ?? "";
        var ok = Starts(R(kept), "operator-seed-order") && First(kept) == "new-Red" && Permutation(kept, w.Suites);
        ok = ok && Starts(R(cross), "operator-seed-order") && Permutation(cross, w.Suites);
        ok = ok && R(missingMarker) == "previous-run-plan-digest-missing" && Original(missingMarker);
        ok = ok && R(foreignSchedule) == "operator-seed-kind-foreign" && Original(foreignSchedule);
        ok = ok && R(forged) == "previous-run-order" && First(forged) == "Priority" && Permutation(forged, tiny);
        ok = ok && R(forgedMissing) == "previous-run-plan-digest-missing" && First(forgedMissing) == "Ordinary" && Permutation(forgedMissing, tiny);
        ok = ok && R(shaSchedule) == "seed-candidate-missing" && Original(shaSchedule);
        ok = ok && R(runSchedule) == "seed-run-missing" && Original(runSchedule);
        ok = ok && Starts(R(regroup), "operator-seed-order") && First(regroup) == "held-red" && T(Arr(regroup, "entries").FirstOrDefault(), "priorFailure")
            && Arr(regroup, "entries").Length == 2 && Permutation(regroup, regrouped);
        ok = ok && Starts(R(sameSchedule), "operator-seed-order") && Permutation(sameSchedule, same);
        return (ok, "kept=" + R(kept) + "; missing=" + R(missingMarker) + "; foreign=" + R(foreignSchedule) + "; forged=" + R(forged) + "/" + First(forged)
            + "; forgedMissing=" + R(forgedMissing) + "/" + First(forgedMissing) + "; sha=" + R(shaSchedule) + "; run=" + R(runSchedule)
            + "; regroup=" + R(regroup) + "/" + First(regroup) + "; same=" + R(sameSchedule));
    });

    [Test, Timeout(300_000)]
    public Task RealPackerRegroupKeepsFailedFirst(CancellationToken token) => Case(nameof(RealPackerRegroupKeepsFailedFirst), w =>
    {
        const string alpha = "Antiphon.Tests.PackWitness.Alpha", beta = "Antiphon.Tests.PackWitness.Beta", gamma = "Antiphon.Tests.PackWitness.Gamma";
        const string never = "Antiphon.Tests.PackWitness.NeverSeen", delta = "Antiphon.Tests.PackWitness.Delta", zeta = "Antiphon.Tests.PackWitness.Zeta";
        var stamp = Stamp(w.Now.AddHours(-2));
        var seedN = 0;
        object[] ConvertCounts(Dictionary<string, int> counts)
        {
            var nodes = new List<object>();
            var leaves = new List<object>();
            var i = 0;
            foreach (var name in counts.Keys.OrderBy(k => k, NightlyPlanCases.SortObject))
            {
                i++;
                var type = name.Split('.')[^1];
                for (var n = 1; n <= counts[name]; n++) nodes.Add(Rec(("ClassName", name), ("Method", "M" + n), ("Type", type), ("Categories", Array.Empty<object>()), ("Uid", name + "/" + n)));
                leaves.Add(Rec(("id", "all-profile-" + i), ("classes", Strs(name)), ("exactFilter", ""), ("nodes", Array.Empty<object>())));
            }
            return NightlyTestsPlan.ProfileLaunchGroups(leaves.ToArray(), nodes.ToArray(), null, NightlyPlanCases.Costs(), null);
        }
        object WritePackSeed(object[] rows, object[] failed) => WriteSeed(w.PathOf("pack-seed-" + ++seedN + ".json"), "synthetic-packer", stamp, rows, failed);
        object[] NewSuite(IEnumerable<object> groups)
        {
            // A group with 'methods' is a split chunk like the real packer's: its own disjoint node list.
            static bool Split(object g) => PsValue.PropertyNames(g).Contains("methods", StringComparer.OrdinalIgnoreCase);
            static object[] MethodNodes(object g)
            {
                var cls = PsValue.Str(Arr(g, "classes")[0]);
                return Arr(g, "methods").Select(m => (object)Node(cls + "/" + PsValue.Str(m), cls, PsValue.Str(m), cls.Split('.')[^1])).ToArray();
            }
            var list = groups.ToArray();
            var chunks = list.Select(g => (object)Rec(("id", S(g, "id")), ("classes", Arr(g, "classes")), ("nodes", Split(g) ? MethodNodes(g) : Array.Empty<object>()))).ToArray();
            var nodes = list.SelectMany(g => Split(g) ? MethodNodes(g)
                : Arr(g, "classes").Select(c => (object)Node(PsValue.Str(c), PsValue.Str(c), "Run", PsValue.Str(c).Split('.')[^1]))).ToArray();
            return [SuiteOf("antiphon", chunks, nodes)];
        }
        object OrderPlan(object doc, IEnumerable<object> groups)
        {
            var prepared = NewSuite(groups);
            var resolved = PriorFailureHistory.Resolve(doc, prepared, "Full", null, null);
            return Sched(prepared, Arr(resolved, "members"), previous: doc, candidate: B40, now: w.Now);
        }
        string Classes(object g) => J(Arr(g, "classes").Select(PsValue.Str).OrderBy(c => c, NightlyPlanCases.SortObject));
        var oldCounts = new Dictionary<string, int> { [alpha] = 1700, [beta] = 100, [gamma] = 100 };
        var newCounts = new Dictionary<string, int> { [alpha] = 1851, [beta] = 100, [gamma] = 100 };
        var old = ConvertCounts(oldCounts);
        var packed = ConvertCounts(newCounts);
        var delta151 = newCounts.Values.Sum() - oldCounts.Values.Sum();
        var moved = false;
        foreach (var group in old)
        {
            var other = packed.Where(p => S(p, "id") == S(group, "id")).ToArray();
            if (Classes(group) != (other.Length == 1 ? Classes(other[0]) : "")) moved = true;
        }
        var betaOld = S(old.First(g => HasClass(g, beta)), "id");
        var rows = old.Select(g => (object)SeedRow("antiphon", S(g, "id"), Arr(g, "classes").Select(PsValue.Str).ToArray(), true, true, stamp)).ToList();
        rows.Add(SeedRow("antiphon", "seed-only", [zeta], true, true, stamp));
        var doc = WritePackSeed(rows.ToArray(), Objs(Failure("antiphon", betaOld, beta, "M1", "Failed"), Failure("antiphon", betaOld, beta, "M2", "Failed")));
        var planned = packed.Append(Rec(("id", "never-added"), ("classes", Strs(never)))).ToArray();
        var betaNew = S(planned.First(g => HasClass(g, beta)), "id");
        var schedule = OrderPlan(doc, planned);
        var again = OrderPlan(doc, planned);
        var ids = Ids(schedule);
        var expected = planned.Select(g => "antiphon/" + S(g, "id")).OrderBy(k => k, StringComparer.Ordinal);
        var actual = Keys(schedule).OrderBy(k => k, StringComparer.Ordinal);
        var packOk = moved && delta151 == 151 && Starts(S(schedule, "orderingReason"), "operator-seed-order") && ids.FirstOrDefault() == betaNew
            && T(Arr(schedule, "entries").FirstOrDefault(), "priorFailure") && !ids.Contains("seed-only") && ids.Contains("never-added")
            && J(ids) == J(Ids(again)) && J(expected) == J(actual);
        object Group(string id, string[] classes, string[]? methods = null) => methods == null
            ? Rec(("id", id), ("classes", Strs(classes)))
            : Rec(("id", id), ("classes", Strs(classes)), ("methods", Strs(methods)));
        object[] splitGroups =
        [
            Group("passed-only", [alpha]), Group("mixed", [beta, gamma]), Group("split-a", [beta], ["M1"]), Group("split-b", [beta], ["M2", "M3"]),
            Group("split-c", [beta], ["M4"]), Group("unfinished", [delta]), Group("never", [never]),
        ];
        object[] splitRows =
        [
            SeedRow("antiphon", "old-alpha", [alpha], true, true, stamp), SeedRow("antiphon", "old-beta", [beta], true, true, stamp),
            SeedRow("antiphon", "old-gamma", [gamma], true, true, stamp), SeedRow("antiphon", "old-delta", [delta], false, true, stamp),
            SeedRow("antiphon", "old-zeta", [zeta], true, true, stamp),
        ];
        var splitDoc = WritePackSeed(splitRows, Objs(Failure("antiphon", "old-beta", beta, "M1", "Failed"), Failure("antiphon", "old-beta", beta, "M2", "Failed")));
        var split = OrderPlan(splitDoc, splitGroups);
        var splitIds = J(Ids(split));
        var byId = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Arr(split, "entries")) byId[S(entry, "chunk")] = entry;
        object? Entry(string id) => byId.TryGetValue(id, out var e) ? e : null;
        // mixed has no node list, so it holds the whole failed class and leads. split-a/b/c carry disjoint node lists like the
        // real packer's split chunks: a and b hold failed M1/M2 and lead, c holds only M4 and follows as a sibling. The torn
        // (unfinished) chunk resumes the lane before the never-seen one.
        var ruleOk = Starts(S(split, "orderingReason"), "operator-seed-order") && splitIds == "mixed,split-a,split-b,unfinished,never,split-c,passed-only"
            && T(Entry("mixed"), "priorFailure") && T(Entry("split-a"), "priorFailure") && T(Entry("split-b"), "priorFailure") && !T(Entry("unfinished"), "priorFailure")
            && !T(Entry("never"), "priorFailure") && !T(Entry("passed-only"), "priorFailure") && !Ids(split).Contains("old-zeta");
        return (packOk && ruleOk, "pack=" + S(schedule, "orderingReason") + "/" + J(ids) + "; moved=" + B(moved) + "; delta=" + delta151 + "; beta=" + betaOld + "->" + betaNew
            + "; split=" + S(split, "orderingReason") + "/" + splitIds);
    });

    [Test, Timeout(300_000)]
    public Task FailedMethodBands(CancellationToken token) => Case(nameof(FailedMethodBands), w =>
    {
        var bandN = 0;
        var stamp = Stamp(w.Now.AddHours(-3));
        var openStamp = Stamp(w.Now.AddHours(-2));
        object WriteBandSeed(object[] rows, object[] failed) => WriteSeed(w.PathOf("band-seed-" + ++bandN + ".json"), "synthetic-method", stamp, rows, failed);
        object BandChunk(string id, string cls, string[] methods, bool omitNodes)
        {
            var chunk = Rec(("id", id), ("classes", Strs(cls)));
            if (!omitNodes) chunk.Add("nodes", methods.Select(m => (object)Node(cls + "/" + m, cls, m, "T")).ToArray());
            return chunk;
        }
        object BandOrder(object doc, object[] chunks)
        {
            var nodes = new List<object>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chunk in chunks)
            {
                var cls = PsValue.Str(Arr(chunk, "classes").FirstOrDefault());
                var chunkNodes = ((PlainObject)chunk).Has("nodes") && P(chunk, "nodes") != null ? Arr(chunk, "nodes") : [];
                if (chunkNodes.Length == 0)
                {
                    var key = cls + "/Run";
                    if (seen.Add(key)) nodes.Add(Node(key, cls, "Run", "T"));
                }
                else
                    foreach (var node in chunkNodes) if (seen.Add(S(node, "Uid"))) nodes.Add(node);
            }
            object[] suite = [SuiteOf("antiphon", chunks, nodes.ToArray())];
            var resolved = PriorFailureHistory.Resolve(doc, suite, "Full", null, null);
            return Sched(suite, Arr(resolved, "members"), previous: doc, candidate: B40, now: w.Now);
        }
        const string split = "Synthetic.Method.Split", torn = "Synthetic.Method.Torn", ok = "Synthetic.Method.Ok";
        object[] chunks =
        [
            BandChunk("s0", split, ["M0"], false), BandChunk("s1", split, ["M1"], false), BandChunk("s2", split, ["M2"], false), BandChunk("s3", split, ["M3"], false),
            BandChunk("s4", split, ["M4(case)"], false), BandChunk("s5", split, ["M5"], false), BandChunk("whole", split, [], false),
            BandChunk("torn", torn, [], false), BandChunk("never", "Synthetic.Method.Never", [], false), BandChunk("ok", ok, [], false),
        ];
        var doc = WriteBandSeed(
            Objs(SeedRow("antiphon", "seed-split", [split], true, true, stamp), SeedRow("antiphon", "seed-torn", [torn], false, true, openStamp),
                SeedRow("antiphon", "seed-ok", [ok], true, true, stamp)),
            Objs(Failure("antiphon", "seed-split", split, "M1", "Failed"), Failure("antiphon", "seed-split", split, "M4", "Failed")));
        var splitIds = J(Ids(BandOrder(doc, chunks)));
        // The torn chunk resumes the lane, so it runs before the never-seen chunk.
        var splitOk = splitIds == "s1,s4,whole,torn,never,s0,s2,s3,s5,ok";
        const string red = "Synthetic.Method.Red";
        object[] missingChunks = [BandChunk("bare", red, [], true), BandChunk("sib", red, ["M9"], false), BandChunk("open", torn, [], false), BandChunk("ok", ok, [], false)];
        var missingDoc = WriteBandSeed(
            Objs(SeedRow("antiphon", "seed-red", [red], true, true, stamp), SeedRow("antiphon", "seed-torn", [torn], false, true, openStamp),
                SeedRow("antiphon", "seed-ok", [ok], true, true, stamp)),
            Objs(Failure("antiphon", "seed-red", red, "Run", "Failed"), Failure("antiphon", "seed-red", red, "RunAgain", "Timeout")));
        var missingIds = J(Ids(BandOrder(missingDoc, missingChunks)));
        var missingOk = missingIds == "bare,open,sib,ok";
        const string other = "Synthetic.Method.Other";
        object[] vanishedChunks =
        [
            BandChunk("r0", red, ["M0"], false), BandChunk("r1", red, ["M1"], false), BandChunk("r2", red, ["M2"], false), BandChunk("other", other, [], false),
            BandChunk("open", torn, [], false),
        ];
        var vanishedDoc = WriteBandSeed(
            Objs(SeedRow("antiphon", "seed-red", [red], true, true, stamp), SeedRow("antiphon", "seed-other", [other], true, true, stamp),
                SeedRow("antiphon", "seed-torn", [torn], false, true, openStamp)),
            Objs(Failure("antiphon", "seed-red", red, "Gone", "Failed"), Failure("antiphon", "seed-other", other, "Run", "Error")));
        var vanishedIds = J(Ids(BandOrder(vanishedDoc, vanishedChunks)));
        var vanishedOk = vanishedIds == "other,open,r0,r1,r2";
        object[] movedChunks = [BandChunk("old", split, ["M0"], false), BandChunk("new", split, ["M1", "M2"], false)];
        var movedDoc = WriteBandSeed(Objs(SeedRow("antiphon", "old", [split], true, true, stamp)),
            Objs(Failure("antiphon", "old", split, "M1", "Failed"), Failure("antiphon", "old", split, "M2", "Aborted")));
        var movedIds = J(Ids(BandOrder(movedDoc, movedChunks)));
        var movedOk = movedIds == "new,old";
        var blank = Copy(doc);
        blank.Remove("sourceSha");
        var blankSchedule = BandOrder(blank, chunks);
        var badSha = Copy(doc);
        badSha["sourceSha"] = new string('G', 40);
        var badSchedule = BandOrder(badSha, chunks);
        var noRun = Copy(doc);
        noRun.Remove("sourceRunId");
        var noRunSchedule = BandOrder(noRun, chunks);
        var canonical = J(chunks.Select(c => S(c, "id")));
        var corruptOk = S(blankSchedule, "orderingReason") == "seed-candidate-missing" && J(Ids(blankSchedule)) == canonical
            && S(badSchedule, "orderingReason") == "seed-candidate-malformed" && J(Ids(badSchedule)) == canonical
            && S(noRunSchedule, "orderingReason") == "seed-run-missing" && J(Ids(noRunSchedule)) == canonical;
        return (splitOk && missingOk && vanishedOk && movedOk && corruptOk, "split=" + splitIds + "; missing=" + missingIds + "; vanished=" + vanishedIds + "; moved=" + movedIds
            + "; blank=" + S(blankSchedule, "orderingReason") + "; sha=" + S(badSchedule, "orderingReason") + "; run=" + S(noRunSchedule, "orderingReason"));
    });

    [Test, Timeout(300_000)]
    public Task RealPackerFailedMethodFollows(CancellationToken token) => Case(nameof(RealPackerFailedMethodFollows), w =>
    {
        const string cls = "Antiphon.Tests.PackWitness.LandProbe", unfinished = "Antiphon.Tests.PackWitness.Delta", never = "Antiphon.Tests.OtherPack.NeverSeen";
        var stamp = Stamp(w.Now.AddHours(-3));
        static object[] ProbeNodes(string bare, IEnumerable<string> methods, string[] categories)
        {
            var type = bare.Split('.')[^1];
            return methods.Select(m => (object)Rec(("ClassName", bare), ("Method", m), ("Type", type), ("Categories", Strs(categories)), ("Uid", bare + "/" + m))).ToArray();
        }
        static object[] ProbePlan(object[] nodes, string[] classes)
        {
            var leaves = classes.Select(name => (object)Rec(("id", "leaf-" + name.Split('.')[^1]), ("classes", Strs(name)), ("exactFilter", ""), ("nodes", Array.Empty<object>()))).ToArray();
            return NightlyTestsPlan.ProfileLaunchGroups(leaves, nodes, null, NightlyPlanCases.Costs(), null);
        }
        var oldMethods = Enumerable.Range(0, 320).Select(i => "M" + i.ToString("000", CultureInfo.InvariantCulture)).ToArray();
        oldMethods[200] = "M200(case)";
        var added = Enumerable.Range(0, 20).Select(i => "A" + i.ToString("00", CultureInfo.InvariantCulture)).ToArray();
        var newMethods = added.Concat(oldMethods).ToArray();
        var oldNodes = ProbeNodes(cls, oldMethods, ["Slow"]).Concat(ProbeNodes(unfinished, ["Run"], [])).Concat(ProbeNodes(never, ["Run"], [])).ToArray();
        var newNodes = ProbeNodes(cls, newMethods, ["Slow"]).Concat(ProbeNodes(unfinished, ["Run"], [])).Concat(ProbeNodes(never, ["Run"], [])).ToArray();
        string[] classes = [cls, unfinished, never];
        var oldPlan = ProbePlan(oldNodes, classes);
        var newPlan = ProbePlan(newNodes, classes);
        static bool HoldsMethod(object group, string method) => Arr(group, "nodes").Any(n => StoppedSeed.MethodName(S(n, "Method")) == method);
        static string FindMethodChunk(object[] plan, string method) => plan.Where(g => HoldsMethod(g, method)).Select(g => S(g, "id")).FirstOrDefault() ?? "";
        var oldId = FindMethodChunk(oldPlan, "M200");
        var newId = FindMethodChunk(newPlan, "M200");
        var methodChunks = newPlan.Where(g => HoldsMethod(g, "M200")).Select(g => S(g, "id")).ToArray();
        object[] rows = [SeedRow("antiphon", oldId, [cls], true, true, stamp), SeedRow("antiphon", "seed-delta", [unfinished], false, true, stamp)];
        object[] failed = [Failure("antiphon", oldId, cls, "M200", "Failed"), Failure("antiphon", oldId, cls, "M200(case)", "Failed")];
        var doc = WriteSeed(w.PathOf("packer-method-seed.json"), "synthetic-packer-method", stamp, rows, failed);
        var suiteChunks = newPlan.Select(g => (object)Rec(("id", S(g, "id")), ("classes", Arr(g, "classes")), ("nodes", Arr(g, "nodes")))).ToArray();
        object[] suite = [SuiteOf("antiphon", suiteChunks, newNodes)];
        var resolved = PriorFailureHistory.Resolve(doc, suite, "Full", null, null);
        var schedule = Sched(suite, Arr(resolved, "members"), previous: doc, candidate: B40, now: w.Now);
        var again = Sched(suite, Arr(resolved, "members"), previous: doc, candidate: B40, now: w.Now);
        var ids = Ids(schedule);
        var lead = Range(ids, 0, methodChunks.Length - 1);
        var leadOk = J(lead.OrderBy(x => x, NightlyPlanCases.SortObject)) == J(methodChunks.OrderBy(x => x, NightlyPlanCases.SortObject)) && ids.FirstOrDefault() == newId;
        var sibling = newPlan.Where(g => HasClass(g, cls) && !methodChunks.Contains(S(g, "id"))).Select(g => S(g, "id")).FirstOrDefault() ?? "";
        var openId = S(newPlan.FirstOrDefault(g => HasClass(g, unfinished)), "id");
        var neverId = S(newPlan.FirstOrDefault(g => HasClass(g, never)), "id");
        var openAt = Array.IndexOf(ids, openId);
        var neverAt = Array.IndexOf(ids, neverId);
        var siblingAt = Array.IndexOf(ids, sibling);
        var moved = oldId != newId && !string.IsNullOrWhiteSpace(oldId) && methodChunks.Length == 1 && !string.IsNullOrWhiteSpace(sibling);
        var packOk = moved && leadOk && openAt == methodChunks.Length && neverAt == openAt + 1 && siblingAt > openAt && J(ids) == J(Ids(again))
            && Starts(S(schedule, "orderingReason"), "operator-seed-order");
        return (packOk, "old=" + oldId + "; new=" + newId + "; lead=" + J(lead) + "; openAt=" + openAt + "; sibling=" + sibling + "@" + siblingAt
            + "; reason=" + S(schedule, "orderingReason") + "; ids=" + J(ids));
    });

    [Test, Timeout(300_000)]
    public Task RealShapeFailedMethodBand(CancellationToken token) => Case(nameof(RealShapeFailedMethodBand), w =>
    {
        var stamp = Stamp(w.Now.AddHours(-4));
        var openStamp = Stamp(w.Now.AddHours(-1));
        var passStamp = Stamp(w.Now.AddHours(-2));
        const string remote = "Antiphon.Tests.SeedShape.RemoteSplit", land = "Antiphon.E2E.SeedShape.LandSplit";
        (string Lane, int Count)[] lanes = [("antiphon", 638), ("e2e", 75), ("agents-pty", 9), ("markdown-pdf", 11), ("session-runner", 53), ("pty-host", 53), ("messaging", 1), ("observability", 1)];
        var suites = new List<PlainObject>();
        var seedRows = new List<object>();
        var failed = new List<object>();
        var failedIds = new List<string>();
        var suiteNodes = new List<object>();
        void AddNode(string cls, string method) => suiteNodes.Add(Node(cls + "/" + method, cls, method, "T"));
        object AddChunk(string lane, string id, string cls, string kind, string method, bool split)
        {
            object[] nodes = [];
            if (split)
            {
                nodes = [Node(cls + "/" + method, cls, method, "T")];
                AddNode(cls, method);
            }
            else if (kind == "fail") AddNode(cls, "Run");
            var when = kind == "open" ? openStamp : kind == "pass" ? passStamp : stamp;
            seedRows.Add(SeedRow(lane, id, [cls], kind != "open", true, when));
            if (kind == "fail")
            {
                failed.Add(Failure(lane, id, cls, method, "Failed"));
                failedIds.Add(id);
            }
            return Rec(("id", id), ("classes", Strs(cls)), ("nodes", nodes));
        }
        foreach (var (lane, count) in lanes)
        {
            var chunks = new List<object>();
            var n = 0;
            if (lane == "antiphon")
            {
                for (var i = 0; i < 11; i++) { chunks.Add(AddChunk(lane, "fail-antiphon-" + i, "Antiphon.Tests.SeedShape.Fail" + i, "fail", "Run", false)); n++; }
                for (var i = 0; i < 181; i++) { chunks.Add(AddChunk(lane, "rs-" + i, remote, i == 10 || i == 20 ? "fail" : "sibling", "M" + i, true)); n++; }
                for (var i = 0; i < 8; i++) { chunks.Add(AddChunk(lane, "open-antiphon-" + i, "Antiphon.Tests.SeedShape.Open" + i, "open", "Run", false)); n++; }
            }
            else if (lane == "e2e")
            {
                chunks.Add(AddChunk(lane, "fail-e2e-0", "Antiphon.E2E.SeedShape.Fail0", "fail", "Run", false)); n++;
                int[] failedSplit = [1, 2, 3, 4, 32, 33];
                for (var i = 0; i < 36; i++) { chunks.Add(AddChunk(lane, "e2-" + i, land, failedSplit.Contains(i) ? "fail" : "sibling", "E" + i, true)); n++; }
            }
            else if (lane == "agents-pty")
            {
                chunks.Add(AddChunk(lane, "fail-agents-0", "Antiphon.Tests.SeedShape.AgentsFail", "fail", "Run", false)); n++;
                chunks.Add(AddChunk(lane, "open-agents-0", "Antiphon.Tests.SeedShape.AgentsOpen", "open", "Run", false)); n++;
            }
            else if (lane == "session-runner")
            {
                for (var i = 0; i < 2; i++) { chunks.Add(AddChunk(lane, "fail-sr-" + i, "Antiphon.Tests.SeedShape.SrFail" + i, "fail", "Run", false)); n++; }
            }
            else if (lane == "pty-host")
            {
                for (var i = 0; i < 2; i++) { chunks.Add(AddChunk(lane, "fail-pty-" + i, "Antiphon.Tests.SeedShape.PtyFail" + i, "fail", "Run", false)); n++; }
            }
            while (n < count)
            {
                chunks.Add(AddChunk(lane, "pass-" + lane + "-" + n, "Antiphon.Tests.SeedShape.Pass" + lane.Replace("-", "") + n, "pass", "Run", false));
                n++;
            }
            if (n != count) throw new InvalidOperationException("lane count " + lane + " " + n);
            suites.Add(SuiteOf(lane, chunks.ToArray(), []));
        }
        if (suites.Count != 8 || failedIds.Count != 25) throw new InvalidOperationException("shape suites=" + suites.Count + " failed=" + failedIds.Count);
        var total = 0;
        foreach (var suite in suites)
        {
            suite["nodes"] = suiteNodes.ToArray();
            total += Arr(suite, "chunks").Length;
        }
        if (total != 841) throw new InvalidOperationException("shape total " + total);
        object[] prepared = suites.Cast<object>().ToArray();
        var doc = WriteSeed(w.PathOf("shape-seed.json"), "synthetic-shape", stamp, seedRows.ToArray(), failed.ToArray());
        var resolved = PriorFailureHistory.Resolve(doc, prepared, "Full", null, null);
        var schedule = Sched(prepared, Arr(resolved, "members"), previous: doc, candidate: B40, now: w.Now);
        var again = Sched(prepared, Arr(resolved, "members"), previous: doc, candidate: B40, now: w.Now);
        var ids = Ids(schedule);
        var lead = Range(ids, 0, 24);
        var leadOk = J(lead) == J(failedIds);
        var openAt = Array.IndexOf(ids, "open-antiphon-0");
        var siblingAt = Array.IndexOf(ids, "rs-0");
        var e2Sibling = Array.IndexOf(ids, "e2-0");
        var unique = ids.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var unresolvedCount = Arr(resolved, "unresolved").Length;
        var shapeOk = leadOk && openAt == 25 && siblingAt > openAt && e2Sibling > openAt && unique == 841 && ids.Length == 841 && J(ids) == J(Ids(again))
            && Starts(S(schedule, "orderingReason"), "operator-seed-order") && unresolvedCount == 0;
        return (shapeOk, "lead=" + J(lead) + "; openAt=" + openAt + "; rs0=" + siblingAt + "; e20=" + e2Sibling + "; unique=" + unique + "; unresolved=" + unresolvedCount
            + "; reason=" + S(schedule, "orderingReason"));
    });

    [Test, Timeout(300_000)]
    public Task UnfinishedRunLaneResume(CancellationToken token) => Case(nameof(UnfinishedRunLaneResume), w =>
    {
        // A trimmed seed in the exporter's own shape for an UNFINISHED run (like a074ff9a: a few torn chunks, many never
        // started), read through the real ingress with acknowledgement.
        var stamp = Stamp(w.Now.AddHours(-5));
        var passStamp = Stamp(w.Now.AddHours(-4));
        var oldTorn = Stamp(w.Now.AddHours(-3));
        var newTorn = Stamp(w.Now.AddHours(-2));
        var seedRows = new List<object>();
        var failed = new List<object>();
        var laneSuites = new List<object>();
        void AddLaneRow(string lane, string cls, string kind, string when)
        {
            if (kind == "unseen") return;
            var row = Rec(("suite", lane), ("chunk", "old-" + cls.Split('.')[^1]), ("classes", Strs(cls)), ("membershipSource", "plan"),
                ("completed", kind is "pass" or "fail"), ("started", kind != "never"), ("lastRunAtUtc", kind == "never" ? null : when));
            if (kind == "torn") row["unfinishedReason"] = "trx-open";
            seedRows.Add(row);
        }
        // Lane plan: id, class, seed state, node methods (split chunks only), failed methods.
        (string Lane, (string Id, string Class, string Kind, string[] Methods, string[] Failed)[] Specs)[] plan =
        [
            ("antiphon",
            [
                ("a-p0", "Antiphon.Tests.Resume.P0", "pass", [], []), ("a-h", "Antiphon.Tests.Resume.Hole", "never", [], []),
                ("a-p1", "Antiphon.Tests.Resume.P1", "pass", [], []), ("a-f", "Antiphon.Tests.Resume.Fail", "fail", [], ["Run"]),
                ("a-i", "Antiphon.Tests.Resume.Torn", "torn", [], []), ("a-u0", "Antiphon.Tests.Resume.U0", "never", [], []),
                ("a-new", "Antiphon.Tests.Resume.Added", "unseen", [], []), ("a-u1", "Antiphon.Tests.Resume.U1", "never", [], []),
                ("a-s0", "Antiphon.Tests.Resume.Split", "fail", ["M0"], ["M0"]), ("a-s1", "Antiphon.Tests.Resume.Split", "fail", ["M1"], []),
            ]),
            ("e2e",
            [
                ("e-u0", "Antiphon.E2E.Resume.U0", "never", [], []), ("e-i", "Antiphon.E2E.Resume.Torn", "torn-old", [], []),
                ("e-u1", "Antiphon.E2E.Resume.U1", "never", [], []), ("e-i2", "Antiphon.E2E.Resume.Torn2", "torn-old", [], []),
                ("e-p0", "Antiphon.E2E.Resume.P0", "pass", [], []),
            ]),
            ("session-runner",
            [
                ("s-u0", "Antiphon.Sr.Resume.U0", "never", [], []), ("s-p0", "Antiphon.Sr.Resume.P0", "pass", [], []),
                ("s-p1", "Antiphon.Sr.Resume.P1", "pass", [], []), ("s-u1", "Antiphon.Sr.Resume.U1", "never", [], []),
                ("s-u2", "Antiphon.Sr.Resume.U2", "never", [], []),
            ]),
        ];
        var seenClass = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (lane, specs) in plan)
        {
            var chunks = new List<object>();
            var suiteNodes = new List<object>();
            foreach (var spec in specs)
            {
                var nodes = spec.Methods.Select(m => (object)Node(spec.Class + "/" + m, spec.Class, m, "T")).ToArray();
                suiteNodes.AddRange(nodes);
                if (nodes.Length == 0) suiteNodes.Add(Node(spec.Class + "/Run", spec.Class, "Run", "T"));
                chunks.Add(Rec(("id", spec.Id), ("classes", Strs(spec.Class)), ("nodes", nodes)));
                var seedKind = spec.Kind.StartsWith("torn", StringComparison.Ordinal) ? "torn" : spec.Kind;
                var when = spec.Kind == "torn" ? newTorn : spec.Kind == "torn-old" ? oldTorn : spec.Kind == "pass" ? passStamp : stamp;
                if (seenClass.Add(lane + "/" + spec.Class)) AddLaneRow(lane, spec.Class, seedKind, when);
                foreach (var m in spec.Failed) failed.Add(Failure(lane, "old-" + spec.Class.Split('.')[^1], spec.Class, m, "Failed"));
            }
            laneSuites.Add(SuiteOf(lane, chunks.ToArray(), suiteNodes.ToArray()));
        }
        object[] evidence = [Rec(("path", "timing-unfinished/groups-planned-antiphon.json"), ("sha256", new string('d', 64)))];
        var payload = Rec(("sourceObservedAtUtc", newTorn), ("chunks", seedRows.ToArray()), ("failedTests", failed.ToArray()), ("evidence", evidence));
        var path = w.PathOf("unfinished-seed.json");
        WriteJson(path, Envelope("a074ff9a-shape", "rc-synthetic-unfinished", C40, Stamp(w.Now.AddHours(-1)), payload));
        var received = PriorFailureHistory.InitializeDispatch(path, "", "", B40, true, "rc-synthetic-unfinished", DateTime.UtcNow);
        if (P(received, "History") == null) throw new InvalidOperationException("ingress " + S(received, "FallbackReason"));
        var prepared = laneSuites.ToArray();
        var resolved = PriorFailureHistory.Resolve(P(received, "History"), prepared, "Full", null, null);
        var schedule = Sched(prepared, Arr(resolved, "members"), previous: P(received, "PreviousRun"), candidate: B40, now: w.Now);
        var ids = J(Ids(schedule));
        // Contract: failed-method chunks; every lane's interrupted chunks (lane order, from the lane's first interrupted
        // chunk); every lane's untouched chunks from there, wrapping to earlier holes; siblings of a placed failure; passed. A
        // lane with no interrupted chunk resumes after its last pass.
        const string want = "a-f,a-s0,a-i,e-i,e-i2,a-u0,a-new,a-u1,a-h,e-u1,e-u0,s-u1,s-u2,s-u0,a-s1,a-p0,a-p1,e-p0,s-p0,s-p1";
        var laneText = J(Arr(schedule, "lanes").Select(l => S(l, "suite") + ":" + S(l, "resumeChunk") + ":" + S(l, "lastPassedChunk")));
        var again = J(Ids(Sched(prepared, Arr(resolved, "members"), previous: P(received, "PreviousRun"), candidate: B40, now: w.Now)));
        return (ids == want && again == ids && laneText == "antiphon:a-i:a-p1,e2e:e-i:e-p0,session-runner:s-u1:s-p1"
                && Starts(S(schedule, "orderingReason"), "operator-seed-order") && Arr(resolved, "unresolved").Length == 0,
            "ids=" + ids + "; lanes=" + laneText + "; reason=" + S(schedule, "orderingReason"));
    });

    [Test, Timeout(300_000)]
    public Task SkipRowsCountAsCompleted(CancellationToken token) => Case(nameof(SkipRowsCountAsCompleted), w =>
    {
        // MTP writes an explicit [Skip] as outcome NotExecuted. A chunk whose only not-executed rows are explicit skips is
        // completed; any other not-executed outcome, or skips under an aborted run summary, leaves the chunk unfinished.
        var trx = Path.Combine(w.Run, "antiphon-all-group-1.trx");
        var original = File.ReadAllText(trx);
        const string skipDefinition = "<UnitTest id=\"2\"><TestMethod className=\"Synthetic.antiphon.Passed\" name=\"Skipped\" /></UnitTest></TestDefinitions>";
        object? ExportVariant(string name, string outcome, string summary)
        {
            var text = original.Replace("</TestDefinitions>", skipDefinition).Replace("</Results>", "<UnitTestResult testId=\"2\" outcome=\"" + outcome + "\" /></Results>")
                .Replace("<ResultSummary outcome=\"Completed\">", "<ResultSummary outcome=\"" + summary + "\">");
            File.WriteAllText(trx, text);
            var outPath = w.PathOf("skip-" + name + ".json");
            StoppedSeedFiles.Export(w.Run, outPath, "", "", "", "");
            return Row(StoppedSeedFiles.Read(outPath, ""), "antiphon", "all-group-1");
        }
        object? skip, named, pending, aborted;
        try
        {
            skip = ExportVariant("notexecuted", "NotExecuted", "Completed");
            named = ExportVariant("skipped", "Skipped", "Completed");
            pending = ExportVariant("pending", "Pending", "Completed");
            aborted = ExportVariant("aborted", "NotExecuted", "Aborted");
        }
        finally { File.WriteAllText(trx, original); }
        return (Is(skip, "completed", true) && Is(named, "completed", true) && Is(pending, "completed", false) && Is(aborted, "completed", false)
                && T(skip, "started") && T(pending, "started"),
            "notExecuted=" + S(skip, "completed") + "; skipped=" + S(named, "completed") + "; pending=" + S(pending, "completed") + "; abortedSummary=" + S(aborted, "completed"));
    });

    [Test, Timeout(300_000)]
    public Task SummaryPrefixTamper(CancellationToken token) => Case(nameof(SummaryPrefixTamper), w =>
    {
        var layout = SiblingLayout(w.PathOf("prefix-logs"), RunId, RunName);
        WriteSeedSummary(layout.Run, layout.Timing, RunId, A40);
        var summaryPath = Path.Combine(layout.Run, "summary.json");
        var prefix = StoppedSeedFiles.SummaryPrefix(summaryPath);
        var prefixLength = PsValue.Str(P(prefix, "Length"));
        var bytes = File.ReadAllBytes(summaryPath);
        // CutBeforeBulk returns the opening quote of the depth-1 bulk key. A flip there hides the key, so the prefix reader
        // parses the tail. Change a byte inside the ignored INVALID value, after that key.
        var marker = new UTF8Encoding(false).GetBytes(",\"timingChunks\":");
        var at = -1;
        for (var i = 0; i <= bytes.Length - marker.Length; i++)
            if (bytes.AsSpan(i, marker.Length).SequenceEqual(marker)) at = i;
        // The cut is the key's opening quote, one byte after this comma.
        if ((at + 1).ToString(CultureInfo.InvariantCulture) != prefixLength) throw new InvalidOperationException("bulk marker is not the prefix cut");
        var flip = at + marker.Length + 2;
        if (flip >= bytes.Length) throw new InvalidOperationException("decoy offset past end");
        var decoy = (byte[])bytes.Clone();
        decoy[flip] ^= 1;
        File.WriteAllBytes(summaryPath, decoy);
        bool decoyOk = false;
        string decoyDetail;
        try
        {
            StoppedSeedFiles.Export(layout.Run, w.PathOf("decoy-seed.json"), "", "rc-synthetic-old", A40, "");
            decoyOk = true;
            decoyDetail = "accepted";
        }
        catch (Exception ex) { decoyDetail = Message(ex); }
        File.WriteAllBytes(summaryPath, bytes);
        var done = false;
        StoppedSeedFiles.ReadBarrier = _ =>
        {
            if (done) return;
            done = true;
            var live = File.ReadAllBytes(summaryPath);
            live[8] ^= 1;
            File.WriteAllBytes(summaryPath, live);
        };
        string tamperDetail;
        try
        {
            StoppedSeedFiles.Export(layout.Run, w.PathOf("prefix-tamper-seed.json"), "", "rc-synthetic-old", A40, "");
            tamperDetail = "accepted";
        }
        catch (Exception ex) { tamperDetail = Message(ex); }
        finally { StoppedSeedFiles.ReadBarrier = null; }
        return (decoyOk && Has(tamperDetail, "seed-summary-tampered"), "decoy=" + decoyDetail + "; tamper=" + tamperDetail + "; cut=" + prefixLength);
    });

    [Test, Timeout(300_000)]
    public Task WindowsAncestorSwapRefused(CancellationToken token) => Case(nameof(WindowsAncestorSwapRefused), w =>
    {
        HistoryNodeIdentity.ForceWindows = true;
        try
        {
            (string Grand, string Leaf) NewLeaf(string name)
            {
                HistoryLinkCache.Reset();
                var grand = w.PathOf(name);
                var mid = Path.Combine(grand, "mid");
                Directory.CreateDirectory(mid);
                var leaf = Path.Combine(mid, "leaf.txt");
                File.WriteAllText(leaf, "real");
                HistoryLinkCache.AssertNoLink(leaf);
                return (grand, leaf);
            }
            var real = NewLeaf("win-grand");
            Directory.Move(real.Grand, w.PathOf("win-grand-held"));
            Directory.CreateDirectory(Path.Combine(real.Grand, "mid"));
            File.WriteAllText(real.Leaf, "FORGED");
            var dirMessage = Refusal(() => HistoryLinkCache.AssertNoLink(real.Leaf));
            var linked = NewLeaf("win-link");
            var forged = w.PathOf("win-link-forged");
            Directory.Move(linked.Grand, w.PathOf("win-link-held"));
            Directory.CreateDirectory(Path.Combine(forged, "mid"));
            File.WriteAllText(Path.Combine(forged, "mid", "leaf.txt"), "FORGED");
            Directory.CreateSymbolicLink(linked.Grand, forged);
            var linkMessage = Refusal(() => HistoryLinkCache.AssertNoLink(linked.Leaf));
            return (Has(dirMessage, "evidence-link") && Has(linkMessage, "evidence-link"), "dir=" + dirMessage + "; link=" + linkMessage);
        }
        finally
        {
            HistoryNodeIdentity.ForceWindows = false;
            HistoryLinkCache.Reset();
        }
    });

    // Provisional: one measured export of a 3,300 + 1,700 file layout (see the detail).
    [Test, Timeout(300_000)]
    public Task ScaleExportBound(CancellationToken token) => Case(nameof(ScaleExportBound), w =>
    {
        var logs = w.PathOf("scale-logs");
        var runDir = Path.Combine(logs, RunName);
        var timing = Path.Combine(logs, "timing-" + RunId);
        Directory.CreateDirectory(runDir);
        Directory.CreateDirectory(timing);
        CopyFiles(FixtureDirectory, runDir);
        var ownerPath = Path.Combine(runDir, "owner.json");
        File.WriteAllText(ownerPath, File.ReadAllText(ownerPath).Replace("\"runId\": \"synthetic-stopped\"", "\"runId\": \"" + RunId + "\""));
        foreach (var plan in Directory.GetFiles(runDir, "groups-planned*.json"))
        {
            File.WriteAllText(Path.Combine(timing, Path.GetFileName(plan)), File.ReadAllText(plan).Replace("\"runId\": \"synthetic-stopped\"", "\"runId\": \"" + RunId + "\""));
            File.Delete(plan);
        }
        File.WriteAllText(Path.Combine(timing, "groups-planned-index.json"), "{\"version\":1}");
        var summaryPath = Path.Combine(runDir, "summary.json");
        File.WriteAllText(summaryPath, SummaryHeader(timing, RunId, A40) + ",\"timingChunks\":\"pad\"" + new string(' ', 4194304) + "}");
        var runCount = Directory.GetFiles(runDir).Length;
        var timingCount = Directory.GetFiles(timing).Length;
        for (var i = runCount; i < 3300; i++) File.WriteAllText(Path.Combine(runDir, "pad-" + i.ToString("0000", CultureInfo.InvariantCulture) + ".txt"), "x");
        for (var i = timingCount; i < 1700; i++) File.WriteAllText(Path.Combine(timing, "pad-" + i.ToString("0000", CultureInfo.InvariantCulture) + ".txt"), "x");
        var outPath = w.PathOf("scale-seed.json");
        var watch = Stopwatch.StartNew();
        StoppedSeedFiles.Export(runDir, outPath, "", "rc-synthetic-old", A40, "");
        watch.Stop();
        var seconds = watch.Elapsed.TotalSeconds;
        var doc = StoppedSeedFiles.Read(outPath, "");
        var rows = Arr(doc, "chunks").Length;
        // Provisional n=1 on this host: export 13.175s. Bound is 27s, 2x that sample.
        return (seconds <= 27 && rows > 0 && S(doc, "sourceRunId") == RunId,
            "exportSeconds=" + seconds.ToString(CultureInfo.InvariantCulture) + "; boundSeconds=27; measuredSampleSeconds=13.175; runFiles=3300; timingFiles=1700; summaryBytes="
            + new FileInfo(summaryPath).Length + "; chunks=" + rows);
    });

    [Test, Timeout(300_000)]
    public Task CumulativeMergeUnion(CancellationToken token) => Case(nameof(CumulativeMergeUnion), async w =>
    {
        const string alpha = "Fixture.Alpha", beta = "Fixture.Beta", gamma = "Fixture.Gamma";
        const string olderObserved = "2026-11-01T00:00:00.0000000+00:00", newerObserved = "2026-11-02T00:00:00.0000000+00:00";
        const string olderCreated = "2026-11-01T01:00:00.0000000+00:00", newerCreated = "2026-11-02T01:00:00.0000000+00:00";
        var older = w.PathOf("union-older.json");
        var newer = w.PathOf("union-newer.json");
        WriteMergeSeed(older, "run-older", "rc-older", A40, olderObserved, olderCreated,
            Objs(MergeChunk("antiphon", "g0", [gamma], true, true, olderObserved), MergeChunk("antiphon", "g1", [alpha], true, true, olderObserved)),
            Objs(MergeFail("antiphon", "g1", alpha, "M1", "Failed"), MergeFail("antiphon", "g0", gamma, "M3", "Failed")),
            Objs(Rec(("path", "older.json"), ("sha256", string.Concat(Enumerable.Repeat("ab", 32))))));
        WriteMergeSeed(newer, "run-newer", "rc-newer", B40, newerObserved, newerCreated,
            Objs(MergeChunk("antiphon", "g1", [alpha], true, true, newerObserved), MergeChunk("antiphon", "g2", [beta], true, true, newerObserved)),
            Objs(MergeFail("antiphon", "g1", alpha, "M2(canceled)", "Failed")),
            Objs(Rec(("path", "newer.json"), ("sha256", string.Concat(Enumerable.Repeat("cd", 32))))));
        var outPath = w.PathOf("union-merged.json");
        var reportPath = w.PathOf("union-report.json");
        var merged = await Merge([older, newer], outPath, reportPath);
        object? doc = null;
        PlainObject? report = null;
        string lead = "", methods = "", chunks = "";
        if (merged.Code == 0)
        {
            doc = StoppedSeedFiles.Read(outPath, "");
            report = MergeReport(reportPath);
            chunks = J(Arr(doc, "chunks").Select(c => S(c, "chunk")));
            methods = J(Arr(doc, "failedTests").Select(f => S(f, "method")));
            object[] plan = [Rec(("id", "g1"), ("classes", Strs(alpha))), Rec(("id", "g2"), ("classes", Strs(beta)))];
            lead = J(Ids(MergeChunkOrder(doc, plan, w.Now)));
        }
        var m1 = MergeIdentity(report, "M1");
        var m2 = MergeIdentity(report, "M2(canceled)");
        var m3 = MergeIdentity(report, "M3");
        var kept = methods == "M1,M2(canceled)" && chunks == "g1,g2" && S(Arr(doc, "evidence").FirstOrDefault(), "path") == "newer.json" && lead.StartsWith("g1,", StringComparison.Ordinal)
            && S(m2, "method") == "M2(canceled)" && S(m1, "status") == "open" && S(m3, "status") == "open" && Is(m3, "placed", false) && !methods.Split(',').Contains("M3");
        var bad = w.PathOf("union-bad.json");
        WriteMergeSeed(bad, "run-bad", "rc-bad", C40, olderObserved, olderCreated, Objs(MergeChunk("antiphon", "g1", [alpha], true, true, olderObserved)),
            Objs(MergeFail("antiphon", "g1", beta, "M9", "Failed")), []);
        var badOut = w.PathOf("union-bad-out.json");
        var badReport = w.PathOf("union-bad-report.json");
        var refused = await Merge([bad], badOut, badReport, cli: true);
        var refusedOk = refused.Code == 2 && !File.Exists(badOut) && !File.Exists(badReport);
        var strictOut = w.PathOf("union-strict.json");
        var strictReport = w.PathOf("union-strict-report.json");
        var strict = await Merge([older, newer], strictOut, strictReport, strict: true);
        var strictDoc = File.Exists(strictOut) ? MergeReport(strictReport) : null;
        var strictOk = strict.Code == 3 && strictDoc != null && PsValue.Double(P(strictDoc, "unplacedCount")) > 0 && Is(MergeIdentity(strictDoc, "M3"), "placed", false);
        return (merged.Code == 0 && kept && refusedOk && strictOk,
            "code=" + merged.Code + "; chunks=" + chunks + "; methods=" + methods + "; lead=" + lead + "; refused=" + refused.Code + "; strict=" + strict.Code + "; text=" + merged.Text
            + (refusedOk ? "" : "\nrefused text=" + refused.Text));
    });

    [Test, Timeout(300_000)]
    public Task CumulativeMergeProvenDrop(CancellationToken token) => Case(nameof(CumulativeMergeProvenDrop), async w =>
    {
        var repo = w.PathOf("proven-repo");
        NewGitRepo(repo);
        var rootSha = Commit(repo, "a.txt", "root", "root", "");
        var fixSha = Commit(repo, "a.txt", "fixed", "fix the arm", "");
        var tipSha = Commit(repo, "b.txt", "later", "later tip", "");
        const string olderObserved = "2026-11-01T00:00:00.0000000+00:00", newerObserved = "2026-11-02T00:00:00.0000000+00:00";
        var older = w.PathOf("proven-older.json");
        var newer = w.PathOf("proven-newer.json");
        WriteMergeSeed(older, "run-older", "rc-older", rootSha, olderObserved, "2026-11-01T01:00:00.0000000+00:00",
            Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], true, true, olderObserved), MergeChunk("antiphon", "g2", ["Fixture.Beta"], true, true, olderObserved)),
            Objs(MergeFail("antiphon", "g1", "Fixture.Alpha", "M1", "Failed"), MergeFail("antiphon", "g2", "Fixture.Beta", "Mpending", "Error")), []);
        WriteMergeSeed(newer, "run-newer", "rc-newer", tipSha, newerObserved, "2026-11-02T01:00:00.0000000+00:00",
            Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], true, true, newerObserved), MergeChunk("antiphon", "g2", ["Fixture.Beta"], false, true, newerObserved)), [], []);
        var decision = w.PathOf("proven-decisions.txt");
        File.WriteAllText(decision, "# comment\n\nantiphon|Fixture.Alpha|M1\t" + fixSha + "\nantiphon|Fixture.Beta|Mpending\t" + fixSha + "\n", new UTF8Encoding(false));
        var outPath = w.PathOf("proven-merged.json");
        var reportPath = w.PathOf("proven-report.json");
        var merged = await Merge([older, newer], outPath, reportPath, decision: decision, repo: repo);
        PlainObject? report = null;
        var methods = "";
        if (merged.Code == 0)
        {
            var doc = StoppedSeedFiles.Read(outPath, "");
            report = MergeReport(reportPath);
            methods = J(Arr(doc, "failedTests").Select(f => S(f, "method")));
        }
        var m1 = MergeIdentity(report, "M1");
        var pending = MergeIdentity(report, "Mpending");
        var ok = merged.Code == 0 && methods == "Mpending" && S(m1, "status") == "proven" && Is(m1, "placed", false) && S(m1, "provingRun") == "run-newer"
            && S(m1, "provingSha") == tipSha && S(m1, "containingCommit") == fixSha && S(pending, "status") == "fixed-pending-proof" && Is(pending, "placed", true)
            && S(pending, "containingCommit") == fixSha && P(pending, "provingRun") == null;
        return (ok, "code=" + merged.Code + "; methods=" + methods + "; m1=" + S(m1, "status") + "/" + S(m1, "containingCommit") + "; pending=" + S(pending, "status")
            + "; tip=" + tipSha + "; fix=" + fixSha + "; text=" + merged.Text);
    });

    [Test, Timeout(300_000)]
    public Task CumulativeMergeUnfixedGreen(CancellationToken token) => Case(nameof(CumulativeMergeUnfixedGreen), async w =>
    {
        var repo = w.PathOf("unfixed-repo");
        NewGitRepo(repo);
        var rootSha = Commit(repo, "a.txt", "root", "root", "");
        var sourceSha = Commit(repo, "b.txt", "unrelated", "unrelated tip", "");
        Git(repo, "checkout", "-b", "fixline", rootSha);
        var fixSha = Commit(repo, "c.txt", "fix", "the real fix", "");
        const string olderObserved = "2026-11-01T00:00:00.0000000+00:00", newerObserved = "2026-11-02T00:00:00.0000000+00:00";
        var older = w.PathOf("unfixed-older.json");
        var newer = w.PathOf("unfixed-newer.json");
        WriteMergeSeed(older, "run-older", "rc-older", rootSha, olderObserved, "2026-11-01T01:00:00.0000000+00:00",
            Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], true, true, olderObserved)), Objs(MergeFail("antiphon", "g1", "Fixture.Alpha", "M1", "Failed")), []);
        WriteMergeSeed(newer, "run-newer", "rc-newer", sourceSha, newerObserved, "2026-11-02T01:00:00.0000000+00:00",
            Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], true, true, newerObserved)), [], []);
        var decision = w.PathOf("unfixed-decisions.txt");
        File.WriteAllText(decision, "antiphon|Fixture.Alpha|M1\t" + fixSha + "\n", new UTF8Encoding(false));
        var outPath = w.PathOf("unfixed-merged.json");
        var reportPath = w.PathOf("unfixed-report.json");
        var merged = await Merge([older, newer], outPath, reportPath, decision: decision, repo: repo);
        string methods = "", status = "", containing = "";
        if (merged.Code == 0)
        {
            var doc = StoppedSeedFiles.Read(outPath, "");
            var row = MergeIdentity(MergeReport(reportPath), "M1");
            methods = J(Arr(doc, "failedTests").Select(f => S(f, "method")));
            status = S(row, "status");
            containing = S(row, "containingCommit");
        }
        return (merged.Code == 0 && methods == "M1" && status == "open" && string.IsNullOrWhiteSpace(containing),
            "code=" + merged.Code + "; methods=" + methods + "; status=" + status + "; containing=" + containing + "; text=" + merged.Text);
    });

    [Test, Timeout(300_000)]
    public Task CumulativeMergeCherryPick(CancellationToken token) => Case(nameof(CumulativeMergeCherryPick), async w =>
    {
        var repo = w.PathOf("cherry-repo");
        NewGitRepo(repo);
        var rootSha = Commit(repo, "a.txt", "root", "root", "");
        var fixSha = Commit(repo, "a.txt", "fixed", "fix the arm", "");
        Git(repo, "checkout", "-b", "side", rootSha);
        var proseSha = Commit(repo, "b.txt", "note", "notes mention " + fixSha + " in the discussion", "");
        var pickSha = Commit(repo, "c.txt", "port", "port the fix", "(cherry picked from commit " + fixSha + ")");
        const string olderObserved = "2026-11-01T00:00:00.0000000+00:00", newerObserved = "2026-11-02T00:00:00.0000000+00:00";
        string[] WritePair(string name, string newerSha)
        {
            var olderPath = w.PathOf(name + "-older.json");
            var newerPath = w.PathOf(name + "-newer.json");
            WriteMergeSeed(olderPath, "run-older", "rc-older", rootSha, olderObserved, "2026-11-01T01:00:00.0000000+00:00",
                Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], true, true, olderObserved)), Objs(MergeFail("antiphon", "g1", "Fixture.Alpha", "M1", "Failed")), []);
            WriteMergeSeed(newerPath, "run-newer", "rc-newer", newerSha, newerObserved, "2026-11-02T01:00:00.0000000+00:00",
                Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], true, true, newerObserved)), [], []);
            return [olderPath, newerPath];
        }
        var decision = w.PathOf("cherry-decisions.txt");
        File.WriteAllText(decision, "antiphon|Fixture.Alpha|M1\t" + fixSha + "\n", new UTF8Encoding(false));
        var pickOut = w.PathOf("cherry-pick-merged.json");
        var pickReport = w.PathOf("cherry-pick-report.json");
        var picked = await Merge(WritePair("cherry-pick", pickSha), pickOut, pickReport, decision: decision, repo: repo);
        string pickStatus = "", pickCommit = "", pickMethods = "";
        if (picked.Code == 0)
        {
            pickMethods = J(Arr(StoppedSeedFiles.Read(pickOut, ""), "failedTests").Select(f => S(f, "method")));
            var row = MergeIdentity(MergeReport(pickReport), "M1");
            pickStatus = S(row, "status");
            pickCommit = S(row, "containingCommit");
        }
        var proseOut = w.PathOf("cherry-prose-merged.json");
        var proseReport = w.PathOf("cherry-prose-report.json");
        var prose = await Merge(WritePair("cherry-prose", proseSha), proseOut, proseReport, decision: decision, repo: repo);
        string proseStatus = "", proseMethods = "", proseCommit = "";
        if (prose.Code == 0)
        {
            proseMethods = J(Arr(StoppedSeedFiles.Read(proseOut, ""), "failedTests").Select(f => S(f, "method")));
            var row = MergeIdentity(MergeReport(proseReport), "M1");
            proseStatus = S(row, "status");
            proseCommit = S(row, "containingCommit");
        }
        var ok = picked.Code == 0 && pickMethods == "" && pickStatus == "proven" && pickCommit == pickSha && prose.Code == 0 && proseMethods == "M1" && proseStatus == "open"
            && string.IsNullOrWhiteSpace(proseCommit);
        return (ok, "pick=" + picked.Code + "/" + pickStatus + "/" + pickCommit + "; prose=" + prose.Code + "/" + proseStatus + "/" + proseCommit + "; pickSha=" + pickSha
            + "; text=" + picked.Text + prose.Text);
    });

    [Test, Timeout(300_000)]
    public Task CumulativeMergeExplicitProven(CancellationToken token) => Case(nameof(CumulativeMergeExplicitProven), async w =>
    {
        const string olderObserved = "2026-11-01T00:00:00.0000000+00:00", newerObserved = "2026-11-02T00:00:00.0000000+00:00";
        var older = w.PathOf("explicit-older.json");
        var newer = w.PathOf("explicit-newer.json");
        WriteMergeSeed(older, "run-older", "rc-older", A40, olderObserved, "2026-11-01T01:00:00.0000000+00:00",
            Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], true, true, olderObserved)), Objs(MergeFail("antiphon", "g1", "Fixture.Alpha", "M1", "Failed")), []);
        WriteMergeSeed(newer, "run-newer", "rc-newer", B40, newerObserved, "2026-11-02T01:00:00.0000000+00:00",
            Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], true, true, newerObserved)), [], []);
        var outPath = w.PathOf("explicit-merged.json");
        var reportPath = w.PathOf("explicit-report.json");
        var merged = await Merge([older, newer], outPath, reportPath, proven: ["antiphon|Fixture.Alpha|M1:run-newer"]);
        string methods = "", status = "", provingRun = "", provingSha = "";
        if (merged.Code == 0)
        {
            methods = J(Arr(StoppedSeedFiles.Read(outPath, ""), "failedTests").Select(f => S(f, "method")));
            var row = MergeIdentity(MergeReport(reportPath), "M1");
            status = S(row, "status");
            provingRun = S(row, "provingRun");
            provingSha = S(row, "provingSha");
        }
        var unknownOut = w.PathOf("explicit-unknown.json");
        var unknown = await Merge([older, newer], unknownOut, w.PathOf("explicit-unknown-report.json"), proven: ["antiphon|Fixture.Alpha|M1:missing-run"]);
        var notLaterOut = w.PathOf("explicit-not-later.json");
        var notLater = await Merge([older, newer], notLaterOut, w.PathOf("explicit-not-later-report.json"), proven: ["antiphon|Fixture.Alpha|M1:run-older"]);
        var redder = w.PathOf("explicit-red-newer.json");
        WriteMergeSeed(redder, "run-red", "rc-red", C40, "2026-11-03T00:00:00.0000000+00:00", "2026-11-03T01:00:00.0000000+00:00",
            Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], false, true, newerObserved)), [], []);
        var redOut = w.PathOf("explicit-red.json");
        var notGreen = await Merge([older, redder], redOut, w.PathOf("explicit-red-report.json"), proven: ["antiphon|Fixture.Alpha|M1:run-red"]);
        var refusedOk = unknown.Code == 2 && Has(unknown.Text, "proven-run-unknown") && !File.Exists(unknownOut) && notLater.Code == 2 && !File.Exists(notLaterOut)
            && notGreen.Code == 2 && Has(notGreen.Text, "proven-not-green") && !File.Exists(redOut);
        return (merged.Code == 0 && methods == "" && status == "proven" && provingRun == "run-newer" && provingSha == B40 && refusedOk,
            "code=" + merged.Code + "; status=" + status + "; unknown=" + unknown.Code + "; notLater=" + notLater.Code + "; notGreen=" + notGreen.Code + "; text=" + merged.Text
            + unknown.Text + notGreen.Text);
    });

    [Test, Timeout(300_000)]
    public Task CumulativeMergeDigestStable(CancellationToken token) => Case(nameof(CumulativeMergeDigestStable), async w =>
    {
        const string olderObserved = "2026-11-01T00:00:00.0000000+00:00", newerObserved = "2026-11-02T00:00:00.0000000+00:00";
        const string newerCreated = "2026-11-02T01:00:00.0000000+00:00";
        var older = w.PathOf("digest-older.json");
        var newer = w.PathOf("digest-newer.json");
        WriteMergeSeed(older, "run-older", "rc-older", A40, olderObserved, "2026-11-01T01:00:00.0000000+00:00",
            Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], true, true, olderObserved)), Objs(MergeFail("antiphon", "g1", "Fixture.Alpha", "M1", "Failed")), []);
        WriteMergeSeed(newer, "run-newer", "rc-newer", B40, newerObserved, newerCreated,
            Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], true, true, newerObserved)), Objs(MergeFail("antiphon", "g1", "Fixture.Alpha", "M2", "Timeout")), []);
        string out1 = w.PathOf("digest-1.json"), report1 = w.PathOf("digest-report-1.json"), out2 = w.PathOf("digest-2.json"), report2 = w.PathOf("digest-report-2.json");
        var first = await Merge([older, newer], out1, report1);
        var second = await Merge([older, newer], out2, report2);
        object? doc = null;
        string hash1 = "", hash2 = "", created = "";
        var same = false;
        if (first.Code == 0 && second.Code == 0)
        {
            doc = StoppedSeedFiles.Read(out1, "");
            hash1 = S(ReadJson(out1), "contentHash");
            hash2 = S(ReadJson(out2), "contentHash");
            created = StoppedSeed.Utc(P(doc, "createdAtUtc"))?.ToString("o", CultureInfo.InvariantCulture) ?? "";
            same = SameBytes(out1, out2) && SameBytes(report1, report2);
        }
        var expectedCreated = StoppedSeed.Utc(newerCreated)?.ToString("o", CultureInfo.InvariantCulture) ?? "";
        return (first.Code == 0 && second.Code == 0 && same && hash1 == hash2 && Regex.IsMatch(hash1, "^[a-f0-9]{64}$") && created == expectedCreated && doc != null,
            "first=" + first.Code + "; second=" + second.Code + "; same=" + B(same) + "; hash=" + hash1 + "; created=" + created + "; expected=" + expectedCreated
            + "; text=" + first.Text + second.Text);
    });

    [Test, Timeout(300_000)]
    public Task CumulativeMergeLaunchDefaults(CancellationToken token) => Case(nameof(CumulativeMergeLaunchDefaults), w =>
    {
        // The merge command's synopsis (scripts/merge-nightly-seeds.ps1 until W2-nightly S26).
        var raw = File.ReadAllBytes(SeedCommandSource);
        var ascii = raw.All(b => b <= 127);
        var text = Encoding.ASCII.GetString(raw);
        const string sentence = "A later green proves an identity only when that seed's source SHA contains the fix commit, or --Proven names that run.";
        var nightly = File.ReadAllText(Path.Combine(Repo, "scripts", "nightly-run.ps1"));
        var declares = nightly.Contains("[string]$PriorFailuresPath", StringComparison.Ordinal) && nightly.Contains("[string]$PriorFailuresDigest", StringComparison.Ordinal)
            && nightly.Contains("[switch]$AcceptCrossCandidateSeed", StringComparison.Ordinal) && nightly.Contains("[string]$SeedSourceCandidateId", StringComparison.Ordinal);
        var noCall = !nightly.Contains("merge-nightly-seeds", StringComparison.Ordinal);
        var hasSentence = text.Contains(sentence, StringComparison.Ordinal);
        return (ascii && hasSentence && declares && noCall, "ascii=" + B(ascii) + "; sentence=" + B(hasSentence) + "; declares=" + B(declares) + "; noCall=" + B(noCall));
    });

    [Test, Timeout(300_000)]
    public Task CumulativeMergeFixPresentAtFailure(CancellationToken token) => Case(nameof(CumulativeMergeFixPresentAtFailure), async w =>
    {
        var repo = w.PathOf("present-repo");
        NewGitRepo(repo);
        var rootSha = Commit(repo, "a.txt", "root", "root", "");
        var fixSha = Commit(repo, "a.txt", "fixed", "fix the arm", "");
        var failSha = Commit(repo, "b.txt", "later", "later tip", "");
        var newerFixSha = Commit(repo, "d.txt", "second fix", "fix the arm again", "");
        Git(repo, "checkout", "-b", "side", rootSha);
        var pickSha = Commit(repo, "c.txt", "port", "port the fix", "(cherry picked from commit " + fixSha + ")");
        var decision = w.PathOf("present-decisions.txt");
        File.WriteAllText(decision, "antiphon|Fixture.Alpha|M1\t" + fixSha + "\n", new UTF8Encoding(false));
        var mixedDecision = w.PathOf("present-mixed.txt");
        File.WriteAllText(mixedDecision, "antiphon|Fixture.Alpha|M1\t" + fixSha + "," + newerFixSha + "\n", new UTF8Encoding(false));
        string[] WritePair(string name, string failingSha, string greenSha, bool greenCompleted)
        {
            const string olderObserved = "2026-11-01T00:00:00.0000000+00:00", newerObserved = "2026-11-02T00:00:00.0000000+00:00";
            var olderPath = w.PathOf(name + "-older.json");
            var newerPath = w.PathOf(name + "-newer.json");
            WriteMergeSeed(olderPath, "run-fail", "rc-fail", failingSha, olderObserved, "2026-11-01T01:00:00.0000000+00:00",
                Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], true, true, olderObserved)), Objs(MergeFail("antiphon", "g1", "Fixture.Alpha", "M1", "Failed")), []);
            WriteMergeSeed(newerPath, "run-green", "rc-green", greenSha, newerObserved, "2026-11-02T01:00:00.0000000+00:00",
                Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], greenCompleted, true, newerObserved)), [], []);
            return [olderPath, newerPath];
        }
        async Task<(int Code, string Status, string Methods, bool Flag, string Proving, string Containing, string Text)> ReadPresent(string name, string[] seeds,
            string decisionPath, string[] proven)
        {
            var outPath = w.PathOf(name + "-merged.json");
            var reportPath = w.PathOf(name + "-report.json");
            var merged = await Merge(seeds, outPath, reportPath, decision: decisionPath, proven: proven, repo: repo);
            string status = "", methods = "", proving = "", containing = "";
            var flag = false;
            if (merged.Code == 0)
            {
                methods = J(Arr(StoppedSeedFiles.Read(outPath, ""), "failedTests").Select(f => S(f, "method")));
                var row = MergeIdentity(MergeReport(reportPath), "M1");
                status = S(row, "status");
                flag = Is(row, "fixPresentAtFailure", true);
                proving = S(row, "provingRun");
                containing = S(row, "containingCommit");
            }
            return (merged.Code, status, methods, flag, proving, containing, merged.Text);
        }
        var same = await ReadPresent("same", WritePair("same", failSha, failSha, true), decision, []);
        var olderRun = await ReadPresent("older", WritePair("older", failSha, fixSha, true), decision, []);
        var held = await ReadPresent("held", WritePair("held", failSha, failSha, false), decision, []);
        var fresh = await ReadPresent("fresh", WritePair("fresh", rootSha, failSha, true), decision, []);
        var overrideRun = await ReadPresent("override", WritePair("override", failSha, failSha, true), decision, ["antiphon|Fixture.Alpha|M1:run-green"]);
        var picked = await ReadPresent("picked", WritePair("picked", pickSha, pickSha, true), decision, []);
        var mixed = await ReadPresent("mixed", WritePair("mixed", failSha, newerFixSha, true), mixedDecision, []);
        var openOk = same.Code == 0 && same.Status == "open" && same.Methods == "M1" && same.Flag && string.IsNullOrWhiteSpace(same.Containing);
        var olderOk = olderRun.Code == 0 && olderRun.Status == "open" && olderRun.Methods == "M1" && olderRun.Flag && string.IsNullOrWhiteSpace(olderRun.Containing);
        var heldOk = held.Code == 0 && held.Status == "open" && held.Methods == "M1" && held.Flag && string.IsNullOrWhiteSpace(held.Containing);
        var freshOk = fresh.Code == 0 && fresh.Status == "proven" && fresh.Methods == "" && !fresh.Flag && fresh.Proving == "run-green" && fresh.Containing == fixSha;
        var overrideOk = overrideRun.Code == 0 && overrideRun.Status == "proven" && overrideRun.Methods == "" && overrideRun.Flag && overrideRun.Proving == "run-green";
        var pickedOk = picked.Code == 0 && picked.Status == "open" && picked.Methods == "M1" && picked.Flag && string.IsNullOrWhiteSpace(picked.Containing);
        var mixedOk = mixed.Code == 0 && mixed.Status == "proven" && mixed.Methods == "" && mixed.Flag && mixed.Proving == "run-green" && mixed.Containing == newerFixSha;
        var ok = openOk && olderOk && heldOk && freshOk && overrideOk && pickedOk && mixedOk;
        return (ok, "same=" + same.Status + "/" + same.Methods + "/" + B(same.Flag) + "; older=" + olderRun.Status + "; held=" + held.Status + "; fresh=" + fresh.Status + "/" + fresh.Containing
            + "; override=" + overrideRun.Status + "/" + B(overrideRun.Flag) + "; picked=" + picked.Status + "; mixed=" + mixed.Status + "/" + mixed.Containing + "; fix=" + fixSha
            + "; newerFix=" + newerFixSha + "; text=" + same.Text + olderRun.Text + held.Text + fresh.Text + overrideRun.Text + picked.Text + mixed.Text);
    });

    [Test, Timeout(300_000)]
    public Task CumulativeMergePrintsDigest(CancellationToken token) => Case(nameof(CumulativeMergePrintsDigest), async w =>
    {
        const string olderObserved = "2026-11-01T00:00:00.0000000+00:00", newerObserved = "2026-11-02T00:00:00.0000000+00:00";
        var older = w.PathOf("print-older.json");
        var newer = w.PathOf("print-newer.json");
        WriteMergeSeed(older, "run-older", "rc-older", A40, olderObserved, "2026-11-01T01:00:00.0000000+00:00",
            Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], true, true, olderObserved)), Objs(MergeFail("antiphon", "g1", "Fixture.Alpha", "M1", "Failed")), []);
        WriteMergeSeed(newer, "run-newer", "rc-newer", B40, newerObserved, "2026-11-02T01:00:00.0000000+00:00",
            Objs(MergeChunk("antiphon", "g1", ["Fixture.Alpha"], true, true, newerObserved)), Objs(MergeFail("antiphon", "g1", "Fixture.Alpha", "M1", "Failed")), []);
        var outPath = w.PathOf("print-merged.json");
        var merged = await Merge([older, newer], outPath, w.PathOf("print-report.json"), cli: true);
        var fileDigest = File.Exists(outPath) ? PriorFailureHistory.Digest(outPath) : "";
        const string note = "A test skipped inside a completed chunk looks green; that is an accepted seed-schema limit.";
        var scriptText = File.ReadAllText(SeedCommandSource);
        var printed = !string.IsNullOrWhiteSpace(fileDigest) && merged.Text.Contains("digest=" + fileDigest, StringComparison.Ordinal);
        var noted = scriptText.Contains(note, StringComparison.Ordinal) && merged.Text.Contains(note, StringComparison.Ordinal);
        return (merged.Code == 0 && Regex.IsMatch(fileDigest, "^[a-f0-9]{64}$") && printed && noted,
            "code=" + merged.Code + "; digest=" + fileDigest + "; printed=" + B(printed) + "; noted=" + B(noted) + "; text=" + merged.Text);
    });
}
