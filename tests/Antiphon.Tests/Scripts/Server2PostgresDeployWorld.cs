using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 54 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the deploy-server2-postgres command (CARD-1406) in
/// process. The deleted driver scripts/test-server2-postgres-compose.ps1 ran scripts/deploy-server2-postgres.ps1 under pwsh with
/// POSIX sh stubs of docker, hostname and tailscale on a private PATH and env files given real POSIX modes, so it ran on Linux
/// only. This world answers the same: the repo is a mock disk holding the checkout's compose files (LF, as a Linux checkout has
/// them) and its docker-compose.dev.yml; the env files sit outside it with the mode the case gives; docker, hostname and
/// tailscale resolve to fixture paths (unless removed) and a recording launcher answers them (docker info prints Daemon, hostname
/// prints Hostname, tailscale ip -4 prints TailscaleText, docker compose exits ComposeExit, anything else 99). Nothing starts.
/// </summary>
internal sealed class Server2PostgresWorld
{
    public const string Bind = "100.64.0.2";
    public const string Password = "fixture-password-1406";
    public static readonly string Image = "postgres:16@sha256:" + new string('a', 64);
    public static readonly string Base = OperatingSystem.IsWindows() ? @"C:\card1406" : "/card1406";

    public Server2PostgresWorld()
    {
        Files.AddDirectory(Root);
        Files.AddDirectory(Secrets);
        Files.AddFile(ProductionCompose, new MockFileData(ProductionText));
        Files.AddFile(DrillCompose, new MockFileData(DrillText));
        Files.AddFile(Files.Path.Combine(Root, "docker-compose.dev.yml"), new MockFileData(DockerStackDocuments.Read("docker-compose.dev.yml").Replace("\r\n", "\n")));
        foreach (var tool in new[] { "docker", "hostname", "tailscale" }) Tools[tool] = Path.Combine(Base, "bin", tool);
    }

    /// <summary>The checkout's production compose, LF.</summary>
    public static string ProductionText { get; } = DockerStackDocuments.Read("docker-compose.server2-postgres.yml").Replace("\r\n", "\n");

    /// <summary>The checkout's drill compose, LF.</summary>
    public static string DrillText { get; } = DockerStackDocuments.Read("docker-compose.server2-postgres-drill.yml").Replace("\r\n", "\n");

    /// <summary>docker-compose.dev.yml's committed dev password.</summary>
    public static string DevPassword { get; } =
        System.Text.RegularExpressions.Regex.Match(DockerStackDocuments.Read("docker-compose.dev.yml"), @"POSTGRES_PASSWORD:\s*(\S+)").Groups[1].Value;

    public MockFileSystem Files { get; } = new();
    public string Root { get; } = Path.Combine(Base, "repo");
    public string Secrets { get; } = Path.Combine(Base, "secrets");
    public string ProductionCompose => Path.Combine(Root, Server2PostgresDeploy.ComposeName);
    public string DrillCompose => Path.Combine(Root, Server2PostgresDeploy.DrillComposeName);

    /// <summary>The host's environment (no OS variable: the POSIX mode rule applies).</summary>
    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

    /// <summary>The full paths whose POSIX mode reads exactly 0600.</summary>
    public HashSet<string> OwnerOnly { get; } = new(StringComparer.Ordinal);

    /// <summary>Get-Command's answers: tool name to path; a removed name is not found.</summary>
    public Dictionary<string, string> Tools { get; } = new(StringComparer.Ordinal);

