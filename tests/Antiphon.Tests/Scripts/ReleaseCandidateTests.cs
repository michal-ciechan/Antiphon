using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 D-3 and CARD-1311 S2: the release candidate (Antiphon.Nightly.ReleaseCandidate, the release-candidate command)
/// in process, over an in-memory fake remote. Unit 13 of 2026-11-22-remaining-ps1-to-csharp.md deleted
/// scripts/release-candidate.ps1; these cases replaced the rows of scripts/test-release-gate.ps1 that ran it:
/// C599_CandidateIdentity and C599_CutRecovery (formerly run by ReleaseGatePublicationTests) and C1311_LongLivedRcRef
/// (formerly run by ReleaseBranchModelGateTests; both classes went once unit 14 moved their other cases), with their row names. The coordinator rows of C599_CloneOwnership are
/// ReleaseGateRunTests.C599_CloneOwnership, LP_CloneFlag is LongPathsCheckoutScriptTests.LP_CloneFlag and the real-git
/// candidate reads (C1339 G-7..G-9) and the command line are ReleaseCandidateGitTests. A separate observer read of the fake
/// remote - never the candidate's own answer - is what proves recipient state.
/// </summary>
[Category("Unit")]
public sealed class ReleaseCandidateTests
{
    private const string CandidateRef = ReleaseCandidateCases.CandidateRef;
    private const string CandidateId = ReleaseCandidateCases.CandidateId;
    private static readonly string ShaA = new('a', 40);
    private static readonly string ShaB = new('b', 40);
    private static readonly string ShaC = new('c', 40);

    [Test]
    public void C599_CandidateIdentity()
    {
        using var fx = NightlyFixture.Create("c599-candidate");
        var rows = new NightlyCaseRows();
        var git = new FakeCandidateRemote(ShaB);
        var releaseRoot = fx.PathOf("releases");
        var checkout = ReleaseCandidateCases.OwnedClone(fx);

        var cut = ReleaseCandidateCases.Cut(fx, git, releaseRoot, checkout, CandidateRef);
        rows.Check(cut.ExitCode == 0 && cut.Pushed, "C599 CandidateIdentity control: a candidate is cut and pushed", cut.Refusal);
        rows.Check(git.Remote(CandidateRef) == ShaB, "C599 CandidateIdentity an observer read finds the candidate at the pinned sha", git.Remote(CandidateRef));
        var journal = ReleaseCandidateCases.Journal(cut.JournalPath);
        rows.Check(Str(journal, "sha") == ShaB && Str(journal, "ref") == CandidateRef, "C599 CandidateIdentity the journal pins one ref and one sha", Str(journal, "sha"));

        // Master moved: a resume must NOT re-pin.
        var moved = new FakeCandidateRemote(ShaC);
        foreach (var (name, sha) in git.Refs) moved.Refs[name] = sha;
        var resume = ReleaseCandidateCases.Cut(fx, moved, releaseRoot, checkout, CandidateRef);
        rows.Check(resume.Sha == ShaB, "C599 CandidateIdentity a resume keeps the original pin even after master moved", resume.Sha);
        rows.Check(resume.Resumed, "C599 CandidateIdentity the resume is reported as a resume");
        rows.Check(!moved.Trace.Any(line => line.StartsWith("GIT fetch", StringComparison.Ordinal)), "C599 CandidateIdentity a resume performs no fetch", string.Join("\n", moved.Trace));

        // A remote candidate at a different sha is a hard refusal, and nothing moves.
        const string other = "release/rc-20260923T160000Z";
        git.Refs[other] = ShaA;
        var conflict = ReleaseCandidateCases.Cut(fx, git, releaseRoot, checkout, other);
        rows.Check(conflict.Refusal == "remote-candidate-conflict", "C599 CandidateIdentity a remote candidate at another sha refuses", conflict.Refusal);
        rows.Check(git.Remote(other) == ShaA, "C599 CandidateIdentity the conflicting remote ref was not moved", git.Remote(other));

        // Ref-shape negatives.
        foreach (var bad in new[] { "release/current", "master", "release/rc-2026", "refs/tags/v1" })
        {
            var r = ReleaseCandidateCases.Cut(fx, git, releaseRoot, checkout, bad);
            rows.Check(r.ExitCode == 3, "C599 CandidateIdentity refuses ref " + bad, r.Refusal);
        }

        // No push argument may carry a force flag.
        var all = string.Join("\n", git.Trace);
        rows.Check(!all.Contains("--force", StringComparison.Ordinal) && !all.Contains("+refs/", StringComparison.Ordinal), "C599 CandidateIdentity no push used force or a plus refspec", all);
        rows.ShouldAllPass(13, "C599 CandidateIdentity control: a candidate is cut and pushed", "C599 CandidateIdentity an observer read finds the candidate at the pinned sha",
            "C599 CandidateIdentity a resume keeps the original pin even after master moved", "C599 CandidateIdentity a resume performs no fetch",
            "C599 CandidateIdentity a remote candidate at another sha refuses", "C599 CandidateIdentity the conflicting remote ref was not moved",
            "C599 CandidateIdentity refuses ref release/current", "C599 CandidateIdentity no push used force or a plus refspec");
    }

