using System.Collections;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599, CARD-1311 and CARD-1339: the release gate's library in process - the tags (ReleaseGateTags), the remote
/// boundary (ReleaseGateRemote: remote ref/head/tag reads, the exact tag push, the tag inventory, GitHub, the publisher's
/// clock and its seam admission), the publication and publish journals (ReleaseGateJournal) and the production native lock.
/// Unit 12 of 2026-11-22-remaining-ps1-to-csharp.md moved scripts/lib/release-gate.ps1 here; these cases replaced the rows
/// of scripts/test-release-gate.ps1 that called the library directly: C1339_ExactRemoteRefs G-1..G-6 (the parser),
/// C1339_RemoteTagAllocation G-21's inventory and G-22..G-24, G-27, G-28, G-67, G-68, G-70 (the proposal),
/// C1311_PreReleaseTag's tag-parse and long-lived proposal rows and C599A_PublisherTestMode's clock-seam row (the row names
/// are kept). Not ported: G-70's PowerShell mandatory-parameter prompt (PowerShell mechanics; RemoteTags is a required
/// argument here and null refuses). The publisher, promoter and candidate rows stay in the driver for their own units.
/// </summary>
[Category("Unit")]
public sealed class ReleaseGateLibraryTests
{
    private const string Head = "1111111111111111111111111111111111111111";
    private const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string C = "cccccccccccccccccccccccccccccccccccccccc";
    private const string Stamp = "rc-20260923T083000Z";
    private static readonly string LongLived = "rc-2026-10-15-" + Head[..12];
    private static readonly DateTime Day = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

    private static NightlyGitResult Ok(string output) => new(0, output);

    private static ReleaseGateJournal Journals(Action<string, string>? write = null) => new(new FakeTimeProvider(Now), write);

    private static NightlyFixture Root() => NightlyFixture.Create("c599-lib");

    // ---- tags (CARD-1311) ----------------------------------------------------------------------------------------

