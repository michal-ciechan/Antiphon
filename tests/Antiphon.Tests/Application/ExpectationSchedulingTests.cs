using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class ExpectationSchedulingTests
{
    [Test]
    public async Task C650_Unanswered_episode_pages_without_repeat_typing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        await SeedSilentAsync(world);
        var clock = Clock(world.Now);
        (await ScanAsync(world, clock)).NudgesCommitted.ShouldBe(1);
        clock.Advance(TimeSpan.FromMinutes(31));
        (await ScanAsync(world, clock)).NudgesCommitted.ShouldBe(0);
        await using (var db = world.Db())
        {
            (await db.ExpectationNudges.CountAsync()).ShouldBe(1);
            var first = await db.ExpectationNudges.SingleAsync();
            first.AnsweredAt.ShouldBeNull();
        }
    }

    [Test]
    public async Task C650_Acknowledged_unresolved_episode_repeats_after_answer_window()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        await SeedSilentAsync(world);
        var clock = Clock(world.Now);
        (await ScanAsync(world, clock)).NudgesCommitted.ShouldBe(1);
        await using (var db = world.Db())
            await db.ExpectationNudges.ExecuteUpdateAsync(u => u.SetProperty(n => n.AnsweredAt, world.Now.AddMinutes(1)));
        clock.SetUtcNow(new DateTimeOffset(world.Now.AddMinutes(30).AddSeconds(59), TimeSpan.Zero));
        (await ScanAsync(world, clock)).NudgesCommitted.ShouldBe(0);
        clock.Advance(TimeSpan.FromSeconds(1));
        (await ScanAsync(world, clock)).NudgesCommitted.ShouldBe(1);
        await using var read = world.Db();
        (await read.ExpectationNudges.CountAsync()).ShouldBe(2);
    }

    [Test]
    public async Task C650_Config_disable_change_and_expiry_cancel_stale_unsent_work()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        await SeedSilentAsync(world);
        var clock = Clock(world.Now);
        (await ScanAsync(world, clock)).NudgesCommitted.ShouldBe(1);
        var original = world.Directive.OperatorChannelId;
        world.Directive.Enabled = false;
        await using (var db = world.Db())
        {
            var service = new ExpectationNudgeDeliveryService(db, new RefusingSender(), clock);
            var id = await db.ExpectationNudges.Select(n => n.Id).SingleAsync();
            (await service.DeliverAsync(world.Directive, id, CancellationToken.None)).Reason
                .ShouldBe("directive_inactive_or_changed");
        }
        world.Directive.Enabled = true;
        world.Directive.OperatorChannelId = Guid.NewGuid();
        await using (var db = world.Db())
        {
            var service = new ExpectationNudgeDeliveryService(db, new RefusingSender(), clock);
            var id = await db.ExpectationNudges.Select(n => n.Id).SingleAsync();
            (await service.DeliverAsync(world.Directive, id, CancellationToken.None)).Reason
                .ShouldBe("directive_inactive_or_changed");
        }
        world.Directive.OperatorChannelId = original;
        await using (var db = world.Db())
            await db.ChatChannels.Where(c => c.Id == original).ExecuteUpdateAsync(u =>
                u.SetProperty(c => c.ExternalId, "c650-operator-moved"));
        await using (var db = world.Db())
        {
            var service = new ExpectationNudgeDeliveryService(db, new RefusingSender(), clock);
            var id = await db.ExpectationNudges.Select(n => n.Id).SingleAsync();
            (await service.DeliverAsync(world.Directive, id, CancellationToken.None)).Reason
                .ShouldBe("directive_inactive_or_changed");
        }
        await using (var db = world.Db())
            await db.ChatChannels.Where(c => c.Id == original).ExecuteUpdateAsync(u =>
                u.SetProperty(c => c.ExternalId, "c650-operator-" + original.ToString("N")));
        world.Directive.ActiveUntilUtc = new DateTimeOffset(world.Now.AddSeconds(-1), TimeSpan.Zero);
        await using (var db = world.Db())
        {
            var service = new ExpectationNudgeDeliveryService(db, new RefusingSender(), clock);
            var id = await db.ExpectationNudges.Select(n => n.Id).SingleAsync();
            (await service.DeliverAsync(world.Directive, id, CancellationToken.None)).Reason
                .ShouldBe("directive_inactive_or_changed");
        }
    }

    [Test]
    public async Task C650_Prompt_byte_ceiling_keeps_identity_and_audit()
    {
        var id = Guid.NewGuid();
        var board = Guid.NewGuid();
        var condition = new ExpectationCondition
        {
            Kind = ExpectationEpisodeKind.DispatchFence, SubjectKey = "fence:emoji",
            Scope = "repo", ReasonCode = "repository-fenced", Evidence = string.Concat(Enumerable.Repeat("😀", 250)),
            IsDue = true, Immediate = true,
        };
        var minimum = ExpectationPromptFormatter.FormatForWrite(id, "tonight", board, [],
            new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc), 4096);
        minimum.ShouldNotBeNull();
        var minBytes = System.Text.Encoding.UTF8.GetByteCount(
            Antiphon.Agents.Pty.PtyInputEncoding.EncodeBody(minimum));
        ExpectationPromptFormatter.FormatForWrite(id, "tonight", board, [condition],
            new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc), minBytes - 1).ShouldBeNull();
        var body = ExpectationPromptFormatter.FormatForWrite(id, "tonight", board, [condition],
            new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc), minBytes + 100);
        body.ShouldNotBeNull();
        body.ShouldContain(id.ToString("D"));
        body.ShouldContain(ExpectationPromptFormatter.AckMarker(id));
        body.ShouldContain(board.ToString("D"));
        System.Text.Encoding.UTF8.GetByteCount(Antiphon.Agents.Pty.PtyInputEncoding.EncodeBody(body))
            .ShouldBeLessThanOrEqualTo(minBytes + 100);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_Too_small_runner_write_ceiling_refuses_and_audits_operator_page()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await ExpectationTestWorld.CreateAsync(schema.ConnectionString);
        await SeedSilentAsync(world);
        var clock = Clock(world.Now);
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var settings = new DelegationSettings { PtySingleChunkBytes = 80 };
        var pty = new PtyDeliveryProfile(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PtyDeliveryProfile>.Instance, Options.Create(settings), clock, "inbox");
        var profile = new SessionDeliveryProfile(pty, Options.Create(settings),
            new RefusingSessionRunnerClient(), clock, NullLogger<SessionDeliveryProfile>.Instance);
        await using (var db = world.Db())
        {
            var ledger = new ExpectationLedger(db, clock, new ExpectationTestWorld.QuietBus());
            var scan = await new ExpectationWatchdogService(db, ledger, clock,
                deliveryProfile: profile).ScanAsync(world.Directive, ExpectationProbeInput.None,
                CancellationToken.None);
            scan.NudgesCommitted.ShouldBe(1);
        }
        await using var read = world.Db();
        var nudge = await read.ExpectationNudges.SingleAsync();
        nudge.AttemptState.ShouldBe(ExpectationAttemptState.Refused);
        nudge.OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Due);
        (await read.CardComments.CountAsync(comment => comment.Body.Contains("byte write ceiling"))).ShouldBe(1);
        (await read.AgentTaskEvents.CountAsync(e => e.Detail != null
            && e.Detail.Contains("expectation-nudge:"))).ShouldBeGreaterThan(0);
    }

    private static FakeTimeProvider Clock(DateTime at) => new(new DateTimeOffset(at, TimeSpan.Zero));
    private static async Task SeedSilentAsync(ExpectationTestWorld world)
    {
        await using var db = world.Db();
        db.AgentTasks.Add(world.Task(Guid.NewGuid(), AgentTaskStatus.Dispatched, world.Now.AddMinutes(-20)));
        await db.SaveChangesAsync();
    }
    private static async Task<ExpectationScanResult> ScanAsync(ExpectationTestWorld world, FakeTimeProvider clock)
    {
        await using var db = world.Db();
        return await world.Service(db, clock).ScanAsync(world.Directive, ExpectationProbeInput.None, CancellationToken.None);
    }
    private sealed class RefusingSender : IExpectationPromptSender
    {
        public Task<ExpectationSendResult> SendAsync(Guid sessionId, DateTime generation, Guid agentId,
            string body, Func<ExpectationSendAttempt, CancellationToken, Task<bool>> commitAttempt,
            CancellationToken ct, Func<ExpectationSendResult, CancellationToken, Task>? recordOutcome = null) =>
            throw new InvalidOperationException("inactive directive sent a prompt");
    }
}