    [Test]
    public void C599_CutRecovery()
    {
        var rows = new NightlyCaseRows();
        foreach (var cut in new[] { "before-push", "commit-then-lose-response" })
        {
            using var fx = NightlyFixture.Create("c599-cut-recovery");
            var git = new FakeCandidateRemote(ShaB);
            var releaseRoot = fx.PathOf("releases");
            var checkout = ReleaseCandidateCases.OwnedClone(fx);
            git.Fail("push", commitsAnyway: cut != "before-push");

            var r1 = ReleaseCandidateCases.Cut(fx, git, releaseRoot, checkout, CandidateRef);
            var observed = git.Remote(CandidateRef);
            if (cut == "before-push")
            {
                rows.Check(r1.Refusal == "push-failed", "C599 CutRecovery before-push reports a failed push", r1.Refusal);
                rows.Check(observed.Length == 0, "C599 CutRecovery before-push left the remote untouched", observed);
            }
            else
            {
                rows.Check(r1.ExitCode == 0 && r1.Pushed, "C599 CutRecovery a lost response is reconciled from the remote", r1.Refusal);
                rows.Check(observed == ShaB, "C599 CutRecovery the remote holds exactly the pinned sha", observed);
            }
            rows.Check(File.Exists(Path.Combine(ReleaseGateCredit.CandidateRoot(releaseRoot, CandidateId), "candidate.json")),
                "C599 CutRecovery " + cut + " wrote the journal before the remote write");

            // Restart from disk: the same identity resumes, never a second candidate.
            git.Clear("push");
            var r2 = ReleaseCandidateCases.Cut(fx, git, releaseRoot, checkout, CandidateRef);
            rows.Check(r2.ExitCode == 0 && r2.Sha == ShaB, "C599 CutRecovery " + cut + " restart resumes the same sha", r2.Sha);
            rows.Check(git.Remote(CandidateRef) == ShaB, "C599 CutRecovery " + cut + " ends with exactly one candidate at the pinned sha");
        }

        // A fetch failure before any pin writes nothing at all.
        using (var fx = NightlyFixture.Create("c599-cut-fetch"))
        {
            var git = new FakeCandidateRemote(ShaB);
            git.Fail("fetch");
            var r = ReleaseCandidateCases.Cut(fx, git, fx.PathOf("releases"), ReleaseCandidateCases.OwnedClone(fx), CandidateRef);
            rows.Check(r.Refusal == "fetch-failed", "C599 CutRecovery a fetch failure refuses", r.Refusal);
            rows.Check(git.Remote(CandidateRef).Length == 0, "C599 CutRecovery a fetch failure leaves the remote untouched");
        }
        rows.ShouldAllPass(12, "C599 CutRecovery before-push reports a failed push", "C599 CutRecovery before-push left the remote untouched",
            "C599 CutRecovery a lost response is reconciled from the remote", "C599 CutRecovery the remote holds exactly the pinned sha",
            "C599 CutRecovery before-push restart resumes the same sha", "C599 CutRecovery a fetch failure leaves the remote untouched");
    }

