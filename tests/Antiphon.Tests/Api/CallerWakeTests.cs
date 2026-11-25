using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CallerWakeTests
{
    [Test]
    public async Task Adopted_caller_wakes_for_an_unread_report_and_a_withheld_shortfall()
    {
        await using var rig = await C544DeliveryRig.CreateAsync(busy: false);
        rig.Caller.DialogAdopted = true;
        var (taskId, _) = await rig.SettleReviewAsync();
        await rig.FlushAsync();
        var row = await rig.RowAsync(taskId);
        row.LastWithholdReason.ShouldBe(DialogDeliveryAdmission.AdoptedSession);
        row.DeliveryAttempts.ShouldBe(0);

        var agentId = Guid.NewGuid();
        var (token, hash) = AgentTaskService.NewToken();
        var now = rig.World.Clock.GetUtcNow().UtcDateTime;
        await using (var db = rig.World.CreateContext())
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == rig.World.CallerSessionId);
            session.DelegationTokenHash = hash;
            session.StandingAgentId = agentId;
            var reminder = new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = session.Id, Sequence = row.Sequence + 1,
                Origin = QueuedMessageOrigin.System, NoteHeader = KeepSixReminderText.NoteHeader,
                CreatedAt = now.AddDays(-12), Body = "Old shortfall",
            };
            db.AddRange(new Agent
            {
                Id = agentId, Name = "Wake caller", Slug = "wake-caller", BoardId = rig.World.Board.Id,
                PersistentSessionId = session.Id.ToString(), WorkingDirectory = session.Cwd,
                CreatedAt = now, UpdatedAt = now,
            }, new AgentBundleAttachment { AgentId = agentId, BundleKey = InstructionBundles.Orchestrator, CreatedAt = now },
                new KeepSixOrchestrator
                {
                    AgentId = agentId, Enabled = true, Target = 6, EnabledAt = now.AddDays(-12),
                    LastReminderEnqueuedAt = reminder.CreatedAt, OutstandingMessageId = reminder.Id, UpdatedAt = now,
                }, reminder, new Card
                {
                    Id = Guid.NewGuid(), Identifier = "WAKE-1", Title = "Available work",
                    BoardId = rig.World.Board.Id, BoardColumnId = rig.World.Card.BoardColumnId,
                    Status = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now,
                });
            await db.SaveChangesAsync();
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddScoped(_ => rig.World.CreateContext());
        builder.Services.AddSingleton<TimeProvider>(rig.World.Clock);
        builder.Services.AddScoped<DispatchConcurrencySettingsService>();
        builder.Services.AddScoped<KeepSixShortfall>();
        builder.Services.AddScoped<CallerWakeService>();
        builder.Services.AddScoped<KeepSixService>();
        builder.Services.AddMemoryCache();
        await using var app = builder.Build();
        app.MapKeepSixEndpoints();
        await app.StartAsync();

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var path = $"/api/orchestrators/{agentId}/caller-wake";
        using (var anonymous = await http.PostAsync(path, null))
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        http.DefaultRequestHeaders.Add(AgentTaskEndpoints.TokenHeader, token);
        using (var other = await http.PostAsync($"/api/orchestrators/{Guid.NewGuid()}/caller-wake", null))
            other.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        async Task<JsonElement> PollAsync()
        {
            using var response = await http.PostAsync(path, null);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, "the independent caller wake route");
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        var early = await PollAsync();
        early.GetProperty("reportTaskIds").GetArrayLength().ShouldBe(0);
        early.GetProperty("shortfall").GetInt32().ShouldBe(0);

        rig.World.Clock.Advance(TimeSpan.FromMinutes(11));
        async Task<bool> HasAttentionAsync()
        {
            await using var db = rig.World.CreateContext();
            var items = await AttentionService.BuildUnreadReportItemsAsync(db, rig.World.Clock.GetUtcNow().UtcDateTime, default);
            return items.Any(i => i.SessionId == rig.World.CallerSessionId);
        }
        (await HasAttentionAsync()).ShouldBeTrue();
        var checkout = C1514Support.RepoRoot();
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(checkout, ".claude", "settings.json")));
        var stop = settings.RootElement.GetProperty("hooks").GetProperty("Stop")[0].GetProperty("hooks")[0];
        stop.GetProperty("asyncRewake").GetBoolean().ShouldBeTrue();
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false,
        };
        var script = stop.GetProperty("args")[0].GetString()!.Replace("${CLAUDE_PROJECT_DIR}", checkout);
        start.ArgumentList.Add(script);
        start.Environment["ANTIPHON_ORCHESTRATOR"] = "1";
        start.Environment.Remove("ANTIPHON_TASK_ID");
        start.Environment["ANTIPHON_HOOK_STATE_DIR"] = rig.World.CallerDirectory;
        start.Environment["ANTIPHON_AGENT_ID"] = agentId.ToString();
        start.Environment["ANTIPHON_TASK_TOKEN"] = token;
        start.Environment["ANTIPHON_API"] = http.BaseAddress!.ToString();
        using var hook = Process.Start(start)!;
        try
        {
            await hook.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { hook_event_name = "Stop", session_id = Guid.NewGuid() }));
            hook.StandardInput.Close();
            await hook.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            hook.ExitCode.ShouldBe(2, "Claude's native idle wake");
            var nudge = await hook.StandardError.ReadToEndAsync();
            nudge.ShouldContain(taskId.ToString());
            nudge.ShouldContain("shortfall");
            (await hook.StandardOutput.ReadToEndAsync()).ShouldBeEmpty();
        }
        finally
        {
            if (!hook.HasExited) hook.Kill(entireProcessTree: true);
            await hook.WaitForExitAsync();
        }

        var duplicate = await PollAsync();
        duplicate.GetProperty("reportTaskIds").GetArrayLength().ShouldBe(0);
        duplicate.GetProperty("shortfall").GetInt32().ShouldBe(0);
        rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
        (await PollAsync()).GetProperty("reportTaskIds")[0].GetGuid().ShouldBe(taskId);
        await rig.World.Services.GetRequiredService<AgentTaskService>().GetAsync(taskId, default, rig.World.CallerSessionId);
        rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
        var afterRead = await PollAsync();
        afterRead.GetProperty("reportTaskIds").GetArrayLength().ShouldBe(0);
        (await HasAttentionAsync()).ShouldBeFalse();
        afterRead.GetProperty("shortfall").GetInt32().ShouldBeGreaterThan(0);
        rig.Caller.SubmittedBodies.ShouldBeEmpty();
        (await rig.RowAsync(taskId)).Status.ShouldBe(QueuedMessageStatus.Pending);
        (await rig.NotificationAsync(taskId))!.ConfirmingPromptSequence.ShouldBeNull();
    }
}
