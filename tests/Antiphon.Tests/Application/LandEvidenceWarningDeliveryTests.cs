using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandEvidenceWarningDeliveryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C788_LandEvidenceRefusalReceipt(bool busy)
    {
        foreach (var mode in new[] { "owner_recovery", "adoption" })
        foreach (var mismatch in new[] { "subject", "ref", "sha" })
        {
            var row = $"busy={busy} {mode}/{mismatch}";
            await using var rig = await RefusalRig.CreateAsync(busy, mode, mismatch);
            var note = await rig.RefuseAsync(row);
            await rig.ReconcileAsync(note.Id);
            await rig.DeliverAsync(row);
            await rig.AssertReceiptAsync(note, row);
        }
    }

    [Test]
    [Arguments("refusal-transaction")]
    [Arguments("refusal-committed")]
    [Arguments("before-enqueue")]
    [Arguments("queue-inserted")]
    [Arguments("receipt-before-save")]
    public async Task C788_LandEvidenceRefusalRecovery(string cut)
    {
        foreach (var busy in new[] { false, true })
        foreach (var mode in new[] { "owner_recovery", "adoption" })
        {
            var row = $"{cut} busy={busy} {mode}";
            await using var rig = await RefusalRig.CreateAsync(busy, mode, "subject");
            if (cut == "refusal-transaction") rig.Land.Fault.TerminalCut = "before-save";
            var note = await rig.RefuseAsync(row, expectTransactionCut: cut == "refusal-transaction");
            if (cut is "before-enqueue" or "queue-inserted") rig.Boundary.Throw.Add(cut);
            if (cut != "refusal-committed")
            {
                await rig.ReconcileAsync(note.Id);
                if (cut == "receipt-before-save")
                {
                    rig.Boundary.Throw.Add(cut);
                    await rig.DeliverAsync(row);
                }
            }
            if (cut is "before-enqueue" or "queue-inserted" or "receipt-before-save")
                rig.Boundary.Reached.ShouldContain(cut, row + ": named cut reached");
            await rig.Land.RestartServicesAsync();
            await rig.ReconcileAsync(note.Id, recovered: true);
            await rig.DeliverAsync(row);
            await rig.AssertReceiptAsync(note, row);
        }
    }

    private sealed class RefusalRig : IAsyncDisposable
    {
        public LandingSafetyHarness Land { get; } = new();
        public BridgeQueueHarness Caller { get; private set; } = null!;
        public C544Boundary Boundary { get; } = new();
        private bool _busy;
        private string _mode = "";
        private string _mismatch = "";
        private string _sha = "";
        private string _requiredRef = "";
        private Guid _subject;
        private Guid _evidence;
        private Guid _request;
        private Guid _note;

        public static async Task<RefusalRig> CreateAsync(bool busy, string mode, string mismatch)
        {
            var rig = new RefusalRig { _busy = busy, _mode = mode, _mismatch = mismatch };
            await rig.Land.InitializeAsync();
            rig.Caller = await BridgeQueueHarness.CreateAsync(new()
                { AlwaysOn = false, ConnectionString = rig.Land.Schema.ConnectionString });
            rig.Land.Messages = rig.Caller.Queue;
            await BridgeQueueHarness.InsertEntryAsync(rig.Caller.SessionId, TranscriptKinds.UserPrompt,
                "land the owner", connectionString: rig.Land.Schema.ConnectionString);
            await BridgeQueueHarness.InsertEntryAsync(rig.Caller.SessionId, TranscriptKinds.TurnEnd,
                stopReason: TranscriptKinds.StopReasons.EndTurn, connectionString: rig.Land.Schema.ConnectionString);
            if (busy)
                await BridgeQueueHarness.InsertEntryAsync(rig.Caller.SessionId, TranscriptKinds.AssistantText,
                    "caller is mid-turn", connectionString: rig.Land.Schema.ConnectionString);
            await rig.PrepareAsync();
            return rig;
        }

        private async Task PrepareAsync()
        {
            var ownerTip = await Land.AddSourceAsync();
            await Land.Fixture.RequiredAsync(Land.Fixture.Source, "push", "origin", Land.Fixture.SourceRef);
            _subject = Land.Fixture.TaskId;
            _sha = ownerTip;
            _requiredRef = Land.Fixture.SourceRef;
            if (_mode == "adoption")
            {
                _subject = Guid.NewGuid();
                _requiredRef = $"refs/heads/feat/card-task-{_subject:N}";
                var sourcePath = Path.Combine(Land.Fixture.Root, "trees", "delivery-repair");
                await Land.Fixture.RequiredAsync(Land.Fixture.Repository, "worktree", "add", "-b",
                    _requiredRef[11..], sourcePath, ownerTip);
                await File.WriteAllTextAsync(Path.Combine(sourcePath, "repair.txt"), "reviewed repair\n");
                await Land.Fixture.RequiredAsync(sourcePath, "add", ".");
                await Land.Fixture.RequiredAsync(sourcePath, "commit", "-m", "reviewed repair");
                _sha = (await Land.Fixture.RequiredAsync(sourcePath, "rev-parse", "HEAD")).Trim();
                await Land.Fixture.RequiredAsync(sourcePath, "push", "origin", _requiredRef);
                await using var db = Land.CreateContext();
                var now = DateTime.UtcNow;
                var project = Guid.NewGuid(); var board = Guid.NewGuid();
                var column = Guid.NewGuid(); var card = Guid.NewGuid();
                db.Projects.Add(new Project { Id = project, Name = "C788 delivery", LocalRepositoryPath = Land.Fixture.Repository,
                    CreatedAt = now, UpdatedAt = now });
                db.Boards.Add(new Board { Id = board, ProjectId = project, Name = "C788 delivery",
                    CreatedAt = now, UpdatedAt = now });
                db.BoardColumns.Add(new BoardColumn { Id = column, BoardId = board, Name = "Ready", StateKey = "ready",
                    CreatedAt = now, UpdatedAt = now });
                db.Cards.Add(new Card { Id = card, BoardId = board, BoardColumnId = column,
                    Identifier = "CARD-0788", Title = "C788 delivery", CreatedAt = now, UpdatedAt = now });
                var owner = await db.AgentTasks.SingleAsync(t => t.Id == Land.Fixture.TaskId);
                owner.CardId = card; owner.ProjectId = project;
                db.AgentTasks.Add(new AgentTask
                {
                    Id = _subject, RootTaskId = _subject, Title = "reviewed source", Goal = "repair",
                    Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
                    WorkingDirectory = Land.Fixture.Repository, RepoPath = Land.Fixture.Repository,
                    WorktreePath = sourcePath, WorktreeBranch = _requiredRef[11..], WorktreeBaseSha = ownerTip,
                    Status = AgentTaskStatus.Failed, CardId = card, ProjectId = project,
                    ReplyTo = AgentTaskReplyTo.None, CreatedAt = now, CompletedAt = now,
                });
                await db.SaveChangesAsync();
            }
            await using (var db = Land.CreateContext())
            {
                var owner = await db.AgentTasks.SingleAsync(t => t.Id == Land.Fixture.TaskId);
                owner.Status = AgentTaskStatus.Failed;
                owner.ReplyTo = AgentTaskReplyTo.Session;
                owner.ParentSessionId = Caller.SessionId;
                var evidence = new StageOutcome
                {
                    Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
                    Source = StageOutcomeSource.Delegate, SubjectTaskId = _subject, StageTaskId = Guid.NewGuid(),
                    ReviewedSourceSha = _sha, ReviewedSourceClean = true, ReviewedSourceRef = _requiredRef,
                    ReviewedRepositoryPath = Land.Fixture.Repository, CommissionedRound = VerificationRound.Final,
                    OrdinaryScopeCompleted = VerificationScope.Full, RecordedAt = DateTime.UtcNow,
                };
                db.StageOutcomes.Add(evidence);
                await db.SaveChangesAsync();
                _evidence = evidence.Id;
            }
            var accepted = await Land.RequestAsync(expectedSourceSha: _sha, reviewEvidenceId: _evidence,
                recoverReviewedSource: _mode == "owner_recovery",
                adoptFromTaskId: _mode == "adoption" ? _subject : null);
            accepted.Status.ShouldBe("queued", "approval was admitted before the fixture mismatch");
            _request = accepted.RequestId;
            await using (var db = Land.CreateContext())
            {
                var mutation = db.StageOutcomes.Where(o => o.Id == _evidence);
                if (_mismatch == "subject")
                    await mutation.ExecuteUpdateAsync(s => s.SetProperty(o => o.SubjectTaskId, Guid.NewGuid()));
                else if (_mismatch == "ref")
                    await mutation.ExecuteUpdateAsync(s => s.SetProperty(o => o.ReviewedSourceRef, "refs/heads/wrong"));
                else
                    await mutation.ExecuteUpdateAsync(s => s.SetProperty(o => o.ReviewedSourceSha, Land.Fixture.SeedSha));
            }
        }

        public async Task<AgentTaskLandNotification> RefuseAsync(string row, bool expectTransactionCut = false)
        {
            if (expectTransactionCut)
            {
                try { await Land.RunQueuedAsync(); }
                catch (LandingSafetyHarness.InjectedSaveFailure) { }
                Land.Fault.Triggered.ShouldBeTrue(row + ": refusal transaction cut reached");
                await Land.RestartServicesAsync();
                await Land.RunAsync();
            }
            else await Land.RunQueuedAsync();
            await Land.RestartServicesAsync();
            await using var db = Land.CreateContext();
            var refusal = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e =>
                e.AgentTaskId == Land.Fixture.TaskId && e.LandRequestId == _request
                && e.Type == AgentTaskEventType.LandRefused);
            refusal.Detail.ShouldContain(_evidence.ToString("D"), Case.Sensitive, row);
            refusal.Detail.ShouldContain(_sha, Case.Sensitive, row);
            refusal.Detail.ShouldContain(_mode == "adoption" ? "-FromTask" : "-RecoverReviewedSource", Case.Sensitive, row);
            var note = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n =>
                n.TaskId == Land.Fixture.TaskId && n.SourceEventId == refusal.Id);
            note.ParentSessionId.ShouldBe(Caller.SessionId, row);
            note.Body.ShouldContain(_evidence.ToString("D"), Case.Sensitive, row);
            _note = note.Id;
            (await db.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == Land.Fixture.TaskId).ToListAsync())
                .ShouldAllBe(o => !new AgentTaskLandingState().HasPublication(o), row);
            return note;
        }

        public async Task ReconcileAsync(Guid noteId, bool recovered = false)
        {
            await using var db = Land.CreateContext();
            var clock = recovered ? new C544Clock() : null;
            clock?.Advance(TimeSpan.FromMinutes(30));
            var service = new AgentTaskLandNotificationService(db, Caller.Queue, new CompletionNoteFlushQueue(),
                Caller.Runtime, clock ?? TimeProvider.System, Boundary);
            try { await service.ReconcileAsync(noteId, CancellationToken.None); }
            catch (IOException e) when (e.Message.StartsWith("c544 boundary cut", StringComparison.Ordinal)) { }
        }

        public async Task DeliverAsync(string row)
        {
            var before = Caller.Adapter.SubmittedBodies.Count;
            await Caller.Queue.FlushIfIdleAsync(Caller.SessionId, CancellationToken.None);
            if (_busy && before == Caller.Adapter.SubmittedBodies.Count)
            {
                await BridgeQueueHarness.InsertEntryAsync(Caller.SessionId, TranscriptKinds.TurnEnd,
                    stopReason: TranscriptKinds.StopReasons.EndTurn,
                    connectionString: Land.Schema.ConnectionString);
                await Caller.Queue.OnTurnEndAsync(Caller.SessionId, CancellationToken.None);
            }
            await ReconcileAsync(_note);
        }

        public async Task AssertReceiptAsync(AgentTaskLandNotification original, string row)
        {
            await using var db = Land.CreateContext();
            var note = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == original.Id);
            var queued = await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.SourceLandNotificationId == note.Id).ToListAsync();
            queued.Count.ShouldBe(1, row + ": one keyed row");
            var prompts = await db.TranscriptEntries.AsNoTracking()
                .Where(t => t.AgentSessionId == Caller.SessionId && t.Kind == TranscriptKinds.UserPrompt && t.Text != null)
                .ToListAsync();
            var carrying = prompts.Where(p => PromptSubmissionMatch.IsConfirmedBy(queued[0].Body, p.Text)).ToList();
            carrying.Count.ShouldBe(1, row + ": one caller UserPrompt for the wire body");
            var payload = queued[0].RemoteSpillBody;
            if (payload is null && queued[0].Body.Contains("YOUR MESSAGE IS NOT IN THIS MESSAGE.", StringComparison.Ordinal))
            {
                var cwd = (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == Caller.SessionId)).Cwd;
                var path = Path.Combine(cwd!, ".antiphon", "inbox", queued[0].Id.ToString("D") + ".md");
                File.Exists(path).ShouldBeTrue(row + ": pointer target exists");
                payload = await File.ReadAllTextAsync(path);
            }
            payload ??= carrying[0].Text!;
            payload.ShouldContain(_evidence.ToString("D"), Case.Sensitive, row);
            payload.ShouldContain(_sha, Case.Sensitive, row);
            PromptSubmissionMatch.IsCompleteIn(queued[0].Body, carrying[0].Text!).ShouldBeTrue(row);
            if (queued[0].Body.Contains("YOUR MESSAGE IS NOT IN THIS MESSAGE.", StringComparison.Ordinal))
            {
                payload.ShouldBe(note.Body, row + ": spill retains the complete refusal");
                queued[0].Body.ShouldContain($".antiphon/inbox/{queued[0].Id:D}.md", Case.Sensitive, row);
            }
            else carrying[0].Text.ShouldContain(note.Body, Case.Sensitive, row);
            carrying[0].Sequence.ShouldBeGreaterThan(queued[0].LastDeliveryBaselineSequence ?? 0, row);
            note.State.ShouldBe(LandNotificationState.Confirmed, row);
            note.ConfirmingPromptSequence.ShouldBe(carrying[0].Sequence, row);
            await ReconcileAsync(note.Id);
            Caller.Adapter.SubmittedBodies.Count.ShouldBe(1, row + ": reconcile is idempotent");
        }

        public async ValueTask DisposeAsync()
        {
            await Caller.DisposeAsync();
            await Land.DisposeAsync();
        }
    }
}
