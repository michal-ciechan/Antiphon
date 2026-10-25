using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0157 S4: PersistTranscriptAsync over the 98c61e03-shaped sequence (multi-call,
/// single-call, boundary) then LoadFullnessAsync returns the anchor-based number.
/// Uses the shared database template through an isolated session-state fixture.
/// </summary>
[Category("Integration")]
public class SessionContextUsagePersistenceTests
{
    [Test]
    public async Task Persisted_98c61e03_shape_computes_anchor_based_fullness()
    {
        await using var fixture = await SessionStateTestFixture.CreateAsync();
        var sessionId = fixture.SessionId;
        var runtime = fixture.Runtime;
        await using (var seed = fixture.Db())
        {
            var session = await seed.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.DefinitionName = "grok";
            session.AgentKind = AgentKind.Grok;
            await seed.SaveChangesAsync();
        }

        var t0 = DateTimeOffset.UtcNow.AddHours(-2);
        // Session 98c61e03 shape: loop-sum turns go up and down, a single-call turn
        // is occupancy, then a compaction anchor resets it.
        await runtime.ObserveTranscriptAsync(
            Turn(sessionId, 1, t0, input: 2_100_000, modelCalls: 12), CancellationToken.None);
        await runtime.ObserveTranscriptAsync(
            Turn(sessionId, 2, t0.AddMinutes(10), input: 18_747_424, modelCalls: 103), CancellationToken.None);
        await runtime.ObserveTranscriptAsync(
            Turn(sessionId, 3, t0.AddMinutes(20), input: 137_657, modelCalls: 1), CancellationToken.None);
        await runtime.ObserveTranscriptAsync(
            Turn(sessionId, 4, t0.AddMinutes(30), input: 3_200_000, modelCalls: 20), CancellationToken.None);
        await runtime.ObserveTranscriptAsync(
            Boundary(sessionId, 5, t0.AddMinutes(40), tokensAfter: 34_833), CancellationToken.None);

        await using var verify = fixture.Db();
        var stored = await verify.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .OrderBy(t => t.Sequence)
            .ToListAsync();
        stored.Count.ShouldBe(5);
        stored[1].ModelCalls.ShouldBe(103);
        stored[2].ModelCalls.ShouldBe(1);
        stored[4].Kind.ShouldBe(TranscriptKinds.CompactBoundary);
        stored[4].InputTokens.ShouldBe(34_833);

        var fullness = await SessionContextUsage.LoadFullnessAsync(
            verify,
            [(sessionId, "grok-4.6-build", AgentKind.Grok)],
            new ContextWindowSettings(),
            logger: null,
            CancellationToken.None);

        fullness[sessionId].Fullness.ShouldNotBeNull();
        fullness[sessionId].Fullness!.Value.ShouldBe(34_833 / 500_000.0, 1e-12);
    }

    private static SessionRunnerTranscriptEvent Turn(
        Guid sessionId, long seq, DateTimeOffset timestamp, int input, int modelCalls) =>
        new(
            sessionId, seq, TranscriptKinds.TurnEnd, $"uuid-{seq}", null, timestamp,
            "assistant", null, null, null, null, null, "end_turn",
            InputTokens: input, OutputTokens: 0, CacheReadTokens: 0, CacheCreationTokens: 0,
            Model: "grok-4.6-build", ModelCalls: modelCalls);

    private static SessionRunnerTranscriptEvent Boundary(
        Guid sessionId, long seq, DateTimeOffset timestamp, int tokensAfter) =>
        new(
            sessionId, seq, TranscriptKinds.CompactBoundary, $"uuid-{seq}", null, timestamp,
            "assistant", $"Context compacted (auto): tokens x -> {tokensAfter}",
            null, null, null, null, null,
            InputTokens: tokensAfter);
}
