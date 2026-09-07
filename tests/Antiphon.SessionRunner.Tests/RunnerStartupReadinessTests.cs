using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), NotInParallel("SessionLiveness"), ParallelLimiter<ProcessSpawnLimit>]
public class RunnerStartupReadinessTests
{
    [Test, Arguments(false), Arguments(true)]
    public async Task Adoption_blocks_http_until_the_complete_session_set_is_represented(bool drop)
    {
        var root = TestSessionLogRoot.Create("c420-barrier");
        var evidence = Path.Combine(RestartFixture.Repo, ".antiphon", "c420-evidence", "barrier-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        var settings = new SessionRunnerSettings { SessionLogPath = root, PtyHostDir = Path.Combine(root, "hosts") };
        var terminalId = RunnerStartupDiagnosticsTests.SeedTerminal(settings);
        var fake = new FakeHerdrServer(); var fakeDisposed = false; fake.Start(); await fake.WaitUntilListeningAsync();
        using var dummy = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /q /k")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        var id = Guid.NewGuid();
        var sidecar = new HerdrPaneSidecar { SessionId = id, WorkspaceKey = "c420", WorkspaceId = "w420", TabId = "t420", PaneId = "p420",
            ChildPid = dummy.Id, ShellPid = 1, LaunchedAtUtc = dummy.StartTime.ToUniversalTime(), Cwd = root, UpdatedAtUtc = DateTime.UtcNow };
        sidecar.SaveAtomic(HerdrPaneSidecar.PathFor(root, id));
        fake.Workspaces.Add(new FakeHerdrServer.WorkspaceState("w420", "seed", 1, "t420",
            [new FakeHerdrServer.TabState("t420", "w420", "1", 1, [new FakeHerdrServer.PaneState("p420", "t420", "w420", "term_seed", null, null, null, null, null)])], new Dictionary<string, string>()));
        fake.SetPaneProcessInfo("p420", 1, (dummy.Id, "cmd.exe"));
        var gate = fake.GateMethod("pane.read");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        new[] { 17202, 17203, 17204, 17205, 17280, 17281, 17282 }.ShouldNotContain(port);
        var exe = Path.Combine(AppContext.BaseDirectory, "Antiphon.SessionRunner.exe"); File.Exists(exe).ShouldBeTrue();
        var info = new ProcessStartInfo(exe) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "--urls", $"http://127.0.0.1:{port}", "--SessionRunner:SessionLogPath", root,
            "--SessionRunner:PtyHostDir", settings.PtyHostDir!, "--Serilog:LogPath", Path.Combine(root, "logs"),
            "--SessionRunner:Herdr:Enabled", "true", "--SessionRunner:Herdr:Session", fake.Session,
            "--SessionRunner:Herdr:RequestTimeoutMs", "20000", "--SessionRunner:CpuWatchdogEnabled", "false",
            "--SessionRunner:Herdr:StatusPush:Enabled", "false", "--SessionRunner:LivenessSweepIntervalMs", "60000" }) info.ArgumentList.Add(a);
        var output = new ConcurrentQueue<string>(); using var runner = new Process { StartInfo = info };
        runner.OutputDataReceived += (_, e) => { if (e.Data is { } line) output.Enqueue(line); };
        runner.ErrorDataReceived += (_, e) => { if (e.Data is { } line) output.Enqueue(line); };
        var observations = new List<object>(); var succeeded = false;
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromMilliseconds(250) };
        runner.Start().ShouldBeTrue(); runner.BeginOutputReadLine(); runner.BeginErrorReadLine();
        try
        {
            var reach = Stopwatch.StartNew();
            while (!fake.Requests.Any(r => r.GetProperty("method").GetString() == "pane.read"))
            { runner.HasExited.ShouldBeFalse(string.Join('\n', output)); reach.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20)); await Task.Delay(50); }
            dummy.HasExited.ShouldBeFalse(); runner.HasExited.ShouldBeFalse();
            observations.Add(new { phase = "gate-entered", port, drop, runnerPid = runner.Id,
                runnerStartUtc = runner.StartTime.ToUniversalTime(), dummyPid = dummy.Id,
                dummyStartUtc = dummy.StartTime.ToUniversalTime(), herdrSessionId = id, terminalSessionId = terminalId,
                observedAtUtc = DateTime.UtcNow });
            var hold = Stopwatch.StartNew();
            while (hold.Elapsed < TimeSpan.FromSeconds(5))
            {
                foreach (var path in new[] { "/health", "/sessions" })
                {
                    try { using var response = await http.GetAsync(path); observations.Add(new { path, phase = "held", status = (int)response.StatusCode }); response.IsSuccessStatusCode.ShouldBeFalse("HTTP became ready while adoption was held"); }
                    catch (HttpRequestException) { observations.Add(new { path, phase = "held", result = "connection-error", elapsedMs = hold.Elapsed.TotalMilliseconds }); }
                    catch (TaskCanceledException) { observations.Add(new { path, phase = "held", result = "timeout", elapsedMs = hold.Elapsed.TotalMilliseconds }); }
                }
                output.Any(l => l.Contains("application-started")).ShouldBeFalse("ApplicationStarted while adoption was held");
                runner.HasExited.ShouldBeFalse(); dummy.HasExited.ShouldBeFalse(); await Task.Delay(100);
            }
            if (drop) { gate.Drop(); await fake.DisposeAsync(); fakeDisposed = true; } else gate.Release();
            var release = Stopwatch.StartNew(); string? firstList = null;
            while (firstList is null)
            {
                release.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15), string.Join('\n', output)); runner.HasExited.ShouldBeFalse(string.Join('\n', output));
                try { using var response = await http.GetAsync("/sessions"); if (response.IsSuccessStatusCode) firstList = await response.Content.ReadAsStringAsync(); }
                catch (HttpRequestException) { } catch (TaskCanceledException) { }
                if (firstList is null) await Task.Delay(100);
            }
            observations.Add(new { phase = "released", firstList, observedAtUtc = DateTime.UtcNow });
            var sessions = JsonSerializer.Deserialize<RunnerSessionDto[]>(firstList, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            sessions.Length.ShouldBe(2); sessions.Single(s => s.SessionId == terminalId).Status.ShouldBe("Exited");
            var herdr = sessions.Single(s => s.SessionId == id); herdr.Status.ShouldBe(drop ? "Starting" : "Running");
            if (drop) herdr.Pending.ShouldBe(HerdrPendingReasons.Unreachable);
            using var health = await http.GetAsync("/health"); health.StatusCode.ShouldBe(HttpStatusCode.OK);
            var names = new[] { "managed-entry", "runtime-resolution-start", "runtime-resolution-end", "claims-start", "claims-end", "herdr-start", "herdr-end", "pty-manifests-start", "pty-manifests-end", "adoption-sweep-end", "application-started" };
            var joined = string.Join('\n', output); var previous = -1;
            foreach (var name in names) { var index = joined.IndexOf('"' + name + '"', StringComparison.Ordinal); index.ShouldBeGreaterThan(previous, name); previous = index; }
            using var decoder = new RestartFixture();
            await decoder.DecodeCapturedMilestones(joined + "\n", runner.Id, runner.StartTime.ToUniversalTime().ToString("O"), "runner", "application-started");
            succeeded = true;
        }
        finally
        {
            gate.Release(); if (!runner.HasExited) runner.Kill(true); await runner.WaitForExitAsync();
            if (!dummy.HasExited) dummy.Kill(true); await dummy.WaitForExitAsync();
            if (!fakeDisposed)
            {
                try { await fake.DisposeAsync(); }
                catch (Exception ex) when (!succeeded) { output.Enqueue("Fake cleanup after primary failure: " + ex.GetType().Name); }
            }
            await File.WriteAllLinesAsync(Path.Combine(evidence, "runner.log"), output);
            await File.WriteAllTextAsync(Path.Combine(evidence, "observations.json"), JsonSerializer.Serialize(observations));
            await File.WriteAllTextAsync(Path.Combine(evidence, "requests.json"), JsonSerializer.Serialize(fake.Requests));
            if (succeeded)
            {
                var resolved = Path.GetFullPath(root);
                resolved.StartsWith(Path.Combine(Path.GetTempPath(), "antiphon-c420-barrier-"), StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
                Directory.Delete(resolved, true);
            }
        }
    }
}
