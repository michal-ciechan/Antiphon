using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1311 S3 and S4: the promoter (Antiphon.Nightly.ReleasePromote, the promote-release command) and the publisher on the
/// long-lived rc line, in process. Unit 14 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md deleted
/// scripts/promote-release.ps1 and publish-release.ps1; these cases replaced C1311_PreReleaseTag and C1311_MovingTipAuthority
/// of scripts/test-release-gate.ps1 (same rows; C1311_LongLivedRcRef, the coordinator's, is ReleaseCandidateTests since unit 13).
/// Two rows changed subject with the scripts: the "no tag delete" row reads ReleasePromote.cs where it read
/// promote-release.ps1, and "a second sha gets its own candidate root" checks the candidate identity
/// (ReleaseGateCredit.Candidate) the coordinator journals instead of running release-candidate.ps1, whose own journalling of
/// a moved rc tip is ReleaseCandidateTests' C1311_LongLivedRcRef row "an existing rc/2026-10-15 tip is journalled and not pushed".
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleasePromoteTests
{
    private static readonly DateTime LineDay = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static bool TraceClean(IEnumerable<string> trace)
    {
        var lines = trace.ToArray();
        return lines.Length > 0 && !lines.Any(l => l.Contains("--force") || Regex.IsMatch(l, @"\+refs/") || l.Contains("tag -d") || Regex.IsMatch(l, "push.*--delete")
            || Regex.IsMatch(l, @"\s:refs/tags"));
    }

    private static string FinalOf(string preTag) => Regex.Replace(preTag, "-rc\\.[0-9]+$", "");

    /// <summary>
    /// Invoke-C1311Promote -Reserve: a long-lived candidate with the real pinned authority, its published reservation, the
    /// pre-release tag on the remote at the tested SHA and (seedGitHub) its GitHub pre-release; the clock on the line's day.
    /// </summary>
    private static ReleaseGateFixture Reserved(bool seedGitHub)
    {
        var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes(), longLived: true);
        var journals = ReleaseGateJournal.Production();
        var reserved = journals.Reserve(fx.PublicationsPath, fx.CandidateId, fx.Head, new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc), []);
        journals.MarkPublished(fx.PublicationsPath, fx.CandidateId, "R-pre", "https://example/releases/tag/pre");
        fx.Remote.Set("refs/tags/" + reserved.Tag, fx.Head);
        if (seedGitHub)
            fx.GitHub.Seed(new FakeReleaseGitHub.StoredRelease
            {
                Id = "R-pre", Name = reserved.Tag, TagName = reserved.Tag, Url = "https://example/releases/tag/pre", IsDraft = false, Assets = ["release-manifest.json"],
            });
        fx.ClockSource = () => LineDay;
        return fx;
    }

    /// <summary>
    /// Invoke-C1311Promote without -Reserve: a long-lived candidate credited at creditSha (no authority: these refuse first),
    /// the live tip at refTip, the pre-release tag peeling to remotePeel unless skipRemote, plus extra remote refs.
    /// </summary>
    private static ReleaseGateRun<ReleasePromoteResult> Unreserved(string preTag, string remotePeel, string creditSha, string refTip, bool skipRemote,
        (string Reference, string Sha)[] extra, out ReleaseGateFixture fixture)
    {
        var fx = ReleaseGateFixture.Create(ReleaseGateFixture.BasePolicyBytes(), noAuthority: true, longLived: true);
        fixture = fx;
        var id = "rc-2026-10-15-" + ReleaseGateFixture.ShaB[..12];
        fx.Remote.HeadSha = ReleaseGateFixture.ShaB;
        var refs = new List<(string, string)> { (ReleaseGateFixture.LongLivedRef, refTip) };
        if (!skipRemote) refs.Add(("refs/tags/" + preTag, remotePeel));
        refs.AddRange(extra);
        fx.Remote.Replace(refs.ToArray());
        fx.NewRcGreen(creditSha, id, ReleaseGateFixture.LongLivedRef, "policy-1");
        fx.ClockSource = () => LineDay;
        return fx.Promote(preTag, candidateId: id);
    }

    [Test]
    public void C1311_PreReleaseTag()
    {
        // D-3/D-4: promote writes the final tag beside the pre-release; stamp publication stays a final tag without
        // --prerelease. The tag parse and the long-lived proposal rows run in process (ReleaseGateLibraryTests).
        var rows = new NightlyCaseRows();
        using var happy = Reserved(seedGitHub: true);
        var preTag = ReleaseGateJournal.Rows(ReleaseGateJournal.Read(happy.PublicationsPath)).Select(r => ReleaseGateJournal.Str(r["tag"])).Single();
        var expectedFinal = FinalOf(preTag);
        var promoted = happy.Promote(preTag).Result;
        var finalSha = happy.Remote.Read("refs/tags/" + expectedFinal);
        ReleasePromoteResult? again = null;
        var secondTrace = Array.Empty<string>();
        if (promoted.ExitCode == 0)
        {
            var before = happy.Remote.Trace.Count;
            again = happy.Promote(preTag).Result;
            secondTrace = happy.Remote.Trace.Skip(before).ToArray();
        }
        var journalFinal = ReleaseGateJournal.Rows(ReleaseGateJournal.Read(happy.PublicationsPath))
            .Where(r => ReleaseGateJournal.Str(r["candidateId"]) == happy.CandidateId).Select(r => ReleaseGateJournal.Str(r["finalTag"])).LastOrDefault() ?? "";
        rows.Check(promoted.ExitCode == 0 && promoted.Promoted && promoted.Tag == expectedFinal && promoted.Sha == happy.Head && finalSha == happy.Head
                && happy.Remote.Read(ReleaseGateFixture.LongLivedRef) == happy.Head && again is { ExitCode: 0, Resumed: true }
                && !secondTrace.Any(l => l.StartsWith("GIT push", StringComparison.Ordinal) || l.StartsWith("GIT tag", StringComparison.Ordinal)) && journalFinal == expectedFinal,
            "C1311 PreReleaseTag promote creates the final tag at the peeled sha",
            "exit=" + promoted.ExitCode + " refusal=" + promoted.Refusal + " tag=" + promoted.Tag + " pre=" + preTag + " finalSha=" + finalSha + " journal=" + journalFinal
            + " again=" + again?.ExitCode + "/" + again?.Resumed);

        var preLeft = happy.Remote.Read("refs/tags/" + preTag);
        var ghLeft = happy.GitHub.Find(preTag);
        rows.Check(Regex.IsMatch(preTag, "-rc\\.[1-9][0-9]*$") && preLeft == happy.Head && ghLeft?.TagName == preTag && finalSha == happy.Head,
            "C1311 PreReleaseTag promote leaves the pre-release tag in place", "pre=" + preTag + "/" + preLeft + " gh=" + ghLeft?.TagName);

        const string pre = "v2026.10.15.1-rc.1";
        var b = ReleaseGateFixture.ShaB;
        var conflict = Unreserved(pre, b, b, b, false, [("refs/tags/v2026.10.15.1", ReleaseGateFixture.ShaA)], out var conflictFx);
        var missing = Unreserved(pre, b, b, b, true, [], out var missingFx);
        var shortPeel = Unreserved(pre, "abcd", b, b, false, [], out var shortFx);
        var mismatch = Unreserved(pre, b, ReleaseGateFixture.ShaC, b, false, [], out var mismatchFx);
        using (conflictFx)
        using (missingFx)
        using (shortFx)
        using (mismatchFx)
        {
            var conflictTrace = conflictFx.Remote.Trace;
            rows.Check(conflict.Result.Refusal == "final-tag-conflict" && conflict.Result.ExitCode == 3
                    && conflictFx.Remote.Read("refs/tags/v2026.10.15.1") == ReleaseGateFixture.ShaA && conflictFx.Remote.Read("refs/tags/" + pre) == b
                    && !conflictTrace.Any(l => l.StartsWith("GIT push", StringComparison.Ordinal) || l.Contains("--force"))
                    && missing.Result.Refusal == "prerelease-missing" && shortPeel.Result.Refusal == "tag-peel" && mismatch.Result.Refusal == "credited-sha-mismatch"
                    && missingFx.Remote.Read("refs/tags/v2026.10.15.1") == "" && shortFx.Remote.Read("refs/tags/v2026.10.15.1") == ""
                    && mismatchFx.Remote.Read("refs/tags/v2026.10.15.1") == "",
                "C1311 PreReleaseTag promote refuses a final tag at another sha",
                "conflict=" + conflict.Result.Refusal + " missing=" + missing.Result.Refusal + " short=" + shortPeel.Result.Refusal + " mismatch=" + mismatch.Result.Refusal);
        }

        bool stampPublished, longPublished, longTagOk;
        int stampCreates, stampPre, longCreates, longPre;
        IReadOnlyList<string> stampTrace, longTrace;
        using (var stamp = ReleaseGateFixture.Create())
        {
            var published = stamp.Publish().Result;
            stampPublished = published.ExitCode == 0 && published.Published && published.Tag == "v2026.09.23.1";
            var creates = stamp.GitHub.Trace.Where(l => l.StartsWith("GH release-create", StringComparison.Ordinal)).ToArray();
            stampCreates = creates.Length;
            stampPre = creates.Count(l => l.Contains("--prerelease"));
            stampTrace = stamp.Remote.Trace;
        }
        using (var line = ReleaseGateFixture.Create(longLived: true))
        {
            var published = line.Publish().Result;
            var parsed = ReleaseGateTags.Tag(published.Tag);
            longPublished = published.ExitCode == 0 && published.Published;
            longTagOk = longPublished && parsed.Ok && parsed.Prerelease && line.Remote.Read("refs/tags/" + published.Tag) == line.Head
                && line.Remote.Read(ReleaseGateFixture.LongLivedRef) == line.Head;
            var creates = line.GitHub.Trace.Where(l => l.StartsWith("GH release-create", StringComparison.Ordinal)).ToArray();
            longCreates = creates.Length;
            longPre = creates.Count(l => l.Contains("--prerelease"));
            longTrace = line.Remote.Trace;
        }
        var promoterText = File.ReadAllText(Path.Combine(ReleaseGateFixture.ScriptsRoot, "lib", "Antiphon.Nightly", "ReleasePromote.cs"));
        var promoteQuiet = new[] { "restart-apphost", "Stop-Process", "taskkill", "deploy-local", "--force", "tag -d" }
            .All(word => !promoterText.Contains(word, StringComparison.OrdinalIgnoreCase));
        var promoteTrace = happy.Remote.Trace;
        rows.Check(stampPublished && stampCreates >= 1 && stampPre == 0 && TraceClean(stampTrace) && stampTrace.Any(l => l.StartsWith("GIT push", StringComparison.Ordinal))
                && longPublished && longTagOk && longCreates >= 1 && longPre == longCreates && TraceClean(longTrace) && longTrace.Any(l => l.StartsWith("GIT push", StringComparison.Ordinal))
                && TraceClean(promoteTrace) && promoteTrace.Any(l => l.StartsWith("GIT push", StringComparison.Ordinal)) && promoteQuiet
                && !TraceClean(["GIT push origin :refs/tags/v2026.10.15.1"]) && !TraceClean(["GIT push origin --delete refs/tags/v2026.10.15.1"])
                && !TraceClean(["GIT push origin --force refs/tags/v2026.10.15.1"]) && !TraceClean(["GIT push origin +refs/tags/v2026.10.15.1:refs/tags/v2026.10.15.1"]),
            "C1311 PreReleaseTag no tag delete and no force push",
            "stamp=" + stampPublished + "/" + stampCreates + "/" + stampPre + " long=" + longPublished + "/" + longTagOk + "/" + longCreates + "/" + longPre + " quiet=" + promoteQuiet);
        rows.ShouldAllPass(4,
            "C1311 PreReleaseTag promote creates the final tag at the peeled sha",
            "C1311 PreReleaseTag promote leaves the pre-release tag in place",
            "C1311 PreReleaseTag promote refuses a final tag at another sha",
            "C1311 PreReleaseTag no tag delete and no force push");
    }

    [Test]
    public void C1311_MovingTipAuthority()
    {
        // D-2/D-5/D-6: a second SHA of the same rc ref gets its own candidate root. The first authority and tag stay on the
        // first SHA. Publish and promote of that SHA refuse remote-candidate-moved once the live tip has moved. scriptHashes
        // bind the pinned script files and source trees, never the moving tip.
        var rows = new NightlyCaseRows();
        using var fx = ReleaseGateFixture.Create(longLived: true);
        var firstSha = fx.Head;
        var secondSha = ReleaseGateFixture.ShaC;
        var firstId = fx.CandidateId;
        var secondId = "rc-2026-10-15-" + secondSha[..12];
        var firstRoot = fx.CandidateRoot;
        var authPath = fx.AuthorityPath;
        var published = fx.Publish(candidateId: firstId).Result;
        var firstTag = published.Tag;
        var authBefore = File.Exists(authPath) ? ReleaseAuthority.FileSha256(authPath) : "";
        var authDoc = File.Exists(authPath) ? ReleaseGateFixture.ReadObject(authPath) : null;

        // The live tip moves to a second SHA: its candidate identity (what the coordinator journals) is a new id and root.
        fx.Remote.Set(ReleaseGateFixture.LongLivedRef, secondSha);
        var second = ReleaseGateCredit.Candidate(ReleaseGateFixture.LongLivedRef, secondSha);
        var secondRoot = ReleaseGateCredit.CandidateRoot(fx.ReleaseRoot, second.CandidateId);
        var firstJournalSha = ReleaseGateFixture.Str(ReleaseGateFixture.ReadObject(fx.JournalPath)["sha"]);
        rows.Check(second.Ok && second.CandidateId == secondId && second.Ref == ReleaseGateFixture.LongLivedRef
                && !string.Equals(Path.GetFullPath(secondRoot), Path.GetFullPath(firstRoot), StringComparison.OrdinalIgnoreCase) && firstJournalSha == firstSha && secondId != firstId,
            "C1311 MovingTipAuthority a second sha gets its own candidate root", "id=" + second.CandidateId + " reason=" + second.Reason + " first=" + firstJournalSha);

        var authAfter = File.Exists(authPath) ? ReleaseAuthority.FileSha256(authPath) : "";
        var authSha = File.Exists(authPath) ? ReleaseGateFixture.Str(ReleaseGateFixture.ReadObject(authPath)["sha"]) : "";
        rows.Check(authBefore.Length > 0 && authAfter == authBefore && authSha == firstSha && authSha != secondSha,
            "C1311 MovingTipAuthority the first authority.json still names the first sha", "sha=" + authSha + " hash=" + authBefore + "/" + authAfter);

        var linesBefore = fx.Remote.Trace.Count;
        var republish = fx.Publish(candidateId: firstId).Result;
        var republishTrace = fx.Remote.Trace.Skip(linesBefore).ToArray();
        rows.Check(republish.ExitCode == 3 && !republish.Published && republish.Refusal.Contains("remote-candidate-moved")
                && !republishTrace.Any(l => l.StartsWith("GIT tag", StringComparison.Ordinal) || l.StartsWith("GIT push", StringComparison.Ordinal)),
            "C1311 MovingTipAuthority publish of the first sha refuses remote-candidate-moved", "exit=" + republish.ExitCode + " refusal=" + republish.Refusal);

        var stillPeeled = firstTag.Length > 0 ? fx.Remote.Read("refs/tags/" + firstTag) : "";
        rows.Check(published.ExitCode == 0 && published.Published && Regex.IsMatch(firstTag, "-rc\\.[1-9][0-9]*$") && stillPeeled == firstSha,
            "C1311 MovingTipAuthority the first tag still peels to the first sha", "tag=" + firstTag + " peel=" + stillPeeled + " published=" + published.ExitCode + "/" + published.Refusal);

        var promoteLines = fx.Remote.Trace.Count;
        var ghLines = fx.GitHub.Trace.Count;
        var promoted = fx.Promote(firstTag, candidateId: firstId).Result;
        var promoteTrace = fx.Remote.Trace.Skip(promoteLines).ToArray();
        var ghTail = fx.GitHub.Trace.Skip(ghLines).ToArray();
        var finalName = firstTag.Length > 0 ? FinalOf(firstTag) : "";
        var finalPeeled = finalName.Length > 0 ? fx.Remote.Read("refs/tags/" + finalName) : "";
        var preStill = firstTag.Length > 0 ? fx.Remote.Read("refs/tags/" + firstTag) : "";
        var finalRecorded = ReleaseGateJournal.Rows(ReleaseGateJournal.Read(fx.PublicationsPath))
            .Where(r => ReleaseGateJournal.Str(r["candidateId"]) == firstId && r.ContainsKey("finalTag")).Select(r => ReleaseGateJournal.Str(r["finalTag"])).LastOrDefault() ?? "";
        rows.Check(promoted.Refusal == "remote-candidate-moved" && promoted.ExitCode == 3 && !promoted.Promoted && finalPeeled == "" && finalRecorded == "" && preStill == firstSha
                && !promoteTrace.Any(l => l.StartsWith("GIT tag", StringComparison.Ordinal) || l.StartsWith("GIT push", StringComparison.Ordinal) || l.Contains("--force"))
                && ghTail.Length == 0,
            "C1311 MovingTipAuthority promote of a moved tip refuses remote-candidate-moved",
            "exit=" + promoted.ExitCode + " refusal=" + promoted.Refusal + " final=" + finalName + "/" + finalPeeled + " recorded=" + finalRecorded + " pre=" + preStill);

        // Unit 14: the publisher and promoter are C# in lib/Antiphon.Nightly, so the authority binds the pinned scripts and two
        // source trees (publish-release.ps1 and promote-release.ps1 are deleted; unit 62 deleted lib/nightly-common.ps1: three keys).
        var want = ReleaseAuthority.Scripts.Concat(ReleaseAuthority.SourceTrees).ToArray();
        var hashes = authDoc?["scriptHashes"]?.AsObject();
        var have = hashes?.Select(p => p.Key).ToArray() ?? [];
        var fresh = ReleaseAuthority.ScriptHashes(ReleaseGateFixture.ScriptsRoot);
        var same = want.Length == 3 && have.Length == want.Length && have.All(want.Contains);
        var omitsTip = true;
        foreach (var rel in want)
        {
            var got = hashes != null && hashes.ContainsKey(rel) ? ReleaseGateFixture.Str(hashes[rel]) : "";
            if (got.Length == 0 || got != (string?)fresh[rel]) same = false;
            if (got.Contains(firstSha) || got.Contains(secondSha) || rel == firstSha || rel == secondSha) omitsTip = false;
        }
        rows.Check(same && omitsTip && authDoc != null && ReleaseGateFixture.Str(authDoc["sha"]) == firstSha, "C1311 MovingTipAuthority scriptHashes omit the branch tip",
            "keys=" + string.Join(",", have) + " same=" + same + " omits=" + omitsTip);
        rows.ShouldAllPass(6,
            "C1311 MovingTipAuthority a second sha gets its own candidate root",
            "C1311 MovingTipAuthority the first authority.json still names the first sha",
            "C1311 MovingTipAuthority publish of the first sha refuses remote-candidate-moved",
            "C1311 MovingTipAuthority the first tag still peels to the first sha",
            "C1311 MovingTipAuthority promote of a moved tip refuses remote-candidate-moved",
            "C1311 MovingTipAuthority scriptHashes omit the branch tip");
    }

    [Test]
    public void Promote_options_read_kebab_flags_and_the_api_variable()
    {
        var scripts = ReleaseGateFixture.ScriptsRoot;
        var options = ReleasePromoteOptions.Parse(["--tag", "v2026.10.15.1-rc.1", "--candidate-id", "rc-2026-10-15-bbbbbbbbbbbb", "--what-if"], scripts,
            name => name == "ANTIPHON_API" ? "http://api.invalid" : null);
        (options.Tag, options.CandidateId, options.WhatIf, options.AntiphonApiUrl, options.Repository, options.ReleaseRoot, options.TestMode)
            .ShouldBe(("v2026.10.15.1-rc.1", "rc-2026-10-15-bbbbbbbbbbbb", true, "http://api.invalid", "michal-ciechan/Antiphon", @"C:\Antiphon\releases", false));
        Should.Throw<NightlyUsageException>(() => ReleasePromoteOptions.Parse(["--expected-policy-hash", "h"], scripts, _ => null));
        // A tag that is not a pre-release refuses its shape before anything remote is read.
        using var fx = ReleaseGateFixture.Create(noAuthority: true, longLived: true);
        foreach (var (tag, reason) in new[] { ("v2026.10.15.1", "tag-shape"), ("v2026.02.30.1-rc.1", "tag-date"), ("", "empty-tag") })
        {
            var r = fx.Promote(tag).Result;
            (r.ExitCode, r.Refusal).ShouldBe((3, reason), tag);
        }
        fx.Remote.Trace.ShouldBeEmpty();
        fx.Promote("v2026.10.15.1-rc.1", candidateId: "").Result.Refusal.ShouldBe("missing-candidate");
    }
}
