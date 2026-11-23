using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0545 S5: the watchdog deploy in process (NightlyWatchdogDeploy, `antiphon-scripts deploy-nightly-watchdog`). These
/// cases replaced scripts/test-deploy-nightly-watchdog.ps1, deleted with scripts/deploy-nightly-watchdog.ps1 (unit 10 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md), and keep its case and row names. No case contacts a
/// host or reads a real profile: the profile, templates and work directory are in a MockFileSystem, dotnet, ssh and scp
/// answer from a RecordingLauncher, git from FakeGit, the snapshot GET from a fake transport, and the default profile path
/// names a missing file. The fictitious profile values are assembled from parts so this file names no host, address, user
/// or home path (it is inside the host-agnostic scan).
/// </summary>
public sealed partial class NightlyVerificationContractTests
{
    private static readonly string C545User = "ops";
    private static readonly string C545Target = C545User + '@' + "watchdog-host" + ".example";
    private static readonly string C545Root = "/" + "home" + "/" + C545User + "/antiphon-watchdog";
    private static readonly string C545Ip = string.Join(".", "10", "0", "0", "5");

    /// <summary>One deploy's world: the file system, the profile and checkout in it, and what each seam was asked.</summary>
    private sealed class C545DeployFx
    {
        public required MockFileSystem Files { get; init; }
        public required string Root { get; init; }
        public required string ProfilePath { get; init; }
        public required string RepoRoot { get; init; }
        public FakeGit Git { get; init; } = new();
        public List<NightlyLaunch> Launches { get; } = [];
        public List<string> Ssh { get; } = [];
        public List<string> Scp { get; } = [];
        public List<string> Publish { get; } = [];
        public List<string> Http { get; } = [];
        public string RemoteEnv { get; set; } = "";

        /// <summary>A launch's exit code (null: 0).</summary>
        public Func<NightlyLaunch, int>? ExitOf { get; set; }

        /// <summary>The snapshot GET's status.</summary>
        public int SnapshotStatus { get; set; } = 200;

        public string WorkDir => Files.Path.Combine(Root, "work");

        /// <summary>The runner calls (publish, ssh, scp, HTTP) the harness counted.</summary>
        public int Calls => Ssh.Count + Scp.Count + Publish.Count + Http.Count;
    }

    private sealed record C545DeployRun(NightlyWatchdogDeployResult Result, string Output);

