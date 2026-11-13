using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// Keep independent cleanup families on separate fixture lifetimes and deadlines (Debug 6130909c).
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)]
public sealed class C0824RefOnlyCleanupTests
{
    [Test]
    [Timeout(30_000)]
    public async Task Retirement_consumers_skip_ref_only_without_deleting_refs()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        foreach (var consumer in new[] { "eligibility", "operator-release", "system-release", "retire-action" })
        {
            var handle = await w.AddTaskAsync(shape: "pushed");
            var sha = (await w.ReadAsync(handle.Path, "rev-parse", "HEAD")).Trim();
            // Seed the stale release before changing representation: action must re-read the task.
            await using var scope = w.Host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == handle.Id);
            var released = await service.ReleaseSystemAsync(task.Id,
                new(task.ConcurrencyToken, sha, "fixture reviewed release"), default);
            var release = await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == released.Id);
            var bound = await BindAsync(w.Host, handle.Id, removeAuthoring: true, identity: handle.Path);
            await db.Entry(task).ReloadAsync();
            var unsafeEntered = false;
            service.DesktopBoundaryAsync = (site, id, ct) => { if (id == task.Id && site == "unsafe:" + consumer) unsafeEntered = true; return Task.CompletedTask; };
            w.ClearTrace();
            if (consumer == "eligibility")
            {
                var result = await service.EvaluateEligibilityAsync(task, release, default);
                result.Authorized.ShouldBeFalse();
                result.Reason.ShouldBe("desktop_not_required", "desktop-not-required:" + consumer);
            }
            else if (consumer == "operator-release")
                (await Should.ThrowAsync<ConflictException>(() => service.ReleaseAsync(task.Id,
                    new(task.ConcurrencyToken, sha, null, true, "reviewed"), new(null, null, ""), default)))
                    .Code.ShouldBe("desktop_not_required", "desktop-not-required:" + consumer);
            else if (consumer == "system-release")
                (await Should.ThrowAsync<ConflictException>(() => service.ReleaseSystemAsync(task.Id,
                    new(task.ConcurrencyToken, sha, "reviewed"), default)))
                    .Code.ShouldBe("desktop_not_required", "desktop-not-required:" + consumer);
            else AssertNotRequired(await service.TryRetireAsync(release, null, default), bound.Id, consumer);
            unsafeEntered.ShouldBeFalse("desktop-not-required:" + consumer + ":no-unsafe-entry");
            (await db.TaskWorktreeRetirementAttempts.CountAsync(a => a.RetirementId == release.Id)).ShouldBe(0);
            (await db.WorkspaceUseReservations.CountAsync(r => r.TaskId == task.Id)).ShouldBe(0);
            await AssertIntactAsync(w.Host, bound, sha, consumer);
        }
    }

    [Test]
    [Timeout(35_000)]
    public async Task Legacy_consumers_skip_ref_only_without_deleting_refs()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        // Candidate, stale precheck and post-inspection action are separate entry points.
        foreach (var consumer in new[] { "legacy-candidate", "legacy-precheck", "legacy-action" })
        {
            var handle = await w.AddTaskAsync(shape: "pushed");
            var sha = (await w.ReadAsync(handle.Path, "rev-parse", "HEAD")).Trim();
            var bound = await BindAsync(w.Host, handle.Id, removeAuthoring: consumer == "legacy-candidate");
            if (consumer != "legacy-candidate")
                await SetCheckoutAsync(w.Host, handle.Id); // Valid stale selection; only mode changes at this boundary.
            var reached = false; var unsafeEntered = false;
            await using var scope = w.Host.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<DesktopLegacyReleasePolicyService>();
            service.DesktopBoundaryAsync = async (site, id, ct) =>
            {
                if (id != handle.Id) return;
                if (site == "before:" + consumer)
                {
                    reached = true;
                    if (consumer != "legacy-candidate") await RestoreRefOnlyAsync(w.Host, handle.Id, bound.Id);
                }
                if (site == "unsafe:" + consumer) unsafeEntered = true;
            };
            w.ClearTrace();
            var run = await service.RunTickAsync(default);
            reached.ShouldBeTrue("desktop-consumer-reached:" + consumer);
            unsafeEntered.ShouldBeFalse("desktop-not-required:" + consumer + ":no-unsafe-entry");
            (await w.RowAsync(run.RunId, handle)).ReasonCode.ShouldBe("desktop_not_required", "desktop-not-required:" + consumer);
            (await w.ReleasesAsync(handle)).ShouldBeEmpty();
            await AssertIntactAsync(w.Host, bound, sha, consumer, forbidSourceCwd: consumer != "legacy-action");
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task Residue_and_health_preserve_ref_only_custody()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        // Read projections retain RefOnly branch custody instead of treating absence as a checkout removal.
        var residueHandle = await w.AddTaskAsync();
        var residueSha = (await w.ReadAsync(residueHandle.Path, "rev-parse", "HEAD")).Trim();
        var residueBinding = await BindAsync(w.Host, residueHandle.Id, removeAuthoring: true);
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "update-ref", "refs/heads/feat/card-task-deadbeef", w.Fixture.SeedSha);
        await using (var scope = w.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var manager = scope.ServiceProvider.GetRequiredService<IWorktreeManager>();
            var settings = Microsoft.Extensions.Options.Options.Create(new Antiphon.Server.Application.Settings.WorktreeResidueSettings { Execute = true, MinSettledMinutes = 120 });
            var gitSettings = Microsoft.Extensions.Options.Options.Create(new Antiphon.Server.Application.Settings.GitSettings { WorktreeBasePath = w.Root });
            var retirement = scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>();
            var persisted = new WorktreeResidueSweepService(db, manager, settings, gitSettings, w.Clock,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<WorktreeResidueSweepService>.Instance, retirement);
            var unsafeSettled = false;
            persisted.DesktopBoundaryAsync = (site, id, ct) => { if (id == residueHandle.Id && site == "unsafe:residue-settled") unsafeSettled = true; return Task.CompletedTask; };
            await persisted.RunAsync(default);
            unsafeSettled.ShouldBeFalse("desktop-not-required:residue-settled:unsafe-entry");
            var run = await db.WorktreeResidueRuns.OrderByDescending(r => r.StartedAt).FirstAsync();
            var row = await db.WorktreeResidueRunCandidates.SingleAsync(r => r.RunId == run.Id && r.TaskId == residueHandle.Id);
            row.ReasonCode.ShouldBe("desktop_not_required", "desktop-not-required:residue-settled");
            row.DirectoryRemoved.ShouldBe(false); row.RegistrationRemoved.ShouldBe(false); row.BranchRemoved.ShouldBe(false);
            var legacy = new WorktreeResidueSweepService(db, manager, settings, gitSettings, w.Clock,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<WorktreeResidueSweepService>.Instance);
            var inventory = await legacy.RunAsync(default);
            var snapshot = inventory.Rows.Single(r => r.TaskId == residueHandle.Id);
            snapshot.Keep.ShouldBeTrue("desktop-not-required:residue-snapshot");
            snapshot.DisplayLabel.ShouldBe("RefOnly");
            snapshot.Detail.ShouldContain(residueBinding.Id.ToString("D"));
            inventory.Removed.ShouldBe(0);
            var health = new WorktreeHealthService(db, manager,
                Microsoft.Extensions.Options.Options.Create(new Antiphon.Server.Application.Settings.DelegationSettings()),
                w.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorktreeHealthService>.Instance);
            var report = await health.SweepAsync(default);
            report.Findings.ShouldNotContain(f => f.TaskId == residueHandle.Id, "ref-only-absence-is-expected");
            report.Findings.ShouldContain(f => f.Branch == "feat/card-task-deadbeef" && f.Shape == "DanglingBranchNoTask",
                "unrelated-real-branch-residue-retained");
            // An attached source is actual custody residue, even when its directory is healthy.
            var registered = Path.Combine(w.Fixture.Root, "attached-ref-only-source");
            await w.Fixture.RequiredAsync(w.Fixture.Repository, "worktree", "add", registered, residueHandle.Branch);
            report = await health.SweepAsync(default);
            report.Findings.ShouldContain(f => f.TaskId == residueHandle.Id && f.Shape == "RefOnlySourceRegistered",
                "desktop-not-required:health-keeps-real-source-registration");
            var collidingId = Guid.Parse(residueHandle.Id8 + "-0000-0000-0000-000000000000");
            collidingId.ShouldNotBe(residueHandle.Id, "discriminating-full-task-identity");
            var keyedByShort = new Dictionary<string, WorktreeHealthService.TaskRow>
            {
                [residueHandle.Id8] = new(collidingId, AgentTaskStatus.Succeeded, w.Fixture.Repository,
                    Path.Combine(w.Root, "foreign"), "feat/card-task-" + residueHandle.Id8 + "-foreign")
            };
            var exactOwner = new WorktreeHealthService.TaskRow(residueHandle.Id, AgentTaskStatus.Succeeded,
                w.Fixture.Repository, residueBinding.Identity, residueHandle.Branch)
                { DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly, TaskRefWorkspaceBindingId = residueBinding.Id };
            WorktreeHealthService.Classify(
                [new(w.Fixture.Repository, registered, residueHandle.Branch, residueHandle.Id8, true, false, null, true, true)],
                keyedByShort, [exactOwner]).ShouldContain(f => f.TaskId == residueHandle.Id && f.Shape == WorktreeHealthShape.RefOnlySourceRegistered,
                    "full-ref-custody-survives-short-id-collision");
            await w.Fixture.RequiredAsync(w.Fixture.Repository, "worktree", "remove", registered);
            var publication = new AgentTaskLanding { Id = Guid.NewGuid(), TaskId = residueHandle.Id, Active = true,
                SourceDesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly, SourceTaskRefWorkspaceBindingId = residueBinding.Id,
                RepositoryPath = w.Fixture.Repository, WorktreePath = residueBinding.Identity, SourceFullRef = residueBinding.FullRef,
                Cleanup = LandCleanupStatus.Refused, Publication = LandPublicationOutcome.Landed };
            db.AgentTaskLandings.Add(publication); await db.SaveChangesAsync();
            var runResult = await persisted.PreviewAsync(null, null, default);
            runResult.Rows.ShouldContain(r => r.LandingOperationId == publication.Id && r.ReasonCode == "desktop_not_required",
                "desktop-not-required:residue-publication");
            w.ClearTrace();
            await AssertIntactAsync(w.Host, residueBinding, residueSha, "residue-and-health");
        }
    }

    [Test]
    [Timeout(55_000)]
    public async Task Landing_and_guarded_cleanup_preserve_ref_only_custody()
    {
        await using (var land = new LandingSafetyHarness())
        {
            await land.InitializeAsync();
            var source = await land.AddSourceAsync();
            var bound = await BindAsync(land, land.Fixture.TaskId, removeAuthoring: true);
            await land.RequestAsync("c0824-controlled-verifier", source); await land.RunQueuedAsync();
            var op = (await land.OperationAsync()).ShouldNotBeNull();
            // Terminal retention owns superseded pins. Exercise cleanup against this real operation's retained source pin.
            bound = bound with { Pin = op.RecoveryRefPrefix + "/source" };
            (await land.Fixture.RequiredAsync(land.Fixture.Repository, "rev-parse", "--verify", bound.Pin))
                .Trim().ShouldBe(source, "protocol-recovery-pin-retained");
            op.DesktopCleanupDisposition.ShouldBe(DesktopCleanupDisposition.NotRequired, "desktop-not-required:protocol-cleanup");
            op.SourceTaskRefWorkspaceBindingId.ShouldBe(bound.Id);
            op.DirectoryRemoved.ShouldBeFalse(); op.RegistrationRemoved.ShouldBeFalse(); op.BranchRemoved.ShouldBeFalse();
            await using var scope = land.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.WorktreeCleanupAttempts.CountAsync(a => a.OperationId == op.Id)).ShouldBe(0);
            await using var lease = await land.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(land.Fixture.Repository, default);
            lease.ShouldNotBeNull();
            var coordinates = new LandSourceCoordinates(bound.TaskId, land.Fixture.Repository, bound.Identity, bound.FullRef, "refs/heads/master")
                { DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly, TaskRefWorkspaceBindingId = bound.Id };
            var request = new WorktreeRemovalRequest(WorktreeRemovalPurpose.Publication, coordinates, bound.Common, "",
                source, source, op.Id, lease!, CleanupContext: new(Guid.NewGuid(), Guid.NewGuid(), op.Id, bound.TaskId));
            land.Fixture.Git.Trace.Clear(); land.Fixture.Git.Commands.Clear();
            var direct = await land.Services.GetRequiredService<WorktreeGuardedCleanup>().RemoveAsync(request,
                land.Services.GetRequiredService<GuardedWorktreeRemoval>(), default);
            AssertNotRequired(direct, bound.Id, "guarded-cleanup");
            await AssertIntactAsync(land, bound, source, "guarded-cleanup");
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task Completed_card_consumers_skip_ref_only_without_deleting_refs()
    {
        await using var card = await CompletedCardCleanupFixture.CreateAsync();
        foreach (var consumer in new[] { "card-discovery", "card-admission", "card-replay", "card-intent" })
        {
            if (consumer != "card-discovery") await card.CreateNextTaskAsync();
            if (consumer != "card-discovery") await card.DiscoverAsync(); // Stale eligible desktop row.
            var cardBinding = await BindAsync(card.Host, card.TaskId, removeAuthoring: false, identity: card.Tree);
            if (consumer == "card-discovery")
            {
                await card.DiscoverAsync();
                await using var read = card.Host.CreateContext();
                var discovered = await read.CardWorktreeCleanupTargets.SingleAsync(t => t.Id == card.TargetId);
                discovered.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly, "desktop-not-required:card-discovery");
                discovered.TaskRefWorkspaceBindingId.ShouldBe(cardBinding.Id);
                discovered.DesktopCleanupDisposition.ShouldBe(DesktopCleanupDisposition.NotRequired);
                (await read.CardWorktreeCleanupEndpoints.CountAsync(e => e.TargetId == discovered.Id && e.EndpointIdentity == "local"))
                    .ShouldBe(0, "desktop-not-required:card-discovery");
                AssertNotRequired(await card.CleanupAsync(), cardBinding.Id, "card-local-entry");
                (await read.CardWorktreeCleanupEndpoints.CountAsync(e => e.TargetId == discovered.Id && e.EndpointIdentity == "local"))
                    .ShouldBe(0, "no-invented-local-endpoint");
                continue;
            }
            await using var db = card.Host.CreateContext();
            var endpoint = await db.CardWorktreeCleanupEndpoints.SingleAsync(e => e.Id == card.EndpointId);
            if (consumer == "card-replay") { endpoint.OperationId = Guid.NewGuid(); await db.SaveChangesAsync(); }
            if (consumer == "card-intent") await SetCheckoutAsync(card.Host, card.TaskId);
            var reached = false; var unsafeEntered = false;
            await using var scope = card.Host.Services.CreateAsyncScope();
            var executor = scope.ServiceProvider.GetRequiredService<CardWorktreeCleanupExecutor>();
            executor.DesktopBoundaryAsync = async (site, id, ct) =>
            {
                if (id != card.TaskId) return;
                if (site == "before:" + consumer) { reached = true; if (consumer == "card-intent") await RestoreRefOnlyAsync(card.Host, id, cardBinding.Id); }
                if (site == "unsafe:" + consumer) unsafeEntered = true;
            };
            AssertNotRequired(await executor.TryAsync(endpoint.Id, null, default), cardBinding.Id, consumer);
            reached.ShouldBeTrue("desktop-consumer-reached:" + consumer);
            unsafeEntered.ShouldBeFalse("desktop-not-required:" + consumer);
            await db.Entry(endpoint).ReloadAsync();
            endpoint.State.ShouldBe(CardWorktreeCleanupEndpointState.NotRequired);
            endpoint.DirectoryRemoved.ShouldBe(false); endpoint.RegistrationRemoved.ShouldBe(false); endpoint.BranchRemoved.ShouldBe(false);
            endpoint.IntentAt.ShouldBeNull("no-desktop-intent");
            (await File.ReadAllTextAsync(card.Sentinel)).ShouldBe(card.OriginalBytes);
        }
    }

    [Test]
    [Timeout(30_000)]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Persistent_reports_survive_ref_only_cleanup(bool temporarySource)
    {
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "Antiphon.sln"))) checkout = checkout.Parent;
        var checkoutRoot = checkout?.FullName ?? throw new InvalidOperationException("checkout missing");
        var nonce = Guid.NewGuid().ToString("N");
        var root = temporarySource ? Path.Combine(Path.GetTempPath(), "c1017", nonce)
            : Path.Combine(checkoutRoot, ".antiphon", "test-output", "c1017", nonce);
        // A fixture-owned sibling of the Code checkout, outside TEMP and both source repositories.
        var reports = Path.Combine(Directory.GetParent(checkoutRoot)!.FullName, ".antiphon-test-evidence", "c0824-r5-" + nonce);
        Directory.Exists(reports).ShouldBeFalse("fresh-persistent-report-root");
        Directory.CreateDirectory(reports);
        try
        {
            await using var card = await CompletedCardCleanupFixture.CreateAsync(sourceRoot: root, reportRoot: reports);
            await using var db = card.Host.CreateContext();
            var row = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == card.TaskId);
            var store = card.Host.Services.GetRequiredService<IAgentReportStore>();
            var options = card.Host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Antiphon.Server.Application.Settings.DelegationSettings>>();
            // Force the configured destination arm, independently of the primary checkout's root.
            row.RepoPath = null; row.WorkingDirectory = Path.GetTempPath();
            var rejectedRoot = Path.Combine(Path.GetTempPath(), "c0824-r5-rejected-" + nonce);
            options.Value.ReportStorageRoot = rejectedRoot;
            (await store.StoreAsync(row, default)).Succeeded.ShouldBeFalse("temporary-report-root-refused");
            Directory.Exists(rejectedRoot).ShouldBeFalse("temporary-report-root-not-created");
            (await File.ReadAllTextAsync(card.Sentinel)).ShouldBe(card.OriginalBytes, "source-bytes-retained");
            options.Value.ReportStorageRoot = reports;
            row.WorkingDirectory = card.Tree; // A linked checkout is not a primary persistent root.
            var receipt = await store.StoreAsync(row, default);
            receipt.Succeeded.ShouldBeTrue("persistent-report-preserved:" + receipt.Reason);
            (await store.IsUsableAsync(receipt.Path, row.Result!, default)).ShouldBeTrue("fresh-preservation-receipt");
            var tracked = await db.AgentTasks.SingleAsync(t => t.Id == row.Id);
            tracked.ResultFilePath = receipt.Path;
            await db.SaveChangesAsync();
            await card.DiscoverAsync();
            var binding = await BindAsync(card.Host, card.TaskId, removeAuthoring: false, identity: card.Tree);
            await SetCheckoutAsync(card.Host, card.TaskId);
            var reached = false;
            await using var scope = card.Host.Services.CreateAsyncScope();
            var executor = scope.ServiceProvider.GetRequiredService<CardWorktreeCleanupExecutor>();
            executor.DesktopBoundaryAsync = async (site, id, ct) =>
            {
                if (id != card.TaskId || site != "before:card-intent") return;
                reached = true;
                await RestoreRefOnlyAsync(card.Host, id, binding.Id);
            };
            AssertNotRequired(await executor.TryAsync(card.EndpointId, null, default), binding.Id, "persistent-root-card-intent");
            reached.ShouldBeTrue("reached:R-5:card-intent:" + temporarySource);
            (await File.ReadAllTextAsync(card.Sentinel)).ShouldBe(card.OriginalBytes);
            (await store.IsUsableAsync(receipt.Path, row.Result!, default)).ShouldBeTrue("report-survives-desktop-refusal");
        }
        finally
        {
            // Only the fresh GUID root created above; no source or shared artifact root is removed.
            if (Directory.Exists(reports)) Directory.Delete(reports, recursive: true);
        }
    }

    [Test]
    public async Task Remote_cleanup_still_uses_its_binding_and_generation()
    {
        await using var f = await CompletedCardMirrorFixture.CreateAsync(MirrorPreparation.Ready);
        // Legacy removal prunes the whole registration root and correctly refuses competing journal custody.
        f.Journal.Read()!.Count.ShouldBe(0, "legacy-removal-has-no-competing-journal-custody");
        await RetirementRemoteActionAsync(f);
        f.Journal.Read()!.Count.ShouldBe(0, "legacy-removal-does-not-borrow-completed-card-authority");
        await f.PrepareNextMirrorAsync(MirrorPreparation.Ready);
        foreach (var preparation in new[] { MirrorPreparation.Ready, MirrorPreparation.Busy })
        {
            if (preparation == MirrorPreparation.Busy) await f.PrepareNextMirrorAsync(preparation);
            var priorJournalEntries = f.Journal.Read()!.Count;
            var binding = await BindAsync(f.Card.Host, f.Card.TaskId, removeAuthoring: true);
            await f.DiscoverAsync();
            var desktop = await f.Card.CleanupAsync();
            AssertNotRequired(desktop, binding.Id, "remote-independent:" + preparation);
            var initial = await f.EndpointAsync();
            initial.State.ShouldBe(CardWorktreeCleanupEndpointState.Pending, "remote-independent-pending");
            // A changed binding must refuse before inspection; recipient validation cannot mask the producer guard.
            var foreignTask = Guid.NewGuid();
            await using (var db = f.Card.Host.CreateContext())
            {
                db.AgentTasks.Add(new AgentTask { Id = foreignTask, RootTaskId = foreignTask, WorkingDirectory = f.Card.Host.Fixture.Repository });
                await db.SaveChangesAsync();
                await db.TaskWorktreeMirrorBindings.Where(b => b.Id == initial.MirrorBindingId)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.TaskId, foreignTask));
            }
            var inspected = f.InspectionRequests;
            await f.HeldAsync("task_snapshot_changed", priorJournalEntries);
            f.InspectionRequests.ShouldBe(inspected, "wrong-binding-refused-before-recipient");
            await using (var db = f.Card.Host.CreateContext())
                await db.TaskWorktreeMirrorBindings.Where(b => b.Id == initial.MirrorBindingId)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.TaskId, f.Card.TaskId));
            foreach (var generation in new int[] { 1, 2 })
            {
                await f.Card.ChangeTaskAsync(t => { t.RemoteWorkspaceSlotId = "fixture-slot"; t.RemoteWorkspaceSlotGeneration = generation;
                    t.RemoteWorkspaceSlotReleasedAt = null; });
                await f.HeldAsync("slot_managed", priorJournalEntries);
                f.InspectionRequests.ShouldBe(inspected, "slot-generation-held:" + generation);
            }
            await f.Card.ChangeTaskAsync(t => { t.RemoteWorkspaceSlotId = null; t.RemoteWorkspaceSlotGeneration = null; });

            var hit = false;
            f.BeforeIntent = _ => { hit = true; throw new IOException("before intent cut"); };
            await Should.ThrowAsync<IOException>(() => f.TryAsync());
            hit.ShouldBeTrue("remote-cut:before-intent");
            (await f.EndpointAsync()).OperationId.ShouldBeNull();
            f.BeforeIntent = null;
            await f.DisconnectAsync(); await f.ReconnectAsync();
            f.AfterIntent = _ => { hit = true; throw new IOException("after intent cut"); };
            hit = false;
            await Should.ThrowAsync<IOException>(() => f.TryAsync());
            hit.ShouldBeTrue("remote-cut:after-intent");
            var intent = await f.EndpointAsync();
            intent.OperationId.ShouldNotBeNull(); intent.ImmutableRequest.ShouldNotBeNull();
            f.SentOperationIds.ShouldBeEmpty("intent-before-send");
            f.AfterIntent = null;
            await f.DisconnectAsync(); await f.ReconnectAsync();

            // Replay store check runs independently of the coordinator's first store read.
            f.LiveStoreOverride = (call, real) => call == 2 ? Guid.NewGuid() : real;
            var sent = f.SentOperationIds.Count;
            await f.HeldAsync("runner_store_mismatch", priorJournalEntries);
            f.SentOperationIds.Count.ShouldBe(sent, "wrong-store-replay-never-sent");
            f.LiveStoreOverride = null;

            hit = false;
            f.BeforeRemoval = (_, _) => { hit = true; throw new Antiphon.Server.Application.Exceptions.ServiceUnavailableException(
                "owned pre-send transport loss", Antiphon.SessionRunner.Contracts.PhoneHomeProblemTypes.Unavailable); };
            (await f.TryAsync()).Complete.ShouldBeFalse();
            hit.ShouldBeTrue("remote-cut:before-send");
            f.Journal.Read()!.Count.ShouldBe(priorJournalEntries);
            f.BeforeRemoval = null; f.Clock.Advance(TimeSpan.FromMinutes(6));
            await f.DisconnectAsync(); await f.ReconnectAsync();

            f.AfterRemoval = _ => new Antiphon.Server.Application.Exceptions.ServiceUnavailableException(
                "owned lost response", Antiphon.SessionRunner.Contracts.PhoneHomeProblemTypes.Unavailable);
            Task? holder = preparation == MirrorPreparation.Busy ? await f.HoldMutationTurnAsync() : null;
            var attempted = f.TryAsync();
            if (holder is not null) await f.ReleaseWhenQueuedAsync(holder, attempted, Antiphon.SessionRunner.Contracts.PhoneHomeOperation.CompletedCardWorkspaceRemove);
            (await attempted).Complete.ShouldBeFalse();
            Directory.Exists(f.Mirror).ShouldBeFalse("remote-recipient-actually-removed");
            f.Journal.Read()!.Count.ShouldBe(priorJournalEntries + 1, "remote-recipient-journal");
            f.AfterRemoval = null; f.Clock.Advance(TimeSpan.FromMinutes(6));
            await f.DisconnectAsync(); await f.ReconnectAsync();
            f.FailReceiptImport = true;
            await Should.ThrowAsync<IOException>(() => f.TryAsync());
            (await f.EndpointAsync()).ImportedReceipt.ShouldBeNull("remote-cut:receipt-import");
            f.FailReceiptImport = false;
            await f.DisconnectAsync(); await f.ReconnectAsync();
            f.AlterReceipt = response => response with { Receipt = response.Receipt! with { PreparationId = Guid.NewGuid() } };
            (await f.TryAsync()).Reason.ShouldBe("receipt_mismatch");
            (await f.EndpointAsync()).ImportedReceipt.ShouldBeNull("wrong-receipt-not-imported");
            f.AlterReceipt = null;
            await f.DisconnectAsync(); await f.ReconnectAsync();
            await f.AssertRemovedAsync(await f.TryAsync(), priorJournalEntries + 1);
            var saved = await f.EndpointAsync();
            saved.OperationId.ShouldBe(intent.OperationId, "same-operation-replay");
            saved.ImmutableRequest.ShouldBe(intent.ImmutableRequest, "same-command-bytes-replay");
            f.SentOperationIds.Distinct().ShouldHaveSingleItem().ShouldBe(intent.OperationId!.Value);
            f.Journal.Read()!.Count.ShouldBe(priorJournalEntries + 1, "no-duplicate-destructive-action");
            if (preparation == MirrorPreparation.Busy)
                f.Order.ShouldContain("queued:" + Antiphon.SessionRunner.Contracts.PhoneHomeOperation.CompletedCardWorkspaceRemove);
            (await File.ReadAllTextAsync(binding.Sentinel)).ShouldBe(binding.SentinelBytes);
            (await f.Card.Host.Fixture.RequiredAsync(f.Card.Host.Fixture.Remote, "rev-parse", binding.FullRef))
                .Trim().ShouldBe(f.Binding.CreationBaselineSha);
        }
    }

    private static async Task RetirementRemoteActionAsync(CompletedCardMirrorFixture f)
    {
        var bound = await BindAsync(f.Card.Host, f.Card.TaskId, removeAuthoring: true);
        await f.Card.ChangeTaskAsync(t => { t.RunnerId = f.Binding.RunnerId; t.RemoteWorktreePath = f.Mirror;
            t.RemoteWorkspaceSlotId = "ws-owned-by-another-generation"; t.RemoteWorkspaceSlotGeneration = 4;
            t.RemoteWorkspaceSlotReleasedAt = null; });
        var remote = new RemoteWorkspaceService(f.Wire.Directory, f.Card.Host.Fixture.Git,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RemoteWorkspaceService>.Instance);
        var commands = 0;
        remote.BeforeRemoveMirrorCommand = () => commands++;
        var task = await f.TaskAsync();
        await remote.RemoveMirrorAsync(task, default);
        commands.ShouldBe(0, "slot-bytes-preserved; no-remove-command");
        (await File.ReadAllTextAsync(f.Sentinel)).ShouldBe("mirror evidence\n", "slot-bytes-preserved");
        task.RemoteWorkspaceSlotGeneration++;
        await remote.RemoveMirrorAsync(task, default);
        commands.ShouldBe(0, "wrong-generation-slot-keeps-its-owner");

        await f.Card.ChangeTaskAsync(t => { t.RemoteWorkspaceSlotId = null; t.RemoteWorkspaceSlotGeneration = null; });
        var prior = f.Card.Host.ConfigureServices;
        // CARD-0824 S4a integration: the no-claim mirror path below must also honour the S4a switch for a system
        // release. Execute stays off for the whole method; the caller-filed release afterwards is not gated.
        f.Card.Host.ConfigureServices = services =>
        {
            prior?.Invoke(services); services.AddSingleton(remote);
            services.PostConfigure<WorktreeLegacyReleaseSettings>(o => o.Execute = false);
        };
        await f.Card.Host.RestartServicesAsync();
        await using var scope = f.Card.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == f.Card.TaskId);
        var report = await File.ReadAllBytesAsync(task.ResultFilePath!);
        var release = new TaskWorktreeRetirement
        {
            Id = Guid.NewGuid(), TaskId = task.Id, TaskAttempt = task.Attempt, TerminalStatus = task.Status,
            TaskCompletedAt = task.CompletedAt!.Value, ReportDigest = CardDoneArtifactPreservation.Digest(task.Result),
            ReleasedTaskRevision = task.ConcurrencyToken, CallerIdentity = TaskWorktreeRetirementService.SystemCallerIdentity,
            ReleaseReason = "fixture mirror release",
            ReleasedAt = f.Clock.GetUtcNow().UtcDateTime, HandoffDispositionJson = "[]", Active = true,
            RepositoryPath = task.RepoPath!, CommonDirectory = bound.Common, WorktreePath = bound.Identity,
            GitDirectory = "", SourceFullRef = bound.FullRef, SourceSha = f.Binding.CreationBaselineSha,
            TargetFullRef = "refs/heads/master", State = WorktreeRetirementState.Released,
            UpdatedAt = f.Clock.GetUtcNow().UtcDateTime
        };
        db.TaskWorktreeRetirements.Add(release); await db.SaveChangesAsync();
        AssertNotRequired(await scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>()
            .TryRetireAsync(release, null, default), bound.Id, "system-release-switch-off");
        commands.ShouldBe(0, "s4a-switch-off-sends-no-mirror-remove");
        Directory.Exists(f.Mirror).ShouldBeTrue("s4a-switch-off-keeps-the-mirror");
        release.CallerIdentity = "operator"; await db.SaveChangesAsync();
        AssertNotRequired(await scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>()
            .TryRetireAsync(release, null, default), bound.Id, "remote-independent-retirement");
        commands.ShouldBe(1, "remote-action-reached");
        Directory.Exists(f.Mirror).ShouldBeFalse("remote-action-recipient-image");
        Directory.Exists(f.Binding.GitDirectory).ShouldBeFalse("remote-action-recipient-registration");
        (await db.TaskWorktreeRetirementAttempts.CountAsync(a => a.RetirementId == release.Id)).ShouldBe(0, "no-desktop-attempt-for-remote-action");
        (await File.ReadAllBytesAsync(task.ResultFilePath!)).ShouldBe(report, "external-report-retained");
        (await File.ReadAllTextAsync(bound.Sentinel)).ShouldBe(bound.SentinelBytes);
        var remoteRef = await f.Card.Host.Fixture.Git.RunAsync(f.Card.Host.Fixture.Remote, ["rev-parse", "--verify", bound.FullRef], default);
        remoteRef.Succeeded.ShouldBeTrue("remote-source-ref-retained");
        remoteRef.Output.Trim().ShouldBe(f.Binding.CreationBaselineSha);
    }

    [Test]
    public async Task Legacy_missing_and_dirty_checkouts_keep_their_guards()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var missing = await w.AddTaskAsync(shape: "none");
        var dirty = await w.AddTaskAsync();
        await File.WriteAllTextAsync(Path.Combine(dirty.Path, "dirty.txt"), "retained dirty bytes");
        var shapedId = Guid.NewGuid();
        var shaped = await w.AddTaskAsync(id: shapedId, path: Path.Combine(w.Root, ".ref-only", shapedId.ToString("N")));
        var clean = await w.AddTaskAsync();
        var origin = await w.OriginRefsAsync();
        var result = await w.TickAsync();
        (await w.RowAsync(result.RunId, missing)).ReasonCode.ShouldBe("directory_absent");
        (await w.RowAsync(result.RunId, dirty)).ReasonCode.ShouldBe("source_dirty");
        (await w.RowAsync(result.RunId, shaped)).ReasonCode.ShouldBe("leaf_mismatch");
        (await File.ReadAllTextAsync(Path.Combine(dirty.Path, "dirty.txt"))).ShouldBe("retained dirty bytes");
        Directory.Exists(shaped.Path).ShouldBeTrue();
        await w.AssertRetiredAsync(clean, origin);
        await AssertMissingCheckoutEligibilityAsync(w, missing);
        await using var db = w.Db();
        foreach (var id in new[] { missing.Id, dirty.Id, shaped.Id, clean.Id })
        {
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
            task.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.Checkout, "legacy-kind=Checkout");
            task.TaskRefWorkspaceBindingId.ShouldBeNull();
        }
    }

    private static async Task AssertMissingCheckoutEligibilityAsync(DesktopLegacyWorld w, DesktopLegacyWorld.Handle missing)
    {
        await using var scope = w.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == missing.Id);
        var release = new TaskWorktreeRetirement { Id = Guid.NewGuid(), TaskId = task.Id, TaskAttempt = task.Attempt,
            Active = true, State = WorktreeRetirementState.Released, TerminalStatus = task.Status,
            ReportDigest = CardDoneArtifactPreservation.Digest(task.Result), RepositoryPath = task.RepoPath!,
            WorktreePath = task.WorktreePath!, SourceFullRef = missing.FullRef };
        var result = await scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>()
            .EvaluateEligibilityAsync(task, release, default);
        result.Reason.ShouldNotBe("desktop_not_required", "no-false-NotRequired");
        (await new TaskWorkspaceContextResolver(db).ResolveAsync(task, default)).Kind.ShouldBe(DesktopWorkspaceKind.Checkout, "legacy-kind=Checkout");
    }

    internal sealed record Binding(Guid Id, Guid TaskId, string Identity, string FullRef, string Common, string Sentinel, string SentinelBytes, string Pin);

    internal static async Task<Binding> BindAsync(LandingSafetyHarness h, Guid taskId, bool removeAuthoring,
        string? identity = null)
    {
        await using var db = h.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        var fullRef = WorkspaceReservationKey.NormalizeRef(task.WorktreeBranch);
        var sha = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", fullRef)).Trim();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", fullRef);
        var originalPath = task.WorktreePath!;
        if (removeAuthoring) await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "remove", originalPath);
        var common = await h.Fixture.Git.CommonDirectoryAsync(h.Fixture.Repository, default);
        // For stale-action cases keep the old path to isolate a mode-only change after an eligible snapshot.
        identity ??= removeAuthoring ? Path.Combine(h.Fixture.Root, "trees", ".ref-only", taskId.ToString("N")) : originalPath;
        if (!Directory.Exists(identity))
        {
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "--detach", identity, sha);
            await File.WriteAllTextAsync(Path.Combine(identity, "foreign.txt"), "foreign decoy bytes");
        }
        var sentinel = removeAuthoring ? Path.Combine(identity, "foreign.txt") : Path.Combine(identity, "keep.txt");
        var sentinelBytes = await File.ReadAllTextAsync(sentinel);
        var pin = "refs/antiphon/land/" + taskId.ToString("N") + "/source";
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", pin, sha);
        var binding = new TaskRefWorkspaceBinding { Id = Guid.NewGuid(), TaskId = task.Id,
            RepositoryPath = h.Fixture.Repository, CommonDirectory = common, IdentityPath = identity,
            Branch = fullRef[11..], FullRef = fullRef, SelectedBaseSha = h.Fixture.SeedSha,
            SelectedBaseRef = "refs/heads/master", SelectedBaseSource = WorktreeBaseSource.Explicit,
            PreparationState = TaskRefWorkspacePreparationState.Published, Revision = 1 };
        task.DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly; task.TaskRefWorkspaceBindingId = binding.Id;
        task.WorktreePath = identity; task.WorktreeBaseSha = binding.SelectedBaseSha;
        task.WorktreeBaseRef = binding.SelectedBaseRef; task.WorktreeBaseSource = binding.SelectedBaseSource;
        db.TaskRefWorkspaceBindings.Add(binding); await db.SaveChangesAsync();
        return new(binding.Id, task.Id, identity, fullRef, common, sentinel, sentinelBytes, pin);
    }

    internal static async Task SetCheckoutAsync(LandingSafetyHarness h, Guid id)
    {
        await using var db = h.CreateContext();
        await db.AgentTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.DesktopWorkspaceKind, DesktopWorkspaceKind.Checkout)
            .SetProperty(t => t.TaskRefWorkspaceBindingId, (Guid?)null));
    }
    internal static async Task RestoreRefOnlyAsync(LandingSafetyHarness h, Guid id, Guid binding)
    {
        await using var db = h.CreateContext();
        await db.AgentTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.DesktopWorkspaceKind, DesktopWorkspaceKind.RefOnly)
            .SetProperty(t => t.TaskRefWorkspaceBindingId, (Guid?)binding));
    }

    private static void AssertNotRequired(WorktreeRemoval result, Guid binding, string consumer)
    {
        result.Residue.ShouldBeNull("desktop-not-required:" + consumer);
        result.DesktopCleanupDisposition.ShouldBe(DesktopCleanupDisposition.NotRequired, "desktop-not-required:" + consumer);
        result.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
        result.TaskRefWorkspaceBindingId.ShouldBe(binding);
        result.IsClean.ShouldBeFalse("no-desktop-removed-claim");
        result.DirectoryGone.ShouldBeFalse(); result.Unregistered.ShouldBeFalse(); result.BranchDeleted.ShouldBeFalse();
    }
    private static async Task AssertIntactAsync(LandingSafetyHarness h, Binding binding, string sha, string consumer, bool forbidSourceCwd = true)
    {
        if (forbidSourceCwd) h.Fixture.Git.Commands.ShouldNotContain(c => LandingGit.PathsEqual(c.Directory, binding.Identity),
            "no-identity-io:" + consumer);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove") || a.Contains("reset") || a.Contains("update-ref"),
            "no-desktop-mutation:" + consumer);
        (await File.ReadAllTextAsync(binding.Sentinel)).ShouldBe(binding.SentinelBytes);
        foreach (var (repository, reference) in new[] { (h.Fixture.Repository, binding.FullRef), (h.Fixture.Repository, binding.Pin), (h.Fixture.Remote, binding.FullRef) })
        {
            var observed = await h.Fixture.Git.RunAsync(repository, ["rev-parse", "--verify", reference], default);
            observed.Succeeded.ShouldBeTrue("source-ref-and-pins-retained:" + consumer);
            observed.Output.Trim().ShouldBe(sha, "source-ref-and-pins-retained:" + consumer);
        }
    }
}
