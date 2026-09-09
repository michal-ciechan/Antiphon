using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class StandingSessionSelectionTests
{
    [Test]
    [Arguments("retry", false)] [Arguments("retry", true)]
    [Arguments("selection", false)] [Arguments("selection", true)]
    [Arguments("fresh", false)] [Arguments("fresh", true)]
    public async Task Missing_managed_credential_refuses_recovery_without_clearing_intent(string decision, bool alwaysOn)
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(true, adapter); await f.SeedAsync(held: true);
        await using (var db = f.Db())
        {
            var profile = await AddRecoveryProfile(db, AgentKind.ClaudeCode, "old");
            var revision = (await db.AgentTuiProfileRevisions.FindAsync(profile.ActiveRevisionId))!;
            revision.AuthenticationMode = AgentTuiAuthenticationMode.ManagedEnvironment;
            revision.SecretEnvironmentNamesJson = JsonSerializer.Serialize(new[] { "C466_MISSING_CREDENTIAL" });
            var agent = (await db.Agents.FindAsync(f.Agent.Id))!;
            agent.TuiProfileId = profile.Id; agent.AlwaysOn = alwaysOn;
            var state = (await db.AgentSupervisionStates.FindAsync(f.Agent.Id))!;
            state.Suspended = true; state.LivenessLatchedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var request = decision == "fresh" ? new Antiphon.Server.Application.Dtos.StartAgentRequest(Fresh: true)
            : decision == "retry" ? new(RetryContinuity: true) : new(ResumeSessionId: f.A.Id);
        (await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() => f.StartAsync(request)))
            .Code.ShouldBe("profile_not_validated");
        adapter.Started.ShouldBeFalse();
        await using var verify = f.Db();
        var preserved = (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))!;
        preserved.ContinuityHeldAt.ShouldNotBeNull(); preserved.Suspended.ShouldBeTrue(); preserved.LivenessLatchedAt.ShouldNotBeNull();
        (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(f.B.Id.ToString("D"));
        (await verify.AgentSessions.CountAsync(s => s.StandingAgentId == f.Agent.Id)).ShouldBe(2);
    }

    [Test]
    [Arguments(AgentKind.ClaudeCode, false)] [Arguments(AgentKind.ClaudeCode, true)]
    [Arguments(AgentKind.Grok, false)] [Arguments(AgentKind.Grok, true)]
    public async Task Owned_history_uses_current_composition_and_native_resume_identity(AgentKind kind, bool alwaysOn)
    {
        var old = new FakeAgentProtocolAdapter(); var fresh = new FakeAgentProtocolAdapter(); var resumed = new FakeAgentProtocolAdapter();
        await using var f = new StandingRecoveryFixture(s =>
        {
            s.AddSingleton(Options.Create(new GrokRulesSettings())); s.AddSingleton<GrokRulesRefreshService>();
        }, true, old, fresh, resumed); await f.SeedAsync();
        f.Harness.Runner.AdvertiseGrokRules = true;
        f.Harness.Runner.RulesReceipt = id =>
        {
            using var db = f.Db(); var row = db.AgentSessions.Single(s => s.Id == id);
            return row.GrokRulesGeneration is not { } generation ? null : new GrokRulesReceipt(
                Path.Combine(f.Root, "instructions", "grok", id.ToString("N"), "rules.md"), row.GrokRulesExpectedSha256!, row.GrokRulesExpectedByteCount!.Value, 1, generation);
        };
        foreach (var adapter in new[] { old, fresh, resumed })
        {
            adapter.RegisterOnStart = f.Harness.Provider.GetRequiredService<AgentSessionRuntime>();
            adapter.OnSubmitted = async body =>
            {
                await using var db = f.Db(); var id = adapter.StartedSessionId!.Value;
                var sequence = await db.TranscriptEntries.Where(e => e.AgentSessionId == id).MaxAsync(e => (long?)e.Sequence) ?? 0;
                var reply = "Synthetic provider response";
                if (body.StartsWith("[antiphon-grok-rules:", StringComparison.Ordinal))
                {
                    var messageId = Guid.ParseExact(body[21..body.IndexOf(']')], "N");
                    var receipt = GrokRulesRefreshService.Receipt((await db.AgentSessions.FindAsync(id))!)!;
                    reply = $"ANTIPHON_RULES_ACK id={messageId:N} generation={receipt.Generation:N} sha256={receipt.Sha256}";
                }
                foreach (var (entryKind, text) in new[] { (TranscriptKinds.UserPrompt, body), (TranscriptKinds.AssistantText, reply), (TranscriptKinds.TurnEnd, "") })
                    db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = id, Sequence = ++sequence,
                        Kind = entryKind, Text = text, StopReason = entryKind == TranscriptKinds.TurnEnd ? "end_turn" : null,
                        CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow });
                await db.SaveChangesAsync();
            };
        }
        await File.WriteAllTextAsync(Path.Combine(f.Root, "AGENTS.md"), "Synthetic original instructions");
        Guid profileRevision;
        await using (var db = f.Db())
        {
            var profile = await AddRecoveryProfile(db, kind, "old");
            var agent = (await db.Agents.FindAsync(f.Agent.Id))!;
            agent.Kind = kind; agent.AlwaysOn = alwaysOn; agent.TuiProfileId = profile.Id; agent.ModelId = "c466-old-model";
            agent.SystemPromptAppend = "Synthetic original standing instructions"; agent.PersistentSessionId = null;
            await db.AgentSessions.Where(s => s.StandingAgentId == agent.Id).ExecuteDeleteAsync();
            await db.SaveChangesAsync();
        }
        var a = Guid.Parse((await f.StartAsync(new(Fresh: true))).PersistentSessionId!); await f.IdleAsync();
        old.Started.ShouldBeTrue();
        string? oldStamp;
        await using (var db = f.Db())
        {
            var initial = (await db.AgentSessions.FindAsync(a))!;
            initial.InteractiveLaunchCompletedAt.ShouldNotBeNull($"status={initial.Status}; failure={initial.FailureReason}; rules={initial.GrokRulesState}; generation={initial.GrokRulesGeneration}");
            if (kind == AgentKind.Grok) GrokRulesRefreshService.Receipt(initial).ShouldNotBeNull();
            oldStamp = initial.ComposedBundleStamp;
        }
        async Task Stop()
        { await using var scope = f.Harness.Provider.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<AgentControlService>().StopAsync(f.Agent.Id, default); }
        await Stop();
        var b = Guid.Parse((await f.StartAsync(new(Fresh: true))).PersistentSessionId!); await f.IdleAsync(); await Stop();
        await File.WriteAllTextAsync(Path.Combine(f.Root, "AGENTS.md"), "Synthetic current instructions");
        await using (var db = f.Db())
        {
            var current = await AddRecoveryProfile(db, kind, "current"); profileRevision = current.ActiveRevisionId!.Value;
            var agent = (await db.Agents.FindAsync(f.Agent.Id))!;
            agent.TuiProfileId = current.Id; agent.ModelId = "c466-current-model"; agent.SessionBackend = SessionBackend.Herdr;
            agent.SystemPromptAppend = "Synthetic current standing instructions"; agent.UpdatedAt = DateTime.UtcNow;
            await AgentBundleAttachments.SetAsync(db, agent, [InstructionBundles.Orchestrator], DateTime.UtcNow, default);
            await db.SaveChangesAsync();
        }
        await f.StartAsync(new(ResumeSessionId: a)); await f.IdleAsync();
        resumed.StartedSessionId.ShouldBe(a);
        resumed.StartedArgs.Count(a => a == "--resume").ShouldBe(1);
        resumed.StartedArgs.ShouldNotContain("--session-id");
        resumed.StartedArgs.ShouldContain("--profile-current"); resumed.StartedArgs.ShouldNotContain("--profile-old");
        resumed.StartedArgs.ShouldContain("c466-current-model"); resumed.StartedArgs.ShouldNotContain("c466-old-model");
        resumed.StartedEnv["C466_PROFILE"].ShouldBe("current"); resumed.StartedEnv["ANTIPHON_ORCHESTRATOR"].ShouldBe("1");
        resumed.StartedHerdr.ShouldNotBeNull();
        await using var verify = f.Db();
        var row = (await verify.AgentSessions.FindAsync(a))!;
        row.TuiProfileRevisionId.ShouldBe(profileRevision); row.EffectiveModelId.ShouldBe("c466-current-model");
        row.ComposedBundleStamp.ShouldNotBe(oldStamp); row.SessionBackend.ShouldBe(SessionBackend.Herdr);
        row.InteractiveLaunchCompletedAt.ShouldNotBeNull();
        if (kind == AgentKind.Grok) row.GrokRulesState.ShouldBe(GrokRulesState.Ready);
        (await verify.AgentSessions.FindAsync(b))!.StandingAgentId.ShouldBe(f.Agent.Id);
        (await verify.Agents.FindAsync(f.Agent.Id))!.PersistentSessionId.ShouldBe(a.ToString("D"));
    }

    private static async Task<AgentTuiProfile> AddRecoveryProfile(AppDbContext db, AgentKind kind, string label)
    {
        var now = DateTime.UtcNow;
        var profile = new AgentTuiProfile { Id = Guid.NewGuid(), DisplayName = $"c466-{label}-{Guid.NewGuid():N}", Kind = kind,
            IsEnabled = true, Source = AgentTuiProfileSource.Operator, CreatedAt = now, UpdatedAt = now };
        db.AgentTuiProfiles.Add(profile); await db.SaveChangesAsync();
        var revision = new AgentTuiProfileRevision { Id = Guid.NewGuid(), ProfileId = profile.Id, RevisionNumber = 1,
            Executable = Path.Combine(Environment.SystemDirectory, "cmd.exe"), ArgumentsJson = JsonSerializer.Serialize(new[] { $"--profile-{label}" }),
            AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged, NonSecretEnvironmentJson = JsonSerializer.Serialize(new { C466_PROFILE = label }),
            ModelArgumentName = "--model", CreatedAt = now };
        db.AgentTuiProfileRevisions.Add(revision);
        db.AgentTuiModels.Add(new AgentTuiModel { Id = Guid.NewGuid(), ProfileId = profile.Id, Identifier = $"c466-{label}-model",
            DisplayName = $"Synthetic {label}", Source = AgentTuiModelSource.Operator, Availability = AgentTuiModelAvailability.Verified,
            CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(); profile.ActiveRevisionId = revision.Id; await db.SaveChangesAsync();
        return profile;
    }
}
