using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0062: a caller can refine a RUNNING delegate instead of only cancelling it or waiting.
///
/// The load-bearing decisions pinned here: a refinement is never a state change (only a
/// <see cref="AgentTaskEventType.Refined"/> event records it); it rides the ordinary WhenIdle queue
/// with the task marker so the delegate's next finished turn still correlates; a still-Queued task
/// has its BRIEF amended instead of a message queued; Blocked and settled tasks are refused; and an
/// oversized refinement takes the same spill-or-inline path a brief does rather than being typed
/// into a pty that would splice it.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentTaskRefineTests
{
    [Test]
    public async Task a_refinement_to_a_running_task_queues_a_marked_message_and_changes_no_state()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedTaskAsync(workspace.Path, AgentTaskStatus.Working);
        const string message = "The CARD-0050 failures are known-red — do not chase them.";

        var summary = await CreateService().RefineAsync(task.Id, message, CancellationToken.None);
        summary.Status.ShouldBe(AgentTaskStatus.Working, "a refinement steers the task; it never moves it");

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Working);

        var queued = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId)
            .SingleAsync();
        queued.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        queued.Status.ShouldBe(QueuedMessageStatus.Pending, "WhenIdle — it lands between turns, never mid-tool-call");
        queued.Body.ShouldContain(
            DelegationReportFormatter.TaskMarker(task.Id),
            customMessage: "the marker is what lets the delegate's next finished turn still settle this task");
        queued.Body.ShouldContain(message);
        queued.Body.ShouldContain(
            "do NOT end your turn just to acknowledge",
            customMessage: "a bare acknowledgment turn would be read as the report and settle the task");
        queued.Body.Contains('\r').ShouldBeFalse("a CR mid-body would submit the fragment before it");

        var refined = await verify.AgentTaskEvents
            .SingleAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Refined);
        refined.Detail.ShouldContain(
            message, customMessage: "the timeline is what proves the delegate was steered, not off-piste");
    }

    [Test]
    public async Task a_refinement_to_a_dispatched_task_is_accepted_too()
    {
        // Dispatched vs Working is a lifecycle nuance the caller cannot see — both are "running".
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedTaskAsync(workspace.Path, AgentTaskStatus.Dispatched);

        await CreateService().RefineAsync(task.Id, "Skip slice 3 — it landed elsewhere.", CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == sessionId)).ShouldBe(1);
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched);
    }

    [Test]
    public async Task a_refinement_to_a_queued_task_amends_the_brief_instead_of_queueing_a_message()
    {
        // Nothing is running yet, so there is nobody to message — and the brief is built from the
        // goal at dispatch, so amending the goal is what makes the refinement actually arrive.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedTaskAsync(workspace.Path, AgentTaskStatus.Queued);
        const string message = "Also cover the Dispatched status, not just Working.";

        await CreateService().RefineAsync(task.Id, message, CancellationToken.None);

        await using var verify = CreateContext();
        var amended = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        amended.Status.ShouldBe(AgentTaskStatus.Queued);
        amended.Goal.ShouldContain(message);
        amended.Goal.ShouldContain("REFINEMENT", customMessage: "the delegate must see it as an amendment, not the goal");
        DelegationReportFormatter.BuildBrief(amended, new DelegationSettings())
            .ShouldContain(message, customMessage: "what the dispatcher will type must carry the refinement");

        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == sessionId))
            .ShouldBe(0, "no session is running this task yet — a queued note would deliver to nobody");
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Refined)).ShouldBe(1);
    }

    [Test]
    public async Task a_blocked_task_is_redirected_to_the_reply_verb()
    {
        // A Blocked delegate is waiting on an ANSWER; a refinement would not unblock it, and
        // treating it as one would leave the caller believing the question was handled.
        using var workspace = new TempWorkspace();
        var (task, _) = await SeedTaskAsync(workspace.Path, AgentTaskStatus.Blocked);

        var refused = await Should.ThrowAsync<ConflictException>(
            () => CreateService().RefineAsync(task.Id, "a refinement", CancellationToken.None));
        refused.Message.ShouldContain("ANSWER");
    }

    [Test]
    [Arguments(AgentTaskStatus.Succeeded)]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    public async Task a_settled_task_cannot_be_refined(AgentTaskStatus settled)
    {
        // The card's hard rule: nothing may correlate to a settled task and reopen anything.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedTaskAsync(workspace.Path, settled);

        await Should.ThrowAsync<ConflictException>(
            () => CreateService().RefineAsync(task.Id, "too late", CancellationToken.None));

        await using var verify = CreateContext();
        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == sessionId)).ShouldBe(0);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Refined)).ShouldBe(0);
    }

    [Test]
    public async Task an_empty_refinement_is_rejected()
    {
        using var workspace = new TempWorkspace();
        var (task, _) = await SeedTaskAsync(workspace.Path, AgentTaskStatus.Working);

        await Should.ThrowAsync<ValidationException>(
            () => CreateService().RefineAsync(task.Id, "   ", CancellationToken.None));
    }

    [Test]
    public async Task an_oversized_refinement_spills_to_a_file_and_types_a_pointer()
    {
        // Same gate as a brief (CARD-0025/0037): the shipped inbox ceiling is 900 UTF-8 bytes, and
        // anything past it is typed as a pointer to a file rather than handed to a pty that drops
        // whole chunks and reports success.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedTaskAsync(workspace.Path, AgentTaskStatus.Working);
        var message = "Read the whole plan before continuing. "
            + string.Join(" ", Enumerable.Range(0, 300).Select(i => $"detail{i:D3}"));

        await CreateService().RefineAsync(task.Id, message, CancellationToken.None);

        await using var verify = CreateContext();
        var queued = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId)
            .SingleAsync();

        var spillDir = Path.Combine(workspace.Path, ".antiphon");
        var spill = Directory.GetFiles(
                spillDir, $"task-{DelegationReportFormatter.Short(task.Id)}-refinement-*.md")
            .ShouldHaveSingleItem();
        (await File.ReadAllTextAsync(spill)).ShouldContain(message, customMessage: "the full text survives in the file");

        queued.Body.ShouldContain(spill, customMessage: "the pointer names where the refinement actually is");
        queued.Body.ShouldNotContain("detail299", customMessage: "the body itself must stay under the ceiling");
        System.Text.Encoding.UTF8.GetByteCount(queued.Body)
            .ShouldBeLessThanOrEqualTo(
                new DelegationSettings().BriefInlineMaxBytes,
                "a pointer that outgrows the ceiling recreates the failure it exists to prevent");
        queued.Body.ShouldContain(
            DelegationReportFormatter.TaskMarker(task.Id),
            customMessage: "correlation must survive even on the pointer path");

        // The timeline keeps its own copy (head-truncated at the event cap), so the record of what
        // was said does not depend on the workspace surviving.
        (await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Refined))
            .Detail.ShouldContain("detail000");
    }

    // ---- plumbing, mirrored from AgentTaskReplyIntegrationTests --------------------------------

    private static AgentTaskReplyService CreateService()
        => new(
            new ScopeFactory(),
            Options.Create(new DelegationSettings()),
            new MockEventBus(),
            TimeProvider.System,
            NullLogger<AgentTaskReplyService>.Instance);

    private static async Task<(AgentTask Task, Guid SessionId)> SeedTaskAsync(
        string workingDirectory, AgentTaskStatus status)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "Seeded delegate",
            Goal = "Do the thing.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            // A Queued task has no session yet; everything later does.
            AgentSessionId = status == AgentTaskStatus.Queued ? null : sessionId,
            Status = status,
            CreatedAt = now,
            DispatchedAt = status == AgentTaskStatus.Queued ? null : now,
        };

        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            CardId = null,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = workingDirectory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return (task, sessionId);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>
    /// The refine path resolves a real DbContext, the message queue (asserted through its persisted
    /// rows — no live pty) and the summary-building AgentTaskService, exactly like the reply path.
    /// </summary>
    private sealed class ScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly ServiceProvider _provider;

        public ScopeFactory()
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
            services.AddSingleton<Antiphon.Server.Application.Interfaces.IDelegateSessionStopper>(
                new RecordingSessionStopper());
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddScoped<AgentTaskService>();
            _provider = services.BuildServiceProvider();
        }

        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => _provider;
        public object? GetService(Type serviceType) => _provider.GetService(serviceType);
        public void Dispose() { }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-refine-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