    /// <summary>D-1/D-2: rc/YYYY-MM-DD is a candidate whose id is one per tested SHA; a legacy stamp still parses and still earns rc-release.</summary>
    [Test]
    public void C1311_LongLivedRcRef()
    {
        var rows = new NightlyCaseRows();
        var accepted = ReleaseGateCredit.Candidate("rc/2026-10-15", "");
        rows.Check(accepted.Ok && accepted.Ref == "rc/2026-10-15", "C1311 LongLivedRcRef rc/2026-10-15 is accepted", accepted.Reason);
        var prefixed = ReleaseGateCredit.Candidate("refs/heads/rc/2026-10-15", "");
        rows.Check(prefixed.Ok && prefixed.Ref == "rc/2026-10-15", "C1311 LongLivedRcRef refs/heads/rc/2026-10-15 normalizes to rc/2026-10-15", prefixed.Ref);
        var prefix = ShaB[..12];
        var withSha = ReleaseGateCredit.Candidate("rc/2026-10-15", ShaB);
        rows.Check(withSha.CandidateId == "rc-2026-10-15-" + prefix, "C1311 LongLivedRcRef candidate id is rc-2026-10-15-" + prefix, withSha.CandidateId);
        var badDate = ReleaseGateCredit.Candidate("rc/2026-13-01", "");
        rows.Check(!badDate.Ok && badDate.Reason == "ref-date", "C1311 LongLivedRcRef rc/2026-13-01 refuses ref-date", badDate.Reason);
        var badShape = ReleaseGateCredit.Candidate("rc/20261015", "");
        rows.Check(!badShape.Ok && badShape.Reason == "ref-shape", "C1311 LongLivedRcRef rc/20261015 refuses ref-shape", badShape.Reason);
        var stamp = ReleaseGateCredit.Candidate(CandidateRef, "");
        rows.Check(stamp.Ok && stamp.CandidateId == CandidateId && stamp.Ref == CandidateRef, "C1311 LongLivedRcRef legacy stamp release/rc-20260923T083000Z is accepted", stamp.Reason);
        var longCredit = ReleaseGateCredit.CreditKind("rc", "rc", "rc/2026-10-15");
        rows.Check(longCredit == "rc-release", "C1311 LongLivedRcRef credit rc/rc/rc/2026-10-15 earns rc-release", longCredit);
        var stampCredit = ReleaseGateCredit.CreditKind("rc", "rc", CandidateRef);
        rows.Check(stampCredit == "rc-release", "C1311 LongLivedRcRef credit rc/rc/release/rc-20260923T083000Z earns rc-release", stampCredit);

        using (var fx = NightlyFixture.Create("c1311-omitted"))
        {
            var git = new FakeCandidateRemote(ShaB);
            var omitted = ReleaseCandidateCases.Cut(fx, git, fx.PathOf("releases"), ReleaseCandidateCases.OwnedClone(fx), "");
            rows.Check(omitted.Refusal == "rc-ref-required" && omitted.ExitCode == 3 && !omitted.Pushed && !git.Trace.Any(line => line.StartsWith("GIT push", StringComparison.Ordinal)),
                "C1311 LongLivedRcRef omitted Ref refuses rc-ref-required", omitted.Refusal);
        }

        // The remote tip is ShaB. rev-parse would answer ShaC, so a master pin is visible.
        using (var fx = NightlyFixture.Create("c1311-live"))
        {
            var git = new FakeCandidateRemote(ShaC);
            git.Refs["rc/2026-10-15"] = ShaB;
            var live = ReleaseCandidateCases.Cut(fx, git, fx.PathOf("releases"), ReleaseCandidateCases.OwnedClone(fx), "rc/2026-10-15");
            var journal = File.Exists(live.JournalPath) ? ReleaseCandidateCases.Journal(live.JournalPath) : new JsonObject();
            rows.Check(live.ExitCode == 0 && !live.Pushed && Str(journal, "sha") == ShaB && Str(journal, "ref") == "rc/2026-10-15"
                    && Str(journal, "candidateId") == "rc-2026-10-15-" + prefix && git.Remote("rc/2026-10-15") == ShaB
                    && !git.Trace.Any(line => line.StartsWith("GIT push", StringComparison.Ordinal)),
                "C1311 LongLivedRcRef an existing rc/2026-10-15 tip is journalled and not pushed",
                "exit=" + live.ExitCode + " refusal=" + live.Refusal + " sha=" + Str(journal, "sha") + " id=" + Str(journal, "candidateId"));
        }
        rows.ShouldAllPass(10, "C1311 LongLivedRcRef omitted Ref refuses rc-ref-required", "C1311 LongLivedRcRef an existing rc/2026-10-15 tip is journalled and not pushed");
    }

