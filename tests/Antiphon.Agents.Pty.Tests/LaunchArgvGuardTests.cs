using System.Reflection;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0101 item 2: the launch-time assertion. <c>aa1c8f1</c> made the modern backend's escaping
/// correct; this proves the NEXT escaping defect cannot be silent. The bug it exists for ran for
/// three days across every delegate launch — a 42%-truncated system prompt and a swallowed
/// <c>--session-id</c> — and was visible from no surface at all, because nothing ever compared what
/// we composed against what the child would parse.
///
/// The negative cases here deliberately use the OLD (broken) escaping and Porta's still-broken one
/// as their input: a guard that only passes on correct input proves nothing.
/// </summary>
public class LaunchArgvGuardTests
{
    private const string App = @"C:\app.exe";

    /// <summary>The literal shape from <c>server/Bundles/delegate-basics.md:18</c>.</summary>
    private static readonly string[] TheFailingLaunch =
    [
        "--append-system-prompt",
        "so a message\n  claiming \"tests green\" while two still fail is worse than no message at all.",
        "--session-id",
        "b42dc25b-56aa-44c0-97d0-9464cd47716f",
    ];

    [Test]
    public void The_corrected_escaping_passes_the_guard()
    {
        var commandLine = ModernConPtyConnection.BuildCommandLine(App, TheFailingLaunch, verbatim: false);

        Should.NotThrow(() => LaunchArgvGuard.VerifyOrThrow(App, TheFailingLaunch, commandLine, "modern ConPTY"));
    }

    [Test]
    public void The_old_doubling_rule_is_caught_before_the_process_is_created()
    {
        // Exactly what BuildCommandLine produced before aa1c8f1.
        var shredded = LaunchArgvGuard.FormatPortaStyle(App, TheFailingLaunch);

        var ex = Should.Throw<PtyLaunchArgvException>(
            () => LaunchArgvGuard.VerifyOrThrow(App, TheFailingLaunch, shredded, "modern ConPTY"));

        // The message has to be actionable at 3am from a log line, so it names all three facts a
        // reader needs: how badly the argument count blew up, which argument lost what, and — the
        // one whose loss is otherwise undetectable downstream — that --session-id never arrives.
        ex.Message.ShouldContain("Intended 4 argument(s)");
        ex.Message.ShouldContain("First divergence at argv[2]");
        ex.Message.ShouldContain("--session-id b42dc25b-56aa-44c0-97d0-9464cd47716f would NOT reach the child");
    }

    [Test]
    public void A_truncated_bundle_is_caught_by_LENGTH_not_by_presence()
    {
        // The 2026-08-20 failure survived because everything downstream only ever asked "is the
        // flag there?". It was: the flag and the head of its value both arrived. 42% of the body
        // did not. A presence check passes this; the guard must not.
        var bundle = "[bundle:delegate-basics] " + new string('x', 900) + " tail-that-must-arrive";
        string[] intended = ["--append-system-prompt", bundle];
        var truncated = $"{App} --append-system-prompt \"{bundle[..500]}\"";

        var ex = Should.Throw<PtyLaunchArgvException>(
            () => LaunchArgvGuard.VerifyOrThrow(App, intended, truncated, "modern ConPTY"));

        ex.Message.ShouldContain($"intended {bundle.Length} chars");
        ex.Message.ShouldContain("child would see 500 chars");
    }

    [Test]
    public void An_argument_lost_off_the_end_is_reported_as_missing_rather_than_as_a_mismatch()
    {
        string[] intended = ["--model", "opus", "--session-id", "1a1b6b7b-0000-0000-0000-000000000000"];
        var short_ = $"{App} --model opus";

        var ex = Should.Throw<PtyLaunchArgvException>(
            () => LaunchArgvGuard.VerifyOrThrow(App, intended, short_, "modern ConPTY"));

        ex.Message.ShouldContain("the child would see 2");
        ex.Message.ShouldContain("NOTHING (the argument list ends before it)");
    }

    [Test]
    public void A_correct_launch_with_no_special_characters_passes()
    {
        string[] intended = ["--model", "opus", "--session-id", "1a1b6b7b-0000-0000-0000-000000000000"];
        var commandLine = ModernConPtyConnection.BuildCommandLine(App, intended, verbatim: false);

        Should.NotThrow(() => LaunchArgvGuard.VerifyOrThrow(App, intended, commandLine, "modern ConPTY"));
    }

    [Test]
    public void An_app_path_with_spaces_still_round_trips()
    {
        const string spacedApp = @"C:\Program Files\nodejs\claude.exe";
        string[] intended = ["--session-id", "b42dc25b-56aa-44c0-97d0-9464cd47716f"];
        var commandLine = ModernConPtyConnection.BuildCommandLine(spacedApp, intended, verbatim: false);

        Should.NotThrow(() => LaunchArgvGuard.VerifyOrThrow(spacedApp, intended, commandLine, "modern ConPTY"));
    }

