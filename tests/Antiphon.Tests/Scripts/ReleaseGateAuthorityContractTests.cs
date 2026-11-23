using System.Collections;
using System.Collections.Specialized;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 D-15 (round B1, V-18 / R-9): the publisher's ordinary authority contract, in process against the PRODUCTION
/// publisher (Antiphon.Nightly.ReleasePublish since unit 14 of 2026-11-22-remaining-ps1-to-csharp.md) and
/// ReleaseAuthority. These replaced C599B_MissingDiscovery, C599B_PinnedPolicy and C599B_AllChunks of
/// scripts/test-release-gate.ps1 (same rows). The release clone is a real git repository whose commit holds the policy; the
/// authority, frozen chunk plan and ledger are written by the production ReleaseAuthority and the chunk evidence is real TRX
/// (ReleaseGateFixture). Recipient state (the fake remote and the GitHub store) is read by a separate observer.
///
/// <para>The decisive behaviours: the policy blob re-read at the pinned SHA - never the summary or report - names the eight
/// required suites; a stale pinned hash or blob id, a missing suite, a failed, skipped or deleted chunk under a passing
/// sibling, and a legacy candidate with no authority each refuse with zero remote writes.</para>
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGateAuthorityContractTests
{
    [Test]
    public void C599B_MissingDiscovery()
    {
        var rows = new NightlyCaseRows();
        foreach (var variant in new[] { "missing", "empty", "null" })
        {
            using var root = NightlyFixture.Create("c599b-discovery");
            var dir = ReleaseAuthority.Dir(root.Root);
            NightlyFixture.WriteJson(Path.Combine(dir, "authority.json"), new OrderedDictionary
            {
                { "candidateId", ReleaseGateFixture.StampCandidateId }, { "intentId", ReleaseGateFixture.IntentId }, { "sha", ReleaseGateFixture.ShaB },
                { "requiredSuites", new object[] { "markdown-pdf" } }, { "exclusions", Array.Empty<object>() },
            });
            var discovery = new Hashtable(StringComparer.OrdinalIgnoreCase);
            if (variant == "empty") discovery["markdown-pdf"] = Array.Empty<object>();
            if (variant == "null") discovery["markdown-pdf"] = null;
            var plan = ReleaseAuthority.Plan(root.Root, discovery, new Hashtable());
            var suites = ((IEnumerable)plan["suites"]!).Cast<IDictionary>().ToArray();
            var frozen = ReleaseGateFixture.ReadObject(Path.Combine(dir, "discovery.json"));
            var frozenSuite = frozen["suites"]?["markdown-pdf"];
            rows.Check(suites.Length == 1 && ((IEnumerable)suites[0]["chunks"]!).Cast<object>().Count() == 0 && frozenSuite is JsonArray { Count: 0 },
                "C599 missing-discovery-" + variant, frozen.ToJsonString());
        }
        rows.ShouldAllPass(3, "C599 missing-discovery-missing", "C599 missing-discovery-empty", "C599 missing-discovery-null");
    }

    [Test]
    public void C599B_PinnedPolicy()
    {
        var rows = new NightlyCaseRows();
        // V-18 / R-9 ordinary contract: the pinned policy blob, not the report, decides.
        using (var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes()))
        {
            var ok = fx.Publish().Result;
            var release = fx.GitHub.Find(ok.Tag);
            rows.Check(ok.ExitCode == 0 && ok.Published, "C599 V-18: a valid eight-suite pinned authority publishes", ok.Refusal);
            rows.Check(release is { IsDraft: false }, "C599 V-18: the recipient holds a published non-draft release");
            var manifest = ReleaseGateFixture.ReadObject(fx.ManifestPath);
            var ids = manifest["suites"]!.AsArray().Select(s => ReleaseGateFixture.Str(s?["id"])).ToArray();
            rows.Check(ids.Length == 8 && ReleaseGateFixture.Str(manifest["sha"]) == fx.Head, "C599 V-18: the manifest names all eight pinned suites at the tested sha", string.Join(",", ids));
        }

        // A shrunken summary alone confers nothing: it is not read, the eight stay required.
        using (var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes()))
        {
            ReleaseGateFixture.UpdateJson(fx.SummaryPath, s =>
            {
                s["requiredSuites"] = new JsonArray("antiphon");
                s["suites"] = ReleaseGateFixture.Where(s["suites"], row => ReleaseGateFixture.Str(row?["id"]) == "antiphon");
            });
            var shrunk = fx.Publish().Result;
            var suites = ReleaseGateFixture.ReadObject(fx.ManifestPath)["suites"]!.AsArray().Count;
            rows.Check(shrunk.Published && suites == 8, "C599 V-18: a shrunken summary is not a source of required suites", shrunk.Refusal);
        }

        // G-224: a seven-suite report (plan, ledger and summary agree on seven) cannot publish against the eight-suite pinned policy.
        using (var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes()))
        {
            fx.UpdateLedger(l => l["chunks"] = ReleaseGateFixture.Where(l["chunks"], c => ReleaseGateFixture.Str(c?["suite"]) != "e2e"),
                p => p["suites"] = ReleaseGateFixture.Where(p["suites"], s => ReleaseGateFixture.Str(s?["id"]) != "e2e"));
            ReleaseGateFixture.UpdateJson(fx.SummaryPath, s => s["requiredSuites"] = new JsonArray("antiphon", "session-runner", "pty-host", "agents-pty", "messaging", "client", "scripts"));
            var seven = fx.Publish().Result;
            rows.Check(seven.Refusal.Contains("required-suite-missing:e2e") && fx.NoRemoteWrite(), "C599 G-224: seven-suite report cannot publish eight-suite policy", seven.Refusal);
        }

        // G-225: a stale pinned hash in the frozen authority blocks before any remote write.
        using (var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes()))
        {
            ReleaseGateFixture.UpdateJson(fx.AuthorityPath, a => a["policyHash"] = new string('0', 64));
            var stale = fx.Publish().Result;
            rows.Check(stale.Refusal.Contains("policy-hash-mismatch") && fx.NoRemoteWrite(), "C599 G-225: wrong pinned hash blocks before any remote write", stale.Refusal);
        }

        // Stale blob identity: the recorded blob is not the one at the pinned commit.
        using (var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes()))
        {
            ReleaseGateFixture.UpdateJson(fx.AuthorityPath, a => a["policyBlobId"] = new string('1', 40));
            var blob = fx.Publish().Result;
            rows.Check(blob.Refusal.Contains("policy-blob-id-mismatch") && fx.NoRemoteWrite(), "C599 V-18: a stale pinned blob id blocks before any remote write", blob.Refusal);
        }

        // R-9: a legacy candidate with no authority cannot publish; nothing is synthesized.
        using (var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes(), noAuthority: true))
        {
            var legacy = fx.Publish().Result;
            rows.Check(legacy.Refusal.Contains("missing-authority") && !legacy.Published, "C599 R-9: a legacy candidate with no authority refuses", legacy.Refusal);
            rows.Check(fx.NoRemoteWrite(), "C599 R-9: the legacy refusal made zero remote writes");
            rows.Check(!File.Exists(fx.AuthorityPath), "C599 R-9: no authority was synthesized for the legacy candidate");
        }
        rows.ShouldAllPass(10,
            "C599 V-18: a valid eight-suite pinned authority publishes",
            "C599 V-18: the recipient holds a published non-draft release",
            "C599 V-18: the manifest names all eight pinned suites at the tested sha",
            "C599 V-18: a shrunken summary is not a source of required suites",
            "C599 G-224: seven-suite report cannot publish eight-suite policy",
            "C599 G-225: wrong pinned hash blocks before any remote write",
            "C599 V-18: a stale pinned blob id blocks before any remote write",
            "C599 R-9: a legacy candidate with no authority refuses",
            "C599 R-9: the legacy refusal made zero remote writes",
            "C599 R-9: no authority was synthesized for the legacy candidate");
    }

    [Test]
    public void C599B_AllChunks()
    {
        var rows = new NightlyCaseRows();
        // V-18 / R-9 ordinary contract: every frozen chunk and every required UID.
        using (var fx = ReleaseGateFixture.Create())
        {
            var antiphon = fx.NativeChunks().Where(c => c.Suite == "antiphon").ToArray();
            rows.Check(antiphon.Length == 3 && !antiphon.SelectMany(c => c.Uids).Contains("antiphon-x1"),
                "C599 V-18: the frozen plan chunks one class each and omits pinned exclusions", antiphon.Length.ToString());
            var ok = fx.Publish().Result;
            rows.Check(ok.ExitCode == 0 && ok.Published && fx.GitHub.Find(ok.Tag) != null, "C599 V-18: a complete multi-chunk ledger publishes", ok.Refusal);
        }

        // G-226: a passing sibling (antiphon-001/002 pass) cannot hide a failed chunk.
        using (var fx = ReleaseGateFixture.Create())
        {
            fx.SetTrxOutcome("antiphon-003", "antiphon-u4", "Failed");
            var failed = fx.Publish().Result;
            rows.Check(failed.Refusal.Contains("uid-not-passed:antiphon-u4") && fx.NoRemoteWrite(), "C599 G-226: passing sibling cannot hide failed chunk", failed.Refusal);
        }

        // G-290: a deleted chunk record blocks authority.
        using (var fx = ReleaseGateFixture.Create())
        {
            fx.UpdateLedger(l => l["chunks"] = ReleaseGateFixture.Where(l["chunks"], c => ReleaseGateFixture.Str(c?["chunk"]) != "antiphon-002"));
            var missing = fx.Publish().Result;
            rows.Check(missing.Refusal.Contains("chunk-missing:antiphon-002") && fx.NoRemoteWrite(), "C599 G-290: deleted or extra chunk blocks authority", missing.Refusal);
        }

        // G-292: a skipped required UID with passing siblings refuses.
        using (var fx = ReleaseGateFixture.Create())
        {
            fx.SetTrxOutcome("messaging-001", "messaging-u2", "NotExecuted");
            var skipped = fx.Publish().Result;
            rows.Check(skipped.Refusal.Contains("uid-not-passed:messaging-u2") && fx.NoRemoteWrite(), "C599 G-292: required skip unknown missing and stale rows refuse", skipped.Refusal);
        }

        // A client roster entry that did not pass refuses by its own declared roster.
        using (var fx = ReleaseGateFixture.Create())
        {
            fx.UpdateRosterRow("client", r => r["entries"]![2]!["outcome"] = "failed");
            var roster = fx.Publish().Result;
            rows.Check(roster.Refusal.Contains("roster-entry-not-passed:client:vitest") && fx.NoRemoteWrite(),
                "C599 V-18: a failed client roster entry blocks before any remote write", roster.Refusal);
        }
        rows.ShouldAllPass(6,
            "C599 V-18: the frozen plan chunks one class each and omits pinned exclusions",
            "C599 V-18: a complete multi-chunk ledger publishes",
            "C599 G-226: passing sibling cannot hide failed chunk",
            "C599 G-290: deleted or extra chunk blocks authority",
            "C599 G-292: required skip unknown missing and stale rows refuse",
            "C599 V-18: a failed client roster entry blocks before any remote write");
    }
}
