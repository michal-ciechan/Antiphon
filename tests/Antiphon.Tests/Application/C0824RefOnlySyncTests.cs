using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)] // CARD-0824 provisional; bounded real Git plus one cloned DB per scenario.
public sealed class C0824RefOnlySyncTests
{
    [Test]
    public async Task Pushed_tip_advances_only_owned_ref()
    {
        foreach (var modern in new[] { false, true })
        {
            await using var f = await RefOnlySyncFixture.CreateAsync(modern);
            var w = f.Git;
            var foreign = await w.PublishForeignBranchAsync("feat/card-task-ffffffff", "foreign.txt");
            await w.RunAsync(w.Desktop, "fetch", "origin", "refs/heads/feat/card-task-ffffffff:refs/heads/feat/card-task-ffffffff");
            var main = await w.RevParseAsync("HEAD");
            var index = await w.RunAsync(w.Desktop, "write-tree");
            var bytes = await File.ReadAllBytesAsync(Path.Combine(w.Desktop, "README.md"));
            var registry = await w.RunAsync(w.Desktop, "worktree", "list", "--porcelain");
            var source = await w.RunnerPushAsync("work.txt", "exact runner bytes");
            w.Git.Clear();

            var result = await f.Service().SyncAsync(f.Task, default);

            result.Confirmed.ShouldBeTrue("owned-ref=S: " + result.Reason);
            (await f.TipAsync()).ShouldBe(source, "owned-ref=S");
            result.DesktopBeforeSha.ShouldBe(w.Baseline);
            result.DesktopAfterSha.ShouldBe(source);
            result.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
            result.TaskRefWorkspaceBindingId.ShouldBe(f.Binding.Id);
            (await w.RevParseAsync("HEAD")).ShouldBe(main, "main-intact");
            (await w.RunAsync(w.Desktop, "write-tree")).ShouldBe(index, "main-intact:index");
            (await File.ReadAllBytesAsync(Path.Combine(w.Desktop, "README.md"))).ShouldBe(bytes, "main-intact:bytes");
            (await w.RevParseAsync("refs/heads/feat/card-task-ffffffff")).ShouldBe(foreign, "foreign-intact");
            (await w.RunAsync(w.Desktop, "worktree", "list", "--porcelain")).ShouldBe(registry, "registry-intact");
            (await w.RunAsync(w.Desktop, "show", source + ":work.txt")).ShouldBe("exact runner bytes");
            Directory.Exists(f.Identity).ShouldBeFalse();
            f.AssertNoDesktopCommands();

            var progress = new TaskCompletionProgressService(w.Git);
            var evaluation = await progress.EvaluateAsync(f.Task, "", result, default);
            evaluation.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved, "evaluation-progress-at-S");
            await w.RunAsync(w.Desktop, "update-ref", w.FullRef, w.Baseline, source);
            (await progress.EvaluateAsync(f.Task, "", result, default)).Assessment
                .ShouldBe(CompletionProgressAssessment.Indeterminate, "evaluation-reobserves-ref");
        }
    }

    [Test]
    public async Task Equal_tip_is_idempotent_and_ancestry_failures_preserve_refs()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        var w = f.Git;
        var equal = await f.Service().SyncAsync(f.Task, default);
        equal.Confirmed.ShouldBeTrue("equal-confirmed:baseline");
        w.Git.Commands.ShouldNotContain(c => c.StartsWith("update-ref " + w.FullRef + " ", StringComparison.Ordinal), "equal-no-rewrite");
        var source = await w.RunnerPushAsync("work.txt", "source");
        (await f.Service().SyncAsync(f.Task, default)).Confirmed.ShouldBeTrue("source-confirmed");
        w.Git.Clear();
        (await f.Service().SyncAsync(f.Task, default)).Confirmed.ShouldBeTrue("equal-confirmed:source");
        w.Git.Commands.ShouldNotContain(c => c.StartsWith("update-ref " + w.FullRef + " ", StringComparison.Ordinal), "equal-no-rewrite");

        var ahead = await w.RunnerPushAsync("later.txt", "ahead");
        await w.RunAsync(w.Desktop, "fetch", "origin", w.FullRef);
        await w.RunAsync(w.Desktop, "update-ref", w.FullRef, ahead, source);
        await w.RunAsync(w.Runner, "push", "--force", "origin", source + ":" + w.FullRef);
        w.Git.Clear();
        var refused = await f.Service().SyncAsync(f.Task, default);
        refused.Confirmed.ShouldBeFalse("unconfirmed:local-lineage");
        refused.Reason.ShouldBe(RemoteSettlementSyncReasons.LocalAhead);
        (await f.TipAsync()).ShouldBe(ahead, "ahead-preserved");
        await w.RunAsync(w.Desktop, "update-ref", w.FullRef, w.Baseline, ahead);
        await w.RunAsync(w.Runner, "checkout", "--orphan", "unrelated");
        await w.RunAsync(w.Runner, "commit", "--allow-empty", "-m", "unrelated root");
        var unrelated = await w.RunAsync(w.Runner, "rev-parse", "HEAD");
        await w.RunAsync(w.Runner, "push", "--force", "origin", unrelated + ":" + w.FullRef);
        w.Git.Clear();
        var crossed = false;
        var service = f.Service();
        service.SyncBoundaryAsync = (site, _) => { if (site == "before-lease") crossed = true; return Task.CompletedTask; };
        refused = await service.SyncAsync(f.Task, default);
        refused.Confirmed.ShouldBeFalse("unconfirmed:baseline-lineage");
        refused.Reason.ShouldBe(RemoteSettlementSyncReasons.Diverged);
        crossed.ShouldBeFalse("baseline-refused-before-lease");
        (await f.TipAsync()).ShouldBe(w.Baseline, "unrelated-preserved");
        f.AssertNoDesktopCommands();
    }

    [Test]
    public async Task Concurrent_ref_advance_fails_compare_and_swap()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        var w = f.Git;
        var source = await w.RunnerPushAsync("work.txt", "source");
        var raced = await w.PublishForeignBranchAsync("race-author", "race.txt");
        await w.RunAsync(w.Desktop, "fetch", "origin", "refs/heads/race-author");
        var hit = false;
        w.Git.BeforeCommandAsync = async (_, args, _) =>
        {
            if (args.Count < 2 || args[0] != "update-ref" || args[1] != w.FullRef || hit) return;
            hit = true;
            await w.RunAsync(w.Desktop, "update-ref", w.FullRef, raced, w.Baseline);
        };
        var result = await f.Service().SyncAsync(f.Task, default);
        hit.ShouldBeTrue("reached:S3:cas:before-command");
        result.Confirmed.ShouldBeFalse("race-unconfirmed");
        (await f.TipAsync()).ShouldBe(raced, "race-preserved=T");
        w.Git.BeforeCommandAsync = null;
        await w.RunAsync(w.Desktop, "update-ref", w.FullRef, w.Baseline, raced);
        (await f.Service().SyncAsync(f.Task, default)).Confirmed.ShouldBeTrue("valid-retry");
        (await f.TipAsync()).ShouldBe(source);

        foreach (var wrong in new[] { false, true })
        {
            await w.RunAsync(w.Desktop, "update-ref", w.FullRef, w.Baseline, source);
            var advanced = false;
            w.Git.BeforeCommandAsync = (_, args, _) =>
            {
                if (args.Count > 1 && args[0] == "update-ref" && args[1] == w.FullRef) advanced = true;
                return Task.CompletedTask;
            };
            w.Git.FailCommand = args => advanced && args.Contains(w.FullRef + "^{commit}")
                ? new(wrong ? 0 : 128, wrong ? w.Baseline + "\n" : "", "") : null;
            result = await f.Service().SyncAsync(f.Task, default);
            advanced.ShouldBeTrue("reached:S3:postcondition:after-cas");
            result.Confirmed.ShouldBeFalse("postcondition-unconfirmed");
            w.Git.FailCommand = null;
            w.Git.BeforeCommandAsync = null;
            (await f.TipAsync()).ShouldBe(source);
        }
        f.AssertNoDesktopCommands();
    }

    [Test]
    public async Task Endpoint_pin_and_binding_changes_refuse()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        var w = f.Git;
        var source = await w.RunnerPushAsync("work.txt", "source");
        var snapshot = TaskProgressJson.TryReadBaseline(f.Task.ProgressBaselineJson)!;
        var originalBaseline = f.Task.ProgressBaselineJson;
        foreach (var variant in new[] { "binding-id", "binding-task", "repository", "common", "identity", "full-ref", "base-sha", "base-ref", "base-provenance", "base-owner", "preparation", "baseline-version", "baseline-mode", "baseline-binding" })
        {
            var binding = await f.Db.TaskRefWorkspaceBindings.SingleAsync(b => b.Id == f.Binding.Id);
            switch (variant)
            {
                case "binding-id": f.Task.TaskRefWorkspaceBindingId = Guid.NewGuid(); break;
                case "binding-task": binding.TaskId = Guid.NewGuid(); break;
                case "repository": binding.RepositoryPath = w.Runner; break;
                case "common": binding.CommonDirectory = Path.Combine(w.Runner, ".git"); break;
                case "identity": binding.IdentityPath = Path.Combine(w.Root, "foreign-identity"); break;
                case "full-ref": binding.FullRef = "refs/heads/foreign"; break;
                case "base-sha": binding.SelectedBaseSha = source; break;
                case "base-ref": binding.SelectedBaseRef = "refs/heads/foreign"; break;
                case "base-provenance": binding.SelectedBaseSource = WorktreeBaseSource.Unset; break;
                case "base-owner": binding.SelectedBaseTaskId = Guid.NewGuid(); break;
                case "preparation": binding.PreparationState = TaskRefWorkspacePreparationState.RefCreated; break;
                case "baseline-version": f.Task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(snapshot with { SchemaVersion = 999 }); break;
                case "baseline-mode": f.Task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(snapshot with { Primary = snapshot.Primary with { DesktopWorkspaceKind = DesktopWorkspaceKind.Checkout } }); break;
                case "baseline-binding": f.Task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(snapshot with { Primary = snapshot.Primary with { TaskRefWorkspaceBindingId = Guid.NewGuid() } }); break;
            }
            await f.Db.SaveChangesAsync();
            w.Git.Clear();
            var result = await f.Service().SyncAsync(f.Task, default);
            result.Confirmed.ShouldBeFalse("refused:" + variant + ":initial");
            w.Git.Commands.ShouldNotContain(c => c.StartsWith("ls-remote ", StringComparison.Ordinal), "refused-before-observe:" + variant);
            (await f.TipAsync()).ShouldBe(w.Baseline);
            await f.Db.Entry(binding).ReloadAsync();
            binding.TaskId = f.Task.Id; binding.RepositoryPath = w.Desktop; binding.CommonDirectory = snapshot.Primary.CanonicalCommonDirectory;
            binding.IdentityPath = f.Identity; binding.FullRef = w.FullRef; binding.SelectedBaseSha = w.Baseline;
            binding.SelectedBaseRef = f.Task.WorktreeBaseRef!; binding.SelectedBaseSource = f.Task.WorktreeBaseSource;
            binding.SelectedBaseTaskId = f.Task.WorktreeBaseTaskId; binding.PreparationState = TaskRefWorkspacePreparationState.Published;
            f.Task.TaskRefWorkspaceBindingId = binding.Id; f.Task.ProgressBaselineJson = originalBaseline;
            await f.Db.SaveChangesAsync();
        }
        foreach (var dimension in new[] { "revision", "binding-id", "endpoint", "observation-pin" })
        {
            var service = f.Service();
            var hit = false;
            service.SyncBoundaryAsync = async (site, _) =>
            {
                if (site != "under-lease") return;
                hit = true;
                if (dimension == "revision") { f.Binding.Revision++; await f.Db.SaveChangesAsync(); }
                if (dimension == "binding-id") { await f.Db.AgentTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u => u.SetProperty(t => t.TaskRefWorkspaceBindingId, (Guid?)Guid.NewGuid())); }
                if (dimension == "endpoint") await w.RunAsync(w.Desktop, "remote", "set-url", "origin", w.Runner);
                if (dimension == "observation-pin")
                {
                    var pin = (await w.RunAsync(w.Desktop, "for-each-ref", "--format=%(refname)", "refs/antiphon/progress/" + f.Task.Id.ToString("N")))
                        .Split('\n').First(p => p.Contains("observation", StringComparison.Ordinal));
                    await w.RunAsync(w.Desktop, "update-ref", pin, w.Baseline);
                }
            };
            var result = await service.SyncAsync(f.Task, default);
            hit.ShouldBeTrue("reached:S4:" + dimension + ":under-lease");
            result.Confirmed.ShouldBeFalse("refused:" + dimension + ":under-lease");
            (await f.TipAsync()).ShouldBe(w.Baseline);
            await w.RunAsync(w.Desktop, "remote", "set-url", "origin", w.Origin);
            await f.Db.AgentTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(u => u.SetProperty(t => t.TaskRefWorkspaceBindingId, (Guid?)f.Binding.Id));
        }
        (await f.Service().SyncAsync(f.Task, default)).Confirmed.ShouldBeTrue("valid-counterpart");
        f.AssertNoDesktopCommands();
    }

    [Test]
    public async Task Source_checkout_attachment_is_not_fast_forwarded()
    {
        foreach (var phase in new[] { "initial", "under-lease" })
        {
            await using var f = await RefOnlySyncFixture.CreateAsync();
            var w = f.Git;
            await w.RunnerPushAsync("work.txt", "source");
            var attached = Path.Combine(w.Root, "attached");
            var hit = false;
            async Task AttachAsync()
            {
                await w.RunAsync(w.Desktop, "worktree", "add", attached, w.Branch);
                hit = true;
            }
            var service = f.Service();
            if (phase == "initial") await AttachAsync();
            else service.SyncBoundaryAsync = async (site, _) => { if (site == "under-lease") await AttachAsync(); };
            w.Git.Clear();
            var result = await service.SyncAsync(f.Task, default);
            hit.ShouldBeTrue("reached:S7:" + phase + ":attached");
            result.Confirmed.ShouldBeFalse("attached-ref-preserved:" + phase);
            (await f.TipAsync()).ShouldBe(w.Baseline);
            (await w.RunAsync(attached, "rev-parse", "HEAD")).ShouldBe(w.Baseline);
            File.Exists(Path.Combine(attached, "work.txt")).ShouldBeFalse();
            if (phase == "initial") w.Git.Commands.ShouldNotContain(c => c.StartsWith("ls-remote ", StringComparison.Ordinal), "registration-refused-before-observe");
            await w.RunAsync(w.Desktop, "worktree", "remove", attached);
            (await f.Service().SyncAsync(f.Task, default)).Confirmed.ShouldBeTrue("no-attachment-counterpart");
            f.AssertNoDesktopCommands();
        }
    }

    [Test]
    public async Task Lease_window_and_timeout_leave_no_running_child()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        var w = f.Git;
        await w.RunnerPushAsync("work.txt", "source");
        var clock = new FakeTimeProvider();
        var busy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var held = await w.Leases.TryAcquireAsync(w.Desktop, default))
        {
            held.ShouldNotBeNull();
            var sync = f.Service(clock, () => busy.TrySetResult()).SyncAsync(f.Task, default);
            await busy.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.Advance(RemoteWorkspaceService.DefaultLeaseWaitSlice);
            var result = await sync.WaitAsync(TimeSpan.FromSeconds(10));
            result.Confirmed.ShouldBeFalse("no-unleased-write");
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseWaiting, "lease-window-bounded");
            (await f.TipAsync()).ShouldBe(w.Baseline);
        }
        w.Git.BlockOn = args => args.Count > 1 && args[0] == "update-ref" && args[1] == w.FullRef;
        using var cancel = new CancellationTokenSource();
        var pending = f.Service().SyncAsync(f.Task, cancel.Token);
        await w.Git.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => pending, "caller-cancellation-propagates");
        w.Git.InFlight.ShouldBe(0, "child-exited:caller");
        (await f.TipAsync()).ShouldBe(w.Baseline);
        w.Git.BlockOn = null;
        (await f.Service().SyncAsync(f.Task, default)).Confirmed.ShouldBeTrue("valid-retry");
        f.AssertNoDesktopCommands();
    }
}
