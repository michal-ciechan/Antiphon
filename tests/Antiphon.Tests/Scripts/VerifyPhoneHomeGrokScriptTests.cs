using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0490 V-7: the isolated phone-home Grok harness. Since unit 36 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md
/// it is the verify-phone-home-grok command (Antiphon.Nightly.PhoneHomeGrokVerify; scripts/verify-phone-home-grok.ps1 is
/// deleted) and these rows run it in process on a fake platform: a temporary checkout holding copies of the compose file, the
/// Grok Dockerfile and the card0490 fixtures (the run roots under its .antiphon, never this checkout's), scripted loopback
/// ports, tailscale, docker, dotnet, the isolated processes and HTTP. Nothing is built, started or contacted. The live rows walk
/// the whole canary on the fakes (Postgres, the isolated runner and server, the standing agent, the compose bring-up, the
/// dispatchEligible hold, the turn and its evidence, the clean-up).
/// </summary>
[Category("Integration")]
public sealed class VerifyPhoneHomeGrokScriptTests
{
    private static readonly int[] Forbidden = [17202, 17203, 17204, 17205];

    /// <summary>A fake machine over a temporary checkout and a temporary primary GROK_HOME.</summary>
    private sealed class World : IPhoneHomeGrokPlatform, IDisposable
    {
        private readonly NightlyFixture _fixture = NightlyFixture.Create("card0490-live");
        private int _nextPort = 41001;

        public World(bool primaryAuth = true)
        {
            foreach (var relative in new[] { "docker-compose.runner-grok.yml", "docker/session-runner-grok/Dockerfile", "tests/fixtures/card0490-linux/assets.lock.json",
                         "tests/fixtures/card0490-linux/card0490-live.example.json" })
            {
                var target = _fixture.PathOf("checkout", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, relative), target);
            }
            Directory.CreateDirectory(Primary);
            if (primaryAuth) File.WriteAllText(Path.Combine(Primary, "auth.json"), """{"x":{"key":"not-a-real-token","expires_at":9999999999}}""");
            Directory.CreateDirectory(Path.Combine(Checkout, "src", "Antiphon.Shared", "bin-card0490-v7-runner"));
        }

