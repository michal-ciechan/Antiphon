using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.NightlyCoreGolden;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// W2-nightly S10: the compiled qualification heartbeat (Antiphon.Nightly/QualificationHeartbeat.cs) in-process. The
/// active-chunk identity is read as the deleted PowerShell read it (the NightlyCoreGoldenTests activeChunk golden) and
/// written bound to the suite's plan and the planned index; the planned cost refuses every unbound, settled or changed
/// case (the heartbeat rows of test-nightly-failed-first.ps1 HeartbeatBindings, with their refusals named); the journal's
/// generation prefix decides liveness; the supervisor's monitor beats on an injected clock and process table.
/// </summary>
[Category("Integration")]
public sealed class QualificationHeartbeatTests
{
    private static readonly string Source = new('a', 40);
    private static readonly string Policy = new('b', 64);
    private static readonly string CostFile = new('c', 64);
    private static readonly string Assembly = new('d', 64);

    /// <summary>A suite's planned groups (groups-planned-&lt;suite&gt;.json) bound to the fixed digests.</summary>
    internal static string PlanJson(string suite, string runId, string attempt, params (string Chunk, double Seconds)[] groups) =>
        new JsonObject
        {
            ["version"] = 1, ["suite"] = suite, ["runId"] = runId, ["attempt"] = attempt, ["sourceDigest"] = Source, ["policyDigest"] = Policy,
            ["costFileDigest"] = CostFile, ["assemblyDigest"] = Assembly,
            ["groups"] = new JsonArray(groups.Select(g => (JsonNode)new JsonObject { ["chunk"] = g.Chunk, ["plannedSeconds"] = g.Seconds }).ToArray()),
        }.ToJsonString();

    /// <summary>The planned index (groups-planned-index.json) naming each suite's plan file.</summary>
    internal static string IndexJson(string runId, string attempt, params string[] suites) =>
        new JsonObject
        {
            ["version"] = 1, ["runId"] = runId, ["attempt"] = attempt,
            ["suites"] = new JsonArray(suites.Select(s => (JsonNode)new JsonObject
            {
                ["suite"] = s, ["file"] = "groups-planned-" + s + ".json", ["runId"] = runId, ["attempt"] = attempt, ["sourceDigest"] = Source,
                ["policyDigest"] = Policy, ["costFileDigest"] = CostFile, ["assemblyDigest"] = Assembly,
            }).ToArray()),
        }.ToJsonString();

