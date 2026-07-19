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

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--session": sessionId = Guid.Parse(args[++i]); break;
                case "--pipe": pipeName = args[++i]; break;
                case "--manifest-dir": manifestDir = args[++i]; break;
                case "--log": logFile = args[++i]; break;
                case "--launch-timeout-sec": launchTimeout = TimeSpan.FromSeconds(int.Parse(args[++i])); break;
                case "--linger-hours": lingerTtl = TimeSpan.FromHours(double.Parse(args[++i])); break;
                case "--ring-cap-chars": ringCap = int.Parse(args[++i]); break;
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
        };
    }
}
