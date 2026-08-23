using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0037 step 3: the delivery ceilings are COUPLED to the pseudoconsole, and the coupling is
/// the point of the card.
///
/// <para>CARD-0030 measured a paste path worth 86 400 bytes in one write, but only where the
/// shipped <c>conpty.dll</c> + <c>OpenConsole.exe</c> are. A machine without them falls back to the
/// inbox conhost, which strips the bracketed-paste markers and clips at one ~1 KB read chunk — so
/// raising the ceilings unconditionally would re-open the original bug on exactly the machines
/// least able to notice. These tests pin that the raise is conditional, that the conservative set
/// is what you get by default and by omission, and that no ceiling exceeds what was measured.</para>
/// </summary>
[Category("Unit")]
public class PtyDeliveryCeilingsTests
{
    private const string AnyReason = "test";

    /// <summary>Where the real gate writes its spill files when it decides to spill.</summary>
    private static readonly Lazy<string> SpillRoot = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "antiphon-card37-ceilings");
        Directory.CreateDirectory(dir);
        return dir;
    });

    private static AgentTask NewTask(string goal) => new()
    {
        Id = Guid.NewGuid(),
        Title = "ceiling probe",
        Goal = goal,
        Kind = AgentTaskKind.Worker,
        Role = AgentTaskRole.Docs,
        ModelLevel = AgentModelLevel.Medium,
        Workspace = WorkspaceMode.Shared,
        WorkingDirectory = SpillRoot.Value,
        Status = AgentTaskStatus.Dispatched,
    };

    /// <summary>
    /// The inbox arm must be byte-identical to what shipped before this card. It is the arm every
    /// machine without the redistributable runs, and the four briefs stranded on 2026-08-11 are the
    /// reason each of these numbers is what it is.
    /// </summary>
    [Test]
    public void The_inbox_backend_keeps_exactly_the_ceilings_that_shipped()
    {
        var settings = new DelegationSettings();

        var ceilings = settings.CeilingsFor(PtyBackend.InboxConhost, AnyReason);

        ceilings.BriefInlineMaxBytes.ShouldBe(900);
        ceilings.ReplyInlineMaxChars.ShouldBe(3_000);
        ceilings.SingleWriteMaxBytes.ShouldBe(1_024, "one read chunk — where typed-body loss starts");
        ceilings.IsPastePath.ShouldBeFalse();
    }

    /// <summary>
    /// Nothing may be raised past the measurement. 86 400 bytes is the largest body observed to
    /// arrive whole (2/2, single write, production path, real Claude, 2026-08-12) and there is no
    /// evidence of any kind above it; the brief ceiling keeps a further 2x margin because that
    /// envelope is ONE machine and ONE Claude version.
    /// </summary>
    [Test]
    public void No_modern_ceiling_exceeds_the_measured_envelope()
    {
        const int measuredSingleWrite = 86_400;
        var settings = new DelegationSettings();

        var ceilings = settings.CeilingsFor(PtyBackend.ModernConPty, AnyReason);

        ceilings.SingleWriteMaxBytes.ShouldBe(measuredSingleWrite,
            "the tripwire sits exactly at the edge of the evidence — it is not removed on this "
            + "backend, only moved");
        ceilings.BriefInlineMaxBytes.ShouldBeLessThanOrEqualTo(ceilings.SingleWriteMaxBytes,
            "a brief is delivered as one write, so its ceiling cannot exceed the single-write one");
        ceilings.BriefInlineMaxBytes.ShouldBe(43_200,
            "the size measured whole on BOTH the bench and the production path, and the only one "
            + "that also survived a paced delivery");

        // The report ceiling is counted in UTF-16 chars while the transport envelope is UTF-8
        // bytes, and these reports are em-dash-heavy at 3 bytes each. The char ceiling must survive
        // that expansion — BriefInlineMaxBytes shipped once comparing string.Length and mangled
        // four briefs that passed it.
        (ceilings.ReplyInlineMaxChars * 3).ShouldBeLessThanOrEqualTo(ceilings.BriefInlineMaxBytes,
            "a report at this ceiling must stay inside the byte envelope even at 3 bytes per char");
    }

    /// <summary>
    /// The raise is real and it is what re-opens the inline path: at the inbox ceiling of 900 bytes
    /// EVERY brief spills (the reporting contract alone is 838 bytes, so BuildBrief's floor is
    /// ~915), which is safe but means a delegate always pays a file read before it knows its task.
    /// Driven through the REAL gate, not a copy of its arithmetic.
    /// </summary>
    [Test]
    public void The_same_brief_spills_on_the_inbox_backend_and_is_typed_inline_on_the_modern_one()
    {
        var settings = new DelegationSettings();
        var task = NewTask(string.Join("\n", Enumerable.Range(0, 120).Select(i => $"goal line {i:D4}")));

        var onInbox = AgentTaskDispatcher.FitBriefForTyping(
            task, settings, settings.CeilingsFor(PtyBackend.InboxConhost, AnyReason));
        var onModern = AgentTaskDispatcher.FitBriefForTyping(
            task, settings, settings.CeilingsFor(PtyBackend.ModernConPty, AnyReason));

        onInbox.ShouldContain("YOUR BRIEF IS NOT IN THIS MESSAGE",
            customMessage: "on the stripping binary a brief this size must still become a pointer");
        onModern.ShouldNotContain("YOUR BRIEF IS NOT IN THIS MESSAGE",
            customMessage: "and on the paste path it must reach the delegate whole — that is the payoff");
        onModern.ShouldContain("goal line 0000");
        onModern.ShouldContain("goal line 0119");
    }

    /// <summary>
    /// The gate is never widened by omission. Every caller that predates the profile — which is
    /// every test, and any future one that forgets — gets the conservative set.
    /// </summary>
    [Test]
    public void A_caller_with_no_profile_gets_the_conservative_ceilings()
    {
        var settings = new DelegationSettings();
        var task = NewTask(string.Join("\n", Enumerable.Range(0, 120).Select(i => $"goal line {i:D4}")));

        AgentTaskDispatcher.FitBriefForTyping(task, settings)
            .ShouldContain("YOUR BRIEF IS NOT IN THIS MESSAGE");
    }

    /// <summary>
    /// The delegate is told what its report ceiling is, and it has to be the one the report will
    /// actually be measured against — a brief quoting 3 000 while the server excerpts at 14 400
    /// makes every delegate spill a report that would have been forwarded whole.
    /// </summary>
    [Test]
    public void The_brief_quotes_the_report_ceiling_that_is_actually_in_force()
    {
        var settings = new DelegationSettings();
        var task = NewTask("do the thing");

        DelegationReportFormatter.BuildBrief(task, settings)
            .ShouldContain("3,000", customMessage: "the default is the inbox number");
        DelegationReportFormatter.BuildBrief(task, settings, settings.ModernPtyReplyInlineMaxChars)
            .ShouldContain("14,400");
    }

    /// <summary>
    /// The dangerous asymmetry, and the reason the server asks instead of assuming: the runner is a
    /// separate process with its own config, and its pty-hosts inherit ITS environment. A server
    /// resolved "modern" in front of an inbox runner would type 43 KB briefs into a pty that clips
    /// at 1 KB — the original failure, restored, while the logs claim the paste path.
    /// </summary>
    [Test]
    public async Task A_runner_on_the_inbox_conhost_downgrades_a_modern_server()
    {
        RequireRedistributable();
        await using var profile = BuildProfile("modern", PtyBackend.InboxConhost);

        var ceilings = await profile.Value.RefreshAsync(CancellationToken.None);

        ceilings.Backend.ShouldBe(DeliveryBackend.InboxConhost);
        ceilings.BriefInlineMaxBytes.ShouldBe(900);
        ceilings.Reason.ShouldContain("session runner",
            customMessage: "and it must say WHY, or the next person re-raises them");
    }

    /// <summary>
    /// A runner that agrees is corroboration, and only then are the raised ceilings used.
    /// </summary>
    [Test]
    public async Task A_runner_on_the_modern_backend_confirms_the_raised_ceilings()
    {
        RequireRedistributable();
        await using var profile = BuildProfile("modern", PtyBackend.ModernConPty);

        var ceilings = await profile.Value.RefreshAsync(CancellationToken.None);

        ceilings.Backend.ShouldBe(DeliveryBackend.ModernConPty);
        ceilings.BriefInlineMaxBytes.ShouldBe(43_200);
    }

    /// <summary>
    /// "I cannot say" is not evidence — it must not downgrade a correctly configured deployment
    /// (an old runner, a restart, the in-proc adapters), and it must not raise anything either. It
    /// leaves this process's own resolution standing, which for the inbox case is the conservative
    /// set regardless of what any runner reports.
    /// </summary>
    [Test]
    public async Task A_silent_runner_leaves_this_processes_own_decision_standing()
    {
        await using var silentInbox = BuildProfile("inbox", reported: null);
        (await silentInbox.Value.RefreshAsync(CancellationToken.None))
            .Backend.ShouldBe(DeliveryBackend.InboxConhost);

        RequireRedistributable();
        await using var silentModern = BuildProfile("modern", reported: null);
        (await silentModern.Value.RefreshAsync(CancellationToken.None))
            .Backend.ShouldBe(DeliveryBackend.ModernConPty);
    }

    /// <summary>
    /// A server on the inbox conhost is never raised by a runner that has the redistributable: the
    /// server types into its own in-proc ptys too, and the ceilings are one set for the deployment.
    /// </summary>
    [Test]
    public async Task An_inbox_server_is_not_raised_by_a_modern_runner()
    {
        await using var profile = BuildProfile("inbox", PtyBackend.ModernConPty);

        (await profile.Value.RefreshAsync(CancellationToken.None))
            .Backend.ShouldBe(DeliveryBackend.InboxConhost);
    }

    private static void RequireRedistributable()
    {
        if (!ConPtyRedistributable.TryLocate(out _, out var why))
            throw new SkipTestException("no shipped conpty.dll: " + why);
    }

    /// <summary>A profile whose runner answers <paramref name="reported"/> (null = cannot say).</summary>
    private static Owned BuildProfile(string backendOverride, PtyBackend? reported)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISessionRunnerClient>(new StubRunnerClient(reported));
        var provider = services.BuildServiceProvider();

        return new Owned(provider, new PtyDeliveryProfile(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PtyDeliveryProfile>.Instance,
            Options.Create(new DelegationSettings()),
            TimeProvider.System,
            backendOverride));
    }

    private sealed record Owned(ServiceProvider Provider, PtyDeliveryProfile Value) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    /// <summary>Answers the capability probe and nothing else — the profile calls nothing else.</summary>
    private sealed class StubRunnerClient(PtyBackend? reported) : ISessionRunnerClient
    {
        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) =>
            Task.FromResult(reported is { } b
                ? new RunnerCapabilitiesDto(b.ToString(), "stub", "stub", false)
                : null);

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
}
