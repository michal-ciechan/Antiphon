using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 64 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the deploy-server2-server (CARD-1775) and
/// deploy-server2-staging-server (CARD-1742) commands in process. The deleted scripts ran under pwsh on the server2 host; their
/// witnesses ran them with a Unix fake docker on PATH and env files given real POSIX modes, so they ran on Linux only. This world
/// answers the same with nothing started: the repo is a mock disk holding the checkout's two compose files (LF, as a Linux checkout
/// has them), its docker-compose.dev.yml and /var/tmp; the env files, the certificate, key and operator token sit outside it; the
/// POSIX mode answer is the OwnerOnly set; docker, hostname, git and curl resolve to fixture paths (unless removed) and a
/// recording launcher answers them (docker info prints Daemon, hostname prints Hostname, git prints GitHead and exits GitExit,
/// docker compose prints the secret on both streams and exits ComposeExit, curl exits HealthExits in turn for /health and
/// VersionExit for /api/version); the production gate's HTTP answers 127.0.0.1:17400 from GateHealth and GateVersion and records
/// each URL. Sleep advances the fake clock.
/// </summary>
internal sealed class Server2ServerDeployWorld
{
    public const string Head = "dddddddddddddddddddddddddddddddddddddddd";
    public const string Secret = "fixture-secret-1775";
    public const string Password = "fixture-password-1775";
    public const string StagingPassword = "fixture-password-1742";
    public static readonly string StagingImage = "postgres:16@sha256:" + new string('a', 64);
    public static readonly string Base = OperatingSystem.IsWindows() ? @"C:\card1775" : "/card1775";

    public Server2ServerDeployWorld()
    {
        Files.AddDirectory(Root);
        Files.AddDirectory(Secrets);
        Files.AddDirectory("/var/tmp");
        Files.AddFile(ProductionCompose, new MockFileData(ProductionText));
        Files.AddFile(StagingCompose, new MockFileData(StagingText));
        Files.AddFile(Files.Path.Combine(Root, "docker-compose.dev.yml"), new MockFileData(DockerStackDocuments.Read("docker-compose.dev.yml").Replace("\r\n", "\n")));
        foreach (var name in new[] { "cert.pem", "key.pem", "operator-token" })
            Files.AddFile(Path.Combine(Secrets, name), new MockFileData(name));
        foreach (var tool in new[] { "docker", "hostname", "git", "curl" }) Tools[tool] = Path.Combine(Base, "bin", tool);
    }

    /// <summary>The checkout's production compose, LF.</summary>
    public static string ProductionText { get; } = DockerStackDocuments.Read("docker-compose.server2-server.yml").Replace("\r\n", "\n");

    /// <summary>The checkout's staging compose, LF.</summary>
    public static string StagingText { get; } = DockerStackDocuments.Read("docker-compose.server2-staging-server.yml").Replace("\r\n", "\n");

    /// <summary>docker-compose.dev.yml's committed dev password.</summary>
    public static string DevPassword { get; } =
        System.Text.RegularExpressions.Regex.Match(DockerStackDocuments.Read("docker-compose.dev.yml"), @"POSTGRES_PASSWORD:\s*(\S+)").Groups[1].Value;

    public MockFileSystem Files { get; } = new();
    public string Root { get; } = Path.Combine(Base, "repo");
    public string Secrets { get; } = Path.Combine(Base, "secrets");
    public string ProductionCompose => Path.Combine(Root, Server2ServerDeploy.ComposeName);
    public string StagingCompose => Path.Combine(Root, Server2StagingDeploy.ComposeName);
    public string Cert => Path.Combine(Secrets, "cert.pem");
    public string Key => Path.Combine(Secrets, "key.pem");
    public string Token => Path.Combine(Secrets, "operator-token");

    /// <summary>The host's environment (no OS variable: the POSIX mode rule applies).</summary>
    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

    /// <summary>The full paths whose POSIX mode reads exactly 0600.</summary>
    public HashSet<string> OwnerOnly { get; } = new(StringComparer.Ordinal);

    /// <summary>The paths given mode 0600 (SetUnixFileMode), in order.</summary>
    public List<string> Chmods { get; } = [];

    /// <summary>Get-Command's answers: tool name to path; a removed name is not found.</summary>
    public Dictionary<string, string> Tools { get; } = new(StringComparer.Ordinal);

    public string? Daemon { get; set; } = "server2";
    public string? Hostname { get; set; } = "server2";
    public string GitHead { get; set; } = Head;
    public int GitExit { get; set; }
    public int ComposeExit { get; set; }

