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
/// The one-shot re-pricing of tasks costed before CARD-0023.
///
/// This drives a GLOBAL sweep — it rescans every AgentTask row in the shared database, including
/// ones other suites left behind — so it must run with nothing else in flight. Hence
/// <c>[NotInParallel]</c> with NO group key (a key would only serialise it against itself). Every
/// assertion is still scoped to the rows this test created.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class DelegationCostBackfillTests
{
    [Test]
    public async Task a_legacy_row_is_repriced_from_its_transcript_and_stamped()
    {
        var dispatched = DateTime.UtcNow.AddMinutes(-30);
        var completed = dispatched.AddMinutes(5);
        var sessionId = await SeedSessionAsync();

        // A cache-heavy session: the shape that the old model overstated by ~10x.
        await SeedApiCallAsync(sessionId, dispatched.AddMinutes(1), input: 400, read: 2_000_000, write: 30_000, output: 5_000);
        // Someone else's work in the same session, after this task settled — must not be charged here.
        await SeedApiCallAsync(sessionId, completed.AddMinutes(10), input: 900_000, read: 9_000_000, write: 200_000, output: 60_000);

        var task = await SeedLegacyTaskAsync(sessionId, dispatched, completed, legacyCost: 18.42m);

        await RunBackfillAsync();

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);

        settled.TokensIn.ShouldBe(400, "only this task's own dispatch-to-settle window counts");
        settled.CacheReadTokens.ShouldBe(2_000_000);
        settled.CacheCreationTokens.ShouldBe(30_000);
        settled.TokensOut.ShouldBe(5_000);
        settled.CostPricingVersion.ShouldBe(DelegationCost.PricingVersion);

        var expected = DelegationCost.Estimate(
            new DelegationPricingSettings(),
            AgentModelLevel.Medium,
            new TokenSpend(400, 2_000_000, 30_000, 5_000),
            completed);
        settled.CostUsd.ShouldBe(expected);
        settled.CostUsd.ShouldBeLessThan(18.42m, "the whole point is that the legacy figure was far too high");
    }

    [Test]
    public async Task a_row_whose_transcript_is_gone_keeps_its_figure_and_its_legacy_label()
    {
        // Nothing to recompute from: overwriting a real (if wrong) number with zero would tell the
        // per-root ceiling the run was free. The row stays put and stays labelled.
        var dispatched = DateTime.UtcNow.AddMinutes(-30);
        var sessionId = await SeedSessionAsync();
        var task = await SeedLegacyTaskAsync(
            sessionId, dispatched, dispatched.AddMinutes(2), legacyCost: 7.11m, tokensIn: 3_000_000);

        await RunBackfillAsync();

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.CostUsd.ShouldBe(7.11m);
        settled.CostPricingVersion.ShouldBe(0, "still a legacy estimate — the UI says so");
    }

    private static async Task RunBackfillAsync()
    {
        var service = new DelegationCostBackfillService(
            new BackfillScopeFactory(),
            Options.Create(new DelegationSettings()),
            NullLogger<DelegationCostBackfillService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;
    }

    private static async Task<AgentTask> SeedLegacyTaskAsync(
        Guid sessionId, DateTime dispatched, DateTime completed, decimal legacyCost, long tokensIn = 0)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "Legacy-priced delegate",
            Goal = "Do the thing.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = dispatched,
            DispatchedAt = dispatched,
            CompletedAt = completed,
            // The pre-CARD-0023 shape: one collapsed input figure, an inflated cost, version 0.
            TokensIn = tokensIn,
            TokensOut = 0,
            CostUsd = legacyCost,
            CostPricingVersion = 0,
        };

        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<Guid> SeedSessionAsync()
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
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    /// <summary>Three entries of ONE API call, all repeating its usage — the real JSONL shape.</summary>
    private static async Task SeedApiCallAsync(
        Guid sessionId, DateTime at, int input, int read, int write, int output)
    {
        await using var db = CreateContext();
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;

        var apiCallId = $"msg_{Guid.NewGuid():N}";
        for (var i = 0; i < 3; i++)
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = ++seq,
                Kind = TranscriptKinds.AssistantText,
                Text = "work",
                Timestamp = at,
                ApiCallId = apiCallId,
                InputTokens = input,
                CacheReadTokens = read,
                CacheCreationTokens = write,
                OutputTokens = output,
                CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>The backfill resolves exactly one thing per scope: a DbContext.</summary>
    private sealed class BackfillScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly ServiceProvider _provider;

        public BackfillScopeFactory()
        {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            _provider = services.BuildServiceProvider();
        }

        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type serviceType) => _provider.GetService(serviceType);
        public void Dispose() => _provider.Dispose();
    }
}