    private static C545DeployFx NewC545DeployFx(IReadOnlyDictionary<string, string?>? overrides = null, string remoteEnv = "", bool noProfile = false,
        FakeGit? git = null)
    {
        var files = new MockFileSystem();
        var root = files.Path.Combine(files.Path.GetTempPath(), "deploy-" + Guid.NewGuid().ToString("N"));
        files.Directory.CreateDirectory(root);
        var profilePath = files.Path.Combine(root, "watchdog-deploy.local.json");
        if (!noProfile) files.File.WriteAllText(profilePath, C545Profile(overrides));
        var repo = files.Path.Combine(root, "repo");
        var assets = files.Path.Combine(repo, "scripts", "nightly-watchdog");
        files.Directory.CreateDirectory(assets);
        foreach (var asset in new[] { "antiphon-nightly-watchdog.service.template", "qual.example.json" })
            files.File.WriteAllText(files.Path.Combine(assets, asset), File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-watchdog", asset)));
        return new C545DeployFx { Files = files, Root = root, ProfilePath = profilePath, RepoRoot = repo, RemoteEnv = remoteEnv, Git = git ?? new FakeGit() };
    }

    /// <summary>The fictitious profile's JSON with these keys replaced (a null value is JSON null).</summary>
    private static string C545Profile(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var profile = new JsonObject
        {
            ["sshTarget"] = C545Target,
            ["sshIdentityFile"] = null,
            ["remoteRoot"] = C545Root,
            ["serviceUser"] = C545User,
            ["rid"] = "linux-x64",
            ["runtime"] = "native",
            ["snapshotBind"] = C545Ip + ":17290",
            ["snapshotUrl"] = "http://" + C545Ip + ":17290/snapshot.json",
            ["qualSnapshotBind"] = C545Ip + ":17291",
            ["stubWindmillBind"] = "127.0.0.1:17292",
        };
        foreach (var pair in overrides ?? new Dictionary<string, string?>()) profile[pair.Key] = pair.Value;
        return profile.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static Dictionary<string, string?> C545Override(string key, string? value) => new() { [key] = value };

    /// <summary>Runs the preflight (or the deploy) on fakes. The output is stdout and stderr together, as Write-Host printed both.</summary>
    private static C545DeployRun RunC545Deploy(C545DeployFx fx, bool deploy = false, bool noProfileArg = false, string? environmentProfile = null,
        string? defaultProfile = null)
    {
        var output = new StringWriter();
        var host = new NightlyHost
        {
            Clock = new FakeTimeProvider(NightlyFixture.DefaultNow),
            Git = fx.Git.Run,
            Launcher = new RecordingLauncher(launch => AnswerC545Launch(fx, launch)),
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = output,
            Trace = TextWriter.Null,
        };
        var options = new NightlyWatchdogDeployOptions
        {
            ProfilePath = noProfileArg ? "" : fx.ProfilePath,
            EnvironmentProfilePath = environmentProfile ?? "",
            DefaultProfilePath = defaultProfile ?? fx.Files.Path.Combine(fx.Root, "missing-default.json"),
            Deploy = deploy,
            RepoRoot = fx.RepoRoot,
            WorkDir = fx.WorkDir,
        };
        var result = NightlyWatchdogDeploy.Run(options, host, fx.Files, (method, uri, headers, body) =>
        {
            fx.Http.Add(uri);
            return new NightlyReportHttpResponse(fx.SnapshotStatus, "{}");
        });
        return new C545DeployRun(result, output.ToString());
    }

    // The fake host: ssh answers the env key names (sed) and applies an appended env fragment; dotnet publish writes a fake
    // binary into its -o directory; scp records the copy.
    private static NightlyLaunchResult AnswerC545Launch(C545DeployFx fx, NightlyLaunch launch)
    {
        fx.Launches.Add(launch);
        var arguments = launch.Arguments.ToList();
        var exit = fx.ExitOf?.Invoke(launch) ?? 0;
        switch (launch.FileName)
        {
            case "ssh":
            {
                var command = arguments[^1];
                fx.Ssh.Add(command);
                if (command.Contains("sed -n", StringComparison.Ordinal))
                {
                    var names = fx.RemoteEnv.Split('\n').Where(line => Regex.IsMatch(line, "^[A-Z0-9_]+=")).Select(line => line.Split('=')[0] + "=");
                    return new NightlyLaunchResult { ExitCode = exit, LogText = string.Concat(names.Select(name => name + "\n")) };
                }
                var append = Regex.Match(command, @"^echo '([A-Za-z0-9+/=]+)' \| base64 -d >>");
                if (append.Success && exit == 0) fx.RemoteEnv += Encoding.ASCII.GetString(Convert.FromBase64String(append.Groups[1].Value));
                return new NightlyLaunchResult { ExitCode = exit, LogText = "ok\n" };
            }
            case "scp":
                fx.Scp.Add(arguments[^2] + " -> " + arguments[^1]);
                return new NightlyLaunchResult { ExitCode = exit };
            case "dotnet":
            {
                fx.Publish.Add(arguments[arguments.IndexOf("-r") + 1]);
                var outputDir = arguments[arguments.IndexOf("-o") + 1];
                fx.Files.Directory.CreateDirectory(outputDir);
                fx.Files.File.WriteAllText(fx.Files.Path.Combine(outputDir, "Antiphon.NightlyWatchdog"), "fake-binary");
                return new NightlyLaunchResult { ExitCode = exit };
            }
            default:
                throw new InvalidOperationException("unexpected launch " + launch.CommandText);
        }
    }

    [Test]
    public void C545_DeployRefusesWithoutProfile()
    {
        var rows = new NightlyCaseRows();
        static bool Untouched(C545DeployFx world) => world.Calls == 0 && world.Launches.Count == 0 && world.Git.Calls.Count == 0;

        var fx = NewC545DeployFx(noProfile: true);
        var r = RunC545Deploy(fx, noProfileArg: true);
        rows.Check(r.Result.ExitCode == 3 && r.Output.Contains("no deploy profile", StringComparison.Ordinal) && Untouched(fx),
            "C545 DeployRefusesWithoutProfile no-profile/no-arg exit 3 no runner call", "exit=" + r.Result.ExitCode + " calls=" + fx.Calls + " out=" + r.Output);

        fx = NewC545DeployFx(noProfile: true);
        r = RunC545Deploy(fx, noProfileArg: true, environmentProfile: fx.Files.Path.Combine(fx.Root, "env-named-but-missing.json"));
        rows.Check(r.Result.ExitCode == 3 && Untouched(fx), "C545 DeployRefusesWithoutProfile no-profile/env-missing-file exit 3", "exit=" + r.Result.ExitCode);

        fx = NewC545DeployFx(C545Override("sshTarget", "<user>" + '@' + "<host>"));
        r = RunC545Deploy(fx);
        rows.Check(r.Result.ExitCode == 3 && r.Output.Contains("sshTarget", StringComparison.Ordinal) && Untouched(fx),
            "C545 DeployRefusesWithoutProfile placeholder/sshTarget exit 3", "exit=" + r.Result.ExitCode + " out=" + r.Output);

        fx = NewC545DeployFx(C545Override("remoteRoot", "/" + "home/<user>/antiphon-watchdog"));
        r = RunC545Deploy(fx);
        rows.Check(r.Result.ExitCode == 3 && r.Output.Contains("remoteRoot", StringComparison.Ordinal) && Untouched(fx),
            "C545 DeployRefusesWithoutProfile placeholder/remoteRoot exit 3", r.Output);

        fx = NewC545DeployFx(C545Override("snapshotBind", "127.0.0.1:17290"));
        r = RunC545Deploy(fx);
        rows.Check(r.Result.ExitCode == 3 && r.Output.Contains("snapshotBind must be a private non-loopback address", StringComparison.Ordinal),
            "C545 DeployRefusesWithoutProfile loopback-bind exit 3", r.Output);

        fx = NewC545DeployFx(C545Override("snapshotBind", "0.0.0.0:17290"));
        r = RunC545Deploy(fx);
        rows.Check(r.Result.ExitCode == 3 && r.Output.Contains("wildcard", StringComparison.Ordinal), "C545 DeployRefusesWithoutProfile wildcard-bind native exit 3", r.Output);

        fx = NewC545DeployFx();
        r = RunC545Deploy(fx);
        rows.Check(r.Result.ExitCode == 0, "C545 DeployRefusesWithoutProfile control valid profile preflight exit 0", r.Output);

        rows.ShouldAllPass(7, "C545 DeployRefusesWithoutProfile no-profile/no-arg exit 3 no runner call",
            "C545 DeployRefusesWithoutProfile placeholder/sshTarget exit 3", "C545 DeployRefusesWithoutProfile control valid profile preflight exit 0");
    }

    [Test]
    public void C545_DeployRendersFromProfile()
    {
        var rows = new NightlyCaseRows();
        var fx = NewC545DeployFx();
        var r = RunC545Deploy(fx);
        var unit = r.Result.RenderedUnit;
        var expectedLines = new[]
        {
            "User=" + C545User,
            "WorkingDirectory=" + C545Root,
            "EnvironmentFile=" + C545Root + "/env",
            "ExecStart=" + C545Root + "/Antiphon.NightlyWatchdog run",
            "ReadWritePaths=" + C545Root + "/state " + C545Root + "/state-qual",
        };
        var unitLines = Regex.Split(unit, "\r?\n");
        var missing = expectedLines.Where(line => !unitLines.Contains(line)).ToArray();
        rows.Check(unit.Length > 0 && !unit.Contains("@@", StringComparison.Ordinal) && missing.Length == 0, "C545 DeployRendersFromProfile unit placeholders fully substituted",
            "missing: " + string.Join(" | ", missing));
        var qual = r.Result.RenderedQual;
        JsonNode? qualJson = null;
        try { qualJson = JsonNode.Parse(qual); }
        catch (JsonException) { }
        rows.Check(!Regex.IsMatch(qual, "[<>]") && qualJson != null && (string?)qualJson["snapshotBind"] == C545Ip + ":17291" && (string?)qualJson["namespace"] == "mc/qual",
            "C545 DeployRendersFromProfile qual.json rendered without placeholders", qual);

        var sudo = fx.Ssh.Where(command => Regex.IsMatch(command, "systemctl|sudo")).ToArray();
        var selfCheck = fx.Ssh.Where(command => command.Contains("--self-check", StringComparison.Ordinal)).ToArray();
        rows.Check(fx.Scp.Count == 0 && sudo.Length == 0 && fx.Ssh.Count == 1 && selfCheck.Length == 1 && fx.Http.Count == 0,
            "C545 DeployRendersFromProfile preflight-no-upload", "ssh=" + string.Join(" || ", fx.Ssh) + " scp=" + fx.Scp.Count);
        rows.Check(fx.Publish.Count == 1 && fx.Publish[0] == "linux-x64", "C545 DeployRendersFromProfile preflight publishes for the profile rid", string.Join(",", fx.Publish));

        var secret = "never-print-" + "this";
        var existing = "ANTIPHON_WATCHDOG_TELEGRAM_BOT_TOKEN=" + secret + "\n" + "ANTIPHON_WATCHDOG_SNAPSHOT_BIND=" + C545Ip + ":17290" + "\n";
        var fx2 = NewC545DeployFx(remoteEnv: existing);
        var d = RunC545Deploy(fx2, deploy: true);
        var fragment = d.Result.EnvFragment;
        var expectedFragment = new[] { "ANTIPHON_WATCHDOG_STATE_DIR=./state", "ANTIPHON_WATCHDOG_NAMESPACE=mc" };
        rows.Check(d.Result.ExitCode == 0 && fragment.SequenceEqual(expectedFragment), "C545 DeployRendersFromProfile env fragment has exactly the absent non-secret keys",
            "exit=" + d.Result.ExitCode + " fragment=" + string.Join("|", fragment) + " out=" + d.Output);
        rows.Check(fx2.RemoteEnv.StartsWith(existing, StringComparison.Ordinal) && fx2.RemoteEnv.Length > existing.Length,
            "C545 DeployRendersFromProfile existing env lines survive byte-for-byte");
        var allText = d.Output + string.Join("\n", fx2.Ssh) + string.Join("\n", fx2.Scp) + string.Join("\n", fx2.Launches.Select(launch => launch.CommandText));
        rows.Check(!allText.Contains(secret, StringComparison.Ordinal), "C545 DeployRendersFromProfile secret never printed or sent");
        rows.Check(fx2.Http.Count == 1 && fx2.Http[0] == "http://" + C545Ip + ":17290/snapshot.json", "C545 DeployRendersFromProfile deploy-verifies-snapshot", string.Join(",", fx2.Http));
        var enable = fx2.Ssh.Where(command => command.Contains("systemctl enable --now antiphon-nightly-watchdog.service", StringComparison.Ordinal)).ToArray();
        rows.Check(enable.Length == 1 && fx2.Scp.Count == 3, "C545 DeployRendersFromProfile deploy uploads and enables the unit", "scp=" + string.Join(" || ", fx2.Scp));

        rows.ShouldAllPass(9, "C545 DeployRendersFromProfile preflight-no-upload", "C545 DeployRendersFromProfile env fragment has exactly the absent non-secret keys",
            "C545 DeployRendersFromProfile unit placeholders fully substituted", "C545 DeployRendersFromProfile deploy-verifies-snapshot");
    }

    // The argv of every child and the remote shell lines, pinned: what runs where must not drift.
    [Test]
    public void C545_DeployCommandLines()
    {
        var rows = new NightlyCaseRows();
        var fx = NewC545DeployFx();
        var d = RunC545Deploy(fx, deploy: true);
        var publish = fx.Launches.Single(launch => launch.FileName == "dotnet");
        var project = fx.Files.Path.Combine(fx.RepoRoot, "src", "Antiphon.NightlyWatchdog", "Antiphon.NightlyWatchdog.csproj");
        var expectedPublish = new[]
        {
            "publish", project, "-c", "Release", "-r", "linux-x64", "--self-contained", "-p:PublishSingleFile=true", "-p:SourceRevisionId=" + FakeGit.DefaultSha,
            "-o", fx.Files.Path.Combine(fx.WorkDir, "publish"),
        };
        rows.Check(d.Result.ExitCode == 0 && publish.Arguments.SequenceEqual(expectedPublish) && publish.WorkingDirectory == fx.RepoRoot,
            "C545 DeployCommandLines publish is self-contained for the rid and stamped with HEAD", publish.CommandText + " in " + publish.WorkingDirectory);
        rows.Check(fx.Git.Calls.Count == 1 && fx.Git.Calls[0].Arguments.SequenceEqual(new[] { "rev-parse", "HEAD" }) && fx.Git.Calls[0].WorkingDirectory == fx.RepoRoot,
            "C545 DeployCommandLines the stamp is the checkout's HEAD", string.Join(" | ", fx.Git.Lines));

        var ssh = fx.Launches.Where(launch => launch.FileName == "ssh").ToArray();
        rows.Check(ssh.Length == 4 && ssh.All(launch => launch.Arguments.Count == 4 && launch.Arguments[0] == "-o" && launch.Arguments[1] == "BatchMode=yes" && launch.Arguments[2] == C545Target),
            "C545 DeployCommandLines ssh runs in batch mode on the profile target", string.Join(" || ", ssh.Select(launch => launch.CommandText)));
        var expectedRemote = new[]
        {
            "mkdir -p '" + C545Root + "/state' '" + C545Root + "/state-qual' && chmod 700 '" + C545Root + "/state' '" + C545Root + "/state-qual'",
            "touch '" + C545Root + "/env' && chmod 600 '" + C545Root + "/env' && sed -n 's/^\\([A-Z0-9_]*\\)=.*/\\1=/p' '" + C545Root + "/env'",
            "echo '" + Convert.ToBase64String(Encoding.ASCII.GetBytes("ANTIPHON_WATCHDOG_SNAPSHOT_BIND=" + C545Ip + ":17290\nANTIPHON_WATCHDOG_STATE_DIR=./state\nANTIPHON_WATCHDOG_NAMESPACE=mc\n"))
                + "' | base64 -d >> '" + C545Root + "/env' && chmod 600 '" + C545Root + "/env'",
            "sudo chown -R '" + C545User + "' '" + C545Root + "/state' '" + C545Root + "/state-qual' '" + C545Root + "/env' && chmod 755 '" + C545Root
                + "/Antiphon.NightlyWatchdog' 2>/dev/null; sudo install -m 0644 '" + C545Root + "/antiphon-nightly-watchdog.service' /etc/systemd/system/antiphon-nightly-watchdog.service"
                + " && sudo systemctl daemon-reload && sudo systemctl enable --now antiphon-nightly-watchdog.service",
        };
        rows.Check(fx.Ssh.SequenceEqual(expectedRemote), "C545 DeployCommandLines the deploy's remote steps in order", string.Join(" || ", fx.Ssh));

        var scp = fx.Launches.Where(launch => launch.FileName == "scp").ToArray();
        var expectedScp = new[]
        {
            new[] { "-o", "BatchMode=yes", fx.Files.Path.Combine(fx.WorkDir, "publish", "Antiphon.NightlyWatchdog"), C545Target + ":" + C545Root + "/Antiphon.NightlyWatchdog" },
            new[] { "-o", "BatchMode=yes", fx.Files.Path.Combine(fx.WorkDir, "antiphon-nightly-watchdog.service"), C545Target + ":" + C545Root + "/antiphon-nightly-watchdog.service" },
            new[] { "-o", "BatchMode=yes", fx.Files.Path.Combine(fx.WorkDir, "qual.json"), C545Target + ":" + C545Root + "/qual.json" },
        };
        rows.Check(scp.Length == 3 && scp.Zip(expectedScp).All(pair => pair.First.Arguments.SequenceEqual(pair.Second)),
            "C545 DeployCommandLines scp uploads the binary, unit and config to the profile root", string.Join(" || ", scp.Select(launch => launch.CommandText)));
        rows.Check(fx.Launches.All(launch => launch.CaptureOutput && launch.Custody == NightlyCustody.Watched && launch.LogPath == null),
            "C545 DeployCommandLines every child is watched with its output captured");

        var unitFile = fx.Files.File.ReadAllText(fx.Files.Path.Combine(fx.WorkDir, "antiphon-nightly-watchdog.service"));
        var qualFile = fx.Files.File.ReadAllText(fx.Files.Path.Combine(fx.WorkDir, "qual.json"));
        rows.Check(!unitFile.Contains('\r') && !qualFile.Contains('\r') && unitFile == d.Result.RenderedUnit.Replace("\r\n", "\n") && qualFile == d.Result.RenderedQual.Replace("\r\n", "\n"),
            "C545 DeployCommandLines the uploaded unit and config are the rendered text with LF line ends");
        var binaryBytes = fx.Files.File.ReadAllBytes(fx.Files.Path.Combine(fx.WorkDir, "publish", "Antiphon.NightlyWatchdog"));
        rows.Check(d.Result.Checksum == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(binaryBytes)).ToLowerInvariant()
            && d.Output.Contains("Binary sha256: " + d.Result.Checksum, StringComparison.Ordinal), "C545 DeployCommandLines the binary's checksum is printed", d.Output);
        var printed = new[] { "rid", "runtime", "remoteRoot", "serviceUser", "snapshotBind", "snapshotUrl", "qualSnapshotBind", "stubWindmillBind" }
            .All(name => d.Output.Contains("  " + name + ": ", StringComparison.Ordinal));
        rows.Check(printed && !d.Output.Contains(C545Target, StringComparison.Ordinal) && d.Output.Contains("DEPLOY OK", StringComparison.Ordinal),
            "C545 DeployCommandLines the non-secret profile values are printed, the ssh target is not", d.Output);

        var identityFile = "C:" + "\\keys\\watchdog-deploy";
        var identity = NewC545DeployFx(C545Override("sshIdentityFile", identityFile));
        RunC545Deploy(identity, deploy: true).Result.ExitCode.ShouldBe(0);
        rows.Check(identity.Launches.Where(launch => launch.FileName is "ssh" or "scp")
                .All(launch => launch.Arguments[0] == "-i" && launch.Arguments[1] == identityFile && launch.Arguments[2] == "-o"),
            "C545 DeployCommandLines the identity file is passed to ssh and scp", string.Join(" || ", identity.Launches.Select(launch => launch.CommandText)));

        rows.ShouldAllPass(10);
    }

    [Test]
    public void C545_DeployStopsAtTheFirstFailure()
    {
        var rows = new NightlyCaseRows();

        var unstamped = NewC545DeployFx(git: new FakeGit("fatal: not a git repository (or any of the parent directories): .git\n", 128));
        var r = RunC545Deploy(unstamped, deploy: true);
        rows.Check(r.Result.ExitCode == 1 && r.Output.Contains("FAILED: git rev-parse HEAD failed; refusing an unstamped watchdog publish.", StringComparison.Ordinal)
            && unstamped.Launches.Count == 0 && unstamped.Http.Count == 0, "C545 DeployStopsAtTheFirstFailure an unreadable HEAD refuses the publish", r.Output);

        var publish = NewC545DeployFx();
        publish.ExitOf = launch => launch.FileName == "dotnet" ? 1 : 0;
        r = RunC545Deploy(publish, deploy: true);
        rows.Check(r.Result.ExitCode == 1 && r.Output.Contains("FAILED: publish failed", StringComparison.Ordinal) && publish.Ssh.Count == 0 && publish.Scp.Count == 0,
            "C545 DeployStopsAtTheFirstFailure a failed publish contacts nothing", r.Output);

        var mkdir = NewC545DeployFx();
        mkdir.ExitOf = launch => launch.FileName == "ssh" && launch.Arguments[^1].StartsWith("mkdir", StringComparison.Ordinal) ? 255 : 0;
        r = RunC545Deploy(mkdir, deploy: true);
        rows.Check(r.Result.ExitCode == 1 && r.Output.Contains("FAILED: create state directories failed", StringComparison.Ordinal) && mkdir.Scp.Count == 0 && mkdir.Http.Count == 0,
            "C545 DeployStopsAtTheFirstFailure a failed ssh step stops the deploy", r.Output);

        var upload = NewC545DeployFx();
        upload.ExitOf = launch => launch.FileName == "scp" && launch.Arguments[^1].EndsWith("/qual.json", StringComparison.Ordinal) ? 1 : 0;
        r = RunC545Deploy(upload, deploy: true);
        rows.Check(r.Result.ExitCode == 1 && r.Output.Contains("FAILED: upload qualification config failed", StringComparison.Ordinal) && upload.Ssh.Count == 1,
            "C545 DeployStopsAtTheFirstFailure a failed upload stops before the env file", r.Output);

        var snapshot = NewC545DeployFx();
        snapshot.SnapshotStatus = 503;
        r = RunC545Deploy(snapshot, deploy: true);
        rows.Check(r.Result.ExitCode == 1 && r.Output.Contains("FAILED: snapshot verification failed", StringComparison.Ordinal) && snapshot.Http.Count == 1,
            "C545 DeployStopsAtTheFirstFailure a snapshot that does not answer 200 fails the deploy", r.Output);

        var selfCheck = NewC545DeployFx();
        selfCheck.ExitOf = launch => launch.FileName == "ssh" ? 7 : 0;
        r = RunC545Deploy(selfCheck);
        rows.Check(r.Result.ExitCode == 0 && r.Output.Contains("Remote self-check exit: 7", StringComparison.Ordinal) && r.Output.Contains("PREFLIGHT OK", StringComparison.Ordinal),
            "C545 DeployStopsAtTheFirstFailure the preflight reports the self-check exit without failing", r.Output);

        rows.ShouldAllPass(6);
    }

    // The lookup order: --profile, the environment's profile, the existing default, exactly one *.local.json.
    [Test]
    public void C545_DeployProfileLookupOrder()
    {
        var rows = new NightlyCaseRows();
        string Used(C545DeployRun run) => Regex.Match(run.Output, "^Deploy profile: (.*)$", RegexOptions.Multiline).Groups[1].Value.TrimEnd('\r');

        var fx = NewC545DeployFx();
        var other = fx.Files.Path.Combine(fx.Root, "other.json");
        fx.Files.File.WriteAllText(other, C545Profile());
        var defaultPath = fx.Files.Path.Combine(fx.Root, "default.json");
        fx.Files.File.WriteAllText(defaultPath, C545Profile());
        rows.Check(Used(RunC545Deploy(fx, environmentProfile: other, defaultProfile: defaultPath)) == fx.ProfilePath, "C545 DeployProfileLookupOrder --profile first");
        rows.Check(Used(RunC545Deploy(fx, noProfileArg: true, environmentProfile: other, defaultProfile: defaultPath)) == other, "C545 DeployProfileLookupOrder then the environment's profile");
        rows.Check(Used(RunC545Deploy(fx, noProfileArg: true, defaultProfile: defaultPath)) == defaultPath, "C545 DeployProfileLookupOrder then the existing default");

        var local = fx.Files.Path.Combine(fx.RepoRoot, "scripts", "nightly-watchdog", "watchdog.local.json");
        fx.Files.File.WriteAllText(local, C545Profile());
        rows.Check(Used(RunC545Deploy(fx, noProfileArg: true)) == local, "C545 DeployProfileLookupOrder then the one *.local.json of the checkout");
        fx.Files.File.WriteAllText(fx.Files.Path.Combine(fx.RepoRoot, "scripts", "nightly-watchdog", "second.local.json"), C545Profile());
        var launches = fx.Launches.Count;
        var two = RunC545Deploy(fx, noProfileArg: true);
        rows.Check(two.Result.ExitCode == 3 && two.Output.Contains("no deploy profile", StringComparison.Ordinal) && fx.Launches.Count == launches,
            "C545 DeployProfileLookupOrder two *.local.json are no profile", two.Output);
        rows.ShouldAllPass(5);
    }

    // Every refusal names the key, never the value, and contacts nothing.
    [Test]
    [Arguments("serviceUser", "Ops", "serviceUser is not a valid user name")]
    [Arguments("runtime", "vm", "runtime must be native or container")]
    [Arguments("remoteRoot", "srv/antiphon-watchdog", "remoteRoot must be an absolute path without quotes or spaces")]
    [Arguments("remoteRoot", "/srv/antiphon watchdog", "remoteRoot must be an absolute path without quotes or spaces")]
    [Arguments("sshTarget", "watchdog-host", "sshTarget must be <user>@<host>")]
    [Arguments("snapshotBind", "watchdog-host", "snapshotBind must be host:port")]
    [Arguments("qualSnapshotBind", "LOCALHOST:17291", "qualSnapshotBind must be a private non-loopback address")]
    [Arguments("snapshotBind", "[::1]:17290", "snapshotBind must be a private non-loopback address")]
    [Arguments("snapshotBind", "*:17290", "snapshotBind must not be a wildcard for the native runtime")]
    [Arguments("snapshotUrl", null, "missing snapshotUrl")]
    [Arguments("stubWindmillBind", "<stub>", "placeholder value in stubWindmillBind")]
    public void C545_DeployProfileRefusals(string key, string? value, string reason)
    {
        var fx = NewC545DeployFx(C545Override(key, value));
        var r = RunC545Deploy(fx, deploy: true);
        r.Result.ExitCode.ShouldBe(3, r.Output);
        r.Output.ShouldContain("REFUSED: invalid deploy profile: " + reason);
        if (!string.IsNullOrEmpty(value) && value.Length > 3) r.Output.ShouldNotContain(value);
        fx.Launches.ShouldBeEmpty();
        fx.Git.Calls.ShouldBeEmpty();
    }

    [Test]
    public void C545_DeployProfileDefaults()
    {
        var check = NightlyWatchdogDeploy.Check(C545Profile(new Dictionary<string, string?>
        {
            ["rid"] = null, ["runtime"] = " ", ["qualSnapshotBind"] = null, ["stubWindmillBind"] = "",
        }));
        check.Ok.ShouldBeTrue(check.Reason);
        check.Profile!.Rid.ShouldBe("linux-x64");
        check.Profile.Runtime.ShouldBe("native");
        check.Profile.QualSnapshotBind.ShouldBe(C545Ip + ":17291");
        check.Profile.StubWindmillBind.ShouldBe("127.0.0.1:17292");

        var container = NightlyWatchdogDeploy.Check(C545Profile(new Dictionary<string, string?> { ["runtime"] = "container", ["snapshotBind"] = "0.0.0.0:17290", ["qualSnapshotBind"] = null }));
        container.Ok.ShouldBeTrue(container.Reason);
        container.Profile!.QualSnapshotBind.ShouldBe("0.0.0.0:17291");

        NightlyWatchdogDeploy.Check("[1, 2]").Reason.ShouldBe("deploy profile is not a JSON object");
        NightlyWatchdogDeploy.Check("{ not json").Reason.ShouldBe("deploy profile is not a JSON object");
        // Keys are matched exactly: a differently spelled key is absent.
        NightlyWatchdogDeploy.Check(C545Profile().Replace("\"snapshotUrl\"", "\"SnapshotUrl\"", StringComparison.Ordinal)).Reason.ShouldBe("missing snapshotUrl");
    }

    [Test]
    public void C545_DeployOptions()
    {
        var options = NightlyWatchdogDeployOptions.Parse(["--profile", "deploy.json", "--deploy"], "from-environment.json", "repo");
        options.ProfilePath.ShouldBe("deploy.json");
        options.Deploy.ShouldBeTrue();
        options.EnvironmentProfilePath.ShouldBe("from-environment.json");
        options.RepoRoot.ShouldBe("repo");
        options.DefaultProfilePath.ShouldBe(NightlyWatchdogDeployOptions.DefaultProfile);
        NightlyWatchdogDeployOptions.DefaultProfile.ShouldBe(@"C:\Antiphon\nightly\watchdog-deploy.json");
        var preflight = NightlyWatchdogDeployOptions.Parse([], null!, "repo");
        preflight.Deploy.ShouldBeFalse();
        preflight.ProfilePath.ShouldBe("");
        preflight.EnvironmentProfilePath.ShouldBe("");
        Should.Throw<NightlyUsageException>(() => NightlyWatchdogDeployOptions.Parse(["-Profile", "x"], "", "repo"));
        Should.Throw<NightlyUsageException>(() => NightlyWatchdogDeployOptions.Parse(["--profile"], "", "repo"));
    }

    private static readonly Regex C545Ipv4 = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b");
    private static readonly Regex C545SshTarget = new("[A-Za-z0-9_.-]+" + '@' + "[A-Za-z0-9_.-]+");
    private static readonly Regex C545HomePath = new("/" + "home/(?!<user>)[^\\s\"']*");

    /// <summary>The tracked watchdog files the host-agnostic scan reads (a *.local.json is never read).</summary>
    private static IEnumerable<string> C545TrackedWatchdogAssets()
    {
        var repo = DelegateScriptRunner.RepoRoot;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, "scripts", "nightly-watchdog"), "*", SearchOption.AllDirectories))
            if (!Path.GetFileName(file).EndsWith(".local.json", StringComparison.OrdinalIgnoreCase)) yield return file;
        yield return Path.Combine(repo, "scripts", "lib", "Antiphon.Nightly", "NightlyWatchdogDeploy.cs");
        yield return Path.Combine(repo, "scripts", "lib", "Antiphon.Scripts", "DeployNightlyWatchdogCommand.cs");
        yield return Path.Combine(repo, "tests", "Antiphon.Tests", "Scripts", "NightlyVerificationContractTests.C545Deploy.cs");
        yield return Path.Combine(repo, "docs", "nightly-watchdog.md");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(repo, "scripts", "windmill"), "antiphon-nightly-readiness*.json")) yield return file;
    }

    // No tracked file names the watchdog host, its addresses, its user or an absolute home path.
    [Test]
    public void C545_HostAgnosticAssets()
    {
        var rows = new NightlyCaseRows();
        var placeholderTarget = "<user>" + '@' + "<host>";
        var allowedTargets = new[] { placeholderTarget, "noreply" + '@' + "anthropic.com" };
        var ipHits = new List<string>();
        var targetHits = new List<string>();
        var homeHits = new List<string>();
        foreach (var file in C545TrackedWatchdogAssets())
        {
            if (!File.Exists(file)) { ipHits.Add("missing file " + file); continue; }
            var n = 0;
            foreach (var line in File.ReadAllLines(file))
            {
                n++;
                var where = Path.GetFileName(file) + ":" + n;
                foreach (Match m in C545Ipv4.Matches(line))
                    if (m.Value != "127.0.0.1" && m.Value != "0.0.0.0") ipHits.Add(where + " " + m.Value);
                foreach (Match m in C545SshTarget.Matches(line))
                {
                    if (m.Value.EndsWith('@' + "host.docker.internal", StringComparison.Ordinal)) continue;
                    if (line.Contains(placeholderTarget, StringComparison.Ordinal)) continue;
                    if (allowedTargets.Contains(m.Value)) continue;
                    targetHits.Add(where + " " + m.Value);
                }
                foreach (Match m in C545HomePath.Matches(line)) homeHits.Add(where + " " + m.Value);
            }
        }
        rows.Check(ipHits.Count == 0, "C545 HostAgnosticAssets no IPv4 literal", string.Join(" | ", ipHits));
        rows.Check(targetHits.Count == 0, "C545 HostAgnosticAssets no ssh-target token", string.Join(" | ", targetHits));
        rows.Check(homeHits.Count == 0, "C545 HostAgnosticAssets no absolute home path", string.Join(" | ", homeHits));
        rows.ShouldAllPass(3, "C545 HostAgnosticAssets no ssh-target token", "C545 HostAgnosticAssets no IPv4 literal", "C545 HostAgnosticAssets no absolute home path");
    }

    [Test]
    public void C545_DeployManifest()
    {
        var rows = new NightlyCaseRows();
        var repo = DelegateScriptRunner.RepoRoot;
        foreach (var asset in new[] { "antiphon-nightly-watchdog.service.template", "deploy.example.json", "env.example", "qual.example.json", "README.md" })
        {
            var path = Path.Combine(repo, "scripts", "nightly-watchdog", asset);
            var ascii = File.Exists(path) && File.ReadAllBytes(path).All(b => b <= 127);
            rows.Check(ascii, "C545 DeployManifest " + asset + " exists and is ASCII");
        }
        rows.Check(File.ReadAllText(Path.Combine(repo, ".gitignore")).Contains("scripts/nightly-watchdog/*.local.json", StringComparison.Ordinal),
            "C545 DeployManifest local profiles are gitignored");
        rows.ShouldAllPass(6);
    }
}
