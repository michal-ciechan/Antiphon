using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public sealed class SessionMessageQueueCodexPointerTests
{
    [Test]
    [Arguments("capture-first")]
    [Arguments("capture-second")]
    [Arguments("prefix-10")]
    [Arguments("prefix-14")]
    [Arguments("missing-last-byte")]
    [Arguments("complete-120")]
    [Arguments("complete-80")]
    [Arguments("partial-then-complete")]
    [Arguments("history-only")]
    [Arguments("complete-swallowed-enter")]
    public async Task C1521_Initial_consumption_is_complete(string variant)
    {
        await using var h = await CreateAsync();
        using var fixture = Fixture();
        var body = fixture.RootElement.GetProperty("pointer").GetString()!;
        body.Length.ShouldBe(676);
        var complete = variant.StartsWith("complete-", StringComparison.Ordinal)
            || variant == "partial-then-complete";
        var screen = Frame(body[..10]);
        if (variant.StartsWith("capture-", StringComparison.Ordinal))
        {
            var capture = fixture.RootElement.GetProperty("captures").EnumerateArray()
                .Single(c => "capture-" + c.GetProperty("name").GetString() == variant);
            var terminal = new TerminalScreen(120, 30);
            terminal.Feed(capture.GetProperty("buffer").GetString()!);
            screen = terminal.GetScreenText();
            var consumed = capture.GetProperty("consumedPrefixLength").GetInt32();
            var normalized = Compact(screen);
            normalized.ShouldContain(Compact(body[..consumed]));
            var nextSubstantive = consumed;
            while (char.IsWhiteSpace(body[nextSubstantive])) nextSubstantive++;
            normalized.ShouldNotContain(Compact(body[..(nextSubstantive + 1)]));
            ComposerDeliveryEvidence.IsVisible("", screen, body).ShouldBeTrue();
            ComposerDeliveryEvidence.BodyConsumed("", screen, body).ShouldBeTrue();
        }
        else if (variant == "prefix-14") screen = Frame(body[..14]);
        else if (variant == "missing-last-byte") screen = Frame(body[..^1]);
        else if (variant == "history-only") screen = body + "\n\n" + Frame("");
        else if (complete && variant != "partial-then-complete")
            screen = Frame(body, variant == "complete-80" ? 80 : 120);

        h.Adapter.RenderedScreenOverride = screen;
        if (variant == "complete-swallowed-enter") h.Adapter.SwallowSubmits = 1;
        var partialReads = 0;
        if (variant == "partial-then-complete")
            h.Adapter.BeforeRenderedSnapshot = () =>
            {
                if (h.Adapter.Inputs.Count == 0) return;
                if (++partialReads == 3)
                {
                    h.Adapter.Inputs.Count(i => i == "\r").ShouldBe(0, "partial input cannot authorize CR");
                    h.Adapter.RenderedScreenOverride = Frame(body);
                }
            };

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle,
            CancellationToken.None, origin: QueuedMessageOrigin.Delegation);

        h.Adapter.Inputs.Count(i => i != "\r").ShouldBe(1, "one body write");
        if (complete)
        {
            h.Adapter.SubmittedBodies.ShouldBe([body]);
            h.Adapter.Inputs.Count(i => i == "\r").ShouldBe(variant == "complete-swallowed-enter" ? 2 : 1);
            var row = await RowAsync(h);
            row.Status.ShouldBe(QueuedMessageStatus.Sent);
            PromptSubmissionMatch.IsCompleteIn(body, h.Adapter.SubmittedBodies.Single()).ShouldBeTrue();
        }
        else
        {
            h.Adapter.Inputs.Count(i => i == "\r").ShouldBe(0, "incomplete pointer must withhold submitting CR");
            h.Adapter.SubmittedBodies.ShouldBeEmpty();
            var row = await RowAsync(h);
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            row.SentAt.ShouldBeNull();
            row.DeliveryAttempts.ShouldBe(1, "the typed attempt must not be refunded");
            row.LastDeliveryStartedAt.ShouldNotBeNull();
            row.LastDeliveryGeneration.ShouldBe(await GenerationAsync(h));
            row.DeliveryVerdict.ShouldBe(DeliveryVerdict.NoComposerEvidence);
            await AssertNoRecoveryAsync(h);
        }
    }

    [Test]
    [Arguments("complete-current")]
    [Arguments("clipped-current")]
    [Arguments("complete-stale")]
    public async Task C1521_Initial_transcript_is_authority(string variant)
    {
        await using var h = await CreateAsync();
        var body = Pointer();
        h.Adapter.RenderedScreenOverride = Frame(body[..14]);
        h.Adapter.BeforeInput = async (input, _) =>
        {
            if (input == "\r") return;
            await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt,
                variant == "clipped-current" ? body[..300] : body,
                timestamp: variant == "complete-stale" ? h.Now.AddHours(-1) : h.Now);
            await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, timestamp: h.Now);
        };
        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle,
            CancellationToken.None, origin: QueuedMessageOrigin.Delegation);
        h.Adapter.Inputs.ShouldBe([body], "receipt reconciliation must not send another Enter or body");
        var row = await RowAsync(h);
        row.Status.ShouldBe(variant == "complete-current" ? QueuedMessageStatus.Sent : QueuedMessageStatus.Pending);
        if (variant == "clipped-current") row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Truncated);
        if (variant == "complete-stale") row.DeliveryVerdict.ShouldBe(DeliveryVerdict.NoComposerEvidence);
        await AssertNoRecoveryAsync(h);
    }

    [Test]
    [Arguments("pending")]
    [Arguments("interrupted-sent")]
    [Arguments("send-now")]
    [Arguments("canceled-before-next")]
    [Arguments("capped-before-next")]
    [Arguments("expectation-direct")]
    public async Task C1521_Recovery_never_stacks_a_pointer(string variant)
    {
        await using var h = await CreateAsync();
        var body = Pointer();
        var status = variant == "interrupted-sent" ? QueuedMessageStatus.Sent
            : variant is "canceled-before-next" or "expectation-direct" ? QueuedMessageStatus.Canceled
            : QueuedMessageStatus.Pending;
        var id = await h.SeedPendingMessageAsync(body, deliveryAttempts: variant == "capped-before-next" ? 3 : 1,
            origin: QueuedMessageOrigin.Delegation, status: status,
            lastDeliveryStartedAt: h.Now.AddMinutes(-2));
        h.Adapter.PrimeComposer(body[..10]);
        h.Adapter.RenderedScreenOverride = Frame(body[..10]);
        var original = await RowAsync(h, id);
        if (variant is "canceled-before-next" or "capped-before-next")
            await h.SeedPendingMessageAsync("next unrelated automatic note");
        if (variant == "send-now")
            await h.Queue.SendNowAsync(h.SessionId, id, CancellationToken.None);
        else if (variant == "expectation-direct")
        {
            await using var db = h.CreateObserverContext();
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.StandingAgentId, h.AgentId));
            var result = await h.Queue.SendExpectationNowAsync(h.SessionId, await GenerationAsync(h), h.AgentId,
                "next expectation automatic input", (_, _) => Task.FromResult(true), CancellationToken.None);
            result.Outcome.ShouldBe(ExpectationSendOutcome.Refused);
        }
        else
        {
            // Reconstruct the singleton service to make the interrupted-row proof persistence based.
            var queue = variant == "interrupted-sent"
                ? ActivatorUtilities.CreateInstance<SessionMessageQueueService>(h.Provider) : h.Queue;
            await queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
            await queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        }
        h.Adapter.Inputs.ShouldBeEmpty("an attempted partial pointer must not be retyped or submitted");
        await AssertStampAsync(h, id, original);
        await AssertNoRecoveryAsync(h);
    }

    [Test]
    [Arguments("complete-idle")]
    [Arguments("working")]
    [Arguments("unreadable")]
    [Arguments("two-empty-screens")]
    [Arguments("new-generation")]
    [Arguments("late-complete-receipt")]
    [Arguments("legacy-refunded-stamp")]
    [Arguments("changed-generation-stale-receipt")]
    public async Task C1521_Recovery_reconciles_before_input(string variant)
    {
        await using var h = await CreateAsync();
        var body = Pointer();
        var generation = await GenerationAsync(h);
        var id = await h.SeedPendingMessageAsync(body,
            deliveryAttempts: variant == "legacy-refunded-stamp" ? 0 : 1,
            origin: QueuedMessageOrigin.Delegation, lastDeliveryStartedAt: h.Now.AddMinutes(-2),
            lastDeliveryGeneration: generation);
        h.Adapter.PrimeComposer(body);
        h.Adapter.RenderedScreenOverride = Frame(variant == "complete-idle" ? body : body[..10]);
        var original = await RowAsync(h, id);
        if (variant == "working") await h.MarkWorkingAsync();
        if (variant == "unreadable") h.Adapter.ThrowOnRenderedSnapshot = true;
        if (variant == "two-empty-screens") h.Adapter.RenderedScreenOverride = Frame("");
        if (variant == "late-complete-receipt")
        {
            await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body, timestamp: h.Now);
            await h.MarkIdleAsync();
        }
        if (variant is "new-generation" or "changed-generation-stale-receipt")
        {
            if (variant == "changed-generation-stale-receipt")
            {
                await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body, timestamp: h.Now.AddMinutes(-1));
                await h.MarkIdleAsync();
            }
            await using var db = h.CreateObserverContext();
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, h.Now.AddSeconds(-1)));
            h.Adapter.PrimeComposer("");
            h.Adapter.RenderedScreenOverride = Frame(body);
        }
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        if (variant == "complete-idle") h.Adapter.Inputs.ShouldBe(["\r"]);
        else if (variant is "new-generation" or "changed-generation-stale-receipt")
            h.Adapter.Inputs.ShouldBe([body, "\r"], "an old generation receipt cannot confirm the fresh attempt");
        else h.Adapter.Inputs.ShouldBeEmpty();
        if (variant is "complete-idle" or "new-generation" or "changed-generation-stale-receipt")
            h.Adapter.SubmittedBodies.ShouldBe([body]);
        if (variant is "working" or "unreadable" or "two-empty-screens" or "legacy-refunded-stamp")
            await AssertStampAsync(h, id, original);
        if (variant == "late-complete-receipt") (await RowAsync(h, id)).Status.ShouldBe(QueuedMessageStatus.Sent);
        await AssertNoRecoveryAsync(h);
    }

    private static async Task<BridgeQueueHarness> CreateAsync()
    {
        var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false });
        await using var db = h.CreateObserverContext();
        await db.AgentSessions.Where(s => s.Id == h.SessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.AgentKind, AgentKind.Codex));
        h.Adapter.RenderedScreenIncludesComposer = true;
        return h;
    }

    private static async Task AssertStampAsync(BridgeQueueHarness h, Guid id, SessionQueuedMessage original)
    {
        var row = await RowAsync(h, id);
        row.DeliveryAttempts.ShouldBe(original.DeliveryAttempts);
        row.LastDeliveryStartedAt.ShouldBe(original.LastDeliveryStartedAt);
        row.LastDeliveryGeneration.ShouldBe(original.LastDeliveryGeneration);
        row.LastDeliveryBaselineSequence.ShouldBe(original.LastDeliveryBaselineSequence);
        row.Body.ShouldBe(original.Body);
        row.RemoteSpillBody.ShouldBe(original.RemoteSpillBody);
    }

    private static async Task AssertNoRecoveryAsync(BridgeQueueHarness h)
    {
        h.Adapter.Killed.ShouldBeFalse();
        await using var db = h.CreateObserverContext();
        (await db.AgentIncidents.AnyAsync(i => i.SessionId == h.SessionId && i.Kind == AgentIncidentKind.BootWedged))
            .ShouldBeFalse();
    }

    private static async Task<SessionQueuedMessage> RowAsync(BridgeQueueHarness h, Guid? id = null)
    {
        await using var db = h.CreateObserverContext();
        return await db.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(m => m.AgentSessionId == h.SessionId && (id == null || m.Id == id));
    }

    private static async Task<DateTime> GenerationAsync(BridgeQueueHarness h)
    {
        await using var db = h.CreateObserverContext();
        return SessionGeneration.Normalize(await db.AgentSessions.Where(s => s.Id == h.SessionId)
            .Select(s => s.StartedAt).SingleAsync());
    }

    private static JsonDocument Fixture()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".antiphon/fixtures/card-1521/partial-boot-pointers.approved.json");
            if (File.Exists(path)) return JsonDocument.Parse(File.ReadAllText(path));
        }
        throw new FileNotFoundException("CARD-1521 approved capture fixture");
    }

    private static string Pointer()
    {
        using var fixture = Fixture();
        return fixture.RootElement.GetProperty("pointer").GetString()!;
    }

    private static string Compact(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    private static string Frame(string body, int width = 120)
    {
        var terminal = new TerminalScreen(width, 30);
        terminal.Feed("\x1b[2J\x1b[H>_ OpenAI Codex (v0.160.0)\r\n\r\n› ");
        for (var i = 0; i < body.Length; i += width - 4)
        {
            if (i > 0) terminal.Feed("\r\n  ");
            terminal.Feed(body.Substring(i, Math.Min(width - 4, body.Length - i)));
        }
        terminal.Feed("\r\n\r\n  GPT-6-Astra xhigh · /test/cwd\r\n");
        return terminal.GetScreenText();
    }
}
