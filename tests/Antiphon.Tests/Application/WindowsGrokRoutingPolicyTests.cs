using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>Proposed CARD-1011 pins in isolated databases; durable admission, not live delivery.</summary>
[Category("Integration")]
public sealed class WindowsGrokRoutingPolicyTests
{
    [Test]
    [Arguments(AgentTaskRole.Review, RequiredPlatform.Windows)]
    [Arguments(AgentTaskRole.Review, RequiredPlatform.Linux)]
    [Arguments(AgentTaskRole.Debug, RequiredPlatform.Windows)]
    [Arguments(AgentTaskRole.Debug, RequiredPlatform.Linux)]
    public Task C1011_healthy_head_is_grok(AgentTaskRole role, RequiredPlatform platform) =>
        VerifyAsync(role, platform, held: 0);

    [Test]
    [Arguments(AgentTaskRole.Review, RequiredPlatform.Windows)]
    [Arguments(AgentTaskRole.Review, RequiredPlatform.Linux)]
    [Arguments(AgentTaskRole.Debug, RequiredPlatform.Windows)]
    [Arguments(AgentTaskRole.Debug, RequiredPlatform.Linux)]
    public Task C1011_held_head_falls_back_to_opus(AgentTaskRole role, RequiredPlatform platform) =>
        VerifyAsync(role, platform, held: 1);

    [Test]
    [Arguments(AgentTaskRole.Review, RequiredPlatform.Windows)]
    [Arguments(AgentTaskRole.Review, RequiredPlatform.Linux)]
    [Arguments(AgentTaskRole.Debug, RequiredPlatform.Windows)]
    [Arguments(AgentTaskRole.Debug, RequiredPlatform.Linux)]
    public Task C1011_exhausted_pair_blocks(AgentTaskRole role, RequiredPlatform platform) =>
        VerifyAsync(role, platform, held: 2);

