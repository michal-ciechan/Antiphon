using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0383: a herdr Grok launch whose argv carries <c>--resume &lt;uuid&gt;</c> with no native
/// session directory is refused before the runner touches herdr. grok 1.0.13 then exits 1 after a
/// remote 404 and herdr never detects it. Defensive of server/runner <c>GROK_HOME</c> skew — the
/// runner makes the authoritative native-store probe. Title-shaped <c>--resume</c> values and
/// every <c>--session-id</c> pass through.
/// </summary>
internal static class HerdrGrokResumeGuard
{
    /// <summary>
    /// Throws <see cref="HerdrLaunchException"/> (<see cref="HerdrProblemTypes.GrokNativeSessionMissing"/>)
    /// for a Grok <c>--resume &lt;uuid&gt;</c> whose native directory is missing.
    /// </summary>
    public static void Require(
        Guid sessionId,
        RunnerLaunchRequest request,
        string expectedKind,
        ILogger logger)
    {
        var isGrok = string.Equals(expectedKind, HerdrAgentKinds.Grok, StringComparison.Ordinal)
            || string.Equals(request.TranscriptFormat, TranscriptFormats.Grok, StringComparison.OrdinalIgnoreCase);
        if (!isGrok)
            return;

        if (!TryReadResumeId(request.Args, out var resumeId))
            return;

        var grokHome = GrokTranscriptTailer.ResolveGrokHome(request.Env);
        var presence = GrokNativeSessionStore.Probe(grokHome, resumeId);
        if (presence == NativeSessionPresence.Found)
            return;
        if (presence == NativeSessionPresence.Unavailable)
            throw new IOException("Grok native session storage is unavailable; resume identity is preserved.");

        var sessions = Path.Combine(grokHome, "sessions");
        logger.LogWarning(
            "Refusing grok --resume {ResumeId} for session {SessionId}: no native directory under {SessionsRoot}",
            resumeId, sessionId, sessions);
        throw new HerdrLaunchException(
            $"Cannot resume {resumeId:D} for session {sessionId:D}: native history is missing. Inspect or select owned history; only an explicit fresh start creates another conversation.",
            HerdrProblemTypes.GrokNativeSessionMissing);
    }

    /// <summary>
    /// <c>--resume &lt;v&gt;</c>, <c>-r &lt;v&gt;</c>, or <c>--resume=&lt;v&gt;</c>, only when
    /// <c>v</c> parses as a <see cref="Guid"/>. Title resumes are not this gate. Last match wins.
    /// </summary>
    internal static bool TryReadResumeId(IReadOnlyList<string>? args, out Guid id)
    {
        id = default;
        if (args is null)
            return false;

        var found = false;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i] ?? "";
            if (string.Equals(arg, "--resume", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-r", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Count && Guid.TryParse(args[i + 1], out var parsed))
                {
                    id = parsed;
                    found = true;
                    i++;
                }

                continue;
            }

            if (arg.StartsWith("--resume=", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(arg.AsSpan("--resume=".Length), out var equalsParsed))
            {
                id = equalsParsed;
                found = true;
            }
        }

        return found;
    }
}
