using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0101's coverage gap, closed at the seam that was one file short (test-coverage plan
/// 2026-08-20, P0-1 / blindness B4).
///
/// <para><c>DelegateBundleLaunchTests</c> already composes the REAL bundles through the REAL
/// <see cref="AgentTaskDispatcher.BuildLaunchSpec"/> — and stops at the <c>Args</c> array. Nothing
/// ever took those real arguments to a command line and back. So on 2026-08-17 the bundle that
/// shredded production (<c>server/Bundles/delegate-basics.md:18</c>, a line carrying a literal
/// <c>"</c>) was already loaded, in-process, in a passing test, on the day it broke: every delegate
/// launched for three days on 58 % of its system prompt with <c>--session-id</c> swallowed, and the
/// suite stayed green.</para>
///
/// <para>What this class adds is one step: take the composed arguments, build the command line the
/// way each backend actually builds it, and parse it back through the real Win32
/// <c>CommandLineToArgvW</c> — the parser <c>claude.exe</c>, node and bun use. Three properties are
/// asserted per case rather than one, because the production failure was not "the launch broke", it
/// was "the launch worked on a different argv":</para>
/// <list type="number">
/// <item>every argument round-trips, element for element (<see cref="LaunchArgvGuard.VerifyOrThrow"/>);</item>
/// <item>the <c>--append-system-prompt</c> value comes back <b>char-for-char and length-equal</b> —
/// the shred delivered a plausible-looking prefix, so a contains-check would have passed it;</item>
/// <item><c>--session-id</c> is present at its intended index — the argument whose loss is invisible
/// downstream, because the session still launches and only the transcript binding degrades.</item>
/// </list>
///
/// <para>The catalog property test and the hostile seed exist because the two shipped CARD-0101
/// unit suites assert on hand-written strings that COPY the failing bundle line: both would have
/// stayed green had <c>delegate-basics.md</c> grown a different hostile character instead, and
/// neither reads a bundle file at all. Bundles are edited by agents, routinely, with no awareness
/// that their punctuation reaches a command line.</para>
///
/// <para>No process, no pseudoconsole, no skip gate beyond Windows (the parser is
/// <c>shell32</c>).</para>
/// </summary>
[Category("Integration")]
public class DelegateLaunchArgvIntegrityTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// A path with a space in it, deliberately: the app half of the command line is quoted by a
    /// DIFFERENT rule than the arguments (quotes toggle, backslashes are never special), and both
    /// backends' composers make that decision on <c>app.Contains(' ')</c>. A test that only ever
    /// used a space-free exe would never exercise it.
    /// </summary>
    private const string ExeWithSpace = @"C:\Program Files\Anthropic\claude.exe";

    private static void SkipIfNotWindows()
    {
        if (!IsWindows)
            throw new SkipTestException("CommandLineToArgvW is Windows-only");
    }

    // ---- the real launch, every (kind × role × agent kind) -----------------------------------

    /// <summary>
    /// The whole matrix the dispatcher supports, with every attachable bundle riding along. This is
    /// the test that would have gone red on <c>28afb5f</c> — the commit that introduced the bug —
    /// with no hand-written hostile string anywhere in it: the hostile content is whatever is in the
    /// repo's own bundle files today.
    /// </summary>
    [Test]
    public void Every_dispatched_launch_round_trips_through_both_backends()
    {
        SkipIfNotWindows();
        var (dispatcher, provider) = CreateHarness();
        using var _ = provider;

        var checkedCases = 0;
        foreach (var kind in Enum.GetValues<AgentTaskKind>())
        foreach (var role in Enum.GetValues<AgentTaskRole>())
        foreach (var agentKind in Enum.GetValues<AgentKind>())
        {
            var sessionId = Guid.NewGuid();
            var task = TaskFor(kind, role, agentKind);
            var args = ComposeLaunchArgs(dispatcher, task, agentKind, sessionId, Attachments);

            var because = $"{kind}/{role} on {agentKind}";
            AssertRoundTripsOnBothBackends(ExeWithSpace, args, because);
            AssertSessionIdSurvives(ExeWithSpace, args, agentKind, sessionId, because);
            AssertAppendedPromptIsIntact(ExeWithSpace, args, because);
            checkedCases++;
        }

        checkedCases.ShouldBe(
            Enum.GetValues<AgentTaskKind>().Length
            * Enum.GetValues<AgentTaskRole>().Length
            * Enum.GetValues<AgentKind>().Length,
            "every combination the dispatcher supports must be covered — a new enum value must not "
            + "quietly widen the matrix without being checked");
    }

    /// <summary>
    /// The same matrix with NO attachments: the role defaults alone are what a fresh pool delegate
    /// launches with (its agent row is ephemeral, so nobody can have attached anything), and that is
    /// the shape the 2026-08-17 shred actually ran on.
    /// </summary>
    [Test]
    public void Every_dispatched_launch_round_trips_with_role_defaults_alone()
    {
        SkipIfNotWindows();
        var (dispatcher, provider) = CreateHarness();
        using var _ = provider;

        foreach (var kind in Enum.GetValues<AgentTaskKind>())
        foreach (var role in Enum.GetValues<AgentTaskRole>())
        foreach (var agentKind in Enum.GetValues<AgentKind>())
        {
            var sessionId = Guid.NewGuid();
            var task = TaskFor(kind, role, agentKind);
            var args = ComposeLaunchArgs(dispatcher, task, agentKind, sessionId, attached: null);

            var because = $"{kind}/{role} on {agentKind} (role defaults only)";
            AssertRoundTripsOnBothBackends(ExeWithSpace, args, because);
            AssertSessionIdSurvives(ExeWithSpace, args, agentKind, sessionId, because);
            AssertAppendedPromptIsIntact(ExeWithSpace, args, because);
        }
    }

    // ---- the catalog on its own ------------------------------------------------------------------

    /// <summary>
    /// Every bundle in the catalog as a single argument, whether or not any role composes it today.
    /// A bundle nobody currently launches with is still one edit away from being launched with, and
    /// this is the test that goes red on the edit rather than on the incident.
    /// </summary>
    [Test]
    public void Every_bundle_in_the_catalog_round_trips_as_a_single_argument()
    {
        SkipIfNotWindows();

        InstructionBundles.All.Count.ShouldBeGreaterThan(
            0, "the catalog is embedded from server/Bundles — an empty one means the resources did not embed");

        foreach (var (key, bundle) in InstructionBundles.All)
        {
            // Rendered, not raw: the header line is what actually rides the command line, and it is
            // composed text a future change could make hostile all by itself.
            foreach (var (label, value) in new[] { ("text", bundle.Text), ("rendered", bundle.Render()) })
            {
                string[] args = ["--append-system-prompt", value];
                var because = $"bundle '{key}' ({label}, {value.Length} chars)";
                AssertRoundTripsOnBothBackends(ExeWithSpace, args, because);
                AssertAppendedPromptIsIntact(ExeWithSpace, args, because);
            }
        }
    }

    // ---- the hostile seed ------------------------------------------------------------------------

    /// <summary>
    /// A synthetic bundle-shaped body carrying every escaping hazard at once, so this class does not
    /// only ever test today's punctuation. Each character here is in the seed for a reason:
    ///
    /// <list type="bullet">
    /// <item><c>"</c> — the literal that shredded production. Porta's rule doubled it; the CRT rule
    /// splits there.</item>
    /// <item><c>\"</c> — a backslash immediately before a quote, the one position where a backslash
    /// run is special and must be doubled.</item>
    /// <item>a TRAILING <c>\</c> — would otherwise escape the closing quote the composer adds and
    /// swallow the next argument whole.</item>
    /// <item><c>{braces}</c> — harmless to <c>CommandLineToArgvW</c>, hostile to anything that
    /// format-strings a command line on the way past.</item>
    /// <item>a lone <c>\r</c> — deliberately NOT in <c>EscapeArgument</c>'s "needs quoting" set, so
    /// this asserts the parser really does not split on it rather than assuming so.</item>
    /// <item>a 40 KB body — the composed system prompt is the largest argument any launch carries,
    /// and quoting bugs are usually found by their effect on length.</item>
    /// </list>
    /// </summary>
    [Test]
    public void The_hostile_seed_round_trips_through_both_backends()
    {
        SkipIfNotWindows();

        var seed = HostileBundleSeed();
        seed.Length.ShouldBeGreaterThan(40_000, "the seed must actually carry its 40 KB body");

        string[] args =
        [
            "--name", "task-deadbeef",
            "--model", "opus",
            "--append-system-prompt", seed,
            "--session-id", "11111111-2222-3333-4444-555555555555",
        ];

        AssertRoundTripsOnBothBackends(ExeWithSpace, args, "hostile seed");
        AssertAppendedPromptIsIntact(ExeWithSpace, args, "hostile seed");

        // Named individually so a failure says WHICH hazard broke rather than "the seed broke".
        foreach (var (label, fragment) in new[]
        {
            ("bare quote", "a \" in the middle"),
            ("escaped quote", "a \\\" sequence"),
            ("trailing backslash", @"ends with a backslash\"),
            ("braces", "{braces} and {{doubled}}"),
            ("lone CR", "before\rafter"),
            ("backslash run before quote", "three\\\\\\ then \\\\\\\""),
            ("empty argument", ""),
            ("only spaces", "   "),
            ("only a backslash", @"\"),
            ("only a quote", "\""),
        })
        {
            AssertRoundTripsOnBothBackends(
                ExeWithSpace, ["--append-system-prompt", fragment], $"hostile fragment: {label}");
        }
    }

    /// <summary>
    /// The negative control. Without it this file proves only "the current code agrees with itself":
    /// every assertion above would still pass if <see cref="LaunchArgvGuard.VerifyOrThrow"/> had
    /// been quietly reduced to a no-op. Porta's own formatter — kept in the guard as a documented
    /// replica of the WRONG algorithm — must still fail the seed, or the thing being worked around
    /// has stopped existing and this whole class means nothing.
    /// </summary>
    [Test]
    public void The_old_porta_composition_still_fails_the_hostile_seed()
    {
        SkipIfNotWindows();

        string[] args = ["--append-system-prompt", HostileBundleSeed(), "--session-id", "abc"];

        var ex = Should.Throw<PtyLaunchArgvException>(() => LaunchArgvGuard.VerifyOrThrow(
            ExeWithSpace, args, LaunchArgvGuard.FormatPortaStyle(ExeWithSpace, args), "porta replica"));

        ex.Message.ShouldContain("--session-id");
        ex.Message.ShouldContain("CARD-0101");
    }

    /// <summary>
    /// And the same control against the real bundle catalog: at least one shipped bundle must still
    /// be shredded by the old rule. If this ever goes green it does not mean the bundles are safe —
    /// it means the seam is no longer being exercised by real content, and the matrix tests above
    /// have quietly become tautologies. Assert it loudly rather than let the coverage evaporate.
    /// </summary>
    [Test]
    public void At_least_one_shipped_bundle_still_shreds_under_the_old_porta_rule()
    {
        SkipIfNotWindows();

        var shredded = InstructionBundles.All.Values
            .Where(b => !RoundTrips(
                ExeWithSpace,
                ["--append-system-prompt", b.Text],
                LaunchArgvGuard.FormatPortaStyle(
                    ExeWithSpace, ["--append-system-prompt", b.Text])))
            .Select(b => b.Key)
            .ToList();

        shredded.ShouldNotBeEmpty(
            "no bundle under server/Bundles/ contains a character the OLD (Porta) quoting rule "
            + "mangles any more, so the real-content arms of this class no longer exercise the "
            + "escaping seam. That is not automatically a problem — but the hostile seed is now the "
            + "ONLY thing keeping CARD-0101 covered, and somebody should know that before deleting it.");
    }

    // ---- assertions ------------------------------------------------------------------------------

    /// <summary>
    /// Both backends, because they compose differently and CARD-0101 was fixed on only one of them
    /// first: <c>aa1c8f1</c> corrected <see cref="ModernConPtyConnection"/> alone, leaving the inbox
    /// path on Porta's doubling rule for another three days.
    /// </summary>
    private static void AssertRoundTripsOnBothBackends(string exe, string[] args, string because)
    {
        // Modern arm — exactly ModernConPtyConnection.Spawn's own pre-flight (:145-153).
        Should.NotThrow(
            () => LaunchArgvGuard.VerifyOrThrow(
                exe, args, ModernConPtyConnection.BuildCommandLine(exe, args, verbatim: false),
                "modern ConPTY"),
            $"modern ConPTY argv round-trip failed for {because}");

        // Inbox arm — exactly PtyAgentRunner.StartAsync's own composition (:99-108): escape the
        // vector with the corrected CRT rule, then hand Porta a VERBATIM line it only space-joins.
        // Replicated rather than referenced because StartAsync spawns; the two lines must stay in
        // step, and PtyBackendContractTests is what pins the production path itself.
        var escaped = args.Select(ModernConPtyConnection.EscapeArgument).ToArray();
        Should.NotThrow(
            () => LaunchArgvGuard.VerifyOrThrow(
                exe, args, ModernConPtyConnection.BuildCommandLine(exe, escaped, verbatim: true),
                "inbox conhost (Porta.Pty)"),
            $"inbox conhost argv round-trip failed for {because}");
    }

    /// <summary>
    /// Per-ARGUMENT, not per-count: the shred delivered 9 argv entries where 3 were intended, but a
    /// subtler one delivers the right count with a truncated value. <c>--append-system-prompt</c>'s
    /// value is compared char-for-char AND length-equal on both backends.
    /// </summary>
    private static void AssertAppendedPromptIsIntact(string exe, string[] args, string because)
    {
        var flag = Array.FindIndex(args, a => a is "--append-system-prompt" or "--rules");
        if (flag < 0 || flag + 1 >= args.Length)
            return; // a Check task composes nothing at all — DelegateBundleLaunchTests pins that.

        var intended = args[flag + 1];
        foreach (var (backend, commandLine) in CommandLines(exe, args))
        {
            var actual = LaunchArgvGuard.ParseArgv(commandLine);
            // +1 throughout: argv[0] is the application itself.
            actual.Length.ShouldBe(
                args.Length + 1,
                $"{backend}: argv element count for {because}");

            var delivered = actual[flag + 2];
            delivered.Length.ShouldBe(
                intended.Length,
                $"{backend}: the system prompt reached the child at a DIFFERENT LENGTH for {because} "
                + $"(intended {intended.Length} chars, child sees {delivered.Length}). "
                + "This is the CARD-0101 shape: a plausible prefix that passes a contains-check.");
            delivered.ShouldBe(
                intended,
                $"{backend}: the system prompt must arrive char-for-char for {because}");
        }
    }

    /// <summary>
    /// The argument CARD-0101 lost first and noticed last. Checked at its INDEX, not by
    /// <c>Contains</c>: a shredded line can still contain the literal <c>--session-id</c> somewhere
    /// while the child parses it as part of the previous argument's value.
    /// </summary>
    private static void AssertSessionIdSurvives(
        string exe, string[] args, AgentKind agentKind, Guid sessionId, string because)
    {
        if (!AgentSessionService.UsesSessionIdentityArgs(agentKind))
        {
            args.ShouldNotContain(
                "--session-id",
                $"{because}: this kind does not support session identity args, so nothing should add one");
            return;
        }

        var flag = Array.IndexOf(args, "--session-id");
        flag.ShouldBeGreaterThanOrEqualTo(0, $"{because}: the launch must carry --session-id");

        foreach (var (backend, commandLine) in CommandLines(exe, args))
        {
            var actual = LaunchArgvGuard.ParseArgv(commandLine);
            actual.Length.ShouldBeGreaterThan(flag + 2, $"{backend}: argv ends before --session-id for {because}");
            actual[flag + 1].ShouldBe("--session-id", $"{backend}: --session-id moved index for {because}");
            actual[flag + 2].ShouldBe(
                sessionId.ToString("D"),
                $"{backend}: --session-id's VALUE must reach the child for {because} — without it the "
                + "transcript can never bind exactly, for the whole session's life, and nothing "
                + "downstream says so");
        }
    }

    private static (string Backend, string CommandLine)[] CommandLines(string exe, string[] args) =>
    [
        ("modern ConPTY", ModernConPtyConnection.BuildCommandLine(exe, args, verbatim: false)),
        ("inbox conhost", ModernConPtyConnection.BuildCommandLine(
            exe, [.. args.Select(ModernConPtyConnection.EscapeArgument)], verbatim: true)),
    ];

    private static bool RoundTrips(string exe, string[] args, string commandLine)
    {
        try
        {
            LaunchArgvGuard.VerifyOrThrow(exe, args, commandLine, "probe");
            return true;
        }
        catch (PtyLaunchArgvException)
        {
            return false;
        }
    }

    // ---- harness ---------------------------------------------------------------------------------

    /// <summary>Every attachable bundle at once — the widest composition an operator can ask for.</summary>
    private static IReadOnlyList<string> Attachments =>
        [.. InstructionBundles.Attachable.Select(b => b.Key)];

    /// <summary>
    /// The REAL production composition, both halves: the dispatcher composes the bundles, then
    /// <see cref="AgentSessionService.BuildSessionIdentityArgs"/> appends the session identity. A
    /// test that stopped at the first half would be verifying a command line nothing ever builds.
    /// </summary>
    private static string[] ComposeLaunchArgs(
        AgentTaskDispatcher dispatcher,
        AgentTask task,
        AgentKind agentKind,
        Guid sessionId,
        IReadOnlyList<string>? attached)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"task-{DelegationReportFormatter.Short(task.Id)}",
            Slug = $"task-{DelegationReportFormatter.Short(task.Id)}",
            WorkingDirectory = task.WorkingDirectory,
            IsPoolDelegate = true,
        };
        var session = new AgentSession
        {
            Id = sessionId,
            DefinitionName = DefinitionNameFor(agentKind),
            AgentKind = agentKind,
            Status = SessionStatus.Starting,
            Cwd = task.WorkingDirectory,
            Cols = 120,
            Rows = 30,
        };

        var args = dispatcher.BuildLaunchSpec(task, agent, session, attached).Args;
        return AgentSessionService.UsesSessionIdentityArgs(agentKind)
            ? [.. AgentSessionService.BuildSessionIdentityArgs(args, sessionId, resumeMode: null)]
            : [.. args];
    }

    /// <summary>
    /// 40 KB of bundle-shaped prose with every hazard threaded through it, assembled rather than
    /// pasted so the individual hazards stay readable and can be named in a failure.
    /// </summary>
    private static string HostileBundleSeed()
    {
        var hazards = string.Join("\n", new[]
        {
            "[bundle:hostile-seed v00000000]",
            "You are a delegate. Quote a \"literal\" and an escaped \\\" one.",
            @"A path that ends in a separator: C:\Antiphon\worktrees\",
            "Format placeholders that are not ours: {0} {braces} {{doubled}}.",
            "A lone carriage return follows:\rand the text continues on the same argument.",
            @"Backslash runs before a quote: \\\"" and \\\\\"" and a trailing \\",
            "A tab\tand a vertical tab\vand a newline all in one line.",
        });

        var filler = string.Join("\n",
            Enumerable.Range(0, 700).Select(i =>
                $"- rule {i}: run it in the foreground, never sub-delegate, and use a FORWARD slash "
                + $"in --property:OutputPath=bin-{i}/ (a trailing backslash \\ loses itself)."));

        return hazards + "\n" + filler + "\n" + hazards;
    }

    private static string DefinitionNameFor(AgentKind kind) => kind switch
    {
        AgentKind.ClaudeCode => "claude",
        AgentKind.Grok => "grok",
        AgentKind.Codex => "codex",
        AgentKind.OpenCode => "opencode",
        _ => "raw",
    };

    private static AgentTask TaskFor(AgentTaskKind kind, AgentTaskRole role, AgentKind agentKind) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        Role = role,
        AgentKind = agentKind,
        ModelLevel = role == AgentTaskRole.Check ? AgentModelLevel.Low : AgentModelLevel.High,
        Status = AgentTaskStatus.Queued,
        Goal = "prove the composed launch arguments survive both command-line composers",
        WorkingDirectory = Path.GetTempPath(),
        Workspace = WorkspaceMode.Shared,
        CreatedAt = DateTime.UtcNow,
    };

    /// <summary>
    /// Same harness shape as <c>DelegateBundleLaunchTests.CreateHarness</c> — the real dispatcher
    /// over the real embedded bundles — with a definition registered for EVERY
    /// <see cref="AgentKind"/>, because the matrix asks the dispatcher to resolve each one.
    /// </summary>
    private static (AgentTaskDispatcher Dispatcher, ServiceProvider Provider) CreateHarness()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        // The budget is production's, deliberately: the widest composition here (every attachable
        // bundle, on the orchestrator role) has to FIT, and a test that raised the ceiling to make
        // itself pass would be hiding the one thing EnsureWithinCommandLineBudget exists to catch.
        services.AddSingleton(Options.Create(new DelegationSettings()));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
            s.Definitions["grok"] = new AgentDefinition { Kind = "Grok", Exe = "grok" };
            s.Definitions["codex"] = new AgentDefinition { Kind = "Codex", Exe = "codex" };
            s.Definitions["opencode"] = new AgentDefinition { Kind = "OpenCode", Exe = "opencode" };
            s.Definitions["raw"] = new AgentDefinition { Kind = "Raw", Exe = "cmd.exe" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-argv-integrity-wt"),
        }));
        services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), provider);
    }
}
