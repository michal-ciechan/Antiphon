using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1150 S2 repair 3. Every brief envelope a producer in this repository writes, or has
/// written into a persisted row, crossed with attempt, row state, receipt and payload. Each
/// verdict is looked up in the hand-written tables below (mirrored in the test design note's
/// corpus matrix), never computed by the classifier's own parser.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    /// <summary>How the classifier must read a form. Hand-written per form.</summary>
    private enum CorpusShape
    {
        /// <summary>A pointer whose payload must be present and intact.</summary>
        Spill,
        /// <summary>The body is its own payload, whatever it quotes or mentions.</summary>
        Inline,
        /// <summary>A pointer without the task marker; only a retained payload names the task.</summary>
        MarkerlessSpill,
    }

    private sealed record CorpusForm(string Name, CorpusShape Shape, string Producer);

    private static readonly CorpusForm[] CorpusForms =
    [
        new("full-current", CorpusShape.Spill, "BuildBriefPointer 563568e60.., marker on the headline"),
        new("full-legacy", CorpusShape.Spill, "BuildBriefPointer 8c42ebd3e..563568e60, bare headline"),
        new("full-legacy-contract", CorpusShape.Spill, "BuildBriefPointer eb24e568f, bare headline, whole reporting contract"),
        new("joined-current", CorpusShape.Spill, "BuildBriefPointer joined (ad258cd41) with the 563568e60 marker"),
        new("joined-legacy", CorpusShape.Spill, "BuildBriefPointer joined, ad258cd41..563568e60"),
        new("compact", CorpusShape.Spill, "BuildBriefPointer compact, 7fb7bc5cb.."),
        new("compact-joined", CorpusShape.Spill, "BuildBriefPointer compact joined, 7fb7bc5cb.."),
        new("readonly-current", CorpusShape.Spill, "BuildBriefPointer ReadOnly workspace line"),
        new("readonly-legacy", CorpusShape.Spill, "BuildBriefPointer ReadOnly line, bare headline"),
        new("check-current", CorpusShape.Spill, "BuildBriefPointer Check reporting section (ba72905e3..)"),
        new("header-current", CorpusShape.Spill, "BuildBriefPointer areas= with a space and verification= (76d45ed24..)"),
        new("message-current", CorpusShape.Spill, "TypedBodySpill with the opening marker, 7851254ea.."),
        new("message-joined", CorpusShape.Spill, "TypedBodySpill joined, 7851254ea.."),
        new("message-legacy", CorpusShape.MarkerlessSpill, "TypedBodySpill 00ad9463c..7851254ea, no marker"),
        new("inline-plain", CorpusShape.Inline, "BuildBrief"),
        new("inline-mentions", CorpusShape.Inline, "BuildBrief, goal names .antiphon paths"),
        new("inline-goal-quotes-own-pointer", CorpusShape.Inline, "BuildBrief, goal fences this task's pointer"),
        new("inline-goal-is-own-pointer", CorpusShape.Inline, "BuildBrief, goal opens with this task's pointer"),
        new("inline-goal-quotes-own-legacy-pointer", CorpusShape.Inline, "BuildBrief, goal fences the bare-headline pointer"),
        new("inline-retry-quotes-own-pointer", CorpusShape.Inline, "BuildBrief + BuildHandoff, result fences this task's pointer"),
        new("inline-retry-quotes-own-legacy-pointer", CorpusShape.Inline, "BuildBrief + BuildHandoff, result fences the bare-headline pointer"),
        new("inline-retry-nested-handoff", CorpusShape.Inline, "BuildBrief + BuildHandoff, result nests an earlier handoff with the raw pointer"),
        new("inline-retry-fenced-headline", CorpusShape.Inline, "BuildBrief + BuildHandoff, result fences only the headline lines"),
        new("inline-api-fallback-pointer", CorpusShape.Inline, "BuildBriefPointer API fallback, no file"),
        new("inline-other-task-pointer", CorpusShape.Inline, "BuildBrief, goal quotes another task's pointer"),
        new("lookalike-without-report-tail", CorpusShape.Inline, "pointer text followed by a paragraph no producer writes"),
        new("lookalike-other-marker-headline", CorpusShape.Inline, "pointer whose headline carries another task's marker"),
        new("lookalike-joined-other-marker-headline", CorpusShape.Inline, "joined pointer whose headline carries another task's marker"),
    ];

    /// <summary>Where the payload lives. Hand-written: readable, and retained by the queue.</summary>
    private sealed record CorpusLocation(string Name, bool Readable, bool Retained);

    private static readonly CorpusLocation[] CorpusLocations =
    [
        new("local-posix", Readable: true, Retained: false),
        new("local-windows", Readable: true, Retained: false),
        new("local-unc", Readable: true, Retained: false),
        new("remote-bound", Readable: true, Retained: true),
        // A runner path that was never bound, and a cwd-relative inbox path: the server cannot read
        // either, so the payload is unproven whatever is on the runner's disk (known residual).
        new("remote-staged", Readable: false, Retained: false),
        new("local-relative", Readable: false, Retained: false),
        new("n/a", Readable: true, Retained: false),
    ];

    private static readonly string[] CorpusPayloads = ["present", "missing", "corrupt"];

    private static readonly string[] CorpusReceipts =
        ["none", "dated-current", "undated-proven", "undated-stale", "dated-old", "dated-under-floor", "clipped"];

    /// <summary>
    /// Whether a complete prompt of the typed body is this attempt's receipt. Hand-written. A
    /// Pending row has no delivery floor; an attempted row's floor is sequence 10.
    /// </summary>
    private static readonly Dictionary<(string Receipt, bool Attempted), bool> CorpusReceiptCounts = new()
    {
        [("none", false)] = false,
        [("none", true)] = false,
        [("dated-current", false)] = true,
        [("dated-current", true)] = true,
        [("undated-proven", false)] = false,
        [("undated-proven", true)] = true,
        [("undated-stale", false)] = false,
        [("undated-stale", true)] = false,
        [("dated-old", false)] = false,
        [("dated-old", true)] = false,
        [("dated-under-floor", false)] = true,
        [("dated-under-floor", true)] = false,
        [("clipped", false)] = false,
        [("clipped", true)] = false,
    };

    /// <summary>The verdict, in table order. Hand-written; the test design note carries the same table.</summary>
    private static string CorpusExpected(CorpusForm form, CorpusLocation location, string payload, string receipt, bool attempted)
    {
        if (form.Shape == CorpusShape.MarkerlessSpill && !(location.Retained && payload != "missing"))
            return "Uncertain/hold";
        if (form.Shape != CorpusShape.MarkerlessSpill && CorpusReceiptCounts[(receipt, attempted)])
            return "Received";
        if (form.Shape != CorpusShape.Inline && !(location.Readable && payload == "present"))
            return "Unavailable/hold";
        return attempted ? "AttemptOwned" : "Reuse";
    }

    [Test]
    [Arguments("full-current", "local-posix")]
    [Arguments("full-current", "local-windows")]
    [Arguments("full-current", "local-unc")]
    [Arguments("full-current", "remote-bound")]
    [Arguments("full-current", "remote-staged")]
    [Arguments("full-legacy", "local-posix")]
    [Arguments("full-legacy", "local-windows")]
    [Arguments("full-legacy", "local-unc")]
    [Arguments("full-legacy-contract", "local-posix")]
    [Arguments("full-legacy-contract", "local-windows")]
    [Arguments("full-legacy-contract", "local-unc")]
    [Arguments("joined-current", "local-posix")]
    [Arguments("joined-current", "local-windows")]
    [Arguments("joined-current", "local-unc")]
    [Arguments("joined-current", "remote-bound")]
    [Arguments("joined-legacy", "local-posix")]
    [Arguments("joined-legacy", "local-windows")]
    [Arguments("joined-legacy", "local-unc")]
    [Arguments("compact", "remote-bound")]
    [Arguments("compact", "remote-staged")]
    [Arguments("compact-joined", "remote-bound")]
    [Arguments("compact-joined", "remote-staged")]
    [Arguments("readonly-current", "local-posix")]
    [Arguments("readonly-current", "remote-bound")]
    [Arguments("readonly-legacy", "local-windows")]
    [Arguments("check-current", "local-posix")]
    [Arguments("check-current", "remote-bound")]
    [Arguments("header-current", "local-posix")]
    [Arguments("header-current", "remote-bound")]
    [Arguments("message-current", "remote-bound")]
    [Arguments("message-current", "local-relative")]
    [Arguments("message-joined", "remote-bound")]
    [Arguments("message-legacy", "remote-bound")]
    [Arguments("message-legacy", "local-relative")]
    [Arguments("inline-plain", "n/a")]
    [Arguments("inline-mentions", "n/a")]
    [Arguments("inline-goal-quotes-own-pointer", "n/a")]
    [Arguments("inline-goal-is-own-pointer", "n/a")]
    [Arguments("inline-goal-quotes-own-legacy-pointer", "n/a")]
    [Arguments("inline-retry-quotes-own-pointer", "n/a")]
    [Arguments("inline-retry-quotes-own-legacy-pointer", "n/a")]
    [Arguments("inline-retry-nested-handoff", "n/a")]
    [Arguments("inline-retry-fenced-headline", "n/a")]
    [Arguments("inline-api-fallback-pointer", "n/a")]
    [Arguments("inline-other-task-pointer", "n/a")]
    [Arguments("lookalike-without-report-tail", "n/a")]
    [Arguments("lookalike-other-marker-headline", "n/a")]
    [Arguments("lookalike-joined-other-marker-headline", "n/a")]
    public Task C1150_Brief_envelope_corpus_matches_the_expected_matrix(string formName, string locationName)
    {
        var form = CorpusForms.Single(f => f.Name == formName);
        var location = CorpusLocations.Single(l => l.Name == locationName);
        string[] payloads = form.Shape == CorpusShape.Inline ? ["n/a"] : CorpusPayloads;
        var mismatches = new List<string>();
        var combinations = 0;
        foreach (var attempt in new[] { 1, 2 })
        foreach (var attempted in new[] { false, true })
        foreach (var payload in payloads)
        foreach (var receipt in CorpusReceipts)
        {
            combinations++;
            var world = BriefWorld.Create(attempt);
            var path = world.PathFor(location.Name);
            var body = world.Form(form.Name, path);
            var row = world.Row(body, attempted);
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            var brief = world.Payload();
            var stored = payload switch
            {
                "present" => brief,
                "corrupt" => world.Marker + "\ncorrupt: the goal was cut away",
                _ => null,
            };
            if (location.Retained)
                row = row with { RemoteSpillRelativePath = path, RemoteSpillBody = stored };
            else if (stored is not null && location.Name.StartsWith("local-", StringComparison.Ordinal))
                files[path] = stored;
            var prompts = world.Receipt(receipt, row.Body);
            var decision = DispatchBriefEvidence.Classify(
                world.Request, world.Snapshot, [row], prompts,
                p => files.TryGetValue(p, out var text) ? text : null);
            var actual = decision.Kind + (decision.Hold ? "/hold" : "");
            var expected = CorpusExpected(form, location, payload, receipt, attempted);
            if (actual != expected)
            {
                mismatches.Add($"{form.Name}/{location.Name} attempt={attempt} "
                    + $"{(attempted ? "attempted" : "pending")} payload={payload} receipt={receipt}: "
                    + $"expected {expected}, got {actual}");
            }
        }

        Console.WriteLine($"C1150-CORPUS {form.Name}/{location.Name} combinations={combinations} mismatches={mismatches.Count}");
        combinations.ShouldBe(payloads.Length * CorpusReceipts.Length * 4, form.Name);
        mismatches.ShouldBeEmpty(string.Join(Environment.NewLine, mismatches));
        return Task.CompletedTask;
    }

    /// <summary>
    /// One task attempt and the envelope bodies its producers write. Attempt 2 carries a
    /// previous report that quotes attempt 1's pointer, as BuildHandoff renders it.
    /// </summary>
    private sealed class BriefWorld
    {
        public const string Goal = "Corpus goal stays exact.";
        private readonly DelegationSettings _settings = new();

        private BriefWorld(int attempt)
        {
            TaskId = Guid.NewGuid();
            SessionId = Guid.NewGuid();
            Dispatched = Pg(DateTime.UtcNow.AddMinutes(-5));
            Marker = DelegationReportFormatter.TaskMarker(TaskId);
            Attempt = attempt;
            RowId = Guid.NewGuid();
        }

        public Guid TaskId { get; }
        public Guid SessionId { get; }
        public Guid RowId { get; }
        public DateTime Dispatched { get; }
        public string Marker { get; }
        public int Attempt { get; }
        public string Short => DelegationReportFormatter.Short(TaskId);

        public static BriefWorld Create(int attempt) => new(attempt);

        public DispatchBriefEnsureRequest Request => new(TaskId, Attempt, SessionId, Dispatched, Dispatched);

        public DispatchBriefTaskSnapshot Snapshot =>
            new(AgentTaskStatus.Dispatched, Attempt, SessionId, Dispatched, Dispatched, Goal);

        public string PathFor(string location) => location switch
        {
            "local-posix" => "/srv/antiphon/corpus/.antiphon/task-" + Short + "-brief.md",
            "local-windows" => @"C:\Antiphon\worktrees\corpus\.antiphon\task-" + Short + "-brief.md",
            "local-unc" => @"\\host\share\corpus\.antiphon\task-" + Short + "-brief.md",
            "remote-bound" => TypedBodySpill.InboxRelativePath(RowId.ToString("D")),
            "remote-staged" => ".antiphon/task-" + Short + "-brief.md",
            "local-relative" => TypedBodySpill.InboxRelativePath(RowId.ToString("D")),
            _ => "/srv/antiphon/corpus/.antiphon/task-" + Short + "-brief.md",
        };

        public AgentTask NewTask(string? goal = null, string? result = null, Action<AgentTask>? shape = null)
        {
            var task = new AgentTask
            {
                Id = TaskId,
                Title = "Corpus pointer",
                Goal = goal ?? Goal,
                Role = AgentTaskRole.Custom,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                Attempt = Attempt,
                Result = Attempt > 1 ? result ?? "Attempt 1 ended without a report." : null,
            };
            shape?.Invoke(task);
            return task;
        }

        public string Payload() => DelegationReportFormatter.BuildBrief(NewTask(), _settings);

        public string Pointer(string? path, AgentKind kind = AgentKind.ClaudeCode, Action<AgentTask>? shape = null,
            int? maxWireBytes = null) =>
            DelegationReportFormatter.BuildBriefPointer(NewTask(shape: shape), _settings, path, 5000, kind, maxWireBytes);

        /// <summary>563568e60's only change to the full and joined forms: the headline lost its marker.</summary>
        public string Legacy(string pointer) =>
            pointer.Replace(Marker + " YOUR BRIEF IS NOT IN THIS MESSAGE.", "YOUR BRIEF IS NOT IN THIS MESSAGE.", StringComparison.Ordinal);

        /// <summary>The eb24e568f text: bare headline, then the whole reporting contract and no closing marker.</summary>
        public string LegacyWithContract(string path) =>
            $"{Marker} role=Custom tier=Frontier workspace=Shared\n\nCorpus pointer\n\n"
            + "YOUR BRIEF IS NOT IN THIS MESSAGE. It is 5,000 characters — too long to type\n"
            + "into a terminal without the transport dropping part of it, so it was written out\n"
            + "instead. Read it in full before you do anything else:\n\n    " + path + "\n\n"
            + "Everything you need is there. Do not start from this summary.\n\n"
            + "--- how to report back ---\n"
            + "Your final message is the entire report the caller receives. Nothing else from this\n"
            + "session is forwarded, and the caller cannot see your screen.\n\n"
            + "If your report would run past 3,000 characters, write the full detail to\n"
            + ".antiphon/task-" + Short + ".md and make your final message a summary that points\nat that path.\n";

        public string Message(string path, AgentKind kind = AgentKind.ClaudeCode) =>
            TypedBodySpill.Fit(new TypedBodySpill.Request(
                Payload(), 64, null, RelativeSpillPath: path, AgentKind: kind, ApiFallback: path)).ToType;

        /// <summary>00ad9463c..7851254ea: no opening marker on the headline and no closing marker.</summary>
        public string LegacyMessage(string path)
        {
            var current = Message(path).ReplaceLineEndings("\n");
            current.ShouldStartWith(Marker + " " + TypedBodySpill.PointerHeadline);
            current.ShouldEndWith("\n" + Marker);
            return current[(Marker.Length + 1)..^(Marker.Length + 1)];
        }

        public string Inline(string goal, string? result = null) =>
            DelegationReportFormatter.BuildBrief(NewTask(goal, result), _settings);

        public string Form(string name, string path)
        {
            var own = Pointer("/srv/antiphon/corpus/.antiphon/task-" + Short + "-brief.md");
            var fenced = "I was given this pointer and could not read it:\n```text\n" + own + "\n```";
            return name switch
            {
                "full-current" => Pointer(path),
                "full-legacy" => Legacy(Pointer(path)),
                "full-legacy-contract" => LegacyWithContract(path),
                "joined-current" => Pointer(path, AgentKind.Codex),
                "joined-legacy" => Legacy(Pointer(path, AgentKind.Codex)),
                "compact" => Pointer(path, maxWireBytes: 400),
                "compact-joined" => Pointer(path, AgentKind.Codex, maxWireBytes: 400),
                "readonly-current" => Pointer(path, shape: t => t.Workspace = WorkspaceMode.ReadOnly),
                "readonly-legacy" => Legacy(Pointer(path, shape: t => t.Workspace = WorkspaceMode.ReadOnly)),
                "check-current" => Pointer(path, shape: t => t.Role = AgentTaskRole.Check),
                "header-current" => Pointer(path, shape: t =>
                {
                    t.Scope = "server/Application, tests/Antiphon.Tests";
                    t.VerificationProfileVersion = 1;
                    t.VerificationRound = VerificationRound.Final;
                }),
                "message-current" => Message(path),
                "message-joined" => Message(path, AgentKind.Codex),
                "message-legacy" => LegacyMessage(path),
                "inline-plain" => Inline(Goal),
                "inline-mentions" => Inline(Goal + "\nNotes: .antiphon/inbox/notes.md and " + own.Split('\n')[0]
                    + " /srv/antiphon/corpus/.antiphon/task-" + Short + "-brief.md"),
                "inline-goal-quotes-own-pointer" => Inline("Analyze this pointer:\n```text\n" + own + "\n```\n" + Goal),
                "inline-goal-is-own-pointer" => Inline(own + "\n\n" + Goal),
                "inline-goal-quotes-own-legacy-pointer" =>
                    Inline("Analyze this pointer:\n```text\n" + Legacy(own) + "\n```\n" + Goal),
                "inline-retry-quotes-own-pointer" => Inline(Goal, fenced),
                "inline-retry-quotes-own-legacy-pointer" => Inline(Goal,
                    "I was given this pointer and could not read it:\n```text\n" + Legacy(own) + "\n```"),
                "inline-retry-nested-handoff" => Inline(Goal,
                    "--- previous attempt ---\nAttempt 1 ran at frontier and did not settle this. Do not start cold — this is\n"
                    + "what it reported:\n\n" + own + "\n\n> " + fenced.Replace("\n", "\n> ", StringComparison.Ordinal)),
                "inline-retry-fenced-headline" => Inline(Goal,
                    "```\n" + own[own.IndexOf(Marker + " YOUR BRIEF", 1, StringComparison.Ordinal)..own.IndexOf("--- how to report back ---", StringComparison.Ordinal)].Trim()
                    + "\n```"),
                "inline-api-fallback-pointer" => Pointer(null),
                "inline-other-task-pointer" => Inline(DelegationReportFormatter.BuildBriefPointer(new AgentTask
                {
                    Id = Guid.NewGuid(),
                    Title = "Another task",
                    Goal = "Another goal",
                    Role = AgentTaskRole.Custom,
                    ModelLevel = AgentModelLevel.Frontier,
                    Workspace = WorkspaceMode.Shared,
                }, _settings, ".antiphon/task-0badc0de-brief.md", 5000) + "\n\n" + Goal),
                "lookalike-without-report-tail" => own[..own.IndexOf("--- how to report back ---", StringComparison.Ordinal)]
                    + "A paragraph no producer writes.\n\n" + Marker,
                "lookalike-other-marker-headline" => own.Replace(
                    Marker + " YOUR BRIEF", DelegationReportFormatter.TaskMarker(Guid.NewGuid()) + " YOUR BRIEF",
                    StringComparison.Ordinal),
                "lookalike-joined-other-marker-headline" => Pointer(
                    "/srv/antiphon/corpus/.antiphon/task-" + Short + "-brief.md", AgentKind.Codex).Replace(
                    Marker + " YOUR BRIEF", DelegationReportFormatter.TaskMarker(Guid.NewGuid()) + " YOUR BRIEF",
                    StringComparison.Ordinal),
                _ => throw new ArgumentOutOfRangeException(nameof(name)),
            };
        }

        public DispatchBriefRowEvidence Row(string body, bool attempted)
        {
            var row = new DispatchBriefRowEvidence(
                RowId, SessionId, QueuedMessageOrigin.Delegation, QueuedMessageStatus.Pending,
                Dispatched, TaskId, null, null, null, null, body, null, null, null,
                0, null, null, null, null, null);
            return attempted
                ? row with
                {
                    Status = QueuedMessageStatus.Sent,
                    SentAt = Dispatched,
                    DeliveryAttempts = 1,
                    DeliveryVerdict = DeliveryVerdict.Delivered,
                    LastDeliveryStartedAt = Dispatched,
                    LastDeliveryBaselineSequence = 10,
                }
                : row;
        }

        public List<DispatchBriefPromptEvidence> Receipt(string receipt, string typed)
        {
            DispatchBriefPromptEvidence Prompt(string text, DateTime? at, long sequence) =>
                new(SessionId, TranscriptKinds.UserPrompt, text, at, sequence);
            var current = Dispatched.AddMinutes(1);
            return receipt switch
            {
                "none" => [],
                "dated-current" => [Prompt(typed, current, 11)],
                "undated-proven" => [Prompt(typed, null, 11)],
                "undated-stale" => [Prompt(typed, null, 3)],
                "dated-old" => [Prompt(typed, Dispatched.AddDays(-1), 3)],
                "dated-under-floor" => [Prompt(typed, current, 9)],
                "clipped" => [Prompt(typed[..(typed.Length / 2)], current, 11)],
                _ => throw new ArgumentOutOfRangeException(nameof(receipt)),
            };
        }
    }
}
