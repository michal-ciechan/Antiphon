using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// Additional F4 witnesses share the production-dispatched fixture, not a hand-recorded binding.
public sealed partial class C0824RefOnlyProducerLifecycleTests
{
    [Test]
    public async Task Dispatch_settle_and_late_land_preserve_other_task()
    {
        await using var f = await RefOnlyDispatchFixture.CreateAsync(capacity: 2);
        var first = await f.LaunchAsync(await f.SeedAsync());
        var second = await f.LaunchAsync(await f.SeedAsync());
        first.WorktreePath.ShouldNotBe(second.WorktreePath);
        first.TaskRefWorkspaceBindingId.ShouldNotBe(second.TaskRefWorkspaceBindingId);
        first.WorktreeBranch.ShouldNotBe(second.WorktreeBranch);
        var main = await f.Git.RevParseAsync("HEAD");
        var index = await f.Git.RunAsync(f.Git.Desktop, "write-tree");
        var registry = await f.Git.RunAsync(f.Git.Desktop, "worktree", "list", "--porcelain");
        var decoy = f.Git.SnapshotWorktree();
        var source = await f.CommitAsync(first, new Dictionary<string, byte[]> { ["owned.txt"] = Encoding.UTF8.GetBytes("first task at S\n") });
        await using var world = await RunnerSettlementWorld.AttachProducedAsync(f, first);
        var verifier = new LandingSafetyHarness.ControlledVerifier();
        world.ConfigureServices = s => s.AddSingleton<ILandingVerifier>(verifier);
        await world.RestartServicesAsync();
        await world.SettleAsync(RunnerSettlementWorld.Report("Published. " + world.Claim(source)));
        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, world.Task.CompletionProgressEvidenceJson);
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(source);
        (await f.Git.RevParseAsync("HEAD")).ShouldBe(main, "settlement preserves canonical HEAD");
        (await f.Git.RunAsync(f.Git.Desktop, "write-tree")).ShouldBe(index, "settlement preserves canonical index");
        await world.EndDelegateSessionAsync();
        // Late land must use S although the canonical target independently advanced.
        await f.Git.RunAsync(f.Git.Desktop, "commit", "--allow-empty", "-m", "independent late target");
        var target = await f.Git.RevParseAsync("HEAD");
        await f.Git.RunAsync(f.Git.Desktop, "push", "origin", "HEAD:refs/heads/master");
        await using (var scope = world.Services.CreateAsyncScope())
        {
            var land = scope.ServiceProvider.GetRequiredService<AgentTaskLandService>();
            await land.RequestAsync(first.Id, new LandAgentTaskRequest("c0824-controlled-verifier", source), default);
            (await land.RunAsync(first.Id, "c0824-controlled-verifier", default)).ShouldBe(LandRunResult.Complete);
        }
        string? landed = null;
        await using (var db = world.CreateContext())
        {
            var op = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == first.Id && o.RemoteConfirmedAt != null);
            landed = op.VerifiedSourceSha.ShouldNotBeNull();
            op.OriginalSourceSha.ShouldBe(source);
            op.SourceDesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
            op.SourceTaskRefWorkspaceBindingId.ShouldBe(first.TaskRefWorkspaceBindingId);
            op.GitDirectory.ShouldBeNull();
            (await f.Git.RunAsync(f.Git.Origin, "show", "refs/heads/master:owned.txt")).ShouldBe("first task at S");
            (await f.Git.TryRunAsync(f.Git.Origin, "merge-base", "--is-ancestor", target, "refs/heads/master")).ShouldBe(0);
        }
        verifier.Calls.ShouldBeGreaterThan(0);
        (await f.Git.RevParseAsync("refs/heads/" + second.WorktreeBranch)).ShouldBe(f.Git.Baseline);
        (await f.Git.RunAsync(f.Git.Origin, "rev-parse", "refs/heads/" + second.WorktreeBranch)).ShouldBe(f.Git.Baseline);
        f.Git.SnapshotWorktree().ShouldBe(decoy);
        (await f.Git.RevParseAsync("HEAD")).ShouldBe(landed, "ordinary land advances only the canonical target");
        (await f.Git.RunAsync(f.Git.Desktop, "write-tree")).ShouldBe(
            await f.Git.RevParseAsync(landed + "^{tree}"), "canonical index equals the verified landed tree");
        // Land owns its separate temporary checkout; neither source acquires a registration.
        (await f.Git.RunAsync(f.Git.Desktop, "worktree", "list", "--porcelain")).ShouldNotContain("branch refs/heads/" + first.WorktreeBranch);
        registry.ShouldContain("branch " + f.Git.FullRef);
        Path.Exists(first.WorktreePath).ShouldBeFalse(); Path.Exists(second.WorktreePath).ShouldBeFalse();
        main.ShouldBe(f.Git.Baseline);
    }

    [Test]
    public async Task Settlement_race_keeps_exact_compare_and_swap()
    {
        await using var f = await RefOnlyDispatchFixture.CreateAsync();
        var task = await f.LaunchAsync(await f.SeedAsync());
        var source = await f.CommitAsync(task, new Dictionary<string, byte[]> { ["work.txt"] = Encoding.UTF8.GetBytes("settled S\n") });
        var foreign = await f.PublishForeignBranchAsync("feat/race-" + task.Id.ToString("N"), "foreign.txt");
        await f.Git.RunAsync(f.Git.Desktop, "fetch", "origin", foreign);
        var reached = false;
        f.Git.Git.BeforeCommandAsync = async (repo, args, _) =>
        {
            if (!args.Contains("update-ref") || !args.Contains("refs/heads/" + task.WorktreeBranch)) return;
            reached = true;
            f.Git.Git.BeforeCommandAsync = null;
            await f.Git.RunAsync(repo, "update-ref", "refs/heads/" + task.WorktreeBranch, foreign, f.Git.Baseline);
        };
        await using var world = await RunnerSettlementWorld.AttachProducedAsync(f, task);
        await world.SettleAsync(RunnerSettlementWorld.Report("Published. " + world.Claim(source)));
        reached.ShouldBeTrue("reached:production-settlement:exact-CAS");
        (await f.Git.RevParseAsync("refs/heads/" + task.WorktreeBranch)).ShouldBe(foreign);
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBeNull("lost CAS cannot confirm S");
        await using var db = world.CreateContext();
        (await db.AgentTaskSyncDebts.Where(d => d.TaskId == task.Id).ToListAsync())
            .ShouldNotContain(d => d.State == AgentTaskSyncDebtState.Ready);
        Path.Exists(task.WorktreePath).ShouldBeFalse();
    }

    [Test]
    public async Task Blocked_reply_and_restart_keep_mode_when_gate_closes()
    {
        await using var f = await RefOnlyDispatchFixture.CreateAsync();
        var task = await f.LaunchAsync(await f.SeedAsync());
        await using var world = await RunnerSettlementWorld.AttachProducedAsync(f, task);
        await world.SettleAsync("Need a choice. [antiphon-report:" + task.Id + " blocked]", closingVerdict: false);
        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, world.Task.FailureReason);
        f.Gate.CurrentValue.Enabled = false; f.Gate.CurrentValue.RunnerIds = []; f.Gate.CurrentValue.Roles = "none";
        await f.Rig.RestartAsync(); await world.RestartServicesAsync();
        await world.Services.GetRequiredService<AgentTaskReplyService>().AnswerAsync(task.Id,
            "Continue on the same runner.", AnswerOrigin.Cli, null, default);
        await world.ReloadAsync();
        world.Task.Status.ShouldBe(AgentTaskStatus.Working);
        world.Task.TaskRefWorkspaceBindingId.ShouldBe(task.TaskRefWorkspaceBindingId);
        world.Task.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
        await using (var scope = f.Rig.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().RelaunchWedgedAsync(task.Id, task.AgentSessionId!.Value, default);
        var resumed = await f.Rig.ReadTaskAsync(task.Id);
        resumed.AgentSessionId.ShouldNotBe(task.AgentSessionId);
        resumed.BootWedgeRelaunchCount.ShouldBe(1);
        resumed.TaskRefWorkspaceBindingId.ShouldBe(task.TaskRefWorkspaceBindingId);
        await using (var db = f.Rig.NewDb())
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == resumed.AgentSessionId);
            session.RunnerId.ShouldBe(task.RunnerId); session.RunnerCwd.ShouldBe(task.RemoteWorktreePath);
            session.RunnerStoreId.ShouldNotBeNull();
            var queued = await db.SessionQueuedMessages.Where(m => m.AgentSessionId == task.AgentSessionId).ToListAsync();
            queued.ShouldContain(m => m.Body.Contains("Continue on the same runner."));
            // Corrupt a persisted execution coordinate: recovery must refuse before another session.
            session.RunnerCwd = null; await db.SaveChangesAsync();
            var count = await db.AgentSessions.CountAsync();
            await using var scope = f.Rig.Provider.CreateAsyncScope();
            await Should.ThrowAsync<ConflictException>(() => scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .RelaunchWedgedAsync(task.Id, resumed.AgentSessionId!.Value, default));
            (await db.AgentSessions.CountAsync()).ShouldBe(count);
        }
        Path.Exists(task.WorktreePath).ShouldBeFalse();
        Path.Exists(f.Root).ShouldBeFalse();
    }

    [Test]
    public async Task Artifacts_use_one_settled_sha_and_no_identity_files()
    {
        await using var f = await RefOnlyDispatchFixture.CreateAsync();
        var task = await f.LaunchAsync(await f.SeedAsync());
        var bytes = new Dictionary<string, byte[]> { ["docs/a.md"] = Encoding.UTF8.GetBytes("first S\r\n"),
            ["docs/b.md"] = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("second 雪 S\r\n")],
            ["mixed.cs"] = Encoding.UTF8.GetBytes("// source companion\n") };
        var source = await f.CommitAsync(task, bytes);
        await using var world = await RunnerSettlementWorld.AttachProducedAsync(f, task);
        using var root = new RefOnlyPersistentRoot();
        var git = new RefOnlyDeliverableFixture.BlobGit();
        var bundles = new DeliverableBundleService(git, Options.Create(new DeliverablesSettings { StorageRoot = root.Path }),
            NullLogger<DeliverableBundleService>.Instance);
        var moved = false;
        bundles.BeforeCommittedMemberAsync = async (relative, _) =>
        {
            if (relative != "docs/b.md") return;
            moved = true;
            await f.CommitAsync(task, new Dictionary<string, byte[]> { [relative] = Encoding.UTF8.GetBytes("different T\n") });
        };
        world.ConfigureServices = s => s.AddSingleton(bundles); await world.RestartServicesAsync();
        await world.SettleAsync(RunnerSettlementWorld.Report("Wrote `docs/a.md` and `docs/b.md`. " + world.Claim(source)));
        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, world.Task.CompletionProgressEvidenceJson);
        moved.ShouldBeTrue("reached:second-member:origin-moved");
        var manifest = JsonSerializer.Deserialize<DeliverableBundleService.SourceManifest>(
            await File.ReadAllTextAsync(Path.Combine(world.Task.DeliverableBundleDir!, DeliverableBundleService.SourceManifestName)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldNotBeNull();
        manifest.Sources.Count.ShouldBe(2);
        foreach (var member in manifest.Sources)
        {
            var expected = bytes[member.OriginalRelativePath];
            member.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(expected)));
            member.Length.ShouldBe(expected.LongLength);
            (await File.ReadAllBytesAsync(Path.Combine(world.Task.DeliverableBundleDir!, member.StoredFile))).ShouldBe(expected);
        }
        git.ByteReads.ShouldNotBeEmpty(); git.ByteReads.ShouldAllBe(r => r.Sha == source);
        git.LengthReads.ShouldAllBe(r => r.Sha == source);
        Path.Exists(task.WorktreePath).ShouldBeFalse(); Path.Exists(f.Root).ShouldBeFalse();
    }
}
