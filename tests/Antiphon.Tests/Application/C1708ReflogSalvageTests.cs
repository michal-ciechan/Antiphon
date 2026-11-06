using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1708 (operator 2026-11-05, option b with salvage refs). The automatic desktop release (S4a) removes the tree's
/// HEAD reflog and the branch reflog. A commit only those reflogs reach (history rewritten locally, never pushed)
/// gets <c>refs/antiphon/salvage/&lt;taskId&gt;/&lt;sha&gt;</c> before anything moves; a salvage that cannot be
/// written refuses the removal; the off switch writes no ref; the tick prunes a salvage ref only once it is older
/// than <c>WorktreeLegacyRelease:SalvageRefRetentionDays</c>. Review 9222cb00 B-1..B-4: a reflog's old ids, the retention
/// counted from a finished release under the repository lease, the switch at each prune delete, and salvage-name
/// collisions.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
[Timeout(180_000)]
public sealed class C1708ReflogSalvageTests
{
    private const string Prefix = "refs/antiphon/salvage/";

    /// <summary>(i) The rewritten commit is still reachable through its salvage ref once the tree is retired.</summary>
    [Test]
    public async Task Released_tree_keeps_its_reflog_only_commit_under_a_salvage_ref()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var control = await w.AddTaskAsync(ageHours: 60);
        var origin = await w.OriginRefsAsync();

        await w.TickAsync();

