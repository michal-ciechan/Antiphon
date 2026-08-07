using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The reply path: a delegate's finished turn becomes the task's result and a note for its parent.
///
/// The load-bearing behaviour here is the MARKER gate. Correlation matches the
/// <c>[antiphon-task:id]</c> marker carried in the brief, never prompt text — so a human typing in
/// a delegate's terminal can never be mistaken for that task finishing.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentTaskReplyIntegrationTests
{
    [Test]
    public async Task a_marked_turn_settles_the_task_and_stores_the_report_verbatim()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);
        const string report = "Added Fizz(int) in Numbers.cs (+11 lines). 142 passed, 0 failed.";

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.", report);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(report, "the report is the deliverable — it is stored untouched");
        settled.CompletedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task an_unmarked_turn_leaves_the_task_running()
    {
        // A human typed in the delegate's terminal. Without the marker gate this would end the task
        // with the wrong text and send that to the caller as the result.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(sessionId, "what files are in this directory?", "Here's the listing: ...");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched, "a human's turn is not the delegate's report");
        stored.Result.ShouldBeNull();
    }

    [Test]
    public async Task another_tasks_marker_does_not_settle_this_task()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(
            sessionId,
            DelegationReportFormatter.TaskMarker(Guid.NewGuid()) + "\n\nA different task entirely.",
            "Did the other thing.");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
    }

    [Test]
    public async Task a_turn_with_no_assistant_text_yet_leaves_the_task_running()
    {
        // Claude sometimes writes the turn's stop marker BEFORE its reply text. Settling here would
        // record an empty report; the AssistantText's own arrival re-triggers settlement.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), assistantText: null);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.Result.ShouldBeNull();
    }

    [Test]
    public async Task a_delegate_that_asks_a_question_comes_back_blocked_not_finished()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(
            sessionId,
            DelegationReportFormatter.TaskMarker(task.Id),
            "Added Fizz(int).\n\nBuzz throws on negatives — should Fizz match that?");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Blocked, "it needs an answer, not a retry");
    }

    [Test]
    public async Task a_report_under_the_ceiling_is_not_spilled_to_a_file()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), new string('x', 18_000));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.ResultFilePath.ShouldBeNull();
        settled.Result!.Length.ShouldBe(18_000);
        Directory.Exists(Path.Combine(workspace.Path, ".antiphon")).ShouldBeFalse();
    }

    [Test]
    public async Task an_oversized_report_is_backstopped_to_a_file_by_the_server()
    {
        // The delegate was told to spill and didn't. The server writes the file itself, so the
        // excerpt the caller receives has somewhere real to point — and the full text survives.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);
        var huge = new string('y', 25_000);

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), huge);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.ResultFilePath.ShouldNotBeNull();
        File.Exists(settled.ResultFilePath).ShouldBeTrue();
        (await File.ReadAllTextAsync(settled.ResultFilePath!)).Length.ShouldBe(25_000);
        settled.Result!.Length.ShouldBe(25_000, "the task row always keeps the untouched original");
    }

    [Test]
    public async Task a_spill_file_the_delegate_wrote_itself_is_used_as_is()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);
        var spillPath = Path.Combine(
            workspace.Path, ".antiphon", $"task-{DelegationReportFormatter.Short(task.Id)}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(spillPath)!);
        await File.WriteAllTextAsync(spillPath, "THE DELEGATE'S OWN FULL DETAIL");

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), new string('z', 25_000));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.ResultFilePath.ShouldBe(spillPath);
        (await File.ReadAllTextAsync(spillPath))
            .ShouldBe("THE DELEGATE'S OWN FULL DETAIL", "the delegate's own file must not be overwritten");
    }

    [Test]
    public async Task the_completion_note_is_delivered_into_the_parents_session()
    {
        // The whole point: the caller learns the outcome without reading a transcript.
        using var workspace = new TempWorkspace();
        var parentSessionId = await SeedSessionAsync(workspace.Path);
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path, parentSessionId);

        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Rewrote the section. 34 lines changed.");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var queued = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == parentSessionId)
            .ToListAsync();

        queued.Count.ShouldBe(1);
        queued[0].Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        queued[0].ConversationKey.ShouldBe($"task:{task.RootTaskId:N}", "same-root results coalesce");
        queued[0].Body.ShouldContain("Rewrote the section. 34 lines changed.");
        queued[0].Body.ShouldContain(DelegationReportFormatter.Short(task.Id));
        queued[0].Body.Contains('\r').ShouldBeFalse("a CR mid-body would submit the fragment before it");
    }

    [Test]
    public async Task a_task_with_no_parent_session_settles_without_delivering_anywhere()
    {
        // The manual entry point: the result lands on the board only.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path, parentSessionId: null);

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        // Scoped to THIS task — the fixture's database is shared, so a global count would pick up
        // rows other tests legitimately left behind.
        var shortId = DelegationReportFormatter.Short(task.Id);
        (await verify.SessionQueuedMessages.CountAsync(m => m.Body.Contains(shortId))).ShouldBe(0);
    }

    [Test]
    public async Task token_spend_is_rolled_up_onto_the_task()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.", inputTokens: 50_000, outputTokens: 4_000);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.TokensIn.ShouldBe(50_000);
        settled.TokensOut.ShouldBe(4_000);
        settled.CostUsd.ShouldBeGreaterThan(0m, "the per-root ceiling can only work if spend is recorded");
    }

    [Test]
    public async Task a_session_running_no_task_is_ignored()
    {
        using var workspace = new TempWorkspace();
        var sessionId = await SeedSessionAsync(workspace.Path);

        await SeedTurnAsync(sessionId, "just a chat", "sure thing");

        // Must be a clean no-op — every ordinary agent session hits this path on every turn-end.
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static AgentTaskReplyService CreateService()
    {
        var settings = new DelegationSettings { ReplyInlineMaxChars = 20_000 };
        return new AgentTaskReplyService(
            new TestScopeFactory(),
            Options.Create(settings),
            new MockEventBus(),
            TimeProvider.System,
            NullLogger<AgentTaskReplyService>.Instance);
    }

    private static async Task<(AgentTask Task, Guid SessionId)> SeedDispatchedTaskAsync(
        string workingDirectory, Guid? parentSessionId = null)
    {
        var sessionId = await SeedSessionAsync(workingDirectory);
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ParentSessionId = parentSessionId,
            ReplyTo = parentSessionId is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session,
            Title = "Seeded delegate",
            Goal = "Do the thing.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = DateTime.UtcNow,
            DispatchedAt = DateTime.UtcNow,
        };

        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return (task, sessionId);
    }

    private static async Task<Guid> SeedSessionAsync(string cwd)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            CardId = null,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    /// <summary>A prompt, optional assistant text, then a TurnEnd — the shape a real turn leaves.</summary>
    private static async Task SeedTurnAsync(
        Guid sessionId, string prompt, string? assistantText, int? inputTokens = null, int? outputTokens = null)
    {
        await using var db = CreateContext();
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;

        db.TranscriptEntries.Add(NewEntry(sessionId, ++seq, TranscriptKinds.UserPrompt, prompt));
        if (assistantText is not null)
        {
            var entry = NewEntry(sessionId, ++seq, TranscriptKinds.AssistantText, assistantText);
            entry.InputTokens = inputTokens;
            entry.OutputTokens = outputTokens;
            db.TranscriptEntries.Add(entry);
        }
        var end = NewEntry(sessionId, ++seq, TranscriptKinds.TurnEnd, null);
        end.StopReason = "end_turn";
        db.TranscriptEntries.Add(end);
        await db.SaveChangesAsync();
    }

    private static TranscriptEntry NewEntry(Guid sessionId, long sequence, string kind, string? text) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = kind,
        Text = text,
        CreatedAt = DateTime.UtcNow,
    };

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>
    /// The reply service is a singleton that opens a DI scope per operation. This supplies the two
    /// services it resolves — a real DbContext and a queue whose runtime is never actually driven
    /// (delivery is asserted through the persisted queue rows, not a live pty).
    /// </summary>
    private sealed class TestScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly ServiceProvider _provider;

        public TestScopeFactory()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            services.AddSingleton<Antiphon.Server.Application.Interfaces.IEventBus, MockEventBus>();
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new DelegationSettings()));
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            _provider = services.BuildServiceProvider();
        }

        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => _provider;
        public object? GetService(Type serviceType) => _provider.GetService(serviceType);
        public void Dispose() { }

        private sealed class TempWorkspaceMarker;
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-reply-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
