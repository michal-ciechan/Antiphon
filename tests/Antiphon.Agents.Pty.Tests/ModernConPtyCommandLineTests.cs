using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0101: <see cref="ModernConPtyConnection.EscapeArgument"/> round-tripped through the REAL
/// Win32 <c>CommandLineToArgvW</c> — the actual parser every Windows/.NET child uses to build its
/// own <c>argv</c>. The old rule (wrap in quotes, double inner quotes) is not that parser's grammar:
/// measured live, a single unescaped <c>"</c> in one bundled argument shredded a 9-argument command
/// line into 165 <c>argv</c> entries, truncating the system prompt and losing <c>--session-id</c>
/// (root cause: <c>docs/investigations/2026-08-20-delegate-command-line-shred.md</c>). Assert
/// against the parser itself, not against the escaped string looking plausible — that is exactly
/// how the original bug shipped unnoticed for three days.
/// </summary>
public class ModernConPtyCommandLineTests
{
    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint hMem);

    private static string[] RealArgv(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        if (argv == nint.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var result = new string[count];
            for (var i = 0; i < count; i++)
                result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * nint.Size))!;
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    private static void AssertRoundTrips(params string[] intendedArgs)
    {
        var commandLine = ModernConPtyConnection.BuildCommandLine("C:\\app.exe", intendedArgs, verbatim: false);
        var argv = RealArgv(commandLine);

        argv.Length.ShouldBe(intendedArgs.Length + 1, $"argv count for command line: {commandLine}");
        argv[0].ShouldBe("C:\\app.exe");
        for (var i = 0; i < intendedArgs.Length; i++)
            argv[i + 1].ShouldBe(intendedArgs[i], $"argv[{i + 1}] for command line: {commandLine}");
    }

    [Test]
    public void An_embedded_quote_survives_as_one_argument()
    {
        // The literal failing shape from server/Bundles/delegate-basics.md: a single unescaped
        // quote inside a multi-line bundled argument. This is the exact case that shredded a
        // 9-argument command line into 165 argv entries in production.
        AssertRoundTrips(
            "--append-system-prompt",
            "so a message\n  claiming \"tests green\" while two still fail is worse than no message at all.",
            "--session-id",
            "b42dc25b-56aa-44c0-97d0-9464cd47716f");
    }

    [Test]
    public void A_trailing_backslash_is_not_swallowed_by_the_closing_quote()
    {
        AssertRoundTrips("C:\\path with spaces\\", "next");
    }

    [Test]
    public void A_backslash_immediately_before_a_quote_is_doubled()
    {
        AssertRoundTrips("back\\\"slashquote", "next");
    }

    [Test]
    public void A_backslash_not_before_a_quote_is_literal()
    {
        AssertRoundTrips("C:\\Users\\lndco\\plain\\path", "next");
    }

    [Test]
    public void Plain_and_whitespace_arguments_round_trip()
    {
        AssertRoundTrips("plain", "has tab\tinside", "has space", "");
    }

    [Test]
    public void An_argument_with_no_special_characters_is_not_quoted_at_all()
    {
        ModernConPtyConnection.EscapeArgument("plain-arg").ShouldBe("plain-arg");
    }

    [Test]
    public void The_old_doubling_rule_would_have_shredded_the_embedded_quote_case()
    {
        // Documents the regression this fixes: the OLD escaping ("wrap in quotes, double inner
        // quotes") does NOT round-trip through the real parser for this exact shape - confirming
        // the bug this test suite guards against was real, not hypothetical.
        var oldEscape = (string a) => string.IsNullOrEmpty(a) ? string.Empty : "\"" + a.Replace("\"", "\"\"") + "\"";
        var intended = new[]
        {
            "--append-system-prompt",
            "so a message\n  claiming \"tests green\" while two still fail is worse than no message at all.",
            "--session-id",
            "b42dc25b-56aa-44c0-97d0-9464cd47716f",
        };
        var oldCommandLine = "C:\\app.exe " + string.Join(" ", intended.Select(oldEscape));
        var oldArgv = RealArgv(oldCommandLine);
        oldArgv.Length.ShouldNotBe(intended.Length + 1, "the old escaping should NOT round-trip this shape");
    }
}