    /// <summary>The journal the script wrote: its fields in order, the cut instant, the host's clock, pushed set only after the remote holds the SHA.</summary>
    [Test]
    public void The_journal_records_the_pin_before_the_push_and_the_progress_lines_go_to_the_host()
    {
        using var fx = NightlyFixture.Create("c599-candidate-journal");
        var git = new FakeCandidateRemote(ShaB);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 23, 8, 31, 0, TimeSpan.Zero));
        var journalWhenPushed = "";
        var releaseRoot = fx.PathOf("releases");
        git.OnPush = () => journalWhenPushed = File.ReadAllText(Path.Combine(ReleaseGateCredit.CandidateRoot(releaseRoot, CandidateId), "candidate.json"));
        var cut = ReleaseCandidateCases.Cut(fx, git, releaseRoot, ReleaseCandidateCases.OwnedClone(fx), CandidateRef,
            o => { o.ScheduleSlot = "u/lndcobra/antiphon_release_candidates"; o.RemoteUrl = "https://example.invalid/antiphon.git"; }, clock);

        cut.ExitCode.ShouldBe(0, cut.Refusal);
        cut.Pushed.ShouldBeTrue();
        cut.Resumed.ShouldBeFalse();
        cut.JournalPath.ShouldBe(Path.Combine(ReleaseGateCredit.CandidateRoot(releaseRoot, CandidateId), "candidate.json"));
        var journal = ReleaseCandidateCases.Journal(cut.JournalPath);
        journal.Select(p => p.Key).ToArray().ShouldBe(new[] { "schemaVersion", "candidateId", "ref", "sha", "sourceRef", "remoteUrl", "scheduleSlot", "cutUtc", "createdAt", "pushed" });
        journal["schemaVersion"]!.GetValue<int>().ShouldBe(1);
        Str(journal, "candidateId").ShouldBe(CandidateId);
        Str(journal, "sourceRef").ShouldBe("master");
        Str(journal, "remoteUrl").ShouldBe("https://example.invalid/antiphon.git");
        Str(journal, "scheduleSlot").ShouldBe("u/lndcobra/antiphon_release_candidates");
        Str(journal, "cutUtc").ShouldBe("2026-09-23T08:30:00.0000000Z");
        Str(journal, "createdAt").ShouldBe("2026-09-23T08:31:00.0000000Z");
        journal["pushed"]!.GetValue<bool>().ShouldBeTrue();
        // Journal before the remote write: the push saw the pinned, not yet pushed, journal.
        var before = JsonNode.Parse(journalWhenPushed)!.AsObject();
        Str(before, "sha").ShouldBe(ShaB);
        before["pushed"]!.GetValue<bool>().ShouldBeFalse();
        git.Trace.ToArray().ShouldBe(new[] { "GIT fetch origin master", "GIT rev-parse FETCH_HEAD", "GIT ls-remote --heads origin refs/heads/" + CandidateRef,
            "GIT push origin " + ShaB + ":refs/heads/" + CandidateRef });
        fx.Out.ToString().ShouldBe("[release-candidate] cut " + CandidateRef + " at " + ShaB + Environment.NewLine);
        cut.ToJson().ShouldBe("{\"candidateId\":\"" + CandidateId + "\",\"ref\":\"" + CandidateRef + "\",\"sha\":\"" + ShaB
            + "\",\"pushed\":true,\"resumed\":false,\"refusal\":\"\",\"exitCode\":0}");
    }

    [Test]
    public void A_resume_keeps_the_journalled_pushed_flag_until_the_remote_is_reread()
    {
        using var fx = NightlyFixture.Create("c599-candidate-resume");
        var git = new FakeCandidateRemote(ShaB);
        var releaseRoot = fx.PathOf("releases");
        var checkout = ReleaseCandidateCases.OwnedClone(fx);
        ReleaseCandidateCases.Cut(fx, git, releaseRoot, checkout, CandidateRef).Pushed.ShouldBeTrue();
        var journalPath = Path.Combine(ReleaseGateCredit.CandidateRoot(releaseRoot, CandidateId), "candidate.json");
        var seen = "";
        git.OnLsRemote = () => seen = File.ReadAllText(journalPath);
        git.Fail("ls-remote");
        var unresolved = ReleaseCandidateCases.Cut(fx, git, releaseRoot, checkout, CandidateRef);
        unresolved.ExitCode.ShouldBe(3);
        unresolved.Refusal.ShouldBe("remote-candidate-unresolved");
        unresolved.Resumed.ShouldBeTrue();
        // The refusal came before any write: the journal still says pushed.
        ReleaseCandidateCases.Journal(journalPath)["pushed"]!.GetValue<bool>().ShouldBeTrue();
        seen.ShouldNotBeEmpty();
        fx.Out.ToString().ShouldContain("[release-candidate] resuming candidate " + CandidateId + " at " + ShaB);
        fx.Out.ToString().ShouldContain("[release-candidate] REFUSED: remote candidate could not be resolved.");
    }

    [Test]
    public void Stamp_refusals_and_failures_keep_their_exit_codes_and_write_nothing_remote()
    {
        // A FETCH_HEAD that is not a full sha.
        using (var fx = NightlyFixture.Create("c599-candidate-sha"))
        {
            var git = new FakeCandidateRemote("abcd");
            var r = ReleaseCandidateCases.Cut(fx, git, fx.PathOf("releases"), ReleaseCandidateCases.OwnedClone(fx), CandidateRef);
            (r.ExitCode, r.Refusal).ShouldBe((1, "sha-unresolved"));
            fx.Out.ToString().ShouldContain("REFUSED: origin/master did not resolve to a full sha (abcd).");
            File.Exists(r.JournalPath).ShouldBeFalse();
            git.Trace.ShouldNotContain(line => line.StartsWith("GIT push", StringComparison.Ordinal));
        }

        // An unreadable journal refuses before any git call.
        using (var fx = NightlyFixture.Create("c599-candidate-unreadable"))
        {
            var git = new FakeCandidateRemote(ShaB);
            var releaseRoot = fx.PathOf("releases");
            var root = ReleaseGateCredit.CandidateRoot(releaseRoot, CandidateId);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "candidate.json"), "{ not json");
            var r = ReleaseCandidateCases.Cut(fx, git, releaseRoot, ReleaseCandidateCases.OwnedClone(fx), CandidateRef);
            (r.ExitCode, r.Refusal).ShouldBe((3, "journal-unreadable"));
            git.Trace.ShouldBeEmpty();
            fx.Out.ToString().ShouldStartWith("[release-candidate] REFUSED: unreadable candidate journal (");
        }

        // WhatIf reads the remote but writes no journal and pushes nothing.
        using (var fx = NightlyFixture.Create("c599-candidate-whatif"))
        {
            var git = new FakeCandidateRemote(ShaB);
            var r = ReleaseCandidateCases.Cut(fx, git, fx.PathOf("releases"), ReleaseCandidateCases.OwnedClone(fx), CandidateRef, o => o.WhatIf = true);
            (r.ExitCode, r.Refusal, r.Pushed, r.Sha).ShouldBe((0, "", false, ShaB));
            File.Exists(r.JournalPath).ShouldBeFalse();
            git.Trace.ShouldNotContain(line => line.StartsWith("GIT push", StringComparison.Ordinal));
            fx.Out.ToString().ShouldBe("[release-candidate] WhatIf: would cut " + CandidateRef + " at " + ShaB + Environment.NewLine);
        }
    }

    [Test]
    public void A_long_lived_ref_is_read_never_pushed_and_never_repinned()
    {
        var shaD = ShaB[..12] + new string('d', 28);
        using var fx = NightlyFixture.Create("c1311-long-lived");
        var releaseRoot = fx.PathOf("releases");
        var checkout = ReleaseCandidateCases.OwnedClone(fx);

        var missing = new FakeCandidateRemote(ShaC);
        var r = ReleaseCandidateCases.Cut(fx, missing, releaseRoot, checkout, "rc/2026-10-15");
        (r.ExitCode, r.Refusal).ShouldBe((3, "rc-ref-missing"));

        var failing = new FakeCandidateRemote(ShaC);
        failing.Fail("ls-remote");
        r = ReleaseCandidateCases.Cut(fx, failing, releaseRoot, checkout, "rc/2026-10-15");
        (r.ExitCode, r.Refusal).ShouldBe((3, "rc-ref-unresolved"));

        var git = new FakeCandidateRemote(ShaC);
        git.Refs["rc/2026-10-15"] = ShaB;
        var whatIf = ReleaseCandidateCases.Cut(fx, git, releaseRoot, checkout, "refs/heads/rc/2026-10-15", o => o.WhatIf = true);
        (whatIf.ExitCode, whatIf.CandidateId, whatIf.Ref, whatIf.Sha).ShouldBe((0, "rc-2026-10-15-" + ShaB[..12], "rc/2026-10-15", ShaB));
        File.Exists(whatIf.JournalPath).ShouldBeFalse();

        var first = ReleaseCandidateCases.Cut(fx, git, releaseRoot, checkout, "rc/2026-10-15");
        (first.ExitCode, first.Pushed, first.Resumed).ShouldBe((0, false, false));
        var journal = ReleaseCandidateCases.Journal(first.JournalPath);
        (Str(journal, "sourceRef"), Str(journal, "cutUtc"), journal["pushed"]!.GetValue<bool>()).ShouldBe(("rc/2026-10-15", "2026-10-15T00:00:00.0000000Z", false));
        var again = ReleaseCandidateCases.Cut(fx, git, releaseRoot, checkout, "rc/2026-10-15");
        (again.ExitCode, again.Resumed).ShouldBe((0, true));

        // The same 12-hex id at another full sha: the journal is never repinned.
        git.Refs["rc/2026-10-15"] = shaD;
        var repinned = ReleaseCandidateCases.Cut(fx, git, releaseRoot, checkout, "rc/2026-10-15");
        (repinned.ExitCode, repinned.Refusal).ShouldBe((3, "candidate-repinned"));
        Str(ReleaseCandidateCases.Journal(first.JournalPath), "sha").ShouldBe(ShaB);
        git.Trace.ShouldNotContain(line => line.StartsWith("GIT push", StringComparison.Ordinal) || line.StartsWith("GIT fetch", StringComparison.Ordinal));
        fx.Out.ToString().ShouldContain("[release-candidate] journalled rc/2026-10-15 at " + ShaB + "; not pushed");
    }

    [Test]
    public void The_clone_is_owned_before_anything_is_journalled()
    {
        using var fx = NightlyFixture.Create("c599-candidate-clone");
        var releaseRoot = fx.PathOf("releases");

        // A linked worktree (a .git file) is not an independent clone.
        var linked = fx.PathOf("linked");
        Directory.CreateDirectory(linked);
        File.WriteAllText(Path.Combine(linked, ".git"), "gitdir: C:/elsewhere/.git/worktrees/linked\n");
        var git = new FakeCandidateRemote(ShaB);
        var r = ReleaseCandidateCases.Cut(fx, git, releaseRoot, linked, CandidateRef);
        (r.ExitCode, r.Refusal).ShouldBe((3, "linked-worktree"));
        git.Trace.ShouldBeEmpty();

        // A missing clone is cloned (into its parent) and marked; a failed clone refuses and marks nothing.
        var fresh = fx.PathOf("fresh", "checkout");
        git.Fail("clone");
        r = ReleaseCandidateCases.Cut(fx, git, releaseRoot, fresh, CandidateRef);
        (r.ExitCode, r.Refusal).ShouldBe((1, "clone-failed"));
        File.Exists(Path.Combine(fresh, ".antiphon-release-owned")).ShouldBeFalse();
        git.Clear("clone");
        r = ReleaseCandidateCases.Cut(fx, git, releaseRoot, fresh, CandidateRef, o => o.RemoteUrl = "https://example.invalid/antiphon.git");
        r.ExitCode.ShouldBe(0, r.Refusal);
        var clone = git.Calls.Last(c => c.Arguments[0] == "clone");
        Path.GetFullPath(clone.WorkingDirectory).ShouldBe(Path.GetFullPath(fx.PathOf("fresh")), StringCompareShould.IgnoreCase);
        string.Join("|", clone.Arguments).ShouldBe(string.Join("|", "clone", "-c", "core.longpaths=true", "https://example.invalid/antiphon.git", Path.GetFullPath(fresh)),
            StringCompareShould.IgnoreCase);
        var marker = ReleaseCandidateCases.Journal(Path.Combine(fresh, ".antiphon-release-owned"));
        (Str(marker, "origin"), Str(marker, "kind")).ShouldBe(("https://example.invalid/antiphon.git", "release-clone"));
    }

    [Test]
    public void The_command_line_names_every_parameter_in_kebab_case()
    {
        var defaults = ReleaseCandidateOptions.Parse([]);
        (defaults.Ref, defaults.ReleaseRoot, defaults.CheckoutRoot, defaults.RemoteUrl, defaults.SourceRef, defaults.ScheduleSlot, defaults.WhatIf)
            .ShouldBe(("", @"C:\Antiphon\releases", "", "https://github.com/michal-ciechan/Antiphon", "master", "", false));
        var given = ReleaseCandidateOptions.Parse(["--ref", "rc/2026-10-15", "--release-root", "r", "--checkout-root", "c", "--remote-url", "u", "--source-ref", "s",
            "--schedule-slot", "slot", "--what-if"]);
        (given.Ref, given.ReleaseRoot, given.CheckoutRoot, given.RemoteUrl, given.SourceRef, given.ScheduleSlot, given.WhatIf)
            .ShouldBe(("rc/2026-10-15", "r", "c", "u", "s", "slot", true));
        Should.Throw<NightlyUsageException>(() => ReleaseCandidateOptions.Parse(["--seams-path", "x"])).Message.ShouldBe("unknown flag --seams-path");
        Should.Throw<NightlyUsageException>(() => ReleaseCandidateOptions.Parse(["rc/2026-10-15"]));
        ReleaseCandidateOptions.Usage.ShouldStartWith("release-candidate ");
    }

    private static string Str(JsonObject o, string name) => ReleaseGateJournal.Str(o[name]);
}

