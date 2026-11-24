using System.IO.Abstractions.TestingHelpers;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0604 S5, R-3: the Docker Desktop harness (the verify-card0604-dind-runner command, Antiphon.Nightly.DindRunnerHarness, which
/// replaced scripts/verify-card0604-dind-runner.ps1 in unit 47 of the 2026-11-22 plan) in process: docker, ssh-keygen and git are a
/// recording launcher that answers as a healthy image would (or as a test's fault says), the runner's HTTP a fake, the clock fake and
/// the files a mock file system. Nothing starts; the real observations are CP-4.
/// </summary>
[Category("Unit")]
public sealed class VerifyDindRunnerScriptTests
{
    private const string Image = "antiphon-session-testing:test";
    private const string Container = "c604-20261124T090000Z";
    private const string Volume = "c604-20261124T090000Z-dind";

    [Test]
    [Arguments(17202)]
    [Arguments(17203)]
    [Arguments(17204)]
    [Arguments(17205)]
    public void Production_port_is_refused(int port)
    {
        var world = new World();
        var run = world.Run("--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        run.Exit.ShouldBe(2);
        run.Lines.ShouldBe(["C604 HARNESS NOTE: refusing production port " + port + " (17202-17205 are the local stack).", "C604 HARNESS EXIT CODE: 2"]);
        world.Events.ShouldBeEmpty();
        world.Files.AllDirectories.ShouldNotContain(path => path.Contains("c604-harness", StringComparison.Ordinal));
        DindRunnerHarness.ForbiddenPorts.ShouldBe([17202, 17203, 17204, 17205]);
    }

    [Test]
    public void Image_is_required()
    {
        Should.Throw<NightlyUsageException>(() => DindRunnerOptions.Parse(["--port", "18298"], "")).Message.ShouldBe("missing --image");
        DindRunnerOptions.Parse(["--image", Image], "").Port.ShouldBe(18298);
    }

    [Test]
    public void A_healthy_image_grades_every_observation_ok()
    {
        var world = new World();
        var run = world.Run();
        run.Exit.ShouldBe(0, run.Output);
        run.Lines.ShouldBe([
            "C604 HARNESS: image=" + Image + " nested=yes egress=yes loopback=200 launchMs=1200 key=ok restart=ok refusal=ok containment=skipped claude=2.1.327 claudeAuth=logged-out",
            "C604 HARNESS EXIT CODE: 0"]);
        world.Evidence("graded.txt").ShouldBe(
            "claude=True\nclaudeAuth=True\negress=True\nkey=True\nlaunch=True\nloopback=True\nnested=True\nrefusal=True\nrestart=True\n");
        world.Evidence("post-sessions.txt").ShouldStartWith("status=201 elapsedMs=1200\n");
        world.Evidence("restart-summary.txt").ShouldBe("restarted=True backHealthy=True imagesRetained=True\nbefore=aaa,bbb\nafter=aaa,bbb\n");
        world.Evidence("docker-version.txt").ShouldBe("docker version --format {{.Server.Version}}\n--- exit 0 ---\n27.5.1\n\n");
        foreach (var name in new[] { "throwaway_key", "throwaway_key.pub", "throwaway_secret", "throwaway_gitconfig" })
            world.Files.FileExists(Path.Combine(world.EvidenceDir, name)).ShouldBeFalse(name + " is deleted at the end");
    }

    [Test]
    public void Harness_names_only_its_own_container()
    {
        var world = new World();
        world.Run().Exit.ShouldBe(0);
        var docker = world.Launches.Where(launch => launch.FileName == "docker").Select(launch => launch.Arguments.ToArray()).ToArray();
        docker.ShouldNotContain(arguments => arguments.Contains("prune") || arguments.Contains("down"));
        var removals = docker.Where(arguments => arguments.Contains("rm") && !arguments.Contains("--rm")).ToArray();
        removals.Select(arguments => string.Join(" ", arguments)).ShouldBe(["rm -f " + Container, "volume rm -f " + Volume]);
        var start = docker.Single(arguments => arguments[0] == "run" && arguments[1] == "-d");
        start.ShouldContain("--name");
        start[Array.IndexOf(start, "--name") + 1].ShouldBe(Container);
        start.ShouldContain("18298:8080");
        start.ShouldContain(Volume + ":/var/lib/docker");
        docker.First(arguments => arguments[0] == "volume").ShouldBe(["volume", "create", Volume]);
    }

    [Test]
    public void Compose_and_dockerfile_do_not_hardcode_a_key_path_value()
    {
        var compose = Read("docker-compose.server2-runner.yml");
        compose.ShouldContain("ANTIPHON_DEPLOY_KEY_FILE:?");
        compose.ShouldContain("PHONE_HOME_SECRET_FILE:?");
        // The private halves live only on server2; no compose or image file may name one.
        System.Text.RegularExpressions.Regex.IsMatch(compose, @"(?m)^\s*file:\s*/(?!\$)")
            .ShouldBeFalse("every secret source is interpolated, never a literal host path");

        var dockerfile = Read("docker/session-runner-grok/Dockerfile");
        System.Text.RegularExpressions.Regex.IsMatch(
            dockerfile, @"COPY\s+[^\n]*deploy[_-]?key",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ShouldBeFalse("the private key half is never baked into the image");
        dockerfile.ShouldNotContain("BEGIN OPENSSH PRIVATE KEY");

        // The harness mounts its own throwaway key and never a server2 secret.
        var world = new World();
        world.Run().Exit.ShouldBe(0);
        var start = world.Launches.Single(launch => launch.FileName == "docker" && launch.Arguments[0] == "run" && launch.Arguments[1] == "-d").Arguments;
        start.ShouldContain(Path.Combine(world.EvidenceDir, "throwaway_key") + ":/run/secrets/antiphon-deploy-key:ro");
        start.ShouldContain(Path.Combine(world.EvidenceDir, "throwaway_secret") + ":/run/secrets/phone-home:ro");
        world.Launches.SelectMany(launch => launch.Arguments).ShouldAllBe(argument => !argument.Contains("/home/mc/antiphon-server2", StringComparison.Ordinal)
            && !argument.Contains("antiphon-server2/secrets", StringComparison.Ordinal));
        var keygen = world.Launches.Single(launch => launch.FileName == "ssh-keygen").Arguments;
        keygen.ShouldBe(["-t", "ed25519", "-N", "", "-C", "c604-harness-throwaway", "-f", Path.Combine(world.EvidenceDir, "throwaway_key")]);
    }

    // CARD-0604 S9 / V-28 (cgroup v2 half). --containment runs the same in-image probe the server2 case runs on v1, as uid 1654,
    // and it is GRADED: an unmeasured containment fails the harness.
    [Test]
    public void Containment_switch_grades_the_v2_measurements()
    {
        var world = new World();
        var run = world.Run("--containment");
        run.Exit.ShouldBe(0, run.Output);
        run.Lines[0].ShouldContain(" containment=ok ");
        world.Evidence("graded.txt").ShouldContain("containment=True\n");
        world.Evidence("containment.txt").ShouldBe("advertised=True\nownership=True\ngrant=True\nprobe=True\nresidue=True\n");
        var probes = world.Launches.Where(launch => launch.Arguments.Contains("--user")).Select(launch => launch.Arguments.ToArray()).ToArray();
        probes.Length.ShouldBe(4);
        probes.ShouldAllBe(arguments => arguments[1] == "--user" && arguments[2] == "1654:1654" && arguments[3] == Container);
        probes[0][4..].ShouldBe(["curl", "-fsS", "http://127.0.0.1:8080/capabilities"]);
        probes[1][4..].ShouldBe(["/bin/sh", "-c", DindRunnerHarness.CustodyScript]);
        probes[2][4..].ShouldBe(["/usr/local/bin/antiphon-custody-containment-probe"]);
        probes[3][4..].ShouldBe(["bash", "-c", DindRunnerHarness.ResidueScript]);

        var windows = new World { Capabilities = "{\"backends\":[\"linux-cgroup-v1\",\"windows-job-v1\"]}" };
        var refused = windows.Run("--containment");
        refused.Exit.ShouldBe(1);
        refused.Lines[0].ShouldBe("C604 HARNESS NOTE: observations not ok: containment");
        windows.Evidence("containment.txt").ShouldStartWith("advertised=False\n");
    }

    [Test]
    [Arguments("\n")]
    [Arguments("\r\n")]
    public void Containment_residue_counts_subdirectories_only(string lineEnding)
    {
        // CARD-1614 counts executions inside namespaces, on v2 or both v1 controllers.
        var script = DindRunnerHarness.ResidueScript;
        script.ShouldContain("source /usr/local/lib/antiphon-custody-common.sh || exit 1");
        script.ShouldContain("ns=$(custody_read_namespace) || exit 1");
        foreach (var root in new[] { "/sys/fs/cgroup/antiphon-custody", "/sys/fs/cgroup/pids/antiphon-custody", "/sys/fs/cgroup/freezer/antiphon-custody" })
            script.ShouldContain("custody_count_execution_trees " + root + " \"$ns\" || exit 1");
        script.ShouldNotContain("ls -1 /sys/fs/cgroup/antiphon-custody");

        var common = Read("docker/session-runner-grok/antiphon-custody-common.sh").ReplaceLineEndings(lineEnding);
        var counter = common[common.IndexOf("custody_count_execution_trees()", StringComparison.Ordinal)..];
        counter.ShouldContain("[ -L \"$ns_path\" ] || [ ! -d \"$ns_path\" ]");
        counter.ShouldContain("[ -L \"$exec_path\" ] || [ ! -d \"$exec_path\" ]");
        counter.ShouldContain("procs=\"$exec_path/tree/cgroup.procs\"");

        // The grade: status=ok and current=0 on lines of their own, and no current, foreign or legacy count above zero. The
        // launcher hands output over as "\n"-ended lines (the script's Out-String joined them with CRLF on Windows, where these
        // (?m)...$ checks could never match).
        foreach (var (residue, ok) in new[]
        {
            ("status=ok\ncurrent=0\n", true),
            ("status=ok\ncurrent=0\ncontroller=freezer\nstatus=ok\ncurrent=0\n", true),
            ("status=ok\ncurrent=0\nforeign=2\n", false),
            ("status=ok\ncurrent=0\nlegacy=1\n", false),
            ("status=ok\ncurrent=3\n", false),
            ("status=read-error\ncurrent=0\n", false),
        })
        {
            var world = new World { Residue = residue };
            world.Run("--containment").Exit.ShouldBe(ok ? 0 : 1, residue);
        }
    }

    // CARD-0628 G-4: step 7b probes the pinned claude as the app uid against a fresh store it created, grades the signed-out
    // verdict, and hands the CLI no credential of any kind.
    [Test]
    public void Harness_probes_claude_as_the_app_uid_and_expects_logged_out()
    {
        var world = new World();
        world.Run().Exit.ShouldBe(0);
        var start = world.Launches.Single(launch => launch.FileName == "docker" && launch.Arguments[0] == "run" && launch.Arguments[1] == "-d").Arguments;
        start.ShouldContain("CLAUDE_CONFIG_DIR=/tmp/state/claude");
        start.ShouldContain("PhoneHome__ClaudeHome=/tmp/state/claude");
        string[] claudeEnv = ["exec", "-u", "1654:1654", "-e", "HOME=/home/app", "-e", "CLAUDE_CONFIG_DIR=/tmp/state/claude", "-e", "DISABLE_AUTOUPDATER=1", Container];
        world.Commands.ShouldContain("docker exec " + Container + " sh -c mkdir -p /tmp/state/claude && chown 1654:1654 /tmp/state/claude");
        world.Commands.ShouldContain("docker " + string.Join(" ", claudeEnv) + " claude --version");
        world.Commands.ShouldContain("docker " + string.Join(" ", claudeEnv) + " claude auth status --json");
        DindRunnerHarness.ClaudeVersion.ShouldBe("2.1.327");
        foreach (var name in new[] { "CLAUDE_CODE_OAUTH_TOKEN", "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "credentials.json" })
            world.Launches.SelectMany(launch => launch.Arguments).ShouldAllBe(argument => !argument.Contains(name, StringComparison.Ordinal), "the harness never passes " + name);

        var signedIn = new World { ClaudeAuthExit = 0 };
        var refused = signedIn.Run();
        refused.Exit.ShouldBe(1);
        refused.Lines[1].ShouldContain(" claude=2.1.327 claudeAuth=unexpected");
        var older = new World { ClaudeVersionText = "2.1.300 (Claude Code)\n" };
        older.Run().Lines[0].ShouldBe("C604 HARNESS NOTE: observations not ok: claude");
    }

    // CARD-1178. A fast HTTP 500 used to grade launch=True because the predicate was elapsed milliseconds. The grade is the
    // status; /tmp/state must be uid 1654's before the POST.
    [Test]
    public void Fast_http_500_does_not_grade_launch_true()
    {
        var fastError = new World { LaunchStatus = 500, LaunchBody = "UnauthorizedAccessException", LaunchElapsed = TimeSpan.FromMilliseconds(100) };
        var failed = fastError.Run();
        failed.Exit.ShouldBe(1);
        failed.Lines[0].ShouldBe("C604 HARNESS NOTE: observations not ok: launch");
        fastError.Evidence("post-sessions.txt").ShouldBe("status=500 elapsedMs=100\nUnauthorizedAccessException\n");

        new World { LaunchStatus = 200, LaunchElapsed = TimeSpan.FromSeconds(9) }.Run().Exit.ShouldBe(0, "a 2xx launch is accepted even when it is slower than 5000ms");
        new World { LaunchStatus = 300, LaunchElapsed = TimeSpan.FromMilliseconds(10) }.Run().Exit.ShouldBe(1, "only a 2xx status grades launch");

        var ordered = new World();
        ordered.Run();
        var prep = ordered.Events.IndexOf("EXEC docker exec " + Container + " sh -c mkdir -p /tmp/state && chown 1654:1654 /tmp/state");
        var post = ordered.Events.IndexOf("HTTP POST http://127.0.0.1:18298/sessions");
        prep.ShouldBeGreaterThanOrEqualTo(0, "the harness pre-creates /tmp/state for uid 1654");
        post.ShouldBeGreaterThan(prep, "/tmp/state is owned by uid 1654 before POST /sessions");
    }

    [Test]
    public void A_setup_failure_after_the_start_still_refuses_and_cleans_up()
    {
        var unhealthy = new World { Health = 503 };
        var run = unhealthy.Run();
        run.Exit.ShouldBe(2);
        run.Lines.ShouldBe([
            "C604 HARNESS NOTE: container never became healthy within 90s; see " + unhealthy.EvidenceDir + "/container-logs-unhealthy.txt",
            "C604 HARNESS: image=" + Image + " nested=unknown egress=unknown loopback=unknown launchMs=-1 key=unknown restart=unknown refusal=unknown containment=skipped claude=unknown claudeAuth=unknown",
            "C604 HARNESS EXIT CODE: 2"]);
        unhealthy.Commands.ShouldContain("docker logs " + Container);
        unhealthy.Commands.Count(command => command.StartsWith("docker run --rm", StringComparison.Ordinal)).ShouldBe(3, "the three refusals still run");
        unhealthy.Commands.ShouldContain("docker rm -f " + Container);
        unhealthy.Commands[^1].ShouldBe("docker volume rm -f " + Volume);
        unhealthy.Files.FileExists(Path.Combine(unhealthy.EvidenceDir, "throwaway_key")).ShouldBeFalse();

        var notStarted = new World { RunExit = 125 };
        notStarted.Run().Exit.ShouldBe(2);
        notStarted.Commands.ShouldNotContain("docker rm -f " + Container);
        notStarted.Commands.ShouldContain("docker volume rm -f " + Volume);
    }

    [Test]
    public void No_docker_daemon_stops_before_anything_else()
    {
        var world = new World { DockerVersionExit = 1 };
        var run = world.Run();
        run.Exit.ShouldBe(2);
        run.Lines[0].ShouldBe("C604 HARNESS NOTE: Docker daemon is not reachable. Start Docker Desktop and retry.");
        world.Commands.ShouldBe(["docker version --format {{.Server.Version}}"]);
    }

    [Test]
    public void Build_uses_the_checkout_head_in_the_foreground()
    {
        var world = new World();
        var run = world.Run("--build");
        run.Exit.ShouldBe(0, run.Output);
        run.Lines[0].ShouldBe("C604 HARNESS: building " + Image + " (session-testing) from " + FakeGit.DefaultSha);
        var build = world.Launches.Single(launch => launch.Arguments[0] == "build");
        build.Arguments.ShouldBe(["build", "-f", DindRunnerHarness.Dockerfile, "--target", "session-testing", "--build-arg", "SOURCE_REVISION=" + FakeGit.DefaultSha,
            "-t", Image, World.Checkout]);
        build.WorkingDirectory.ShouldBe(World.Checkout);
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    // A Docker Desktop that answers as a healthy session-testing image does, unless a field says otherwise.
    private sealed class World
    {
        public static readonly string Checkout = OperatingSystem.IsWindows() ? @"C:\c604" : "/c604";
        private int _inspects;

        public int DockerVersionExit { get; init; }
        public int RunExit { get; init; }
        public int Health { get; init; } = 200;
        public int LaunchStatus { get; init; } = 201;
        public string LaunchBody { get; init; } = "{\"sessionId\":\"s\"}";
        public TimeSpan LaunchElapsed { get; init; } = TimeSpan.FromMilliseconds(1200);
        public int ClaudeAuthExit { get; init; } = 1;
        public string ClaudeVersionText { get; init; } = "2.1.327 (Claude Code)\n";
        public string Capabilities { get; init; } = "{\"backends\":[\"linux-cgroup-v1\"]}";
        public string Residue { get; init; } = "status=ok\ncurrent=0\n";

        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-24T09:00:00Z"));
        public MockFileSystem Files { get; } = new();
        public List<string> Events { get; } = [];
        public List<NightlyLaunch> Launches { get; } = [];
        public string EvidenceDir => Path.Combine(Path.Combine(Checkout, ".antiphon/c604-harness"), "20261124T090000Z");
        public IReadOnlyList<string> Commands => Launches.Where(launch => launch.FileName == "docker").Select(launch => launch.CommandText).ToArray();

        public string Evidence(string name) => Files.File.ReadAllText(Path.Combine(EvidenceDir, name));

        public (int Exit, string Output, string[] Lines) Run(params string[] extra)
        {
            using var output = new StringWriter();
            var host = new NightlyHost
            {
                Clock = Clock,
                Git = new FakeGit().Run,
                Launcher = new RecordingLauncher(Answer),
                Processes = new FakeProcessTable(),
                Probe = new FakeHostProbe(),
                Out = output,
                Error = output,
                Trace = TextWriter.Null,
                Variables = _ => null,
                Sleep = Clock.Advance,
            };
            var seams = new DindRunnerSeams { Http = Http, Secret = () => new byte[32], SessionId = () => "c604-session" };
            var exit = DindRunnerHarness.Run(DindRunnerOptions.Parse(["--image", Image, .. extra], Checkout), host, Files, seams);
            var text = output.ToString().ReplaceLineEndings("\n");
            return (exit, text, text.TrimEnd('\n').Split('\n'));
        }

        private DindHttpAnswer Http(string method, string url, string? body, TimeSpan timeout)
        {
            Events.Add("HTTP " + method + " " + url);
            if (method == "GET") return new DindHttpAnswer(Health, "");
            Clock.Advance(LaunchElapsed);
            return new DindHttpAnswer(LaunchStatus, LaunchBody);
        }

        private NightlyLaunchResult Answer(NightlyLaunch launch)
        {
            Launches.Add(launch);
            Events.Add("EXEC " + launch.CommandText);
            if (launch.FileName == "ssh-keygen")
            {
                var key = launch.Arguments[^1];
                Files.File.WriteAllText(key, "private");
                Files.File.WriteAllText(key + ".pub", "public");
                return Ok("");
            }
            var a = launch.Arguments;
            var line = string.Join(" ", a);
            if (a[0] == "version") return new NightlyLaunchResult { ExitCode = DockerVersionExit, LogText = "27.5.1\n" };
            if (a[0] == "run" && a[1] == "-d") return new NightlyLaunchResult { ExitCode = RunExit, LogText = "container-id\n" };
            if (a[0] == "run" && a[1] == "--rm")
            {
                var reason = !line.Contains("antiphon-deploy-key:ro", StringComparison.Ordinal) ? "DeployKeyMissing"
                    : !line.Contains("phone-home:ro", StringComparison.Ordinal) ? "PhoneHomeSecretMissing" : "NotPrivileged";
                return new NightlyLaunchResult { ExitCode = 3, LogText = "refused: " + reason + "\n" };
            }
            if (a[0] == "inspect") return Ok(_inspects++ == 0 ? "0\n" : "1\n");
            if (a[0] != "exec") return Ok("");
            if (line.Contains("docker info", StringComparison.Ordinal)) return Ok("c604host overlay2 2\n");
            if (line.EndsWith(" hostname", StringComparison.Ordinal)) return Ok("c604host\n");
            if (line.Contains("api.github.com/zen", StringComparison.Ordinal)) return Ok("Keep it logically awesome.\n");
            if (line.Contains("nginx:alpine", StringComparison.Ordinal)) return Ok("200\n");
            if (line.Contains("claude --version", StringComparison.Ordinal)) return Ok(ClaudeVersionText);
            if (line.Contains("claude auth status", StringComparison.Ordinal))
                return new NightlyLaunchResult { ExitCode = ClaudeAuthExit, LogText = "{\n  \"loggedIn\": " + (ClaudeAuthExit == 1 ? "false" : "true") + "\n}\n" };
            if (line.Contains("stat -c %u %a", StringComparison.Ordinal)) return Ok("1654 400\n");
            if (line.Contains("ssh -F", StringComparison.Ordinal)) return Ok("user app\nhostname ssh.github.com\nport 443\nidentitiesonly yes\nidentityfile /run/antiphon/deploy-key\n");
            if (line.Contains("docker images -q", StringComparison.Ordinal)) return Ok("bbb\naaa\n");
            if (line.Contains("/capabilities", StringComparison.Ordinal)) return Ok(Capabilities + "\n");
            if (line.Contains("sudo -n -l", StringComparison.Ordinal))
                return Ok("root:root:755\nroot:root:755\nroot:root:440\n(root) NOPASSWD: /usr/local/bin/antiphon-custody-enter, /usr/local/bin/antiphon-custody-kill\n");
            if (line.Contains("containment-probe", StringComparison.Ordinal)) return Ok("containment=ok\n");
            if (line.Contains("custody_count_execution_trees", StringComparison.Ordinal)) return Ok(Residue);
            return Ok("");
        }

        private static NightlyLaunchResult Ok(string text) => new() { ExitCode = 0, LogText = text };
    }
}
