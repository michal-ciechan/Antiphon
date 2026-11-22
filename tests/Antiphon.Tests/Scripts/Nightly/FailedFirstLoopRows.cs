using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using static Antiphon.Tests.Scripts.Nightly.FailedFirstArmCases;
using static Antiphon.Tests.Scripts.Nightly.FailedFirstLoopCases;
using H = Antiphon.Tests.Scripts.Nightly.FailedFirstHistoryCases;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S14: the rows of the actual-loop cases of scripts/test-nightly-failed-first.ps1 (Test-ActualLoopFollowsSchedule,
/// Test-FFPreviousRunChain, Test-FFHeartbeatBindings with Test-FFHeartbeatLiveness, Test-JournalCustodySurvivesInterleaving),
/// the same conditions over the nightly tests run (NightlyTests.Run on the loop seams since W2-nightly S17; the entry
/// nightly-tests.ps1 is deleted) and the compiled heartbeat (QualificationHeartbeat, W2-nightly S10).
/// </summary>
internal static class FailedFirstLoopRows
{
    private static object Schedule(object[] prepared, object[]? members = null, object? previous = null, string candidateSha = "", DateTime? now = null, string runId = "", string attempt = "") =>
        NightlyDispatch.Schedule(prepared, members ?? [], "", null, true, runId, attempt, previous, candidateSha, now ?? DateTime.UtcNow, "");

    private static string[] Keys(object schedule) => FailedFirstDispatchCases.Keys(schedule);

    private static string[] Canonical(object[] prepared) => prepared.SelectMany(s => H.Items(s, "chunks").Select(c => H.Str(s, "id") + "/" + H.Str(c, "id"))).ToArray();

    // ---- Test-ActualLoopFollowsSchedule -------------------------------------------------------------------------

    public static async Task ActualLoopFollowsSchedule(NightlyFixture fixture, NightlyCaseRows rows, CancellationToken cancellationToken)
    {
        const string c = "FF ActualLoopFollowsSchedule.";
        var root = fixture.Root;
        var fx = Loop(full: true);
        var h = History(root, fx);
        var arm = await Run(root, fx, "actual-full", h.Path, cancellationToken: cancellationToken);
        var resolved = PriorFailureHistory.Resolve(h.History, arm.Prepared, "Full", null, null);
        var expected = Schedule(arm.Prepared, H.Items(resolved, "members"));
        var actual = arm.Trace.Keys;
        var entries = H.Items(expected, "entries");
        rows.Check(actual.SequenceEqual(Keys(expected)) && H.True(entries[0], "priorFailure") && entries.Any(e => !H.True(e, "priorFailure")), c + "actualTrace",
            "launches=" + actual.Length + " output=" + Tail(arm.Output));
        var canonical = Canonical(arm.Prepared);
        var replayNodes = H.Items(fx.Suites[0], "nodes").Length;
        rows.Check(actual.Length == canonical.Length && actual.Distinct(StringComparer.Ordinal).Count() == canonical.Length && NightlyPlanCases.SameSet(actual, canonical) && arm.Trace.Max == 1
            && replayNodes == 19012, c + "permutation");
        rows.Check(arm.Trace.Builds == 1 && arm.Trace.Discovery.Count == fx.Suites.Length && arm.Trace.Discovery.Distinct(StringComparer.Ordinal).Count() == fx.Suites.Length, c + "oneBuildDiscovery");
        rows.Check(S(arm.Schedule, "historyDigest") == PriorFailureHistory.Digest(h.Path) && File.Exists(Path.Combine(arm.Root, "prior-failures.json")), c + "historyBinding");
        rows.Check(arm.TestsPassed && arm.CoverageComplete && arm.ExitCode == 0 && B(arm.Summary, "succeeded"), c + "allChunksVerdict",
            string.Join(",", A(arm.Summary, "reasons").Select(r => r.ToString())));
        var projected = A(arm.Projection, "chunks");
        rows.Check(projected.Length == canonical.Length && !arm.Trace.SettledBeforeReturn && projected.Length > 0 && arm.Trace.Launches.Count > 0
            && S(projected[^1], "chunk") == arm.Trace.Launches[^1].Chunk, c + "finalProjection");
        var bad = Path.Combine(root, "malformed.json");
        File.WriteAllText(bad, "{");
        var fallback = await Run(root, Loop(), "fallback", bad, cancellationToken: cancellationToken);
        rows.Check(fallback.TestsPassed && S(fallback.Schedule, "historyFallbackReason").Length > 0 && fallback.Trace.Keys.SequenceEqual(Entries(fallback.Schedule)), c + "fallback");
        await PreviousRunChain(fixture, rows, cancellationToken);
        HeartbeatBindings(fixture, rows, "ActualLoopFollowsSchedule");
        var io = await Run(root, Loop(), "identity-io", null, "identity-io", cancellationToken: cancellationToken);
        rows.Check(io.ExitCode == 0 && io.TestsPassed && io.CoverageComplete && io.Trace.Launches.Count > 0 && !io.Trace.Launches.Any(l => l.LegacyCost != null || l.QualifiedCost != null),
            c + "heartbeatIo");
    }

    // ---- Test-FFPreviousRunChain --------------------------------------------------------------------------------

    // Connect the real producer, export/ingress and coordinator in one small fixture. Expected launch traces are
    // independent of the scheduler under test.
    public static async Task PreviousRunChain(NightlyFixture fixture, NightlyCaseRows rows, CancellationToken cancellationToken)
    {
        const string c = "FF ActualLoopFollowsSchedule.";
        var root = fixture.Root;
        var fx = Loop();
        var first = await Run(root, fx, "chain-first", null, "failure", noExclusions: true, cancellationToken: cancellationToken);
        var historyPath = Path.Combine(root, "chain-history.json");
        var export = PriorFailureHistory.Export(first.Root, historyPath);
        var exportHistory = PsValue.Prop(export, "History");
        var failed = A(first.Summary, "suites").Where(s => S(s, "result") == "FAIL").ToArray();
        var resolved = PriorFailureHistory.Resolve(exportHistory, first.Prepared, "Full", null, null);
        var members = H.Items(exportHistory, "members");
        var resolvedMembers = H.Items(resolved, "members");
        rows.Check(first.ExitCode != 0 && failed.Length == 1 && S(failed[0], "id") == "antiphon" && S(failed[0], "chunk") == "Priority"
            && S(first.Summary.GetProperty("dispatchProgress"), "canonicalPlanDigest") == S(first.Schedule, "canonicalPlanDigest") && members.Length == 1
            && H.Items(members[0], "observations").Count(o => H.Str(o, "kind") == "trx" && H.Str(o, "state") == "Failed" && H.Str(o, "chunk") == "Priority") == 1
            && resolvedMembers.Length == 1 && H.Str(resolvedMembers[0], "suite") == "antiphon" && string.Join(",", H.Items(resolvedMembers[0], "chunks").Select(PsValue.Str)) == "Priority",
            c + "chainProducer");
        var second = await Run(root, fx, "chain-second", historyPath, noExclusions: true, cancellationToken: cancellationToken);
        var trace = second.Trace.Keys;
        string[] wanted = ["antiphon/Priority", "antiphon/Ordinary", "session-runner/Ordinary", "session-runner/Priority"];
        rows.Check(S(second.Schedule, "orderingReason") == "previous-run-order" && S(second.Schedule, "canonicalPlanDigest") == S(first.Schedule, "canonicalPlanDigest")
            && trace.SequenceEqual(wanted) && trace.Distinct().Count() == 4 && second.TestsPassed && second.CoverageComplete && second.ExitCode == 0, c + "chainPreviousOrder",
            S(second.Schedule, "orderingReason") + " " + string.Join(",", trace));
        // Preserve every key but swap membership: the roster guard cannot detect it.
        var regrouped = Loop();
        var regroupedChunks = H.Items(regrouped.Suites[0], "chunks");
        var classes = PsValue.Prop(regroupedChunks[0], "classes");
        ((PlainObject)regroupedChunks[0])["classes"] = PsValue.Prop(regroupedChunks[1], "classes");
        ((PlainObject)regroupedChunks[1])["classes"] = classes;
        var third = await Run(root, regrouped, "chain-regrouped", historyPath, noExclusions: true, cancellationToken: cancellationToken);
        trace = third.Trace.Keys;
        string[] canonicalOrder = ["antiphon/Ordinary", "antiphon/Priority", "session-runner/Ordinary", "session-runner/Priority"];
        rows.Check(S(third.Schedule, "orderingReason") == "previous-run-plan-mismatch" && S(third.Schedule, "canonicalPlanDigest") != S(first.Schedule, "canonicalPlanDigest")
            && trace.SequenceEqual(canonicalOrder) && trace.Distinct().Count() == 4 && third.TestsPassed && third.CoverageComplete && third.ExitCode == 0, c + "chainRegrouped",
            S(third.Schedule, "orderingReason") + " " + string.Join(",", trace));
        // Otherwise-valid same-candidate record with the digest removed. Original order, stated reason.
        var digestCopy = Path.Combine(root, "chain-no-digest-root");
        CopyTree(first.Root, digestCopy);
        var digestSummaryPath = Path.Combine(digestCopy, "summary.json");
        var digestSummary = JsonNode.Parse(File.ReadAllText(digestSummaryPath))!;
        digestSummary["dispatchProgress"]!.AsObject().Remove("canonicalPlanDigest");
        File.WriteAllText(digestSummaryPath, digestSummary.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        var digestHistory = Path.Combine(root, "chain-history-no-digest.json");
        PriorFailureHistory.Export(digestCopy, digestHistory);
        var fourth = await Run(root, fx, "chain-no-digest", digestHistory, noExclusions: true, cancellationToken: cancellationToken);
        trace = fourth.Trace.Keys;
        rows.Check(S(fourth.Schedule, "orderingReason") == "previous-run-plan-digest-missing" && trace.SequenceEqual(first.Trace.Keys) && trace.Distinct().Count() == 4
            && fourth.TestsPassed && fourth.CoverageComplete && fourth.ExitCode == 0, c + "chainDigestMissing", S(fourth.Schedule, "orderingReason") + " " + string.Join(",", trace));
        var plain = Schedule(first.Prepared, runId: "ingress", attempt: "1");
        var plainKeys = Keys(plain);
        var baseRecord = (PlainObject)H.Copy(PriorFailureHistory.SummaryDispatchProgress(first.SummaryValue))!;
        baseRecord["liveness"] = "closed";
        baseRecord["schemaVersion"] = 1;
        baseRecord["canonicalPlanDigest"] = H.Str(plain, "canonicalPlanDigest");
        DateTimeOffset completed;
        var completedAt = PsValue.Prop(baseRecord, "completedAt");
        if (completedAt is DateTime date) completed = new DateTimeOffset(date);
        else if (!DateTimeOffset.TryParse(PsValue.Str(completedAt), CultureInfo.InvariantCulture, DateTimeStyles.None, out completed)) completed = DateTimeOffset.UtcNow;
        var nowUtc = completed.UtcDateTime.AddHours(1);
        var ingress = true;
        var detail = new StringBuilder();
        var validBefore = H.JsonText(baseRecord);
        var valid = Schedule(first.Prepared, previous: baseRecord, candidateSha: new string('a', 40), now: nowUtc, runId: "ingress", attempt: "1");
        var validOrder = Keys(valid);
        if (H.Str(valid, "orderingReason") != "previous-run-order" || validOrder.SequenceEqual(plainKeys)
            || string.Join(",", validOrder) != "antiphon/Priority,antiphon/Ordinary,session-runner/Ordinary,session-runner/Priority" || H.JsonText(baseRecord) != validBefore) ingress = false;
        detail.Append("valid reason=").Append(H.Str(valid, "orderingReason")).Append(" order=").Append(string.Join(",", validOrder)).Append("; ");
        foreach (var name in new[] { "absent", "null", "empty", "whitespace", "short", "long", "nonhex", "candidate", "candidate-blank", "candidate-short", "liveness", "liveness-blank", "schema", "schema-blank", "schema-foreign" })
        {
            var record = (PlainObject)H.Copy(baseRecord)!;
            var reason = "previous-run-plan-digest-missing";
            switch (name)
            {
                case "absent": record.Remove("canonicalPlanDigest"); break;
                case "null": record["canonicalPlanDigest"] = null; break;
                case "empty": record["canonicalPlanDigest"] = ""; break;
                case "whitespace": record["canonicalPlanDigest"] = "   "; break;
                case "short": record["canonicalPlanDigest"] = new string('a', 63); reason = "previous-run-plan-digest-malformed"; break;
                case "long": record["canonicalPlanDigest"] = new string('a', 65); reason = "previous-run-plan-digest-malformed"; break;
                case "nonhex": record["canonicalPlanDigest"] = new string('g', 64); reason = "previous-run-plan-digest-malformed"; break;
                case "candidate": record.Remove("sha"); reason = "previous-run-candidate-missing"; break;
                case "candidate-blank": record["sha"] = ""; reason = "previous-run-candidate-missing"; break;
                case "candidate-short": record["sha"] = "abc"; reason = "previous-run-schema"; break;
                case "liveness": record.Remove("liveness"); reason = "previous-run-liveness-missing"; break;
                case "liveness-blank": record["liveness"] = ""; reason = "previous-run-liveness-missing"; break;
                case "schema": record.Remove("schemaVersion"); reason = "previous-run-schema-missing"; break;
                case "schema-blank": record["schemaVersion"] = ""; reason = "previous-run-schema-missing"; break;
                case "schema-foreign": record["schemaVersion"] = 2; reason = "previous-run-schema"; break;
            }
            var before = H.JsonText(record);
            var schedule = Schedule(first.Prepared, previous: record, candidateSha: new string('a', 40), now: nowUtc, runId: "ingress", attempt: "1");
            var order = Keys(schedule);
            var match = H.Str(schedule, "orderingReason") == reason && order.SequenceEqual(plainKeys) && order.Length == 4 && order.Distinct().Count() == 4 && H.JsonText(record) == before;
            detail.Append(name).Append(" expected=").Append(reason).Append(" actual=").Append(H.Str(schedule, "orderingReason")).Append(" original=").Append(match).Append("; ");
            if (!match) ingress = false;
        }
        rows.Check(ingress, c + "chainIngress", detail.ToString());
    }

    // ---- Test-FFHeartbeatBindings and Test-FFHeartbeatLiveness --------------------------------------------------

    public static void HeartbeatBindings(NightlyFixture fixture, NightlyCaseRows rows, string caseName)
    {
        var c = "FF " + caseName + ".";
        var root = fixture.Root;
        var dir = Path.Combine(root, "heartbeat");
        Directory.CreateDirectory(dir);
        var scope = new TestPhasePublicationScope("", "Core", "7.5.0", null, null);
        foreach (var sid in new[] { "antiphon", "session-runner" })
        {
            var seconds = sid == "session-runner" ? 60 : 1800;
            object[] groups =
            [
                new PlainObject(("id", "all-group-3"), ("classes", new object[] { "Fixture.Cost" }), ("seconds", seconds)),
                new PlainObject(("id", "all-group-4"), ("classes", new object[] { "Fixture.Short" }), ("seconds", 120)),
            ];
            WritePlanned(new TestPhasePlannedRequest
            {
                LogRoot = dir, RunId = "heartbeat-run", Attempt = "2", Suite = sid, SourceDigest = new string('a', 40), AssemblyDigest = new string('d', 64), PolicyDigest = new string('b', 64),
                CostFileDigest = new string('c', 64), AssemblyId = sid, Groups = groups, DiscoveryNodes = Array.Empty<object>(),
            }, scope);
        }
        var timing = Path.Combine(dir, "timing");
        var path = Path.Combine(timing, "groups-planned.json");
        var collision = true;
        foreach (var sid in new[] { "antiphon", "session-runner", "antiphon" })
        {
            Identity(timing, sid, "all-group-3", "heartbeat-run", "2");
            var expected = sid == "session-runner" ? 60.0 : 1800.0;
            var legacy = Cost(path, "all-group-3");
            var qualified = Cost(path, "all-group-3", sid, "heartbeat-run", "2");
            if (legacy != expected || qualified != expected || QualificationHeartbeat.IdleBudget(legacy).Minutes != Math.Max(10, expected / 60)) collision = false;
        }
        rows.Check(collision, c + "heartbeatCollision");
        var legacyOk = true;
        foreach (var (chunk, seconds) in new[] { ("all-group-3", 1800.0), ("all-group-4", 120.0) })
        {
            Identity(timing, "antiphon", chunk, "heartbeat-run", "2");
            if (Cost(path, chunk) != seconds) legacyOk = false;
        }
        rows.Check(legacyOk, c + "heartbeatLegacy");
        Identity(timing, "antiphon", "all-group-3", "heartbeat-run", "2");
        var bad = Cost(path, "all-group-3", "session-runner", "heartbeat-run", "2") == null && Cost(path, "all-group-3", "antiphon", "wrong-run", "2") == null
            && Cost(path, "all-group-3", "antiphon", "heartbeat-run", "9") == null && Cost(path, "All-group-3") == null;
        Identity(timing, "antiphon", "all-group-3", "heartbeat-run", "2", "Idle");
        bad = bad && Cost(path, "all-group-3") == null;
        Identity(timing, "antiphon", "all-group-3", "heartbeat-run", "2");
        var sidecar = Path.Combine(timing, "antiphon-all-group-3.timing.json");
        H.WriteJson(sidecar, new OrderedDictionary { { "lastPhase", "complete" } });
        bad = bad && Cost(path, "all-group-3") == null;
        H.WriteJson(sidecar, new OrderedDictionary { { "lastPhase", "run" } });
        // The identity changes while it is read (an Idle write between the read and the digest check).
        bad = bad && Cost(path, "all-group-3", identityRead: active => Identity(Path.GetDirectoryName(active)!, "antiphon", "all-group-3", "heartbeat-run", "2", "Idle")) == null;
        // Rebind the changed plan hash so each absent field is the discriminating defect, rather than a stale digest
        // accidentally rejecting the fixture.
        Identity(timing, "antiphon", "all-group-3", "heartbeat-run", "2");
        var planPath = Path.Combine(timing, "groups-planned-antiphon.json");
        var indexPath = Path.Combine(timing, "groups-planned-index.json");
        var activePath = Path.Combine(timing, "active-chunk.json");
        var planText = File.ReadAllText(planPath);
        var indexText = File.ReadAllText(indexPath);
        var activeText = File.ReadAllText(activePath);
        static JsonObject Obj(string text) => JsonNode.Parse(text)!.AsObject();
        static JsonObject AntiphonRow(JsonObject index) => index["suites"]!.AsArray().First(s => (string?)s!["suite"] == "antiphon")!.AsObject();
        void Write(string p, JsonNode node) => File.WriteAllText(p, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        void Restore() { File.WriteAllText(planPath, planText); File.WriteAllText(indexPath, indexText); File.WriteAllText(activePath, activeText); }
        foreach (var field in new[] { "sourceDigest", "policyDigest", "costFileDigest", "assemblyDigest" })
        {
            var plan = Obj(planText); var index = Obj(indexText); var active = Obj(activeText);
            plan.Remove(field); active.Remove(field); AntiphonRow(index).Remove(field);
            Write(planPath, plan); Write(indexPath, index);
            active["planDigest"] = PriorFailureHistory.Digest(planPath); Write(activePath, active);
            if (Cost(path, "all-group-3") != null) bad = false;
            Restore();
        }
        foreach (var variant in new[] { "index-version", "plan-version", "chunk-path", "header-type" })
        {
            var plan = Obj(planText); var index = Obj(indexText); var active = Obj(activeText);
            var lookup = "all-group-3";
            switch (variant)
            {
                case "index-version": index["version"] = 2; break;
                case "plan-version": plan["version"] = 2; break;
                case "chunk-path": lookup = "../all-group-3"; active["chunk"] = lookup; plan["groups"]![0]!["chunk"] = lookup; break;
                case "header-type": active["attempt"] = 2; index["attempt"] = 2; plan["attempt"] = 2; AntiphonRow(index)["attempt"] = 2; break;
            }
            Write(planPath, plan); Write(indexPath, index);
            active["planDigest"] = PriorFailureHistory.Digest(planPath); Write(activePath, active);
            if (Cost(path, lookup) != null) bad = false;
            Restore();
        }
        rows.Check(bad, c + "heartbeatRefusals");
        Liveness(root, timing, rows, c);
    }

    private static void Liveness(string root, string bindingDirectory, NightlyCaseRows rows, string c)
    {
        var ok = true;
        var detail = new StringBuilder();
        var scope = new TestPhasePublicationScope("", "Core", "7.5.0", null, null);
        foreach (var variant in new[] { "valid-empty", "valid-prefix", "missing-generation", "missing-metadata", "metadata-digest", "metadata-run", "metadata-attempt", "metadata-source",
            "metadata-version", "pointer-root", "pointer-version", "corrupt", "gap", "truncated", "settled", "terminal", "seeded" })
        {
            var timing = Path.Combine(root, "liveness-" + variant);
            Directory.CreateDirectory(timing);
            foreach (var file in new[] { "groups-planned.json", "groups-planned-index.json", "groups-planned-antiphon.json", "active-chunk.json" })
                File.Copy(Path.Combine(bindingDirectory, file), Path.Combine(timing, file), false);
            var context = TestPhasePublication.Open(timing, timing, "heartbeat-run", "2", "", new string('a', 40), "", false, false, scope);
            try
            {
                var journal = Path.Combine(timing, "groups-executed.journal");
                var pointerPath = Path.Combine(journal, "current.json");
                var generation = Path.Combine(journal, context.Generation);
                var metaPath = Path.Combine(generation, "metadata.json");
                var pointer = JsonNode.Parse(File.ReadAllText(pointerPath))!.AsObject();
                var meta = JsonNode.Parse(File.ReadAllText(metaPath))!.AsObject();
                var recordChunk = variant == "settled" ? "all-group-3" : "other-chunk";
                var sequence = variant == "gap" ? 2 : 1;
                if (variant is "valid-prefix" or "gap" or "settled")
                {
                    var envelope = TestPhaseJournal.EnvelopeText("chunk", sequence, Guid.NewGuid().ToString("n"), context, "", "antiphon", recordChunk, "", "", "heartbeat-run", "2", "antiphon", "{}", "");
                    File.WriteAllText(Path.Combine(generation, sequence.ToString("00000000", CultureInfo.InvariantCulture) + ".json"), envelope);
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
                        H.WriteJson(Path.Combine(timing, "groups-executed.state.json"), new OrderedDictionary
                        {
                            { "version", 1 }, { "generation", context.Generation }, { "sequence", 1 }, { "root", timing }, { "publicationComplete", true }, { "unresolved", false },
                            { "quality", Array.Empty<object>() },
                        });
                        break;
                    case "terminal": H.WriteJson(Path.Combine(generation, "seal.json"), new OrderedDictionary { { "terminal", "success" } }); break;
                    case "seeded": meta["seedDigest"] = new string('c', 64); meta["parentGeneration"] = new string('2', 32); break;
                }
                if (variant is "metadata-run" or "metadata-attempt" or "metadata-source" or "metadata-version" or "seeded")
                {
                    File.WriteAllText(metaPath, meta.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                    pointer["metadataDigest"] = PriorFailureHistory.Digest(metaPath);
                }
                File.WriteAllText(pointerPath, pointer.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                var cost = Cost(Path.Combine(timing, "groups-planned.json"), "all-group-3");
                var expected = "unknown";
                var matches = cost == null;
                if (variant is "valid-empty" or "valid-prefix") { expected = "1800"; matches = cost == 1800; }
                else if (QualificationHeartbeat.IdleBudget(cost).Source != "default-unknown-group") matches = false;
                detail.Append("LIVENESS ").Append(variant).Append(" expected=").Append(expected).Append(" actual=")
                    .Append(cost == null ? "unknown" : cost.Value.ToString(CultureInfo.InvariantCulture)).Append("; ");
                if (!matches) ok = false;
            }
            finally { TestPhasePublication.Close(context); }
        }
        // Unknown is not death evidence and cannot classify missing rows as unfinished.
        object[] entries = [new PlainObject(("suite", "A"), ("chunk", "1")), new PlainObject(("suite", "A"), ("chunk", "2"))];
        var previous = new PlainObject(("sha", new string('a', 40)), ("completedAt", "2026-10-31T01:00:00Z"), ("liveness", "unknown"),
            ("suites", new object[] { new PlainObject(("id", "A"), ("chunk", "2"), ("result", "FAIL"), ("exitCode", 1)) }));
        var before = H.JsonText(previous);
        var order = DispatchSchedule.PreviousRunOrder(entries, previous, new string('a', 40), new DateTime(2026, 10, 31, 6, 0, 0, DateTimeKind.Utc));
        var orderOk = H.Str(order, "reason") == "previous-run-liveness-unknown" && string.Join(",", H.Items(order, "entries").Select(e => H.Str(e, "chunk"))) == "1,2"
            && H.Items(order, "lanes").Length == 0 && H.JsonText(previous) == before;
        detail.Append("history-unknown original-order=").Append(orderOk).Append(" reason=").Append(H.Str(order, "reason")).Append("; ");
        var history = History(root, Loop());
        var sourceRoot = H.Str(history.History, "sourceRoot");
        var summaryPath = Path.Combine(sourceRoot, "summary.json");
        var summary = JsonNode.Parse(File.ReadAllText(summaryPath))!.AsObject();
        summary["dispatchProgress"] = JsonNode.Parse(H.JsonText(previous));
        File.WriteAllText(summaryPath, summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        var unknownPath = Path.Combine(root, "unknown-liveness-history.json");
        PriorFailureHistory.Export(sourceRoot, unknownPath);
        var input = PriorFailureHistory.InitializeDispatch(unknownPath, "", "", new string('a', 40), false, "", DateTime.UtcNow);
        var inputOk = PsValue.Prop(input, "History") == null && PsValue.Prop(input, "PreviousRun") == null && H.Str(input, "FallbackReason") == "history-liveness-unknown";
        detail.Append("history-ingress original-order=").Append(inputOk).Append(" reason=").Append(H.Str(input, "FallbackReason"));
        rows.Check(ok && orderOk && inputOk, c + "heartbeatLiveness", detail.ToString());
    }

    // ---- Test-JournalCustodySurvivesInterleaving ----------------------------------------------------------------

    public static async Task JournalCustody(NightlyFixture fixture, NightlyCaseRows rows, CancellationToken cancellationToken)
    {
        const string c = "FF JournalCustodySurvivesInterleaving.";
        var root = fixture.Root;
        var fx = Loop();
        var h = History(root, fx);
        var a = await Run(root, fx, "journal", h.Path, "failure", noExclusions: true, cancellationToken: cancellationToken);
        var keys = a.Trace.Keys;
        rows.Check(string.Join(",", keys) == "antiphon/Priority,session-runner/Priority,antiphon/Ordinary,session-runner/Ordinary", c + "interleaving", string.Join(",", keys) + " " + Tail(a.Output));
        rows.Check(a.Trace.Discovery.Count == 2 && !a.Trace.Launches.Any(l => l.Nodes.Length != 1
            || !l.Host.Contains(l.Suite == "antiphon" ? "Antiphon.Tests" : "Antiphon.SessionRunner.Tests", StringComparison.OrdinalIgnoreCase)), c + "hostDiscovery");
        var prefix = true;
        var seen = false;
        foreach (var row in A(a.Projection, "chunks"))
        {
            if (B(row, "testsPassed") != !seen) prefix = false;
            if (S(row, "suite") == "antiphon" && S(row, "chunk") == "Priority") seen = true;
        }
        rows.Check(prefix && !a.TestsPassed && a.ExitCode == 1 && !B(a.Summary, "succeeded"), c + "prefixState");
        var journal = Path.Combine(a.Root, "timing", "groups-executed.journal");
        var pointer = ReadJson(Path.Combine(journal, "current.json"));
        var generation = Path.Combine(journal, S(pointer, "generation"));
        var records = Records(generation);
        var projected = A(a.Projection, "chunks").Select(r => S(r, "suite") + "/" + S(r, "chunk")).ToArray();
        rows.Check(records.Length == 4 && projected.SequenceEqual(keys) && S(a.Projection, "runId") == "actual-run" && S(a.Projection, "attempt") == "1", c + "journalBindings");
        rows.Check(File.Exists(Path.Combine(generation, "seal.json")) && !a.Trace.SettledBeforeReturn && Directory.GetDirectories(journal).Length == 1, c + "oneContextClose");
        var timeout = await Run(root, fx, "timeout-priority", h.Path, "timeout", cancellationToken: cancellationToken);
        var plain = await Run(root, fx, "timeout-canonical", null, "timeout", cancellationToken: cancellationToken);
        rows.Check(S(timeout.Summary, "outcome") == "TIMEOUT" && S(plain.Summary, "outcome") == "TIMEOUT" && !B(timeout.Summary, "succeeded") && !B(plain.Summary, "succeeded"), c + "timeoutPermutation");
        // Existing M1/M2 accessor must refuse corruption instead of manufacturing completion.
        var bytes = File.ReadAllText(records[0]);
        File.WriteAllText(records[0], "{");
        var read = TestPhasePublication.ReadExecutedPrefix(a.Root, Path.Combine(a.Root, "timing"), "");
        rows.Check(read.Refused || Unresolved(read) || !read.PublicationComplete, c + "custody");
        File.WriteAllText(records[0], bytes);
        var summaryKeys = A(a.Summary, "suites").Select(s => S(s, "id") + "/" + S(s, "chunk")).ToArray();
        rows.Check(S(a.Projection, "suite") == a.Trace.Launches[0].Suite && summaryKeys.SequenceEqual(keys) && A(a.Summary, "suiteTimeline").Length == 2
            && File.Exists(Path.Combine(a.Root, "timing", "summary.json")), c + "fieldPolicies");
        var identities = true;
        foreach (var arm in new[] { a, plain })
        {
            foreach (var launch in arm.Trace.Launches)
            {
                var identity = launch.Identity;
                if (identity == null || S(identity.Value, "suite") != launch.Suite || S(identity.Value, "chunk") != launch.Chunk || S(identity.Value, "runId") != "actual-run"
                    || S(identity.Value, "attempt") != "1" || S(identity.Value, "state") != "Active") identities = false;
            }
        }
        var idle = QualificationHeartbeat.ReadActiveChunkIdentity(Path.Combine(a.Root, "timing"));
        var prices = a.Trace.Launches.Concat(plain.Trace.Launches).All(l => l.ProvenBinding
            ? l.LegacyCost == l.PlannedCost && l.QualifiedCost == l.PlannedCost && l.LegacyCost != null
            : l.LegacyCost == null && l.QualifiedCost == null);
        rows.Check(identities && idle?.State == "Idle" && prices, c + "activeIdentity");
    }

    // ReadExecutedPrefix's unresolved flag, when the result carries one.
    private static bool Unresolved(object read) => PsValue.IsTrue(PsValue.Prop(read, "Unresolved"));

    // The active-chunk identity writer and the planned cost of the compiled heartbeat (W2-nightly S10); suite, run and
    // attempt are bound only when given.
    private static bool Identity(string directory, string suite, string chunk, string run, string attempt, string state = "Active") =>
        QualificationHeartbeat.WriteActiveChunkIdentity(directory, suite, chunk, run, attempt, state).Written;

    private static double? Cost(string path, string chunk, string? suite = null, string? run = null, string? attempt = null, Action<string>? identityRead = null) =>
        QualificationHeartbeat.PlannedCost(path, chunk, suite, run, attempt, identityRead).Seconds;

    private static string Tail(string output) => output.Length <= 1500 ? output : output[^1500..];
}