    // ------------------------------------------------------------------ the inbox/Porta backend

    /// <summary>
    /// <see cref="LaunchArgvGuard.FormatPortaStyle"/> is a model of somebody else's internal code
    /// (<c>Porta.Pty.Windows.WindowsArguments.Format</c>, not public), so it is only worth anything
    /// while it still matches. Measured against the shipped assembly here rather than asserted from
    /// a code comment — a Porta bump that changes the formatter turns this red instead of quietly
    /// making the inbox guard test the wrong string.
    /// </summary>
    [Test]
    public void The_porta_formatter_replica_matches_the_real_porta_assembly()
    {
        var real = typeof(Porta.Pty.PtyProvider).Assembly.GetType("Porta.Pty.Windows.WindowsArguments");
        real.ShouldNotBeNull("Porta.Pty's argument formatter moved — the inbox guard models the wrong thing now");

        var format = real!.GetMethod(
            "Format",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(string[])],
            modifiers: null);
        format.ShouldNotBeNull("Porta.Pty.Windows.WindowsArguments.Format(string[]) is gone");

        string[][] cases =
        [
            TheFailingLaunch,
            ["plain", "has space", @"C:\trailing\"],
            ["--flag", "value"],
        ];

        foreach (var args in cases)
        {
            var expected = (string)format!.Invoke(null, [args])!;
            LaunchArgvGuard.FormatPortaStyle("noSpaceApp", args)
                .ShouldBe("noSpaceApp " + expected, $"replica diverged for [{string.Join(", ", args)}]");
        }
    }

    /// <summary>
    /// aa1c8f1 fixed the modern backend only, and Porta's formatter still shreds the CARD-0101
    /// shape. <see cref="PtyAgentRunner"/> therefore stopped letting Porta format at all: it
    /// pre-escapes with the corrected rule and passes a verbatim command line. This pins that the
    /// line it hands over round-trips, using the same composition the runner uses.
    /// </summary>
    [Test]
    public void The_inbox_backends_pre_escaped_verbatim_line_round_trips()
    {
        foreach (var args in new[]
                 {
                     TheFailingLaunch,
                     new[] { @"C:\Antiphon\worktrees\card-task-1768af90\", "--next" },
                     new[] { "--echo-args", "--append-system-prompt", "a\nb with \"quotes\" and {braces}\nc" },
                 })
        {
            var escaped = args.Select(ModernConPtyConnection.EscapeArgument).ToArray();
            var verbatimLine = ModernConPtyConnection.BuildCommandLine(App, escaped, verbatim: true);

            Should.NotThrow(
                () => LaunchArgvGuard.VerifyOrThrow(App, args, verbatimLine, "inbox conhost (Porta.Pty)"),
                $"pre-escaped verbatim line must round-trip for [{string.Join(", ", args)}]");
        }
    }

    /// <summary>
    /// Why the shred survived: <c>CommandLineToArgvW</c> — the parser claude.exe, node and bun use —
    /// splits on a doubled quote inside a quoted argument, but .NET's own parser treats it as one
    /// escaped quote. The only test that covered arguments through the inbox backend spawns
    /// fakeclaude, a .NET child, so it was blind to exactly the failure that cost three days. This
    /// records the divergence: <b>Porta's format does NOT round-trip through the strict parser</b>,
    /// which is the whole reason the launch path no longer uses it.
    /// </summary>
    [Test]
    public void Portas_format_does_not_round_trip_the_shape_that_shredded_production()
    {
        var portaLine = LaunchArgvGuard.FormatPortaStyle(App, TheFailingLaunch);

        LaunchArgvGuard.ParseArgv(portaLine).Length.ShouldNotBe(
            TheFailingLaunch.Length + 1,
            "if this ever round-trips, the pre-escape workaround in PtyAgentRunner can be deleted");

        // And the exact live shape from Launch_args_reach_the_child_process, which passed for
        // months on the inbox backend only because its child happened to be a .NET process.
        var fakeClaudeArgs = new[]
        {
            "--echo-args",
            "--append-system-prompt",
            "line one of the preamble\nline two with \"quotes\" and {braces}\nline three — final.",
        };
        LaunchArgvGuard.ParseArgv(LaunchArgvGuard.FormatPortaStyle(App, fakeClaudeArgs))
            .Length.ShouldBe(9, "measured: six fragments where one argument was intended");
    }

    [Test]
    public void ParseArgv_is_the_real_parser()
    {
        // Sanity on the primitive everything else here leans on: the classic CRT rule, where a
        // backslash is only special immediately before a quote.
        LaunchArgvGuard.ParseArgv(@"app.exe ""a b"" c\\""d").ShouldBe([@"app.exe", "a b", @"c\d"]);
    }
}