    /// <summary>curl's exits for /health, one per call (the last repeats).</summary>
    public int[] HealthExits { get; set; } = [0];

    public int VersionExit { get; set; }

    /// <summary>The gate's /health status, one per call (the last repeats); its /api/version body.</summary>
    public int[] GateHealth { get; set; } = [200];

    public string GateVersion { get; set; } = "{\"version\":\"" + Head + "\"}";

    public List<string> GateCalls { get; } = [];
    public RecordingLauncher Launcher { get; private set; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 26, 3, 0, 0, TimeSpan.Zero));

    /// <summary>The production env file the witnesses used (LF), with changes and drops, mode 0600 unless ownerOnly is false.</summary>
    public string ProductionEnv(string name = "prod.env", Dictionary<string, string>? change = null, string[]? drop = null, bool ownerOnly = true)
    {
        var map = new List<KeyValuePair<string, string>>
        {
            new("POSTGRES_PASSWORD", Password),
            new("HANGFIRE_STORAGE", "InMemory"),
            new("ANTIPHON_PUBLIC_ORIGIN", "https://example.invalid"),
            new("PHONE_HOME_SHARED_SECRET", Secret),
            new("PHONE_HOME_HOST_WORKSPACE_ROOT", "/work"),
            new("PHONE_HOME_DESKTOP_WIN_SHARED_SECRET", "fixture-desktop-win-secret"),
            new("PHONE_HOME_DESKTOP_WIN_HOME", "C:/Users/fixture"),
            new("KEYRING_CERT_FILE", Cert),
            new("KEYRING_KEY_FILE", Key),
            new("OPERATOR_TOKEN_FILE", Token),
            new("SOURCE_REVISION", Head),
        };
        return EnvFromMap(name, map, change, drop, ownerOnly);
    }

    /// <summary>The staging env file (image and password), with changes and drops.</summary>
    public string StagingEnv(string name = "stage.env", Dictionary<string, string>? change = null, string[]? drop = null, bool ownerOnly = true)
    {
        var map = new List<KeyValuePair<string, string>> { new("ANTIPHON_STAGING_POSTGRES_IMAGE", StagingImage), new("POSTGRES_PASSWORD", StagingPassword) };
        return EnvFromMap(name, map, change, drop, ownerOnly);
    }

    private string EnvFromMap(string name, List<KeyValuePair<string, string>> map, Dictionary<string, string>? change, string[]? drop, bool ownerOnly)
    {
        var lines = map.Where(pair => drop == null || !drop.Contains(pair.Key))
            .Select(pair => pair.Key + "=" + (change != null && change.TryGetValue(pair.Key, out var value) ? value : pair.Value)).ToList();
        if (change != null) lines.AddRange(change.Where(pair => map.All(entry => entry.Key != pair.Key)).Select(pair => pair.Key + "=" + pair.Value));
        return EnvLines(name, lines, ownerOnly);
    }

    /// <summary>An env file of exactly these lines (LF) in Secrets.</summary>
    public string EnvLines(string name, IEnumerable<string> lines, bool ownerOnly = true)
    {
        var path = Path.Combine(Secrets, name);
        Files.AddFile(path, new MockFileData(string.Join("\n", lines) + "\n"));
        if (ownerOnly) OwnerOnly.Add(Files.Path.GetFullPath(path));
        else OwnerOnly.Remove(Files.Path.GetFullPath(path));
        return path;
    }

    /// <summary>A compose file of this text in Base, outside the repo.</summary>
    public string Compose(string name, string text)
    {
        var path = Path.Combine(Base, name);
        Files.AddFile(path, new MockFileData(text));
        return path;
    }

    private int _healthCall;
    private int _gateCall;

    private NightlyLaunchResult Answer(NightlyLaunch launch)
    {
        var args = launch.Arguments.ToList();
        switch (Path.GetFileNameWithoutExtension(launch.FileName))
        {
            case "docker" when args.Count > 0 && args[0] == "info":
                return Answer(0, Daemon == null ? "" : Daemon + "\n");
            case "docker" when args.Count > 0 && args[0] == "compose":
                return new NightlyLaunchResult { ExitCode = ComposeExit, StandardOutputText = "compose-out-" + Secret + "\n", StandardErrorText = "compose-err-" + Secret + "\n" };
            case "hostname":
                return Answer(0, Hostname == null ? "" : Hostname + "\n");
            case "git":
                return new NightlyLaunchResult { ExitCode = GitExit, StandardOutputText = GitHead.Length == 0 ? "" : GitHead + "\n", StandardErrorText = "git-stderr\n" };
            case "curl" when args.Count > 3 && args[3] == Server2StagingDeploy.VersionUrl:
                return Answer(VersionExit, "curl-out\n");
            case "curl":
                return Answer(HealthExits[Math.Min(_healthCall++, HealthExits.Length - 1)], "curl-out\n");
            default:
                return Answer(99);
        }
    }

    public static NightlyLaunchResult Answer(int exit, string text = "") => new() { ExitCode = exit, StandardOutputText = text, LogText = text };

    public Server2ServerDeploySeams Seams => new()
    {
        Resolve = name => Tools.TryGetValue(name, out var path) ? path : null,
        OwnerReadWriteOnly = path => OwnerOnly.Contains(Files.Path.GetFullPath(path)),
        SetOwnerReadWrite = path =>
        {
            if (!Files.File.Exists(path)) throw new FileNotFoundException("Could not find file '" + path + "'.");
            Chmods.Add(path);
        },
        GateHttp = url =>
        {
            GateCalls.Add(url);
            if (url.EndsWith("/health", StringComparison.Ordinal))
                return new LinuxServerHttpAnswer(GateHealth[Math.Min(_gateCall++, GateHealth.Length - 1)], "Healthy");
            return new LinuxServerHttpAnswer(200, GateVersion);
        },
    };

    public NightlyHost Host(StringWriter output, StringWriter error) => new()
    {
        Clock = Clock,
        Git = (_, _) => throw new InvalidOperationException("the deploy runs git on the launcher"),
        Launcher = Launcher,
        Processes = new NightlyProcessTable(),
        Probe = new NightlyHostProbe(),
        Out = output,
        Error = error,
        Trace = TextWriter.Null,
        Variables = name => Variables.TryGetValue(name, out var value) ? value : null,
        Sleep = wait => Clock.Advance(wait),
    };

    private (StringWriter Output, StringWriter Error) Fresh()
    {
        Launcher = new RecordingLauncher(Answer);
        _healthCall = 0;
        _gateCall = 0;
        GateCalls.Clear();
        Chmods.Clear();
        return (new StringWriter(), new StringWriter());
    }

    private Server2DeployRun Finish(int exit, StringWriter output, StringWriter error)
    {
        var tools = Launcher.Launches.Select(launch => Path.GetFileNameWithoutExtension(launch.FileName) + (launch.Arguments.Count == 0 ? "" : " " + string.Join(" ", launch.Arguments))).ToArray();
        return new Server2DeployRun(exit, output.ToString().Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries), error.ToString(), tools, GateCalls.ToArray(),
            Launcher.Launches.Select(launch => launch.CaptureOutput).ToArray());
    }

    /// <summary>One production run, the checkout being Root.</summary>
    public Server2DeployRun Production(params string[] arguments)
    {
        var (output, error) = Fresh();
        var exit = Server2ServerDeploy.Run(Server2ServerDeployOptions.Parse(arguments, Root), Host(output, error), Files, Seams);
        return Finish(exit, output, error);
    }

    /// <summary>One staging run, the checkout being Root.</summary>
    public Server2DeployRun Staging(params string[] arguments)
    {
        var (output, error) = Fresh();
        var exit = Server2StagingDeploy.Run(Server2StagingDeployOptions.Parse(arguments, Root), Host(output, error), Files, Seams);
        return Finish(exit, output, error);
    }
}