    private static async Task VerifyAsync(AgentTaskRole role, RequiredPlatform platform, int held)
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        // Prefer the opposite OS: PlatformMatches must actually enforce the request.
        var opposite = platform == RequiredPlatform.Windows ? "server2" : "desktop";
        var kit = DefaultRunnerKit.Create(isolated.ConnectionString, opposite);
        var directory = new MatrixDirectory(kit.Directory.Local);
        kit.RealDirectory = directory;
        var pinId = Guid.NewGuid();
        const string reason = "CARD-1011 isolated proposed policy; never a fleet pin write";
        var pair = RoutingCandidate.Serialize(
            [new(AgentKind.Grok, AgentModelLevel.High), new(AgentKind.ClaudeCode, AgentModelLevel.High)]);
        var now = DateTime.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10)); // PostgreSQL timestamp precision.
        await using (var seed = kit.Context())
        {
            seed.RoutingPins.Add(new RoutingPin
            {
                Id = pinId, Role = role, Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required, CandidatesJson = pair,
                Reason = reason, CreatedAt = now, UpdatedAt = now,
            });
            if (held >= 1) Hold(seed, AgentKind.Grok, "grok-4.7", now);
            if (held >= 2) Hold(seed, AgentKind.ClaudeCode, "opus", now);
            await seed.SaveChangesAsync();
        }
        await using var db = kit.Context();
        var defaults = new RunnerDefaultSettingsService(db, Options.Create(kit.Settings), TimeProvider.System, new MockEventBus());
        await defaults.EnsureInitializedAsync(CancellationToken.None);
        var created = await kit.Service(db, defaults, withRouting: true).CreateAsync(
            new CreateAgentTaskRequest("C1011 route and preserve OS", Role: role,
                Workspace: WorkspaceMode.Worktree, RequiredPlatform: platform),
            kit.Caller, CancellationToken.None);
        var saved = await kit.ReadAsync(created.Id);
        saved.Task.RoutingPinId.ShouldBe(pinId);
        saved.Task.ModelLevel.ShouldBe(AgentModelLevel.High);
        saved.Task.Role.ShouldBe(role);
        saved.Task.RequiredPlatform.ShouldBe(platform);
        saved.Task.ExplicitAgentKind.ShouldBeNull();
        saved.Task.ExplicitModelLevel.ShouldBeNull();
        var expectedPlatform = platform == RequiredPlatform.Windows ? "windows" : "linux";
        saved.Task.ObservedPlatform.ShouldBe(expectedPlatform);
        var descriptor = await directory.DescribeAsync(saved.Task.RunnerId, CancellationToken.None);
        descriptor.ShouldNotBeNull();
        descriptor.Platform.ShouldBe(expectedPlatform);
        saved.Created.ShouldContain($"the human required stage-wide {role} routing pin");
        var routing = created.Routing;
        routing.ShouldNotBeNull();
        routing.Walked.ShouldBeTrue();
        routing.Role.ShouldBe(role);
        routing.Source.ShouldBe($"pin:stage {role}");
        routing.Candidates.Count.ShouldBe(2);
        routing.Candidates.Select(x => x.AgentKind).ShouldBe([AgentKind.Grok, AgentKind.ClaudeCode]);
        routing.Candidates.Select(x => x.ModelLevel).ShouldBe([AgentModelLevel.High, AgentModelLevel.High]);
        routing.Candidates.Select(x => x.Alias).ShouldBe(["grok-4.7", "opus"]);
        routing.Candidates.ShouldAllBe(x => x.Origin == RoutingCandidates.OriginPin);

        if (held == 2)
        {
            created.Status.ShouldBe(AgentTaskStatus.Blocked);
            saved.Task.Status.ShouldBe(AgentTaskStatus.Blocked);
            saved.Task.FailureReason.ShouldContain(ComplexityRoutingService.RoutingExhaustedPrefix);
            saved.Task.FailureReason.ShouldContain($"stage {role} pin (human, required)");
            saved.Created.ShouldContain("exhausted");
            routing.Candidates.ShouldAllBe(x => x.Outcome == "skipped");
            routing.Candidates.ShouldAllBe(x => x.Reason == "held (manual, no re-enable time)");
            routing.Candidates.ShouldNotContain(x => x.Outcome == "chosen");
        }
        else
        {
            var expectedKind = held == 0 ? AgentKind.Grok : AgentKind.ClaudeCode;
            var expectedAlias = held == 0 ? "grok-4.7" : "opus";
            created.Status.ShouldBe(AgentTaskStatus.Queued);
            saved.Task.Status.ShouldBe(AgentTaskStatus.Queued);
            created.AgentKind.ShouldBe(expectedKind);
            saved.Task.AgentKind.ShouldBe(expectedKind);
            var chosen = routing.Candidates.Single(x => x.Outcome == "chosen");
            chosen.AgentKind.ShouldBe(expectedKind);
            chosen.Alias.ShouldBe(expectedAlias);
            chosen.Reason.ShouldBeNull();
            saved.Created.ShouldContain($"candidate {held + 1}/2 {expectedAlias}");
            if (held == 1)
            {
                routing.Candidates[0].Outcome.ShouldBe("skipped");
                routing.Candidates[0].Reason.ShouldBe("held (manual, no re-enable time)");
                created.Warning.ShouldContain("grok-4.7");
                created.Warning.ShouldContain("held");
            }
            else
            {
                routing.Candidates[1].Outcome.ShouldBe("skipped");
                routing.Candidates[1].Reason.ShouldBe("already chose an earlier candidate");
            }
        }

        await using var verify = kit.Context();
        var unchanged = await verify.RoutingPins.AsNoTracking().SingleAsync(x => x.Id == pinId);
        unchanged.Role.ShouldBe(role);
        unchanged.CardId.ShouldBeNull();
        unchanged.Provenance.ShouldBe(RoutingPinProvenance.Human);
        unchanged.Strength.ShouldBe(RoutingPinStrength.Required);
        unchanged.CandidatesJson.ShouldBe(pair);
        unchanged.Reason.ShouldBe(reason);
        unchanged.ClearedAt.ShouldBeNull();
        unchanged.UpdatedAt.ShouldBe(now);
    }

    private static void Hold(Antiphon.Server.Infrastructure.Data.AppDbContext db, AgentKind kind, string alias, DateTime now) =>
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(), Kind = kind, ModelAlias = alias,
            Source = ModelAvailabilitySource.Manual, DisabledUntil = null,
            HitAt = now, Reason = "C1011 manual open-ended hold",
        });

    private sealed class MatrixDirectory(ISessionRunnerClient local) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => local;
        public IReadOnlyList<string> KnownRunnerIds => ["desktop", "server2"];
        public Guid? GetLiveStoreId(string? runnerId) => string.IsNullOrWhiteSpace(runnerId)
            ? null : Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct)
        {
            var id = string.IsNullOrWhiteSpace(runnerId) || RunnerRequestIntent.IsDesktopAlias(runnerId) ? "desktop" : runnerId;
            var platform = id == "desktop" ? "windows" : "linux";
            return Task.FromResult<RunnerDescriptor?>(new RunnerDescriptor(id, id, platform,
                DateTimeOffset.UtcNow, true, true, false, 4,
                new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
                    Features: [RunnerPlatformWire.Feature], Platform: platform)));
        }
        public ISessionRunnerClient Resolve(string? runnerId) => KnownRunnerIds.Contains(runnerId)
            ? Local : throw new ServiceUnavailableException("Test runner unavailable", PhoneHomeProblemTypes.Unavailable);
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            throw new InvalidOperationException("placement must not list inventory");
    }
}
