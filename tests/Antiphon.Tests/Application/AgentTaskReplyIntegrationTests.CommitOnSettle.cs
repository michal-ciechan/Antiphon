using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Antiphon.Tests.Application;

public partial class AgentTaskReplyIntegrationTests
{
    [Test]
    public async Task C527_unattributable_dirty_tree_spawns_a_commit_child_routed_by_the_card_pin()
    {
        await using var db = CreateContext();
        var card = await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0527T");
        var pin = new RoutingPin { Id = Guid.NewGuid(), CardId = card.Id, Role = AgentTaskRole.Commit,
            Strength = RoutingPinStrength.Required, Provenance = RoutingPinProvenance.Human,
            CandidatesJson = RoutingCandidate.Serialize([new(AgentKind.Codex, AgentModelLevel.Low), new(AgentKind.ClaudeCode, AgentModelLevel.Medium)]),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Reason = "C527" };
        db.RoutingPins.Add(pin); await db.SaveChangesAsync();
        try
        {
            using var w = await C527World.Create(t => { t.CardId = card.Id; t.AgentKind = AgentKind.Codex; }, pins: true);
            await w.Write("x.md", false); AttachTerminal(w.Factory, w.Parent); await w.Settle(); await w.Unchanged();
            var child = await w.Child();
            child.Role.ShouldBe(AgentTaskRole.Commit); child.Kind.ShouldBe(AgentTaskKind.Worker);
            child.AgentKind.ShouldBe(AgentKind.Codex); child.ModelLevel.ShouldBe(AgentModelLevel.Low); child.RoutingPinId.ShouldBe(pin.Id);
            child.RootTaskId.ShouldBe(w.Row.RootTaskId); child.ParentSessionId.ShouldBe(w.Parent); child.CardId.ShouldBe(card.Id);
            child.Workspace.ShouldBe(WorkspaceMode.Shared); child.WorkingDirectory.ShouldBe(w.Repo.Path); child.Ephemeral.ShouldBeTrue(); child.MaxAttempts.ShouldBe(2);
            child.CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never); child.CommitBaselineSha.ShouldBe(w.Before.Trim()); child.Title.ShouldBe("Commit: Seeded delegate");
            child.Goal.ShouldContain("x.md"); child.Goal.ShouldContain("unattributable");
            await Queue(w.Factory).FlushSessionAsync(w.Parent, default);
            (await AssertParentReceivedNoteAsync(w.Parent, w.Row, "Finished the requested change.")).Prompt.Text.ShouldContain($"git=uncommitted:1 -> commit task {DelegationReportFormatter.Short(child.Id)}");
        }
        finally { await db.RoutingPins.Where(p => p.Id == pin.Id).ExecuteDeleteAsync(); }
    }

    [Test]
    public async Task C527_busy_parent_receives_the_committed_note_on_its_turn_end()
    {
        using var w = await C527World.Create(); await w.Write(); var terminal = AttachTerminal(w.Factory, w.Parent);
        await SeedEntryAsync(w.Parent, TranscriptKinds.UserPrompt, "another request", DateTime.UtcNow);
        await SeedEntryAsync(w.Parent, TranscriptKinds.AssistantText, "working", DateTime.UtcNow);
        await w.Settle(); await Queue(w.Factory).FlushSessionAsync(w.Parent, default);
        terminal.Inputs.ShouldBeEmpty(); (await w.Note()).Status.ShouldBe(QueuedMessageStatus.Pending);
        await SeedEntryAsync(w.Parent, TranscriptKinds.TurnEnd, null, DateTime.UtcNow);
        await Queue(w.Factory).OnTurnEndAsync(w.Parent, default);
        (await AssertParentReceivedNoteAsync(w.Parent, w.Row, "Finished the requested change.")).Prompt.Text.ShouldContain("git=committed:");
        terminal.SubmittedBodies.Count.ShouldBe(1);
    }

    private sealed class ThrowOnceSaveInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Fired { get; private set; }
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData data, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken ct = default)
        {
            if (!Fired && data.Context!.ChangeTracker.Entries<AgentTaskEvent>().Any(e => e.Entity.Type == AgentTaskEventType.Committed))
            { Fired = true; throw new InvalidOperationException("C527 injected save failure after commit"); }
            return ValueTask.FromResult(result);
        }
    }
    [Test]
    public async Task C527_settle_save_failure_after_the_commit_recovers_without_a_second_commit()
    {
        var interceptor = new ThrowOnceSaveInterceptor(); using var w = await C527World.Create(save: interceptor); await w.Write();
        var count = int.Parse(await w.Repo.GitReadAsync("rev-list", "--count", "HEAD"));
        await w.Settle(); interceptor.Fired.ShouldBeTrue();
        await using (var db = CreateContext()) (await db.AgentTasks.SingleAsync(t => t.Id == w.Row.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        await CreateService(w.Factory).OnTurnEndAsync(w.Session, default);
        int.Parse(await w.Repo.GitReadAsync("rev-list", "--count", "HEAD")).ShouldBe(count + 1);
        (await w.Note()).NoteHeader.ShouldContain("committed:");
        await using var verify = CreateContext();
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == w.Row.Id && e.Type == AgentTaskEventType.Committed)).ShouldBe(1);
        (await verify.AgentTasks.SingleAsync(t => t.Id == w.Row.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        (await verify.AgentTasks.CountAsync(t => t.ParentTaskId == w.Row.Id)).ShouldBe(0);
    }

    public enum Ineligible { Blocked, Failed, ReadOnly, CommitRole, MutationRole, SourceLanding }
    [Test]
    [Arguments(Ineligible.Blocked)][Arguments(Ineligible.Failed)][Arguments(Ineligible.ReadOnly)]
    [Arguments(Ineligible.CommitRole)][Arguments(Ineligible.MutationRole)][Arguments(Ineligible.SourceLanding)]
    public async Task C527_ineligible_tasks_never_run_the_hook(Ineligible c)
    {
        Guid? landing = null;
        if (c == Ineligible.SourceLanding)
        {
            await using var db = CreateContext(); var owner = new AgentTask { Id = Guid.NewGuid(), Title = "source", Goal = "source", WorkingDirectory = "source", CreatedAt = DateTime.UtcNow }; owner.RootTaskId = owner.Id;
            db.AgentTasks.Add(owner); await db.SaveChangesAsync();
            var op = new AgentTaskLanding { Id = Guid.NewGuid(), TaskId = owner.Id }; db.AgentTaskLandings.Add(op); await db.SaveChangesAsync(); landing = op.Id;
        }
        using var w = await C527World.Create(t =>
        {
            if (c == Ineligible.ReadOnly) t.Workspace = WorkspaceMode.ReadOnly;
            if (c == Ineligible.CommitRole) t.Role = AgentTaskRole.Commit;
            if (c == Ineligible.MutationRole) t.Role = AgentTaskRole.Mutation;
            t.SourceLandingOperationId = landing;
        });
        await w.Write(); await w.Settle("Wrote `a.md`.", c == Ineligible.Blocked ? "blocked" : c == Ineligible.Failed ? "failed" : "done");
        await w.Unchanged(); w.Spy.Verbs.ShouldNotContain("add");
        await using var verify = CreateContext();
        (await verify.AgentTasks.CountAsync(t => t.ParentTaskId == w.Row.Id)).ShouldBe(0);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == w.Row.Id && e.Type == AgentTaskEventType.Committed)).ShouldBe(0);
        if (c is Ineligible.Blocked or Ineligible.Failed) (await w.Note()).Body.ShouldContain("still uncommitted");
    }

    private static async Task C527Audit(bool ignored, bool upstream)
    {
        using var w = await C527World.Create(t => t.Role = AgentTaskRole.Commit);
        if (upstream) await w.Repo.AddBareOriginAsync();
        await using (var db = CreateContext())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == w.Row.Id); row.CommitBaselineSha = w.Before.Trim();
            db.AgentTaskEvents.Add(new() { Id = Guid.NewGuid(), AgentTaskId = row.Id, Type = AgentTaskEventType.Created, At = DateTime.UtcNow,
                Detail = "Commit upstream baseline: " + (upstream ? w.Before.Trim() : "none") }); await db.SaveChangesAsync();
        }
        await w.Write(ignored ? "a.secret" : "a.md", false);
        await w.Repo.GitAsync("add", "-f", ignored ? "a.secret" : "a.md"); await w.Repo.GitAsync("commit", "-m", "outside gate");
        if (upstream) await w.Repo.GitAsync("push");
        var sha = (await w.Repo.GitReadAsync("rev-parse", "HEAD")).Trim(); AttachTerminal(w.Factory, w.Parent);
        await w.Settle(); await Queue(w.Factory).FlushSessionAsync(w.Parent, default);
        var receipt = await AssertParentReceivedNoteAsync(w.Parent, w.Row, "Finished the requested change.");
        await using var verify = CreateContext();
        var warnings = await verify.AgentTaskEvents.Where(e => e.AgentTaskId == w.Row.Id && e.Type == AgentTaskEventType.Warning).Select(e => e.Detail).ToListAsync();
        if (ignored)
        {
            var expected = $"REVERT commit {sha[..7]}: it contains ignored path(s) a.secret";
            warnings.ShouldContain(expected); receipt.Prompt.Text.ShouldContain(expected);
            (await verify.AgentIncidents.AnyAsync(i => i.SessionId == w.Session && i.Kind == AgentIncidentKind.DelegateCommitAudit)).ShouldBeTrue();
        }
        else if (upstream) warnings.ShouldContain(x => x.Contains("pushed") && x.Contains(sha[..7]));
        else
        {
            warnings.ShouldContain($"commit {sha[..7]} was made outside the gate");
            receipt.Prompt.Text.ShouldNotContain("REVERT");
            (await verify.AgentIncidents.AnyAsync(i => i.SessionId == w.Session && i.Kind == AgentIncidentKind.DelegateCommitAudit)).ShouldBeFalse();
        }
    }
    [Test] public Task C527_commit_child_audit_flags_an_ignored_path_committed_outside_the_gate() => C527Audit(true, false);
    [Test] public Task C527_commit_child_audit_flags_a_commit_without_the_gate_trailer() => C527Audit(false, false);
    [Test] public Task C527_commit_child_audit_flags_upstream_movement() => C527Audit(false, true);

    [Test]
    public async Task a_shared_report_naming_an_uncommitted_path_is_committed_when_the_policy_is_on()
    { using var w = await C527World.Create(); await w.Write("docs/x.md", false); await w.Settle("Wrote `docs/x.md`."); (await w.Note()).NoteHeader.ShouldContain("git=committed:"); }
    private static async Task SeedFileEditAsync(Guid sessionId, string toolName, string absolutePath, DateTime at)
    {
        await using var db = CreateContext();
        var seq = await db.TranscriptEntries.Where(e => e.AgentSessionId == sessionId).MaxAsync(e => (long?)e.Sequence) ?? 0;
        var entry = NewEntry(sessionId, seq + 1, TranscriptKinds.ToolCall, null);
        entry.ToolName = toolName; entry.ToolUseId = Guid.NewGuid().ToString(); entry.CreatedAt = at;
        entry.ToolInput = JsonSerializer.Serialize(new Dictionary<string, string> { [toolName == "NotebookEdit" ? "notebook_path" : "file_path"] = absolutePath });
        db.TranscriptEntries.Add(entry); await db.SaveChangesAsync();
    }

    private sealed class C527World : IDisposable
    {
        public ScratchGitRepo Repo { get; } = new("c527-reply");
        public RecordingGitWorkspaceService Spy { get; } = new();
        public TestScopeFactory Factory { get; private set; } = null!;
        public AgentTask Row { get; private set; } = null!;
        public Guid Session { get; private set; }
        public Guid Parent { get; private set; }
        public string Before { get; private set; } = "";
        public static async Task<C527World> Create(Action<AgentTask>? configure = null, DelegationSettings? settings = null, bool pins = false,
            Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor? save = null)
        {
            var w = new C527World();
            await w.Repo.CommitFileAsync(".gitignore", ".antiphon/\n*.secret\n_private/\n");
            await w.Repo.CommitFileAsync("tracked.txt", "base");
            w.Before = await w.Repo.GitReadAsync("rev-parse", "HEAD");
            w.Factory = new(w.Repo.WorktreeRoot, delegation: settings ?? new DelegationSettings { PtySingleChunkBytes = 43200 },
                routingPins: pins, gitSpy: w.Spy, saveInterceptor: save);
            w.Parent = await SeedSessionAsync(w.Repo.Path); await SeedParentHistoryAsync(w.Parent);
            (w.Row, w.Session) = await SeedDispatchedTaskAsync(w.Repo.Path, w.Parent, t =>
            { t.RepoPath = w.Repo.Path; t.Role = AgentTaskRole.Custom; t.DispatchedAt = DateTime.UtcNow.AddMinutes(-1); configure?.Invoke(t); });
            return w;
        }
        public async Task Write(string path = "a.md", bool transcript = true, bool old = false, string tool = "Write")
        {
            var full = Path.Combine(Repo.Path, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, "task edit");
            if (transcript) await SeedFileEditAsync(Session, tool, full, old ? Row.DispatchedAt!.Value.AddSeconds(-1) : DateTime.UtcNow);
        }
        public async Task Settle(string report = "Finished the requested change.", string verdict = "done")
        {
            await SeedTurnAsync(Session, DelegationReportFormatter.TaskMarker(Row.Id), report + "\n" + DelegationReportFormatter.ReportToken(Row.Id, verdict), closingVerdict: false);
            await CreateService(Factory).OnTurnEndAsync(Session, default);
        }
        public async Task<SessionQueuedMessage> Note()
        { await using var db = CreateContext(); return await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == Row.Id); }
        public async Task<AgentTask> Child()
        { await using var db = CreateContext(); return await db.AgentTasks.SingleAsync(t => t.ParentTaskId == Row.Id); }
        public async Task Unchanged()
        { (await Repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(Before); Spy.Verbs.ShouldNotContain("commit"); }
        public void Dispose() { Repo.Dispose(); Factory.Dispose(); }
    }

    [Test]
    public async Task C527_tier1_commits_the_transcript_footprint_and_delivers_committed_to_the_parent()
    {
        using var w = await C527World.Create(); await w.Write(); await w.Write("b.md");
        var terminal = AttachTerminal(w.Factory, w.Parent);
        await w.Settle(); await Queue(w.Factory).FlushSessionAsync(w.Parent, default);
        var sha = (await w.Repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        (await w.Repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")).Trim().ShouldBe("a.md\nb.md");
        (await w.Repo.GitReadAsync("status", "--porcelain")).ShouldBeEmpty();
        (await w.Repo.GitReadAsync("log", "-1", "--format=%B")).ShouldContain($"antiphon-task: {w.Row.Id}");
        await using var db = CreateContext();
        var committed = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == w.Row.Id && e.Type == AgentTaskEventType.Committed);
        committed.Detail.ShouldContain(sha[..7]); committed.Detail.ShouldContain("a.md"); committed.Detail.ShouldContain("b.md");
        (await AssertParentReceivedNoteAsync(w.Parent, w.Row, "Finished the requested change.")).Prompt.Text.ShouldContain($"git=committed:{sha[..7]} (2 files)");
        terminal.SubmittedBodies.Count.ShouldBe(1); w.Spy.Verbs.ShouldContain("commit"); w.Spy.Verbs.ShouldNotContain("push");
    }

    [Test]
    public async Task C527_residual_dirty_path_outside_the_footprint_is_left_and_named()
    {
        using var w = await C527World.Create(); await w.Write(); await w.Write("b.md"); await w.Write("other.md", false); await w.Settle();
        (await w.Repo.GitReadAsync("status", "--porcelain")).Trim().ShouldBe("?? other.md");
        (await w.Note()).NoteHeader.ShouldContain("(2 files); 1 other dirty path(s) left as found");
        await using var db = CreateContext(); (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == w.Row.Id && e.Type == AgentTaskEventType.Warning)).ShouldBeFalse();
    }

    [Test]
    public async Task C527_report_named_path_is_footprint_when_the_transcript_is_empty()
    { using var w = await C527World.Create(); await w.Write("docs/x.md", false); await w.Settle("Wrote `docs/x.md`."); (await w.Note()).NoteHeader.ShouldContain("committed:"); }
    [Test]
    public async Task C527_notebook_path_edits_count_as_footprint()
    { using var w = await C527World.Create(); await w.Write("book.ipynb", tool: "NotebookEdit"); await w.Settle(); (await w.Note()).NoteHeader.ShouldContain("committed:"); }
    [Test]
    public async Task C527_NoCommit_task_leaves_the_tree_with_the_no_commit_header()
    {
        using var w = await C527World.Create(t => t.CommitOnSettle = CommitOnSettlePolicy.Never); await w.Write(); await w.Settle(); await w.Unchanged();
        var note = await w.Note(); note.NoteHeader.ShouldContain("uncommitted:1 (no-commit)");
        note.Body.ShouldContain("1 file(s) left uncommitted as requested (-NoCommit)."); note.Body.ShouldNotContain("Commit before building on it");
    }
    private static async Task<Guid> C527ProjectOff()
    {
        await using var db = CreateContext(); var p = new Project { Id = Guid.NewGuid(), Name = "C527", CommitOnSettle = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Projects.Add(p); await db.SaveChangesAsync(); return p.Id;
    }
    [Test]
    public async Task C527_project_off_inherited_skips_with_todays_warning()
    { var p = await C527ProjectOff(); using var w = await C527World.Create(t => t.ProjectId = p); await w.Write(); await w.Settle(); await w.Unchanged(); (await w.Note()).NoteHeader.ShouldContain("(commit-on-settle off)"); (await w.Note()).Body.ShouldContain("Commit before building on it."); }
    [Test]
    public async Task C527_task_Always_beats_project_off()
    { var p = await C527ProjectOff(); using var w = await C527World.Create(t => { t.ProjectId = p; t.CommitOnSettle = CommitOnSettlePolicy.Always; }); await w.Write(); await w.Settle(); (await w.Note()).NoteHeader.ShouldContain("committed:"); }
    [Test]
    public async Task C527_global_off_with_project_null_skips()
    { using var w = await C527World.Create(settings: new() { CommitOnSettle = false }); await w.Write(); await w.Settle(); await w.Unchanged(); (await w.Note()).NoteHeader.ShouldContain("(commit-on-settle off)"); }
    [Test]
    public async Task C527_Agent_policy_skips_tier1_and_spawns_the_child()
    { using var w = await C527World.Create(t => t.CommitOnSettle = CommitOnSettlePolicy.Agent); await w.Write(); await w.Settle(); await w.Unchanged(); (await w.Child()).Goal.ShouldContain("agent-policy"); (await w.Note()).NoteHeader.ShouldContain("-> commit task"); }
    [Test]
    public async Task C527_commit_child_falls_back_to_role_policy_with_a_warning_when_no_pin_service()
    {
        using var w = await C527World.Create(); await w.Write(transcript: false); await w.Settle(); var child = await w.Child();
        child.AgentKind.ShouldBe(AgentKind.ClaudeCode); child.ModelLevel.ShouldBe(AgentModelLevel.Medium); child.RoutingPinId.ShouldBeNull();
        await using var db = CreateContext(); (await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == child.Id && e.Type == AgentTaskEventType.Warning)).Detail.ShouldContain("pin service");
    }
    [Test]
    public async Task C527_edits_before_dispatch_are_not_in_the_footprint()
    { using var w = await C527World.Create(); await w.Write(old: true); await w.Settle(); await w.Unchanged(); (await w.Child()).Goal.ShouldContain("unattributable"); }
    [Test]
    public async Task C527_more_than_twenty_dirty_paths_are_all_attributed_from_the_transcript()
    { using var w = await C527World.Create(); for (var i = 0; i < 25; i++) await w.Write($"file{i}.md"); await w.Settle(); (await w.Note()).NoteHeader.ShouldContain("(25 files)"); (await w.Repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(25); }
    [Test]
    public async Task C527_deleted_gitignore_refuses_spawns_the_child_and_warns()
    { using var w = await C527World.Create(); await w.Write(); File.Delete(Path.Combine(w.Repo.Path, ".gitignore")); await w.Settle(); await w.Unchanged(); (await w.Child()).Goal.ShouldContain("ignore-rules-changed"); (await w.Child()).Goal.ShouldContain(".gitignore"); (await w.Repo.GitReadAsync("diff", "--cached", "--name-only")).ShouldBeEmpty(); }
    [Test]
    public async Task C527_held_lease_is_RepositoryBusy_and_spawns_the_child()
    { using var w = await C527World.Create(); await w.Write(); await using var held = await w.Factory.ServiceProvider.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(w.Repo.Path, default); await w.Settle(); await w.Unchanged(); (await w.Child()).Goal.ShouldContain("repository-busy"); }
    [Test]
    public async Task C527_hook_failure_is_CommitFailed_and_the_child_brief_carries_stderr()
    {
        using var w = await C527World.Create(); await w.Write(); await w.Repo.InstallFailingPreCommitHookAsync("gate says no"); await w.Settle();
        (await w.Repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(w.Before); (await w.Child()).Goal.ShouldContain("gate says no");
        (await w.Note()).NoteHeader.ShouldNotContain("committed:"); (await w.Repo.GitReadAsync("diff", "--cached", "--name-only")).Trim().ShouldBe("a.md");
    }
    [Test]
    public async Task C527_task_cap_leaves_the_header_and_warning()
    { using var w = await C527World.Create(settings: new() { MaxTasksPerRoot = 1 }); await w.Write(transcript: false); await w.Settle(); (await w.Note()).NoteHeader.ShouldContain("uncommitted:1 (commit task not spawned: run at task cap)"); (await w.Note()).Body.ShouldContain("Commit before building on it."); }
    [Test]
    public async Task C527_gate_refusal_with_no_child_possible_renders_commit_refused()
    { using var w = await C527World.Create(settings: new() { MaxTasksPerRoot = 1 }); await w.Write(); File.Delete(Path.Combine(w.Repo.Path, ".gitignore")); await w.Settle(); (await w.Note()).NoteHeader.ShouldContain("commit refused: ignore rules changed (.gitignore)"); }
    [Test]
    public async Task C527_tracked_then_ignored_path_refuses_the_whole_footprint()
    { using var w = await C527World.Create(); await w.Repo.CommitFileAsync(".gitignore", ".antiphon/\n*.secret\ntracked.txt\n"); await w.Write("tracked.txt"); await w.Write("new.txt"); var head = await w.Repo.GitReadAsync("rev-parse", "HEAD"); await w.Settle(); (await w.Repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head); var status = await w.Repo.GitReadAsync("status", "--porcelain"); status.ShouldContain("tracked.txt"); status.ShouldContain("?? new.txt"); (await w.Child()).Goal.ShouldContain("ignored-path-staged"); }
    [Test]
    public async Task C527_foreign_staged_path_survives_tier1_untouched()
    { using var w = await C527World.Create(); await w.Write("foo.txt", false); await w.Repo.GitAsync("add", "foo.txt"); await w.Write(); await w.Settle(); (await w.Repo.GitReadAsync("diff", "--cached", "--name-only")).Trim().ShouldBe("foo.txt"); (await w.Repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")).Trim().ShouldBe("a.md"); }
}