/// <summary>One run: the exit, the stdout lines, stderr, the launches ("docker info --format {{.Name}}", "hostname", ...) and the gate's URLs.</summary>
internal sealed record Server2DeployRun(int Exit, string[] Lines, string Error, string[] Tools, string[] Gate, bool[] Captured)
{
    public string Out => string.Join("\n", Lines);

    /// <summary>The SERVER2-SERVER or SERVER2-STAGING line's reason, '' without one.</summary>
    public string Reason
    {
        get
        {
            var match = System.Text.RegularExpressions.Regex.Match(Out, "(?m)^SERVER2-(?:SERVER|STAGING) .*reason=([A-Za-z0-9-]+)");
            return match.Success ? match.Groups[1].Value : "";
        }
    }

    /// <summary>The secret reached no stream and no argv.</summary>
    public bool Hides(string secret) =>
        !Out.Contains(secret, StringComparison.Ordinal) && !Error.Contains(secret, StringComparison.Ordinal) && !Tools.Any(tool => tool.Contains(secret, StringComparison.Ordinal));

    public override string ToString() => "exit=" + Exit + "\n" + Out + "\n--- stderr\n" + Error + "\n--- tools\n" + string.Join("\n", Tools) + "\n--- gate\n" + string.Join("\n", Gate);
}