        await w.AssertRetiredAsync(a, origin);
        await w.AssertRetiredAsync(control, origin);
        (await SalvageRefsAsync(w)).ShouldBe([Prefix + a.Id.ToString("D") + "/" + lost],
            "one salvage ref, for the rewritten commit only (the pushed control has none)");
        (await w.ReadAsync(w.Fixture.Repository, "rev-parse", "--verify", Prefix + a.Id.ToString("D") + "/" + lost))
            .Trim().ShouldBe(lost);
        (await w.ReadAsync(w.Fixture.Repository, "rev-list", "--all")).ShouldContain(lost,
            Case.Sensitive, "the commit is reachable from a ref, not only from a deleted reflog");
    }

    /// <summary>(ii) A salvage ref that cannot be written (a file where its directory must go) refuses the removal.</summary>
    [Test]
    public async Task Salvage_failure_refuses_the_removal()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var origin = await w.OriginRefsAsync();
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "update-ref", Prefix + a.Id.ToString("D"), lost);
        var before = await w.SnapshotAsync(a.Path);

        var tick = await w.TickAsync();

        (await w.SnapshotAsync(a.Path)).ShouldBe(before, "the tree is byte-intact");
        (await w.RegisteredAsync(a.Path)).ShouldBeTrue("the registration is intact");
        (await w.BranchExistsAsync(a)).ShouldBeTrue("the branch and its reflog are intact");
        (await w.OriginRefsAsync()).ShouldBe(origin);
        (await w.ReadAsync(a.Path, "reflog", "show", "--format=%H", "HEAD")).ShouldContain(lost, Case.Sensitive);
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused));
        row.ReasonCode.ShouldBe("reflog_salvage_failed");
        (await w.ReleasesAsync(a)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);
    }

    /// <summary>(iii) Disabled, dry run and preview write no salvage ref; with the switch on the same tree gets one.</summary>
    [Test]
    public async Task Off_switch_writes_no_salvage_ref()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);

        w.Settings.Value.Enabled = false;
        await w.TickAsync();
        w.Settings.Value.Enabled = true;
        w.Settings.Value.Execute = false;
        await w.TickAsync();
        await w.TickAsync(preview: true);
        w.Settings.Value.Execute = true;
        await w.TickAsync(preview: true);

        (await SalvageRefsAsync(w)).ShouldBeEmpty("no salvage side effect while the switch is off or in a preview");
        await w.AssertIntactAsync(a, before);

        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
        (await SalvageRefsAsync(w)).ShouldBe([Prefix + a.Id.ToString("D") + "/" + lost], "the switch on salvages (control)");
    }

    /// <summary>(iii) Execute turned off as the salvage ref write starts: no ref is written and nothing is removed.</summary>
    [Test]
    public async Task Late_switch_at_the_salvage_write_writes_no_ref_and_keeps_the_tree()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);
        var fired = 0;
        w.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args.Count > 0 && args[0] == "update-ref" && args.Any(x => x.StartsWith(Prefix, StringComparison.Ordinal)))
            {
                fired++;
                w.Settings.Value.Execute = false;
            }
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };

        var tick = await w.TickAsync();

        w.Fixture.Git.BeforeCommand = null;
        fired.ShouldBe(1, "the salvage write was reached once");
        (await SalvageRefsAsync(w)).ShouldBeEmpty();
        (await w.SnapshotAsync(a.Path)).ShouldBe(before, "the tree is byte-intact");
        (await w.RegisteredAsync(a.Path)).ShouldBeTrue();
        (await w.BranchExistsAsync(a)).ShouldBeTrue();
        (await w.RowAsync(tick.RunId, a)).ReasonCode.ShouldBe(TaskWorktreeRetirementService.LegacyReleaseExecuteOff);

        w.Settings.Value.Execute = true;
        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
        (await SalvageRefsAsync(w)).ShouldBe([Prefix + a.Id.ToString("D") + "/" + lost]);
    }

    /// <summary>(iv) The tick keeps a salvage ref younger than the retention (29 of 30 days) and prunes an older one.</summary>
    [Test]
    public async Task Age_pruning_keeps_young_salvage_refs()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        await w.TickAsync();
        var salvage = Prefix + a.Id.ToString("D") + "/" + lost;
        (await SalvageRefsAsync(w)).ShouldBe([salvage]);

        w.Clock.Advance(TimeSpan.FromDays(29));
        await w.TickAsync();
        (await SalvageRefsAsync(w)).ShouldBe([salvage], "29 days is younger than the 30 day retention");

        w.Settings.Value.Execute = false;
        w.Clock.Advance(TimeSpan.FromDays(2));
        await w.TickAsync();
        (await SalvageRefsAsync(w)).ShouldBe([salvage], "a dry run prunes nothing");

        w.Settings.Value.Execute = true;
        await w.TickAsync();
        (await SalvageRefsAsync(w)).ShouldBeEmpty("31 days is past the retention");
    }

    /// <summary>
    /// Review 9222cb00 B-1. The commit entry is gone from both reflogs, so the rewritten commit survives only as the
    /// OLD id of the reset entry; it still gets its salvage ref once the tree is retired.
    /// </summary>
    [Test]
    public async Task Commit_kept_only_as_a_reflog_old_id_gets_a_salvage_ref()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        await w.Fixture.RequiredAsync(a.Path, "reflog", "delete", "HEAD@{1}");
        await w.Fixture.RequiredAsync(a.Path, "reflog", "delete", a.FullRef + "@{1}");
        (await w.ReadAsync(a.Path, "reflog", "show", "--no-abbrev", "--format=%H", "HEAD")).ShouldNotContain(lost, Case.Sensitive);
        (await w.ReadAsync(w.Fixture.Repository, "reflog", "show", "--no-abbrev", "--format=%H", a.FullRef))
            .ShouldNotContain(lost, Case.Sensitive);
        (await w.ReadAsync(w.Fixture.Repository, "rev-list", "--reflog", "--all")).ShouldContain(lost, Case.Sensitive,
            "the reset entry's old id still protects the commit");
        var origin = await w.OriginRefsAsync();

        await w.TickAsync();

        await w.AssertRetiredAsync(a, origin);
        (await SalvageRefsAsync(w)).ShouldBe([Prefix + a.Id.ToString("D") + "/" + lost]);
        (await w.ReadAsync(w.Fixture.Repository, "rev-list", "--all")).ShouldContain(lost, Case.Sensitive);
    }

    /// <summary>
    /// Review 9222cb00 B-2. The first attempt writes the salvage ref and then refuses; the retry 31 days later
    /// finishes. The retention counts from that finished release, so the same tick keeps the ref, and so does 29
    /// days later; 31 days after the completion it is pruned.
    /// </summary>
    [Test]
    public async Task Aged_retry_keeps_its_salvage_ref_for_a_retention_after_completion()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var origin = await w.OriginRefsAsync();
        var salvage = Prefix + a.Id.ToString("D") + "/" + lost;
        var fired = FailVerifyAfterWrite(w, a);

        var first = await w.TickAsync();

        w.Fixture.Git.BeforeCommand = null;
        fired().ShouldBe(1, "the verification after the salvage write failed once");
        (await w.RowAsync(first.RunId, a)).ReasonCode.ShouldBe("reflog_salvage_failed");
        (await SalvageRefsAsync(w)).ShouldBe([salvage], "the refused attempt left its salvage ref");

        w.Clock.Advance(TimeSpan.FromDays(31));
        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
        (await SalvageRefsAsync(w)).ShouldBe([salvage], "the retry just finished: its salvage ref is not old");

        w.Clock.Advance(TimeSpan.FromDays(29));
        await w.TickAsync();
        (await SalvageRefsAsync(w)).ShouldBe([salvage], "29 days after the completion");

        w.Clock.Advance(TimeSpan.FromDays(2));
        await w.TickAsync();
        (await SalvageRefsAsync(w)).ShouldBeEmpty("31 days after the completion (control)");
    }

    /// <summary>
    /// Review 9222cb00 B-2. A release that never finished (refused once, then retry exhausted) keeps its salvage ref
    /// past the retention, while a finished release's ref in the same repository and tick is pruned.
    /// </summary>
    [Test]
    public async Task Unfinished_release_keeps_its_salvage_ref_past_the_retention()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync(s => s.MaxRefusedAttempts = 1);
        var a = await w.AddTaskAsync(ageHours: 60);
        var lostA = await RewriteAsync(w, a);
        var c = await w.AddTaskAsync(ageHours: 60);
        var lostC = await RewriteAsync(w, c);
        var origin = await w.OriginRefsAsync();
        var fired = FailVerifyAfterWrite(w, a);

        await w.TickAsync();

        w.Fixture.Git.BeforeCommand = null;
        fired().ShouldBe(1);
        await w.AssertRetiredAsync(c, origin);
        var salvageA = Prefix + a.Id.ToString("D") + "/" + lostA;
        var salvageC = Prefix + c.Id.ToString("D") + "/" + lostC;
        (await SalvageRefsAsync(w)).Order(StringComparer.Ordinal).ShouldBe(new[] { salvageA, salvageC }.Order(StringComparer.Ordinal));

        w.Clock.Advance(TimeSpan.FromDays(31));
        var tick = await w.TickAsync();

        (await w.RowAsync(tick.RunId, a)).ReasonCode.ShouldBe("retry_exhausted");
        (await w.ReleasesAsync(a)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);
        (await SalvageRefsAsync(w)).ShouldBe([salvageA], "the unfinished release keeps its ref; the finished one is pruned");
        (await w.BranchExistsAsync(a)).ShouldBeTrue();
    }

    /// <summary>Review 9222cb00 B-2. Pruning holds the repository lease the release holds; another holder keeps every ref.</summary>
    [Test]
    public async Task Prune_needs_the_repository_lease()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        await w.TickAsync();
        var salvage = Prefix + a.Id.ToString("D") + "/" + lost;
        (await SalvageRefsAsync(w)).ShouldBe([salvage]);
        w.Clock.Advance(TimeSpan.FromDays(31));

        await using (var lease = await w.Harness.LeaseAsync())
        {
            await w.TickAsync();
            (await SalvageRefsAsync(w)).ShouldBe([salvage], "the lease is held elsewhere: nothing is pruned");
        }

        await w.TickAsync();
        (await SalvageRefsAsync(w)).ShouldBeEmpty("with the lease free the aged ref is pruned (control)");
    }

    /// <summary>
    /// Review 9222cb00 B-3. Execute turned off as the first prune delete starts: that delete never runs, no later
    /// one is attempted and every salvage ref stays.
    /// </summary>
    [Test]
    public async Task Late_switch_at_the_prune_delete_keeps_every_salvage_ref()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lostA = await RewriteAsync(w, a);
        var b = await w.AddTaskAsync(ageHours: 60);
        var lostB = await RewriteAsync(w, b);
        await w.TickAsync();
        var expected = new[] { Prefix + a.Id.ToString("D") + "/" + lostA, Prefix + b.Id.ToString("D") + "/" + lostB }
            .Order(StringComparer.Ordinal).ToArray();
        (await SalvageRefsAsync(w)).Order(StringComparer.Ordinal).ShouldBe(expected);
        w.Clock.Advance(TimeSpan.FromDays(31));
        var fired = 0;
        w.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args.Count > 0 && args[0] == "update-ref" && args.Contains("-d")
                && args.Any(x => x.StartsWith(Prefix, StringComparison.Ordinal)))
            {
                fired++;
                w.Settings.Value.Execute = false;
            }
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };

        await w.TickAsync();

        w.Fixture.Git.BeforeCommand = null;
        fired.ShouldBe(1, "the first delete was reached and no later one was attempted");
        (await SalvageRefsAsync(w)).Order(StringComparer.Ordinal).ToArray().ShouldBe(expected, "the switch went off: nothing is pruned");

        w.Settings.Value.Execute = true;
        await w.TickAsync();
        (await SalvageRefsAsync(w)).ShouldBeEmpty("switch on (control)");
    }

    /// <summary>
    /// Review 9222cb00 B-4. A salvage name that is already a symbolic ref to another branch refuses the removal and
    /// never moves that branch.
    /// </summary>
    [Test]
    public async Task Symbolic_salvage_collision_refuses_and_never_moves_another_branch()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var salvage = Prefix + a.Id.ToString("D") + "/" + lost;
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "branch", "precious", "master");
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "symbolic-ref", salvage, "refs/heads/precious");
        var heads = await w.ReadAsync(w.Fixture.Repository, "for-each-ref", "--format=%(refname) %(objectname)", "refs/heads/");
        var precious = await w.ReadAsync(w.Fixture.Repository, "reflog", "show", "--no-abbrev", "--format=%H %gs", "refs/heads/precious");
        var before = await w.SnapshotAsync(a.Path);

        var tick = await w.TickAsync();

        (await w.ReadAsync(w.Fixture.Repository, "for-each-ref", "--format=%(refname) %(objectname)", "refs/heads/"))
            .ShouldBe(heads, "no branch moved");
        (await w.ReadAsync(w.Fixture.Repository, "reflog", "show", "--no-abbrev", "--format=%H %gs", "refs/heads/precious"))
            .ShouldBe(precious);
        (await w.ReadAsync(w.Fixture.Repository, "symbolic-ref", salvage)).Trim().ShouldBe("refs/heads/precious");
        (await w.SnapshotAsync(a.Path)).ShouldBe(before, "the tree is byte-intact");
        (await w.BranchExistsAsync(a)).ShouldBeTrue();
        (await w.RowAsync(tick.RunId, a)).ReasonCode.ShouldBe("reflog_salvage_collision");
    }

    /// <summary>Review 9222cb00 B-4. A direct salvage name holding another object refuses and is left unchanged.</summary>
    [Test]
    public async Task Direct_salvage_collision_refuses_and_keeps_the_existing_ref()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var lost = await RewriteAsync(w, a);
        var salvage = Prefix + a.Id.ToString("D") + "/" + lost;
        var master = (await w.ReadAsync(w.Fixture.Repository, "rev-parse", "master")).Trim();
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "update-ref", salvage, master);
        var before = await w.SnapshotAsync(a.Path);

        var tick = await w.TickAsync();

        (await w.ReadAsync(w.Fixture.Repository, "for-each-ref", "--format=%(refname) %(objectname) %(symref)", Prefix))
            .Trim().ShouldBe(salvage + " " + master, "the colliding ref is unchanged and still direct");
        (await w.SnapshotAsync(a.Path)).ShouldBe(before, "the tree is byte-intact");
        (await w.BranchExistsAsync(a)).ShouldBeTrue();
        (await w.RowAsync(tick.RunId, a)).ReasonCode.ShouldBe("reflog_salvage_collision");
    }

    /// <summary>
    /// Review 9222cb00 B-4. A symbolic ref under a finished, aged release's salvage scope (named after its target's
    /// object) is never pruned through: the branch it names stays.
    /// </summary>
    [Test]
    public async Task Prune_never_deletes_through_a_symbolic_salvage_ref()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        await RewriteAsync(w, a);
        await w.TickAsync();
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "branch", "precious", "master");
        var master = (await w.ReadAsync(w.Fixture.Repository, "rev-parse", "master")).Trim();
        var planted = Prefix + a.Id.ToString("D") + "/" + master;
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "symbolic-ref", planted, "refs/heads/precious");
        w.Clock.Advance(TimeSpan.FromDays(31));

        await w.TickAsync();

        (await w.ReadAsync(w.Fixture.Repository, "rev-parse", "--verify", "refs/heads/precious")).Trim().ShouldBe(master);
        (await w.ReadAsync(w.Fixture.Repository, "symbolic-ref", planted)).Trim().ShouldBe("refs/heads/precious");
    }

    /// <summary>Fails the first verification read of <paramref name="a"/>'s salvage scope after a salvage ref write.</summary>
    private static Func<int> FailVerifyAfterWrite(DesktopLegacyWorld w, DesktopLegacyWorld.Handle a)
    {
        var scope = Prefix + a.Id.ToString("D") + "/";
        var wrote = false;
        var fired = 0;
        w.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args.Count > 0 && args[0] == "update-ref" && args.Any(x => x.StartsWith(scope, StringComparison.Ordinal)))
                wrote = true;
            else if (wrote && fired == 0 && args.Count > 0 && args[0] == "for-each-ref" && args.Contains(scope))
            {
                fired++;
                return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(new(1, "", "c1708_injected"));
            }
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };
        return () => fired;
    }

    /// <summary>A commit, then <c>reset --hard HEAD~1</c>: clean, at the pushed tip, the commit only in the reflogs.</summary>
    private static async Task<string> RewriteAsync(DesktopLegacyWorld w, DesktopLegacyWorld.Handle a)
    {
        await w.CommitAsync(a, a.Id8 + "-rewritten.txt");
        var lost = (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim();
        await w.Fixture.RequiredAsync(a.Path, "reset", "--hard", "HEAD~1");
        (await w.ReadAsync(w.Fixture.Repository, "rev-list", "--all")).ShouldNotContain(lost, Case.Sensitive);
        return lost;
    }

    private static async Task<string[]> SalvageRefsAsync(DesktopLegacyWorld w) =>
        (await w.ReadAsync(w.Fixture.Repository, "for-each-ref", "--format=%(refname)", Prefix))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
