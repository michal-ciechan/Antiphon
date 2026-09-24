using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0161 B2 — per-session ceiling resolution.</summary>
[Category("Unit")]
public class SessionDeliveryProfileTests
{
    [Test]
    public async Task PtyHost_snapshot_delegates_to_process_wide_pty_profile()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var session = await SeedSessionAsync(db, SessionBackend.PtyHost);
        await using var owned = Build(advertiseHerdr: true);

        var ceilings = await owned.Profile.ForSessionAsync(db, session.Id, CancellationToken.None);

        ceilings.Backend.ShouldBe(owned.Pty.Ceilings.Backend);
        ceilings.BriefInlineMaxBytes.ShouldBe(owned.Pty.Ceilings.BriefInlineMaxBytes);
    }

    [Test]
    public async Task Herdr_snapshot_plus_runner_advertising_herdr_returns_herdr_set()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var session = await SeedSessionAsync(db, SessionBackend.Herdr);
        await using var owned = Build(advertiseHerdr: true);

        var ceilings = await owned.Profile.ForSessionAsync(db, session.Id, CancellationToken.None);

        ceilings.Backend.ShouldBe(DeliveryBackend.HerdrPane);
        ceilings.SingleWriteMaxBytes.ShouldBe(86_400);
        ceilings.IsPastePath.ShouldBeTrue();
    }

    [Test]
    public async Task Herdr_snapshot_but_runner_without_herdr_downgrades_to_inbox()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var session = await SeedSessionAsync(db, SessionBackend.Herdr);
        await using var owned = Build(advertiseHerdr: false);

        var ceilings = await owned.Profile.ForSessionAsync(db, session.Id, CancellationToken.None);

        ceilings.Backend.ShouldBe(DeliveryBackend.InboxConhost);
        ceilings.SingleWriteMaxBytes.ShouldBe(1_024);
        ceilings.Reason.ShouldContain("SessionBackends", Case.Insensitive);
    }

    [Test]
    public async Task Herdr_snapshot_with_unreachable_runner_uses_conservative_inbox()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var session = await SeedSessionAsync(db, SessionBackend.Herdr);
        await using var owned = Build(advertiseHerdr: null);

        var ceilings = await owned.Profile.ForSessionAsync(db, session.Id, CancellationToken.None);

        ceilings.Backend.ShouldBe(DeliveryBackend.InboxConhost);
        ceilings.Reason.ShouldContain("no answer", Case.Insensitive);
    }

    [Test]
    public async Task Phone_home_Grok_never_uses_local_modern_evidence()
    {
        RequireLocalModernConPty();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "grok",
            AgentKind = AgentKind.Grok,
            SessionBackend = SessionBackend.PtyHost,
            Status = SessionStatus.Running,
            Cwd = "D:/tmp",
            Cols = 120,
            Rows = 30,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            RunnerId = "grok-linux",
            RunnerStoreId = Guid.NewGuid(),
            RunnerCwd = "/work",
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        await using var owned = Build(advertiseHerdr: true, backendOverride: "modern");

        var ceilings = await owned.Profile.ForSessionAsync(db, session.Id, CancellationToken.None);
        var remoteSingleWriteMaxBytes = ceilings.SingleWriteMaxBytes;
        remoteSingleWriteMaxBytes.ShouldBe(1_024);
        ceilings.Backend.ShouldBe(DeliveryBackend.InboxConhost);
        ceilings.BriefInlineMaxBytes.ShouldBe(0);
        owned.Pty.Ceilings.Backend.ShouldBe(DeliveryBackend.ModernConPty);
    }

    [Test]
    public async Task Phone_home_Claude_keeps_the_inbox_ceiling_for_its_own_kind()
    {
        RequireLocalModernConPty();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            SessionBackend = SessionBackend.PtyHost,
            Status = SessionStatus.Running,
            Cwd = "D:/tmp",
            Cols = 120,
            Rows = 30,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            RunnerId = "server2",
            RunnerStoreId = Guid.NewGuid(),
            RunnerCwd = "/work/worktrees/task-903bf8a7",
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        await using var owned = Build(advertiseHerdr: true, backendOverride: "modern");

        var ceilings = await owned.Profile.ForSessionAsync(db, session.Id, CancellationToken.None);

        ceilings.Backend.ShouldBe(DeliveryBackend.InboxConhost);
        ceilings.SingleWriteMaxBytes.ShouldBe(1_024);
        ceilings.BriefInlineMaxBytes.ShouldBe(900);
        owned.Pty.Ceilings.BriefInlineMaxBytes.ShouldBe(43_200);
    }

    [Test]
    public async Task Unknown_session_id_returns_pty_profile_answer()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        await using var owned = Build(advertiseHerdr: true);

        var ceilings = await owned.Profile.ForSessionAsync(db, Guid.NewGuid(), CancellationToken.None);

        ceilings.Backend.ShouldBe(owned.Pty.Ceilings.Backend);
    }

    /// <summary>CARD-0681: these cases prove the phone-home ceiling ignores THIS process's modern
    /// ConPTY evidence, so this process must resolve modern. ConPTY exists only on Windows; off it
    /// the local profile is inbox too and the contrast cannot be observed. On Windows a missing
    /// conpty.dll still fails the local-modern assertion rather than skipping.</summary>
    private static void RequireLocalModernConPty()
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("ConPTY only on Windows");
    }

    private static async Task<AgentSession> SeedSessionAsync(AppDbContext db, SessionBackend backend)
    {
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            SessionBackend = backend,
            Status = SessionStatus.Running,
            Cwd = "D:/tmp",
            Cols = 120,
            Rows = 30,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    /// <param name="advertiseHerdr">true = lists herdr; false = answers without herdr; null = unreachable.</param>
    private static Owned Build(bool? advertiseHerdr, string backendOverride = "inbox")
    {
        var services = new ServiceCollection();
        ISessionRunnerClient client = advertiseHerdr switch
        {
            true => new HerdrCapabilitiesClient(),
            false => new PtyOnlyCapabilitiesClient(),
            null => new NullCapabilitiesClient(),
        };
        services.AddSingleton(client);
        var provider = services.BuildServiceProvider();
        var pty = new PtyDeliveryProfile(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PtyDeliveryProfile>.Instance,
            Options.Create(new DelegationSettings()),
            TimeProvider.System,
            backendOverride: backendOverride);
        var profile = new SessionDeliveryProfile(
            pty,
            Options.Create(new DelegationSettings()),
            provider.GetRequiredService<ISessionRunnerClient>(),
            TimeProvider.System,
            NullLogger<SessionDeliveryProfile>.Instance);
        return new Owned(provider, pty, profile);
    }

    private sealed record Owned(
        ServiceProvider Provider, PtyDeliveryProfile Pty, SessionDeliveryProfile Profile) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private class NullCapabilitiesClient : ISessionRunnerClient
    {
        public virtual Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) =>
            Task.FromResult<RunnerCapabilitiesDto?>(null);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class HerdrCapabilitiesClient : NullCapabilitiesClient
    {
        public override Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) =>
            Task.FromResult<RunnerCapabilitiesDto?>(new RunnerCapabilitiesDto(
                "InboxConhost", "inbox", "test", false,
                SessionBackends: [SessionBackends.PtyHost, SessionBackends.Herdr]));
    }

    private sealed class PtyOnlyCapabilitiesClient : NullCapabilitiesClient
    {
        public override Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) =>
            Task.FromResult<RunnerCapabilitiesDto?>(new RunnerCapabilitiesDto(
                "InboxConhost", "inbox", "test", false,
                SessionBackends: [SessionBackends.PtyHost]));
    }
}
