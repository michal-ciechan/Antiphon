using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 64 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the restart-linux-server command (CARD-1745) in
/// process. The deleted driver scripts/test-restart-linux-server.ps1 ran scripts/restart-linux-server.ps1 under pwsh against a
/// loopback HTTP stub and a #!/bin/sh fake docker, so it ran on Linux only. This world answers the same with no socket and no
/// child: the config and the lock live on a mock disk, the HTTP seam answers /health and /api/version from Answers (an absent
/// path is no response, status 0) and records every URL, and a recording launcher answers docker inspect from Image, Ref and
/// TagId (as the driver's fake did: inspect --format {{.Image}} prints Image, {{.Config.Image}} prints Ref, image inspect prints
/// TagId, anything else exits 99). Nothing starts.
/// </summary>
internal sealed class LinuxServerRestartWorld
{
    public const string Sha64 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string Sha40 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    public const string Other = "cccccccccccccccccccccccccccccccccccccccc";
    public const string BaseUrl = "http://127.0.0.1:17999/";
    public static readonly string Base = OperatingSystem.IsWindows() ? @"C:\card1745" : "/card1745";

    public LinuxServerRestartWorld()
    {
        Files.AddDirectory(Base);
    }

    public MockFileSystem Files { get; } = new();
    public string ConfigPath => Path.Combine(Base, "server.cfg");
    public string Lock => Path.Combine(Base, "restart.lock");
    public string DockerPath { get; } = Path.Combine(Base, "bin", "docker");

    /// <summary>The host's environment (ANTIPHON_LINUX_SERVER_CONFIG and ANTIPHON_LINUX_SERVER_DOCKER are unset unless a case sets them).</summary>
    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

    /// <summary>The HTTP answers by URL path (/health, /api/version); an absent path is no response.</summary>
    public Dictionary<string, LinuxServerHttpAnswer> Answers { get; } = new(StringComparer.Ordinal);

    /// <summary>docker inspect's answers: each a (exit, stdout lines) pair; null runs as exit 99 with no output.</summary>
    public (int Exit, string[] Lines)? Image { get; set; } = (0, ["sha256:c1745previous"]);
    public (int Exit, string[] Lines)? Ref { get; set; } = (0, ["antiphon-server:current"]);
    public (int Exit, string[] Lines)? TagId { get; set; } = (0, ["sha256:c1745previous"]);

