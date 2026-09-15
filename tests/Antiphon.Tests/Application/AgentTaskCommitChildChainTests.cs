using System.Net;
using System.Net.Http.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class AgentTaskCommitEndpointTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Spawned_child_complete_brief_gated_operation_and_parent_receipt(bool recover)
    {
        // Endpoint fixtures intentionally leave Working rows. A dispatcher scenario owns
        // a fresh database so those independent requests cannot consume its task capacity.
        await using var isolated = new CommitEndpointWebAppFactory();
        await new CommitChildChain(isolated).RunAsync(recover);
    }

    private sealed class CommitChildChain(CommitEndpointWebAppFactory factory)
    {
        private readonly CommitEndpointWebAppFactory _factory = factory;

        public async Task RunAsync(bool recover)
        {
            using var repo = await SeedRepoAsync();
            using var client = _factory.CreateClient();
            await repo.AddBareOriginAsync();
            var upstream = await repo.GitReadAsync("rev-parse", "origin/master");
            await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "assigned work");
            var parent = Guid.NewGuid();
            var workerSession = Guid.NewGuid();
            var childSession = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.RoutingPins.Where(p => p.Reason == "C527 child receipt").ExecuteDeleteAsync();
                foreach (var session in new[] { parent, workerSession, childSession })
                {
                    db.AgentSessions.Add(new AgentSession
                    {
                        Id = session, Cwd = repo.Path, AgentKind = AgentKind.ClaudeCode,
                        DefinitionName = "test-raw", Status = SessionStatus.Running, Cols = 120, Rows = 30,
                        CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
                    });
                }
                db.AgentTasks.Add(new AgentTask
                {
                    Id = taskId, RootTaskId = taskId, ParentSessionId = parent, AgentSessionId = workerSession,
                    Title = "C527 produced worker", Goal = "make the change", RepoPath = repo.Path,
                    WorkingDirectory = repo.Path, Workspace = WorkspaceMode.Shared, Role = AgentTaskRole.Custom,
                    AgentKind = AgentKind.ClaudeCode, Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Dispatched,
                    ReplyTo = AgentTaskReplyTo.Session, CommitOnSettle = CommitOnSettlePolicy.Agent,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-1), DispatchedAt = DateTime.UtcNow.AddMinutes(-1),
                });
                db.RoutingPins.Add(new RoutingPin
                {
                    Id = Guid.NewGuid(), Role = AgentTaskRole.Commit, Provenance = RoutingPinProvenance.Human,
                    Strength = RoutingPinStrength.Required,
                    CandidatesJson = RoutingCandidate.Serialize([new(AgentKind.ClaudeCode, AgentModelLevel.Medium)]),
                    Reason = "C527 child receipt", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                });
                var agentId = Guid.NewGuid();
                db.Agents.Add(new Agent
                {
                    Id = agentId, Name = "c527-" + agentId.ToString("N")[..8], Slug = "c527-" + agentId.ToString("N")[..8],
                    WorkingDirectory = repo.Path, Kind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium,
                    Status = AgentStatus.Idle, IsPoolDelegate = true, PoolIdleSince = DateTime.UtcNow.AddMinutes(-10),
                    PersistentSessionId = childSession.ToString("D"), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            await ChainEntryAsync(parent, TranscriptKinds.UserPrompt, "Dispatch work.");
            await ChainEntryAsync(parent, TranscriptKinds.TurnEnd, null);
            await ChainEntryAsync(childSession, TranscriptKinds.UserPrompt, "Ready for work.");
            await ChainEntryAsync(childSession, TranscriptKinds.TurnEnd, null);
            var parentAdapter = ChainAdapter(parent);
            var childAdapter = ChainAdapter(childSession);
            await ChainEntryAsync(workerSession, TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(taskId));
            await ChainEntryAsync(workerSession, TranscriptKinds.AssistantText, "Assigned work complete.\n" + DelegationReportFormatter.ReportToken(taskId, "done"));
            await ChainEntryAsync(workerSession, TranscriptKinds.TurnEnd, null);
            if (recover) _factory.Boundary.InterruptedTask = taskId;
            await _factory.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(workerSession, CancellationToken.None);

            Guid childId;
            Guid parentNoteId;
            string token;
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var child = await db.AgentTasks.SingleAsync(t => t.ParentTaskId == taskId);
                (await db.AgentTasks.CountAsync(t => t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Dispatched))
                    .ShouldBe(0, "the chain must not inherit another endpoint test's active tasks");
                childId = child.Id;
                child.CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never);
                child.AgentKind.ShouldBe(AgentKind.ClaudeCode);
                child.ModelLevel.ShouldBe(AgentModelLevel.Medium);
                AgentTaskService.RawTokens.ContainsKey(childId).ShouldBeTrue();
                token = AgentTaskService.RawTokens[childId];
                var note = await db.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == taskId);
                parentNoteId = note.Id;
                if (recover) note.QueueMessageId.ShouldBeNull();
            }
            if (recover)
            {
                _factory.Boundary.Interrupted.ShouldBeTrue();
                using var scanner = new AgentTaskLandNotificationHostedService(
                    _factory.Services.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<AgentTaskLandNotificationHostedService>.Instance);
                await scanner.StartAsync(CancellationToken.None);
                try
                {
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    var queued = false;
                    while (DateTime.UtcNow < deadline)
                    {
                        await using var scope = _factory.Services.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        queued = await db.SessionQueuedMessages.AnyAsync(m => m.SourceLandNotificationId == parentNoteId);
                        if (queued) break;
                        await Task.Delay(50);
                    }
                    queued.ShouldBeTrue("the hosted scanner must recover the durable completion");
                }
                finally { await scanner.StopAsync(CancellationToken.None); }
            }
            var queue = _factory.Services.GetRequiredService<SessionMessageQueueService>();
            await queue.FlushSessionAsync(parent, CancellationToken.None);
            await ChainConfirmAsync(taskId);
            await using (var dispatch = _factory.Services.CreateAsyncScope())
                await dispatch.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
            await queue.FlushSessionAsync(childSession, CancellationToken.None);

            await using (var verifyScope = _factory.Services.CreateAsyncScope())
            {
                var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var child = await db.AgentTasks.SingleAsync(t => t.Id == childId);
                child.Status.ShouldBe(AgentTaskStatus.Dispatched);
                child.AgentSessionId.ShouldBe(childSession);
                var brief = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == childSession && m.ExecutionTaskId == childId);
                brief.ExecutionTaskId.ShouldBe(child.Id);
                var prompt = await db.TranscriptEntries.SingleAsync(p => p.AgentSessionId == childSession
                    && p.Kind == TranscriptKinds.UserPrompt && p.Sequence > brief.LastDeliveryBaselineSequence);
                prompt.Text.ShouldBe(brief.Body);
                prompt.Text.ShouldContain(child.Goal);
                prompt.Text.ShouldContain("explicitly authorized to commit");
                prompt.Text.ShouldContain("scripts/task-commit.ps1");
                prompt.Text.ShouldContain("Do NOT push");
                prompt.Text.ShouldNotContain(DelegationReportFormatter.DoNotCommitLine);
                childAdapter.SubmittedBodies.Count.ShouldBe(1);
            }
            client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
            var response = await client.PostAsJsonAsync($"/api/agent-tasks/{childId}/commit",
                new { paths = new[] { "x.md" }, message = "C527 assigned paths" });
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")).Trim().ShouldBe("x.md");
            var sha = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
            await ChainEntryAsync(childSession, TranscriptKinds.AssistantText,
                "Committed " + sha + " x.md.\n" + DelegationReportFormatter.ReportToken(childId, "done"));
            await ChainEntryAsync(childSession, TranscriptKinds.TurnEnd, null);
            await _factory.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(childSession, CancellationToken.None);
            await queue.FlushSessionAsync(parent, CancellationToken.None);
            await ChainConfirmAsync(childId);
            await ChainConfirmAsync(childId);
            await using (var verifyScope = _factory.Services.CreateAsyncScope())
            {
                var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notes = await db.AgentTaskLandNotifications.Where(n => n.TaskId == taskId || n.TaskId == childId).ToListAsync();
                notes.Count.ShouldBe(2);
                foreach (var note in notes)
                {
                    note.ConfirmedAt.ShouldNotBeNull();
                    var row = await db.SessionQueuedMessages.SingleAsync(m => m.SourceLandNotificationId == note.Id);
                    var prompt = await db.TranscriptEntries.SingleAsync(p => p.AgentSessionId == parent && p.Sequence == note.ConfirmingPromptSequence);
                    prompt.Sequence.ShouldBeGreaterThan(row.LastDeliveryBaselineSequence!.Value);
                    prompt.Text.ShouldBe(note.Body);
                }
                notes.Single(n => n.TaskId == childId).Body.ShouldContain(sha);
                (await db.AgentTasks.AnyAsync(t => t.ParentTaskId == childId)).ShouldBeFalse();
                (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == childId && e.Type == AgentTaskEventType.Warning)).ShouldBeFalse();
            }
            parentAdapter.SubmittedBodies.Count.ShouldBe(2);
            _factory.Spy.Verbs.ShouldNotContain("push");
            (await repo.GitReadAsync("rev-parse", "origin/master")).ShouldBe(upstream);
        }

        private FakeAgentProtocolAdapter ChainAdapter(Guid session)
        {
            var adapter = new FakeAgentProtocolAdapter
            {
                OnSubmitted = async body =>
                {
                    await ChainEntryAsync(session, TranscriptKinds.UserPrompt, body);
                    await ChainEntryAsync(session, TranscriptKinds.TurnEnd, null);
                },
            };
            _factory.Services.GetRequiredService<AgentSessionRuntime>().Register(session, adapter);
            return adapter;
        }

        private async Task ChainEntryAsync(Guid session, string kind, string? body)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sequence = (await db.TranscriptEntries.Where(t => t.AgentSessionId == session).MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = session, Sequence = sequence, Kind = kind, Text = body,
                Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        private async Task ChainConfirmAsync(Guid task)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var id = await db.AgentTaskLandNotifications.Where(n => n.TaskId == task).Select(n => n.Id).SingleAsync();
            await scope.ServiceProvider.GetRequiredService<AgentTaskLandNotificationService>().ReconcileAsync(id, CancellationToken.None);
        }
    }
}
