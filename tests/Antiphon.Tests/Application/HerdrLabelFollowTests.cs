using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
public class HerdrLabelFollowTests
{
    [Test]
    public async Task Current_pointer_must_still_name_the_session()
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync();
        await f.MutateAsync((a, _) => a.PersistentSessionId = Guid.NewGuid().ToString());
        (await f.ApplyAsync()).ShouldBeFalse(); (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Old");
    }

    [Test][Arguments("launch")][Arguments("session")]
    public async Task Physical_owner_must_match_both_launch_and_session(string arm)
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync();
        if (arm == "launch") f.Dto = f.Dto with { LabelObservation = f.Dto.LabelObservation! with { Intent = f.Dto.LabelObservation!.Intent with { StandingAgentId = Guid.NewGuid() } } };
        else await f.MutateAsync((_, s) => s.StandingAgentId = Guid.NewGuid());
        (await f.ApplyAsync()).ShouldBeFalse(); (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Old");
    }

    [Test]
    [Arguments("attached")][Arguments("pool")][Arguments("agent-backend")][Arguments("session-backend")]
    [Arguments("stopped")][Arguments("failed")][Arguments("ended")][Arguments("deleted")][Arguments("card")]
    public async Task Only_live_standing_herdr_agents_are_eligible(string arm)
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync();
        if (arm == "attached") f.Dto = f.Dto with { HerdrOrigin = HerdrPaneOrigins.Attached, LabelObservation = f.Dto.LabelObservation! with { Origin = HerdrPaneOrigins.Attached } };
        else if (arm == "deleted") { await using var db = f.Open(); await db.Agents.Where(a => a.Id == f.AgentId).ExecuteDeleteAsync(); }
        else if (arm == "card")
        {
            await using var db = f.Open(); var now = f.Clock.GetUtcNow().UtcDateTime;
            var project = new Project { Id = Guid.NewGuid(), Name = "card", CreatedAt = now, UpdatedAt = now };
            var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "board", CreatedAt = now, UpdatedAt = now };
            var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "todo" };
            var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id, Identifier = "CARD-0001", Title = "card", CreatedAt = now, UpdatedAt = now };
            db.AddRange(project, board, column, card); await db.SaveChangesAsync();
            await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.CardId, card.Id));
        }
        else await f.MutateAsync((a, s) => { switch (arm) {
            case "pool": a.IsPoolDelegate = true; break; case "agent-backend": a.SessionBackend = SessionBackend.PtyHost; break;
            case "session-backend": s.SessionBackend = SessionBackend.PtyHost; break; case "stopped": s.Status = SessionStatus.Stopped; break;
            case "failed": s.Status = SessionStatus.Failed; break; case "ended": s.EndedAt = f.Clock.GetUtcNow().UtcDateTime; break; } });
        var service = f.Service(); (await service.ApplyAsync(f.AgentId, f.Dto, CancellationToken.None)).ShouldBeFalse(); service.ConditionalWrites.ShouldBe(0);
        if (arm != "deleted") (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Old");
    }

    [Test]
    public async Task Unrelated_edit_preserves_follow_authority()
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync();
        await f.ManualAsync(); (await f.ReadAsync()).HerdrPlacementEditToken.ShouldBe(f.EditToken);
        (await f.ApplyAsync()).ShouldBeTrue(); (await f.ReadAsync()).HerdrTabLabel.ShouldBe("New");
    }

    [Test][Arguments(false)][Arguments(true)]
    public async Task Same_id_new_generation_rejects_old_observation(bool missing)
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync();
        if (missing) f.Dto = f.Dto with { AcceptedStartedAt = null };
        else await f.MutateAsync((_, s) => s.StartedAt = s.StartedAt.AddTicks(10));
        (await f.ApplyAsync()).ShouldBeFalse(); (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Old");
    }

    [Test]
    public async Task Older_or_duplicate_sequence_cannot_roll_back_labels()
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync(); var old = f.Dto;
        f.Dto = old with { LabelObservation = old.LabelObservation! with { Sequence = 2, TabLabel = "Latest" } };
        (await f.ApplyAsync()).ShouldBeTrue(); (await f.ApplyAsync()).ShouldBeFalse(); f.Dto = old; (await f.ApplyAsync()).ShouldBeFalse();
        (await f.ReadAsync()).HerdrTabLabel.ShouldBe("Latest"); f.Bus.Count.ShouldBe(1);
    }

    [Test]
    [Arguments("expiry")][Arguments("past")][Arguments("future")][Arguments("unverified")][Arguments("refusal")][Arguments("in-progress")]
    [Arguments("expiry-before")][Arguments("future-boundary")]
    public async Task Expired_future_or_unverified_observation_is_ignored(string arm)
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync(); var o = f.Dto.LabelObservation!; var now = f.Clock.GetUtcNow().UtcDateTime;
        f.Dto = f.Dto with { LabelObservation = arm switch {
            "expiry" => o with { ExpiresAtUtc = now }, "past" => o with { ExpiresAtUtc = now.AddTicks(-1) },
            "expiry-before" => o with { ExpiresAtUtc = now.AddTicks(1) }, "future" => o with { CompletedAtUtc = now.AddSeconds(30).AddTicks(1) },
            "future-boundary" => o with { CompletedAtUtc = now.AddSeconds(30) }, "unverified" => o with { PositivelyVerified = false },
            "refusal" => o with { ResultCode = "read_failed" }, _ => o with { ResultCode = "in-progress" } } };
        var valid = arm is "expiry-before" or "future-boundary";
        (await f.ApplyAsync()).ShouldBe(valid); (await f.ReadAsync()).HerdrTabLabel.ShouldBe(valid ? "New" : "Old");
    }

    [Test][Arguments("tab")][Arguments("workspace")]
    public async Task Null_pins_are_never_created_even_with_claimed_intent(string field)
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync();
        await f.MutateAsync((a, _) => { if (field == "tab") a.HerdrTabLabel = null; else a.HerdrWorkspaceLabel = null; });
        await f.ApplyAsync(); var a = await f.ReadAsync(); (field == "tab" ? a.HerdrTabLabel : a.HerdrWorkspaceLabel).ShouldBeNull();
    }

    [Test][Arguments("tab")][Arguments("workspace")]
    public async Task Null_launch_intent_cannot_authorize_follow(string field)
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync(); var o = f.Dto.LabelObservation!;
        f.Dto = f.Dto with { LabelObservation = o with { Intent = field == "tab" ? o.Intent with { TabLabel = null } : o.Intent with { WorkspaceLabel = null } } };
        await f.ApplyAsync(); var a = await f.ReadAsync(); (field == "tab" ? a.HerdrTabLabel : a.HerdrWorkspaceLabel).ShouldBe(field == "tab" ? "Old" : "Old workspace");
    }

    [Test]
    public async Task Labels_and_watermark_commit_together_and_only_changes_notify()
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync(); var original = await f.ReadAsync();
        var failing = f.Service(); failing.Boundary = (name, _) => name == "before-commit" ? Task.FromException(new IOException("rollback")) : Task.CompletedTask;
        await Should.ThrowAsync<IOException>(() => failing.ApplyAsync(f.AgentId, f.Dto, CancellationToken.None));
        var rolled = await f.ReadAsync(); rolled.HerdrTabLabel.ShouldBe("Old"); rolled.HerdrLabelFollowSequence.ShouldBe(0); f.Bus.Count.ShouldBe(0);
        f.Bus.BeforeRecord = async () => { var a = await f.ReadAsync(); a.HerdrTabLabel.ShouldBe("New"); a.HerdrLabelFollowSequence.ShouldBe(1); };
        (await f.ApplyAsync()).ShouldBeTrue(); (await f.ReadAsync()).UpdatedAt.ShouldBeGreaterThan(original.UpdatedAt);
        var timestamp = (await f.ReadAsync()).UpdatedAt;
        (await f.ApplyAsync()).ShouldBeFalse();
        f.Dto = f.Dto with { LabelObservation = f.Dto.LabelObservation! with { Sequence = 2 } }; (await f.ApplyAsync()).ShouldBeFalse();
        f.Dto = f.Dto with { LabelObservation = f.Dto.LabelObservation! with { Sequence = 3, ResultCode = "read_failed", TabLabel = null, WorkspaceLabel = null } }; (await f.ApplyAsync()).ShouldBeFalse();
        var final = await f.ReadAsync(); final.HerdrLabelFollowSequence.ShouldBe(3); final.UpdatedAt.ShouldBe(timestamp); f.Bus.Count.ShouldBe(1);
    }

    [Test][Arguments(0)][Arguments(-1)]
    public void Invalid_sweep_settings_refuse_startup(int value)
    { var s = new HerdrLabelFollowSettings(); s.SweepPeriodSeconds.ShouldBe(60); s.SweepPeriodSeconds = value; Should.Throw<OptionsValidationException>(s.Validate); }

    [Test]
    public async Task Disabled_sweep_polls_nothing()
    { await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync(); f.Settings.Enabled = false;
        await f.Service().SweepAsync(CancellationToken.None); f.Clock.Advance(TimeSpan.FromMinutes(1)); await f.Service().SweepAsync(CancellationToken.None); f.Polls.ShouldBe(0); }

    [Test]
    public async Task Sweep_is_independent_of_corroboration_and_turn_state()
    {
        await using var f = new HerdrLabelFollowDbFixture(); await f.StartAsync();
        await using (var db = f.Open())
        {
            var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001"); var firstSession = Guid.NewGuid(); var now = f.Clock.GetUtcNow().UtcDateTime;
            db.Agents.Add(new Agent { Id = firstId, Name = "Unreachable", Slug = "unreachable", SessionBackend = SessionBackend.Herdr,
                HerdrTabLabel = "Old", PersistentSessionId = firstSession.ToString(), CreatedAt = now, UpdatedAt = now });
            db.AgentSessions.Add(new AgentSession { Id = firstSession, StandingAgentId = firstId, SessionBackend = SessionBackend.Herdr,
                Status = SessionStatus.Running, StartedAt = now, CreatedAt = now, LastSeenAt = now }); await db.SaveChangesAsync();
        }
        var calls = 0; f.Runner.GetOverride = (id, _) => { calls++; return id == f.SessionId ? Task.FromResult(f.Dto) : Task.FromException<Antiphon.Server.Application.Dtos.SessionRunnerSessionDto>(new IOException("offline")); };
        await f.Service().SweepAsync(CancellationToken.None); (await f.ReadAsync()).HerdrTabLabel.ShouldBe("New"); calls.ShouldBe(2);
    }
}