    [Test]
    public void C1311_PreReleaseTag_parse_and_proposal()
    {
        using var fx = Root();
        var rows = new NightlyCaseRows();
        var accepted = ReleaseGateTags.Tag("v2026.10.15.2-rc.1");
        var bareRc = ReleaseGateTags.Tag("v2026.10.15.2-rc");
        rows.Check(accepted.Ok && accepted.Prerelease && accepted.Rc == 1 && accepted.Sequence == 2 && !bareRc.Ok && bareRc.Reason == "tag-shape",
            "C1311 PreReleaseTag v2026.10.15.2-rc.1 is accepted", accepted.Reason + "/" + bareRc.Reason);
        var finalOnly = ReleaseGateTags.Tag("v2026.10.15.2");
        rows.Check(finalOnly.Ok && !finalOnly.Prerelease && finalOnly.Sequence == 2, "C1311 PreReleaseTag v2026.10.15.2 is accepted", finalOnly.Reason);
        var zero = ReleaseGateTags.Tag("v2026.10.15.2-rc.0");
        rows.Check(!zero.Ok && zero.Reason == "tag-shape", "C1311 PreReleaseTag v2026.10.15.2-rc.0 refuses tag-shape", zero.Reason);

        var journal = fx.PathOf("publications.json");
        var journals = Journals();
        var cut = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);
        const string shaD = "dddddddddddddddddddddddddddddddddddddddd", shaE = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        string Id(string date, string sha) => "rc-" + date + "-" + sha[..12];
        var p1 = journals.Proposal(journal, Id("2026-10-15", B), B, cut, []);
        journals.Reserve(journal, Id("2026-10-15", B), B, cut, []);
        var p2 = journals.Proposal(journal, Id("2026-10-15", C), C, cut, []);
        journals.Reserve(journal, Id("2026-10-15", C), C, cut, []);
        journals.MarkPublished(journal, Id("2026-10-15", B), "fixture-pre", "https://example.invalid/pre");
        var closed = journals.RecordFinalTag(journal, Id("2026-10-15", B), B, p1.Tag, "v2026.10.15.1");
        var p3 = journals.Proposal(journal, Id("2026-10-15", shaD), shaD, cut, []);
        var p4 = journals.Proposal(journal, Id("2026-10-16", shaE), shaE, new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc), []);
        var stampJournal = fx.PathOf("stamp.json");
        var stampCut = new DateTime(2026, 9, 23, 8, 30, 0, DateTimeKind.Utc);
        var stamp = journals.Proposal(stampJournal, Stamp, B, stampCut, []);
        journals.Reserve(stampJournal, Stamp, B, stampCut, []);
        var reused = journals.Proposal(stampJournal, Stamp, B, stampCut, []);
        var threw = Should.Throw<NightlyRefusal>(() => journals.Proposal(stampJournal, Stamp, C, stampCut, [])).Message;
        rows.Check(p1.Tag == "v2026.10.15.1-rc.1" && p2.Tag == "v2026.10.15.1-rc.2" && closed && p3.Tag == "v2026.10.15.2-rc.1" && p4.Tag == "v2026.10.16.1-rc.1"
                && stamp.Tag == "v2026.09.23.1" && reused.Reused && reused.Tag == "v2026.09.23.1" && threw.Contains("already reserved", StringComparison.Ordinal),
            "C1311 PreReleaseTag a long-lived proposal is vYYYY.MM.DD.N-rc.K",
            $"p1={p1.Tag} p2={p2.Tag} closed={closed} p3={p3.Tag} p4={p4.Tag} stamp={stamp.Tag} reused={reused.Tag} threw={threw}");
        rows.ShouldAllPass(4);
    }

    [Test]
    public void Tags_refuse_empty_and_impossible_dates_and_spell_canonically()
    {
        ReleaseGateTags.Tag("").Reason.ShouldBe("empty-tag");
        ReleaseGateTags.Tag("v2026.02.30.1").Reason.ShouldBe("tag-date");
        ReleaseGateTags.Tag("v2026.13.01.1").Reason.ShouldBe("tag-date");
        ReleaseGateTags.Tag("v2026.10.15.01").Reason.ShouldBe("tag-shape");
        ReleaseGateTags.Canonical(ReleaseGateTags.Tag("V2026.10.15.3-RC.2")).ShouldBe("v2026.10.15.3-rc.2");
        ReleaseGateTags.Tag("v2026.10.15.3").Date.ShouldBe(new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc));
        Should.Throw<NightlyRefusal>(() => ReleaseGateTags.NewTag(Day, 0));
        Should.Throw<NightlyRefusal>(() => ReleaseGateTags.NewPreReleaseTag(Day, 1, 0));
        ReleaseGateTags.LongLivedCandidateId(LongLived).ShouldBeTrue();
        ReleaseGateTags.LongLivedCandidateId(Stamp).ShouldBeFalse();
        ReleaseGateTags.LongLivedCandidateId("rc-2026-10-15-" + Head[..12].ToUpperInvariant().Replace('1', 'A')).ShouldBeFalse();
    }

    // ---- the exact remote-ref parser (CARD-1339 G-1..G-6) -----------------------------------------------------

    [Test]
    public void C1339_ExactRemoteRefs_parser()
    {
        var rows = new NightlyCaseRows();
        const string preTag = "v2026.10.15.1-rc.1", candidateRef = "rc/2026-10-15";
        var reference = "refs/tags/" + preTag + "^{}";
        var nestedLine = B + "\trefs/tags/refs/tags/" + preTag + "^{}";
        var exactLine = Head + "\t" + reference;
        var checks = new List<bool>();
        foreach (var output in new[] { nestedLine + "\n" + exactLine, exactLine + "\n" + nestedLine })
        {
            var read = ReleaseGateTags.RemoteRef(Ok(output), reference);
            checks.Add(read.Status == ReleaseGateRef.Found && read.Sha == Head);
        }
        checks.Add(ReleaseGateTags.RemoteRef(Ok(nestedLine), reference).Status == ReleaseGateRef.Absent);
        rows.Check(checks.All(c => c), "C1339 G-1 exact-tag", string.Join(",", checks));

        var headRef = "refs/heads/" + candidateRef;
        var headOutput = Head + "\t" + headRef + "\n" + B + "\trefs/heads/archive/" + candidateRef + "\n";
        var head = ReleaseGateTags.RemoteRef(Ok(headOutput), headRef);
        var suffix = ReleaseGateTags.RemoteRef(Ok(B + "\trefs/heads/archive/" + candidateRef), headRef);
        rows.Check(head.Status == ReleaseGateRef.Found && head.Sha == Head && suffix.Status == ReleaseGateRef.Absent, "C1339 G-2 exact-head");
        rows.Check(ReleaseGateTags.RemoteRef(Ok(Head + "\t" + reference.ToUpperInvariant()), reference).Status == ReleaseGateRef.Absent, "C1339 G-3 ordinal-case");
        checks.Clear();
        foreach (var sha in new[] { "abcd", new string('z', 40) }) checks.Add(ReleaseGateTags.RemoteRef(Ok(sha + "\t" + reference), reference).Status == ReleaseGateRef.Failed);
        rows.Check(checks.All(c => c), "C1339 G-4 full-sha");
        checks.Clear();
        foreach (var output in new[] { exactLine + "\n" + B + "\t" + reference, B + "\t" + reference + "\n" + exactLine })
            checks.Add(ReleaseGateTags.RemoteRef(Ok(output), reference).Status == ReleaseGateRef.Failed);
        var unrelated = ReleaseGateTags.RemoteRef(Ok("bad unrelated\n" + exactLine), reference);
        checks.Add(unrelated.Status == ReleaseGateRef.Found && unrelated.Sha == Head);
        checks.Add(ReleaseGateTags.RemoteRef(Ok(reference), reference).Status == ReleaseGateRef.Failed);
        rows.Check(checks.All(c => c), "C1339 G-5 ambiguous-record", string.Join(",", checks));
        checks.Clear();
        foreach (var output in new[] { "", exactLine }) checks.Add(ReleaseGateTags.RemoteRef(new NightlyGitResult(1, output), reference).Status == ReleaseGateRef.Failed);
        checks.Add(ReleaseGateTags.RemoteRef(Ok(""), reference).Status == ReleaseGateRef.Absent);
        checks.Add(ReleaseGateTags.RemoteRef(null!, reference).Status == ReleaseGateRef.Failed);
        rows.Check(checks.All(c => c), "C1339 G-6 query-failure", string.Join(",", checks));
        rows.ShouldAllPass(6);
    }

    // ---- the remote: refs, tags, the exact push, the inventory --------------------------------------------------

    [Test]
    public void Remote_reads_name_origin_and_the_complete_ref()
    {
        var git = new FakeGit(output: "", exitCode: 0);
        git.Reply(a => a.Contains("refs/heads/rc/2026-10-15"), Ok(Head + "\trefs/heads/rc/2026-10-15\n"));
        var remote = new ReleaseGateRemote(git.Run, null, null);
        var head = remote.Head("wd", "rc/2026-10-15");
        head.ShouldBe(new ReleaseGateRef(ReleaseGateRef.Found, Head));
        remote.Head("wd", "refs/heads/rc/2026-10-15").Sha.ShouldBe(Head);
        git.Calls[0].Arguments.ShouldBe(["ls-remote", "--heads", "origin", "refs/heads/rc/2026-10-15"]);
        git.Calls[0].WorkingDirectory.ShouldBe("wd");
        remote.Ref("wd", "refs/tags/v1").Status.ShouldBe(ReleaseGateRef.Absent);
        git.Calls[^1].Arguments.ShouldBe(["ls-remote", "--tags", "origin", "refs/tags/v1"]);
        Should.Throw<NightlyRefusal>(() => remote.Ref("wd", "rc/2026-10-15")).Message.ShouldBe("remote ref must be fully qualified");
    }

    [Test]
    public void Remote_tag_is_found_only_with_both_the_peeled_and_the_direct_record()
    {
        const string tag = "v2026.10.15.1-rc.1";
        const string objectSha = "2222222222222222222222222222222222222222";
        ReleaseGateRemoteTag Read(string? peeled, string? direct, int exit = 0)
        {
            var git = new FakeGit(output: "", exitCode: exit);
            if (peeled != null) git.Reply(a => a[^1] == "refs/tags/" + tag + "^{}", new NightlyGitResult(exit, peeled + "\trefs/tags/" + tag + "^{}"));
            if (direct != null) git.Reply(a => a[^1] == "refs/tags/" + tag, new NightlyGitResult(exit, direct + "\trefs/tags/" + tag));
            return new ReleaseGateRemote(git.Run, null, null).Tag("wd", tag);
        }
        Read(Head, objectSha).ShouldBe(new ReleaseGateRemoteTag(ReleaseGateRef.Found, Head, objectSha));
        Read(null, null).ShouldBe(new ReleaseGateRemoteTag(ReleaseGateRef.Absent, "", ""));
        // A lightweight tag has no peeled record: not a release tag.
        Read(null, Head).ShouldBe(new ReleaseGateRemoteTag(ReleaseGateRef.Failed, "", Head));
        Read(Head, objectSha, exit: 1).Status.ShouldBe(ReleaseGateRef.Failed);
    }

    [Test]
    public void Exact_tag_push_maps_one_private_ref_and_removes_it_with_a_compare_and_swap()
    {
        var git = new FakeGit(output: Head);
        var push = new ReleaseGateRemote(git.Run, null, null).PushExactTag("wd", "v2026.10.15.1");
        push.ExitCode.ShouldBe(0);
        var calls = git.Calls.Select(c => c.Arguments.ToArray()).ToArray();
        calls.Length.ShouldBe(4);
        calls[0].ShouldBe(["show-ref", "--verify", "--hash", "refs/tags/v2026.10.15.1"]);
        calls[1][0].ShouldBe("update-ref");
        calls[1][1].ShouldMatch("^refs/antiphon-release-push/[0-9a-f]{32}/v2026\\.10\\.15\\.1$");
        calls[1][2..].ShouldBe([Head, new string('0', 40)]);
        var prefix = calls[1][1][..^"v2026.10.15.1".Length];
        calls[2].ShouldBe(["push", "origin", prefix + "*:refs/tags/*"]);
        calls[3].ShouldBe(["update-ref", "-d", calls[1][1], Head]);
        git.Lines.ShouldAllBe(line => !line.Contains("--force") && !line.Contains(" +") && !line.Contains("--delete"));

        // No readable full tag object, no private ref, or a failed cleanup: exit 1 and nothing further.
        new ReleaseGateRemote(new FakeGit(output: Head, exitCode: 1).Run, null, null).PushExactTag("wd", "t").ExitCode.ShouldBe(1);
        var shortSha = new FakeGit(output: "abcd");
        new ReleaseGateRemote(shortSha.Run, null, null).PushExactTag("wd", "t").ExitCode.ShouldBe(1);
        shortSha.Calls.Count.ShouldBe(1);
        var noRef = new FakeGit(output: Head).Reply(a => a[0] == "update-ref", new NightlyGitResult(1, ""));
        new ReleaseGateRemote(noRef.Run, null, null).PushExactTag("wd", "t").ExitCode.ShouldBe(1);
        noRef.Calls.Any(c => c.Arguments[0] == "push").ShouldBeFalse();
        var failedPush = new FakeGit(output: Head).Reply("push", new NightlyGitResult(128, "rejected"));
        var failed = new ReleaseGateRemote(failedPush.Run, null, null).PushExactTag("wd", "t");
        failed.ExitCode.ShouldBe(128);
        failedPush.Calls[^1].Arguments.Take(2).ShouldBe(["update-ref", "-d"]);
        var dirty = new FakeGit(output: Head).Reply(a => a.Contains("-d"), new NightlyGitResult(1, ""));
        new ReleaseGateRemote(dirty.Run, null, null).PushExactTag("wd", "t").ExitCode.ShouldBe(1);
    }

    [Test]
    public void Tag_inventory_keeps_canonical_release_tags_only()
    {
        ReleaseGateTagInventory Read(string output, int exit = 0)
        {
            var git = new FakeGit().Reply("--refs", new NightlyGitResult(exit, output));
            var inventory = new ReleaseGateRemote(git.Run, null, null).TagInventory("wd");
            git.Calls[0].Arguments.ShouldBe(["ls-remote", "--tags", "--refs", "origin"]);
            return inventory;
        }
        // G-21: a nested tag, an irrelevant tag and a non-canonical spelling are skipped; the peeled records of a plain
        // ls-remote --tags answer are not tags either.
        var refs = B + "\trefs/tags/refs/tags/v2026.10.15.1\n" + B + "\trefs/tags/irrelevant\n" + Head + "\trefs/tags/v2026.10.15.3\n"
            + Head + "\trefs/tags/v2026.10.15.03\n" + Head + "\trefs/tags/V2026.10.15.4\n";
        var inventory = Read(refs);
        inventory.Ok.ShouldBeTrue();
        inventory.Tags.ShouldBe(["v2026.10.15.3"]);
        var withPeels = Read(refs + C + "\trefs/tags/v2026.10.15.3^{}\n" + C + "\trefs/tags/refs/tags/v2026.10.15.1^{}\n");
        withPeels.Ok.ShouldBeTrue();
        withPeels.Tags.ShouldBe(["v2026.10.15.3"]);
        Read(Head + "\trefs/tags/v2026.10.15.3-rc.2\n" + Head + "\trefs/tags/v2026.10.15.3-rc.2\n" + Head + "\trefs/heads/master\n").Tags.ShouldBe(["v2026.10.15.3-rc.2"]);
        // G-25: an unusable inventory is no inventory.
        Read("", exit: 1).Ok.ShouldBeFalse();
        Read("short\trefs/tags/v2026.10.15.1").Ok.ShouldBeFalse();
        Read(Head + "\trefs/tags/v2026.10.15.1\n" + B + "\trefs/tags/v2026.10.15.1").Ok.ShouldBeFalse();
        Read("malformed").Ok.ShouldBeFalse();
        Read("").Tags.ShouldBeEmpty();
    }

    // ---- the proposal over the journal and the inventory (CARD-1339) -------------------------------------------

    [Test]
    public void C1339_RemoteTagAllocation_proposal()
    {
        using var fx = Root();
        var rows = new NightlyCaseRows();
        var journals = Journals();
        var journal = fx.PathOf("journal", "publications.json");
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        File.WriteAllText(journal, new JsonObject
        {
            ["schemaVersion"] = 1,
            ["reservations"] = new JsonArray(ReleaseGateJournal.Entry("journal-candidate", B, "v2026.10.15.2-rc.7", 2, "", false, "", "", "", 7, "")),
        }.ToJsonString());
        var r = journals.Proposal(journal, LongLived, Head, Day, ["v2026.10.15.1"]);
        rows.Check(r.Tag == "v2026.10.15.2-rc.8" && ReleaseGateJournal.Rows(r.Journal).Count == 1, "C1339 G-22 journal-union", r.Tag);
        // This extra final closes a journal-only open sequence too.
        var closedJournal = journals.Proposal(journal, LongLived, Head, Day, ["v2026.10.15.1", "v2026.10.15.2", "v2026.10.15.3"]).Tag == "v2026.10.15.4-rc.1";
        var empty = fx.PathOf("empty", "publications.json");
        r = journals.Proposal(empty, LongLived, Head, Day, ["v2026.10.15.1", "v2026.10.15.3"]);
        rows.Check(r.Tag == "v2026.10.15.2-rc.1" && closedJournal, "C1339 G-23 smallest-free", r.Tag);
        var legacy = journals.Proposal(empty, Stamp, Head, Day, ["v2026.10.15.1", "v2026.10.15.2-rc.4"]);
        var legacyJournal = journals.Proposal(journal, Stamp, Head, Day, ["v2026.10.15.1"]);
        rows.Check(legacy.Tag == "v2026.10.15.3" && legacyJournal.Tag == "v2026.10.15.3", "C1339 G-24 legacy-occupancy", legacy.Tag + "/" + legacyJournal.Tag);

        var reserved = fx.PathOf("reserved", "publications.json");
        journals.Reserve(reserved, LongLived, Head, Day, []);
        var before = File.ReadAllText(reserved);
        var shaConflict = Should.Throw<NightlyRefusal>(() => journals.Proposal(reserved, LongLived, C, Day, [])).Message;
        rows.Check(shaConflict.Contains("already reserved for sha", StringComparison.Ordinal) && File.ReadAllText(reserved) == before,
            "C1339 G-27 candidate-sha-conflict", shaConflict);
        var candidateConflict = Should.Throw<NightlyRefusal>(() => journals.Reserve(reserved, "rc-2026-10-15-" + B[..12], Head, Day, [])).Message;
        rows.Check(candidateConflict.Contains("already reserved under candidate", StringComparison.Ordinal) && File.ReadAllText(reserved) == before,
            "C1339 G-28 sha-candidate-conflict", candidateConflict);

        var open = new[] { "v2026.10.15.2-rc.9", "v2026.10.15.3-rc.2", "v2026.10.15.3-rc.4" };
        var greatest = new List<ReleaseGateProposal>();
        foreach (var order in new[] { open, Enumerable.Reverse(open).ToArray() })
            greatest.Add(journals.Proposal(fx.PathOf("open", "publications.json"), LongLived, Head, Day, order));
        rows.Check(greatest.All(p => p.Sequence == 3), "C1339 G-67 greatest-open-n", string.Join(",", greatest.Select(p => p.Tag)));
        rows.Check(greatest.All(p => p.Rc == 5), "C1339 G-68 greatest-k", string.Join(",", greatest.Select(p => p.Tag)));

        var implicitChecks = new List<bool>();
        var none = fx.PathOf("none", "publications.json");
        implicitChecks.Add(Should.Throw<ArgumentNullException>(() => journals.Proposal(none, LongLived, Head, Day, null!)).ParamName == "remoteTags");
        implicitChecks.Add(Should.Throw<ArgumentNullException>(() => journals.Reserve(none, LongLived, Head, Day, null!)).ParamName == "remoteTags");
        implicitChecks.Add(!File.Exists(none));
        implicitChecks.Add(journals.Proposal(none, LongLived, Head, Day, []).Tag == "v2026.10.15.1-rc.1");
        implicitChecks.Add(journals.Reserve(none, LongLived, Head, Day, []).Tag == "v2026.10.15.1-rc.1");
        rows.Check(implicitChecks.All(c => c), "C1339 G-70 no-implicit-inventory", string.Join(",", implicitChecks));
        rows.ShouldAllPass(8);
    }

    [Test]
    public void Proposal_cut_date_is_utc_and_a_short_sha_refuses()
    {
        using var fx = Root();
        var journals = Journals();
        var path = fx.PathOf("publications.json");
        var local = new DateTimeOffset(2026, 10, 20, 1, 59, 59, TimeSpan.FromHours(2)).UtcDateTime;
        journals.Proposal(path, LongLived, Head, local, []).Tag.ShouldBe("v2026.10.19.1-rc.1");
        journals.Proposal(path, LongLived, Head, DateTime.MinValue, []).Tag.ShouldBe("v2026.11.23.1-rc.1");
        Should.Throw<NightlyRefusal>(() => journals.Proposal(path, LongLived, Head[..12], Day, [])).Message.ShouldContain("full sha");
    }

    // ---- the publication journal ------------------------------------------------------------------------------

    [Test]
    public void Reservation_publication_and_previous_release_rows()
    {
        using var fx = Root();
        var journals = Journals();
        var path = fx.PathOf("releases", "publications.json");
        ReleaseGateJournal.Read(path)["reservations"]!.AsArray().Count.ShouldBe(0);
        var first = journals.Reserve(path, LongLived, Head, Day, []);
        first.Reused.ShouldBeFalse();
        var row = ReleaseGateJournal.Rows(ReleaseGateJournal.Read(path)).Single();
        row.Select(p => p.Key).ShouldBe(["candidateId", "sha", "tag", "sequence", "reservedAt", "published", "releaseId", "releaseUrl", "rc"]);
        ReleaseGateJournal.Str(row["reservedAt"]).ShouldBe("2026-11-23T10:00:00.0000000Z");
        row["published"]!.GetValue<bool>().ShouldBeFalse();
        var again = journals.Reserve(path, LongLived.ToUpperInvariant(), Head, Day, []);
        again.Reused.ShouldBeTrue();
        again.Tag.ShouldBe(first.Tag);
        ReleaseGateJournal.Rows(ReleaseGateJournal.Read(path)).Count.ShouldBe(1);

        var stampRow = journals.Reserve(path, Stamp, B, new DateTime(2026, 9, 23, 8, 30, 0, DateTimeKind.Utc), []);
        stampRow.Tag.ShouldBe("v2026.09.23.1");
        ReleaseGateJournal.Rows(ReleaseGateJournal.Read(path))[1].ContainsKey("rc").ShouldBeFalse();
        ReleaseGateJournal.PreviousPublishedTag(path, LongLived).ShouldBeEmpty();
        journals.MarkPublished(path, Stamp, "R1", "https://example/releases/tag/v2026.09.23.1");
        ReleaseGateJournal.PreviousPublishedTag(path, LongLived).ShouldBe("v2026.09.23.1");
        ReleaseGateJournal.PreviousPublishedTag(path, Stamp).ShouldBeEmpty();
        journals.MarkPublished(path, LongLived, "R2", "https://example/releases/tag/" + first.Tag);
        journals.RecordFinalTag(path, LongLived, Head, first.Tag, "v2026.10.15.1").ShouldBeTrue();
        journals.MarkPublished(path, LongLived, "R3", "https://example/r3");
        var published = ReleaseGateJournal.Rows(ReleaseGateJournal.Read(path))[0];
        published.Select(p => p.Key).ShouldBe(["candidateId", "sha", "tag", "sequence", "reservedAt", "published", "releaseId", "releaseUrl", "publishedAt", "rc", "finalTag"]);
        ReleaseGateJournal.Str(published["releaseId"]).ShouldBe("R3");
        ReleaseGateJournal.Str(published["finalTag"]).ShouldBe("v2026.10.15.1");
        ReleaseGateJournal.Str(published["reservedAt"]).ShouldBe("2026-11-23T10:00:00.0000000Z");
        Should.Throw<NightlyRefusal>(() => journals.MarkPublished(path, "rc-unknown", "R", "U")).Message.ShouldBe("no reservation for candidate rc-unknown");

        File.WriteAllText(path, "{broken");
        Should.Throw<NightlyRefusal>(() => ReleaseGateJournal.Read(path)).Message.ShouldStartWith("unreadable publication journal " + path);
    }

    private static JsonObject ValidJournal(string candidate, string sha, string tag) => new()
    {
        ["schemaVersion"] = 1,
        ["reservations"] = new JsonArray(ReleaseGateJournal.Entry(candidate, sha, tag, 1, "2026-10-15T00:00:00Z", true, "fixture-pre", "https://example.invalid/pre", "2026-10-15T01:00:00Z", 1, "")),
    };

    [Test]
    public void Promotion_consumes_exactly_one_published_typed_reservation()
    {
        using var fx = Root();
        const string pre = "v2026.10.15.1-rc.1", final = "v2026.10.15.1";
        var path = fx.PathOf("publications.json");
        string Check(Action<JsonObject>? edit = null, string? raw = null, string finalTag = final, string preTag = pre)
        {
            if (raw != null) File.WriteAllText(path, raw);
            else
            {
                var journal = ValidJournal(LongLived, Head, pre);
                edit?.Invoke(journal);
                File.WriteAllText(path, journal.ToJsonString());
            }
            var check = ReleaseGateJournal.PromotionReservation(path, LongLived, Head, preTag, finalTag);
            return check.Ok ? "ok" : check.Reason["promotion-reservation-".Length..];
        }
        JsonObject Row(JsonObject j) => j["reservations"]![0]!.AsObject();
        Check().ShouldBe("ok");
        ReleaseGateJournal.PromotionReservation(fx.PathOf("absent.json"), LongLived, Head, pre, final).Reason.ShouldBe("promotion-reservation-missing");
        Check(j => j["reservations"] = new JsonArray()).ShouldBe("missing");
        Check(j => j["reservations"]!.AsArray().Add(Row(j).DeepClone())).ShouldBe("ambiguous");
        foreach (var invalid in new Action<JsonObject>[]
        {
            j => j["schemaVersion"] = "1", j => j["schemaVersion"] = 2, j => j["schemaVersion"] = 1.5, j => j["reservations"] = Row(j).DeepClone(),
            j => j["reservations"] = new JsonArray("row"), j => Row(j)["candidateId"] = new JsonArray(LongLived), j => Row(j)["sha"] = new JsonArray(Head),
            j => Row(j)["tag"] = new JsonArray(pre), j => Row(j)["published"] = "true", j => Row(j)["releaseId"] = new JsonArray("fixture-pre"),
            j => Row(j)["releaseUrl"] = new JsonArray("u"), j => Row(j)["finalTag"] = new JsonArray(final), j => Row(j)["finalTag"] = null,
            j => Row(j).Remove("releaseUrl"),
        })
            Check(invalid).ShouldBe("invalid");
        Check(raw: "{broken").ShouldBe("invalid");
        Check(raw: "[]").ShouldBe("invalid");
        Check(raw: "[" + ValidJournal(LongLived, Head, pre).ToJsonString() + "]").ShouldBe("invalid");
        Check(raw: "{\"schemaVersion\":1.0,\"reservations\":[]}").ShouldBe("invalid");
        Check(j => Row(j)["candidateId"] = "foreign-candidate").ShouldBe("candidate-mismatch");
        Check(j => Row(j)["candidateId"] = LongLived.ToUpperInvariant()).ShouldBe("candidate-mismatch");
        Check(j => { Row(j)["sha"] = B; }, preTag: pre).ShouldBe("sha-mismatch");
        Check(j => Row(j)["sha"] = Head[..12]).ShouldBe("sha-mismatch");
        // A full SHA is lower-case hex: an upper-case spelling of the same SHA matches the row but is no full SHA.
        var lettered = "abcdef" + Head[6..];
        File.WriteAllText(path, ValidJournal(LongLived, lettered.ToUpperInvariant(), pre).ToJsonString());
        ReleaseGateJournal.PromotionReservation(path, LongLived, lettered, pre, final).Reason.ShouldBe("promotion-reservation-sha-mismatch");
        Check(j => Row(j)["tag"] = "v2026.10.15.2-rc.1").ShouldBe("tag-mismatch");
        Check(j => Row(j)["tag"] = pre.ToUpperInvariant()).ShouldBe("tag-mismatch");
        Check(j => Row(j)["published"] = false).ShouldBe("unpublished");
        Check(j => Row(j)["releaseId"] = "").ShouldBe("release-identity");
        Check(j => Row(j)["releaseUrl"] = " ").ShouldBe("release-identity");
        Check(j => Row(j)["finalTag"] = "v2026.10.15.2").ShouldBe("final-conflict");
        Check(finalTag: "v2026.10.15.2").ShouldBe("final-conflict");
        Check(j => Row(j)["finalTag"] = final).ShouldBe("ok");
        Check(j => Row(j)["tag"] = final, preTag: final).ShouldBe("final-conflict");
    }

    [Test]
    public void Recording_the_final_tag_rechecks_before_and_after_the_write()
    {
        using var fx = Root();
        const string pre = "v2026.10.15.1-rc.1", final = "v2026.10.15.1";
        var path = fx.PathOf("publications.json");
        var journal = ValidJournal(LongLived, Head, pre);
        journal["reservations"]!.AsArray().Add(ReleaseGateJournal.Entry("rc-2026-10-15-" + C[..12], C, "v2026.10.15.1-rc.2", 1, "x", false, "", "", "", 2, ""));
        journal["extra"] = new JsonObject { ["kept"] = true };
        File.WriteAllText(path, journal.ToJsonString());
        var unrelated = JsonNode.Parse(File.ReadAllText(path))!["reservations"]![1]!.ToJsonString();

        var writes = new List<string>();
        Journals((p, text) => { writes.Add(p); ReleaseGateJournal.WriteAtomic(p, text); }).RecordFinalTag(path, LongLived, Head, pre, final).ShouldBeTrue();
        writes.ShouldBe([path]);
        var after = JsonNode.Parse(File.ReadAllText(path))!;
        ((string?)after["reservations"]![0]!["finalTag"]).ShouldBe(final);
        after["reservations"]![1]!.ToJsonString().ShouldBe(unrelated);
        ((bool?)after["extra"]!["kept"]).ShouldBe(true);
        Journals().RecordFinalTag(path, LongLived, Head, pre, final).ShouldBeTrue();
        Journals().RecordFinalTag(path, LongLived, Head, pre, "v2026.10.15.2").ShouldBeFalse();

        File.WriteAllText(path, ValidJournal(LongLived, Head, pre).ToJsonString());
        var before = File.ReadAllText(path);
        Should.Throw<IOException>(() => Journals((p, t) => throw new IOException("fixture-atomic-write-failure")).RecordFinalTag(path, LongLived, Head, pre, final));
        File.ReadAllText(path).ShouldBe(before);
        Journals((p, t) => { }).RecordFinalTag(path, LongLived, Head, pre, final).ShouldBeFalse();
        File.ReadAllText(path).ShouldBe(before);
    }

    [Test]
    public void Publish_journal_steps_are_durable_and_resumable()
    {
        using var fx = Root();
        var journals = Journals();
        var path = fx.PathOf("candidate", "publish-journal.json");
        ReleaseGateJournal.HasStep(path, "intent").ShouldBeFalse();
        journals.WriteStep(path, "intent", new Hashtable { ["tag"] = "v2026.10.15.1-rc.1", ["sha"] = Head });
        journals.WriteStep(path, "tag-local", null);
        journals.WriteStep(path, "TAG-LOCAL", null);
        journals.WriteStep(path, "asset:release-manifest.json", new Hashtable { ["asset:release-manifest.json:digest"] = "d1" });
        journals.WriteStep(path, "published", new Hashtable { ["tag"] = "replaced" });
        var doc = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        doc["steps"]!.AsArray().Select(s => (string?)s).ShouldBe(["intent", "tag-local", "asset:release-manifest.json", "published"]);
        ((string?)doc["tag"]).ShouldBe("replaced");
        ((string?)doc["sha"]).ShouldBe(Head);
        ((string?)doc["asset:release-manifest.json:digest"]).ShouldBe("d1");
        ((string?)doc["updatedAt"]).ShouldBe("2026-11-23T10:00:00.0000000Z");
        ReleaseGateJournal.HasStep(path, "Tag-Local").ShouldBeTrue();
        ReleaseGateJournal.HasStep(path, "draft-created").ShouldBeFalse();
        File.WriteAllText(path, "{broken");
        ReleaseGateJournal.HasStep(path, "intent").ShouldBeFalse();
        Should.Throw<NightlyRefusal>(() => journals.WriteStep(path, "intent", null)).Message.ShouldStartWith("unreadable journal " + path);
    }

    // ---- seams: test mode, admission, the clock, PowerShell-shaped answers ------------------------------------------

    [Test]
    public void Clock_seam_answers_only_in_test_mode()
    {
        var rows = new NightlyCaseRows();
        var instant = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
        var outside = Should.Throw<NightlyRefusal>(() => ReleaseGateRemote.Seamed(null!, null!, () => instant, testMode: false).UtcNow()).Message;
        var inside = ReleaseGateRemote.Seamed(null!, null!, () => instant, testMode: true).UtcNow();
        rows.Check(outside == "release-gate clock seam refused outside test mode" && inside == instant,
            "C599 V-18: the release-gate clock seam is refused outside test mode and honoured inside it", "outside=" + outside + " inside=" + inside.ToString("o"));
        rows.ShouldAllPass(1);
        ReleaseGateRemote.Seamed(null!, null!, () => new DateTimeOffset(2026, 10, 20, 1, 59, 59, TimeSpan.FromHours(2)).LocalDateTime, true).UtcNow()
            .ShouldBe(new DateTime(2026, 10, 19, 23, 59, 59, DateTimeKind.Utc));
        Should.Throw<NightlyRefusal>(() => ReleaseGateRemote.Seamed(null!, null!, () => "2026-10-15", true).UtcNow()).Message.ShouldBe("release-gate clock seam returned no instant");
        var wall = ReleaseGateRemote.Seamed(null!, null!, null!, false).UtcNow();
        (DateTime.UtcNow - wall).Duration().ShouldBeLessThan(TimeSpan.FromMinutes(1));
        foreach (var value in new[] { null, "", "0", "true", "TRUE", " 1", "1 ", "yes" }) ReleaseGateRemote.TestMode(value).ShouldBeFalse(value);
        ReleaseGateRemote.TestMode("1").ShouldBeTrue();
    }

    [Test]
    public void Publisher_admission_refuses_seams_outside_test_mode_and_a_real_remote_inside_it()
    {
        using var fx = Root();
        var seams = fx.PathOf("seams.ps1");
        File.WriteAllText(seams, "# seams");
        var fake = new object();
        ReleaseGateRemote.PublisherAdmission(false, "", null!, null!).ShouldBe("");
        ReleaseGateRemote.PublisherAdmission(false, seams, fake, fake).ShouldBe("seams-outside-test-mode");
        ReleaseGateRemote.PublisherAdmission(true, "", null!, null!).ShouldBe("test-mode-real-remote");
        ReleaseGateRemote.PublisherAdmission(true, seams, fake, null!).ShouldBe("test-mode-real-remote");
        ReleaseGateRemote.PublisherAdmission(true, seams, null!, fake).ShouldBe("test-mode-real-remote");
        ReleaseGateRemote.PublisherAdmission(true, seams, fake, fake).ShouldBe("");
        Should.Throw<NightlyRefusal>(() => ReleaseGateRemote.PublisherAdmission(true, fx.PathOf("missing.ps1"), fake, fake)).Message.ShouldStartWith("missing seams ");
    }

    [Test]
    public void Seamed_git_and_github_read_the_seam_answers_by_name()
    {
        var gitCalls = new List<string>();
        var ghCalls = new List<string>();
        var body = new Hashtable { ["id"] = "R1", ["isDraft"] = false };
        var remote = ReleaseGateRemote.Seamed(
            (wd, args) =>
            {
                gitCalls.Add(wd + "|" + string.Join(" ", args));
                return args[0] switch
                {
                    "rev-parse" => new Hashtable { ["ExitCode"] = 0, ["Output"] = Head },
                    "fetch" => new Hashtable { ["Output"] = "" },
                    "log" => null!,
                    _ => new object[] { "stray output", new Hashtable { ["ExitCode"] = 7, ["Output"] = "last" } },
                };
            },
            (verb, args, wd) =>
            {
                ghCalls.Add(verb + "|" + args["tag"] + "|" + wd);
                return new Hashtable { ["ExitCode"] = 0, ["Body"] = body };
            },
            null!, testMode: true);
        remote.Git("wd", "rev-parse", "HEAD").Output.ShouldBe(Head);
        remote.Git("wd", "fetch", "origin").ExitCode.ShouldBe(0);
        remote.Git("wd", "log").ExitCode.ShouldBe(1);
        var many = remote.Git("wd", "push");
        many.ExitCode.ShouldBe(7);
        many.Output.ShouldBe("last");
        gitCalls[0].ShouldBe("wd|rev-parse HEAD");
        var view = remote.GitHub("release-view", new Hashtable { ["tag"] = "v1" }, "co");
        view.ExitCode.ShouldBe(0);
        view.Body.ShouldBeSameAs(body);
        ghCalls.ShouldBe(["release-view|v1|co"]);
    }

    [Test]
    public void Production_github_runs_gh_and_reads_back_the_release_view()
    {
        var launcher = new RecordingLauncher(launch => new NightlyLaunchResult
        {
            ExitCode = 0,
            LogText = launch.CaptureOutput ? "{\"id\":\"RE_1\",\"name\":\"v1\",\"url\":\"https://github.com/o/r/releases/tag/v1\",\"isDraft\":false,\"tagName\":\"v1\"}\n" : "",
        });
        var gh = ReleaseGateRemote.ProductionGitHub(launcher);
        var view = gh("release-view", new Hashtable { ["tag"] = "v1" }, "co");
        var release = view.Body.ShouldBeOfType<System.Collections.Specialized.OrderedDictionary>();
        release["ID"].ShouldBe("RE_1");
        release["isDraft"].ShouldBe(false);
        gh("release-create", new Hashtable { ["tag"] = "v1-rc.1", ["notesFile"] = @"C:\notes dir\n.md", ["prerelease"] = true }, "co").Body.ShouldBeNull();
        gh("release-upload", new Hashtable { ["tag"] = "v1", ["asset"] = "m.json" }, "co");
        gh("release-publish", new Hashtable { ["tag"] = "v1" }, "co");
        launcher.Launches.Select(l => l.FileName).ShouldAllBe(f => f == "gh");
        launcher.Launches.Select(l => l.CaptureOutput).ShouldBe([true, false, false, false]);
        launcher.Launches.Select(l => l.WorkingDirectory).ShouldAllBe(w => w == "co");
        launcher.Launches[0].Arguments.ShouldBe(["release", "view", "v1", "--json", "id,name,url,isDraft,tagName"]);
        launcher.Launches[1].Arguments.ShouldBe(["release", "create", "v1-rc.1", "--verify-tag", "--draft", "--title", "v1-rc.1", "--notes-file", @"C:\notes dir\n.md", "--prerelease"]);
        launcher.Launches[2].Arguments.ShouldBe(["release", "upload", "v1", "m.json", "--clobber"]);
        launcher.Launches[3].Arguments.ShouldBe(["release", "edit", "v1", "--draft=false"]);
        ReleaseGateRemote.GitHubArguments("release-create", new Hashtable { ["tag"] = "v1", ["notesFile"] = "n" }).ShouldNotContain("--prerelease");
        Should.Throw<NightlyRefusal>(() => gh("release-delete", new Hashtable(), "co")).Message.ShouldBe("unknown github verb release-delete");
        var failed = ReleaseGateRemote.ProductionGitHub(new RecordingLauncher(_ => new NightlyLaunchResult { ExitCode = 1, LogText = "release not found" }));
        failed("release-view", new Hashtable { ["tag"] = "v9" }, "co").Body.ShouldBeNull();
    }

    // ---- the production native lock (the live lane's) ------------------------------------------------------------

    [Test]
    public void Production_native_lock_enters_once_and_exits_its_own_record()
    {
        using var fx = Root();
        var coordination = fx.PathOf("coord");
        var owner = ReleaseGateNativeLock.Production();
        var entered = owner.Enter(coordination, "run-1", "fixture-live");
        entered.Ok.ShouldBeTrue();
        File.Exists(ReleaseGatePublication.NativeLockPath(coordination)).ShouldBeTrue();
        var contender = ReleaseGateNativeLock.Production();
        var busy = contender.Enter(coordination, "run-2", "fixture-live");
        busy.Ok.ShouldBeFalse();
        busy.Reason.ShouldBe("deferred-busy");
        contender.Exit(coordination, "run-2");
        File.Exists(ReleaseGatePublication.NativeLockPath(coordination)).ShouldBeTrue();
        owner.Exit(coordination, "run-1");
        File.Exists(ReleaseGatePublication.NativeLockPath(coordination)).ShouldBeFalse();
    }
}
