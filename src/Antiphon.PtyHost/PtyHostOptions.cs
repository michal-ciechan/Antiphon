using Antiphon.PtyHost.Protocol;

namespace Antiphon.PtyHost;

public sealed record PtyHostOptions
{
    public required Guid SessionId { get; init; }
    public required string PipeName { get; init; }
    public required string ManifestDir { get; init; }
    public string? LogFile { get; init; }

    /// <summary>Self-destruct if no Launch arrives within this window (runner died mid-start).</summary>
    public TimeSpan LaunchTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long to linger serving state after child exit with no runner Shutdown ack.</summary>
    public TimeSpan LingerTtl { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Replay ring capacity in chars; attaches further back than this get a Resync.</summary>
    public int RingCapChars { get; init; } = 1_000_000;

    /// <summary>
    /// Which pseudoconsole this host's session spawns under (<c>inbox</c> / <c>modern</c>), or null
    /// to read <c>ANTIPHON_PTY_BACKEND</c> from the inherited environment as before.
    ///
    /// <para>CARD-0045: stated on the command line rather than left ambient. A host inherits its
    /// launcher's environment block, so before this the backend of a detached host was whatever the
    /// runner process — or, for a test, the test process, or the shell that started the test — had
    /// exported, invisible in the host's own command line and manifest while diagnosing it. The
    /// resolved backend is unchanged in production (the daemon still exports the same value from
    /// <c>SessionRunner:PtyBackend</c>); it is now simply said twice.</para>
    /// </summary>
    public string? PtyBackend { get; init; }

    public string ManifestPath => PtyHostManifest.PathFor(ManifestDir, SessionId);

    public static PtyHostOptions Parse(string[] args)
    {
        Guid? sessionId = null;
        string? pipeName = null;
        string? manifestDir = null;
        string? logFile = null;
        var launchTimeout = TimeSpan.FromSeconds(30);
        var lingerTtl = TimeSpan.FromHours(24);
        var ringCap = 1_000_000;
        string? ptyBackend = null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--session": sessionId = Guid.Parse(args[++i]); break;
                case "--pipe": pipeName = args[++i]; break;
                case "--manifest-dir": manifestDir = args[++i]; break;
                case "--log": logFile = args[++i]; break;
                case "--launch-timeout-sec": launchTimeout = TimeSpan.FromSeconds(int.Parse(args[++i])); break;
                case "--linger-hours":
                    lingerTtl = TimeSpan.FromHours(
                        double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "--ring-cap-chars": ringCap = int.Parse(args[++i]); break;
                case "--pty-backend": ptyBackend = args[++i]; break;
            }
        }

        if (sessionId is null)
            throw new ArgumentException("--session <guid> is required.");
        if (manifestDir is null)
            throw new ArgumentException("--manifest-dir <dir> is required.");

        return new PtyHostOptions
        {
            SessionId = sessionId.Value,
            PipeName = pipeName ?? PtyHostProtocol.PipeNameFor(sessionId.Value),
            ManifestDir = manifestDir,
            LogFile = logFile,
            LaunchTimeout = launchTimeout,
            LingerTtl = lingerTtl,
            RingCapChars = ringCap,
            PtyBackend = ptyBackend,
        };
    }
}
