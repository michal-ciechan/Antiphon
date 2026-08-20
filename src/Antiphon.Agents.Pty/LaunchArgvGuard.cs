using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Antiphon.Agents.Pty;

/// <summary>
/// A launch was refused because the command line would not have produced the intended
/// <c>argv</c> in the child. Thrown BEFORE the process exists, so the caller gets a clean failure
/// instead of a running session on a mangled instruction set.
/// </summary>
public sealed class PtyLaunchArgvException(string message) : InvalidOperationException(message);

/// <summary>
/// CARD-0101's second half: prove, at launch time, that the <c>argv</c> the child will actually
/// receive is the <c>argv</c> we meant to send.
///
/// <para>For three days every delegate launch ran on 58% of its system prompt with
/// <c>--session-id</c> swallowed, and <b>no surface anywhere showed it</b> — not the runner log,
/// not the session row, not an incident. The escaping fix in <c>aa1c8f1</c> makes that particular
/// shape correct; this makes the NEXT one loud. The check is not "does it contain
/// <c>--session-id</c>" — it is a full round-trip of the finished command line through the real
/// Win32 <c>CommandLineToArgvW</c>, the same parser the child's own CRT uses to build <c>argv</c>,
/// compared element for element against what we intended. That subsumes both of the card's
/// requirements (<c>--session-id</c> present, the bundle length-checked rather than
/// presence-checked) and catches every other argument as well.</para>
///
/// <para>Cost is one <c>shell32</c> call per session launch, on a path that is already about to
/// create a pseudoconsole and a process.</para>
/// </summary>
public static class LaunchArgvGuard
{
    /// <summary>How much of a diverging argument to quote back in the failure message.</summary>
    private const int ExcerptChars = 60;

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint hMem);

    /// <summary>
    /// The child's own view of the command line: <c>CommandLineToArgvW</c>, not a re-implementation
    /// of it. Re-implementing the parser is how the original bug shipped — the escaped string
    /// looked plausible to a reader and was not what the parser does.
    /// </summary>
    public static string[] ParseArgv(string commandLine)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("CommandLineToArgvW is Windows-only.");

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

    /// <summary>
    /// Throws <see cref="PtyLaunchArgvException"/> unless <paramref name="commandLine"/> parses back
    /// to exactly <paramref name="app"/> followed by <paramref name="intendedArgs"/>. Call this after
    /// the command line is finished and BEFORE anything is created — a launch that fails here must
    /// leave no process, no pseudoconsole and no job behind.
    /// </summary>
    /// <param name="backend">Named in the failure message; the two backends escape differently.</param>
    public static void VerifyOrThrow(string app, string[]? intendedArgs, string commandLine, string backend)
    {
        var intended = intendedArgs ?? [];
        string[] actual;
        try
        {
            actual = ParseArgv(commandLine);
        }
        catch (Exception ex) when (ex is Win32Exception or PlatformNotSupportedException)
        {
            // The guard is a safety net, never a new way to fail a healthy launch: if the parser
            // itself is unavailable we cannot verify, and an unverified launch is what shipped
            // before this card. Let it through rather than inventing a failure.
            return;
        }

        // argv[0] is parsed by a DIFFERENT rule than the rest (quotes toggle, backslashes are never
        // special), which is why it is compared but never diagnosed as a "value" divergence.
        var appMatches = actual.Length > 0 && string.Equals(actual[0], app, StringComparison.Ordinal);
        var argsMatch = actual.Length == intended.Length + 1;
        if (argsMatch)
        {
            for (var i = 0; i < intended.Length; i++)
            {
                if (!string.Equals(actual[i + 1], intended[i], StringComparison.Ordinal))
                {
                    argsMatch = false;
                    break;
                }
            }
        }

        if (appMatches && argsMatch)
            return;

        throw new PtyLaunchArgvException(Describe(app, intended, actual, commandLine, backend));
    }

    /// <summary>
    /// The inbox-conhost backend formats its command line inside <c>Porta.Pty</c>
    /// (<c>WindowsArguments.Format</c>, an internal type), so the finished string cannot be
    /// inspected from here the way <see cref="VerifyOrThrow"/> inspects the modern backend's. What
    /// CAN be done is reproduce that formatter — <b>measured against the shipped 1.0.7 assembly</b>,
    /// and pinned by <c>LaunchArgvGuardTests</c> so a package bump that changes it goes red — and
    /// round-trip the result through the same real parser.
    ///
    /// <para>This matters because <c>aa1c8f1</c> fixed only the modern backend.
    /// <c>WindowsArguments.Format</c> still wraps every argument in quotes and doubles inner quotes,
    /// so on the inbox backend the CARD-0101 shape still shreds — and a trailing backslash still
    /// escapes the closing quote. Those launches were silently broken before; now they refuse.
    /// Refusing is the correct outcome: a delegate running on a truncated system prompt with no
    /// <c>--session-id</c> is not a degraded session, it is an unreadable one that costs agent-hours
    /// before anybody notices.</para>
    /// </summary>
    public static void VerifyInboxBackendOrThrow(string app, string[]? intendedArgs)
    {
        var intended = intendedArgs ?? [];
        if (intended.Length == 0)
            return;

        VerifyOrThrow(app, intended, FormatPortaStyle(app, intended), "inbox conhost (Porta.Pty)");
    }

    /// <summary>
    /// Replica of <c>Porta.Pty.Windows.WindowsArguments.Format</c> plus Porta's app-quoting rule,
    /// as measured from Porta.Pty 1.0.7: wrap every argument in <c>"</c>, double every inner
    /// <c>"</c>. Deliberately kept as the WRONG algorithm — it is a model of what the other backend
    /// does, not a thing to fix. Fixing it means changing how Porta is called, not changing this.
    /// </summary>
    internal static string FormatPortaStyle(string app, string[] args)
    {
        var arguments = string.Join(" ", args.Select(a =>
            string.IsNullOrEmpty(a) ? string.Empty : "\"" + a.Replace("\"", "\"\"") + "\""));

        var quoteApp = app.Contains(' ') && !app.StartsWith('"') && !app.EndsWith('"');
        var builder = new StringBuilder(app.Length + arguments.Length + 4);
        if (quoteApp) builder.Append('"').Append(app).Append('"');
        else builder.Append(app);
        if (arguments.Length > 0) builder.Append(' ').Append(arguments);
        return builder.ToString();
    }

    /// <summary>
    /// The whole point of the card: say exactly what the child would have received, in terms a
    /// human reading a log at 3am can act on — argument counts, the first argument that diverges
    /// with both lengths, and an explicit line for <c>--session-id</c>, whose loss is what made the
    /// original failure survive three days (an unbound transcript looks like a transcript-layer bug).
    /// </summary>
    private static string Describe(
        string app, string[] intended, string[] actual, string commandLine, string backend)
    {
        var sb = new StringBuilder();
        sb.Append("CARD-0101: refusing to launch — the command line does not round-trip through ")
          .Append("CommandLineToArgvW, so the child would receive a different argv than intended. ")
          .Append("Backend: ").Append(backend).Append(". ")
          .Append("Intended ").Append(intended.Length).Append(" argument(s) after the app; the child ")
          .Append("would see ").Append(Math.Max(0, actual.Length - 1)).Append(". ");

        if (actual.Length == 0 || !string.Equals(actual[0], app, StringComparison.Ordinal))
        {
            sb.Append("The application path itself diverged: intended '").Append(app)
              .Append("', child would see '")
              .Append(actual.Length > 0 ? actual[0] : "<nothing>").Append("'. ");
        }

        for (var i = 0; i < intended.Length; i++)
        {
            var got = i + 1 < actual.Length ? actual[i + 1] : null;
            if (got is not null && string.Equals(got, intended[i], StringComparison.Ordinal))
                continue;

            sb.Append("First divergence at argv[").Append(i + 1).Append("]: intended ")
              .Append(intended[i].Length).Append(" chars ").Append(Excerpt(intended[i]))
              .Append(", child would see ")
              .Append(got is null ? "NOTHING (the argument list ends before it)" :
                  $"{got.Length} chars {Excerpt(got)}")
              .Append(". ");
            break;
        }

        // Called out by name because this is the argument whose loss is invisible downstream: the
        // session still launches, still works, and only the transcript binding quietly degrades.
        var sessionIdFlag = Array.IndexOf(intended, "--session-id");
        if (sessionIdFlag >= 0)
        {
            var wanted = sessionIdFlag + 1 < intended.Length ? intended[sessionIdFlag + 1] : null;
            var delivered = wanted is not null && Array.IndexOf(actual, wanted) > 0;
            sb.Append("--session-id ").Append(wanted ?? "<no value>").Append(delivered
                ? " DOES reach the child, but other arguments did not."
                : " would NOT reach the child at all (exact transcript binding would be impossible "
                  + "for the whole session's life). ");
        }

        sb.Append("Command line was ").Append(commandLine.Length).Append(" chars.");
        return sb.ToString();
    }

    private static string Excerpt(string value)
    {
        var head = value.Length <= ExcerptChars ? value : value[..ExcerptChars] + "…";
        return "\"" + head.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }
}