    /// <summary>The names docker resolves to on PATH; a removed name is not found.</summary>
    public Dictionary<string, string> Tools { get; } = new(StringComparer.Ordinal);

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 26, 3, 0, 0, TimeSpan.Zero));
    public RecordingLauncher Launcher { get; private set; } = new();

    /// <summary>The driver's Write-C1745Config: baseUrl, container and lockPath lines.</summary>
    public string Config(string baseUrl = BaseUrl, string container = "antiphon-staging") =>
        ConfigLines($"baseUrl={baseUrl}", $"container={container}", $"lockPath={Lock}");

    /// <summary>A config of exactly these lines (LF).</summary>
    public string ConfigLines(params string[] lines)
    {
        Files.AddFile(ConfigPath, new MockFileData(lines.Length == 0 ? "" : string.Join("\n", lines) + "\n"));
        return ConfigPath;
    }

    /// <summary>Healthy, with this version body.</summary>
    public void Serve(string versionBody, int healthCode = 200, int versionCode = 200)
    {
        Answers["/health"] = new LinuxServerHttpAnswer(healthCode, "Healthy");
        Answers["/api/version"] = new LinuxServerHttpAnswer(versionCode, versionBody);
    }

    /// <summary>The driver's version body: {"version":"&lt;sha&gt;","capabilities":["land-v2"]}.</summary>
    public static string Version(string sha) => "{\"version\":\"" + sha + "\",\"capabilities\":[\"land-v2\"]}";

    /// <summary>A lock last written this many minutes before the clock (a file holding reason=old, or a directory with an entry).</summary>
    public void OldLock(double minutes, bool directory = false)
    {
        var when = Clock.GetUtcNow().UtcDateTime.AddMinutes(-minutes);
        if (directory)
        {
            Files.AddFile(Path.Combine(Lock, "inner.txt"), new MockFileData("x"));
            Files.Directory.SetLastWriteTimeUtc(Lock, when);
        }
        else
        {
            Files.AddFile(Lock, new MockFileData("reason=old\n"));
            Files.File.SetLastWriteTimeUtc(Lock, when);
        }
    }

    /// <summary>The lock's text with its utc line as written by the clock, or null when there is none.</summary>
    public string? LockText => Files.File.Exists(Lock) ? Files.File.ReadAllText(Lock) : null;

    /// <summary>The lock the gate writes: six lines, each ending in the platform newline (Set-Content -Encoding ascii).</summary>
    public string ExpectedLock(string reason, string expected, string observed, string mode, string previousImage = "") =>
        string.Join(Environment.NewLine, new[]
        {
            "reason=" + reason, "expected=" + expected, "observed=" + observed, "mode=" + mode, "previousImage=" + previousImage,
            "utc=" + Clock.GetUtcNow().UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
        }) + Environment.NewLine;

    public static NightlyLaunchResult Answer(int exit, IEnumerable<string>? lines = null)
    {
        var text = lines == null ? "" : string.Concat(lines.Select(line => line + "\n"));
        return new NightlyLaunchResult { ExitCode = exit, StandardOutputText = text, LogText = text };
    }

    private NightlyLaunchResult Docker(NightlyLaunch launch)
    {
        var args = launch.Arguments.ToList();
        (int Exit, string[] Lines)? entry = null;
        if (args.Count > 2 && args[0] == "inspect" && args[2] == "{{.Image}}") entry = Image;
        else if (args.Count > 2 && args[0] == "inspect" && args[2] == "{{.Config.Image}}") entry = Ref;
        else if (args.Count > 1 && args[0] == "image" && args[1] == "inspect") entry = TagId;
        return entry == null ? Answer(99) : Answer(entry.Value.Exit, entry.Value.Lines);
    }

    public LinuxServerRestartSeams Seams(List<string> calls) => new()
    {
        Http = url =>
        {
            calls.Add(url);
            var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : "";
            return Answers.TryGetValue(path, out var answer) ? answer : new LinuxServerHttpAnswer(0, "");
        },
        Resolve = name => Tools.TryGetValue(name, out var path) ? path : null,
    };

    public NightlyHost Host(StringWriter output, StringWriter error) => new()
    {
        Clock = Clock,
        Git = (_, _) => throw new InvalidOperationException("the gate runs no git"),
        Launcher = Launcher,
        Processes = new NightlyProcessTable(),
        Probe = new NightlyHostProbe(),
        Out = output,
        Error = error,
        Trace = TextWriter.Null,
        Variables = name => Variables.TryGetValue(name, out var value) ? value : null,
        Sleep = _ => throw new InvalidOperationException("the gate does not wait"),
    };

    /// <summary>One run with a fresh launcher record, as the driver's Invoke-C1745Gate (its -TimeoutSec 5 is the command's default here).</summary>
    public LinuxServerRestartRun Run(params string[] arguments)
    {
        Tools["docker"] = DockerPath;
        Launcher = new RecordingLauncher(Docker);
        var output = new StringWriter();
        var error = new StringWriter();
        var calls = new List<string>();
        var exit = LinuxServerRestart.Run(LinuxServerRestartOptions.Parse(arguments), Host(output, error), Files, Seams(calls));
        var docker = Launcher.Launches.Select(launch => string.Join(" ", launch.Arguments)).ToArray();
        return new LinuxServerRestartRun(exit, output.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n'), error.ToString(), calls.ToArray(), docker,
            Launcher.Launches.Select(launch => launch.FileName).Distinct().ToArray());
    }
}

/// <summary>One gate run: the exit, the stdout lines, stderr, the URLs asked and the docker argument lines.</summary>
internal sealed record LinuxServerRestartRun(int Exit, string[] Lines, string Error, string[] Http, string[] Docker, string[] DockerFiles)
{
    public string Out => string.Join("\n", Lines);

    public override string ToString() => "exit=" + Exit + "\n" + Out + "\n--- stderr\n" + Error + "\n--- http\n" + string.Join("\n", Http)
        + "\n--- docker\n" + string.Join("\n", Docker);
}
