using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts;

internal sealed record C590Run(int ExitCode, string Output, string ResultJson, IReadOnlyList<string> Commands, IReadOnlyList<NightlyLaunch> Launches);

/// <summary>
/// The CARD-0590 entries in process (unit 47 of the 2026-11-22 plan: the commands verify-docker-stack, test-docker and
/// test-docker-container that replaced scripts/test-docker.ps1, scripts/test-docker-container.ps1 and the switch of
/// scripts/verify-docker-stack.ps1). The manifest, the evidence root and the sink live in a mock file system; the launcher records
/// and starts nothing; ANTIPHON_C590_STUB is "1" and ANTIPHON_C590_SINK the sink unless a test's environment says otherwise (a
/// null value removes the variable). A usage error is exit 2 with its line in the output, as the CLI answers it.
/// </summary>
internal static class C590Harness
{
    public static string RepoRoot => DockerStackRoot();

    /// <summary>The mock file system's fixture root: the manifest, evidence/ and sink.jsonl live here.</summary>
    public static string FixtureRoot => OperatingSystem.IsWindows() ? @"C:\c590" : "/c590";

    public static string Evidence => Path.Combine(FixtureRoot, "evidence");

    public static string ManifestPath => Path.Combine(FixtureRoot, "manifest.json");

    public static string Sink => Path.Combine(FixtureRoot, "sink.jsonl");

    /// <summary>
    /// Runs command (verify-docker-stack, test-docker or test-docker-container) with args and then --manifest; the manifest's
    /// evidenceRoot is the fixture's. files seeds the mock file system; answer answers each launch (null: exit 0).
    /// </summary>
    public static C590Run Run(string command, IDictionary<string, object?> manifest, IDictionary<string, string?>? environment = null,
        IDictionary<string, string>? files = null, Func<NightlyLaunch, NightlyLaunchResult>? answer = null, params string[] args)
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory(Evidence);
        manifest["evidenceRoot"] = Evidence;
        fs.File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest));
        foreach (var pair in files ?? new Dictionary<string, string>())
        {
            fs.Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)!);
            fs.File.WriteAllText(pair.Key, pair.Value);
        }
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [C590Boundary.StubVariable] = "1",
            [C590Boundary.SinkVariable] = Sink,
        };
        foreach (var pair in environment ?? new Dictionary<string, string?>()) variables[pair.Key] = pair.Value;
        var inherited = variables.Where(pair => pair.Value != null).ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);

        using var output = new StringWriter();
        var launcher = new RecordingLauncher(launch => answer?.Invoke(launch) ?? new NightlyLaunchResult { ExitCode = 0 });
        var host = new NightlyHost
        {
            Clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-24T09:00:00Z")),
            Git = new FakeGit().Run,
            Launcher = launcher,
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = output,
            Trace = TextWriter.Null,
            Variables = name => inherited.TryGetValue(name, out var value) ? value : null,
            Sleep = _ => { },
        };
        string[] argv = [.. args, "--manifest", ManifestPath];
        int exit;
        try
        {
            exit = command switch
            {
                "verify-docker-stack" => DockerStackVerify.Run(DockerStackVerifyOptions.Parse(argv, RepoRoot), host, fs),
                "test-docker" => DockerTestEntry.Run(DockerTestOptions.Parse(argv, RepoRoot, inherited), host, fs),
                "test-docker-container" => DockerTestContainer.Run(DockerTestContainerOptions.Parse(argv), host, fs),
                _ => throw new ArgumentException("not a CARD-0590 command: " + command, nameof(command)),
            };
        }
        catch (NightlyUsageException e)
        {
            output.WriteLine("usage: antiphon-scripts " + command + ": " + e.Message);
            exit = 2;
        }
        var resultPath = Path.Combine(Evidence, C590Boundary.ResultFileName);
        var result = fs.File.Exists(resultPath) ? fs.File.ReadAllText(resultPath) : "";
        var commands = fs.File.Exists(Sink) ? fs.File.ReadAllLines(Sink) : [];
        return new C590Run(exit, output.ToString(), result, commands, launcher.Launches);
    }

    public static Dictionary<string, object?> Happy() => new()
    {
        ["sourceRoot"] = RepoRoot,
        ["checkoutRoot"] = RepoRoot,
        ["sourceSha"] = "abc",
        ["runId"] = "run-1",
        ["task"] = new Dictionary<string, object?> { ["readable"] = true, ["sourceLandingOperationId"] = "" },
        ["socket"] = new Dictionary<string, object?> { ["present"] = true, ["gid"] = 999, ["groups"] = new[] { 999 } },
        // CARD-0604 D-5: the nested daemon's own name equals the runner container's hostname.
        ["daemon"] = new Dictionary<string, object?> { ["present"] = true, ["name"] = "runner-host", ["hostname"] = "runner-host" },
        ["dbProbeOk"] = true,
        ["childExit"] = 0,
        ["reportPresent"] = true,
        ["report"] = new Dictionary<string, object?>
        {
            ["executed"] = 1, ["passed"] = 1, ["failed"] = 0, ["skipped"] = 0,
            ["classes"] = new[] { "A" }, ["runId"] = "run-1",
        },
        ["expectedClasses"] = new[] { "A" },
        ["artifactDigest"] = "digest",
        ["copiedDigest"] = "digest",
    };

    private static string DockerStackRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root was not found");
    }
}
