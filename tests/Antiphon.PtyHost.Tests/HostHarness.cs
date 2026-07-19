using Antiphon.PtyHost.Protocol;

namespace Antiphon.PtyHost.Tests;

/// <summary>
/// Runs a <see cref="PtyHostServer"/> in-process on a unique pipe name with temp state dirs.
/// In-process keeps the protocol/session tests fast and debuggable; the out-of-process spike
/// tests cover real process detachment separately.
/// </summary>
public sealed class HostHarness : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts;

    private HostHarness(
        PtyHostOptions options, HostSession session, Task runTask, string tempDir, CancellationTokenSource cts)
    {
        Options = options;
        Session = session;
        RunTask = runTask;
        TempDir = tempDir;
        _cts = cts;
    }

    public PtyHostOptions Options { get; }
    public HostSession Session { get; }
    public Task RunTask { get; }
    public string TempDir { get; }

    public Guid SessionId => Options.SessionId;
    public string AnsiLogPath => Path.Combine(TempDir, "session.ansi.log");
    public string ManifestPath => Options.ManifestPath;

    public static HostHarness Start(Func<PtyHostOptions, PtyHostOptions>? configure = null)
    {
        var sessionId = Guid.NewGuid();
        var tempDir = Path.Combine(Path.GetTempPath(), "antiphon-ptyhost-tests", sessionId.ToString("N"));
        Directory.CreateDirectory(tempDir);

        var options = new PtyHostOptions
        {
            SessionId = sessionId,
            PipeName = $"antiphon-pty-test-{sessionId:N}",
            ManifestDir = Path.Combine(tempDir, "manifests"),
            LogFile = Path.Combine(tempDir, "host.log"),
        };
        if (configure is not null)
            options = configure(options);

        var log = new HostLog(options.LogFile);
        var session = new HostSession(options, log);
        var server = new PtyHostServer(options, session, log);
        var cts = new CancellationTokenSource();
        var run = Task.Run(() => server.RunAsync(cts.Token));

        return new HostHarness(options, session, run, tempDir, cts);
    }

    /// <summary>
    /// Launch spec running <paramref name="batchLines"/> as a temp .cmd file. Multi-command
    /// strings must NOT be passed inline through cmd /c - `&amp;` and spaces do not survive PTY
    /// arg quoting (same reason the Agents.Pty tests use TempBatch).
    /// </summary>
    public LaunchMessage CmdLaunch(params string[] batchLines)
    {
        var batPath = Path.Combine(TempDir, $"launch-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(batPath, "@echo off\r\n" + string.Join("\r\n", batchLines) + "\r\n");
        return new LaunchMessage(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/c", batPath],
            new Dictionary<string, string>(),
            TempDir,
            120,
            30,
            MemoryLimitMb: 0,
            TranscriptEnabled: false,
            AnsiLogPath);
    }

    public LaunchMessage InteractiveCmdLaunch() =>
        new(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            [],
            new Dictionary<string, string>(),
            TempDir,
            120,
            30,
            MemoryLimitMb: 0,
            TranscriptEnabled: false,
            AnsiLogPath);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await RunTask.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // Teardown best-effort; a wedged run task will die with the test process.
        }

        await Session.DisposeAsync();
        _cts.Dispose();
        if (Environment.GetEnvironmentVariable("ANTIPHON_PTYHOST_TESTS_KEEP") == "1")
            return;
        try
        {
            Directory.Delete(TempDir, recursive: true);
        }
        catch
        {
            // Locked files (ConPTY teardown lag) - temp cleanup is best-effort.
        }
    }
}