    [Test]
    public void Active_chunk_reader_matches_the_PowerShell_golden()
    {
        // The variants Active_chunk_marker_matches_the_goldens writes; the golden's ids are what PowerShell read.
        var root = NewRoot();
        try
        {
            var h = new string('a', 64);
            (string, object?)[] valid = [("format", "antiphon-qualification-active-chunk-v1"), ("state", "Active"), ("generation", 3), ("runId", "run-1"), ("attempt", "2"),
                ("suite", "antiphon"), ("chunk", "all-group-1"), ("planFile", "groups-planned-antiphon.json"), ("planDigest", h), ("sourceDigest", new string('b', 40)),
                ("policyDigest", h), ("costFileDigest", h), ("assemblyDigest", h)];
            string Variant((string Key, object? Value)[] changes, params string[] drop)
            {
                var v = Ordered();
                foreach (var (key, value) in valid) if (!drop.Contains(key)) v[key] = value;
                foreach (var (key, value) in changes) v[key] = value;
                return PsJson.Indented(v, 5);
            }
            var texts = new (string Name, string Text)[]
            {
                ("valid", Variant([])), ("idle", Variant([("state", "Idle"), ("generation", 1)])), ("stateCase", Variant([("state", "active")])),
                ("gen0", Variant([("generation", 0)])), ("genStr", Variant([("generation", "1")])), ("genDouble", Variant([("generation", 1.5)])),
                ("genBig", Variant([("generation", 99999999999L)])), ("noGen", Variant([], "generation")), ("noFormat", Variant([], "format")),
                ("runIdDate", Variant([("runId", "2026-01-01T00:00:00")])), ("attempt0", Variant([("attempt", "0")])),
                ("suiteUpper", Variant([("suite", "Antiphon"), ("planFile", "groups-planned-Antiphon.json")])), ("planFile", Variant([("planFile", "groups-planned-other.json")])),
                ("digestUpper", Variant([("planDigest", new string('A', 64))])), ("sourceNum", Variant([("sourceDigest", 5)])),
                ("hasIdentity", Variant([("identityDigest", "x")])), ("extra", Variant([("extra", new object[] { 1, 2 })])),
                ("bom", (char)0xFEFF + Variant([])), ("array", "[1,2]"), ("comment", "{ /* c */ " + Variant([])[1..]),
                ("trailing", Variant([]).TrimEnd().TrimEnd('}') + ",}"), ("empty", ""),
            };
            using var golden = JsonDocument.Parse(Read("activeChunk"));
            var expected = golden.RootElement.GetProperty("ids").EnumerateArray().Select(e => e.GetString()!).ToArray();
            expected.Length.ShouldBe(texts.Length);
            var accepted = 0;
            for (var i = 0; i < texts.Length; i++)
            {
                var (name, text) = texts[i];
                var dir = Path.Combine(root, name);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "active-chunk.json"), text.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
                var read = QualificationHeartbeat.ReadActiveChunkIdentity(dir);
                var want = expected[i];
                want.ShouldStartWith(name + "=");
                var body = want[(name.Length + 1)..];
                if (body == "null")
                {
                    read.ShouldBeNull(name);
                    continue;
                }
                read.ShouldNotBeNull(name);
                accepted++;
                using var record = JsonDocument.Parse(body[..body.LastIndexOf('|')]);
                var r = record.RootElement;
                string S(string field) => r.GetProperty(field).GetString()!;
                (read.Format, read.State, read.RunId, read.Attempt, read.Suite, read.Chunk, read.PlanFile, read.PlanDigest)
                    .ShouldBe((S("format"), S("state"), S("runId"), S("attempt"), S("suite"), S("chunk"), S("planFile"), S("planDigest")), name);
                (read.SourceDigest, read.PolicyDigest, read.CostFileDigest, read.AssemblyDigest)
                    .ShouldBe((S("sourceDigest"), S("policyDigest"), S("costFileDigest"), S("assemblyDigest")), name);
                read.Generation.ShouldBe(r.GetProperty("generation").GetInt64(), name);
                read.IdentityDigest.ShouldBe(S("identityDigest"), name);
            }
            accepted.ShouldBe(7);
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public void The_active_chunk_identity_is_bound_to_the_plan_and_counts_generations()
    {
        var root = NewRoot();
        try
        {
            var plan = PlanJson("antiphon", "run-1", "2", ("all-group-1", 600), ("all-group-2", 60));
            File.WriteAllText(Path.Combine(root, "groups-planned-antiphon.json"), plan);
            File.WriteAllText(Path.Combine(root, "groups-planned-index.json"), IndexJson("run-1", "2", "antiphon"));
            var planDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "groups-planned-antiphon.json"))));

            QualificationHeartbeat.WriteActiveChunkIdentity(root, "antiphon", "all-group-1", "run-1", "2").ShouldBe(new QualificationActiveChunkWrite(true, ""));
            var activePath = Path.Combine(root, "active-chunk.json");
            var expected = Ordered(("format", "antiphon-qualification-active-chunk-v1"), ("state", "Active"), ("generation", 1), ("runId", "run-1"), ("attempt", "2"),
                ("suite", "antiphon"), ("chunk", "all-group-1"), ("planFile", "groups-planned-antiphon.json"), ("planDigest", planDigest), ("sourceDigest", Source),
                ("policyDigest", Policy), ("costFileDigest", CostFile), ("assemblyDigest", Assembly));
            File.ReadAllText(activePath).ShouldBe(TestPhaseRecords.ActiveChunkJson(expected), "the ConvertTo-Json text Write-NightlyAtomicJson wrote");
            var first = QualificationHeartbeat.ReadActiveChunkIdentity(root)!;
            first.Generation.ShouldBe(1);
            first.PlanDigest.ShouldBe(planDigest);
            first.IdentityDigest.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(activePath))));

            QualificationHeartbeat.WriteActiveChunkIdentity(root, "antiphon", "all-group-1", "run-1", "2", "Idle").Written.ShouldBeTrue();
            QualificationHeartbeat.WriteActiveChunkIdentity(root, "antiphon", "all-group-2", "run-1", "2").Written.ShouldBeTrue();
            var third = QualificationHeartbeat.ReadActiveChunkIdentity(root)!;
            (third.Generation, third.State, third.Chunk).ShouldBe((3L, "Active", "all-group-2"));

            QualificationHeartbeat.WriteActiveChunkIdentity(root, "antiphon", "all-group-1", "run-1", "2", "active").Reason.ShouldBe("active-identity-schema");
            QualificationHeartbeat.WriteActiveChunkIdentity(root, "Antiphon", "all-group-1", "run-1", "2").Reason.ShouldBe("active-identity-schema");
            QualificationHeartbeat.WriteActiveChunkIdentity(root, "antiphon", "../all-group-1", "run-1", "2").Reason.ShouldBe("active-identity-schema");
            QualificationHeartbeat.WriteActiveChunkIdentity(root, "antiphon", "all-group-1", "run-1", "0").Reason.ShouldBe("active-identity-schema");
            QualificationHeartbeat.WriteActiveChunkIdentity(root, "antiphon", "all-group-9", "run-1", "2").Reason.ShouldBe("active-plan-binding");
            QualificationHeartbeat.WriteActiveChunkIdentity(root, "antiphon", "all-group-1", "run-2", "2").Reason.ShouldBe("active-plan-binding");
            QualificationHeartbeat.WriteActiveChunkIdentity(root, "session-runner", "all-group-1", "run-1", "2").Reason.ShouldBe("binding-file-unavailable");
            QualificationHeartbeat.ReadActiveChunkIdentity(root)!.Generation.ShouldBe(3, "a refused write leaves the identity as it was");

            // The index row and the plan disagree on a digest: refused, named by the field.
            var index = JsonNode.Parse(IndexJson("run-1", "2", "antiphon"))!;
            index["suites"]![0]!["policyDigest"] = new string('e', 64);
            File.WriteAllText(Path.Combine(root, "groups-planned-index.json"), index.ToJsonString());
            QualificationHeartbeat.WriteActiveChunkIdentity(root, "antiphon", "all-group-1", "run-1", "2").Reason.ShouldBe("active-plan-policyDigest");
            File.WriteAllText(Path.Combine(root, "groups-planned-index.json"), IndexJson("run-1", "2", "antiphon"));

            // A new attempt starts again at generation 1.
            File.WriteAllText(Path.Combine(root, "groups-planned-antiphon.json"), PlanJson("antiphon", "run-1", "3", ("all-group-1", 600)));
            File.WriteAllText(Path.Combine(root, "groups-planned-index.json"), IndexJson("run-1", "3", "antiphon"));
            QualificationHeartbeat.WriteActiveChunkIdentity(root, "antiphon", "all-group-1", "run-1", "3").Written.ShouldBeTrue();
            QualificationHeartbeat.ReadActiveChunkIdentity(root)!.Generation.ShouldBe(1);

            // The identity cannot be written over a directory, and a directory is no identity.
            File.Delete(activePath);
            Directory.CreateDirectory(activePath);
            QualificationHeartbeat.WriteActiveChunkIdentity(root, "antiphon", "all-group-1", "run-1", "3").Written.ShouldBeFalse();
            QualificationHeartbeat.ReadActiveChunkIdentity(root).ShouldBeNull();
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public void The_run_binding_writes_each_chunk_and_warns_on_a_refusal()
    {
        var root = NewRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "groups-planned-antiphon.json"), PlanJson("antiphon", "run-1", "1", ("all-group-1", 600)));
            File.WriteAllText(Path.Combine(root, "groups-planned-index.json"), IndexJson("run-1", "1", "antiphon"));
            var warnings = new List<string>();
            var write = QualificationHeartbeat.ActiveChunkWriter(warnings.Add);
            write(new NightlyActiveChunk(root, "antiphon", "all-group-1", "run-1", "1", "Active"));
            write(new NightlyActiveChunk(root, "antiphon", "all-group-1", "run-1", "1", "Idle"));
            warnings.ShouldBeEmpty();
            var idle = QualificationHeartbeat.ReadActiveChunkIdentity(root)!;
            (idle.Generation, idle.State).ShouldBe((2L, "Idle"));
            write(new NightlyActiveChunk(root, "antiphon", "missing", "run-1", "1", "Active"));
            warnings.ShouldBe(new[] { "active-identity-unavailable: active-plan-binding" });
            // The kept parse is keyed by the plan's bytes: a rewritten plan is read again.
            File.WriteAllText(Path.Combine(root, "groups-planned-antiphon.json"), PlanJson("antiphon", "run-1", "1", ("all-group-1", 600), ("missing", 60)));
            write(new NightlyActiveChunk(root, "antiphon", "missing", "run-1", "1", "Active"));
            warnings.Count.ShouldBe(1);
            QualificationHeartbeat.ReadActiveChunkIdentity(root)!.Chunk.ShouldBe("missing");
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public void The_planned_cost_is_the_bound_chunks_and_unknown_otherwise()
    {
        var root = NewRoot();
        try
        {
            // HeartbeatBindings: two suites share a chunk id; the identity says which suite is running.
            File.WriteAllText(Path.Combine(root, "groups-planned-antiphon.json"), PlanJson("antiphon", "heartbeat-run", "2", ("all-group-3", 1800), ("all-group-4", 120)));
            File.WriteAllText(Path.Combine(root, "groups-planned-session-runner.json"), PlanJson("session-runner", "heartbeat-run", "2", ("all-group-3", 60), ("all-group-4", 120)));
            File.WriteAllText(Path.Combine(root, "groups-planned-index.json"), IndexJson("heartbeat-run", "2", "antiphon", "session-runner"));
            var path = Path.Combine(root, "groups-planned.json");
            File.WriteAllText(path, "{}");
            void Active(string suite, string chunk, string state = "Active") =>
                QualificationHeartbeat.WriteActiveChunkIdentity(root, suite, chunk, "heartbeat-run", "2", state).Written.ShouldBeTrue();

            foreach (var (suite, seconds) in new[] { ("antiphon", 1800.0), ("session-runner", 60.0), ("antiphon", 1800.0) })
            {
                Active(suite, "all-group-3");
                var legacy = QualificationHeartbeat.PlannedCost(path, "all-group-3");
                legacy.ShouldBe(new QualificationPlannedCost(seconds, ""), "heartbeatCollision " + suite);
                QualificationHeartbeat.PlannedCost(path, "all-group-3", suite, "heartbeat-run", "2").Seconds.ShouldBe(seconds);
                QualificationHeartbeat.IdleBudget(legacy.Seconds).ShouldBe(new QualificationIdleBudget(Math.Max(10, seconds / 60), "plannedSeconds"));
            }
            foreach (var (chunk, seconds) in new[] { ("all-group-3", 1800.0), ("all-group-4", 120.0) })
            {
                Active("antiphon", chunk);
                QualificationHeartbeat.PlannedCost(path, chunk).Seconds.ShouldBe(seconds, "heartbeatLegacy " + chunk);
            }

            Active("antiphon", "all-group-3");
            Refused(QualificationHeartbeat.PlannedCost(path, "all-group-3", "session-runner", "heartbeat-run", "2"), "identity-suite");
            Refused(QualificationHeartbeat.PlannedCost(path, "all-group-3", "antiphon", "wrong-run", "2"), "identity-runId");
            Refused(QualificationHeartbeat.PlannedCost(path, "all-group-3", "antiphon", "heartbeat-run", "9"), "identity-attempt");
            Refused(QualificationHeartbeat.PlannedCost(path, "all-group-3", "", "heartbeat-run", "2"), "identity-suite");
            Refused(QualificationHeartbeat.PlannedCost(path, "All-group-3"), "identity-chunk");
            Refused(QualificationHeartbeat.PlannedCost(Path.Combine(root, "groups-planned-antiphon.json.bak"), "all-group-3"), "supplied-plan-unavailable");
            Active("antiphon", "all-group-3", "Idle");
            Refused(QualificationHeartbeat.PlannedCost(path, "all-group-3"), "active-unavailable");
            Active("antiphon", "all-group-3");
            var sidecar = Path.Combine(root, "antiphon-all-group-3.timing.json");
            File.WriteAllText(sidecar, "{\"lastPhase\":\"complete\"}");
            Refused(QualificationHeartbeat.PlannedCost(path, "all-group-3"), "chunk-settled");
            File.WriteAllText(sidecar, "{\"lastPhase\":\"run\",\"completedAt\":\"2026-11-21T00:00:00Z\"}");
            Refused(QualificationHeartbeat.PlannedCost(path, "all-group-3"), "chunk-settled");
            File.WriteAllText(sidecar, "{\"lastPhase\":\"run\"}");
            QualificationHeartbeat.PlannedCost(path, "all-group-3").Seconds.ShouldBe(1800);
            // The identity changed while it was read (an Idle write between the read and the digest check).
            Refused(QualificationHeartbeat.PlannedCost(path, "all-group-3", null, null, null,
                active => QualificationHeartbeat.WriteActiveChunkIdentity(Path.GetDirectoryName(active)!, "antiphon", "all-group-3", "heartbeat-run", "2", "Idle")), "active-changed");

            // Each digest field absent from the plan, the index row and the identity (the plan digest rebound): unknown.
            Active("antiphon", "all-group-3");
            var planPath = Path.Combine(root, "groups-planned-antiphon.json");
            var indexPath = Path.Combine(root, "groups-planned-index.json");
            var activePath = Path.Combine(root, "active-chunk.json");
            var (planText, indexText, activeText) = (File.ReadAllText(planPath), File.ReadAllText(indexPath), File.ReadAllText(activePath));
            void Mutant(string label, string lookup, Action<JsonNode, JsonNode, JsonNode> change)
            {
                var (p, x, a) = (JsonNode.Parse(planText)!, JsonNode.Parse(indexText)!, JsonNode.Parse(activeText)!);
                change(p, x, a);
                File.WriteAllText(planPath, p.ToJsonString());
                File.WriteAllText(indexPath, x.ToJsonString());
                a["planDigest"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(planPath)));
                File.WriteAllText(activePath, a.ToJsonString());
                QualificationHeartbeat.PlannedCost(path, lookup).Seconds.ShouldBeNull("heartbeatRefusals " + label);
                File.WriteAllText(planPath, planText);
                File.WriteAllText(indexPath, indexText);
                File.WriteAllText(activePath, activeText);
            }
            foreach (var field in new[] { "sourceDigest", "policyDigest", "costFileDigest", "assemblyDigest" })
                Mutant(field, "all-group-3", (p, x, a) => { p.AsObject().Remove(field); a.AsObject().Remove(field); x["suites"]![0]!.AsObject().Remove(field); });
            Mutant("index-version", "all-group-3", (p, x, a) => x["version"] = 2);
            Mutant("plan-version", "all-group-3", (p, x, a) => p["version"] = 2);
            Mutant("chunk-path", "../all-group-3", (p, x, a) => { a["chunk"] = "../all-group-3"; p["groups"]![0]!["chunk"] = "../all-group-3"; });
            Mutant("header-type", "all-group-3", (p, x, a) => { a["attempt"] = 2; x["attempt"] = 2; p["attempt"] = 2; x["suites"]![0]!["attempt"] = 2; });
            // The digest left in place: proven bindings only.
            QualificationHeartbeat.PlannedCost(path, "all-group-3").Seconds.ShouldBe(1800);
            File.WriteAllText(planPath, PlanJson("antiphon", "heartbeat-run", "2", ("all-group-3", 0), ("all-group-4", 120)));
            Active("antiphon", "all-group-3");
            Refused(QualificationHeartbeat.PlannedCost(path, "all-group-3"), "plan-cost");
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public void The_journal_generation_prefix_decides_liveness()
    {
        // HeartbeatLiveness: a timing root with a journal is live only while its current generation is a readable,
        // gap-free prefix of this run's chunk records that has not settled the active chunk.
        var cases = new (string Variant, string? Reason)[]
        {
            ("valid-empty", null), ("valid-prefix", null), ("missing-generation", "journal-generation-unavailable"), ("missing-metadata", "binding-file-unavailable"),
            ("metadata-digest", "journal-metadata-digest"), ("metadata-run", "journal-metadata-identity"), ("metadata-attempt", "journal-metadata-identity"),
            ("metadata-source", "journal-metadata-identity"), ("metadata-version", "journal-metadata-identity"), ("pointer-root", "journal-pointer"),
            ("pointer-version", "journal-pointer"), ("corrupt", ""), ("gap", "journal-gap"), ("truncated", "journal-state-unavailable"), ("settled", "chunk-settled"),
            ("terminal", "journal-terminal"), ("seeded", "journal-liveness-unknown"),
        };
        var root = NewRoot();
        try
        {
            foreach (var (variant, reason) in cases)
            {
                var timing = Path.Combine(root, "liveness-" + variant);
                Directory.CreateDirectory(timing);
                File.WriteAllText(Path.Combine(timing, "groups-planned-antiphon.json"), PlanJson("antiphon", "heartbeat-run", "2", ("all-group-3", 1800)));
                File.WriteAllText(Path.Combine(timing, "groups-planned-index.json"), IndexJson("heartbeat-run", "2", "antiphon"));
                File.WriteAllText(Path.Combine(timing, "groups-planned.json"), "{}");
                QualificationHeartbeat.WriteActiveChunkIdentity(timing, "antiphon", "all-group-3", "heartbeat-run", "2").Written.ShouldBeTrue();
                var context = TestPhasePublication.Open(timing, timing, "heartbeat-run", "2", "", Source, "", false, false, new TestPhasePublicationScope());
                try
                {
                    var journal = Path.Combine(timing, "groups-executed.journal");
                    var pointerPath = Path.Combine(journal, "current.json");
                    var generation = Path.Combine(journal, context.Generation);
                    var metaPath = Path.Combine(generation, "metadata.json");
                    var pointer = JsonNode.Parse(File.ReadAllText(pointerPath))!;
                    var meta = JsonNode.Parse(File.ReadAllText(metaPath))!;
                    if (variant is "valid-prefix" or "gap" or "settled")
                    {
                        var sequence = variant == "gap" ? 2 : 1;
                        var envelope = TestPhaseJournal.EnvelopeText("chunk", sequence, Guid.NewGuid().ToString("n"), context, "", "antiphon",
                            variant == "settled" ? "all-group-3" : "other-chunk", "", "", "heartbeat-run", "2", "antiphon", "{}", "");
                        File.WriteAllText(Path.Combine(generation, sequence.ToString("00000000") + ".json"), envelope);
                    }
                    switch (variant)
                    {
                        case "missing-generation": pointer["generation"] = new string('1', 32); break;
                        case "missing-metadata": File.Delete(metaPath); break;
                        case "metadata-digest": pointer["metadataDigest"] = new string('f', 64); break;
                        case "metadata-run": meta["runId"] = "other-run"; break;
                        case "metadata-attempt": meta["attempt"] = "3"; break;
                        case "metadata-source": meta["source"] = new string('b', 40); break;
                        case "metadata-version": meta["version"] = 2; break;
                        case "pointer-root": pointer["timingRoot"] = Path.Combine(root, "other-root"); break;
                        case "pointer-version": pointer["version"] = 2; break;
                        case "corrupt": File.WriteAllText(Path.Combine(generation, "00000001.json"), "{"); break;
                        case "truncated":
                            File.WriteAllText(Path.Combine(timing, "groups-executed.state.json"), new JsonObject
                            {
                                ["version"] = 1, ["generation"] = context.Generation, ["sequence"] = 1, ["root"] = timing, ["publicationComplete"] = true,
                                ["unresolved"] = false, ["quality"] = new JsonArray(),
                            }.ToJsonString());
                            break;
                        case "terminal": File.WriteAllText(Path.Combine(generation, "seal.json"), "{\"terminal\":\"success\"}"); break;
                        case "seeded": meta["seedDigest"] = new string('c', 64); meta["parentGeneration"] = new string('2', 32); break;
                    }
                    if (variant is "metadata-run" or "metadata-attempt" or "metadata-source" or "metadata-version" or "seeded")
                    {
                        File.WriteAllText(metaPath, meta.ToJsonString());
                        pointer["metadataDigest"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(metaPath)));
                    }
                    File.WriteAllText(pointerPath, pointer.ToJsonString());
                    var cost = QualificationHeartbeat.PlannedCost(Path.Combine(timing, "groups-planned.json"), "all-group-3");
                    if (reason == null) cost.ShouldBe(new QualificationPlannedCost(1800, ""), variant);
                    else
                    {
                        cost.Seconds.ShouldBeNull(variant);
                        if (reason.Length > 0) cost.Reason.ShouldBe(reason, variant);
                        QualificationHeartbeat.IdleBudget(cost.Seconds).Source.ShouldBe("default-unknown-group", variant);
                    }
                }
                finally { TestPhasePublication.Close(context); }
            }
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public void The_monitor_beats_on_its_clock_and_process_table_and_keeps_only_written_beats()
    {
        var root = NewRoot();
        try
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));
            var table = new ScriptedTable();
            var monitor = new QualificationHeartbeatMonitor(clock, table, 15);
            var path = Path.Combine(root, "task", "supervisor-heartbeat.json");
            monitor.Previous.ShouldBeNull();
            for (var minute = 0; minute <= 15; minute++)
            {
                table.Rows = [new(1, 0, 500 + minute * 60), new(40, 1, 3 + minute * 0.01), new(41, 40, 10)];
                var beat = monitor.Beat(path, 40, [1], 0, 1000, "chunk.log");
                monitor.Previous.ShouldBeSameAs(beat);
                if (minute < 15) clock.Advance(TimeSpan.FromMinutes(1));
            }
            var last = QualificationHeartbeat.Read(path);
            last.HangSuspected.ShouldBeTrue();
            last.IdleBudgetSource.ShouldBe("default-unknown-group");
            last.IdleBudgetMinutes.ShouldBe(15);
            last.SuspectPids.ShouldBe(new[] { 40, 41 });
            last.ObservedAtUtc.ShouldBe("2026-11-21T12:15:00.0000000Z");
            last.HostCpu.Select(s => s.Pid).ShouldBe(new[] { 40, 41 });
            File.Exists(path + ".tmp").ShouldBeFalse();

            // A write that fails keeps the previous beat.
            var kept = monitor.Previous;
            var blocked = Path.Combine(root, "blocked");
            Directory.CreateDirectory(blocked);
            Directory.CreateDirectory(Path.Combine(blocked, "supervisor-heartbeat.json"));
            clock.Advance(TimeSpan.FromMinutes(1));
            Should.Throw<Exception>(() => monitor.Beat(Path.Combine(blocked, "supervisor-heartbeat.json"), 40, [1], 0, 1000, "chunk.log"));
            monitor.Previous.ShouldBeSameAs(kept);
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public void The_production_process_table_lists_this_process()
    {
        var rows = new QualificationProcessTable().Snapshot();
        var self = rows.Single(r => r.Pid == Environment.ProcessId);
        self.CpuSeconds.ShouldBeGreaterThan(0);
        self.ParentPid.ShouldBe(Math.Max(0, NightlyProcesses.ParentProcessId(Environment.ProcessId)));
    }

    [Test]
    public void Diagnostic_progress_reads_whole_lines_and_never_the_heartbeat()
    {
        var root = NewRoot();
        try
        {
            var diag = Path.Combine(root, "chunk.diag");
            var line1 = "TestNodeUid { Value = a } PassedTestNodeStateProperty { }\n";
            var line2 = "TestNodeUid { Value = b } InProgressTestNodeStateProperty { }\n";
            File.WriteAllText(diag, line1 + line2 + "noise without a state\n");
            var partial = QualificationHeartbeat.ReadDiagnosticProgress(diag, 0, "", line1.Length + 10);
            (partial.Grew, partial.Offset, partial.LastCompletedUid, partial.InFlightUid, partial.LastPhase).ShouldBe((true, (long)line1.Length, "a", (string?)null, "Passed"));
            var rest = QualificationHeartbeat.ReadDiagnosticProgress(diag, partial.Offset, "");
            (rest.Grew, rest.InFlightUid, rest.LastPhase, rest.LastCompletedUid).ShouldBe((true, "b", "InProgress", (string?)null));
            rest.Offset.ShouldBe(new FileInfo(diag).Length);
            var none = QualificationHeartbeat.ReadDiagnosticProgress(diag, 1 << 20, "");
            (none.Grew, none.Offset).ShouldBe((false, new FileInfo(diag).Length));
            var tiny = QualificationHeartbeat.ReadDiagnosticProgress(diag, 0, "", 4);
            (tiny.Grew, tiny.Offset).ShouldBe((false, 0L), "no whole line in the read: nothing consumed");
            var missing = QualificationHeartbeat.ReadDiagnosticProgress(Path.Combine(root, "absent.diag"), 7, "");
            (missing.Grew, missing.Offset).ShouldBe((false, 7L));
            var self = QualificationHeartbeat.ReadDiagnosticProgress(diag, 0, diag.ToUpperInvariant());
            (self.Grew, self.Offset).ShouldBe((false, 0L), "the heartbeat file is never progress");
        }
        finally { DeleteRoot(root); }
    }

    [Test]
    public void Heartbeat_json_has_the_fixed_shape_and_reads_back()
    {
        var record = new QualificationHeartbeatRecord
        {
            CpuSeconds = 1.0 / 3, Bytes = 12345678901, IdleMinutes = 20.5, Trx = 4, Newest = "a\"b\\c", HangSuspected = true,
            HangReason = "line\nnext\ttab\rret" + (char)1 + (char)0xE9, SuspectPids = [7, 9], IdleBudgetMinutes = 20, IdleBudgetSource = "default-unknown-group",
            IdleSinceUtc = null, ObservedAtUtc = "2026-11-21T12:00:00.0000000Z", HostCpu = [new(7, 1.25), new(9, 0)], LastCompletedUid = " ",
            LastCompletedAtUtc = null, LastDiagGrowthAtUtc = "2026-11-21T11:59:00.0000000Z", InFlightUid = "uid-1", LastPhase = "InProgress",
        };
        var json = QualificationHeartbeat.Json(record);
        json.ShouldBe("{\"cpuSeconds\":0.333333333333333,\"bytes\":12345678901,\"idleMinutes\":20.5,\"trx\":4,\"newest\":\"a\\\"b\\\\c\",\"hangSuspected\":true,"
            + "\"hangReason\":\"line\\nnext\\ttab\\rret\\u0001" + (char)0xE9 + "\",\"suspectPids\":[7,9],\"idleBudgetMinutes\":20,\"idleBudgetSource\":\"default-unknown-group\","
            + "\"idleSinceUtc\":null,\"observedAtUtc\":\"2026-11-21T12:00:00.0000000Z\",\"hostCpu\":[{\"pid\":7,\"cpuSeconds\":1.25},{\"pid\":9,\"cpuSeconds\":0}],"
            + "\"lastCompletedUid\":null,\"lastCompletedAtUtc\":null,\"lastDiagGrowthAtUtc\":\"2026-11-21T11:59:00.0000000Z\",\"inFlightUid\":\"uid-1\",\"lastPhase\":\"InProgress\"}");
        var root = NewRoot();
        try
        {
            var path = Path.Combine(root, "beat.json");
            QualificationHeartbeat.Write(record, path);
            File.ReadAllBytes(path).ShouldBe(new UTF8Encoding(false).GetBytes(json));
            var back = QualificationHeartbeat.Read(path);
            QualificationHeartbeat.Json(back).ShouldBe(json);
        }
        finally { DeleteRoot(root); }
    }

    private static void Refused(QualificationPlannedCost cost, string reason)
    {
        cost.Seconds.ShouldBeNull(reason);
        cost.Reason.ShouldBe(reason);
    }

    private sealed class ScriptedTable : IQualificationProcessTable
    {
        public QualificationProcess[] Rows { get; set; } = [];

        public IReadOnlyList<QualificationProcess> Snapshot() => Rows;
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "s10-heartbeat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