/// <summary>The candidate cases' shared set-up: an owned clone, one cut over a git seam, a journal read back.</summary>
internal static class ReleaseCandidateCases
{
    public const string CandidateRef = "release/rc-20260923T083000Z";
    public const string CandidateId = "rc-20260923T083000Z";

    /// <summary>New-C599OwnedReleaseClone: Root/&lt;name&gt; with a .git directory naming origin and the .antiphon-release-owned marker.</summary>
    public static string OwnedClone(NightlyFixture fx, string name = "checkout")
    {
        var clone = fx.PathOf(name);
        Directory.CreateDirectory(Path.Combine(clone, ".git"));
        File.WriteAllText(Path.Combine(clone, ".git", "config"), "[remote \"origin\"]\n\turl = https://github.com/michal-ciechan/Antiphon\n");
        NightlyFixture.WriteJson(Path.Combine(clone, ReleaseCandidate.OwnedMarker), new { kind = "release-clone" });
        return clone;
    }

    /// <summary>ReleaseCandidate.Run of reference over git (the remote's git), with the fixture's host (clock: the given one, else the fixture's).</summary>
    public static ReleaseCandidateResult Cut(NightlyFixture fx, NightlyGit git, string releaseRoot, string checkout, string reference,
        Action<ReleaseCandidateOptions>? more = null, TimeProvider? clock = null)
    {
        var options = new ReleaseCandidateOptions { ReleaseRoot = releaseRoot, CheckoutRoot = checkout, Ref = reference };
        more?.Invoke(options);
        return ReleaseCandidate.Run(options, fx.Host(clock: clock), new System.IO.Abstractions.FileSystem(), new ReleaseGateRemote(git, null, null));
    }

