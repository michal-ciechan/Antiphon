using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class C1364BoundLandWaitProofTests
{
    private const string ProjectId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
    private const string LandId = "22222222-2222-2222-2222-222222222222";
    private const string Operation = "c100800000000000000000000000000000001";
    private static string LandPath => RollingVolumeRecycleScriptTests.C1087ListPath(ProjectId, "land");

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1364_Wrapper_redeploy_reports_waited()
    {
        foreach (var mode in new[] { "ordinary", "resume", "preview" })
        foreach (var clears in new[] { false, true })
            await WrapperVector("V-1/" + mode, "redeploy-old", mode, clears, earlierCensuses: 0,
                nextCase: "deploy-parent");
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1364_Wrapper_cleanup_reports_waited()
    {
        foreach (var entry in new[] { "drain-temp", "retire-temp", "preview" })
        foreach (var clears in new[] { false, true })
            await WrapperVector("V-2/" + entry, entry == "drain-temp" ? entry : "retire-temp",
                entry == "preview" ? "preview" : "ordinary", clears, earlierCensuses: 0,
                nextCase: "retire-temp-containers");
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1364_Wrapper_retire_volumes_reports_waited()
    {
        foreach (var mode in new[] { "ordinary", "resume", "preview" })
        foreach (var clears in new[] { false, true })
            await WrapperVector("V-3/" + mode, "retire-temp", mode, clears,
                earlierCensuses: mode == "resume" ? 0 : 1, nextCase: "retire-temp-runner");
    }

    private static JsonObject Land(string runner) => new()
    {
        ["id"] = LandId, ["status"] = "Succeeded", ["runnerId"] = runner,
        ["projectId"] = ProjectId, ["scopeSource"] = "Task",
        ["landRequestedAt"] = "2020-01-01T00:00:00Z", ["landStartedAt"] = null
    };

    private static async Task WrapperVector(string label, string phase, string mode, bool clears,
        int earlierCensuses, string nextCase)
    {
        using var fixture = new C1008WrapperFixture();
        var runner = phase == "redeploy-old" ? "server2" : "server2-temp";
        if (runner == "server2")
        {
            fixture.MainDrained();
            fixture.State["allowClear"] = true;
        }
        var empty = fixture.State["tasks"]!;
        var pending = empty.DeepClone();
        pending["items"]!.AsArray().Add(Land(runner));
        var sequence = new JsonArray();
        for (var i = 0; i < earlierCensuses * 2; i++) sequence.Add(empty.DeepClone());
        sequence.Add(pending);
        if (clears) sequence.Add(empty.DeepClone());
        fixture.State["taskQueries"] = new JsonObject { [LandPath] = sequence };
        var args = new List<string>();
        if (mode == "resume") args.AddRange(["-ResumeRecycle", Operation]);
        if (mode == "preview") args.Add("-DryRun");
        if (clears) fixture.Environment["C727_TEST_LAND_WAIT_MS"] = "60000";
        else args.AddRange(["-LandWaitMinutes", "0"]);
        var observer = Path.Combine(fixture.Root, "observe-case.ps1");
        var observedReceipt = Path.Combine(fixture.Root, "selected-census.json");
        File.WriteAllText(observer, $$"""
            param([string]$Case, [string]$Manifest)
            $ErrorActionPreference = 'Stop'
            if ($Case -eq '{{nextCase}}') {
                $request = Get-Content -Raw -LiteralPath $Manifest | ConvertFrom-Json
                Copy-Item -LiteralPath (Join-Path $request.evidenceRoot 'census-{{phase}}-{{runner}}.json') -Destination '{{observedReceipt}}'
            }
            & '{{DelegateScriptRunner.RepoRoot}}/scripts/fixtures/c727-fake-verify.ps1' -Case $Case -Manifest $Manifest
            exit $LASTEXITCODE
            """);
        fixture.Environment["C727_TEST_VERIFY_STUB"] = observer;
        var run = await fixture.Run(phase, args.ToArray());
        label += clears ? "/clears" : "/expires";
        run.Exit.ShouldBe(clears ? 0 : 2, label + ": " + run.Output);
        var cases = run.Trace.Where(t => t["kind"]?.GetValue<string>() == "case")
            .Select(t => t["name"]!.GetValue<string>()).ToArray();
        cases.Contains(nextCase).ShouldBe(clears, label + ": next host effect");
        if (earlierCensuses != 0)
            cases.ShouldContain("retire-temp-containers", label + ": earlier cleanup really completed");
        if (!clears)
            cases.ShouldBe(earlierCensuses == 0 ? [] : new[] { "retire-temp-containers" }, label);
        var reads = run.Trace.Where(t => t["path"]?.GetValue<string>() == LandPath).ToArray();
        // V-2 retire-temp clearing can continue through the later volume census as well.
        var laterCensuses = clears && nextCase == "retire-temp-containers" && phase == "retire-temp" ? 1 : 0;
        reads.Length.ShouldBe(earlierCensuses * 2 + (clears ? 3 : 1) + laterCensuses * 2, label);
        var waitLines = run.Output.Split('\n').Select(x => x.TrimEnd('\r'))
            .Where(x => x.StartsWith("RECYCLE_LAND_WAIT ", StringComparison.Ordinal)).ToArray();
        waitLines.ShouldBe(clears ? new[] { $"RECYCLE_LAND_WAIT runner={runner} id={LandId} polls=0" } : [], label);
        var root = run.Trace.First(t => t["kind"]?.GetValue<string>() == "prerequisite")["evidenceRoot"]!.GetValue<string>();
        var receipt = JsonNode.Parse(File.ReadAllText(clears ? observedReceipt :
            Path.Combine(root, $"census-{phase}-{runner}.json")))!;
        receipt["landWait"]!["polls"]!.GetValue<int>().ShouldBe(clears ? 1 : 0, label);
        receipt["landWait"]!["outcome"]!.GetValue<string>().ShouldBe(clears ? "cleared" : "expired", label);
        if (!clears)
        {
            var diagnostic = run.Output.Split('\n').Select(x => x.TrimEnd('\r'))
                .Single(x => x.StartsWith("Rolling deploy stopped: ", StringComparison.Ordinal))[24..];
            AssertWaitDiagnostic(diagnostic, runner, 0, label);
        }
        Console.WriteLine("C1364_VECTOR " + label + " passed");
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1364_Host_recycle_admission_reports_waited()
    {
        C1008HostFixture.RequireNativeLinux();
        foreach (var main in new[] { true, false })
        {
            foreach (var preview in new[] { false, true })
                await RecycleVector($"V-4/{(main ? "main" : "temp")}/{(preview ? "preview" : "apply")}",
                    main, proof: 1, preview: preview);
            await RecycleVector($"V-5/{(main ? "main" : "temp")}/apply", main, proof: 2);
        }
        foreach (var (main, phase) in new[] { (true, "recreating"), (true, "verified"), (false, "removing") })
        foreach (var proof in new[] { 1, 2 })
            await RecycleVector($"V-{(proof == 1 ? 4 : 5)}/{(main ? "main" : "temp")}/resume-{phase}",
                main, proof, resumePhase: phase);
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1364_Host_recycle_post_removal_reports_waited()
    {
        C1008HostFixture.RequireNativeLinux();
        // R-1 must be first: the existing late-land input reaches the real post-audit proof.
        await RecycleVector("V-6/main/late-land", main: true, proof: 3, lateLand: true);
        await RecycleVector("V-6/temp", main: false, proof: 3);
        await RecycleVector("V-6/main/resume", main: true, proof: 3, resumePhase: "containersRemoved");
        await RecycleVector("V-6/temp/resume", main: false, proof: 3, resumePhase: "removing");
        foreach (var main in new[] { true, false })
        for (var index = 0; index < (main ? 3 : 4); index++)
            await RecycleVector($"V-7/{(main ? "main" : "temp")}/volume-{index + 1}",
                main, proof: 4 + index);
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1364_Host_cleanup_reports_waited()
    {
        C1008HostFixture.RequireNativeLinux();
        foreach (var (label, candidates, proof, preview, lateLand) in new[]
        {
            ("V-8/apply", 2, 1, false, false),
            ("V-8/preview", 2, 1, true, false),
            ("V-9/before-first", 2, 2, false, false),
            ("V-9/after-one", 2, 3, false, true),
            ("V-10/after-only", 1, 3, false, true),
            ("V-10/initially-absent", 0, 2, false, false)
        })
        {
            using var fixture = new C1008HostFixture(main: false);
            for (var i = 0; i < candidates; i++)
                fixture.Docker["containers"]!.AsArray().Add(fixture.Container((char)('7' + i),
                    "antiphon-runner-temp", i == 0 ? "session-runner" : "state-init", false, previousGeneration: true));
            if (lateLand) fixture.Docker["fault"] = "c994-late-land";
            var before = fixture.Docker["volumes"]!.ToJsonString();
            var run = await fixture.Run("retire-temp-containers",
                ObserveHost("server2-temp", proof, inject: !lateLand), preview);
            AssertHostWait(fixture, run, "retire-temp-containers", "server2-temp", proof, label);
            var removed = lateLand ? 1 : 0;
            fixture.Trace.Where(a => a[0] == "rm").Select(a => a.Last())
                .ShouldBe(removed == 0 ? [] : new[] { new string('7', 64) }, label);
            var receipt = ReadJson(fixture, "evidence/retire-temp-containers/temp-containers.json");
            receipt["removals"]!.AsArray().Count.ShouldBe(removed, label);
            receipt["outcome"]!.GetValue<string>().ShouldBe(removed == 0 ? "refused" : "partial", label);
            receipt["finalCensus"].ShouldBeNull(label + ": final success cannot follow a refused proof");
            fixture.ReloadDocker();
            fixture.Docker["volumes"]!.ToJsonString().ShouldBe(before, label + ": volumes preserved");
            Console.WriteLine("C1364_VECTOR " + label + " passed");
        }
    }

    private static async Task RecycleVector(string label, bool main, int proof, bool preview = false,
        string? resumePhase = null, bool lateLand = false)
    {
        using var fixture = new C1008HostFixture(main);
        var hostCase = main ? "deploy-parent" : "retire-temp-runner";
        var runner = main ? "server2" : "server2-temp";
        if (!main && resumePhase is not null)
        {
            // This fixture starts with no temp containers, so its saved generation is null.
            // A valid resume then requires stack.env at the requested SHA (there is no saved
            // previous SHA to authorize the fixture's default older stack).
            var targetSha = new string('a', 40);
            fixture.Statuses["server2-temp"]!["buildVersion"] = targetSha;
            File.WriteAllText(Path.Combine(fixture.Root, "temp.env"),
                "RUNNER_GROK_STORE_DIR=" + fixture.Root + "/grok\nSOURCE_REVISION=" + targetSha + "\n");
        }
        if (resumePhase is not null) await SeedResume(fixture, hostCase, resumePhase);
        if (lateLand) fixture.Docker["fault"] = "late-land";
        // Temp apply reaches compose down only after a Ready preview. The observed run still owns the proof counter.
        if (!main && !preview && resumePhase is null && proof >= 4)
            await VolumeRetirementFixture.ReadyPreview(fixture);
        var removedBefore = fixture.Docker["removed"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        var mutated = lateLand && Environment.GetEnvironmentVariable("C1364_WAIT_MUTANT") == "post-removal-zero";
        var run = await fixture.Run(hostCase,
            (resumePhase is null ? "" : "C1008_RESUME=1\n") + ObserveHost(runner, proof, inject: !lateLand),
            preview, sourceTransform: mutated ? CorruptPostRemovalCount : null);
        // Boundary and receipt assertions precede the numeric diagnostic assertion, so CP-1
        // cannot masquerade as a useful red when the fixture or earlier proof refuses.
        var boundary = HostBoundaries(fixture).Last();
        boundary["record"]!["phase"]!.GetValue<string>().ShouldBe(proof >= 4 ? "removing" :
            proof == 3 ? "containersRemoved" : resumePhase ?? "preflight", label + ": " + run.Output);
        var selected = boundary["record"]!;
        if (proof >= 3)
        {
            selected["ownedRemoved"]!.GetValue<bool>().ShouldBeTrue(label);
            var auditRuns = fixture.Trace.Count(a => a[0] == "start");
            auditRuns.ShouldBe(2, label + ": both audits ran before this refusal");
            fixture.Trace.Count(a => a[0] == "start" && a.Contains("-a")).ShouldBe(2, label + ": attached audits completed");
            fixture.Trace.Count(a => a[0] == "rm" && a.Last() == new string('6', 64))
                .ShouldBe(2, label + ": both audit helpers removed after completion");
            var saved = ReadJson(fixture, "server/recycle/" + Operation + ".json");
            saved["phase"]!.GetValue<string>().ShouldBe(proof == 3 ? "containersRemoved" : "removing", label);
            saved["volumes"]!.AsObject().Count(v => v.Value!["outcome"]!.GetValue<string>() == "removed")
                .ShouldBe(Math.Max(0, proof - 4), label + ": exact prior per-volume journal progress");
            var targets = new[] { "work", "runner-tmp", "dind-data", "runner-state" }
                .Take(main ? 3 : 4).Select(x => (main ? "antiphon-runner_" : "antiphon-runner-temp_") + x).ToArray();
            // Temp down -v precedes V-7. It has already removed all four volumes; no further
            // Docker mutation is permitted, while only preceding iterations may be journalled.
            fixture.Removed.ShouldBe(main ? targets.Take(Math.Max(0, proof - 4)).ToArray() :
                proof >= 4 ? targets : [], label);
            fixture.Trace.Count(a => a.Contains("down")).ShouldBe(!main && proof >= 4 ? 1 : 0, label);
            if (main)
                fixture.Trace.Where(a => a[0] == "rm" && a.Last() != new string('6', 64)).Select(a => a.Last())
                    .ShouldBe(resumePhase is null ? new[] { new string('1', 64), new string('2', 64) } : [], label);
        }
        else
        {
            fixture.Removed.ShouldBe(removedBefore, label + ": admission cannot remove more volumes");
            fixture.Trace.Any(a => a[0] is "stop" or "rm" or "create" || a.Contains("down"))
                .ShouldBeFalse(label + ": no destructive continuation at admission");
        }
        if (mutated)
            File.ReadAllText(Path.Combine(fixture.Root, "mutant-hit")).Trim()
                .ShouldBe("containersRemoved waited=1polls->waited=0polls", label + ": caller-local mutant hit");
        AssertHostWait(fixture, run, hostCase, runner, proof, label);
        Console.WriteLine("C1364_VECTOR " + label + " passed");
    }

    private static string ObserveHost(string runner, int targetProof, bool inject) => $$"""
        : > "$C1008_FIXTURE_ROOT/proofs.jsonl"
        : > "$C1008_FIXTURE_ROOT/land-reads.jsonl"
        : > "$C1008_FIXTURE_ROOT/sleeps"
        c849_status_body() {
            if [ "$1" = '{{runner}}' ] && { [ "${FUNCNAME[1]}" = c1008_status_proof ] || [ "${FUNCNAME[1]}" = c994_status_proof ]; }; then
                local n record
                n=$(( $(wc -l < "$C1008_FIXTURE_ROOT/proofs.jsonl") + 1 ))
                record="${C994_RECORD:-null}"
                if [ -n "${C1008_RECORD:-}" ]; then record="$C1008_RECORD"; fi
                jq -cn --argjson n "$n" --argjson record "$record" '{n:$n,record:$record}' >> "$C1008_FIXTURE_ROOT/proofs.jsonl"
                if [ "$n" = '{{targetProof}}' ] && [ '{{(inject ? "1" : "0")}}' = 1 ]; then
                    jq --argjson land '{{Land(runner).ToJsonString()}}' '.scopes["{{ProjectId}}"].items += [$land]' "$C1008_FIXTURE_ROOT/tasks.json" > "$C1008_FIXTURE_ROOT/tasks.injected"
                    mv "$C1008_FIXTURE_ROOT/tasks.injected" "$C1008_FIXTURE_ROOT/tasks.json"
                fi
            fi
            jq -ec --arg runner "$1" '.[$runner]' "$C1008_FIXTURE_ROOT/statuses.json"
        }
        original_http=$(declare -f c1008_http); eval "${original_http/c1008_http/c1364_original_http}"
        c1008_http() {
            c1364_original_http "$@" || return $?
            if [ "$1" = '{{LandPath}}' ]; then
                printf '%s\n' "$C1008_HTTP_BODY" >> "$C1008_FIXTURE_ROOT/land-reads.jsonl"
            fi
        }
        sleep() { printf '%s\n' "$*" >> "$C1008_FIXTURE_ROOT/sleeps"; }
        """;

    private static JsonNode ReadJson(C1008HostFixture fixture, string path) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Root, path)))!;

    private static JsonNode[] HostBoundaries(C1008HostFixture fixture) =>
        File.ReadAllLines(Path.Combine(fixture.Root, "proofs.jsonl")).Select(x => JsonNode.Parse(x)!).ToArray();

    private static void AssertHostWait(C1008HostFixture fixture, (int Exit, string Output) run,
        string hostCase, string runner, int proof, string label)
    {
        run.Exit.ShouldBe(2, label + ": " + run.Output);
        var boundaries = HostBoundaries(fixture);
        boundaries.Select(x => x["n"]!.GetValue<int>()).ShouldBe(Enumerable.Range(1, proof), label + ": " + run.Output);
        var reads = File.ReadAllLines(Path.Combine(fixture.Root, "land-reads.jsonl"))
            .Select(x => JsonNode.Parse(x)!["items"]!.AsArray()).ToArray();
        reads.Length.ShouldBe((proof - 1) * 2 + 2, label + ": clean earlier proofs plus two refusing reads");
        reads.Take(reads.Length - 2).All(x => x.Count == 0).ShouldBeTrue(label + ": no earlier bound land");
        foreach (var read in reads.TakeLast(2))
        {
            read.Count.ShouldBe(1, label);
            read[0]!["id"]!.GetValue<string>().ShouldBe(LandId, label);
            read[0]!["runnerId"]!.GetValue<string>().ShouldBe(runner, label);
        }
        File.ReadAllLines(Path.Combine(fixture.Root, "sleeps")).ShouldBe(new[] { "1" }, label + ": one bounded sleep");
        run.Output.Split('\n').Select(x => x.TrimEnd('\r'))
            .Where(x => x.StartsWith("C1008_LAND_WAIT ", StringComparison.Ordinal))
            .ShouldBe(new[] { $"C1008_LAND_WAIT runner={runner} reason=RecycleLandInFlight {LandId} polls=0" }, label);
        var receipt = ReadJson(fixture, $"evidence/{hostCase}/census-{hostCase}-{runner}.json");
        receipt["runnerId"]!.GetValue<string>().ShouldBe(runner, label);
        receipt["phase"]!.GetValue<string>().ShouldBe(hostCase, label);
        receipt["refusal"]!.GetValue<string>().ShouldBe("RecycleLandInFlight " + LandId, label);
        receipt["landWaitPolls"]!.GetValue<int>().ShouldBe(1, label);
        var result = ReadJson(fixture, $"evidence/{hostCase}/c590-result.json");
        var diagnosis = result["diagnosis"]!.GetValue<string>();
        run.Output.ShouldContain(diagnosis, customMessage: label + ": public host diagnosis");
        AssertWaitDiagnostic(diagnosis, runner, 1, label);
    }

    private static async Task SeedResume(C1008HostFixture fixture, string hostCase, string phase)
    {
        var recreating = phase is "recreating" or "verified";
        var setup = recreating ? """
            build_server2_images() { :; }
            c849_prepare() { :; }
            c849_require_ready() { :; }
            ensure_build_slots_broker() { :; }
            seed_runner_checkout() { :; }
            compose_host() {
                if [ "$1" = up ]; then return 1; fi
                docker compose -p "$HOST_PROJECT" "$@"
            }
            """ : $$"""
            original_save=$(declare -f c1008_save); eval "${original_save/c1008_save/c1364_original_save}"
            c1008_save() {
                c1364_original_save || return $?
                if printf '%s' "$C1008_RECORD" | jq -e '.phase=="{{phase}}"' >/dev/null; then exit 77; fi
            }
            """;
        var seed = await fixture.Run(hostCase, setup);
        seed.Exit.ShouldBe(recreating ? 2 : 77, "valid resume seed: " + seed.Output);
        if (recreating) seed.Output.ShouldContain("HostComposeFailed");
        var journal = ReadJson(fixture, "server/recycle/" + Operation + ".json");
        journal["phase"]!.GetValue<string>().ShouldBe(recreating ? "recreating" : phase, "durable resume boundary");
        if (phase == "verified")
        {
            // Same valid post-recreation journal form used by the C1105 verified-resume vector.
            journal["phase"] = "verified";
            File.WriteAllText(Path.Combine(fixture.Root, "server/recycle/" + Operation + ".json"), journal.ToJsonString());
        }
        fixture.ReloadDocker();
        var statuses = ReadJson(fixture, "statuses.json").AsObject();
        fixture.Statuses.Clear();
        foreach (var (key, value) in statuses) fixture.Statuses[key] = value?.DeepClone();
        File.WriteAllText(Path.Combine(fixture.Root, "docker-trace.jsonl"), "");
        File.WriteAllText(Path.Combine(fixture.Root, "http-trace"), "");
    }

    private static string CorruptPostRemovalCount(string source)
    {
        const string anchor = "    code=0; c1008_tasks_wait || code=$?\n    [ \"$code\" = 0 ] || c1008_refuse \"$C1008_TASK_ERROR\"";
        source.Split(anchor, StringSplitOptions.None).Length.ShouldBe(2, "R-1: exactly one caller anchor");
        return source.Replace(anchor, """
                code=0; c1008_tasks_wait || code=$?
                if [ "$code" != 0 ] && [ "$(printf '%s' "$C1008_RECORD" | jq -r .phase)" = containersRemoved ]; then
                    if [[ "$C1008_TASK_ERROR" == *'waited=1polls' ]]; then
                        C1008_TASK_ERROR="${C1008_TASK_ERROR/waited=1polls/waited=0polls}"
                        printf '%s\n' 'containersRemoved waited=1polls->waited=0polls' > "$C1008_FIXTURE_ROOT/mutant-hit"
                    fi
                fi
                [ "$code" = 0 ] || c1008_refuse "$C1008_TASK_ERROR"
            """, StringComparison.Ordinal);
    }

    private static void AssertWaitDiagnostic(string diagnostic, string runner, int polls, string label)
    {
        var match = Regex.Match(diagnostic,
            @"\ARecycleLandInFlight (?<id>[0-9a-f-]{36}) runner=(?<runner>[a-z0-9-]+) waited=(?<polls>[0-9]+)polls\z");
        match.Success.ShouldBeTrue(label + ": complete, single waited suffix: " + diagnostic);
        match.Groups["id"].Value.ShouldBe(LandId, label);
        match.Groups["runner"].Value.ShouldBe(runner, label);
        int.Parse(match.Groups["polls"].Value).ShouldBe(polls, label + ": diagnostic waited count");
    }
}
