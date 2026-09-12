using System.Management;
using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.FakeLlmApi;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Agents;

/// <summary>CARD-0497 interactive canaries. Opt-in: <c>ANTIPHON_REAL_CLI_STUB_TESTS=1</c>.</summary>
[Explicit]
[Category("RealCliStubProxy")]
[NotInParallel("RealCliStubProxy")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Integration")]
public sealed class CodexCommandLengthSessionTests
{
    private const int PerTestBudgetSeconds = 180;

    [Test]
    [Timeout(PerTestBudgetSeconds * 1000)]
    public async Task Incident_fixture_launches_through_node_and_delivers_the_whole_developer_block(
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        await RunInteractiveAsync(
            CodexInstructionFixtures.ShortControl,
            CodexInstructionFixtures.Incident,
            ptyBackend: "modern",
            compareBaseInstructions: true);
    }

    [Test]
    [Timeout(PerTestBudgetSeconds * 1000)]
    public async Task Near_cap_fixture_launches_and_arrives_intact(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var nearCap = FitHop2Payload(29_990);
        await RunInteractiveAsync(developer: nearCap, ptyBackend: "modern");
    }

    [Test]
    [Timeout(PerTestBudgetSeconds * 1000)]
    public async Task Incident_fixture_launches_on_the_inbox_backend(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        await RunInteractiveAsync(developer: CodexInstructionFixtures.Incident, ptyBackend: "inbox");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Legacy_batch_launcher_still_dies_on_the_incident_fixture()
    {
        RealCliStubGate.SkipIfNotEligible(AgentKind.Codex);
        var shim = NpmShimPath();
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var control = new string('x', 6_700);
        var controlResult = await RealCliStubProcess.RunAsync(
            cmd,
            ["/d", "/c", $"{Quote(shim)} --version -c developer_instructions={control}"],
            new Dictionary<string, string>(),
            TimeSpan.FromSeconds(20));
        controlResult.ExitCode.ShouldBe(0, controlResult.Combined);

        var fail = await RealCliStubProcess.RunAsync(
            cmd,
            ["/d", "/c", $"{Quote(shim)} --version -c developer_instructions={CodexInstructionFixtures.Incident}"],
            new Dictionary<string, string>(),
            TimeSpan.FromSeconds(20));
        fail.ExitCode.ShouldBe(1);
        fail.Combined.ShouldContain("The command line is too long");
    }

    private static async Task RunInteractiveAsync(
        string? controlDeveloper = null,
        string? developer = null,
        string ptyBackend = "modern",
        bool compareBaseInstructions = false)
    {
        RealCliStubGate.SkipIfNotEligible(AgentKind.Codex);
        var shim = NpmShimPath();
        developer ??= CodexInstructionFixtures.Incident;

        string? controlInstructions = null;
        if (controlDeveloper is not null)
            controlInstructions = await LaunchOnceAsync(shim, controlDeveloper, ptyBackend, compareFixture: controlDeveloper);

        var incidentInstructions = await LaunchOnceAsync(shim, developer, ptyBackend, compareFixture: developer);
        if (compareBaseInstructions && controlInstructions is not null)
            controlInstructions.ShouldBe(incidentInstructions);
    }

    private static async Task<string?> LaunchOnceAsync(
        string shim, string developer, string ptyBackend, string compareFixture)
    {
        var nonce = $"STUBCANARY-{Guid.NewGuid():N}";
        var reply = $"STUBREPLY-{Guid.NewGuid():N}";
        var syntheticKey = $"stub-codex-{Guid.NewGuid():N}";
        var tempRoot = Path.Combine(Path.GetTempPath(), $"c0497-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var cwd = Path.Combine(tempRoot, "cwd");
        Directory.CreateDirectory(cwd);
        var logs = Path.Combine(tempRoot, "logs");
        Directory.CreateDirectory(logs);
        var codexHome = Path.Combine(tempRoot, "codex-home");
        Directory.CreateDirectory(codexHome);

        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Codex = true });
        stub.Script.SetDefault(StubEndpointKeys.CodexResponses, new ScriptedTextTurn(reply));
        var overlay = RealCliStubEnv.ForCodex(stub.BaseUrl, syntheticKey, codexHome);
        var env = new Dictionary<string, string>(overlay.Env, StringComparer.OrdinalIgnoreCase)
        {
            ["TERM"] = "xterm-256color",
        };

        var args = new List<string>
        {
            "--no-alt-screen",
            "--dangerously-bypass-approvals-and-sandbox",
        };
        args.AddRange(overlay.Args);
        args.AddRange(["-c", "disable_paste_burst=true"]);
        args.AddRange(["-c", "developer_instructions=" + developer]);

        var options = Options.Create(new AgentRegistrySettings
        {
            DefaultDefinition = "codex",
            Definitions = { ["codex"] = new AgentDefinition { Kind = "Codex", Exe = shim } },
            CodexReadyQuietPeriodMs = 1_000,
            CodexReadyMaxWaitMs = 60_000,
            CodexDoneQuietPeriodMs = 3_000,
            CodexDoneMaxWaitMs = 90_000,
        });

        await using var client = new DirectSessionRunnerClient(logs, ptyBackend: ptyBackend, codexTranscript: true);
        var adapter = new RunnerCodexAdapter(client, options);
        var sessionId = Guid.NewGuid();
        try
        {
            await adapter.StartAsync(
                new AgentLaunchSpec(
                    "codex",
                    AgentKind.Codex,
                    shim,
                    args,
                    env,
                    cwd,
                    120,
                    30,
                    SessionId: sessionId),
                CancellationToken.None);

            var pid = adapter.Pid ?? throw new InvalidOperationException("runner DTO has no child pid");
            var (name, commandLine) = QueryProcess(pid);
            name.ShouldBe("node.exe");
            commandLine.ShouldContain(@"\bin\codex.js");
            commandLine.ShouldNotContain("codex.cmd");

            (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();
            await adapter.SendPromptAsync($"Reply with exactly this token and nothing else is needed: {nonce}", CancellationToken.None);
            var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
            turn.TurnCompleted.ShouldBeTrue();
            turn.ResponseText.ShouldNotBeNull();
            turn.ResponseText!.ShouldContain(reply);

            var transcript = await client.GetTranscriptAsync(sessionId, CancellationToken.None);
            transcript.Entries.Any(e =>
                e.Kind == TranscriptKinds.UserPrompt
                && e.Text != null
                && e.Text.Contains(nonce, StringComparison.Ordinal)).ShouldBeTrue();
            transcript.Entries.Any(e =>
                e.Text != null && e.Text.Contains(reply, StringComparison.Ordinal)).ShouldBeTrue();

            var chatHit = stub.Requests.All.FirstOrDefault(r =>
                r.Method == "POST" && r.Path == "/v1/responses" && r.Body.Contains(nonce, StringComparison.Ordinal));
            chatHit.ShouldNotBeNull();
            chatHit!.Headers["Authorization"].ShouldBe([$"Bearer {syntheticKey}"]);
            stub.Requests.All.ShouldContain(r => r.Method == "GET" && r.Path == "/v1/models");

            var strings = WalkStrings(JsonDocument.Parse(chatHit.Body).RootElement).ToList();
            strings.Count(s => s.Contains(compareFixture, StringComparison.Ordinal)).ShouldBe(1);
            strings.ShouldContain(s => s.Contains(nonce, StringComparison.Ordinal));
            if (compareFixture.Contains(CodexInstructionFixtures.StartSentinel, StringComparison.Ordinal))
            {
                strings.ShouldContain(s => s.Contains(CodexInstructionFixtures.StartSentinel, StringComparison.Ordinal));
                strings.ShouldContain(s => s.Contains(CodexInstructionFixtures.MidSentinel, StringComparison.Ordinal));
                strings.ShouldContain(s => s.Contains(CodexInstructionFixtures.EndSentinel, StringComparison.Ordinal));
            }

            var killed = await adapter.KillAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            killed.ShouldBeTrue();
            AssertNoProcessMentions($"127.0.0.1:{stub.ListenPort}");

            return ExtractInstructions(strings);
        }
        finally
        {
            try { await adapter.KillAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
            catch { /* teardown */ }
            RealCliStubBServerHarness.TryDelete(tempRoot);
        }
    }

    private static string NpmShimPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var shim = Path.Combine(appData, "npm", "codex.cmd");
        if (!File.Exists(shim))
            throw new SkipTestException($"npm Codex shim not found at {shim}");
        return shim;
    }

    private static string FitHop2Payload(int target)
    {
        var shimDir = Path.GetDirectoryName(NpmShimPath())!;
        var js = Path.Combine(shimDir, "node_modules", "@openai", "codex", "bin", "codex.js");
        var native = ResolveNative(shimDir) ?? throw new SkipTestException("vendored codex.exe not found");
        var overlayLen = 400;
        var prefix = new[]
        {
            "--no-alt-screen",
            "--dangerously-bypass-approvals-and-sandbox",
            "-c", "model_providers.stub.name=\"Stub\"",
            "-c", "model_providers.stub.env_key=\"OPENAI_API_KEY\"",
            "-c", "model_providers.stub.wire_api=\"responses\"",
            "-c", "model_provider=stub",
            "-c", "disable_paste_burst=true",
            "-c",
        };
        _ = overlayLen;
        var filler = new string('A', Math.Max(100, target));
        var payload = CodexInstructionFixtures.StartSentinel + filler;
        var args = prefix.Concat(["developer_instructions=" + payload]).ToArray();
        var measured = WindowsCommandLine.Measure(native, args);
        if (measured > target)
            payload = payload[..^Math.Min(payload.Length - 20, measured - target)];
        else
            payload += new string('A', target - measured);
        args = prefix.Concat(["developer_instructions=" + payload]).ToArray();
        WindowsCommandLine.Measure(native, args).ShouldBe(target);
        _ = js;
        return payload;
    }

    private static string? ResolveNative(string npmRoot)
    {
        foreach (var probe in new[]
                 {
                     Path.Combine(npmRoot, "node_modules", "@openai", "codex", "node_modules", "@openai"),
                     Path.Combine(npmRoot, "node_modules", "@openai"),
                 })
        {
            if (!Directory.Exists(probe)) continue;
            foreach (var platform in Directory.EnumerateDirectories(probe, "codex-win32-*"))
            {
                var vendor = Path.Combine(platform, "vendor");
                if (!Directory.Exists(vendor)) continue;
                foreach (var triple in Directory.EnumerateDirectories(vendor))
                {
                    var exe = Path.Combine(triple, "bin", "codex.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
        }

        return null;
    }

    private static (string Name, string CommandLine) QueryProcess(int pid)
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT Name, CommandLine FROM Win32_Process WHERE ProcessId={pid}");
        foreach (ManagementObject obj in searcher.Get())
        {
            using (obj)
            {
                return (
                    obj["Name"]?.ToString() ?? "",
                    obj["CommandLine"]?.ToString() ?? "");
            }
        }

        throw new InvalidOperationException($"Win32_Process pid {pid} not found");
    }

    private static void AssertNoProcessMentions(string needle)
    {
        using var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process");
        foreach (ManagementObject obj in searcher.Get())
        {
            using (obj)
            {
                var cmd = obj["CommandLine"]?.ToString() ?? "";
                cmd.Contains(needle, StringComparison.OrdinalIgnoreCase).ShouldBeFalse(cmd);
            }
        }
    }

    private static IEnumerable<string> WalkStrings(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                yield return el.GetString() ?? "";
                break;
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                foreach (var s in WalkStrings(p.Value))
                    yield return s;
                break;
            case JsonValueKind.Array:
                foreach (var i in el.EnumerateArray())
                foreach (var s in WalkStrings(i))
                    yield return s;
                break;
        }
    }

    private static string? ExtractInstructions(IReadOnlyList<string> strings) =>
        strings.FirstOrDefault(s => s.Contains("You are Codex", StringComparison.OrdinalIgnoreCase)
            || s.Contains("developer", StringComparison.OrdinalIgnoreCase) && s.Length > 200);

    private static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;
}
