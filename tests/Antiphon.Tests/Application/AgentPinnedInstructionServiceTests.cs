using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentPinnedInstructionServiceTests
{
    [Test]
    public async Task V01_empty_migrated_store_has_no_pin_rows_or_io()
    {
        await using var world = await World.CreateAsync();
        (await world.Db.AgentPinnedInstructions.CountAsync()).ShouldBe(0);
        (await world.Db.AgentPinnedInstructionStates.CountAsync()).ShouldBe(0);
        (await world.Db.AgentPinProjections.CountAsync()).ShouldBe(0);
        (await world.Db.AgentPinReconciliations.CountAsync()).ShouldBe(0);
        (await world.Db.AgentPinOperations.CountAsync()).ShouldBe(0);
        (await world.Db.AgentPinCleanupRecords.CountAsync()).ShouldBe(0);
        world.Reconciler.CallCount.ShouldBe(0);
    }

    [Test]
    public async Task V01_capture_persists_immutable_text_provenance_and_first_use_intent()
    {
        await using var world = await World.CreateAsync();
        var agent = await world.SeedAgentAsync();
        var requestId = Guid.NewGuid();
        var result = await world.Service.CaptureAsync(
            agent.Id,
            new CapturePinnedInstructionRequest(
                requestId,
                0,
                "  keep this \r\nline  ",
                "kb",
                "row-1",
                "KB f5792203"),
            world.Operator,
            CancellationToken.None);

        result.CreatedNewRow.ShouldBeTrue();
        result.Set.Revision.ShouldBe(1);
        result.Set.FirstUsedAt.ShouldNotBeNull();
        result.Set.ContentHash.ShouldNotBeNull();
        result.Set.ContentHash!.Length.ShouldBe(64);
        result.Set.Reconciliation!.Status.ShouldBe(PinProjectionStatus.Pending);
        result.Set.Pins.ShouldHaveSingleItem();
        result.Set.Pins[0].Text.ShouldBe("keep this \nline");
        result.Set.Pins[0].Source.ShouldBe(PinInstructionSource.Operator);
        result.Set.Pins[0].SourceKey.ShouldBe("row-1");
        result.Set.Projections.ShouldHaveSingleItem();
        var projection = result.Set.Projections[0];
        projection.TargetRelativePath.ShouldBe(AgentPinPaths.RelativePath(agent.Id));
        projection.TargetAbsolutePath.ShouldBe(AgentPinPaths.AbsolutePath(
            AgentPinPaths.CanonicalCwd(agent.WorkingDirectory), agent.Id));
        projection.Status.ShouldBe(PinProjectionStatus.Pending);
        world.Reconciler.CallCount.ShouldBe(1);

        await using var verify = world.FreshDb();
        var stored = await verify.AgentPinnedInstructions.SingleAsync(p => p.AgentId == agent.Id);
        stored.Text.ShouldBe("keep this \nline");
        stored.CreatedByUserId.ShouldBe(world.Operator.UserId);
        var state = await verify.AgentPinnedInstructionStates.SingleAsync(s => s.AgentId == agent.Id);
        state.Revision.ShouldBe(1);
        state.FirstUsedAt.ShouldBe(result.Set.FirstUsedAt!.Value, TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task V01_failure_before_commit_leaves_nothing_and_skips_io()
    {
        await using var world = await World.CreateAsync();
        var agent = await world.SeedAgentAsync();
        var interceptor = new ThrowOnceSaveInterceptor(onSaving: true);
        await using var db = world.FreshDb(interceptor);
        var reconciler = new NoOpAgentPinnedInstructionReconciler();
        var service = new AgentPinnedInstructionService(db, world.Events, TimeProvider.System, reconciler);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            service.CaptureAsync(
                agent.Id,
                new CapturePinnedInstructionRequest(Guid.NewGuid(), 0, "do not persist"),
                world.Operator,
                CancellationToken.None));

        await using var verify = world.FreshDb();
        (await verify.AgentPinnedInstructions.CountAsync(p => p.AgentId == agent.Id)).ShouldBe(0);
        (await verify.AgentPinnedInstructionStates.CountAsync(s => s.AgentId == agent.Id)).ShouldBe(0);
        (await verify.AgentPinProjections.CountAsync(p => p.AgentId == agent.Id)).ShouldBe(0);
        reconciler.CallCount.ShouldBe(0);
    }

    [Test]
    public async Task V01_failure_after_commit_before_reconcile_keeps_durable_intent()
    {
        await using var world = await World.CreateAsync();
        var agent = await world.SeedAgentAsync();
        var throwing = new ThrowingReconciler();
        var service = new AgentPinnedInstructionService(world.Db, world.Events, TimeProvider.System, throwing);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            service.CaptureAsync(
                agent.Id,
                new CapturePinnedInstructionRequest(Guid.NewGuid(), 0, "survive the gap"),
                world.Operator,
                CancellationToken.None));

        throwing.Calls.ShouldBe(1);
        await using var verify = world.FreshDb();
        (await verify.AgentPinnedInstructions.CountAsync(p => p.AgentId == agent.Id)).ShouldBe(1);
        var state = await verify.AgentPinnedInstructionStates.SingleAsync(s => s.AgentId == agent.Id);
        state.Revision.ShouldBe(1);
        var reconciliation = await verify.AgentPinReconciliations.SingleAsync(r => r.AgentId == agent.Id);
        reconciliation.Status.ShouldBe(PinProjectionStatus.Pending);
        (await verify.AgentPinProjections.CountAsync(p => p.AgentId == agent.Id)).ShouldBe(1);
    }

    [Test]
    public async Task V01_exact_request_id_replay_is_noop()
    {
        await using var world = await World.CreateAsync();
        var agent = await world.SeedAgentAsync();
        var requestId = Guid.NewGuid();
        var first = await world.Service.CaptureAsync(
            agent.Id,
            new CapturePinnedInstructionRequest(requestId, 0, "same", "kb", "k1"),
            world.Operator,
            CancellationToken.None);
        world.Events.Clear();
        world.Reconciler.Calls.Count.ShouldBe(1);

        var replay = await world.Service.CaptureAsync(
            agent.Id,
            new CapturePinnedInstructionRequest(requestId, 0, "same", "kb", "k1"),
            world.Operator,
            CancellationToken.None);

        replay.CreatedNewRow.ShouldBeFalse();
        replay.Set.Revision.ShouldBe(1);
        replay.Set.Pins.ShouldHaveSingleItem();
        replay.Set.Pins[0].Id.ShouldBe(first.Set.Pins[0].Id);
        world.Reconciler.CallCount.ShouldBe(1);
        world.Events.PublishedEvents.ShouldBeEmpty();
        (await world.Db.AgentPinOperations.CountAsync(o => o.AgentId == agent.Id)).ShouldBe(1);
    }

    [Test]
    public async Task V01_same_source_same_text_and_revoked_revoke_are_nops()
    {
        await using var world = await World.CreateAsync();
        var agent = await world.SeedAgentAsync();
        var first = await world.Service.CaptureAsync(
            agent.Id,
            new CapturePinnedInstructionRequest(Guid.NewGuid(), 0, "keep", "kb", "k1"),
            world.Operator,
            CancellationToken.None);
        var same = await world.Service.CaptureAsync(
            agent.Id,
            new CapturePinnedInstructionRequest(Guid.NewGuid(), 1, "keep", "kb", "k1"),
            world.Operator,
            CancellationToken.None);
        same.CreatedNewRow.ShouldBeFalse();
        same.Set.Revision.ShouldBe(1);

        var revoked = await world.Service.RevokeAsync(
            agent.Id,
            first.Set.Pins[0].Id,
            new RevokePinnedInstructionRequest(Guid.NewGuid(), 1),
            world.Operator,
            CancellationToken.None);
        revoked.Revision.ShouldBe(2);
        var again = await world.Service.RevokeAsync(
            agent.Id,
            first.Set.Pins[0].Id,
            new RevokePinnedInstructionRequest(Guid.NewGuid(), 2),
            world.Operator,
            CancellationToken.None);
        again.Revision.ShouldBe(2);
        (await world.Db.AgentPinnedInstructions.CountAsync(p => p.AgentId == agent.Id)).ShouldBe(1);
    }

    [Test]
    public async Task V01_changed_request_id_fingerprint_conflicts_even_with_stale_revision()
    {
        await using var world = await World.CreateAsync();
        var agent = await world.SeedAgentAsync();
        var requestId = Guid.NewGuid();
        await world.Service.CaptureAsync(
            agent.Id,
            new CapturePinnedInstructionRequest(requestId, 0, "first"),
            world.Operator,
            CancellationToken.None);

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            world.Service.CaptureAsync(
                agent.Id,
                new CapturePinnedInstructionRequest(requestId, 0, "different"),
                world.Operator,
                CancellationToken.None));
        ex.Code.ShouldBe(AgentPinnedInstructionService.RequestConflict);
    }

    [Test]
    public async Task V01_full_id_path_rename_locations_and_cleanup_cascade()
    {
        await using var world = await World.CreateAsync();
        var prefix = Guid.NewGuid().ToString("N")[..8];
        var idA = Guid.Parse(prefix + Guid.NewGuid().ToString("N")[8..]);
        var idB = Guid.Parse(prefix + Guid.NewGuid().ToString("N")[8..]);
        while (idB == idA)
            idB = Guid.Parse(prefix + Guid.NewGuid().ToString("N")[8..]);

        var cwd = $@"D:\src\shared-{prefix}";
        var agentA = await world.SeedAgentAsync(idA, cwd, name: "Alpha");
        var agentB = await world.SeedAgentAsync(idB, cwd, name: "Beta");
        await world.Service.CaptureAsync(
            agentA.Id,
            new CapturePinnedInstructionRequest(Guid.NewGuid(), 0, "A_ONLY_one"),
            world.Operator,
            CancellationToken.None);
        await world.Service.CaptureAsync(
            agentB.Id,
            new CapturePinnedInstructionRequest(Guid.NewGuid(), 0, "B_ONLY_one"),
            world.Operator,
            CancellationToken.None);

        var pathA = AgentPinPaths.AbsolutePath(AgentPinPaths.CanonicalCwd(cwd), agentA.Id);
        var pathB = AgentPinPaths.AbsolutePath(AgentPinPaths.CanonicalCwd(cwd), agentB.Id);
        pathA.ShouldNotBe(pathB);
        pathA.ShouldContain(agentA.Id.ToString("N"));
        pathA.ShouldNotContain("alpha");

        agentA.Name = "Renamed";
        await world.Db.SaveChangesAsync();
        var afterRename = await world.Service.GetAsync(agentA.Id, world.Operator, false, CancellationToken.None);
        afterRename.Projections[0].TargetAbsolutePath.ShouldBe(pathA);
        afterRename.Revision.ShouldBe(1);

        var otherHost = await world.Service.EnsureLocationAsync(
            agentA.Id, "other-host", cwd, configuredConsumer: true, CancellationToken.None);
        otherHost.LocationGeneration.ShouldBe(2);
        otherHost.CanonicalHost.ShouldBe("other-host");
        var defaultLocation = (await world.Db.AgentPinProjections
            .SingleAsync(p => p.AgentId == agentA.Id && p.CanonicalHost == "local"));
        defaultLocation.LocationGeneration.ShouldBe(1);

        await world.Service.RecordProjectionWriteAsync(defaultLocation.Id, 1, "abc", CancellationToken.None);
        (await world.Db.AgentPinProjections.SingleAsync(p => p.Id == defaultLocation.Id))
            .Status.ShouldBe(PinProjectionStatus.Ready);
        (await world.Db.AgentPinProjections.SingleAsync(p => p.Id == otherHost.Id))
            .Status.ShouldBe(PinProjectionStatus.Pending);

        await world.Service.PreserveCleanupOnDeleteAsync(agentA.Id, CancellationToken.None);
        world.Db.Agents.Remove(await world.Db.Agents.SingleAsync(a => a.Id == agentA.Id));
        await world.Db.SaveChangesAsync();

        (await world.Db.AgentPinnedInstructions.CountAsync(p => p.AgentId == agentA.Id)).ShouldBe(0);
        (await world.Db.AgentPinCleanupRecords.CountAsync(c => c.OriginalAgentId == agentA.Id)).ShouldBe(2);
        (await world.Db.AgentPinnedInstructions.CountAsync(p => p.AgentId == agentB.Id)).ShouldBe(1);
    }

    [Test]
    public async Task V02_race_two_captures_one_wins_and_retry_cannot_exceed_20()
    {
        await using var world = await World.CreateAsync();
        var agent = await world.SeedAgentAsync();
        for (var i = 0; i < 19; i++)
        {
            await world.Service.CaptureAsync(
                agent.Id,
                new CapturePinnedInstructionRequest(Guid.NewGuid(), i, $"pin-{i}"),
                world.Operator,
                CancellationToken.None);
        }

        await using var db1 = world.FreshDb();
        await using var db2 = world.FreshDb();
        var left = new AgentPinnedInstructionService(db1, new MockEventBus(), TimeProvider.System, new NoOpAgentPinnedInstructionReconciler());
        var right = new AgentPinnedInstructionService(db2, new MockEventBus(), TimeProvider.System, new NoOpAgentPinnedInstructionReconciler());
        var t1 = left.CaptureAsync(agent.Id, new CapturePinnedInstructionRequest(Guid.NewGuid(), 19, "race-a"), world.Operator, CancellationToken.None);
        var t2 = right.CaptureAsync(agent.Id, new CapturePinnedInstructionRequest(Guid.NewGuid(), 19, "race-b"), world.Operator, CancellationToken.None);
        var results = await Task.WhenAll(Wrap(t1), Wrap(t2));
        results.Count(r => r.Ok).ShouldBe(1);
        results.Count(r => r.Error is ConflictException { Code: AgentPinnedInstructionService.RevisionConflict }).ShouldBe(1);

        await using var verify = world.FreshDb();
        (await verify.AgentPinnedInstructions.CountAsync(p => p.AgentId == agent.Id && p.RevokedAt == null)).ShouldBe(20);

        var state = await verify.AgentPinnedInstructionStates.SingleAsync(s => s.AgentId == agent.Id);
        var retry = await Should.ThrowAsync<ValidationException>(() =>
            world.Service.CaptureAsync(
                agent.Id,
                new CapturePinnedInstructionRequest(Guid.NewGuid(), state.Revision, "twenty-first"),
                world.Operator,
                CancellationToken.None));
        retry.StatusCode.ShouldBe(422);
        (await verify.AgentPinnedInstructions.CountAsync(p => p.AgentId == agent.Id && p.RevokedAt == null)).ShouldBe(20);
    }

    [Test]
    public async Task V02_race_replace_and_revoke_never_duplicates_or_drops_both()
    {
        await using var world = await World.CreateAsync();
        var agent = await world.SeedAgentAsync();
        var created = await world.Service.CaptureAsync(
            agent.Id,
            new CapturePinnedInstructionRequest(Guid.NewGuid(), 0, "original"),
            world.Operator,
            CancellationToken.None);
        var pinId = created.Set.Pins[0].Id;

        await using var db1 = world.FreshDb();
        await using var db2 = world.FreshDb();
        var left = new AgentPinnedInstructionService(db1, new MockEventBus(), TimeProvider.System, new NoOpAgentPinnedInstructionReconciler());
        var right = new AgentPinnedInstructionService(db2, new MockEventBus(), TimeProvider.System, new NoOpAgentPinnedInstructionReconciler());
        var replace = left.CaptureAsync(
            agent.Id,
            new CapturePinnedInstructionRequest(Guid.NewGuid(), 1, "replaced", ReplacesPinId: pinId),
            world.Operator,
            CancellationToken.None);
        var revoke = right.RevokeAsync(
            agent.Id,
            pinId,
            new RevokePinnedInstructionRequest(Guid.NewGuid(), 1),
            world.Operator,
            CancellationToken.None);
        var results = await Task.WhenAll(Wrap(replace), WrapSet(revoke));
        results.Count(r => r.Ok).ShouldBe(1);
        results.Count(r => r.Error is ConflictException { Code: AgentPinnedInstructionService.RevisionConflict }).ShouldBe(1);

        await using var verify = world.FreshDb();
        var pins = await verify.AgentPinnedInstructions.Where(p => p.AgentId == agent.Id).ToListAsync();
        var active = pins.Where(p => p.RevokedAt == null).ToList();
        active.Count.ShouldBeLessThanOrEqualTo(1);
        if (active.Count == 1)
            active[0].Text.ShouldBe("replaced");
        else
            pins.ShouldAllBe(p => p.RevokedAt != null);
    }

    [Test]
    public async Task V02_changed_text_requires_replace_and_delayed_replay_does_not_resurrect()
    {
        await using var world = await World.CreateAsync();
        var agent = await world.SeedAgentAsync();
        var requestId = Guid.NewGuid();
        var first = await world.Service.CaptureAsync(
            agent.Id,
            new CapturePinnedInstructionRequest(requestId, 0, "v1", "kb", "same"),
            world.Operator,
            CancellationToken.None);
        var conflict = await Should.ThrowAsync<ConflictException>(() =>
            world.Service.CaptureAsync(
                agent.Id,
                new CapturePinnedInstructionRequest(Guid.NewGuid(), 1, "v2", "kb", "same"),
                world.Operator,
                CancellationToken.None));
        conflict.Code.ShouldBe(AgentPinnedInstructionService.SourceConflict);

        var replaced = await world.Service.CaptureAsync(
            agent.Id,
            new CapturePinnedInstructionRequest(Guid.NewGuid(), 1, "v2", "kb", "same", ReplacesPinId: first.Set.Pins[0].Id),
            world.Operator,
            CancellationToken.None);
        replaced.Set.Pins.ShouldHaveSingleItem();
        replaced.Set.Pins[0].Text.ShouldBe("v2");
        replaced.Set.Pins[0].SupersedesPinId.ShouldBe(first.Set.Pins[0].Id);

        await world.Service.RevokeAsync(
            agent.Id,
            replaced.Set.Pins[0].Id,
            new RevokePinnedInstructionRequest(Guid.NewGuid(), 2),
            world.Operator,
            CancellationToken.None);

        var replay = await world.Service.CaptureAsync(
            agent.Id,
            new CapturePinnedInstructionRequest(requestId, 0, "v1", "kb", "same"),
            world.Operator,
            CancellationToken.None);
        replay.Set.Revision.ShouldBe(3);
        (await world.Db.AgentPinnedInstructions.CountAsync(p => p.AgentId == agent.Id && p.RevokedAt == null)).ShouldBe(0);

        var resurrect = await Should.ThrowAsync<ConflictException>(() =>
            world.Service.CaptureAsync(
                agent.Id,
                new CapturePinnedInstructionRequest(Guid.NewGuid(), 3, "v3", "kb", "same"),
                world.Operator,
                CancellationToken.None));
        resurrect.Code.ShouldBe(AgentPinnedInstructionService.SourceConflict);

        var repin = await world.Service.CaptureAsync(
            agent.Id,
            new CapturePinnedInstructionRequest(
                Guid.NewGuid(),
                3,
                "v3",
                "kb",
                "same",
                RepinsPinId: replaced.Set.Pins[0].Id),
            world.Operator,
            CancellationToken.None);
        repin.Set.Pins.ShouldHaveSingleItem();
        repin.Set.Pins[0].SupersedesPinId.ShouldBe(replaced.Set.Pins[0].Id);
        repin.Set.Revision.ShouldBe(4);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("x")]
    public async Task V02_empty_and_short_text(string? text)
    {
        await using var world = await World.CreateAsync();
        var agent = await world.SeedAgentAsync();
        if (string.IsNullOrEmpty(text))
        {
            await Should.ThrowAsync<ValidationException>(() =>
                world.Service.CaptureAsync(
                    agent.Id,
                    new CapturePinnedInstructionRequest(Guid.NewGuid(), 0, text!),
                    world.Operator,
                    CancellationToken.None));
            (await world.Db.AgentPinnedInstructions.CountAsync(p => p.AgentId == agent.Id)).ShouldBe(0);
            return;
        }

        var captured = await world.Service.CaptureAsync(
            agent.Id,
            new CapturePinnedInstructionRequest(Guid.NewGuid(), 0, text),
            world.Operator,
            CancellationToken.None);
        captured.Set.Pins[0].Text.ShouldBe("x");
    }

    [Test]
    public async Task V02_length_and_control_and_provenance_boundaries()
    {
        await using var world = await World.CreateAsync();
        var agent = await world.SeedAgentAsync();
        var ok = new string('a', 500);
        (await world.Service.CaptureAsync(
            agent.Id,
            new CapturePinnedInstructionRequest(Guid.NewGuid(), 0, ok),
            world.Operator,
            CancellationToken.None)).Set.Pins[0].Text.Length.ShouldBe(500);

        await Should.ThrowAsync<ValidationException>(() =>
            world.Service.CaptureAsync(
                agent.Id,
                new CapturePinnedInstructionRequest(Guid.NewGuid(), 1, new string('a', 501)),
                world.Operator,
                CancellationToken.None));
        await Should.ThrowAsync<ValidationException>(() =>
            world.Service.CaptureAsync(
                agent.Id,
                new CapturePinnedInstructionRequest(Guid.NewGuid(), 1, "ok\0"),
                world.Operator,
                CancellationToken.None));
        await Should.ThrowAsync<ValidationException>(() =>
            world.Service.CaptureAsync(
                agent.Id,
                new CapturePinnedInstructionRequest(Guid.NewGuid(), 1, "ok\x1b"),
                world.Operator,
                CancellationToken.None));
        await Should.ThrowAsync<ValidationException>(() =>
            world.Service.CaptureAsync(
                agent.Id,
                new CapturePinnedInstructionRequest(Guid.NewGuid(), 1, "ok", new string('n', 65), "k"),
                world.Operator,
                CancellationToken.None));
        await Should.ThrowAsync<ValidationException>(() =>
            world.Service.CaptureAsync(
                agent.Id,
                new CapturePinnedInstructionRequest(Guid.NewGuid(), 1, "ok", "n", new string('k', 201)),
                world.Operator,
                CancellationToken.None));
        await Should.ThrowAsync<ValidationException>(() =>
            world.Service.CaptureAsync(
                agent.Id,
                new CapturePinnedInstructionRequest(Guid.NewGuid(), 1, "ok", SourceRef: new string('r', 201)),
                world.Operator,
                CancellationToken.None));

        (await world.Db.AgentPinnedInstructions.CountAsync(p => p.AgentId == agent.Id)).ShouldBe(1);
        (await world.Db.AgentPinnedInstructionStates.SingleAsync(s => s.AgentId == agent.Id)).Revision.ShouldBe(1);
    }

    [Test]
    public async Task V02_two_agents_may_share_a_source_key()
    {
        await using var world = await World.CreateAsync();
        var a = await world.SeedAgentAsync();
        var b = await world.SeedAgentAsync();
        await world.Service.CaptureAsync(
            a.Id,
            new CapturePinnedInstructionRequest(Guid.NewGuid(), 0, "A", "kb", "shared"),
            world.Operator,
            CancellationToken.None);
        var other = await world.Service.CaptureAsync(
            b.Id,
            new CapturePinnedInstructionRequest(Guid.NewGuid(), 0, "B", "kb", "shared"),
            world.Operator,
            CancellationToken.None);
        other.Set.Pins[0].Text.ShouldBe("B");
        (await world.Db.AgentPinnedInstructions.CountAsync(p => p.SourceKey == "shared")).ShouldBe(2);
    }

    private static async Task<(bool Ok, Exception? Error)> Wrap(Task<PinnedInstructionMutationResult> task)
    {
        try
        {
            await task;
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
    }

    private static async Task<(bool Ok, Exception? Error)> WrapSet(Task<PinnedInstructionSetDto> task)
    {
        try
        {
            await task;
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
    }

    private sealed class ThrowingReconciler : IAgentPinnedInstructionReconciler
    {
        public int Calls;

        public Task ReconcileAfterCommitAsync(Guid agentId, int revision, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("reconcile-after-commit");
        }
    }

    private sealed class World : IAsyncDisposable
    {
        private World(
            IsolatedTestSchema schema,
            AppDbContext db,
            MockEventBus events,
            NoOpAgentPinnedInstructionReconciler reconciler,
            AgentPinnedInstructionService service)
        {
            Schema = schema;
            Db = db;
            Events = events;
            Reconciler = reconciler;
            Service = service;
            Operator = PinPrincipal.Operator(Guid.Parse("a0000000-0000-0000-0000-000000000001"));
        }

        public IsolatedTestSchema Schema { get; }
        public AppDbContext Db { get; }
        public MockEventBus Events { get; }
        public NoOpAgentPinnedInstructionReconciler Reconciler { get; }
        public AgentPinnedInstructionService Service { get; }
        public PinPrincipal Operator { get; }

        public static async Task<World> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var events = new MockEventBus();
            var reconciler = new NoOpAgentPinnedInstructionReconciler();
            var service = new AgentPinnedInstructionService(db, events, TimeProvider.System, reconciler);
            return new World(schema, db, events, reconciler, service);
        }

        public AppDbContext FreshDb(params IInterceptor[] interceptors)
        {
            if (interceptors.Length == 0)
                return new AppDbContext(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

            var builder = new DbContextOptionsBuilder<AppDbContext>();
            builder.UseNpgsql(Schema.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            });
            builder.AddInterceptors(interceptors);
            return new AppDbContext(builder.Options);
        }

        public async Task<Agent> SeedAgentAsync(
            Guid? id = null,
            string? cwd = null,
            string? name = null,
            bool pool = false)
        {
            var now = DateTime.UtcNow;
            var agent = new Agent
            {
                Id = id ?? Guid.NewGuid(),
                Name = name ?? $"pin-{Guid.NewGuid():N}"[..18],
                Slug = $"pin-{Guid.NewGuid():N}"[..18],
                WorkingDirectory = cwd ?? $@"D:\src\pins\{Guid.NewGuid():N}",
                Details = "pin tests",
                Kind = AgentKind.ClaudeCode,
                IsPoolDelegate = pool,
                CreatedAt = now,
                UpdatedAt = now
            };
            Db.Agents.Add(agent);
            await Db.SaveChangesAsync();
            return agent;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Schema.DisposeAsync();
        }
    }
}