    /// <summary>The fake remote's cut.</summary>
    public static ReleaseCandidateResult Cut(NightlyFixture fx, FakeCandidateRemote git, string releaseRoot, string checkout, string reference,
        Action<ReleaseCandidateOptions>? more = null, TimeProvider? clock = null) => Cut(fx, git.Run, releaseRoot, checkout, reference, more, clock);

    /// <summary>A JSON object file.</summary>
    public static JsonObject Journal(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
}

/// <summary>
/// New-C599GitSeams in memory: ls-remote reads the branch table, push writes it, rev-parse answers the head SHA, and a
/// failing subcommand answers exit 1 (a failing push that commits anyway lands its write first: a lost response). Every
/// call is traced ("GIT &lt;arguments&gt;"); Remote is the independent observer read.
/// </summary>
internal sealed class FakeCandidateRemote(string headSha)
{
    private readonly HashSet<string> _failing = new(StringComparer.Ordinal);
    private readonly HashSet<string> _commitsAnyway = new(StringComparer.Ordinal);

    /// <summary>The branches on the remote: name (without refs/heads/) to SHA.</summary>
    public Dictionary<string, string> Refs { get; } = new(StringComparer.Ordinal);

    /// <summary>"GIT &lt;arguments&gt;" per call, in order.</summary>
    public List<string> Trace { get; } = [];

