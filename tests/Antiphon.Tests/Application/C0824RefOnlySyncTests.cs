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
            var foreignPin = "refs/antiphon/progress/ffffffffffffffffffffffffffffffff/keep";
            await w.RunAsync(w.Desktop, "update-ref", foreignPin, foreign);
            if (modern)
            {
                Directory.CreateDirectory(f.Identity);
                await File.WriteAllTextAsync(Path.Combine(f.Identity, "foreign.txt"), "foreign decoy bytes");
            }
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
            (await w.RevParseAsync(foreignPin)).ShouldBe(foreign, "foreign-intact:pin");
            (await w.RunAsync(w.Desktop, "worktree", "list", "--porcelain")).ShouldBe(registry, "registry-intact");
            (await w.RunAsync(w.Desktop, "show", source + ":work.txt")).ShouldBe("exact runner bytes");
            if (modern) (await File.ReadAllTextAsync(Path.Combine(f.Identity, "foreign.txt"))).ShouldBe("foreign decoy bytes", "foreign-intact:identity");
            else Directory.Exists(f.Identity).ShouldBeFalse();
            f.AssertNoDesktopCommands();

            var progress = new TaskCompletionProgressService(w.Git, workspaceContexts: f.Contexts);
            var evaluation = await progress.EvaluateAsync(f.Task, "", result, default);
            evaluation.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved, "evaluation-progress-at-S");
            var evidence = TaskProgressJson.TryReadEvidence(TaskProgressJson.SerializeEvidence(evaluation.Evidence))!.RemoteSync!;
            var original = f.Task.ProgressBaselineJson;
            var snapshot = TaskProgressJson.TryReadBaseline(original)!;
            f.Task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(snapshot with { SchemaVersion = 999 });
            await f.Db.SaveChangesAsync();
            var unsupported = await progress.EvaluateAsync(f.Task, "", result, default);
            f.Task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(snapshot with
            {
                Primary = snapshot.Primary with { LocalSha = source },
            });
            await f.Db.SaveChangesAsync();
            var wrongBase = await progress.EvaluateAsync(f.Task, "", result, default);
            f.Task.ProgressBaselineJson = original;
            await f.Db.SaveChangesAsync();
            var wrongRef = await progress.EvaluateAsync(f.Task, "", result with { FullRef = "refs/heads/foreign" }, default);
            evaluation.ShouldSatisfyAllConditions(
                _ => evidence.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly, "evaluation-mode-durable"),
                _ => evidence.TaskRefWorkspaceBindingId.ShouldBe(f.Binding.Id, "evaluation-binding-durable"),
                _ => unsupported.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate, "evaluation-baseline-version"),
                _ => wrongBase.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate, "evaluation-baseline-base"),
                _ => wrongRef.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate, "evaluation-prepared-ref"));
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
        // The observation pin deliberately does not force-fetch rewritten history. Make this
        // fixture's unrelated object readable so the SUT reaches its independent ancestry guard.
        await w.RunAsync(w.Desktop, "fetch", "origin", w.FullRef);
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
            if (args[0] != "update-ref" || !args.Contains(w.FullRef) || hit) return;
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
                if (args[0] == "update-ref" && args.Contains(w.FullRef)) advanced = true;
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
        foreach (var endpoint in new[] { w.Runner, "" })
        {
            await w.RunAsync(w.Desktop, "remote", "set-url", "origin", endpoint);
            w.Git.Clear();
            (await f.Service().SyncAsync(f.Task, default)).Confirmed.ShouldBeFalse("endpoint-refused-before-observe");
            w.Git.Commands.ShouldNotContain(c => c.StartsWith("ls-remote ", StringComparison.Ordinal), "endpoint-refused-before-observe");
            (await f.TipAsync()).ShouldBe(w.Baseline);
            await w.RunAsync(w.Desktop, "remote", "set-url", "origin", w.Origin);
        }
        var foreignId = Guid.NewGuid();
        f.Db.AgentTasks.Add(new() { Id = foreignId, RootTaskId = foreignId, WorkingDirectory = w.Desktop });
        await f.Db.SaveChangesAsync();
        foreach (var variant in new[] { "binding-id", "binding-task", "repository", "common", "identity", "full-ref", "base-sha", "base-ref", "base-provenance", "base-owner", "preparation", "baseline-version", "baseline-mode", "baseline-binding" })
        {
            var binding = await f.Db.TaskRefWorkspaceBindings.SingleAsync(b => b.Id == f.Binding.Id);
            switch (variant)
            {
                case "binding-id": f.Task.TaskRefWorkspaceBindingId = Guid.NewGuid(); break;
                case "binding-task": binding.TaskId = foreignId; break;
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
            await WriteBindingFaultAsync(f);
            w.Git.Clear();
            var result = await f.Service().SyncAsync(f.Task, default);
            result.Confirmed.ShouldBeFalse("refused:" + variant + ":initial");
            w.Git.Commands.ShouldNotContain(c => c.StartsWith("ls-remote ", StringComparison.Ordinal), "refused-before-observe:" + variant);
            (await f.TipAsync()).ShouldBe(w.Baseline);
            binding.TaskId = f.Task.Id; binding.RepositoryPath = w.Desktop; binding.CommonDirectory = snapshot.Primary.CanonicalCommonDirectory;
            binding.IdentityPath = f.Identity; binding.FullRef = w.FullRef; binding.SelectedBaseSha = w.Baseline;
            binding.SelectedBaseRef = f.Task.WorktreeBaseRef!; binding.SelectedBaseSource = f.Task.WorktreeBaseSource;
            binding.SelectedBaseTaskId = f.Task.WorktreeBaseTaskId; binding.PreparationState = TaskRefWorkspacePreparationState.Published;
            f.Task.TaskRefWorkspaceBindingId = binding.Id; f.Task.ProgressBaselineJson = originalBaseline;
            await WriteBindingFaultAsync(f);
        }
        foreach (var dimension in new[] { "revision", "binding-id", "endpoint", "observation-pin" })
        {
            await w.RunnerPushAsync("gap-" + dimension + ".txt", dimension);
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
                        .Split('\n').First(p => p.Contains("observe-", StringComparison.Ordinal));
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
        w.Git.FailCommand = args => args[0] == "update-ref" && args.Contains(RemoteWorkspaceService.SettlementPin(f.Task.Id))
            ? new(128, "", "pin refusal") : null;
        var noPin = await f.Service().SyncAsync(f.Task, default);
        noPin.Confirmed.ShouldBeFalse("refused:settlement-pin:under-lease");
        (await f.TipAsync()).ShouldBe(w.Baseline);
        w.Git.FailCommand = null;
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
        w.Git.BlockOn = args => args[0] == "update-ref" && args.Contains(w.FullRef);
        using var cancel = new CancellationTokenSource();
        var pending = f.Service().SyncAsync(f.Task, cancel.Token);
        await w.Git.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => pending, "caller-cancellation-propagates");
        w.Git.InFlight.ShouldBe(0, "child-exited:caller");
        (await f.TipAsync()).ShouldBe(w.Baseline);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        w.Git.OnBlocked = () => blocked.TrySetResult();
        var timed = f.Service(clock).SyncAsync(f.Task, default);
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(120));
        var timedResult = await timed.WaitAsync(TimeSpan.FromSeconds(10));
        timedResult.Reason.ShouldBe(RemoteSettlementSyncReasons.Timeout);
        w.Git.InFlight.ShouldBe(0, "child-exited:timeout");
        (await f.TipAsync()).ShouldBe(w.Baseline);
        w.Git.BlockOn = null;
        (await f.Service().SyncAsync(f.Task, default)).Confirmed.ShouldBeTrue("valid-retry");
        f.AssertNoDesktopCommands();
    }

    // These are corruption inputs, not normal EF writes: the production model deliberately makes
    // these binding fields immutable. ExecuteUpdate bypasses that writer rule to exercise readers.
    private static async Task WriteBindingFaultAsync(RefOnlySyncFixture f)
    {
        var b = f.Binding;
        // Setting an EF entry Unchanged rejects its pending values. Capture the corrupt input
        // first; otherwise ExecuteUpdate would silently write the restored original tuple.
        var fault = new { b.TaskId, b.RepositoryPath, b.CommonDirectory, b.IdentityPath, b.FullRef,
            b.SelectedBaseSha, b.SelectedBaseRef, b.SelectedBaseSource, b.SelectedBaseTaskId, b.PreparationState };
        f.Db.Entry(b).State = EntityState.Unchanged;
        await f.Db.SaveChangesAsync();
        await f.Db.TaskRefWorkspaceBindings.Where(row => row.Id == b.Id).ExecuteUpdateAsync(u => u
            .SetProperty(row => row.TaskId, fault.TaskId).SetProperty(row => row.RepositoryPath, fault.RepositoryPath)
            .SetProperty(row => row.CommonDirectory, fault.CommonDirectory).SetProperty(row => row.IdentityPath, fault.IdentityPath)
            .SetProperty(row => row.FullRef, fault.FullRef).SetProperty(row => row.SelectedBaseSha, fault.SelectedBaseSha)
            .SetProperty(row => row.SelectedBaseRef, fault.SelectedBaseRef).SetProperty(row => row.SelectedBaseSource, fault.SelectedBaseSource)
            .SetProperty(row => row.SelectedBaseTaskId, fault.SelectedBaseTaskId).SetProperty(row => row.PreparationState, fault.PreparationState));
        var stored = await f.Db.TaskRefWorkspaceBindings.AsNoTracking().SingleAsync(row => row.Id == b.Id);
        new { stored.TaskId, stored.RepositoryPath, stored.CommonDirectory, stored.IdentityPath, stored.FullRef,
            stored.SelectedBaseSha, stored.SelectedBaseRef, stored.SelectedBaseSource, stored.SelectedBaseTaskId, stored.PreparationState }
            .ShouldBe(fault, "fault-input-reached-durable-binding");
    }
}
