using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;

namespace Antiphon.Tests.Scripts;

// Deterministic HTTP schedules drive the real census functions. No network or Docker calls.
internal static class C1361CensusProbes
{
    private const string Project = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1";
    private const string OtherProject = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string ThirdProject = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    private const string Id = "11111111-1111-1111-1111-111111111111";
    private const string Event = "44444444-4444-4444-4444-444444444444";
    private static string PathFor(string project, string kind = "open") =>
        $"/api/agent-tasks?projectId={project}&unscoped=include&includeChecks=true&" +
        (kind == "open" ? "status=Queued,Dispatched,Working,Blocked" : "landPending=true");
    private static JsonObject Row(string status = "Queued") => new()
    {
        ["id"] = Id, ["status"] = status, ["runnerId"] = "desktop", ["projectId"] = null,
        ["scopeSource"] = "None", ["landRequestedAt"] = null, ["landStartedAt"] = null
    };
    private static JsonObject Proof(string state = "Completed", string? terminal = Event) =>
        new() { ["state"] = state, ["terminalEventId"] = terminal };
    private static JsonObject Detail(JsonObject row, JsonNode? proof = null) =>
        new() { ["summary"] = row.DeepClone(), ["landRequest"] = proof?.DeepClone() };
    private static JsonObject Envelope(string project, params JsonObject[] rows) => new()
    {
        ["items"] = new JsonArray(rows.Select(x => x.DeepClone()).ToArray()),
        ["scope"] = new JsonObject { ["projectId"] = project, ["unscoped"] = "include" },
        ["excluded"] = new JsonObject { ["total"] = 0, ["unscoped"] = 0, ["byProject"] = new JsonArray() }
    };
    private static JsonObject Exclude(JsonObject envelope, params (string Project, int Count)[] entries)
    {
        envelope["excluded"]!["total"] = entries.Sum(x => x.Count);
        envelope["excluded"]!["byProject"] = new JsonArray(entries.Select(x => (JsonNode)new JsonObject
            { ["projectId"] = x.Project, ["count"] = x.Count }).ToArray());
        return envelope;
    }
    private sealed record Vector(string Name, JsonObject Requests, string Error = "", JsonObject? Expected = null,
        JsonNode? Proof = null, int Sleeps = 0, bool Report = false, bool Wait = false);

    private static Vector StatusVector(string name, bool passes, JsonObject before, JsonObject after,
        JsonNode? proof = null, string error = "", JsonObject? detailOverride = null, JsonNode? laterProof = null,
        bool changeProof = false, bool report = false)
    {
        var firstDetail = passes ? Detail(before, proof) : detailOverride ?? Detail(after, proof);
        var secondDetail = passes ? detailOverride ?? Detail(after, changeProof ? laterProof : proof) : firstDetail.DeepClone();
        var current = passes ? after : before;
        var requests = new JsonObject
        {
            [PathFor(Project)] = new JsonArray(Envelope(Project, before), Envelope(Project, current)),
            [PathFor(Project, "land")] = new JsonArray(Envelope(Project)),
            ["/api/agent-tasks/" + Id] = new JsonArray(firstDetail, secondDetail)
        };
        // A changed task ID has its own detail route; it must still fail the pass comparison.
        if (passes && after["id"]!.GetValue<string>() != Id)
            requests["/api/agent-tasks/" + after["id"]!.GetValue<string>()] = new JsonArray(Detail(after, proof));
        return new Vector(name, requests, error, current, changeProof ? laterProof : proof, Report: report);
    }

