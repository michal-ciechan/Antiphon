using System.Collections;
using System.Collections.Specialized;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 V-6 and D-8: the publisher in process (Antiphon.Nightly.ReleasePublish, the publish-release command). Unit 14 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md deleted scripts/publish-release.ps1; these cases replaced
/// C599_PublicationGate, C599_TagRecovery, C599_DraftRecovery, C599_NotesCredentialScan, C599_AssetRecovery,
/// C599_PublishRecovery and C599_ManifestAllowlist of scripts/test-release-gate.ps1 (formerly run by
/// ReleaseGatePublicationTests, whose candidate cases are ReleaseCandidateTests since unit 13), with their row names. The production
/// publisher runs over the in-memory fake remote and the stateful GitHub fixture (ReleaseGateFixture); a separate observer
/// read of those recipients - never the publisher's own return value - is what proves recipient state.
///
/// <para>Every remote boundary is cut two ways: fail before the write, and commit the write then lose the response. After
/// each cut the run restarts from disk and must land on exactly one tag reservation and one published release - never a
/// second identity. The publication gate refuses every incomplete, diagnostic, NoReport, seam-driven, moved-candidate and
/// missing-required-suite case; a tag with no published release stays pending; and the manifest carries only allowlisted
/// fields bound to the tested SHA.</para>
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleasePublishTests
{
    private const string StampTag = "v2026.09.23.1";

    [Test]
    public void C599_PublicationGate()
    {
        var rows = new NightlyCaseRows();
        using (var fx = ReleaseGateFixture.Create())
        {
            var good = File.ReadAllText(fx.GreenPath);
            var ok = fx.Publish().Result;
            rows.Check(ok.ExitCode == 0 && ok.Published, "C599 PublicationGate control: a complete rc green publishes", ok.Refusal);
            rows.Check(good.Length > 0, "C599 PublicationGate control fixture green was readable");
        }
        foreach (var (field, value, label) in new (string, JsonNode, string)[]
                 {
                     ("coverageComplete", false, "coverage false"), ("testsPassed", false, "tests false"), ("reportDelivered", false, "report false"),
                     ("exitCode", 1, "non-zero exit"), ("trigger", "scheduled", "wrong trigger"), ("profile", "nightly", "wrong profile"),
                     ("noReport", true, "NoReport run"), ("diagnostic", true, "diagnostic run"), ("seamed", true, "seam-driven run"),
                 })
        {
            using var fx = ReleaseGateFixture.Create();
            fx.UpdateGreen(g => g[field] = value.DeepClone());
            var r = fx.Publish().Result;
            rows.Check(r.ExitCode != 0 && !r.Published, "C599 PublicationGate refuses a " + label, r.Refusal);
        }

        // A moved remote candidate refuses even with a perfect green; an unresolved remote is not agreement.
        using (var fx = ReleaseGateFixture.Create())
        {
            fx.Remote.Replace((ReleaseGateFixture.StampCandidateRef, ReleaseGateFixture.ShaC));
            var moved = fx.Publish().Result;
            rows.Check(moved.ExitCode != 0 && moved.Refusal.Contains("remote-candidate-moved"), "C599 PublicationGate a moved remote candidate refuses", moved.Refusal);
        }
        using (var fx = ReleaseGateFixture.Create())
        {
            fx.Remote.Replace();
            var unresolved = fx.Publish().Result;
            rows.Check(unresolved.ExitCode != 0 && unresolved.Refusal.Contains("remote-candidate-unresolved"), "C599 PublicationGate an unresolved remote candidate refuses",
                unresolved.Refusal);
        }

        // A required suite absent from the frozen plan refuses (D-15: the pinned policy names it; the summary is not consulted).
        using (var fx = ReleaseGateFixture.Create())
        {
            fx.UpdateLedger(plan: p => p["suites"] = ReleaseGateFixture.Where(p["suites"], s => ReleaseGateFixture.Str(s?["id"]) != "e2e"));
            var missingSuite = fx.Publish().Result;
            rows.Check(missingSuite.ExitCode != 0 && missingSuite.Refusal.Contains("required-suite-missing:e2e"), "C599 PublicationGate a missing required e2e suite refuses",
                missingSuite.Refusal);
        }

        // A policy hash that does not match the qualified one refuses.
        using (var fx = ReleaseGateFixture.Create())
        {
            var wrongHash = fx.Publish(o => o.ExpectedPolicyHash = "some-other-hash").Result;
            rows.Check(wrongHash.ExitCode != 0 && wrongHash.Refusal.Contains("policy-hash-mismatch"), "C599 PublicationGate a policy hash mismatch refuses", wrongHash.Refusal);
        }

        // Missing evidence refuses before any remote write.
        using (var fx = ReleaseGateFixture.Create())
        {
            File.Delete(fx.GreenPath);
            var noEvidence = fx.Publish().Result;
            rows.Check(noEvidence.Refusal == "missing-evidence", "C599 PublicationGate missing rc green refuses", noEvidence.Refusal);
            rows.Check(fx.GitHub.Find(StampTag) == null, "C599 PublicationGate a refused publication created no release");
        }
        rows.ShouldAllPass(17,
            "C599 PublicationGate control: a complete rc green publishes",
            "C599 PublicationGate refuses a coverage false",
            "C599 PublicationGate refuses a wrong trigger",
            "C599 PublicationGate refuses a NoReport run",
            "C599 PublicationGate refuses a diagnostic run",
            "C599 PublicationGate refuses a seam-driven run",
            "C599 PublicationGate a moved remote candidate refuses",
            "C599 PublicationGate an unresolved remote candidate refuses",
            "C599 PublicationGate a missing required e2e suite refuses",
            "C599 PublicationGate a policy hash mismatch refuses",
            "C599 PublicationGate a refused publication created no release");
    }

    [Test]
    public void C599_TagRecovery()
    {
        var rows = new NightlyCaseRows();
        foreach (var cut in new[] { "before-push", "commit-then-lose-response" })
        {
            using var fx = ReleaseGateFixture.Create();
            fx.Remote.Fail("push", commitsAnyway: cut != "before-push");
            var r1 = fx.Publish().Result;
            if (cut == "before-push")
            {
                rows.Check(r1.Refusal == "tag-push-failed", "C599 TagRecovery before-push refuses", r1.Refusal);
                rows.Check(fx.GitHub.Find(r1.Tag) == null, "C599 TagRecovery before-push created no release");
            }
            else rows.Check(r1.ExitCode == 0, "C599 TagRecovery a lost tag-push response is reconciled from the remote peel", r1.Refusal);
            rows.Check(r1.Tag.Length > 0, "C599 TagRecovery " + cut + " reserved a tag before touching the remote", r1.Tag);

            fx.Remote.Clear("push");
            var r2 = fx.Publish().Result;
            rows.Check(r2.Tag == r1.Tag, "C599 TagRecovery " + cut + " restart reuses the same tag, not a new sequence", r1.Tag + " vs " + r2.Tag);
            rows.Check(r2.ExitCode == 0 && r2.Published, "C599 TagRecovery " + cut + " restart completes the publication", r2.Refusal);
            var reservations = ReleaseGateJournal.Rows(ReleaseGateJournal.Read(fx.PublicationsPath)).Count;
            rows.Check(reservations == 1, "C599 TagRecovery " + cut + " allocated exactly one reservation", reservations.ToString());
        }
        using (var fx = ReleaseGateFixture.Create())
        {
            var seams = fx.Seams();
            rows.Check(seams.Git != null && seams.GitHub != null, "C599 TagRecovery control fixture seams were written");
        }
        rows.ShouldAllPass(12,
            "C599 TagRecovery before-push refuses",
            "C599 TagRecovery before-push created no release",
            "C599 TagRecovery a lost tag-push response is reconciled from the remote peel",
            "C599 TagRecovery before-push restart reuses the same tag, not a new sequence",
            "C599 TagRecovery before-push allocated exactly one reservation");
    }

    [Test]
    public void C599_DraftRecovery()
    {
        var rows = new NightlyCaseRows();
        foreach (var cut in new[] { "before-write", "commit-then-lose-response" })
        {
            using var fx = ReleaseGateFixture.Create();
            fx.GitHub.Fail("release-create", commitsAnyway: cut != "before-write");
            var r1 = fx.Publish().Result;
            var stored = fx.GitHub.Find(r1.Tag);
            if (cut == "before-write")
            {
                rows.Check(r1.Refusal == "draft-create-failed", "C599 DraftRecovery before-write refuses", r1.Refusal);
                rows.Check(stored == null, "C599 DraftRecovery before-write stored no release");
            }
            else
            {
                rows.Check(stored != null, "C599 DraftRecovery a lost create response still left the draft stored", r1.Refusal);
                rows.Check(r1.ExitCode == 0, "C599 DraftRecovery a lost create response is reconciled by readback", r1.Refusal);
            }
            fx.GitHub.Clear("release-create");
            var r2 = fx.Publish().Result;
            rows.Check(r2.ExitCode == 0 && r2.Published, "C599 DraftRecovery " + cut + " restart publishes", r2.Refusal);
            var final = fx.GitHub.Find(r2.Tag);
            rows.Check(final is { IsDraft: false }, "C599 DraftRecovery " + cut + " the recipient sees a published, non-draft release");
            rows.Check(fx.GitHub.Releases.Count == 1, "C599 DraftRecovery " + cut + " exactly one release exists", fx.GitHub.Releases.Count.ToString());
        }
        rows.ShouldAllPass(10,
            "C599 DraftRecovery before-write refuses",
            "C599 DraftRecovery before-write stored no release",
            "C599 DraftRecovery a lost create response is reconciled by readback",
            "C599 DraftRecovery before-write the recipient sees a published, non-draft release",
            "C599 DraftRecovery before-write exactly one release exists");
    }

    /// <summary>Get-C599NotesCredentialFindings: metadata only (pattern and line), never the note text.</summary>
    private static List<(string Pattern, int Line)> CredentialFindings(string notes)
    {
        // A token label may be underscore-delimited (GITHUB_TOKEN/access_token). An embedded word in a test class, such as
        // RefreshGithubTokenRemoteCommandTests, is not a credential label. Case-insensitive, as PowerShell -match.
        var patterns = new (string Name, string Pattern)[] { ("bearer", "Bearer "), ("token", "(?<![a-z0-9])token(?![a-z0-9])") };
        var findings = new List<(string, int)>();
        var lines = Regex.Split(notes, "\r?\n");
        for (var i = 0; i < lines.Length; i++)
            foreach (var (name, pattern) in patterns)
                if (Regex.IsMatch(lines[i], pattern, RegexOptions.IgnoreCase)) findings.Add((name, i + 1));
        return findings;
    }

    private static string Format(List<(string Pattern, int Line)> findings) => string.Join("; ", findings.Select(f => "pattern=" + f.Pattern + " line=" + f.Line));

    [Test]
    public void C599_NotesCredentialScan()
    {
        var rows = new NightlyCaseRows();
        // The real generator with the Windows exclusion list, even on Linux.
        using var fx = ReleaseGateFixture.Create();
        fx.Publish();
        var notes = File.ReadAllText(fx.NotesPath);
        var findings = CredentialFindings(notes);
        rows.Check(notes.Contains("RefreshGithubTokenRemoteCommandTests"), "C599 NotesCredentialScan generator preserves the Windows exclusion class");
        rows.Check(findings.Count == 0, "C599 NotesCredentialScan generated Windows exclusions are not credentials", Format(findings));

        var benign = @"C:\Antiphon\releases\checkout user=fixture machine=fixture-host" + "\r\n" + new string('a', 40) + "\r\n" + new string('b', 64) + "\r\nRefreshGithubTokenRemoteCommandTests";
        findings = CredentialFindings(benign);
        rows.Check(findings.Count == 0, "C599 NotesCredentialScan Windows paths CRLF hashes and identifiers are not credentials", Format(findings));

        var witnesses = new (string Name, string Pattern, string Text)[]
        {
            ("bearer", "bearer", "Bearer c599-synthetic-value"), ("token assignment", "token", "token=c599-synthetic-value"),
            ("JSON token", "token", "\"token\": \"c599-synthetic-value\""), ("environment token", "token", "GITHUB_TOKEN=c599-synthetic-value"),
            ("access token", "token", "access_token: c599-synthetic-value"),
        };
        foreach (var (endingName, ending) in new[] { ("LF", "\n"), ("CRLF", "\r\n") })
            foreach (var witness in witnesses)
            {
                var detail = Format(CredentialFindings(string.Join(ending, "Release fixture", "", witness.Text)));
                rows.Check(detail == "pattern=" + witness.Pattern + " line=3", "C599 NotesCredentialScan " + endingName + " " + witness.Name + " is rejected with safe location", detail);
            }
        rows.ShouldAllPass(13,
            "C599 NotesCredentialScan generator preserves the Windows exclusion class",
            "C599 NotesCredentialScan generated Windows exclusions are not credentials",
            "C599 NotesCredentialScan Windows paths CRLF hashes and identifiers are not credentials",
            "C599 NotesCredentialScan LF bearer is rejected with safe location",
            "C599 NotesCredentialScan CRLF environment token is rejected with safe location");
    }

    [Test]
    public void C599_AssetRecovery()
    {
        var rows = new NightlyCaseRows();
        foreach (var asset in new[] { "release-manifest.json", "verification-summary.json" })
            foreach (var cut in new[] { "before-write", "commit-then-lose-response" })
            {
                using var fx = ReleaseGateFixture.Create();
                var key = "release-upload:" + asset;
                fx.GitHub.Fail(key, commitsAnyway: cut != "before-write");
                var r1 = fx.Publish().Result;
                rows.Check(r1.ExitCode != 0 && !r1.Published, "C599 AssetRecovery " + asset + " " + cut + " does not publish", r1.Refusal);
                var stored = fx.GitHub.Find(r1.Tag);
                rows.Check(stored is { IsDraft: true }, "C599 AssetRecovery " + asset + " " + cut + " leaves a pending draft, not a release");

                fx.GitHub.Clear(key);
                var r2 = fx.Publish().Result;
                rows.Check(r2.ExitCode == 0 && r2.Published, "C599 AssetRecovery " + asset + " " + cut + " restart publishes", r2.Refusal);
                var assets = fx.GitHub.Find(r2.Tag)?.Assets ?? [];
                rows.Check(assets.Contains("release-manifest.json") && assets.Contains("verification-summary.json"),
                    "C599 AssetRecovery " + asset + " " + cut + " both assets are present after recovery", string.Join(",", assets));
                var manifest = ReleaseGateFixture.ReadObject(fx.ManifestPath);
                rows.Check(ReleaseGateFixture.Str(manifest["sha"]) == fx.Head, "C599 AssetRecovery " + asset + " " + cut + " the manifest is bound to the tested sha",
                    ReleaseGateFixture.Str(manifest["sha"]));
                rows.Check(ReleaseGateFixture.Str(manifest["tag"]) == r2.Tag, "C599 AssetRecovery " + asset + " " + cut + " the manifest names the published tag",
                    ReleaseGateFixture.Str(manifest["tag"]));
            }
        // A notes file exists and never leaks a credential-shaped value.
        using (var fx = ReleaseGateFixture.Create())
        {
            var r = fx.Publish().Result;
            var notes = File.ReadAllText(fx.NotesPath);
            rows.Check(r.Tag.Length > 0 && notes.Contains(r.Tag), "C599 AssetRecovery notes name the tag", r.Tag);
            rows.Check(notes.Contains("no earlier release"), "C599 AssetRecovery the first release says there is no earlier release");
            var findings = CredentialFindings(notes);
            rows.Check(findings.Count == 0, "C599 AssetRecovery notes carry no credential-shaped text", Format(findings));
            rows.Check(notes.Contains("Exclusions"), "C599 AssetRecovery notes list the exclusions");
            rows.Check(notes.Contains("Suites"), "C599 AssetRecovery notes list the per-suite counts");
        }
        rows.ShouldAllPass(29,
            "C599 AssetRecovery release-manifest.json before-write does not publish",
            "C599 AssetRecovery release-manifest.json before-write leaves a pending draft, not a release",
            "C599 AssetRecovery release-manifest.json before-write both assets are present after recovery",
            "C599 AssetRecovery release-manifest.json before-write the manifest is bound to the tested sha",
            "C599 AssetRecovery notes name the tag",
            "C599 AssetRecovery the first release says there is no earlier release",
            "C599 AssetRecovery notes carry no credential-shaped text");
    }

    [Test]
    public void C599_PublishRecovery()
    {
        var rows = new NightlyCaseRows();
        foreach (var cut in new[] { "before-write", "commit-then-lose-response" })
        {
            using var fx = ReleaseGateFixture.Create();
            fx.GitHub.Fail("release-publish", commitsAnyway: cut != "before-write");
            var r1 = fx.Publish().Result;
            var stored = fx.GitHub.Find(r1.Tag);
            if (cut == "before-write")
            {
                rows.Check(r1.ExitCode != 0 && !r1.Published, "C599 PublishRecovery before-write stays pending", r1.Refusal);
                rows.Check(stored is { IsDraft: true }, "C599 PublishRecovery before-write the release is still a draft");
            }
            else
            {
                rows.Check(r1.ExitCode == 0 && r1.Published, "C599 PublishRecovery a lost publish response is reconciled by readback", r1.Refusal);
                rows.Check(stored is { IsDraft: false }, "C599 PublishRecovery the recipient sees it published");
            }
            fx.GitHub.Clear("release-publish");
            var r2 = fx.Publish().Result;
            rows.Check(r2.ExitCode == 0 && r2.Published, "C599 PublishRecovery " + cut + " restart ends published", r2.Refusal);
            rows.Check(r2.Tag == r1.Tag, "C599 PublishRecovery " + cut + " recovery reuses the same tag", r1.Tag + " vs " + r2.Tag);
            var published = ReleaseGateJournal.Rows(ReleaseGateJournal.Read(fx.PublicationsPath)).Where(row => ReleaseGateJournal.Truthy(row["published"])).ToArray();
            rows.Check(published.Length == 1 && !string.IsNullOrWhiteSpace(ReleaseGateJournal.Str(published[0]["releaseId"])),
                "C599 PublishRecovery " + cut + " exactly one published reservation with a release id", published.Length.ToString());
        }
        rows.ShouldAllPass(10,
            "C599 PublishRecovery before-write stays pending",
            "C599 PublishRecovery before-write the release is still a draft",
            "C599 PublishRecovery a lost publish response is reconciled by readback",
            "C599 PublishRecovery before-write recovery reuses the same tag",
            "C599 PublishRecovery before-write exactly one published reservation with a release id");
    }

    [Test]
    public void C599_ManifestAllowlist()
    {
        var rows = new NightlyCaseRows();
        var manifest = ReleaseGatePublication.NewManifest(new Hashtable(StringComparer.OrdinalIgnoreCase) { ["repository"] = "r", ["tag"] = StampTag, ["sha"] = ReleaseGateFixture.ShaB });
        var check = ReleaseGatePublication.ManifestAllowlistCheck(manifest);
        rows.Check(check.Ok, "C599 ManifestAllowlist a built manifest carries only allowlisted keys", string.Join(",", check.Extra));
        var message = NightlyPlanCases.Refusal(() => ReleaseGatePublication.NewManifest(new Hashtable(StringComparer.OrdinalIgnoreCase) { ["hostname"] = "desktop", ["tag"] = "v1" })) ?? "";
        rows.Check(message.Contains("not allowlisted"), "C599 ManifestAllowlist a hostname field is refused", message);
        var bad = ReleaseGatePublication.ManifestAllowlistCheck(new OrderedDictionary { { "tag", "v1" }, { "transcript", "secret" } });
        rows.Check(!bad.Ok && bad.Extra.Contains("transcript"), "C599 ManifestAllowlist a transcript field is detected", string.Join(",", bad.Extra));
        var digest = ReleaseGatePublication.ManifestDigest(manifest);
        rows.Check(digest.Length == 64, "C599 ManifestAllowlist the manifest digest is a sha256", digest);
        rows.ShouldAllPass(4,
            "C599 ManifestAllowlist a built manifest carries only allowlisted keys",
            "C599 ManifestAllowlist a hostname field is refused",
            "C599 ManifestAllowlist a transcript field is detected",
            "C599 ManifestAllowlist the manifest digest is a sha256");
    }

    // ---- the command (unit 14) ---------------------------------------------------------------------------------------

    [Test]
    public void Publish_options_read_kebab_flags_the_api_variable_and_test_mode()
    {
        var scripts = ReleaseGateFixture.ScriptsRoot;
        var defaults = ReleasePublishOptions.Parse(["--candidate-id", "rc-1"], scripts, _ => null);
        defaults.CandidateId.ShouldBe("rc-1");
        defaults.ReleaseRoot.ShouldBe(@"C:\Antiphon\releases");
        defaults.Repository.ShouldBe("michal-ciechan/Antiphon");
        defaults.CheckoutRoot.ShouldBe("");
        defaults.AntiphonApiUrl.ShouldBe("");
        defaults.RequiredSuites.ShouldBeEmpty();
        defaults.WhatIf.ShouldBeFalse();
        defaults.TestMode.ShouldBeFalse();
        defaults.ScriptsRoot.ShouldBe(scripts);

        string? Variables(string name) => name switch { "ANTIPHON_API" => "http://api.invalid", ReleaseGateRemote.TestModeVariable => "1", _ => null };
        var full = ReleasePublishOptions.Parse(["--candidate-id", "rc-1", "--release-root", "R", "--checkout-root", "C", "--repository", "o/n", "--expected-policy-hash", "h",
            "--required-suites", "antiphon, e2e", "--what-if"], scripts, Variables);
        (full.ReleaseRoot, full.CheckoutRoot, full.Repository, full.ExpectedPolicyHash, full.WhatIf, full.TestMode, full.AntiphonApiUrl)
            .ShouldBe(("R", "C", "o/n", "h", true, true, "http://api.invalid"));
        full.RequiredSuites.ShouldBe(["antiphon", "e2e"]);
        ReleasePublishOptions.Parse(["--antiphon-api-url", "http://explicit.invalid"], scripts, Variables).AntiphonApiUrl.ShouldBe("http://explicit.invalid");
        // Test mode is the exact value '1'.
        foreach (var value in new[] { "", "0", "true", " 1", "1 " })
            ReleasePublishOptions.Parse([], scripts, name => name == ReleaseGateRemote.TestModeVariable ? value : null).TestMode.ShouldBeFalse(value);
        Should.Throw<NightlyUsageException>(() => ReleasePublishOptions.Parse(["-CandidateId", "x"], scripts, _ => null));
        Should.Throw<NightlyUsageException>(() => ReleasePublishOptions.Parse(["--candidate-id"], scripts, _ => null));
    }

    [Test]
    public void Test_mode_on_the_production_host_refuses_before_anything_is_read()
    {
        // No seam is bound, so test mode would reach the real remote: refused before any file, git or GitHub read.
        var output = new StringWriter();
        var result = ReleasePublish.Run(new ReleasePublishOptions { CandidateId = "rc-20260923T083000Z", ReleaseRoot = Path.Combine(Path.GetTempPath(), "c599-absent-" + Guid.NewGuid().ToString("N")), TestMode = true },
            ReleaseGateHost.Production(output));
        (result.ExitCode, result.Refusal, result.Published).ShouldBe((3, "test-mode-real-remote", false));
        output.ToString().Trim().ShouldBe("[publish-release] REFUSED: seam admission (test-mode-real-remote).");
        result.ToJson().ShouldBe("{\"candidateId\":\"rc-20260923T083000Z\",\"tag\":\"\",\"sha\":\"\",\"published\":false,\"releaseId\":\"\",\"releaseUrl\":\"\",\"refusal\":\"test-mode-real-remote\",\"exitCode\":3}");
        // A blank candidate refuses missing-candidate (exit 3), as -CandidateId '' did.
        using var fx = ReleaseGateFixture.Create(noAuthority: true);
        var blank = fx.Publish(o => o.CandidateId = "").Result;
        (blank.ExitCode, blank.Refusal).ShouldBe((3, "missing-candidate"));
        fx.Remote.Trace.ShouldBeEmpty();
    }

    [Test]
    public void Release_cut_publishes_through_the_compiled_publisher_once()
    {
        // release-cut.ps1 (still PowerShell, started by the Windmill job antiphon-release-candidates) runs the publisher's C#
        // in process from the core it loads: one call, no wrapper function, no script.
        var cut = File.ReadAllText(Path.Combine(ReleaseGateFixture.ScriptsRoot, "release-cut.ps1"));
        Regex.Matches(cut, Regex.Escape("[Antiphon.Nightly.ReleasePublish]::Run(")).Count.ShouldBe(1);
        cut.ShouldContain("[Antiphon.Nightly.ReleaseGateHost]::Production([Console]::Out)");
        cut.ShouldContain("ScriptsRoot = $PSScriptRoot");
        cut.ShouldNotContain("publish-release.ps1");
        File.Exists(Path.Combine(ReleaseGateFixture.ScriptsRoot, "publish-release.ps1")).ShouldBeFalse();
        File.Exists(Path.Combine(ReleaseGateFixture.ScriptsRoot, "promote-release.ps1")).ShouldBeFalse();
    }
}
