using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1339 and CARD-1733: the publisher's and promoter's real-git tag allocation, exact remote reads, authority and
// reservation matrices. Unit 14 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md replaced
// scripts/publish-release.ps1 and scripts/promote-release.ps1 with ReleasePublish and ReleasePromote
// (scripts/lib/Antiphon.Nightly), so these cases moved from scripts/test-release-gate.ps1 in process with the same row names
// and counts. They drive the production publisher and promoter against a real local bare remote through
// ReleaseGateGitFixture, the C# twin of New-C1339FixtureSeed and of the confined transport adapter
// scripts/fixtures/c1339-release-git.ps1. C1339_ExactRemoteRefs keeps only the candidate coordinator's rows (G-7..G-9,
// release-candidate.ps1) and the PowerShell adapter's own guards (G-64, G-65) in the driver, for another unit; its
// publisher and promoter rows (G-10..G-17) are C1339_ExactRemoteRefs_publisher_and_promoter. The remote-ref parser
// (G-1..G-6) and the proposal over the journal and the inventory (G-22..G-24, G-27, G-28, G-67, G-68, G-70) run in process
// since unit 12 (ReleaseGateLibraryTests).
//
// Deliberate deviations (unit 14 decisions):
// 1. The release authority no longer pins promote-release.ps1 (deleted); the promoter's C# is pinned by the
//    lib/Antiphon.Nightly source-tree digest. G-38's shape check is now "scriptHashes holds exactly the keys of
//    ReleaseAuthority.Scripts and ReleaseAuthority.SourceTrees, and its lib/Antiphon.Nightly digest is
//    ReleaseAuthority.ScriptHashes(scripts)[lib/Antiphon.Nightly]", and every G-38..G-40 variant that changed or removed
//    promote-release.ps1 changes or removes lib/Antiphon.Nightly and expects script-digest-mismatch:lib/Antiphon.Nightly.
//    The row names stay.
// 2. G-42 'explicit override': the core no longer reads ANTIPHON_API; the CLI passes it in through
//    ReleasePromoteOptions.Parse(arguments, scriptsRoot, variables). The check parses with --antiphon-api-url (the flag
//    wins) and without it (the variable is taken) while ANTIPHON_API names the fallback fixture, whose card is at
//    revision 5, and a -WhatIf promotion with the explicit URL passes. The two refusal variants are unchanged.
// 3. A refusal raised inside the C# (NightlyRefusal) is a clean exit-3 refusal with its message, where PowerShell turned
//    some into thrown errors; the 'threw:' wrappers stay as defensive code.
// 4. Seeds are not shared between cases: each case builds its own fixture (no seed/clone), and the C1733 seed/clone
//    timing is dropped.
[Category("Slow")]
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseTagAllocatorTests
{
    private const string NightlyTree = "lib/Antiphon.Nightly";
    private static readonly string Zeros = new('0', 64);
    private static readonly DateTime Day = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Cut = new(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Regex Unsafe = new(@"--force|push[^\r\n]* \+|tag -d|push[^\r\n]*--delete", RegexOptions.IgnoreCase);

    // @($x).Count of a JSON value: an array's length, a single value 1, nothing 0.
    private static int Count(JsonNode? list) => list is JsonArray array ? array.Count : list is null ? 0 : 1;

    // $x[i] of a JSON value: an array's entry, a single value as its own [0].
    private static JsonNode? At(JsonNode? list, int index) =>
        list is JsonArray array ? (index < array.Count ? array[index] : null) : index == 0 ? list : null;

    // The publication journal's reservations (null when there is no journal).
    private static JsonNode? Reservations(string path) => File.Exists(path) ? ReleaseGateFixture.ReadObject(path)["reservations"] : null;

    private static string Str(JsonNode? node) => ReleaseGateFixture.Str(node);

    private static (string Label, string Reason, Action[] Edits) Variant(string label, string reason, params Action[] edits) => (label, reason, edits);

    // ---- the reservation matrices (G-43..G-53) -------------------------------------------------------------------

    [Test]
    public void C1339_PromoteFreshReservations()
    {
        var rows = new NightlyCaseRows();
        PromotionReservations(rows, resume: false);
        rows.ShouldAllPass(10,
            "C1339 G-43 reservation-journal",
            "C1339 G-44 reservation-unique",
            "C1339 G-45 reservation-typed",
            "C1339 G-46 reservation-candidate",
            "C1339 G-47 reservation-sha",
            "C1339 G-48 reservation-tag",
            "C1339 G-49 reservation-published",
            "C1339 G-50 reservation-release-id",
            "C1339 G-51 reservation-release-url",
            "C1339 G-52 reservation-final-consistent");
    }

    [Test]
    public void C1339_PromoteResumedReservations()
    {
        var rows = new NightlyCaseRows();
        PromotionReservations(rows, resume: true);
        rows.ShouldAllPass(1, "C1339 G-53 reservation-resume");
    }

    // Invoke-C1339PromotionReservations: every malformed or foreign reservation refuses before any write, fresh (one row per
    // variant) or with the final tag already on the remote (all of them in G-53).
    private static void PromotionReservations(NightlyCaseRows rows, bool resume)
    {
        using var happy = ReleaseGateGitFixture.CreatePromotion();
        var result = happy.Promote();

        using var fx = ReleaseGateGitFixture.CreatePromotion();
        var path = fx.PublicationsPath;
        void Update(Action<JsonObject> mutate) => ReleaseGateFixture.UpdateJson(path, mutate);
        void Row0(Action<JsonObject> mutate) => Update(j => mutate(j["reservations"]![0]!.AsObject()));
        var variants = new[]
        {
            Variant("C1339 G-43 reservation-journal", "missing",
                () => File.Delete(path),
                () => Update(j => j["reservations"] = new JsonArray())),
            Variant("C1339 G-44 reservation-unique", "ambiguous",
                () => Update(j => j["reservations"] = new JsonArray(j["reservations"]![0]!.DeepClone(), j["reservations"]![0]!.DeepClone()))),
            Variant("C1339 G-45 reservation-typed", "invalid",
                () => File.WriteAllText(path, "{broken"),
                () => File.WriteAllText(path, "[]"),
                () =>
                {
                    var raw = File.ReadAllText(path);
                    File.WriteAllText(path, "[" + raw + "]");
                },
                () => Update(j => j["schemaVersion"] = "1"),
                () => Update(j => j["schemaVersion"] = 2),
                () => Update(j => j["reservations"] = j["reservations"]![0]!.DeepClone()),
                () => Update(j => j["reservations"] = new JsonArray(JsonValue.Create("row"))),
                () => Row0(r => r["candidateId"] = ReleaseGateFixture.Wrapped(r["candidateId"])),
                () => Row0(r => r["sha"] = ReleaseGateFixture.Wrapped(r["sha"])),
                () => Row0(r => r["tag"] = ReleaseGateFixture.Wrapped(r["tag"])),
                () => Row0(r => r["published"] = "true"),
                () => Row0(r => r["releaseId"] = ReleaseGateFixture.Wrapped(JsonValue.Create("fixture-pre"))),
                () => Row0(r => r["releaseUrl"] = ReleaseGateFixture.Wrapped(JsonValue.Create("https://example.invalid/pre"))),
                () => Row0(r => r["finalTag"] = ReleaseGateFixture.Wrapped(JsonValue.Create("v2026.10.15.1")))),
            Variant("C1339 G-46 reservation-candidate", "candidate-mismatch",
                () => Row0(r => r["candidateId"] = "foreign-candidate")),
            Variant("C1339 G-47 reservation-sha", "sha-mismatch",
                () => Row0(r => r["sha"] = fx.B),
                () => Row0(r => r["sha"] = fx.Head[..12])),
            Variant("C1339 G-48 reservation-tag", "tag-mismatch",
                () => Row0(r => r["tag"] = "v2026.10.15.2-rc.1"),
                () => Row0(r => r["tag"] = fx.PreTag.ToUpperInvariant())),
            Variant("C1339 G-49 reservation-published", "unpublished",
                () => Row0(r => r["published"] = false)),
            Variant("C1339 G-50 reservation-release-id", "release-identity",
                () => Row0(r => r["releaseId"] = "")),
            Variant("C1339 G-51 reservation-release-url", "release-identity",
                () => Row0(r => r["releaseUrl"] = "")),
            Variant("C1339 G-52 reservation-final-consistent", "final-conflict",
                () => Row0(r => r["finalTag"] = "v2026.10.15.2"),
                () => Row0(r => r["finalTag"] = fx.PreTag)),
        };

        if (resume) fx.AddTag(fx.FinalTag, fx.Head);
        var valid = fx.Promote(o => o.WhatIf = true);
        var snap = fx.Fx.Save();
        var resumedChecks = new List<ReleaseGateCheck> { new(valid.ExitCode == 0, "valid control:" + valid.Refusal) };
        foreach (var (label, reason, edits) in variants)
        {
            var checks = new List<ReleaseGateCheck> { new(valid.ExitCode == 0 && result.ExitCode == 0, "valid controls") };
            foreach (var edit in edits) checks.Add(fx.ReservationVariant(snap, edit, "promotion-reservation-" + reason));
            if (label == "C1339 G-52 reservation-final-consistent")
            {
                var before = File.ReadAllText(path);
                var wrongFinal = ReleaseGateJournal.Production().RecordFinalTag(path, fx.CandidateId, fx.Head, fx.PreTag, "v2026.10.15.2");
                checks.Add(new ReleaseGateCheck(!wrongFinal && File.ReadAllText(path) == before, "recorder final must match pre-release"));
            }
            if (!resume) ReleaseGateFixture.Matrix(rows, label, checks);
            else resumedChecks.AddRange(checks);
        }
        if (resume) ReleaseGateFixture.Matrix(rows, "C1339 G-53 reservation-resume", resumedChecks);
    }

    // ---- the promoted target, journal recording and races (G-54..G-63, G-66) --------------------------------------

    [Test]
    public void C1339_PromoteReservationAndTarget()
    {
        var rows = new NightlyCaseRows();
        var preChecks = new List<ReleaseGateCheck>();
        var recoveryChecks = new List<ReleaseGateCheck>();

        using (var happy = ReleaseGateGitFixture.CreatePromotion())
        {
            var preBefore = happy.PreReleaseState();
            var result = happy.Promote();
            var obj = happy.LocalGit(["show-ref", "--verify", "--hash", "refs/tags/" + happy.FinalTag], remote: true, allowAbsent: true);
            var peel = happy.Peel(happy.FinalTag);
            var type = obj.Length > 0 ? happy.LocalGit(["cat-file", "-t", obj], remote: true) : "";
            preChecks.Add(new ReleaseGateCheck(happy.PreReleaseState() == preBefore, "fresh success"));
            var head = happy.LocalGit(["rev-parse", "HEAD"]);
            var live = happy.LocalGit(["rev-parse", "refs/heads/" + happy.CandidateRef], remote: true);
            rows.Check(result.ExitCode == 0 && result.Promoted && happy.Head != happy.B && head == happy.B && live == happy.Head && peel == happy.Head && peel != head,
                "C1339 G-54 target-peel", result.Refusal + ":" + peel + "/" + head);
            rows.Check(type == "tag" && peel == happy.Head, "C1339 G-55 annotated-final", type);
            happy.ClearTrace();
            var again = happy.Promote();
            var stored = Reservations(happy.PublicationsPath);
            recoveryChecks.Add(new ReleaseGateCheck(again.ExitCode == 0 && again.Resumed && !again.Promoted && happy.NoWrites() && Count(stored) == 1
                && Str(At(stored, 0)?["finalTag"]) == happy.FinalTag
                && happy.LocalGit(["show-ref", "--verify", "--hash", "refs/tags/" + happy.FinalTag], remote: true) == obj, "ordinary same-final retry"));
            preChecks.Add(new ReleaseGateCheck(happy.PreReleaseState() == preBefore, "same-final retry"));
        }

        using (var fx = ReleaseGateGitFixture.CreatePromotion())
        {
            fx.AddTag(fx.FinalTag, fx.C);
            var before = fx.RecipientState();
            var preBefore = fx.PreReleaseState();
            var r = fx.Promote();
            rows.Check(r.Refusal == "final-tag-conflict" && !r.Promoted && !r.Resumed && fx.NoWrites() && fx.RecipientState() == before,
                "C1339 G-56 final-conflict", r.Refusal);
            preChecks.Add(new ReleaseGateCheck(fx.PreReleaseState() == preBefore, "conflicting final"));
        }

        var checks = new List<ReleaseGateCheck>();
        foreach (var final in new[] { false, true })
        {
            using var fx = ReleaseGateGitFixture.CreatePromotion();
            var tag = fx.PreTag;
            if (final)
            {
                tag = fx.FinalTag;
                fx.AddTag(tag, fx.Head);
            }
            fx.SetRead("refs/tags/" + tag + "^{}", "", mode: "replace");
            var before = fx.RecipientState();
            var r = fx.Promote();
            checks.Add(new ReleaseGateCheck(r.Refusal == "tag-peel" && fx.NoWrites() && fx.RecipientState() == before, tag + ":" + r.Refusal));
        }
        ReleaseGateFixture.Matrix(rows, "C1339 G-57 direct-without-peel", checks);

        foreach (var fault in new[] { "remove", "throw", "silent", "replace" })
        {
            using var fx = ReleaseGateGitFixture.CreatePromotion();
            var path = fx.PublicationsPath;
            var unrelated = "";
            if (fault == "silent")
            {
                ReleaseGateJournal.Production().Reserve(path, "rc-2026-10-15-" + fx.C[..12], fx.C, Cut, [fx.PreTag]);
                unrelated = At(Reservations(path), 1)?.ToJsonString() ?? "";
            }
            var expectedCount = unrelated.Length > 0 ? 2 : 1;
            var before = File.ReadAllText(path);
            var preBefore = fx.PreReleaseState();
            fx.InstallWriterFault();
            fx.Faults = fault is "throw" or "silent"
                ? new ReleaseGateGitFaults { Writer = fault }
                : new ReleaseGateGitFaults { JournalCut = new ReleaseGateJournalCut("refs/tags/" + fx.FinalTag, path, fault, fx.B) };
            ReleasePromoteResult r;
            try { r = fx.Promote(); }
            catch (Exception e) { r = new ReleasePromoteResult { ExitCode = 1, Refusal = "threw:" + e.Message }; }
            var after = File.ReadAllText(path);
            var cutRows = Reservations(path);
            var shape = fault switch
            {
                "remove" => Count(cutRows) == 0,
                "replace" => Count(cutRows) == 1 && Str(At(cutRows, 0)?["sha"]) == fx.B && Str(At(cutRows, 0)?["finalTag"]) == "",
                _ => after == before,
            };
            var label = fault switch
            {
                "remove" => "C1339 G-58 journal-result",
                "throw" => "C1339 G-59 journal-exception",
                "silent" => "C1339 G-60 journal-readback",
                _ => "C1339 G-66 recording-identity",
            };
            rows.Check(r.ExitCode != 0 && r.Refusal == "promotion-journal-write-failed" && !r.Promoted && !r.Resumed && shape && fx.Peel(fx.FinalTag) == fx.Head,
                label, fault + ":" + r.Refusal);
            preChecks.Add(new ReleaseGateCheck(fx.PreReleaseState() == preBefore, "journal fault:" + fault));
            var finalObject = fx.LocalGit(["show-ref", "--verify", "--hash", "refs/tags/" + fx.FinalTag], remote: true);
            fx.Faults = null;
            File.WriteAllText(path, before);
            fx.ClearTrace();
            var again = fx.Promote();
            var recovered = Reservations(path);
            var unrelatedOk = unrelated.Length == 0 || (At(recovered, 1)?.ToJsonString() ?? "") == unrelated;
            var first = At(recovered, 0);
            recoveryChecks.Add(new ReleaseGateCheck(again.ExitCode == 0 && again.Resumed && fx.NoWrites() && Count(recovered) == expectedCount && unrelatedOk
                && Str(first?["sha"]) == fx.Head && Str(first?["tag"]) == fx.PreTag && Str(first?["finalTag"]) == fx.FinalTag
                && fx.LocalGit(["show-ref", "--verify", "--hash", "refs/tags/" + fx.FinalTag], remote: true) == finalObject,
                "journal recovery:" + fault + ":" + again.Refusal));
        }

        checks = new List<ReleaseGateCheck>();
        foreach (var fault in new[] { "race", "before", "after" })
        {
            using var fx = ReleaseGateGitFixture.CreatePromotion();
            var preBefore = fx.PreReleaseState();
            fx.Faults = fault == "race"
                ? new ReleaseGateGitFaults { PushRace = new ReleaseGateGitRace(fx.FinalTag, fx.C) }
                : new ReleaseGateGitFaults { Push = fault };
            var r = fx.Promote();
            var safe = !Unsafe.IsMatch(string.Join("\n", fx.GitTrace));
            if (fault == "race")
            {
                checks.Add(new ReleaseGateCheck(r.Refusal == "tag-push-failed" && !r.Promoted && !r.Resumed && fx.Peel(fx.FinalTag) == fx.C && safe, "race:" + r.Refusal));
            }
            else
            {
                var expected = (fault == "before" && r.Refusal == "tag-push-failed" && fx.Peel(fx.FinalTag) == "")
                    || (fault == "after" && r.ExitCode == 0 && fx.Peel(fx.FinalTag) == fx.Head);
                fx.Faults = null;
                var retry = fx.Promote();
                checks.Add(new ReleaseGateCheck(expected && safe && retry.ExitCode == 0 && fx.Peel(fx.FinalTag) == fx.Head, fault + ":" + r.Refusal + "/" + retry.Refusal));
            }
            preChecks.Add(new ReleaseGateCheck(fx.PreReleaseState() == preBefore, "push:" + fault));
        }
        ReleaseGateFixture.Matrix(rows, "C1339 G-63 create-only-race", checks);
        ReleaseGateFixture.Matrix(rows, "C1339 G-61 same-tag-recovery", recoveryChecks);
        ReleaseGateFixture.Matrix(rows, "C1339 G-62 prerelease-preserved", preChecks);

        rows.ShouldAllPass(11,
            "C1339 G-54 target-peel",
            "C1339 G-55 annotated-final",
            "C1339 G-56 final-conflict",
            "C1339 G-57 direct-without-peel",
            "C1339 G-58 journal-result",
            "C1339 G-59 journal-exception",
            "C1339 G-60 journal-readback",
            "C1339 G-61 same-tag-recovery",
            "C1339 G-62 prerelease-preserved",
            "C1339 G-63 create-only-race",
            "C1339 G-66 recording-identity");
    }

    // ---- the promoter's authority (G-38..G-42) ---------------------------------------------------------------------

    [Test]
    public void C1339_PromoteAuthorityHash()
    {
        var rows = new NightlyCaseRows();

        using (var fx = ReleaseGateGitFixture.CreatePromotion())
        {
            var control = fx.Promote(o => o.WhatIf = true);
            var snap = fx.Fx.Save();
            var hashChecks = new List<ReleaseGateCheck> { new(control.ExitCode == 0 && fx.NoWrites(), "valid:" + control.Refusal) };
            // Deviation 1: the promoter's C# is pinned by the lib/Antiphon.Nightly source-tree digest.
            var bound = ReleaseGateFixture.ReadObject(fx.Fx.AuthorityPath)["scriptHashes"];
            var hashes = bound as JsonObject ?? new JsonObject();
            var expected = ReleaseAuthority.Scripts.Concat(ReleaseAuthority.SourceTrees).ToArray();
            var current = (string?)ReleaseAuthority.ScriptHashes(ReleaseGateFixture.ScriptsRoot)[NightlyTree];
            hashChecks.Add(new ReleaseGateCheck(bound is JsonObject && hashes.Count == expected.Length && expected.All(key => hashes.ContainsKey(key))
                && !string.IsNullOrEmpty(current) && Str(hashes[NightlyTree]) == current, "the pinned script and source-tree digests"));
            foreach (var missing in new[] { false, true })
            {
                hashChecks.Add(fx.AuthorityVariant(snap, () => fx.Fx.UpdateAuthority(a =>
                {
                    if (missing) a["scriptHashes"]!.AsObject().Remove(NightlyTree);
                    else a["scriptHashes"]![NightlyTree] = Zeros;
                }), "script-digest-mismatch:" + NightlyTree));
            }
            ReleaseGateFixture.Matrix(rows, "C1339 G-38 promotion-hash-required", hashChecks);
        }

        foreach (var resume in new[] { false, true })
        {
            using var fx = ReleaseGateGitFixture.CreatePromotion();
            if (resume) fx.AddTag(fx.FinalTag, fx.Head);
            var control = fx.Promote(o => o.WhatIf = true);
            var snap = fx.Fx.Save();
            var checks = new List<ReleaseGateCheck> { new(control.ExitCode == 0 && fx.NoWrites(), "valid:" + control.Refusal) };
            var variants = new (string Reason, Action Edit)[]
            {
                ("script-digest-mismatch:" + NightlyTree, () => fx.Fx.UpdateAuthority(a => a["scriptHashes"]![NightlyTree] = Zeros)),
                ("missing-authority", () => File.Delete(fx.Fx.AuthorityPath)),
                ("ledger-selection", () => fx.Fx.UpdateLedger(l => l["selection"] = "targeted")),
                ("ledger-diagnostic", () => fx.Fx.UpdateLedger(l => l["diagnostic"] = true)),
                ("uid-not-passed:antiphon-u1", () => fx.Fx.SetTrxOutcome("antiphon-001", "antiphon-u1", "Failed")),
                ("policy-raw-digest-mismatch", () => fx.Fx.UpdateAuthority(a => a["policyRawSha256"] = Zeros)),
                ("release-card-readback-unavailable", () => fx.Fx.UpdateCard(store: s => s.Status = 503)),
            };
            foreach (var (reason, edit) in variants) checks.Add(fx.AuthorityVariant(snap, edit, reason));
            var restored = fx.Promote();
            checks.Add(new ReleaseGateCheck(restored.ExitCode == 0 && fx.Peel(fx.FinalTag) == fx.Head && (restored.Promoted || restored.Resumed),
                "restored admission:" + restored.Refusal));
            ReleaseGateFixture.Matrix(rows, resume ? "C1339 G-40 promotion-resume-authority" : "C1339 G-39 promotion-fresh-authority", checks);
        }

        using (var fx = ReleaseGateGitFixture.CreatePromotion())
        {
            var snap = fx.Fx.Save();
            var checks = new List<ReleaseGateCheck>
            {
                fx.AuthorityVariant(snap, () => { }, "authority-repository", o => o.Repository = "fixture/foreign"),
                // @{ Repository = $null } dropped the parameter: the default destination.
                fx.AuthorityVariant(snap, () => { }, "authority-repository", o => o.Repository = ReleasePublishOptions.DefaultRepository),
            };
            var control = fx.Promote(o => o.WhatIf = true);
            checks.Add(new ReleaseGateCheck(control.ExitCode == 0, control.Refusal));
            ReleaseGateFixture.Matrix(rows, "C1339 G-41 promotion-repository", checks);

            // Deviation 2: ANTIPHON_API reaches the promoter only through the CLI's Parse; an explicit URL wins over it.
            using var fallback = ReleaseGateGitFixture.CreatePromotion();
            fallback.Fx.UpdateCard(card: c => c["revisionCount"] = 5);
            Func<string, string?> variables = name => name == "ANTIPHON_API" ? fallback.Fx.CardApi.Url : null;
            var flagged = ReleasePromoteOptions.Parse(["--tag", fx.PreTag, "--candidate-id", fx.CandidateId, "--antiphon-api-url", fx.Fx.CardApi.Url],
                ReleaseGateFixture.ScriptsRoot, variables!);
            var unflagged = ReleasePromoteOptions.Parse(["--tag", fx.PreTag, "--candidate-id", fx.CandidateId], ReleaseGateFixture.ScriptsRoot, variables!);
            var explicitUrl = flagged.AntiphonApiUrl;
            var overridden = fx.Promote(o =>
            {
                o.WhatIf = true;
                o.AntiphonApiUrl = explicitUrl;
            });
            checks = new List<ReleaseGateCheck>
            {
                new(explicitUrl == fx.Fx.CardApi.Url && unflagged.AntiphonApiUrl == fallback.Fx.CardApi.Url && overridden.ExitCode == 0,
                    "explicit override:" + overridden.Refusal + " flagged=" + explicitUrl + " unflagged=" + unflagged.AntiphonApiUrl),
            };
            checks.Add(fx.AuthorityVariant(snap, () => fx.Fx.UpdateCard(store: s => s.Status = 503), "release-card-readback-unavailable"));
            checks.Add(fx.AuthorityVariant(snap, () => fx.Fx.UpdateCard(card: c => c["revisionCount"] = 5), "release-card-readback-revision"));
            ReleaseGateFixture.Matrix(rows, "C1339 G-42 promotion-card-readback", checks);
        }

        rows.ShouldAllPass(5,
            "C1339 G-38 promotion-hash-required",
            "C1339 G-39 promotion-fresh-authority",
            "C1339 G-40 promotion-resume-authority",
            "C1339 G-41 promotion-repository",
            "C1339 G-42 promotion-card-readback");
    }

    // ---- the allocation day (G-33..G-37) ---------------------------------------------------------------------------

    [Test]
    public void C1339_AllocationUtcDay()
    {
        var rows = new NightlyCaseRows();
        bool identityPreserved;

        using (var fx = ReleaseGateGitFixture.Create())
        {
            fx.SetClock("2026-10-20T01:59:59+02:00", "2026-10-20T00:00:01Z");
            var candidatePath = fx.Fx.JournalPath;
            var candidateBefore = File.ReadAllText(candidatePath);
            var preview = fx.Publish(whatIf: true);
            var previewClean = !File.Exists(fx.PublicationsPath);
            var first = fx.Publish();
            rows.Check(preview.ExitCode == 0 && preview.Tag == "v2026.10.19.1-rc.1" && previewClean && first.ExitCode == 0 && first.Published
                && first.Tag == "v2026.10.19.1-rc.1" && fx.Peel(first.Tag) == fx.Head, "C1339 G-33 utc-day", first.Tag + ":" + first.Refusal);
            var calls = fx.ClockSamples;
            rows.Check(calls == 2, "C1339 G-34 one-clock-sample", calls.ToString(CultureInfo.InvariantCulture));
            identityPreserved = File.ReadAllText(candidatePath) == candidateBefore;
        }

        using (var fx = ReleaseGateGitFixture.Create())
        {
            fx.SetClock("2026-10-19T23:59:59Z");
            fx.Faults = new ReleaseGateGitFaults { Push = "before" };
            var failed = fx.Publish();
            var journal = fx.PublicationsPath;
            var reserved = At(Reservations(journal), 0)?.DeepClone() ?? new JsonObject();
            var reservedBeforeMidnight = failed.Refusal == "tag-push-failed" && failed.Tag == "v2026.10.19.1-rc.1" && fx.Peel(failed.Tag) == ""
                && !ReleaseGateJournal.Truthy(reserved["published"]);
            fx.Faults = null;
            fx.SetClock("2026-10-20T00:00:01Z");
            var again = fx.Publish();
            var stored = Reservations(journal);
            var row = At(stored, 0);
            var same = reservedBeforeMidnight && again.ExitCode == 0 && again.Published && again.Resumed && again.Tag == "v2026.10.19.1-rc.1" && Count(stored) == 1
                && Str(row?["tag"]) == Str(reserved["tag"]) && Str(row?["sha"]) == Str(reserved["sha"]) && Str(row?["candidateId"]) == Str(reserved["candidateId"])
                && Str(row?["reservedAt"]) == Str(reserved["reservedAt"]);
            fx.SetCandidate(fx.B);
            var next = fx.Publish();
            rows.Check(same && next.ExitCode == 0 && next.Published && next.Tag == "v2026.10.20.1-rc.1" && fx.Peel(next.Tag) == fx.B,
                "C1339 G-35 midnight-reuse", again.Tag + "/" + next.Tag + ":" + next.Refusal);
        }

        using (var stamp = ReleaseGateGitFixture.Create(stamp: true))
        {
            stamp.SetClock("2026-10-20T00:00:01Z");
            var legacy = stamp.Publish();
            rows.Check(legacy.ExitCode == 0 && legacy.Published && legacy.Tag == "v2026.09.23.1" && stamp.Peel(legacy.Tag) == stamp.Head,
                "C1339 G-36 stamp-day", legacy.Tag + ":" + legacy.Refusal);
        }
        rows.Check(identityPreserved, "C1339 G-37 candidate-date-preserved");

        rows.ShouldAllPass(5,
            "C1339 G-33 utc-day",
            "C1339 G-34 one-clock-sample",
            "C1339 G-35 midnight-reuse",
            "C1339 G-36 stamp-day",
            "C1339 G-37 candidate-date-preserved");
    }

    // ---- exact remote reads -----------------------------------------------------------------------------------------

    // G-7..G-9 (the candidate's reads) run in process since unit 13: ReleaseCandidateGitTests.C1339_CandidateRemoteReads;
    // G-10..G-17 since unit 14 below. Only the PowerShell fixture adapter's guards stay in the driver.
    [Test]
    public Task C1339_ExactRemoteRefs() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1339", "C1339_ExactRemoteRefs", 2,
            "C1339 G-64 fixture-path-contained",
            "C1339 G-65 fixture-transport-allowlist");

    [Test]
    public void C1339_ExactRemoteRefs_publisher_and_promoter()
    {
        var rows = new NightlyCaseRows();

        var checks = new List<ReleaseGateCheck>();
        foreach (var order in new[] { "control", "nested-first", "exact-first" })
        {
            using var fx = ReleaseGateGitFixture.Create();
            fx.AddTag(fx.PreTag, fx.Head);
            if (order != "control")
            {
                fx.AddTag("refs/tags/" + fx.PreTag, fx.B);
                var reference = "refs/tags/" + fx.PreTag + "^{}";
                var output = fx.LocalGit(["ls-remote", "--tags", fx.Bare, reference]);
                // Real records, reordered so the nested record arrives first.
                var lines = output.Split('\n').OrderBy(line => line.Split('\t') is { Length: > 1 } fields ? fields[1] : "", StringComparer.OrdinalIgnoreCase).ToArray();
                if (order == "exact-first") Array.Reverse(lines);
                fx.Faults = new ReleaseGateGitFaults { Reads = [new ReleaseGateGitRead(reference, string.Join("\n", lines), "replace")] };
            }
            fx.InitializePromotionReservation();
            var r = fx.Promote();
            var peel = fx.LocalGit(["rev-parse", "--verify", "refs/tags/" + fx.FinalTag + "^{}"], remote: true, allowAbsent: true);
            checks.Add(new ReleaseGateCheck(r.ExitCode == 0 && r.Promoted && peel == fx.Head, order + " refusal=" + r.Refusal + " peel=" + peel));
        }
        ReleaseGateFixture.Matrix(rows, "C1339 G-13 promotion-pre-read", checks);

        using (var fx = ReleaseGateGitFixture.Create())
        {
            fx.LocalGit(["push", fx.Bare, fx.B + ":refs/heads/" + fx.CandidateRef]);
            fx.SetRead("refs/heads/" + fx.CandidateRef, fx.Head + "\trefs/heads/archive/" + fx.CandidateRef);
            var r = fx.Publish();
            rows.Check(r.Refusal == "remote-candidate-moved" && fx.NoWrites() && fx.Peel(fx.PreTag) == "", "C1339 G-10 publisher-head-read", r.Refusal);
        }

        checks = new List<ReleaseGateCheck>();
        foreach (var push in new[] { "after", "before" })
        {
            using var fx = ReleaseGateGitFixture.Create();
            var nestedSha = push == "before" ? fx.Head : fx.B;
            fx.SetRead("refs/tags/" + fx.PreTag + "^{}", nestedSha + "\trefs/tags/refs/tags/" + fx.PreTag + "^{}", push: push);
            var r = fx.Publish();
            var peel = fx.Peel(fx.PreTag);
            var ok = push == "after" ? r.ExitCode == 0 && r.Published && peel == fx.Head : r.Refusal == "tag-push-failed" && peel == "";
            checks.Add(new ReleaseGateCheck(ok && Count(Reservations(fx.PublicationsPath)) == 1, push + ":" + r.Refusal));
        }
        ReleaseGateFixture.Matrix(rows, "C1339 G-11 publisher-recovery-read", checks);

        using (var fx = ReleaseGateGitFixture.Create())
        {
            fx.AddTag("refs/tags/" + fx.PreTag, fx.B);
            fx.SetRead("refs/tags/" + fx.PreTag + "^{}", fx.B + "\trefs/tags/unrelated/" + fx.PreTag + "^{}");
            var r = fx.Publish();
            var releases = fx.Fx.GitHub.Releases;
            var privateRefs = fx.LocalGit(["for-each-ref", "--format=%(refname)", "refs/antiphon-release-push/"]);
            rows.Check(r.ExitCode == 0 && r.Published && fx.Peel(fx.PreTag) == fx.Head && releases.Count == 1 && !releases[0].IsDraft && privateRefs == "",
                "C1339 G-12 publisher-verify-read", r.Refusal);
        }

        using (var fx = ReleaseGateGitFixture.Create())
        {
            fx.AddTag(fx.PreTag, fx.Head);
            fx.LocalGit(["push", fx.Bare, fx.B + ":refs/heads/" + fx.CandidateRef]);
            fx.SetRead("refs/heads/" + fx.CandidateRef, fx.Head + "\trefs/heads/archive/" + fx.CandidateRef);
            fx.InitializePromotionReservation();
            var r = fx.Promote();
            rows.Check(r.Refusal == "remote-candidate-moved" && fx.NoWrites() && fx.Peel(fx.FinalTag) == "", "C1339 G-14 promotion-head-read", r.Refusal);
        }

        checks = new List<ReleaseGateCheck>();
        foreach (var conflict in new[] { false, true })
        {
            using var fx = ReleaseGateGitFixture.Create();
            fx.AddTag(fx.PreTag, fx.Head);
            fx.AddTag("refs/tags/" + fx.FinalTag, fx.Head);
            if (conflict) fx.AddTag(fx.FinalTag, fx.C);
            fx.InitializePromotionReservation();
            var r = fx.Promote();
            var peel = fx.Peel(fx.FinalTag);
            var privateRefs = fx.LocalGit(["for-each-ref", "--format=%(refname)", "refs/antiphon-release-push/"]);
            var ok = conflict
                ? r.Refusal == "final-tag-conflict" && peel == fx.C && fx.NoWrites()
                : r.ExitCode == 0 && r.Promoted && !r.Resumed && peel == fx.Head;
            checks.Add(new ReleaseGateCheck(ok && privateRefs == "" && fx.Peel("refs/tags/" + fx.FinalTag) == fx.Head, "conflict=" + conflict + ":" + r.Refusal));
        }
        ReleaseGateFixture.Matrix(rows, "C1339 G-15 promotion-existing-read", checks);

        checks = new List<ReleaseGateCheck>();
        foreach (var push in new[] { "after", "before" })
        {
            using var fx = ReleaseGateGitFixture.Create();
            fx.AddTag(fx.PreTag, fx.Head);
            var nestedSha = push == "before" ? fx.Head : fx.B;
            fx.SetRead("refs/tags/" + fx.FinalTag + "^{}", nestedSha + "\trefs/tags/refs/tags/" + fx.FinalTag + "^{}", ordinal: 2, push: push);
            fx.InitializePromotionReservation();
            var r = fx.Promote();
            var peel = fx.Peel(fx.FinalTag);
            var ok = push == "after" ? r.ExitCode == 0 && r.Promoted && peel == fx.Head : r.Refusal == "tag-push-failed" && peel == "";
            checks.Add(new ReleaseGateCheck(ok, push + ":" + r.Refusal));
        }
        ReleaseGateFixture.Matrix(rows, "C1339 G-16 promotion-recovery-read", checks);

        checks = new List<ReleaseGateCheck>();
        foreach (var failure in new[] { false, true })
        {
            using var fx = ReleaseGateGitFixture.Create();
            fx.AddTag(fx.PreTag, fx.Head);
            if (failure) fx.SetRead("refs/tags/" + fx.FinalTag + "^{}", mode: "replace", ordinal: 2, exit: 1);
            else fx.SetRead("refs/tags/" + fx.FinalTag + "^{}", fx.B + "\trefs/tags/refs/tags/" + fx.FinalTag + "^{}", ordinal: 2);
            fx.InitializePromotionReservation();
            var r = fx.Promote();
            var ok = failure ? r.Refusal == "tag-peel" && !r.Promoted && !r.Resumed : r.ExitCode == 0 && r.Promoted;
            checks.Add(new ReleaseGateCheck(ok && fx.Peel(fx.FinalTag) == fx.Head, "failed-query=" + failure + ":" + r.Refusal));
        }
        ReleaseGateFixture.Matrix(rows, "C1339 G-17 promotion-verify-read", checks);

        rows.ShouldAllPass(8,
            "C1339 G-10 publisher-head-read",
            "C1339 G-11 publisher-recovery-read",
            "C1339 G-12 publisher-verify-read",
            "C1339 G-13 promotion-pre-read",
            "C1339 G-14 promotion-head-read",
            "C1339 G-15 promotion-existing-read",
            "C1339 G-16 promotion-recovery-read",
            "C1339 G-17 promotion-verify-read");
    }

    // ---- remote tag allocation (G-18..G-21, G-25, G-26, G-29..G-32, G-69) -------------------------------------------

    [Test]
    public void C1339_RemoteTagAllocation()
    {
        var rows = new NightlyCaseRows();

        // Two finals on the remote, annotated or lightweight, close sequences 1 and 2; G-19 and G-20 build on the last.
        ReleaseGateCheck Finals(ReleaseGateGitFixture fx, bool lightweight)
        {
            foreach (var n in new[] { 1, 2 })
            {
                var tag = "v2026.10.15." + n.ToString(CultureInfo.InvariantCulture);
                if (lightweight) fx.LocalGit(["tag", tag, fx.Head], remote: true);
                else fx.AddTag(tag, fx.Head);
            }
            var r = fx.Publish(whatIf: true);
            return new ReleaseGateCheck(r.ExitCode == 0 && r.Tag == "v2026.10.15.3-rc.1" && !File.Exists(fx.PublicationsPath) && fx.NoWrites(),
                "lightweight=" + lightweight + " tag=" + r.Tag + ":" + r.Refusal);
        }
        var finalChecks = new List<ReleaseGateCheck>();
        using (var annotated = ReleaseGateGitFixture.Create()) finalChecks.Add(Finals(annotated, false));
        using (var finals = ReleaseGateGitFixture.Create())
        {
            finalChecks.Add(Finals(finals, true));
            ReleaseGateFixture.Matrix(rows, "C1339 G-18 remote-finals", finalChecks);
            finals.AddTag("v2026.10.15.3-rc.4", finals.Head);
            var r = finals.Publish(whatIf: true);
            rows.Check(r.ExitCode == 0 && r.Tag == "v2026.10.15.3-rc.5", "C1339 G-19 remote-prerelease", r.Tag);
            finals.AddTag("v2026.10.15.3", finals.Head);
            r = finals.Publish(whatIf: true);
            rows.Check(r.ExitCode == 0 && r.Tag == "v2026.10.15.4-rc.1", "C1339 G-20 remote-final-closes", r.Tag);
        }

        using (var fx = ReleaseGateGitFixture.Create())
        {
            fx.AddTag("refs/tags/v2026.10.15.1", fx.B);
            fx.AddTag("irrelevant", fx.B);
            fx.AddTag("v2026.10.15.3", fx.Head);
            var r = fx.Publish(whatIf: true);
            // The inventory's own canonical filter (with and without peeled records) is
            // ReleaseGateLibraryTests.Tag_inventory_keeps_canonical_release_tags_only.
            var output = fx.LocalGit(["ls-remote", "--tags", fx.Bare]);
            fx.SetRead("origin", output, mode: "replace");
            var withPeels = fx.Publish(whatIf: true);
            rows.Check(r.ExitCode == 0 && r.Tag == "v2026.10.15.1-rc.1" && withPeels.ExitCode == 0 && withPeels.Tag == "v2026.10.15.1-rc.1",
                "C1339 G-21 canonical-inventory", r.Tag + "/" + withPeels.Tag);
        }

        using (var fx = ReleaseGateGitFixture.Create())
        {
            var control = fx.Publish(whatIf: true);
            var checks = new List<ReleaseGateCheck> { new(control.ExitCode == 0 && control.Tag == "v2026.10.15.1-rc.1", "valid inventory admission") };
            foreach (var preview in new[] { true, false })
            {
                fx.SetRead("origin", mode: "replace", exit: 1);
                var r = fx.Publish(whatIf: preview);
                checks.Add(new ReleaseGateCheck(r.Refusal == "remote-tag-inventory-failed" && r.Tag == "" && fx.NoWrites() && !File.Exists(fx.PublicationsPath),
                    "preview=" + preview + ":" + r.Refusal));
            }
            foreach (var bad in new[] { "short\trefs/tags/v2026.10.15.1", fx.Head + "\trefs/tags/v2026.10.15.1\n" + fx.B + "\trefs/tags/v2026.10.15.1", "malformed" })
            {
                fx.SetRead("origin", bad, mode: "replace");
                var r = fx.Publish(whatIf: true);
                checks.Add(new ReleaseGateCheck(r.Refusal == "remote-tag-inventory-failed" && r.Tag == "" && fx.NoWrites(), "invalid inventory:" + r.Refusal));
            }
            ReleaseGateFixture.Matrix(rows, "C1339 G-25 inventory-required", checks);
        }

        using (var fx = ReleaseGateGitFixture.Create())
        {
            fx.Faults = new ReleaseGateGitFaults { Push = "before" };
            var first = fx.Publish();
            fx.AddTag("v2026.10.15.2-rc.4", fx.B);
            fx.Faults = new ReleaseGateGitFaults();
            var second = fx.Publish();
            var stored = Reservations(fx.PublicationsPath);
            rows.Check(first.Refusal == "tag-push-failed" && second.ExitCode == 0 && second.Published && second.Resumed && first.Tag == "v2026.10.15.1-rc.1"
                && second.Tag == first.Tag && Count(stored) == 1 && fx.Peel(second.Tag) == fx.Head, "C1339 G-26 reservation-reuse", second.Refusal);
        }
        // G-22..G-24, G-27, G-28, G-67, G-68 and G-70 (the proposal over the journal and the inventory) run in process:
        // ReleaseGateLibraryTests.C1339_RemoteTagAllocation_proposal.

        foreach (var final in new[] { false, true })
        {
            using var fx = ReleaseGateGitFixture.Create();
            var tag = final ? fx.FinalTag : fx.PreTag;
            var reason = final ? "final-tag-conflict" : "tag-conflict";
            var label = final ? "C1339 G-30 selected-final-recheck" : "C1339 G-29 selected-tag-recheck";
            fx.Faults = new ReleaseGateGitFaults { InventoryRace = new ReleaseGateGitRace(tag, fx.C) };
            var r = fx.Publish();
            var journal = fx.PublicationsPath;
            var stored = Reservations(journal);
            var localTag = fx.LocalGit(["show-ref", "--verify", "--hash", "refs/tags/" + fx.PreTag], allowAbsent: true);
            var checks = new List<ReleaseGateCheck>
            {
                new(r.Refusal == reason && r.Tag == fx.PreTag && Count(stored) == 1 && Str(At(stored, 0)?["tag"]) == fx.PreTag && fx.NoWrites() && localTag == ""
                    && fx.Peel(tag) == fx.C, "race:" + r.Refusal),
            };
            // Restart from that persisted reservation against the now conflicting ref.
            var before = File.ReadAllText(journal);
            var again = fx.Publish();
            checks.Add(new ReleaseGateCheck(again.Refusal == reason && again.Tag == fx.PreTag && File.ReadAllText(journal) == before && fx.NoWrites(),
                "reserved conflict:" + again.Refusal));
            ReleaseGateFixture.Matrix(rows, label, checks);
        }

        using (var fx = ReleaseGateGitFixture.Create())
        {
            fx.AddTag("v2026.10.15.1", fx.B);
            var journal = fx.PublicationsPath;
            var pubAuthority = fx.Fx.PublicationAuthorityPath;
            var preview = fx.Publish(whatIf: true);
            var emptyPreview = preview.ExitCode == 0 && preview.Tag == "v2026.10.15.2-rc.1" && !File.Exists(journal);
            var emptyAuthority = !File.Exists(pubAuthority);
            ReleaseGateJournal.Production().Reserve(journal, fx.CandidateId, fx.Head, Day, ["v2026.10.15.1"]);
            var before = File.ReadAllText(journal);
            preview = fx.Publish(whatIf: true);
            rows.Check(emptyPreview && preview.ExitCode == 0 && preview.Resumed && File.ReadAllText(journal) == before && fx.NoWrites(),
                "C1339 G-31 preview-reservation", preview.Refusal);
            rows.Check(emptyAuthority && !File.Exists(pubAuthority), "C1339 G-32 preview-authority");
        }

        using (var fx = ReleaseGateGitFixture.Create())
        {
            fx.AddTag("v2026.10.15.1", fx.Head);
            fx.AddTag("v2026.10.15.2", fx.Head);
            fx.Faults = new ReleaseGateGitFaults { Push = "before" };
            var r = fx.Publish();
            var stored = Reservations(fx.PublicationsPath);
            var row = At(stored, 0);
            rows.Check(r.Refusal == "tag-push-failed" && Count(stored) == 1 && Str(row?["tag"]) == "v2026.10.15.3-rc.1" && Str(row?["candidateId"]) == fx.CandidateId
                && !ReleaseGateJournal.Truthy(row?["published"]), "C1339 G-69 no-invented-publications", r.Refusal);
        }

        rows.ShouldAllPass(11,
            "C1339 G-18 remote-finals",
            "C1339 G-19 remote-prerelease",
            "C1339 G-20 remote-final-closes",
            "C1339 G-21 canonical-inventory",
            "C1339 G-25 inventory-required",
            "C1339 G-26 reservation-reuse",
            "C1339 G-29 selected-tag-recheck",
            "C1339 G-30 selected-final-recheck",
            "C1339 G-31 preview-reservation",
            "C1339 G-32 preview-authority",
            "C1339 G-69 no-invented-publications");
    }
}
