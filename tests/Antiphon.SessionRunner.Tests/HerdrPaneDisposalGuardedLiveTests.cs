using System.Diagnostics;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>Historical class ID retained. Stock Herdr, best-effort checks, no atomicity claim.</summary>
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class HerdrPaneDisposalGuardedLiveTests
{
    [Test] [Arguments(false)] [Arguments(true)] public async Task Isolated_backend_closes_only_reviewed_incarnation(bool occupied)
    {
        await using var h = new LiveFixture(); await h.StartAsync();
        if (occupied) await h.StartOwnedCodexDummyAsync();
        var p = await h.PreviewAsync(); p.Eligible.ShouldBeTrue(); p.AtomicClose.ShouldBeFalse();
        var sentinel = await h.Client.PaneGetAsync(h.Sentinel, default);
        var r = await h.Service.ExecuteAsync(h.Request(p), default); r.Outcome.ShouldBe("Closed");
        (await h.Client.PaneGetAsync(h.Sentinel, default)).TerminalId.ShouldBe(sentinel.TerminalId);
        (await h.Client.PaneListAsync(null, default)).ShouldNotContain(pane => pane.TerminalId == p.TerminalId);
    }
    [Test] public async Task Isolated_backend_recovers_lost_close_result()
    {
        await using var h = new LiveFixture(); await h.StartAsync(); var p = await h.PreviewAsync(); var request = h.Request(p);
        h.Backend.DropAfterClose = true;
        (await h.Service.ExecuteAsync(request, default)).Outcome.ShouldBe("Unknown");
        HerdrPaneDisposalReceipt? receipt = null;
        for (var i = 0; i < 100; i++)
        { receipt = await h.Service.GetAsync(request.OperationId, default); if (receipt!.Outcome == "AlreadyAbsent") break; await Task.Delay(50); }
        receipt!.Outcome.ShouldBe("AlreadyAbsent"); h.Backend.Closes.ShouldBe(1);
        await h.Client.PaneGetAsync(h.Sentinel, default);
    }
    [Test] public Task C461_G083_Foreign_after_runner_inspection() => ForeignAtFinalCheck(false);
    [Test] public Task C461_G084_External_tree_race_at_teardown() => ForeignAtFinalCheck(true);
    private static async Task ForeignAtFinalCheck(bool background)
    {
        await using var h = new LiveFixture(); await h.StartAsync(); var p = await h.PreviewAsync();
        var heartbeat = Path.Combine(h.Root, "foreign-started.txt");
        var finalInspection = h.Backend.Inspections + 2;
        h.Backend.BeforeInspect = async n =>
        {
            if (n != finalInspection) return;
            var command = background
                ? "$p = Start-Process -FilePath $env:ComSpec -ArgumentList '/c ping -t 127.0.0.1 > nul' -PassThru -WindowStyle Hidden; $p.Id | Set-Content -LiteralPath '" + heartbeat + "'"
                : "Set-Content -LiteralPath '" + heartbeat + "' -Value ready; ping.exe -t 127.0.0.1";
            await h.Client.PaneSendTextAsync(h.Pane, command, default);
            await h.Client.SendRequestAsync("pane.send_keys", new { pane_id = h.Pane, keys = new[] { "Enter" } }, default);
            for (var i = 0; i < 100 && !File.Exists(heartbeat); i++) await Task.Delay(50);
            File.Exists(heartbeat).ShouldBeTrue("owned foreign process must start before the final check");
            // Wait for the independent OS census, not a sidecar or screen redraw.
            for (var i = 0; i < 100; i++)
            {
                var info = await h.Client.PaneProcessInfoAsync(h.Pane, default);
                var tree = new HerdrDisposalProcessInspector().Inspect(info);
                if (tree.Affected.Count > 1) return;
                await Task.Delay(50);
            }
            throw new InvalidOperationException("Owned foreign process did not enter the pane tree.");
        };
        var r = await h.Service.ExecuteAsync(h.Request(p), default);
        r.Outcome.ShouldBe("Refused"); h.Backend.Closes.ShouldBe(0);
        (await h.Client.PaneGetAsync(h.Pane, default)).TerminalId.ShouldBe(p.TerminalId);
        var remaining = new HerdrDisposalProcessInspector().Inspect(await h.Client.PaneProcessInfoAsync(h.Pane, default));
        remaining.Affected.Count.ShouldBeGreaterThan(1);
        foreach (var process in remaining.Affected) new HerdrDisposalProcessInspector().IsSameProcessAlive(process).ShouldBe(true);
    }
    [Test] public async Task C461_G085_Backend_rpc_fence()
    {
        await using var h = new LiveFixture(); await h.StartAsync(); var p = await h.PreviewAsync();
        var finalInspection = h.Backend.Inspections + 2;
        h.Backend.BeforeInspect = async n =>
        {
            if (n == finalInspection) await h.Client.SendRequestAsync("pane.move", new { pane_id = h.Pane, destination = new { type = "new_tab", label = "moved by owned test" } }, default);
        };
        var r = await h.Service.ExecuteAsync(h.Request(p), default);
        r.Outcome.ShouldBe("Refused"); h.Backend.Closes.ShouldBe(0);
        (await h.Client.PaneListAsync(null, default)).ShouldContain(pane => pane.TerminalId == p.TerminalId);
        await h.Client.PaneGetAsync(h.Sentinel, default);
    }

    internal sealed class LiveFixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "antiphon-c461-live-" + Guid.NewGuid().ToString("N"));
        private readonly string _session = "c461-" + Guid.NewGuid().ToString("N");
        private Process? _server;
        private Task<string>? _stdout, _stderr;
        public HerdrClient Client { get; private set; } = null!;
        public SessionRunnerRuntime Runtime { get; private set; } = null!;
        public HerdrPaneDisposalService Service { get; private set; } = null!;
        public HerdrPaneDisposalFixture.TestBackend Backend { get; private set; } = null!;
        public string Pane { get; private set; } = "";
        public string Sentinel { get; private set; } = "";
        public Guid Session { get; } = Guid.NewGuid();
        public async Task StartAsync()
        {
            Directory.CreateDirectory(Root);
            var config = Path.Combine(Root, "config.toml");
            await File.WriteAllTextAsync(config, "onboarding = false\n[terminal]\ndefault_shell = 'pwsh.exe'\nshell_mode = 'non_login'\n[update]\nversion_check = false\nmanifest_check = false\n");
            var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Herdr", "bin", "herdr.exe");
            File.Exists(executable).ShouldBeTrue("V-9 requires the installed stock Herdr binary");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Root };
            foreach (var arg in new[] { "--session", _session, "server" }) start.ArgumentList.Add(arg);
            start.Environment["APPDATA"] = Root; start.Environment["LOCALAPPDATA"] = Root;
            start.Environment["USERPROFILE"] = Root; start.Environment["HOME"] = Root;
            start.Environment["HERDR_CONFIG_PATH"] = config;
            foreach (var key in new[] { "HERDR_SOCKET_PATH", "HERDR_SESSION", "ANTIPHON_TASK_TOKEN", "OPENAI_API_KEY", "ANTHROPIC_API_KEY", "XAI_API_KEY", "GROK_HOME", "CODEX_HOME", "CLAUDE_CONFIG_DIR" }) start.Environment.Remove(key);
            _server = Process.Start(start)!; _stdout = _server.StandardOutput.ReadToEndAsync(); _stderr = _server.StandardError.ReadToEndAsync();
            Client = new(new HerdrSettings { Enabled = true, Session = _session, ConnectTimeoutMs = 100 }, Path.Combine(Root, "herdr", "sessions", _session, "herdr.sock"));
            HerdrServerInfo? info = null;
            for (var i = 0; i < 100; i++)
            {
                if (_server.HasExited) throw new InvalidOperationException("Owned Herdr exited: " + await _stderr);
                try { info = await Client.ConnectAndValidateAsync(default); break; }
                catch (HerdrBackendUnavailableException) { await Task.Delay(50); }
            }
            info.ShouldNotBeNull(); info.InstanceId.ShouldStartWith(_server.Id + ":");
            Console.WriteLine($"C461 isolated Herdr {_session}: version={info.Version} protocol={info.Protocol} instance={info.InstanceId} root={Root}");
            var created = await Client.WorkspaceCreateAsync(Root, "C461 isolated", default);
            Pane = created.RootPane.PaneId;
            var tab = await Client.TabCreateAsync(created.WorkspaceId, Root, null, "sentinel", default); Sentinel = tab.InitialPaneId;
            await Client.SendRequestAsync("pane.report_metadata", new { pane_id = Pane, source = "antiphon", tokens = new Dictionary<string, string> { ["antiphon-session"] = Session.ToString("D") } }, default);
            var settings = new SessionRunnerSettings { SessionLogPath = Path.Combine(Root, "runner") };
            Runtime = new(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance, Client);
            Backend = new(new HerdrDisposalBackend(Client, Runtime, new(settings.SessionLogPath), new HerdrDisposalProcessInspector()));
            Service = new(Runtime, settings.SessionLogPath, TimeProvider.System, Backend);
            for (var i = 0; i < 100; i++)
            { if ((await PreviewAsync()).Eligible) return; await Task.Delay(50); }
            throw new InvalidOperationException("Owned idle shell never became eligible.");
        }
        public Task<HerdrPaneDisposalPreview> PreviewAsync() => Service.PreviewAsync(new(Pane, Session), default);
        public async Task StartOwnedCodexDummyAsync()
        {
            // A renamed Windows command interpreter is an inert dummy, with no provider home,
            // network client or authentication. Exact recorded OS identity is the proof arm.
            var dummy = Path.Combine(Root, "codex.exe"); File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), dummy);
            await Client.PaneSendTextAsync(Pane, "& '" + dummy + "' /d /q /k", default);
            await Client.PaneSendKeysAsync(Pane, ["Enter"], default);
            for (var i = 0; i < 200; i++)
            {
                var pane = await Client.PaneGetAsync(Pane, default);
                var tree = new HerdrDisposalProcessInspector().Inspect(await Client.PaneProcessInfoAsync(Pane, default));
                var child = tree.Foreground?.SingleOrDefault(p => p.Pid != tree.Shell?.Pid);
                if (pane.Agent == "codex" && child?.StartedAtUtc is not null)
                {
                    new HerdrPaneSidecar { SessionId = Session, PaneId = Pane, TabId = pane.TabId, WorkspaceId = pane.WorkspaceId,
                        WorkspaceKey = "owned-live", AgentKind = "codex", Origin = HerdrPaneOrigins.Attached,
                        ChildPid = child.Pid, ChildStartedAtUtc = child.StartedAtUtc, ShellPid = tree.Shell?.Pid }
                        .SaveAtomic(HerdrPaneSidecar.PathFor(Path.Combine(Root, "runner"), Session));
                    return;
                }
                await Task.Delay(50);
            }
            throw new InvalidOperationException("Owned inert Codex dummy was not detected by stock Herdr.");
        }
        public HerdrPaneDisposalRequest Request(HerdrPaneDisposalPreview p) => new(Guid.NewGuid(), p.PreviewId, "owned live test", "antiphon-best-effort");
        public async ValueTask DisposeAsync()
        {
            if (Runtime is not null) await Runtime.DisposeAsync();
            if (_server is not null)
            {
                // This exact Process was created by this fixture with an isolated home and unique pipe.
                if (!_server.HasExited) { _server.Kill(entireProcessTree: true); await _server.WaitForExitAsync(); }
                await File.WriteAllTextAsync(Path.Combine(Root, "server-output.txt"), (await _stdout!) + (await _stderr!));
                _server.Dispose();
            }
            // Retain isolated logs as V-9 evidence; no default-daemon shutdown or provider cleanup.
        }
    }
}
