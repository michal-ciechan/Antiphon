using System.Runtime.InteropServices;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>
/// The regression test the three brief-size mitigations never had (CARD-0028 item 4): drive the
/// REAL gate — <see cref="AgentTaskDispatcher.FitBriefForTyping"/> — through the REAL delivery path
/// (<see cref="SessionMessageQueueService"/> → <see cref="AgentSessionRuntime"/> →
/// <see cref="DirectSessionRunnerClient"/> → a real ConPTY) into a peer that CLIPS like the real
/// Claude TUI, and assert what actually reaches the child.
///
/// <para><b>Why this could not be written before.</b> fakeclaude received every byte, so the same
/// path with an oversized brief looked identical to one with a pointer: both "arrived". The
/// ceilings were therefore only ever asserted as arithmetic — a number compared to another number
/// in a unit test — while the property they exist for (the delegate can read its instructions) was
/// checked by nobody. With <c>ANTIPHON_FAKE_STDIN_CLIP=1</c> the fake keeps one ~1024-byte read
/// chunk per turn, so an oversized brief loses its head here exactly as it did live on 2026-08-11,
/// and the pointer path is what makes the difference between a delegate that knows its task and one
/// that does not.</para>
///
/// <para>Needs Windows ConPTY, the staged <c>fakeclaude.exe</c>, and the test Postgres — same as the
/// other queue integration tests.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("Headed")]
public class DelegationBriefCeilingPtyTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeClaudeExe =>
        Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    private const string Head = "HEAD-MARKER";
    private const string Tail = "TAIL-MARKER";

    private const int MarkerLineBytes = 16; // "M0000 xxxxxxxxx\n"

    /// <summary>
    /// A task whose goal is a marker at each end and <paramref name="goalBytes"/> of indexed
    /// 16-byte lines between them, so a delivery that lost a chunk names the exact span rather than
    /// being merely short. ASCII on purpose: ConPTY narrows non-ASCII input to one byte per char
    /// before a .NET peer reads it, so a multibyte goal would be a different size down here than it
    /// is on the wire.
    /// </summary>
    private static AgentTask NewTask(string workingDirectory, int goalBytes)
    {
        var lines = Math.Max(0, (goalBytes - Head.Length - Tail.Length - 2) / MarkerLineBytes);
        var filler = string.Concat(
            Enumerable.Range(0, lines).Select(i => $"M{i:D4} {new string('x', 9)}\n"));
        return new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = "CARD-0028 ceiling probe",
            Goal = $"{Head}\n{filler}{Tail}",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            Status = AgentTaskStatus.Dispatched,
        };
    }

    /// <summary>
    /// A brief SO large that it must spill must still reach the delegate intact — as a pointer.
    /// This is the shape that stranded four tasks on 2026-08-11: they ran to completion and sat
    /// Dispatched forever because the head that carried the task marker never arrived.
    /// </summary>
    [Test]
    public async Task An_oversized_brief_reaches_the_child_whole_through_the_pointer_path()
    {
        SkipIfUnavailable();
        await using var h = await Harness.StartAsync();

        var task = NewTask(h.Cwd, goalBytes: 2_000);
        var settings = new DelegationSettings();
        var typed = AgentTaskDispatcher.FitBriefForTyping(task, settings);

        Encoding.UTF8.GetByteCount(DelegationReportFormatter.BuildBrief(task, settings))
            .ShouldBeGreaterThan(settings.BriefInlineMaxBytes, "the brief must be over the ceiling");
        typed.ShouldContain("YOUR BRIEF IS NOT IN THIS MESSAGE",
            customMessage: "so the gate must have spilled it");

        await h.DeliverAsync(typed);

        // The whole pointer, not most of it: the marker at each end AND the path in the middle.
        var arrived = await h.WaitForSubmittedAsync(
            s => s.Contains(DelegationReportFormatter.TaskMarker(task.Id))
                 && s.Contains("YOUR BRIEF IS NOT IN THIS MESSAGE")
                 && s.Contains($"task-{DelegationReportFormatter.Short(task.Id)}-brief.md"));
        arrived.ShouldBeTrue(
            "the pointer must reach the child whole through a CLIPPING receiver — it is the one "
            + $"thing standing between a delegate and a headless brief. Raw:\n{await h.RawAsync()}");

        // And the text it points at must actually be there, whole.
        var spill = Path.Combine(h.Cwd, ".antiphon", $"task-{DelegationReportFormatter.Short(task.Id)}-brief.md");
        File.Exists(spill).ShouldBeTrue($"the spill file must exist at {spill}");
        var spilled = await File.ReadAllTextAsync(spill);
        spilled.ShouldContain(Head, customMessage: "the spilled brief keeps its head…");
        spilled.ShouldContain(Tail, customMessage: "…and its tail");
    }

    /// <summary>
    /// The negative control, and the whole reason the clipping fake exists: the SAME brief typed
    /// inline — what the code did before the ceiling — reaches the child with whole 1024-byte
    /// chunks of itself silently gone. Without this, the test above proves only that a short string
    /// fits down a pipe.
    ///
    /// <para>WHICH span vanishes is not asserted, because it is not stable: ConPTY hands the write
    /// to the child as several reads with real gaps between them, so how many of the fake's turns
    /// the body falls across — and therefore which chunks survive — varies run to run, exactly as
    /// it did against real Claude (whole, clipped, clipped on three identical trials; runs here
    /// have lost anywhere from a third of the goal to all 248 lines of it). What is INVARIANT is
    /// that the model drops whole chunks or nothing, so "any marker missing" is exactly "a chunk
    /// was dropped". The geometry is pinned where the turn window is under test control —
    /// <c>FakeClaudeContractTests</c> and <c>StdinClipModelTests</c>.</para>
    /// </summary>
    [Test]
    public async Task The_same_brief_typed_inline_silently_loses_a_whole_chunk()
    {
        SkipIfUnavailable();
        await using var h = await Harness.StartAsync();

        var task = NewTask(h.Cwd, goalBytes: 4_000);
        var raw = DelegationReportFormatter.BuildBrief(task, new DelegationSettings());
        var sent = MarkersIn(raw);
        sent.Count.ShouldBeGreaterThan(200, "the goal must carry enough markers to localise the loss");

        await h.DeliverAsync(raw);

        var submitted = await h.WaitForSubmittedAsync(_ => true);
        submitted.ShouldBeTrue($"something must submit. Raw:\n{await h.RawAsync()}");

        var arrived = await h.RawAsync();
        var got = MarkersIn(arrived);
        var missing = sent.Except(got).OrderBy(i => i).ToList();

        // The model drops whole 1024-byte chunks or nothing at all, so "anything missing" IS "a
        // chunk was dropped" — while HOW MANY depends on how the write fell across turns, which is
        // ConPTY's cadence and not ours to fix in an assertion.
        missing.Count.ShouldBeGreaterThan(0,
            "an oversized brief typed inline must silently lose whole 1 024-byte chunks. All "
            + $"{sent.Count} goal markers arrived — the clip model has stopped working and every "
            + "ceiling test above it is hollow");
        Console.WriteLine(
            $"[CARD-0028] inline brief: {missing.Count}/{sent.Count} goal lines lost "
            + $"(first M{missing[0]:D4}), {got.Count} survived");

        // …and none of it was reported. DeliverAsync verifies a delivery by looking for the body's
        // head OR tail on the composer, so a body missing everything in between still certifies as
        // Delivered: EnqueueAsync above returned normally instead of throwing ConflictException.
        // That silence IS the failure mode — a delegate left holding formatting rules and no task,
        // with the delivery logged as fine.
    }

    /// <summary>Goal-line markers present in a text (CR/LF stripped: ConPTY soft-wraps long lines).</summary>
    private static HashSet<int> MarkersIn(string text) =>
        System.Text.RegularExpressions.Regex
            .Matches(text.Replace("\r", "").Replace("\n", ""), @"M(\d{4}) ")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToHashSet();

    /// <summary>
    /// The other side of the gate: a brief the gate lets through inline must arrive WHOLE through
    /// the same clipping receiver — that is what "under the ceiling" has to mean.
    ///
    /// <para>The ceiling used here is 1 024 bytes — the measured read quantum — not the shipped
    /// default of 900, because at 900 this branch is unreachable: the reporting contract alone is
    /// 838 bytes, so <see cref="DelegationReportFormatter.BuildBrief"/> cannot produce a brief under
    /// 900 bytes for ANY goal (the floor is ~915). Every real brief spills today. That is a safe
    /// state, not a broken one, but it means only the pointer path is exercised in production — so
    /// this test pins the inline path at the physical boundary instead, and the assertion below
    /// pins the fact itself, so that shrinking the contract quietly reopens a path nobody is
    /// watching.</para>
    /// </summary>
    [Test]
    public async Task A_brief_inside_the_ceiling_arrives_whole()
    {
        SkipIfUnavailable();
        await using var h = await Harness.StartAsync();

        var task = NewTask(h.Cwd, goalBytes: 40);
        var settings = new DelegationSettings { BriefInlineMaxBytes = 1_024 };

        AgentTaskDispatcher.FitBriefForTyping(task, new DelegationSettings())
            .ShouldContain("YOUR BRIEF IS NOT IN THIS MESSAGE", customMessage:
                "at the shipped 900-byte ceiling even a 40-byte goal spills — the reporting contract "
                + "alone is 838 bytes. If this ever fails, the inline path has reopened and needs "
                + "the coverage this test gives it");

        var typed = AgentTaskDispatcher.FitBriefForTyping(task, settings);
        Encoding.UTF8.GetByteCount(typed).ShouldBeLessThanOrEqualTo(1_024);
        typed.ShouldContain(Head, customMessage: "this one is the brief itself, not a pointer");

        await h.DeliverAsync(typed);

        var arrived = await h.WaitForSubmittedAsync(
            s => s.Contains(Head) && s.Contains(Tail)
                 && s.Contains(DelegationReportFormatter.TaskMarker(task.Id)));
        arrived.ShouldBeTrue(
            "a brief inside one read chunk must arrive whole — head, tail and marker. Raw:\n"
            + await h.RawAsync());
    }

    private static void SkipIfUnavailable()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");
    }

    /// <summary>
    /// A live session on the production delivery path, with the fake set to CLIP. Same wiring as
    /// <see cref="SessionMessageQueuePtyIntegrationTests"/>; the only difference is the env that
    /// arms the clipping.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private DirectSessionRunnerClient _client = null!;
        private ServiceProvider _provider = null!;
        private Guid _sessionId;

        public string Cwd { get; private set; } = null!;

        public static async Task<Harness> StartAsync()
        {
            var h = new Harness
            {
                _sessionId = Guid.NewGuid(),
            };
            h.Cwd = Path.Combine(Path.GetTempPath(), $"antiphon-card28-{h._sessionId:N}");
            Directory.CreateDirectory(h.Cwd);
            h._client = new DirectSessionRunnerClient(
                Path.Combine(Path.GetTempPath(), $"antiphon-card28-log-{h._sessionId:N}"));

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                }));
            var eventBus = new MockEventBus();
            services.AddSingleton(eventBus);
            services.AddSingleton<IEventBus>(eventBus);
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings()));
            services.AddSingleton<ISessionRunnerClient>(h._client);
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddLogging();
            h._provider = services.BuildServiceProvider();

            var spec = new AgentLaunchSpec(
                DefinitionName: "fakeclaude",
                Kind: AgentKind.ClaudeCode,
                Exe: FakeClaudeExe,
                Args: Array.Empty<string>(),
                // The deterministic worst case: every chunk of a turn but one is discarded.
                Env: new Dictionary<string, string> { ["ANTIPHON_FAKE_STDIN_CLIP"] = "1" },
                Cwd: h.Cwd,
                Cols: 120,
                Rows: 30);

            await h._client.StartAsync(h._sessionId, spec, CancellationToken.None);
            var ready = await h.WaitRawAsync(
                s => s.Contains("Fake Claude ready") && s.Contains("CLIP:mode="), TimeSpan.FromSeconds(15));
            ready.ShouldBeTrue("the fake must reach readiness with clipping armed");

            await using (var scope = h._provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;
                db.AgentSessions.Add(new AgentSession
                {
                    Id = h._sessionId, CardId = null, DefinitionName = "fakeclaude",
                    AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running,
                    Cwd = h.Cwd, Cols = 120, Rows = 30, CreatedAt = now, StartedAt = now, LastSeenAt = now,
                });
                await db.SaveChangesAsync();
            }

            return h;
        }

        /// <summary>Queue the body the way a dispatch does — the queue owns the encoding and the CR.</summary>
        public Task DeliverAsync(string body) =>
            _provider.GetRequiredService<SessionMessageQueueService>()
                .EnqueueAsync(_sessionId, body, MessageSendMode.Now, CancellationToken.None,
                    QueuedMessageOrigin.Delegation);

        /// <summary>
        /// Waits for a SUBMITTED marker satisfying <paramref name="predicate"/>. ConPTY soft-wraps
        /// the long marker line, so CR/LF are stripped before matching.
        /// </summary>
        public Task<bool> WaitForSubmittedAsync(Func<string, bool> predicate) =>
            WaitRawAsync(
                raw =>
                {
                    var flat = raw.Replace("\r", "").Replace("\n", "");
                    return flat.Contains("SUBMITTED:") && predicate(flat);
                },
                TimeSpan.FromSeconds(15));

        public async Task<string> RawAsync() =>
            (await _client.GetSnapshotAsync(_sessionId, CancellationToken.None)).RawOutput ?? string.Empty;

        private async Task<bool> WaitRawAsync(Func<string, bool> predicate, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (predicate(await RawAsync())) return true;
                await Task.Delay(150);
            }
            return false;
        }

        public async ValueTask DisposeAsync()
        {
            try { await _client.KillAsync(_sessionId, CancellationToken.None); } catch { /* best effort */ }
            await _client.DisposeAsync();
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                await db.SessionQueuedMessages.Where(m => m.AgentSessionId == _sessionId).ExecuteDeleteAsync();
                await db.AgentSessions.Where(s => s.Id == _sessionId).ExecuteDeleteAsync();
            }
            await _provider.DisposeAsync();
            try { Directory.Delete(Cwd, recursive: true); } catch { /* best effort */ }
        }
    }
}