    internal static async Task Status(bool host, bool passes)
    {
        var unsafeError = passes ? "RecycleTaskCensusUnknown cause=Unstable" : "RecycleLandUnknown";
        // First independent red witness: no helper-extraction or fixture failure can masquerade as red.
        var primary = StatusVector("primary", passes, Row(), Row("Dispatched"));
        await Check(host, primary);
        if (!host) await FullWrapper(passes);
        var vectors = new List<Vector>();
        foreach (var from in new[] { "Queued", "Dispatched", "Working", "Blocked" })
        foreach (var to in new[] { "Queued", "Dispatched", "Working", "Blocked" })
            vectors.Add(StatusVector($"open-pair/{from}/{to}", passes, Row(from), Row(to)));
        foreach (var state in new[] { "Completed", "Superseded", "Canceled" })
            vectors.Add(StatusVector("terminal/" + state, passes, Row(), Row("Dispatched"), Proof(state)));
        vectors.Add(StatusVector("same/Blocked-pending", passes, Row("Blocked"), Row("Blocked"), Proof("Running", null)));
        foreach (var key in new[] { "landRequestedAt", "landStartedAt" })
        {
            var a = Row(); var b = Row("Dispatched");
            a[key] = "2020-01-01T00:00:00Z"; b[key] = "2020-01-01T00:00:00Z";
            vectors.Add(StatusVector("stable-stamp/" + key, passes, a, b));
        }
        foreach (var key in new[] { "id", "runnerId", "projectId", "scopeSource", "landRequestedAt", "landStartedAt" })
        {
            var a = Row(); var b = Row("Dispatched");
            b[key] = key switch { "id" => Event, "runnerId" => "other", "projectId" => Project,
                "scopeSource" => "Card", _ => "2020-01-01T00:00:00Z" };
            if (key == "projectId") b["scopeSource"] = "Task";
            if (key == "scopeSource") { a["scopeSource"] = "Task"; a["projectId"] = Project; b["projectId"] = Project; }
            vectors.Add(StatusVector("field/" + key, passes, a, b, error: unsafeError));
        }
        foreach (var key in new[] { "id", "status", "runnerId", "projectId", "scopeSource", "landRequestedAt", "landStartedAt" })
        {
            var d = Detail(Row("Dispatched")); d["summary"]!.AsObject().Remove(key);
            vectors.Add(StatusVector("missing-summary/" + key, passes, Row(), Row("Dispatched"),
                error: "RecycleLandUnknown", detailOverride: d));
        }
        foreach (var state in new[] { "Queued", "Held", "Running", "NeedsResolution" })
            vectors.Add(StatusVector("pending/" + state, passes, Row(), Row("Dispatched"), Proof(state, null), unsafeError));
        foreach (var variant in new[] { "unknown", "missing", "missing-event", "null-event", "invalid-event" })
        {
            var d = Detail(Row("Dispatched"), Proof());
            if (variant == "unknown") d["landRequest"]!["state"] = "unknown";
            if (variant == "missing") d.Remove("landRequest");
            if (variant == "missing-event") d["landRequest"]!.AsObject().Remove("terminalEventId");
            if (variant == "null-event") d["landRequest"]!["terminalEventId"] = null;
            if (variant == "invalid-event") d["landRequest"]!["terminalEventId"] = "bad";
            vectors.Add(StatusVector("unsafe-proof/" + variant, passes, Row(), Row("Dispatched"),
                error: "RecycleLandUnknown", detailOverride: d));
        }
        var bound = Row(); bound["runnerId"] = "server2";
        var boundAfter = bound.DeepClone().AsObject(); boundAfter["status"] = "Dispatched";
        vectors.Add(StatusVector("bound/ordinary", passes, bound, bound, error: "RecycleBoundTasks"));
        if (!host)
        {
            vectors.Add(StatusVector("bound/report-drift", passes, bound, boundAfter, error: unsafeError, report: true));
            vectors.Add(StatusVector("bound/report-stable", passes, bound, bound, report: true));
        }
        if (passes)
        {
            vectors.Add(StatusVector("proof-change/event", true, Row(), Row("Dispatched"), Proof(), unsafeError,
                laterProof: Proof(terminal: "55555555-5555-5555-5555-555555555555"), changeProof: true));
            vectors.Add(StatusVector("proof-change/null-terminal", true, Row(), Row("Dispatched"), error: unsafeError,
                laterProof: Proof(), changeProof: true));
            vectors.Add(StatusVector("proof-change/terminal-null", true, Row(), Row("Dispatched"), Proof(), unsafeError,
                changeProof: true));
            foreach (var variant in new[] { "settled", "appeared", "runner", "scope", "land", "off-runner-land" })
            {
                var v = StatusVector("companion/" + variant, true, Row(), Row("Dispatched"),
                    error: variant is "settled" or "appeared" or "off-runner-land" ? "" : unsafeError, report: variant == "land");
                var other = Row(); other["id"] = Event;
                var lists = v.Requests[PathFor(Project)]!.AsArray();
                if (variant != "appeared") lists[0]!["items"]!.AsArray().Add(other.DeepClone());
                if (variant != "settled") lists[1]!["items"]!.AsArray().Add(other.DeepClone());
                if (variant == "runner") lists[1]!["items"]![1]!["runnerId"] = "other";
                v.Requests["/api/agent-tasks/" + Event] = new JsonArray(
                    Detail(variant == "settled" ? new JsonObject(other.Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone()))) : other),
                    Detail(variant == "runner" ? lists[1]!["items"]![1]!.AsObject() : other));
                if (variant == "settled") v.Requests["/api/agent-tasks/" + Event]![0]!["summary"]!["status"] = "Succeeded";
                if (variant == "scope")
                {
                    Exclude(lists[1]!.AsObject(), (OtherProject, 1));
                    var scoped = Row(); scoped["id"] = "66666666-6666-6666-6666-666666666666";
                    scoped["projectId"] = OtherProject; scoped["scopeSource"] = "Task";
                    v.Requests[PathFor(OtherProject)] = new JsonArray(Envelope(OtherProject, scoped));
                    v.Requests["/api/agent-tasks/" + scoped["id"]] = new JsonArray(Detail(scoped));
                }
                if (variant is "land" or "off-runner-land")
                {
                    var landRow = Row("Succeeded"); landRow["id"] = Event;
                    landRow["runnerId"] = variant == "land" ? "server2" : "desktop";
                    landRow["landRequestedAt"] = "2020-01-01T00:00:00Z";
                    v.Requests[PathFor(Project, "land")] = new JsonArray(Envelope(Project), Envelope(Project, landRow));
                    if (host && variant == "land") v = v with { Error = "RecycleLandInFlight" };
                }
                vectors.Add(v);
            }
        }
        foreach (var vector in vectors) await Check(host, vector);
        await Boundaries(host, passes);
        if (!passes) await PendingDetailBoundary(host);
        Console.WriteLine($"C1361 {(host ? "host" : "wrapper")}/{(passes ? "passes" : "detail")}: {vectors.Count + 1} census cases plus boundary cases");
    }

    internal static async Task Counts()
    {
        var failures = new List<string>();
        // Both languages run even when the pre-fix baseline fails; each independently diagnoses the defect.
        foreach (var host in new[] { false, true })
        {
            try
            {
                foreach (var kind in new[] { "open", "land" })
                foreach (var site in new[] { "collected", "advertised" })
                foreach (var delta in new[] { -1, 1 })
                foreach (var recover in new[] { true, false })
                {
                    var requests = new JsonObject { [PathFor(Project, "land")] = new JsonArray(Envelope(Project)),
                        [PathFor(Project)] = new JsonArray(Envelope(Project)) };
                    JsonObject Scoped(string project, int number)
                    {
                        var rows = Enumerable.Range(1, number).Select(i =>
                        {
                            var row = Row(kind == "open" ? "Queued" : "Succeeded");
                            row["id"] = project[..8] + "-1111-1111-1111-" + i.ToString("D12");
                            row["projectId"] = project; row["scopeSource"] = "Task";
                            if (kind == "land") row["landRequestedAt"] = "2020-01-01T00:00:00Z";
                            requests["/api/agent-tasks/" + row["id"]] = new JsonArray(Detail(row));
                            return row;
                        }).ToArray();
                        return Envelope(project, rows);
                    }
                    var a = Scoped(Project, 6); var b = Scoped(OtherProject, 1); var c = Scoped(ThirdProject, 2);
                    Exclude(a, site == "collected" ? [(OtherProject, 1)] : [(OtherProject, 1), (ThirdProject, 2)]);
                    Exclude(b, site == "collected" ? [(Project, 6 + delta)] : [(ThirdProject, 2 + delta)]);
                    var stable = b.DeepClone().AsObject(); Exclude(stable, site == "collected" ? [(Project, 6)] : [(ThirdProject, 2)]);
                    requests[PathFor(Project, kind)] = new JsonArray(a);
                    requests[PathFor(OtherProject, kind)] = recover ? new JsonArray(b, stable) : new JsonArray(b);
                    requests[PathFor(ThirdProject, kind)] = new JsonArray(c);
                    var expected = site == "collected" ? 6 + delta : 2;
                    var observed = site == "collected" ? 6 : 2 + delta;
                    var error = recover ? "" : $"RecycleTaskCensusUnknown cause=Drift field=excluded.byProject.count project={(site == "collected" ? Project : ThirdProject)} expected={expected} observed={observed}";
                    await Check(host, new Vector($"{site}/{kind}/{delta}/{recover}", requests, error, Sleeps: recover ? 1 : 2, Wait: !recover));
                    if (!host && site == "collected" && kind == "open" && delta == -1 && recover)
                        await CountRecoveryReceipt(requests);
                    if (recover)
                    {
                        requests[PathFor(OtherProject, kind)] = new JsonArray(stable.DeepClone());
                        await Check(host, new Vector($"stable-nonzero/{site}/{kind}/{delta}", requests));
                    }
                    else if (delta == -1)
                    {
                        requests[PathFor(Project, kind)]![0]!["items"]![0]!["runnerId"] = "server2";
                        await Check(host, new Vector($"drift-bound-companion/{site}/{kind}", requests,
                            kind == "open" ? "RecycleBoundTasks" : "RecycleLandInFlight"));
                    }
                }
                foreach (var type in new[] { "count", "total", "project" })
                foreach (var variant in type == "project" ? new[] { "self", "duplicate", "invalid" } :
                    new[] { "zero", "negative", "fraction", "string", "null", "missing" })
                {
                    var envelope = Exclude(Envelope(Project), (OtherProject, 1));
                    JsonNode? value = variant switch { "zero" => JsonValue.Create(0), "negative" => JsonValue.Create(-1),
                        "fraction" => JsonValue.Create(1.5), "string" => JsonValue.Create("1"), _ => null };
                    if (type == "project")
                    {
                        if (variant == "duplicate") Exclude(envelope, (OtherProject, 1), (OtherProject, 1));
                        else envelope["excluded"]!["byProject"]![0]!["projectId"] = variant == "self" ? Project : "bad";
                    }
                    else
                    {
                        var owner = type == "count" ? envelope["excluded"]!["byProject"]![0]!.AsObject() : envelope["excluded"]!.AsObject();
                        owner[type] = value;
                        if (variant == "missing") owner.Remove(type);
                        if (type == "count" && variant == "zero") envelope["excluded"]!["total"] = 0;
                    }
                    var requests = new JsonObject { [PathFor(Project)] = new JsonArray(envelope) };
                    await Check(host, new Vector($"malformed-{type}/{variant}", requests,
                        "RecycleTaskCensusUnknown cause=Malformed field=excluded." + (type == "total" ? "total" : "byProject." + (type == "project" ? "projectId" : "count"))));
                }
                await TotalBoundary(host);
            }
            catch (Exception e) { failures.Add((host ? "host: " : "wrapper: ") + e.Message); }
        }
        failures.ShouldBeEmpty(string.Join("\n", failures));
    }

    // The incident: six collected rows, then the nested scope advertises five.
    // After container removal that churn must not refuse the census. Before removal it stays Drift.
    internal static async Task PostRemoval()
    {
        var failures = new List<string>();
        foreach (var host in new[] { false, true })
        {
            try
            {
                var removed = await Probe(host, IncidentRequests(), removed: true);
                removed.Error.ShouldBe("", (host ? "host" : "wrapper") + " post-removal must continue, not " + removed.Error);
                removed.Code.ShouldBe(0);
                removed.Sleeps.ShouldBe(2, "the stable retry is still attempted before acceptance");
                removed.Error.ShouldNotContain("Malformed");
                if (host)
                {
                    removed.Snapshot!["open"]!["tasks"]!.AsObject().Count.ShouldBe(7, "walked rows are kept");

                    // Each removal marker has to be enough on its own. phase=stopped is not.
                    var ownedOnly = await Probe(true, IncidentRequests(), removed: true,
                        record: """{"phase":"stopped","ownedRemoved":true}""");
                    ownedOnly.Error.ShouldBe("", "ownedRemoved alone must keep rows, not " + ownedOnly.Error);
                    ownedOnly.Code.ShouldBe(0);
                    ownedOnly.Sleeps.ShouldBe(2, "ownedRemoved alone still retries before acceptance");
                    ownedOnly.Snapshot!["open"]!["tasks"]!.AsObject().Count.ShouldBe(7, "ownedRemoved alone keeps walked rows");

                    var phaseOnly = await Probe(true, IncidentRequests(), removed: true,
                        record: """{"phase":"volumesRemoved","ownedRemoved":false}""");
                    phaseOnly.Error.ShouldBe("", "volumesRemoved alone must keep rows, not " + phaseOnly.Error);
                    phaseOnly.Code.ShouldBe(0);
                    phaseOnly.Sleeps.ShouldBe(2, "volumesRemoved alone still retries before acceptance");
                    phaseOnly.Snapshot!["open"]!["tasks"]!.AsObject().Count.ShouldBe(7, "volumesRemoved alone keeps walked rows");

                    var stopped = await Probe(true, IncidentRequests(), removed: true,
                        record: """{"phase":"stopped","ownedRemoved":false}""");
                    stopped.Error.ShouldStartWith(
                        "RecycleTaskCensusUnknown cause=Drift field=excluded.byProject.count project=" + Project + " expected=5 observed=6");
                    stopped.Code.ShouldBe(2);
                    stopped.Sleeps.ShouldBe(2, "a resumed stopped journal still refuses drift");
                }

                var before = await Probe(host, IncidentRequests(), removed: false);
                before.Error.ShouldStartWith(
                    "RecycleTaskCensusUnknown cause=Drift field=excluded.byProject.count project=" + Project + " expected=5 observed=6");
                before.Code.ShouldBe(2);
                before.Sleeps.ShouldBe(2);
                before.Error.ShouldNotContain("Malformed");

                var malformed = IncidentRequests();
                malformed[PathFor(Project)]![0]!["excluded"]!["byProject"]![0]!["count"] = 0;
                malformed[PathFor(Project)]![0]!["excluded"]!["total"] = 0;
                var bad = await Probe(host, malformed, removed: false, wait: false);
                bad.Error.ShouldStartWith("RecycleTaskCensusUnknown cause=Malformed field=excluded.byProject.count");
                bad.Sleeps.ShouldBe(0);

                var bound = IncidentRequests();
                bound[PathFor(Project)]![0]!["items"]![0]!["runnerId"] = "server2";
                var held = await Probe(host, bound, removed: true, wait: false);
                held.Error.ShouldStartWith("RecycleBoundTasks");
                held.Code.ShouldBe(host ? 4 : 2);
                held.Sleeps.ShouldBe(0, "a bound row refuses before count drift is classified");
            }
            catch (Exception e) { failures.Add((host ? "host: " : "wrapper: ") + e.Message); }
        }
        failures.ShouldBeEmpty(string.Join("\n", failures));
    }

    private static JsonObject IncidentRequests()
    {
        var requests = new JsonObject
        {
            [PathFor(Project, "land")] = new JsonArray(Envelope(Project)),
            [PathFor(OtherProject, "land")] = new JsonArray(Envelope(OtherProject))
        };
        JsonObject Scope(string project, int count, (string Project, int Count) excluded)
        {
            var rows = Enumerable.Range(1, count).Select(i =>
            {
                var row = Row();
                row["id"] = project[..8] + "-1111-1111-1111-" + i.ToString("D12");
                row["projectId"] = project;
                row["scopeSource"] = "Task";
                requests["/api/agent-tasks/" + row["id"]] = new JsonArray(Detail(row));
                return row;
            }).ToArray();
            return Exclude(Envelope(project, rows), excluded);
        }
        requests[PathFor(Project)] = new JsonArray(Scope(Project, 6, (OtherProject, 1)));
        requests[PathFor(OtherProject)] = new JsonArray(Scope(OtherProject, 1, (Project, 5)));
        return requests;
    }

    private static async Task<(string Error, int Code, int Sleeps, JsonNode? Snapshot)> Probe(
        bool host, JsonObject requests, bool removed, bool wait = true, string? record = null)
    {
        using var scratch = new Scratch();
        File.WriteAllText(Path.Combine(scratch.Root, "requests.json"), requests.ToJsonString());
        if (host && record is not null)
            File.WriteAllText(Path.Combine(scratch.Root, "record.json"), record);
        else if (removed && host)
            File.WriteAllText(Path.Combine(scratch.Root, "record.json"),
                """{"phase":"containersRemoved","ownedRemoved":true}""");
        if (removed && !host)
            File.WriteAllText(Path.Combine(scratch.Root, "resume"), "1");
        var body = host ? HostProgram(scratch.Root, wait && !removed) : WrapperProgram(scratch.Root, false, wait);
        var run = await Run(host ? "bash" : "pwsh", body, scratch.Root);
        run.Exit.ShouldBe(0, "fixture: " + run.Output);
        var result = JsonNode.Parse(File.ReadAllText(Path.Combine(scratch.Root, "result.json")))!;
        return (result["error"]!.GetValue<string>(), result["code"]!.GetValue<int>(),
            result["sleeps"]!.GetValue<int>(), result["snapshot"]);
    }

    private static async Task Check(bool host, Vector vector)
    {
        using var scratch = new Scratch();
        File.WriteAllText(Path.Combine(scratch.Root, "requests.json"), vector.Requests.ToJsonString());
        var body = host ? HostProgram(scratch.Root, vector.Wait) : WrapperProgram(scratch.Root, vector.Report, vector.Wait);
        var run = await Run(host ? "bash" : "pwsh", body, scratch.Root);
        run.Exit.ShouldBe(0, vector.Name + " fixture: " + run.Output);
        var result = JsonNode.Parse(File.ReadAllText(Path.Combine(scratch.Root, "result.json")))!;
        var error = result["error"]!.GetValue<string>();
        if (host)
        {
            var receipt = JsonNode.Parse(File.ReadAllText(Path.Combine(scratch.Root, "census-probe-server2.json")))!;
            receipt["refusal"]!.GetValue<string>().ShouldBe(error, vector.Name + " receipt refusal");
            JsonNode.DeepEquals(receipt["snapshot"], result["snapshot"]).ShouldBeTrue(vector.Name + " receipt snapshot");
            if (error.Length == 0 && !vector.Wait)
                JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(Path.Combine(scratch.Root, "snapshot.json"))), result["snapshot"])
                    .ShouldBeTrue(vector.Name + " returned original second snapshot");
        }
        if (vector.Error.Length == 0)
        {
            error.ShouldBe("", vector.Name + ": " + run.Output);
            result["code"]!.GetValue<int>().ShouldBe(0, vector.Name);
            if (vector.Name == "bound/report-stable") result["report"]!["boundOpen"]!.GetValue<int>().ShouldBe(1);
            if (vector.Expected is not null)
            {
                var snapshot = result["snapshot"]!;
                JsonNode.DeepEquals(snapshot["open"]!["tasks"]![Id], vector.Expected).ShouldBeTrue(vector.Name + " exact observed facts: " + snapshot);
                snapshot["open"]!["land"]!.AsObject().ContainsKey(Id).ShouldBeTrue(vector.Name + " explicit proof");
                JsonNode.DeepEquals(snapshot["open"]!["land"]![Id], vector.Proof).ShouldBeTrue(vector.Name + " proof identity");
                if (!host && !vector.Report)
                    JsonNode.DeepEquals(result["returned"], snapshot).ShouldBeTrue(vector.Name + " returned original second snapshot");
                result["reads"]!.AsArray().Any(x => x!.GetValue<string>() == "/api/agent-tasks/" + Id).ShouldBeTrue(vector.Name + " detail read");
            }
        }
        else
        {
            error.ShouldStartWith(vector.Error, customMessage: vector.Name + ": " + run.Output);
            result["code"]!.GetValue<int>().ShouldBe(host && vector.Error == "RecycleBoundTasks" ? 4 :
                host && vector.Error == "RecycleLandInFlight" ? 3 : 2, vector.Name);
            if (vector.Error == "RecycleBoundTasks")
                result["reads"]!.AsArray().Any(x => x!.GetValue<string>() == "/api/agent-tasks/" + Id).ShouldBeFalse(vector.Name + " before detail");
        }
        result["sleeps"]!.GetValue<int>().ShouldBe(vector.Sleeps, vector.Name + " bounded retry count");
        if (vector.Sleeps > 0)
            result["reads"]!.AsArray().Count(x => x!.GetValue<string>() == PathFor(Project)).ShouldBe(3, vector.Name + " complete census restarts");
        Console.WriteLine($"CASE {(host ? "host" : "wrapper")}/{vector.Name} PASS sleeps={vector.Sleeps}");
    }

    private static string WrapperProgram(string root, bool report, bool wait) => $$"""
        $ErrorActionPreference='Stop'
        $root='{{root}}'
        $ast=[Management.Automation.Language.Parser]::ParseFile('{{DelegateScriptRunner.RepoRoot}}/scripts/deploy-server2.ps1',[ref]$null,[ref]$null)
        foreach ($f in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)) { Invoke-Expression $f.Extent.Text }
        $script:requests=Get-Content -Raw "$root/requests.json" | ConvertFrom-Json
        $script:positions=@{}; $script:reads=[Collections.Generic.List[string]]::new(); $script:sleeps=0
        $script:recycleReceipt=@{}; $script:recycleReceiptTasks=@{}
        function Write-RecycleReceipt {}
        function Start-Sleep { param($Seconds,$Milliseconds) $script:sleeps++; if ($Seconds -ne 30) { throw 'retry must sleep 30 seconds' } }
        function Invoke-RecycleRead { param($Path)
            $script:reads.Add($Path)
            $values=$script:requests.$Path
            if ($null -eq $values) { throw "fixture missing $Path" }
            $index=[int]$script:positions[$Path]; $script:positions[$Path]=$index+1
            return ($values[[Math]::Min($index,$values.Count-1)] | ConvertTo-Json -Depth 30 -Compress | ConvertFrom-Json)
        }
        $errorText=''; $snapshot=$null; $code=0
        $ResumeRecycle=$null
        if (Test-Path -LiteralPath "$root/resume") { $ResumeRecycle='c100800000000000000000000000000000001' }
        $LandWaitMinutes=30
        try { $value={{(wait ? "Wait-RecycleTaskCensus" : "Assert-RecycleTaskCensus")}} -RunnerId server2 -ProjectId '{{Project}}' {{(report ? "-Report" : "")}}; $snapshot=$script:recycleReceipt.snapshot }
        catch { $errorText=$_.Exception.Message; $code=2 }
        $returned=$null; $reportResult=$null
        if ($code -eq 0 -and '{{report}}' -eq 'False' -and $null -ne $value) { $returned=$value|ConvertFrom-Json }
        if ($code -eq 0 -and '{{report}}' -eq 'True') { $reportResult=$value }
        @{error=$errorText;code=$code;snapshot=$snapshot;returned=$returned;report=$reportResult;reads=@($script:reads);sleeps=$script:sleeps} | ConvertTo-Json -Depth 40 | Set-Content "$root/result.json"
        """;

    private static string HostProgram(string root, bool wait)
    {
        var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
        var functions = Regex.Matches(source, @"(?ms)^c1008_tasks(?:_\w+)?\(\) \{\n.*?^\}").Select(m => m.Value);
        return string.Join('\n', functions) + "\n" + $$"""
            set -u
            root='{{root}}'; CASE_DIR="$root"; CASE=probe
            C1008_RUNNER=server2; C1008_PROJECT_ID={{Project}}
            C1008_RECORD=''
            if [ -f "$root/record.json" ]; then C1008_RECORD="$(cat "$root/record.json")"; fi
            declare -A positions=()
            reads='[]'; sleeps=0
            sleep() { [ "$1" = 30 ] || return 99; sleeps=$((sleeps+1)); }
            c1008_http() {
                local path="$1" index="${positions[$1]:-0}"
                positions[$path]=$((index+1))
                reads="$(jq -nc --argjson prior "$reads" --arg path "$path" '$prior+[$path]')"
                C1008_HTTP_BODY="$(jq -ce --arg path "$path" --argjson index "$index" '.[$path] | .[([$index,length-1]|min)]' "$root/requests.json")" || {
                    C1008_TASK_ERROR="fixture missing $path"; return 2;
                }
            }
            code=0; {{(wait ? "c1008_tasks_wait" : "c1008_tasks")}} > "$root/snapshot.json" || code=$?
            jq -n --arg error "$C1008_TASK_ERROR" --argjson code "$code" --argjson snapshot "$C1008_TASKS" --argjson reads "$reads" --argjson sleeps "$sleeps" \
                '{error:$error,code:$code,snapshot:$snapshot,reads:$reads,sleeps:$sleeps}' > "$root/result.json"
            exit 0
            """;
    }

    private static async Task Boundaries(bool host, bool passes)
    {
        // Bind the real shared predicate and, for pass comparisons, the actual reconciliation.
        // These probes intentionally reach guards that normal HTTP shape validation masks.
        using var scratch = new Scratch();
        var data = new JsonArray();
        void Add(string name, JsonObject before, JsonObject after, JsonObject detail, bool expected)
            => data.Add(new JsonObject { ["name"] = name, ["before"] = before, ["after"] = after,
                ["detail"] = detail, ["expected"] = expected });
        Add("safe", Row(), Row("Dispatched"), Detail(Row("Dispatched")), true);
        var boundBefore = Row(); var boundAfter = Row("Dispatched");
        boundBefore["runnerId"] = "server2"; boundAfter["runnerId"] = "server2";
        Add("both-bound", boundBefore, boundAfter, Detail(boundAfter), false);
        foreach (var side in new[] { "before", "after" })
        foreach (var fault in new[] { "terminal", "bound", "missing" })
        {
            var a = Row(); var b = Row("Dispatched"); var target = side == "before" ? a : b;
            if (fault == "terminal") target["status"] = "Succeeded";
            if (fault == "bound") target["runnerId"] = "server2";
            if (fault == "missing") target.Remove("id");
            Add(side + "/" + fault, a, b, Detail(b, Proof()), false);
        }
        foreach (var key in new[] { "id", "status", "runnerId", "projectId", "scopeSource", "landRequestedAt", "landStartedAt" })
        {
            var a = Row(); var b = Row("Dispatched"); a.Remove(key); b.Remove(key);
            Add("both-missing/" + key, a, b, Detail(b), false);
        }
        foreach (var key in new[] { "id", "runnerId", "projectId", "scopeSource", "landRequestedAt", "landStartedAt" })
        {
            var a = Row(); var b = Row("Dispatched"); b[key] = "changed";
            Add("field/" + key, a, b, Detail(b), false);
        }
        foreach (var variant in new[] { "missing", "pending", "unknown", "invalid-event", "missing-event" })
        {
            var a = Row(); var b = Row("Dispatched"); var d = Detail(b, Proof());
            if (variant == "missing") d.Remove("landRequest");
            if (variant == "pending") d["landRequest"] = Proof("Running", null);
            if (variant == "unknown") d["landRequest"] = Proof("unknown");
            if (variant == "invalid-event") d["landRequest"] = Proof(terminal: "BAD");
            if (variant == "missing-event") d["landRequest"]!.AsObject().Remove("terminalEventId");
            Add(variant, a, b, d, false);
        }
        File.WriteAllText(Path.Combine(scratch.Root, "boundary.json"), data.ToJsonString());
        string body;
        if (!host)
        {
            body = $$"""
                $ErrorActionPreference='Stop'
                $ast=[Management.Automation.Language.Parser]::ParseFile('{{DelegateScriptRunner.RepoRoot}}/scripts/deploy-server2.ps1',[ref]$null,[ref]$null)
                foreach ($f in $ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst]},$false)) { Invoke-Expression $f.Extent.Text }
                $calls=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Test-RecycleOpenStatusMove'},$true))
                if ($calls.Count -ne 2) { throw 'both real status-qualification call sites required' }
                $cases=Get-Content -Raw '{{scratch.Root}}/boundary.json' | ConvertFrom-Json
                foreach ($c in $cases) {
                    $before=$c|ConvertTo-Json -Depth 30 -Compress
                    $actual=Test-RecycleOpenStatusMove -Before $c.before -After $c.after -Detail $c.detail -RunnerId server2
                    if ($actual -ne $c.expected) { throw "boundary/$($c.name): expected=$($c.expected) actual=$actual" }
                    if (($c|ConvertTo-Json -Depth 30 -Compress) -cne $before) { throw 'input changed' }
                }
                'BOUNDARIES_DONE'
                """;
        }
        else
        {
            var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
            var matches = Regex.Matches(source, @"(?s)# C1361 predicate begin\n(.*?)# C1361 predicate end");
            matches.Count.ShouldBe(2, "both real qualification call sites");
            matches[0].Groups[1].Value.ShouldBe(matches[1].Groups[1].Value);
            body = "jq -e '" + matches[0].Groups[1].Value +
                " all(.[]; . as $c | (open_status_move($c.before;$c.after;$c.detail;\"server2\"))==$c.expected)' '" + scratch.Root + "/boundary.json' || exit 1\necho BOUNDARIES_DONE\n";
        }
        var run = await Run(host ? "bash" : "pwsh", body, scratch.Root);
        run.Exit.ShouldBe(0, run.Output); run.Output.ShouldContain("BOUNDARIES_DONE");
        Console.WriteLine($"BOUNDARY {(host ? "host" : "wrapper")}/{passes}: {data.Count} cases PASS");
        if (passes) await ReconciliationBoundaries(host, data);
        if (passes) await LandProjection(host);
    }

    private static async Task FullWrapper(bool passes)
    {
        foreach (var variant in new[] { "primary", "pending", "runner", "bound-report" })
        {
            var before = Row(); var after = Row("Dispatched");
            if (variant == "runner") after["runnerId"] = "other";
            if (variant == "bound-report") { before["runnerId"] = "server2"; after["runnerId"] = "server2"; }
            var v = StatusVector(variant, passes, before, after, variant == "pending" ? Proof("Running", null) : null);
            using var fixture = new C1008WrapperFixture();
            var queries = new JsonObject(); var details = new JsonObject();
            foreach (var (key, value) in v.Requests)
            {
                if (key.Contains('?')) queries[key] = value!.DeepClone();
                else details[key[(key.LastIndexOf('/') + 1)..]] = value!.DeepClone();
            }
            fixture.State["taskQueries"] = queries; fixture.State["details"] = details;
            var run = await fixture.Run("check-census");
            run.Trace.All(x => x["kind"]?.GetValue<string>() != "case").ShouldBeTrue(variant + " offline wrapper has no host action");
            run.Trace.Any(x => x["path"]?.GetValue<string>() == "/api/agent-tasks/" + Id).ShouldBeTrue(variant + " actual detail read");
            if (variant == "primary")
            {
                run.Exit.ShouldBe(0, run.Output);
                var root = Regex.Match(run.Output, @"(?m)^Census check complete: (.+)$").Groups[1].Value.Trim();
                var receipt = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "census-check-census-server2.json")))!;
                JsonNode.DeepEquals(receipt["snapshot"]!["open"]!["tasks"]![Id], v.Expected).ShouldBeTrue("wrapper actual second list facts");
                var recorded = receipt["tasks"]!.AsArray().Single()!;
                recorded["status"]!.GetValue<string>().ShouldBe(passes ? "Dispatched" : "Queued");
                recorded.AsObject().ContainsKey("landRequest").ShouldBeTrue(); recorded["landRequest"].ShouldBeNull();
            }
            else
            {
                run.Exit.ShouldBe(2, run.Output);
                run.Output.ShouldContain(passes ? "RecycleTaskCensusUnknown cause=Unstable" : "RecycleLandUnknown");
            }
            Console.WriteLine("FULL_WRAPPER " + variant + " PASS");
        }
    }

    private static async Task ReconciliationBoundaries(bool host, JsonArray predicateCases)
    {
        using var scratch = new Scratch();
        var cases = new JsonArray();
        JsonObject Snapshot(JsonNode row, JsonNode? proof) => new()
        {
            ["open"] = new JsonObject { ["tasks"] = new JsonObject { [Id] = row.DeepClone() },
                ["land"] = new JsonObject { [Id] = proof?.DeepClone() }, ["scopes"] = new JsonObject() },
            ["land"] = new JsonObject { ["tasks"] = new JsonObject() }
        };
        void Add(string name, JsonObject prior, JsonObject current, bool align)
        {
            var expected = prior.DeepClone();
            if (align) expected["open"]!["tasks"]![Id]!["status"] = current["open"]!["tasks"]![Id]!["status"]?.DeepClone();
            cases.Add(new JsonObject { ["name"] = name, ["prior"] = prior, ["current"] = current, ["expected"] = expected });
        }
        foreach (var c in predicateCases)
        {
            var prior = Snapshot(c!["before"]!, c["detail"]!["landRequest"]);
            var current = Snapshot(c["after"]!, c["detail"]!["landRequest"]);
            if (!c["detail"]!.AsObject().ContainsKey("landRequest")) current["open"]!["land"]!.AsObject().Remove(Id);
            Add(c["name"]!.GetValue<string>(), prior, current, c["expected"]!.GetValue<bool>());
        }
        foreach (var variant in new[] { "missing-prior", "missing-current", "event", "null-terminal", "terminal-null" })
        {
            var prior = Snapshot(Row(), variant is "event" or "terminal-null" ? Proof() : null);
            var current = Snapshot(Row("Dispatched"), variant == "event" ? Proof(terminal: "55555555-5555-5555-5555-555555555555") :
                variant == "null-terminal" ? Proof() : null);
            if (variant == "missing-prior") prior["open"]!["land"]!.AsObject().Remove(Id);
            if (variant == "missing-current") current["open"]!["land"]!.AsObject().Remove(Id);
            Add(variant, prior, current, false);
        }
        var input = cases.ToJsonString(); File.WriteAllText(Path.Combine(scratch.Root, "maps.json"), input);
        string body;
        if (!host)
        {
            body = $$"""
                $ErrorActionPreference='Stop'
                $ast=[Management.Automation.Language.Parser]::ParseFile('{{DelegateScriptRunner.RepoRoot}}/scripts/deploy-server2.ps1',[ref]$null,[ref]$null)
                foreach ($name in @('Test-RecycleJsonEqual','Test-RecycleOpenStatusMove','Sync-RecycleOpenStatuses')) {
                    $functions=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true))
                    if ($functions.Count -ne 1) { throw "extraction count: $name" }; Invoke-Expression $functions[0].Extent.Text
                }
                foreach ($c in (Get-Content -Raw '{{scratch.Root}}/maps.json'|ConvertFrom-Json)) {
                    $liveBefore=$c.current|ConvertTo-Json -Depth 30 -Compress
                    $copy=$c.prior|ConvertTo-Json -Depth 30 -Compress|ConvertFrom-Json
                    $null=Sync-RecycleOpenStatuses -Prior $copy -Live $c.current -RunnerId server2
                    if (-not (Test-RecycleJsonEqual $copy $c.expected)) { throw "reconciliation/$($c.name) changed ineligible facts" }
                    if (($c.current|ConvertTo-Json -Depth 30 -Compress) -cne $liveBefore) { throw 'live input changed' }
                }
                'RECONCILIATION_DONE'
                """;
        }
        else
        {
            var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
            var predicate = Regex.Matches(source, @"(?s)# C1361 predicate begin\n(.*?)# C1361 predicate end");
            var block = Regex.Matches(source, @"(?s)# C1361 reconciliation begin\n(.*?)# C1361 reconciliation end");
            predicate.Count.ShouldBe(2); block.Count.ShouldBe(1);
            body = "jq -e '" + predicate[1].Groups[1].Value +
                " all(.[]; . as $case | .current as $current | \"server2\" as $runner | .prior | (" + block[0].Groups[1].Value +
                ")==$case.expected)' '" + scratch.Root + "/maps.json' || exit 1\necho RECONCILIATION_DONE\n";
        }
        var run = await Run(host ? "bash" : "pwsh", body, scratch.Root);
        run.Exit.ShouldBe(0, run.Output); run.Output.ShouldContain("RECONCILIATION_DONE");
        File.ReadAllText(Path.Combine(scratch.Root, "maps.json")).ShouldBe(input, "input JSON bytes unchanged");
        Console.WriteLine($"RECONCILIATION {(host ? "host" : "wrapper")}: {cases.Count} cases PASS");
    }

    private static async Task LandProjection(bool host)
    {
        using var scratch = new Scratch();
        var bound = Row("Succeeded"); bound["runnerId"] = "server2"; bound["landStartedAt"] = "2020-01-01T00:00:00Z";
        var other = bound.DeepClone().AsObject(); other["id"] = Event; other["runnerId"] = "desktop";
        var rows = new JsonObject { [Id] = bound, [Event] = other };
        File.WriteAllText(Path.Combine(scratch.Root, "rows.json"), rows.ToJsonString());
        string body;
        if (host)
        {
            var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
            var expressions = Regex.Matches(source, @"'(?<body>if \$kind==""open"" then \{scopes:\.,tasks:\$rows,land:\$land\} else .*?)'");
            expressions.Count.ShouldBe(1, "real land projection");
            body = "jq -n --arg kind land --arg runner server2 --argjson land '{}' --slurpfile input '" + scratch.Root +
                "/rows.json' '$input[0] as $rows | {} | " + expressions[0].Groups["body"].Value +
                "' > '" + scratch.Root + "/projection.json'\n";
        }
        else
        {
            body = $$"""
                $ErrorActionPreference='Stop'
                $ast=[Management.Automation.Language.Parser]::ParseFile('{{DelegateScriptRunner.RepoRoot}}/scripts/deploy-server2.ps1',[ref]$null,[ref]$null)
                $blocks=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.ForEachStatementAst] -and $n.Extent.Text.StartsWith('foreach ($key in @($rows.Keys | Sort-Object))')},$true))
                if ($blocks.Count -ne 1) { throw 'real land projection count' }
                $rows=@{}; $source=Get-Content -Raw '{{scratch.Root}}/rows.json'|ConvertFrom-Json
                foreach ($p in $source.PSObject.Properties) { $rows[$p.Name]=$p.Value|ConvertTo-Json -Compress }
                $kind='land'; $RunnerId='server2'; $taskSnapshot=[ordered]@{}
                Invoke-Expression $blocks[0].Extent.Text
                @{tasks=$taskSnapshot}|ConvertTo-Json -Depth 20|Set-Content '{{scratch.Root}}/projection.json'
                """;
        }
        var run = await Run(host ? "bash" : "pwsh", body, scratch.Root);
        run.Exit.ShouldBe(0, run.Output);
        var actual = JsonNode.Parse(File.ReadAllText(Path.Combine(scratch.Root, "projection.json")))!["tasks"]!.AsObject();
        actual.Count.ShouldBe(1, "bound-only land projection");
        JsonNode.DeepEquals(actual[Id], bound).ShouldBeTrue("all bound land facts retained");
        Console.WriteLine("LAND_PROJECTION bound-only PASS");
    }

    private static async Task TotalBoundary(bool host)
    {
        using var scratch = new Scratch();
        string body;
        if (host)
        {
            var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
            source = source[source.IndexOf("c1008_tasks_collect() {", StringComparison.Ordinal)..source.IndexOf("c1008_tasks_match_record() {", StringComparison.Ordinal)];
            var predicates = Regex.Matches(source, @"def count:.*?;");
            predicates.Count.ShouldBe(1, "actual shared count type/range validator");
            body = "jq -ne '" + predicates[0].Value +
                " [null, -1, 1.5, \"1\", true, {}, []] | all(.[]; count|not)' || exit 1\n" +
                "jq -ne '" + predicates[0].Value + " [0,1,2] | all(.[]; count)' || exit 1\necho TOTAL_BOUNDARY_DONE\n";
        }
        else
        {
            body = $$"""
                $ErrorActionPreference='Stop'
                $ast=[Management.Automation.Language.Parser]::ParseFile('{{DelegateScriptRunner.RepoRoot}}/scripts/deploy-server2.ps1',[ref]$null,[ref]$null)
                $blocks=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.IfStatementAst] -and $n.Extent.Text.StartsWith('if (($envelope.excluded.$key -isnot [int]')},$true))
                if ($blocks.Count -ne 1) { throw 'actual total validation block count' }
                $key='total'; $path='fixture'; $script:recycleReceipt=$null
                $fn=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Stop-RecycleMalformed'},$true)
                Invoke-Expression $fn.Extent.Text
                foreach ($v in @($null,-1,1.5,'1',$true,@{},@(),0,1,2)) {
                    $envelope=[pscustomobject]@{excluded=[pscustomobject]@{total=$v} }
                    $errorText=''; try { Invoke-Expression $blocks[0].Extent.Text } catch { $errorText=$_.Exception.Message }
                    $valid=($v -is [int] -and $v -ge 0)
                    if ($valid -and $errorText) { throw "valid total refused: $errorText" }
                    if (-not $valid -and $errorText -cnotlike 'RecycleTaskCensusUnknown cause=Malformed field=excluded.total *') { throw "invalid total accepted: $v" }
                }
                'TOTAL_BOUNDARY_DONE'
                """;
        }
        var run = await Run(host ? "bash" : "pwsh", body, scratch.Root);
        run.Exit.ShouldBe(0, run.Output); run.Output.ShouldContain("TOTAL_BOUNDARY_DONE");
        Console.WriteLine($"TOTAL_BOUNDARY {(host ? "host" : "wrapper")} PASS");
    }

    private static async Task CountRecoveryReceipt(JsonObject requests)
    {
        using var fixture = new C1008WrapperFixture();
        var queries = new JsonObject(); var details = new JsonObject();
        foreach (var (path, responses) in requests)
        {
            if (path.Contains('?')) queries[path] = responses!.DeepClone();
            else details[path[(path.LastIndexOf('/') + 1)..]] = responses!.DeepClone();
        }
        fixture.State["taskQueries"] = queries; fixture.State["details"] = details;
        var run = await fixture.Run("check-census");
        run.Exit.ShouldBe(0, "count recovery reaches the real wrapper receipt: " + run.Output);
        var root = Regex.Match(run.Output, @"(?m)^Census check complete: (.+)$").Groups[1].Value.Trim();
        var receipt = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "census-check-census-server2.json")))!;
        receipt["tasks"]!.AsArray().Count.ShouldBe(7, "all collected scoped rows retained");
        receipt["reads"]!.AsArray().Any(x => x!["outcome"]?.GetValue<string>() == "Malformed").ShouldBeFalse("valid drift is never marked malformed");
        receipt["snapshot"]!["open"]!["tasks"]!.AsObject().Count.ShouldBe(7, "complete recovered closure");
        run.Trace.Count(x => x["path"]?.GetValue<string>() == PathFor(OtherProject)).ShouldBe(5, "retry plus two successful passes for each report target");
        run.Trace.All(x => x["kind"]?.GetValue<string>() != "case").ShouldBeTrue("no host actions");
        Console.WriteLine("COUNT_RECOVERY_RECEIPT PASS");
    }

    private static async Task PendingDetailBoundary(bool host)
    {
        using var scratch = new Scratch();
        string body;
        if (host)
        {
            var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
            var start = source.IndexOf("if printf '%s' \"$detail\" | jq -e --argjson row \"$row\" --arg runner \"$C1008_RUNNER\" '.landRequest!=null", StringComparison.Ordinal);
            start.ShouldBeGreaterThanOrEqualTo(0, "real pending-detail guard");
            var end = source.IndexOf("\n                fi", start, StringComparison.Ordinal);
            end.ShouldBeGreaterThan(start);
            body = "probe() {\n" + source[start..(end + "\n                fi".Length)] + "\n}\n" + $$"""
                C1008_RUNNER=server2; id={{Id}}; detail='{"landRequest":{"state":"Running","terminalEventId":null} }'
                for runner in server2 desktop; do
                    row="$(jq -nc --arg runner "$runner" '{runnerId:$runner}')"
                    C1008_TASK_ERROR=''; code=0; probe || code=$?
                    if [ "$runner" = server2 ]; then
                        [ "$code" = 3 ] && [ "$C1008_TASK_ERROR" = 'RecycleLandInFlight {{Id}}' ] || exit 1
                    else [ "$code" = 0 ] && [ -z "$C1008_TASK_ERROR" ] || exit 1; fi
                done
                echo PENDING_DETAIL_DONE
                """;
        }
        else
        {
            body = $$"""
                $ErrorActionPreference='Stop'
                $ast=[Management.Automation.Language.Parser]::ParseFile('{{DelegateScriptRunner.RepoRoot}}/scripts/deploy-server2.ps1',[ref]$null,[ref]$null)
                $blocks=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.IfStatementAst] -and $n.Extent.Text.StartsWith('if ($null -ne $detail.landRequest) {')},$true))
                if ($blocks.Count -ne 1) { throw 'real pending-detail guard count' }
                $detail='{"landRequest":{"state":"Running","terminalEventId":null} }'|ConvertFrom-Json
                $id='{{Id}}'; $Report=$false
                foreach ($bound in @($true,$false)) {
                    $pendingLands=[Collections.Generic.HashSet[string]]::new(); $boundLands=[Collections.Generic.HashSet[string]]::new()
                    $errorText=''; try { Invoke-Expression $blocks[0].Extent.Text } catch { $errorText=$_.Exception.Message }
                    if ($bound -and $errorText -cne 'RecycleLandInFlight {{Id}}') { throw "bound pending detail: $errorText" }
                    if (-not $bound -and $errorText) { throw "off-runner pending detail: $errorText" }
                    if ($pendingLands.Count -ne 1 -or $boundLands.Count -ne [int]$bound) { throw 'pending counters' }
                }
                'PENDING_DETAIL_DONE'
                """;
        }
        var run = await Run(host ? "bash" : "pwsh", body, scratch.Root);
        run.Exit.ShouldBe(0, run.Output); run.Output.ShouldContain("PENDING_DETAIL_DONE");
        Console.WriteLine($"PENDING_DETAIL_BOUNDARY {(host ? "host" : "wrapper")} bound/off-runner PASS");
    }

    internal static async Task AppearanceBoundary()
    {
        // Public host calls reject the bound land earlier; execute the actual appearance program
        // to make its independent land-map guard observable before final snapshot equality.
        using var scratch = new Scratch();
        var fresh = Row("Working"); var bound = Row("Succeeded"); bound["runnerId"] = "server2";
        bound["landRequestedAt"] = "2020-01-01T00:00:00Z";
        JsonObject Snapshot(bool current) => new()
        {
            ["open"] = new JsonObject { ["tasks"] = current ? new JsonObject { [Id] = fresh.DeepClone() } : new JsonObject(),
                ["land"] = current ? new JsonObject { [Id] = null } : new JsonObject(),
                ["scopes"] = new JsonObject { [Project] = new JsonObject { ["ids"] = current ? new JsonArray(Id) : new JsonArray() } } },
            ["land"] = new JsonObject { ["tasks"] = new JsonObject { [Id] = bound.DeepClone() } }
        };
        var prior = Snapshot(false); var current = Snapshot(true);
        File.WriteAllText(Path.Combine(scratch.Root, "prior.json"), prior.ToJsonString());
        File.WriteAllText(Path.Combine(scratch.Root, "current.json"), current.ToJsonString());
        var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
        var programs = Regex.Matches(source, @"(?s)if forgiven=""\$\(printf '%s' ""\$previous"" \| jq -Sc --argjson current ""\$snapshot"" '\n(.*?)\n            '\)");
        programs.Count.ShouldBe(1, "unique real appearance program");
        var body = "jq --slurpfile live '" + scratch.Root + "/current.json' '$live[0] as $current | " + programs[0].Groups[1].Value +
            "' '" + scratch.Root + "/prior.json' > '" + scratch.Root + "/result.json'\n";
        var run = await Run("bash", body, scratch.Root);
        run.Exit.ShouldBe(0, run.Output);
        JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(Path.Combine(scratch.Root, "result.json"))), prior)
            .ShouldBeTrue("bound land membership prevents appearance reconciliation");
        Console.WriteLine("APPEARANCE_BOUNDARY bound-land PASS");
    }

    private static async Task<(int Exit, string Output)> Run(string program, string body, string root)
    {
        if (program == "bash" && OperatingSystem.IsWindows())
        {
            // Reuse the WSL lane; Windows paths and bash.exe's implicit WSL argv do
            // not name the same files. All embedded scratch paths are shell quoted.
            var quoted = root.Replace("'", "'\\''");
            var input = "census_root=$(wslpath -u '" + quoted + "') || exit 97\n" +
                body.Replace(root, "'\"$census_root\"'", StringComparison.Ordinal);
            return RemoteScriptContractTests.LinuxShellResult(input);
        }
        var file = Path.Combine(root, program == "pwsh" ? "probe.ps1" : "probe.sh"); File.WriteAllText(file, body);
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (program == "pwsh") { psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-File"); }
        psi.ArgumentList.Add(file);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } throw; }
        return (process.ExitCode, await stdout + await stderr);
    }
    private sealed class Scratch : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("c1361-").FullName;
        public void Dispose() => Directory.Delete(Root, true);
    }
}