    public string? Daemon { get; set; } = "not-the-host";
    public string? Hostname { get; set; } = "fixture-host";
    public string TailscaleText { get; set; } = "";
    public int ComposeExit { get; set; }
    public RecordingLauncher Launcher { get; private set; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 26, 3, 0, 0, TimeSpan.Zero));

    /// <summary>New-EnvFile: bind, image and password lines (LF), mode 0600 unless ownerOnly is false.</summary>
    public string Env(string name = "postgres.env", string bind = Bind, string? image = null, string password = Password, bool ownerOnly = true,
        bool omitBind = false, bool emptyBind = false)
    {
        var lines = new List<string>();
        if (emptyBind) lines.Add("ANTIPHON_POSTGRES_BIND_ADDRESS=");
        else if (!omitBind) lines.Add("ANTIPHON_POSTGRES_BIND_ADDRESS=" + bind);
        lines.Add("ANTIPHON_POSTGRES_IMAGE=" + (image ?? Image));
        lines.Add("POSTGRES_PASSWORD=" + password);
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

    /// <summary>A compose file of this text (as given) in Base, outside the repo.</summary>
    public string Compose(string name, string text)
    {
        var path = Path.Combine(Base, name);
        Files.AddFile(path, new MockFileData(text));
        return path;
    }

    public Server2PostgresDeploySeams Seams => new()
    {
        Resolve = name => Tools.TryGetValue(name, out var path) ? path : null,
        OwnerReadWriteOnly = path => OwnerOnly.Contains(Files.Path.GetFullPath(path)),
    };

    public static NightlyLaunchResult Answer(int exit, string text = "") =>
        new() { ExitCode = exit, StandardOutputText = text, LogText = text };

    private NightlyLaunchResult Answer(NightlyLaunch launch)
    {
        var args = launch.Arguments.ToList();
        switch (Path.GetFileNameWithoutExtension(launch.FileName))
        {
            case "docker" when args.Count > 0 && args[0] == "info":
                return Answer(0, Daemon == null ? "" : Daemon + "\n");
            case "docker" when args.Count > 0 && args[0] == "compose":
                return Answer(ComposeExit);
            case "hostname":
                return Answer(0, Hostname == null ? "" : Hostname + "\n");
            case "tailscale":
                return Answer(0, TailscaleText);
            default:
                return Answer(99);
        }
    }

    public NightlyHost Host(StringWriter output, StringWriter error) => new()
    {
        Clock = Clock,
        Git = (_, _) => throw new InvalidOperationException("the deploy runs no git"),
        Launcher = Launcher,
        Processes = new NightlyProcessTable(),
        Probe = new NightlyHostProbe(),
        Out = output,
        Error = error,
        Trace = TextWriter.Null,
        Variables = name => Variables.TryGetValue(name, out var value) ? value : null,
        Sleep = _ => { },
    };

    /// <summary>One run with a fresh launcher record (the driver's marker was new for each case), the checkout being Root.</summary>
    public Server2PostgresRun Run(params string[] arguments)
    {
        Launcher = new RecordingLauncher(Answer);
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = Server2PostgresDeploy.Run(Server2PostgresDeployOptions.Parse(arguments, Root), Host(output, error), Files, Seams);
        var tools = Launcher.Launches.Select(launch => Path.GetFileNameWithoutExtension(launch.FileName) + (launch.Arguments.Count == 0 ? "" : " " + string.Join(" ", launch.Arguments))).ToArray();
        return new Server2PostgresRun(exit, output.ToString().Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries), error.ToString(), tools);
    }
}

/// <summary>One run: the exit, the stdout lines, stderr and the launches ("docker info --format {{.Name}}", "hostname", ...).</summary>
internal sealed record Server2PostgresRun(int Exit, string[] Lines, string Error, string[] Tools)
{
    public string Out => string.Join("\n", Lines);

    /// <summary>The SERVER2-POSTGRES line's reason (Get-Reason), '' without one.</summary>
    public string Reason
    {
        get
        {
            var match = System.Text.RegularExpressions.Regex.Match(Out, "(?m)^SERVER2-POSTGRES .*reason=([A-Za-z0-9-]+)");
            return match.Success ? match.Groups[1].Value : "";
        }
    }

    /// <summary>The password reached no stream and no argv (Assert-NoDocker's password half).</summary>
    public bool Hides(string secret) =>
        !Out.Contains(secret, StringComparison.Ordinal) && !Error.Contains(secret, StringComparison.Ordinal) && !Tools.Any(tool => tool.Contains(secret, StringComparison.Ordinal));

    public override string ToString() => "exit=" + Exit + "\n" + Out + "\n--- stderr\n" + Error + "\n--- tools\n" + string.Join("\n", Tools);
}
