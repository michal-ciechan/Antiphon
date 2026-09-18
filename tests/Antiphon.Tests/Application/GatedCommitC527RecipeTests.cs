using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0527 S1 (D-1): the identity resolver is two `git log` legs and never `--reflog`. The full
/// reflog walk cost 74-135s in a many-worktree checkout and blew the 15s git budget on every
/// eligible settle since go-live.
/// </summary>
public sealed partial class GatedCommitServiceTests
{
    // A-1: the exact recipe, in order, with no --reflog anywhere.
    [Test]
    public async Task C527_identity_search_is_refs_then_head_reflog_and_never_uses_reflog_walk()
    {
        using var repo = await SeedAsync();
        var (_, spy) = Gate();
        var taskId = Guid.NewGuid();
        const string settlement = "recipe-settlement";

        var result = await spy.FindSettlementCommitsAsync(repo.Path, taskId, settlement, CancellationToken.None);
        result.Succeeded.ShouldBeTrue();

        var logs = spy.Calls.Where(c => c.Length > 0 && c[0] == "log" && c.Contains("--all-match")).ToList();
        logs.Count.ShouldBe(2);
        logs[0].ShouldBe(new[]
        {
            "log", "--all", "--fixed-strings", "--all-match",
            $"--grep={taskId:D}", $"--grep={settlement}", "--format=%H",
        });
        logs[1].ShouldBe(new[]
        {
            "log", "--walk-reflogs", "HEAD", "--fixed-strings", "--all-match",
            $"--grep={taskId:D}", $"--grep={settlement}", "--format=%H",
        });
        spy.Calls.ShouldAllBe(c => !c.Contains("--reflog"));
    }

    // A-2: a failing reflog leg fails the whole resolution; RecoverAsync surfaces it as 503.
    [Test]
    public async Task C527_reflog_leg_failure_fails_the_resolution()
    {
        using var repo = await SeedAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "selected.md"), "selected");
        var (gate, spy) = Gate();
        var trailers = Trailers();
        var taskId = Guid.Parse(trailers.Single(t => t.Key == "antiphon-task").Value);
        const string settlement = "reflog-leg-settlement";
        var committed = await gate.CommitAsync(repo.Path, ["selected.md"], Message(),
            [.. trailers, ("antiphon-settlement", settlement)], CancellationToken.None);
        committed.Outcome.ShouldBe(GatedCommitOutcome.Committed);
        var operationId = Guid.Parse((await repo.GitReadAsync("log", "-1",
            "--format=%(trailers:key=antiphon-operation,valueonly)")).Trim());

        spy.OverrideRun = args => args.Contains("--walk-reflogs") ? (128, "", "reflog unavailable") : null;

        var found = await spy.FindSettlementCommitsAsync(repo.Path, taskId, settlement, CancellationToken.None);
        found.Succeeded.ShouldBeFalse();
        found.Error.ShouldBe("reflog unavailable");
        found.Items.ShouldBeEmpty();
        await Should.ThrowAsync<ServiceUnavailableException>(() =>
            gate.RecoverAsync(repo.Path, taskId, operationId, CancellationToken.None));
    }

    // A-3: leg 1 runs first and short-circuits, so the six existing --all injections keep meaning.
    [Test]
    public async Task C527_refs_leg_failure_short_circuits_before_the_reflog_leg()
    {
        using var repo = await SeedAsync();
        var (_, spy) = Gate();
        spy.OverrideRun = args => args.Contains("--all") ? (128, "", "history unavailable") : null;

        var found = await spy.FindSettlementCommitsAsync(
            repo.Path, Guid.NewGuid(), "short-circuit", CancellationToken.None);

        found.Succeeded.ShouldBeFalse();
        found.Error.ShouldBe("history unavailable");
        spy.Calls.ShouldAllBe(c => !c.Contains("--walk-reflogs"));
    }

    // A-4: an unborn HEAD has no reflog to walk; refs alone cover it.
    [Test]
    public async Task C527_unborn_head_skips_the_reflog_leg_and_still_recovers_from_refs()
    {
        using var repo = await SeedAsync();
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "selected.md"), "selected");
        var (gate, spy) = Gate();
        var trailers = Trailers();
        var taskId = Guid.Parse(trailers.Single(t => t.Key == "antiphon-task").Value);
        const string settlement = "unborn-settlement";
        var committed = await gate.CommitAsync(repo.Path, ["selected.md"], Message(),
            [.. trailers, ("antiphon-settlement", settlement)], CancellationToken.None);
        committed.Outcome.ShouldBe(GatedCommitOutcome.Committed);

        await repo.GitAsync("switch", "-c", "other", baseline);
        await repo.GitAsync("symbolic-ref", "HEAD", "refs/heads/unborn");
        spy.Calls.Clear();

        var found = await spy.FindSettlementCommitsAsync(repo.Path, taskId, settlement, CancellationToken.None);

        found.Succeeded.ShouldBeTrue();
        found.Items.ShouldBe(new[] { committed.Sha! });
        spy.Calls.ShouldAllBe(c => !c.Contains("--walk-reflogs"));
    }
}