        public string Checkout => _fixture.PathOf("checkout");
        public string Primary => _fixture.PathOf("grok-primary");
        public string Config => _fixture.PathOf("live", "card0490-live.json");
        public string Evidence => _fixture.PathOf("evidence");
        public IFileSystem Files { get; } = new FileSystem();
        public Queue<int> Ports { get; } = new();
        public List<(string File, string[] Arguments, IReadOnlyDictionary<string, string>? Environment, bool Capture)> Runs { get; } = [];
        public Func<string, string[], PhoneHomeGrokRun> Answer { get; set; } = (_, _) => new PhoneHomeGrokRun(0, "", "");
        public List<(string Dll, string Directory, IReadOnlyDictionary<string, string> Environment, Child Child)> Dlls { get; } = [];
        public List<(string Method, string Url, string? Body)> Requests { get; } = [];
        public Func<string, string, string?, string> Http { get; set; } = (method, url, _) => throw new PhoneHomeGrokHttpException("404 " + method + " " + url);
        public Func<string, int> Health { get; set; } = _ => 200;
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero));
        public StringWriter Out { get; } = new();
        public StringWriter Error { get; } = new();
        public FakeGit Git { get; } = new();

        public int FreeLoopbackPort() => Ports.Count > 0 ? Ports.Dequeue() : _nextPort++;

        public PhoneHomeGrokRun Run(string file, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment, bool capture)
        {
            var copy = arguments.ToArray();
            Runs.Add((file, copy, environment == null ? null : new Dictionary<string, string>(environment), capture));
            return Answer(file, copy);
        }

        public int? ExitedAtStart { get; init; }

        public IPhoneHomeGrokChild StartDll(string dll, string workingDirectory, IReadOnlyDictionary<string, string> environment)
        {
            var child = new Child(9000 + Dlls.Count) { HasExited = ExitedAtStart != null, ExitCode = ExitedAtStart ?? 0 };
            Dlls.Add((dll, workingDirectory, new Dictionary<string, string>(environment), child));
            return child;
        }

        public int Status(string url, int timeoutSec) => Health(url);

        public string Request(string method, string url, string? jsonBody, int timeoutSec)
        {
            Requests.Add((method, url, jsonBody));
            return Http(method, url, jsonBody);
        }

        public int Verify(params string[] arguments)
        {
            var host = new NightlyHost
            {
                Clock = Clock, Git = Git.Run, Launcher = new RecordingLauncher(), Processes = new FakeProcessTable(), Probe = new FakeHostProbe(), Out = Out,
                Error = Error, Trace = TextWriter.Null, Sleep = Clock.Advance,
                Variables = name => name == "GROK_HOME" ? Primary : name == "USERPROFILE" ? _fixture.PathOf("profile") : null!,
            };
            var options = PhoneHomeGrokVerifyOptions.Parse(["--configuration-file", Config, "--evidence-root", Evidence, .. arguments], Checkout, _fixture.Root);
            return PhoneHomeGrokVerify.Run(options, host, this);
        }

        public JsonElement ConfigJson => JsonDocument.Parse(File.ReadAllText(Config)).RootElement;

        public string Text => Out.ToString() + Error;

        public void Dispose() => _fixture.Dispose();

        internal sealed class Child(int id) : IPhoneHomeGrokChild
        {
            public int Id => id;
            public bool HasExited { get; set; }
            public int ExitCode { get; set; }
            public bool Stopped { get; private set; }
            public void Stop() => Stopped = true;
            public void Dispose() { }
        }
    }

    [Test]
    public void Compose_and_dockerfile_do_not_hardcode_server_origin()
    {
        var compose = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "docker-compose.runner-grok.yml"));
        compose.ShouldContain("PHONE_HOME_SERVER_ORIGIN");
        compose.ShouldNotContain("PhoneHome__ServerOrigin: http");
        compose.ShouldNotContain("host.docker.internal:17202");
        compose.ShouldNotContain("host.docker.internal:17203");
        compose.ShouldNotContain("host.docker.internal:17204");
        compose.ShouldNotContain("host.docker.internal:17205");
        compose.ShouldContain("PHONE_HOME_GROK_HOME");
        compose.ShouldContain("PHONE_HOME_GROK_SESSIONS");
        compose.ShouldContain("SessionRunner__PtyHostDir");
        compose.ShouldContain("/tmp/antiphon-pty-hosts");
        System.Text.RegularExpressions.Regex.IsMatch(
            compose,
            @"PHONE_HOME_GROK_HOME[\s\S]*?read_only:\s*true",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ShouldBeTrue("throwaway GROK_HOME must be a read-only bind");

        var dockerfile = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "docker/session-runner-grok/Dockerfile"));
        System.Text.RegularExpressions.Regex.IsMatch(
            dockerfile, @"^\s*ENV\s+PhoneHome__ServerOrigin\b",
            System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ShouldBeFalse();
        dockerfile.ShouldNotContain("17202");
        dockerfile.ShouldContain("1.0.41");
        dockerfile.ShouldContain("https://x.ai/cli/grok-${GROK_VERSION}-linux-x86_64");
        dockerfile.ShouldNotContain("x.ai/cli/install.sh");
        System.Text.RegularExpressions.Regex.IsMatch(
            dockerfile, @"COPY\s+[^\n]*auth\.json",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ShouldBeFalse();

        // The command refuses a compose file that names the origin or a production port before it writes anything.
        using var world = new World();
        File.WriteAllText(Path.Combine(world.Checkout, "docker-compose.runner-grok.yml"), compose.Replace("${PHONE_HOME_SERVER_ORIGIN:?", "http://x ${PHONE_HOME_SERVER_ORIGIN:?"));
        world.Verify("--write-config-only").ShouldBe(1, world.Text);
        world.Error.ToString().ShouldContain("hardcodes PhoneHome__ServerOrigin");
        File.Exists(world.Config).ShouldBeFalse();
    }

    [Test]
    public void WriteConfigOnly_allocates_isolated_ports_and_refuses_production()
    {
        using var world = new World(primaryAuth: false);
        var exit = world.Verify("--write-config-only");
        exit.ShouldBe(2, "the OAuth copy is missing, a container blocker: " + world.Text);
        File.Exists(world.Config).ShouldBeTrue(world.Text);
        var json = world.ConfigJson;
        var serverPort = json.GetProperty("serverPort").GetInt32();
        var postgresPort = json.GetProperty("postgresPort").GetInt32();
        var runnerPort = json.GetProperty("runnerPort").GetInt32();
        (serverPort, postgresPort, runnerPort).ShouldBe((41001, 41002, 41003));
        Forbidden.ShouldNotContain(serverPort);
        json.GetProperty("phoneHomeServerOrigin").GetString().ShouldBe($"http://host.docker.internal:{serverPort}");
        json.GetProperty("serverOrigin").GetString().ShouldBe($"http://127.0.0.1:{serverPort}");
        json.GetProperty("localRunnerOrigin").GetString().ShouldBe($"http://127.0.0.1:{runnerPort}");
        json.TryGetProperty("sharedSecret", out _).ShouldBeFalse();
        json.GetProperty("image").GetProperty("grokVersion").GetString().ShouldBe(PhoneHomeGrokVerify.GrokVersion);
        json.GetProperty("standingAgentId").GetString().ShouldNotBeNullOrWhiteSpace();
        var runRoot = Path.Combine(world.Checkout, ".antiphon", "card0490-live-" + json.GetProperty("runId").GetString());
        File.ReadAllText(Path.Combine(runRoot, "phone-home.secret")).ShouldMatch("^[0-9a-f]{64}$");
        world.Text.ShouldContain("PhoneHome__ServerOrigin");
        world.Text.ShouldContain("Remaining V-7 container blockers:");
        File.ReadAllText(Path.Combine(world.Evidence, "blockers.txt")).ShouldContain("Grok OAuth copy missing");
        world.Runs.ShouldBeEmpty("nothing was run");

        // A forbidden port from the allocator is refused.
        using var unlucky = new World();
        unlucky.Ports.Enqueue(17204);
        unlucky.Verify("--write-config-only").ShouldBe(1);
        unlucky.Error.ToString().ShouldContain("Allocated a forbidden production port 17204.");
    }

    [Test]
    public void WriteConfigOnly_copies_primary_auth_into_throwaway_home()
    {
        using var world = new World();
        File.WriteAllText(Path.Combine(world.Primary, "auth.json.lock"), "lock");
        world.Verify("--write-config-only").ShouldBe(0, world.Text);
        var json = world.ConfigJson;
        var mount = json.GetProperty("oauthMount").GetString().ShouldNotBeNull();
        Path.GetFullPath(mount).ShouldNotBe(Path.GetFullPath(world.Primary));
        File.Exists(Path.Combine(mount, "auth.json")).ShouldBeTrue(world.Text);
        File.Exists(Path.Combine(mount, "auth.json.lock")).ShouldBeFalse("never the live lock");
        Directory.Exists(Path.Combine(mount, "sessions")).ShouldBeTrue();
        world.Text.ShouldNotContain("not-a-real-token");
        world.Text.ShouldContain("copiedAuth=True");
        json.GetProperty("grokSessionsMount").GetString().ShouldNotBeNullOrWhiteSpace();
        json.GetProperty("grokHomeSource").GetString().ShouldBe(Path.GetFullPath(world.Primary));
    }

    [Test]
    public void Server2_placement_uses_desktop_tailscale_not_docker_internal()
    {
        using var world = new World();
        world.Answer = (file, arguments) => file == "tailscale" ? new PhoneHomeGrokRun(0, "100.64.7.9\nfd7a::1\n", "") : new PhoneHomeGrokRun(0, "", "");
        world.Verify("--placement", "Server2", "--write-config-only").ShouldBe(0, world.Text);
        var json = world.ConfigJson;
        json.GetProperty("placement").GetString().ShouldBe("server2");
        var origin = json.GetProperty("phoneHomeServerOrigin").GetString().ShouldNotBeNull();
        origin.ShouldNotContain("host.docker.internal");
        origin.ShouldBe("http://100.64.7.9:" + json.GetProperty("serverPort").GetInt32());
        json.GetProperty("serverOrigin").GetString().ShouldContain("127.0.0.1");
        world.Runs.ShouldHaveSingleItem().Arguments.ShouldBe(["ip", "-4"]);

        // tailscale that cannot answer: the known desktop address.
        using var offline = new World();
        offline.Answer = (file, _) => throw new System.ComponentModel.Win32Exception("tailscale not found");
        offline.Verify("--placement", "server2", "--write-config-only").ShouldBe(0, offline.Text);
        offline.ConfigJson.GetProperty("phoneHomeServerOrigin").GetString().ShouldMatch(@"^http://100\.79\.51\.37:\d+$");
    }

    [Test]
    public void Configuration_naming_production_port_is_refused()
    {
        using var world = new World();
        Directory.CreateDirectory(Path.GetDirectoryName(world.Config)!);
        File.WriteAllText(world.Config, """{"serverPort":17202}""");
        world.Verify("--write-config-only").ShouldNotBe(0);
        world.Text.ShouldContain("17202");
        world.Runs.ShouldBeEmpty();

        // An existing configuration's other values are kept (an empty or zero value is not).
        using var merged = new World();
        Directory.CreateDirectory(Path.GetDirectoryName(merged.Config)!);
        File.WriteAllText(merged.Config, """{"runnerPort":0,"runnerId":"","standingAgentId":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb","note":"kept"}""");
        merged.Verify("--write-config-only").ShouldBe(0, merged.Text);
        var json = merged.ConfigJson;
        (json.GetProperty("runnerPort").GetInt32(), json.GetProperty("runnerId").GetString(), json.GetProperty("standingAgentId").GetString(), json.GetProperty("note").GetString())
            .ShouldBe((41003, "grok-linux", "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "kept"));
    }

    // ---- the live canary on the fakes ----

    private const string StandingAgent = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    private static void Canary(World world)
    {
        string? nonce = null;
        world.Http = (method, url, body) =>
        {
            if (method == "GET" && url.EndsWith("/api/agents/" + StandingAgent, StringComparison.Ordinal))
                return "{\"id\":\"" + StandingAgent + "\",\"kind\":\"Grok\",\"working\":false,\"workingDirectory\":\"w\","
                    + "\"liveSession\":{\"id\":\"s1\",\"status\":\"Running\",\"startedAt\":\"2026-11-24T10:00:05Z\"}}";
            if (method == "PATCH") return "{\"id\":\"" + StandingAgent + "\",\"kind\":\"Grok\"}";
            if (method == "GET" && url.EndsWith("/api/session-runners/grok-linux/status", StringComparison.Ordinal))
                return "{\"dispatchEligible\":true,\"available\":true,\"runnerStoreId\":\"store-1\",\"processBootId\":\"boot-1\",\"epoch\":1}";
            if (method == "POST" && url.EndsWith("/start", StringComparison.Ordinal))
                return "{\"liveSession\":{\"id\":\"s1\",\"startedAt\":\"2026-11-24T10:00:05Z\"}}";
            if (method == "POST" && url.EndsWith("/api/sessions/s1/messages", StringComparison.Ordinal))
            {
                nonce = JsonNode.Parse(body!)!["body"]!.GetValue<string>().Split(' ')[2];
                return "{\"messages\":[{\"id\":\"q0\"},{\"id\":\"q1\"}]}";
            }
            if (method == "GET" && url.EndsWith("/api/sessions/s1/transcript", StringComparison.Ordinal))
                return "{\"entries\":[{\"kind\":\"UserPrompt\",\"text\":\"Reply with " + nonce + " and do not use tools.\",\"uuid\":\"u1\",\"sequence\":1,\"timestamp\":\"2026-11-24T10:00:06Z\"},"
                    + "{\"kind\":\"AssistantText\",\"text\":\"" + nonce + "\",\"uuid\":\"u2\",\"sequence\":2},{\"kind\":\"TurnEnd\",\"uuid\":\"u3\",\"sequence\":3}]}";
            if (method == "POST" && url.EndsWith("/stop", StringComparison.Ordinal)) return "{}";
            throw new PhoneHomeGrokHttpException("404 " + method + " " + url);
        };
        world.Answer = (file, arguments) => file == "docker" && arguments.Contains("images")
            ? new PhoneHomeGrokRun(0, "sha256:abc\n", "")
            : file == "docker" && arguments.Contains("--version") ? new PhoneHomeGrokRun(0, "grok 1.0.41\n", "") : new PhoneHomeGrokRun(0, "", "");
    }

    [Test]
    public void Script_uses_isolated_local_runner_and_fresh_first_start()
    {
        using var world = new World();
        Canary(world);
        world.Verify().ShouldBe(0, world.Text);
        var json = world.ConfigJson;
        var runnerOrigin = json.GetProperty("localRunnerOrigin").GetString()!;

        // The isolated runner (never 17204, never the dead port 1) and the server pointed at it.
        world.Dlls.Count.ShouldBe(2);
        var runner = world.Dlls[0];
        runner.Dll.ShouldBe(Path.Combine(world.Checkout, "src", "Antiphon.SessionRunner", "bin-card0490-v7-runner", "Antiphon.SessionRunner.dll"));
        runner.Environment["ASPNETCORE_URLS"].ShouldBe(runnerOrigin);
        runner.Environment["PhoneHome__Enabled"].ShouldBe("false");
        runnerOrigin.ShouldNotEndWith(":17204");
        runnerOrigin.ShouldNotBe("http://127.0.0.1:1");
        var server = world.Dlls[1].Environment;
        server["SessionRunner__BaseUrl"].ShouldBe(runnerOrigin);
        server["PhoneHomeRunner__AllowDelegatedTasks"].ShouldBe("false");
        server["PhoneHomeRunner__SharedSecret"].ShouldMatch("^[0-9a-f]{64}$");
        server["PhoneHomeRunner__CallbackOrigin"].ShouldBe(json.GetProperty("phoneHomeServerOrigin").GetString());

        // The compose project gets the origin at launch; the version probe runs as uid 1654; the first start is fresh.
        var up = world.Runs.Single(run => run.Arguments.Contains("up"));
        up.Environment.ShouldNotBeNull()["PHONE_HOME_SERVER_ORIGIN"].ShouldBe(json.GetProperty("phoneHomeServerOrigin").GetString());
        up.Arguments.ShouldContain("--build");
        world.Runs.ShouldContain(run => string.Join(" ", run.Arguments).Contains("exec -T --user 1654:1654 session-runner-grok grok --version", StringComparison.Ordinal));
        world.Requests.Single(request => request.Url.EndsWith("/start", StringComparison.Ordinal)).Body.ShouldBe("{\"fresh\":true}");
        var message = world.Requests.Single(request => request.Url.EndsWith("/messages", StringComparison.Ordinal)).Body!;
        message.ShouldContain("PHONE_HOME_OK_");
        message.ShouldContain("\"mode\":\"WhenIdle\"");

        // The evidence, then everything this run started is gone.
        var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(world.Evidence, "v7-turn.json"))).RootElement;
        (evidence.GetProperty("sessionId").GetString(), evidence.GetProperty("queueId").GetString(), evidence.GetProperty("grokVersion").GetString(),
            evidence.GetProperty("imageDigest").GetString(), evidence.GetProperty("epoch").GetInt32()).ShouldBe(("s1", "q1", "grok 1.0.41", "sha256:abc", 1));
        evidence.GetProperty("sourceSha").GetString().ShouldBe(FakeGit.DefaultSha);
        evidence.GetProperty("assistantText").GetProperty("hasNonce").GetBoolean().ShouldBeTrue();
        world.Text.ShouldContain("V-7 turn confirmed.");
        world.Requests.ShouldContain(request => request.Method == "POST" && request.Url.EndsWith("/stop", StringComparison.Ordinal));
        world.Runs.ShouldContain(run => run.Arguments.Contains("down") && run.Arguments.Contains("--remove-orphans"));
        world.Runs.ShouldContain(run => run.Arguments.SequenceEqual(new[] { "rm", "-f", "antiphon-card0490-pg-" + json.GetProperty("runId").GetString() }));
        world.Dlls.ShouldAllBe(dll => dll.Child.Stopped);
        Directory.Exists(Path.Combine(world.Checkout, "src", "Antiphon.Shared", "bin-card0490-v7-runner")).ShouldBeFalse("every bin-card0490-v7* output goes");
        world.Runs.ShouldNotContain(run => run.File == "netsh", "local-docker opens no firewall rule");
    }

    [Test]
    public void A_runner_that_never_answers_or_exits_early_fails_and_still_cleans_up()
    {
        using var silent = new World();
        Canary(silent);
        silent.Health = _ => 0;
        silent.Verify("--skip-container").ShouldBe(1, silent.Text);
        silent.Error.ToString().ShouldContain("Isolated local SessionRunner /health did not return 200 on http://127.0.0.1:41003/health.");
        silent.Runs.ShouldContain(run => run.Arguments.Length > 1 && run.Arguments[0] == "rm" && run.Arguments[1] == "-f");
        silent.Runs.ShouldNotContain(run => run.Arguments.Contains("down"), "the compose variables were never set");
        silent.Dlls.ShouldHaveSingleItem().Child.Stopped.ShouldBeTrue();

        using var crashed = new World { ExitedAtStart = 3 };
        Canary(crashed);
        crashed.Health = _ => 0;
        crashed.Verify("--skip-container").ShouldBe(1, crashed.Text);
        crashed.Error.ToString().ShouldContain("Isolated local SessionRunner exited early (code 3).");
        crashed.Dlls.ShouldHaveSingleItem().Child.Stopped.ShouldBeFalse("an exited child is not stopped again");

        using var refused = new World();
        refused.Answer = (file, arguments) => new PhoneHomeGrokRun(arguments[0] == "run" ? 125 : 0, "", "");
        refused.Verify().ShouldBe(1);
        refused.Error.ToString().ShouldContain("docker run postgres failed (exit 125).");
        refused.Runs.ShouldNotContain(run => run.Arguments[0] == "rm", "no Postgres container was started");
    }

    [Test]
    public void Server2_live_run_opens_and_removes_the_tailscale_firewall_rule()
    {
        using var world = new World();
        Canary(world);
        var inner = world.Answer;
        world.Answer = (file, arguments) => file == "tailscale" ? new PhoneHomeGrokRun(0, "100.64.7.9\n", "") : inner(file, arguments);
        world.Verify("--placement", "server2").ShouldBe(0, world.Text);
        var port = world.ConfigJson.GetProperty("serverPort").GetInt32();
        var netsh = world.Runs.Where(run => run.File == "netsh").Select(run => string.Join(" ", run.Arguments)).ToArray();
        netsh.ShouldBe([
            $"advfirewall firewall add rule name=CARD-0490-v7-{port} dir=in action=allow protocol=TCP localport={port} remoteip=100.64.0.0/10",
            $"advfirewall firewall delete rule name=CARD-0490-v7-{port}",
        ]);
        world.Runs.ShouldNotContain(run => run.Arguments.Contains("up"), "server2's container starts on server2");
        world.Text.ShouldContain("server2 placement: start the container on server2 with PHONE_HOME_SERVER_ORIGIN=http://100.64.7.9:" + port);
        File.Exists(Path.Combine(world.Evidence, "v7-turn.json")).ShouldBeFalse("no turn without the local container");
    }

    [Test]
    public void Options_refuse_an_unknown_placement_and_require_the_evidence_root()
    {
        Should.Throw<NightlyUsageException>(() => PhoneHomeGrokVerifyOptions.Parse(["--evidence-root", "e", "--placement", "moon"], "r", "w"));
        Should.Throw<NightlyUsageException>(() => PhoneHomeGrokVerifyOptions.Parse([], "r", "w"));
        var options = PhoneHomeGrokVerifyOptions.Parse(["--evidence-root", "e"], "r", "w");
        (options.ConfigurationFile, options.Placement, options.HoldSeconds, options.TurnTimeoutSeconds).ShouldBe((".antiphon/card0490-live.json", (string?)null, 180, 600));
    }
}
