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
/// than <c>WorktreeLegacyRelease:SalvageRefRetentionDays</c>.
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