    /// <summary>Each call's working directory and arguments.</summary>
    public List<(string WorkingDirectory, string[] Arguments)> Calls { get; } = [];

    /// <summary>Runs at the start of each push (before the fault).</summary>
    public Action? OnPush { get; set; }

    /// <summary>Runs at the start of each ls-remote (before the fault).</summary>
    public Action? OnLsRemote { get; set; }

    /// <summary>Set-C599GitFailure: the subcommand fails; commitsAnyway: a push still lands.</summary>
    public void Fail(string subcommand, bool commitsAnyway = false)
    {
        _failing.Add(subcommand);
        if (commitsAnyway) _commitsAnyway.Add(subcommand);
    }

    /// <summary>Set-C599GitFailure -Clear.</summary>
    public void Clear(string subcommand)
    {
        _failing.Remove(subcommand);
        _commitsAnyway.Remove(subcommand);
    }

    /// <summary>Read-C599Remote: the SHA the remote holds for the ref, "" when absent.</summary>
    public string Remote(string reference) => Refs.TryGetValue(Key(reference), out var sha) ? sha : "";

    /// <summary>The NightlyGit.</summary>
    public NightlyGitResult Run(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var argv = arguments.ToArray();
        Trace.Add("GIT " + string.Join(" ", argv));
        Calls.Add((workingDirectory, argv));
        var sub = argv.Length > 0 ? argv[0] : "";
        if (sub == "push") OnPush?.Invoke();
        if (sub == "ls-remote") OnLsRemote?.Invoke();
        if (_failing.Contains(sub))
        {
            if (sub == "push" && _commitsAnyway.Contains(sub)) Push(argv);
            return new NightlyGitResult(1, "injected failure");
        }
        switch (sub)
        {
            case "rev-parse":
            case "rev-list":
            case "show-ref":
                return new NightlyGitResult(0, headSha);
            case "ls-remote":
                var reference = argv[^1];
                return Refs.TryGetValue(Key(reference), out var sha) && sha.Length > 0
                    ? new NightlyGitResult(0, sha + "\t" + reference)
                    : new NightlyGitResult(0, "");
            case "push":
                Push(argv);
                return new NightlyGitResult(0, "");
            default:
                return new NightlyGitResult(0, "");
        }
    }

    private void Push(string[] argv)
    {
        var parts = argv[2].Split(':');
        Refs[Key(parts[^1])] = parts[0];
    }

    private static string Key(string reference) => reference.StartsWith("refs/heads/", StringComparison.Ordinal) ? reference["refs/heads/".Length..] : reference;
}
