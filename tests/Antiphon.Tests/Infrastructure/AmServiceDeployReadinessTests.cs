using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AmServiceDeployReadinessTests
{
    private const string Body = "[{\"channel\":\"telegram\"},{\"channel\":\"slack\"}]";
    private static string Root => DelegateScriptRunner.RepoRoot;

    [Test]
    public async Task RetriesUntilHttpReadyAsync()
    {
        var baseline = await LoadBaselineAsync();
        try
        {
            using var probe = new ReadinessFixture(2, "ready");
            var red = await probe.RunAsync("V-1-baseline", baseline);
            red.ShellExit.ShouldBe(7, red.Detail);
            red.CurlExits.ShouldBe(new[] { 7 }, red.Detail);
            red.SleepArguments.ShouldBeEmpty(red.Detail);
            red.LaterCalls.ShouldBe(0, red.Detail);
            red.OuterError.ShouldContain("exit 7", red.Detail);
            red.ChildOutput.ShouldContain("REMOTE DEPLOY VERDICT: failed", red.Detail);
            red.Journal.ShouldContain("inspect", red.Detail);
            Console.WriteLine("C504 baseline: 1 expected failure, 1 curl exit 7, 0 sleeps");
        }
        finally { File.Delete(baseline); }

        foreach (var readyAt in new[] { 3, 5 })
        {
            using var fixture = new ReadinessFixture(readyAt - 1, "ready");
            var run = await fixture.RunAsync("V-1", Path.Combine(Root, "scripts", "deploy-am-service.ps1"));
            AssertSuccess(run, readyAt, readyAt - 1);
        }
    }

    [Test]
    public async Task FailsWhenHttpReadinessBudgetIsExhaustedAsync()
    {
        foreach (var mode in new[] { "refused", "timeout", "unavailable" })
        {
            using var fixture = new ReadinessFixture(0, mode);
            var run = await fixture.RunAsync("V-2", Path.Combine(Root, "scripts", "deploy-am-service.ps1"));
            var expected = mode switch { "refused" => 7, "timeout" => 28, _ => 22 };
            run.ShellExit.ShouldBe(44, run.Detail);
            run.CurlExits.ShouldBe(Enumerable.Repeat(expected, 5), run.Detail);
            run.SleepArguments.ShouldBe(Enumerable.Repeat("2", 4), run.Detail);
            AssertCurlArguments(run);
            run.LaterCalls.ShouldBe(0, run.Detail);
            run.Journal.ShouldNotContain("migration", run.Detail);
            run.Journal.ShouldNotContain("logs", run.Detail);
            run.OuterError.ShouldContain("HTTP readiness was not confirmed", run.Detail);
            run.OuterError.ShouldContain("/api/channels", run.Detail);
            run.OuterError.ShouldContain("44", run.Detail);
            run.ChildOutput.ShouldContain("REMOTE DEPLOY VERDICT: failed", run.Detail);
            run.ChildOutput.ShouldNotContain("REMOTE DEPLOY VERDICT: ok", run.Detail);
            run.ChildOutput.ShouldNotContain(Body, run.Detail);
            run.ChildOutput.ShouldNotContain("c504-response-must-not-leak", run.Detail);
            run.ShellOutput.ShouldBeEmpty(run.Detail);
        }
    }

    [Test]
    public async Task SucceedsImmediatelyAndPreservesDeployContractAsync()
    {
        using (var fixture = new ReadinessFixture(0, "ready"))
        {
            var run = await fixture.RunAsync("V-3", Path.Combine(Root, "scripts", "deploy-am-service.ps1"));
            AssertSuccess(run, 1, 0);
            run.ServedRequests.ShouldBe(1, run.Detail);
        }
        var legacy = await RunLegacyAsync();
        legacy.ShouldContain("DEPLOY-AM-SERVICE TESTS EXIT CODE: 0", Case.Sensitive);
        foreach (var name in new[] {
            "broker identity probe execs Compose service redpanda",
            "preflight accepts redpanda.",
            "identity, compose, and container output never leak",
            "deploy writes the managed override and verifies the merge before recreate",
            "marked existing override is backed up and reported",
            "remote seam parses endpoint adapters after recreate",
            "postdeploy verification refuses degraded" })
            legacy.ShouldContain(name, Case.Sensitive);
        legacy.ShouldNotContain("FAIL ", Case.Sensitive);
        Console.WriteLine("PASS C504 R-1");
    }

    private static void AssertSuccess(Run run, int calls, int sleeps)
    {
        run.ShellExit.ShouldBe(0, run.Detail);
        run.CurlExits.ShouldBe(Enumerable.Repeat(7, calls - 1).Append(0), run.Detail);
        run.SleepArguments.ShouldBe(Enumerable.Repeat("2", sleeps), run.Detail);
        AssertCurlArguments(run);
        run.ShellOutput[0].ShouldBe(Body, run.Detail);
        run.ShellOutput.Count(x => x == Body).ShouldBe(1, run.Detail);
        run.ShellOutput.Skip(1).ShouldAllBe(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^\\d{14}_.+$"), run.Detail);
        run.Journal.Count(x => x == "migration").ShouldBe(1, run.Detail);
        run.Journal.Count(x => x == "logs").ShouldBe(1, run.Detail);
        run.Journal.IndexOf("migration").ShouldBeGreaterThan(run.Journal.FindLastIndex(x => x.StartsWith("curl-end|", StringComparison.Ordinal)), run.Detail);
        run.Journal.IndexOf("logs").ShouldBeGreaterThan(run.Journal.IndexOf("migration"), run.Detail);
        run.AdapterNames.ShouldBe(new[] { "telegram", "slack" }, run.Detail);
        run.LaterCalls.ShouldBeGreaterThan(0, run.Detail);
        run.OuterError.ShouldBeEmpty(run.Detail);
        run.ChildOutput.ShouldContain("REMOTE DEPLOY VERDICT: ok", run.Detail);
    }

    private static void AssertCurlArguments(Run run)
    {
        var starts = run.Journal.Where(x => x.StartsWith("curl-start|", StringComparison.Ordinal)).ToArray();
        starts.Length.ShouldBe(run.CurlExits.Length, run.Detail);
        foreach (var item in starts)
        {
            item.ShouldContain("--connect-timeout 2", run.Detail);
            item.ShouldContain("--max-time 3", run.Detail);
            item.ShouldContain("-fsS", run.Detail);
            item.ShouldContain(run.Endpoint, run.Detail);
        }
    }

    private static async Task<string> LoadBaselineAsync()
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("show");
        start.ArgumentList.Add("10fe51b0f668a272d621a935c27e53313b8de1c8:scripts/deploy-am-service.ps1");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git show did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, await error);
        var path = Path.Combine(Path.GetTempPath(), "c504-baseline-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(path, await output);
        return path;
    }

    private static async Task<string> RunLegacyAsync()
    {
        var start = new ProcessStartInfo("pwsh") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(Root, "scripts", "test-deploy-am-service.ps1") }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(300));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        var output = await stdout + await stderr;
        process.ExitCode.ShouldBe(0, output);
        return output;
    }

    private sealed class ReadinessFixture : IDisposable
    {
        private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private readonly CancellationTokenSource _stop = new();
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "c504-" + Guid.NewGuid().ToString("N"));
        private readonly int _refusals;
        private readonly string _mode;
        private Task? _server;
        private int _served;

        public ReadinessFixture(int refusals, string mode)
        {
            _refusals = refusals;
            _mode = mode;
            Directory.CreateDirectory(_directory);
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            if (mode != "refused" && refusals == 0) Listen();
        }

        private string Endpoint => $"http://127.0.0.1:{((IPEndPoint)_socket.LocalEndPoint!).Port}/api/channels";
        private string JournalPath => Path.Combine(_directory, "journal.txt");

        public async Task<Run> RunAsync(string caseName, string deploymentScript)
        {
            var wrappers = Path.Combine(_directory, "wrappers");
            Directory.CreateDirectory(wrappers);
            var migrationIds = Directory.GetFiles(Path.Combine(Root, "src", "Antiphon.Messaging.Service", "Migrations"), "*.cs")
                .Select(Path.GetFileNameWithoutExtension).Where(x => !x!.EndsWith(".Designer") && !x.EndsWith("ModelSnapshot"));
            File.WriteAllLines(Path.Combine(_directory, "migrations.txt"), migrationIds!);
            WriteWrapper(wrappers, "docker", """
                #!/bin/sh
                case "$*" in
                  *State.Running*) printf 'inspect\n' >> "$C504_JOURNAL"; printf 'true\n' ;;
                  *am-postgres*) printf 'migration\n' >> "$C504_JOURNAL"; cat "$C504_MIGRATIONS" ;;
                  'logs --since 5m --tail 100 am-service') printf 'logs\n' >> "$C504_JOURNAL" ;;
                  *) printf 'unexpected-docker\n' >> "$C504_JOURNAL"; exit 91 ;;
                esac
                """);
            WriteWrapper(wrappers, "curl", """
                #!/bin/sh
                case " $* " in *" $C504_ENDPOINT "*) ;; *) printf 'bad-url\n' >> "$C504_JOURNAL"; exit 92 ;; esac
                n=1
                if [ -f "$C504_COUNT" ]; then n=$(cat "$C504_COUNT"); n=$((n + 1)); fi
                printf '%s\n' "$n" > "$C504_COUNT"
                printf 'curl-start|%s|%s\n' "$n" "$*" >> "$C504_JOURNAL"
                "$C504_REAL_CURL" --disable --noproxy '*' "$@"
                code=$?
                printf 'curl-end|%s|%s\n' "$n" "$code" >> "$C504_JOURNAL"
                if [ "$code" -eq 7 ] && [ "$n" -eq "$C504_REFUSALS" ] && [ "$C504_MODE" = ready ]; then
                  ticks=0
                  while [ ! -f "$C504_ACK" ]; do
                    "$C504_REAL_SLEEP" 0.1
                    ticks=$((ticks + 1))
                    [ "$ticks" -lt 100 ] || exit 93
                  done
                fi
                exit "$code"
                """);
            WriteWrapper(wrappers, "sleep", """
                #!/bin/sh
                printf 'sleep|%s\n' "$*" >> "$C504_JOURNAL"
                exec "$C504_REAL_SLEEP" "$@"
                """);
            foreach (var name in new[] { "ssh", "scp" })
                WriteWrapper(wrappers, name, "#!/bin/sh\nprintf 'unexpected-native-" + name + "\\n' >> \"$C504_JOURNAL\"\nexit 94\n");

            var start = new ProcessStartInfo("pwsh") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(Root, "scripts", "test-deploy-am-service.ps1"), "-ReadinessCase", caseName, "-FixtureDirectory", _directory, "-DeploymentScript", deploymentScript }) start.ArgumentList.Add(arg);
            start.Environment["PATH"] = wrappers + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            start.Environment["C504_ENDPOINT"] = Endpoint;
            start.Environment["C504_JOURNAL"] = JournalPath;
            start.Environment["C504_MIGRATIONS"] = Path.Combine(_directory, "migrations.txt");
            start.Environment["C504_COUNT"] = Path.Combine(_directory, "curl-count.txt");
            start.Environment["C504_ACK"] = Path.Combine(_directory, "listen-ack.txt");
            start.Environment["C504_REFUSALS"] = _refusals.ToString();
            start.Environment["C504_MODE"] = _mode;
            start.Environment["C504_REAL_CURL"] = FindProgram("curl");
            start.Environment["C504_REAL_SLEEP"] = FindProgram("sleep");
            start.Environment["C504_POSIX_FIXTURE"] = ToPosixPath(_directory);
            foreach (var key in start.Environment.Keys.Where(x => x.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) || x.Contains("SECRET", StringComparison.OrdinalIgnoreCase) || x.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) || x.Contains("API_KEY", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("PowerShell did not start");
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(300));
            Task? transition = null;
            if (_mode == "ready" && _refusals > 0)
                transition = Task.Run(async () => {
                    while (!_stop.IsCancellationRequested)
                    {
                        if (File.Exists(JournalPath) && File.ReadAllLines(JournalPath).Contains($"curl-end|{_refusals}|7")) { Listen(); File.WriteAllText(Path.Combine(_directory, "listen-ack.txt"), "ready"); return; }
                        await Task.Delay(25, _stop.Token);
                    }
                });
            try
            {
                await child.WaitForExitAsync(timeout.Token);
                if (transition is not null)
                {
                    if (File.Exists(Path.Combine(_directory, "listen-ack.txt"))) await transition.WaitAsync(TimeSpan.FromSeconds(10));
                    else _stop.Cancel(); // The baseline stops after its first refusal.
                }
                var output = await stdout + await stderr;
                child.ExitCode.ShouldBe(0, output);
                output.ShouldContain("PASS C504 " + caseName, Case.Sensitive);
                output.ShouldContain("DEPLOY-AM-SERVICE TESTS EXIT CODE: 0", Case.Sensitive);
                output.ShouldNotContain("FAIL ", Case.Sensitive);
                var result = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, "result.json"))).RootElement;
                var journal = File.Exists(JournalPath) ? File.ReadAllLines(JournalPath).ToList() : new List<string>();
                journal.ShouldNotContain(x => x.StartsWith("unexpected-", StringComparison.Ordinal), output);
                var shellOutput = result.GetProperty("shellOutput").EnumerateArray().Select(x => x.GetString()!).ToArray();
                return new Run(result.GetProperty("shellExit").GetInt32(), shellOutput,
                    result.GetProperty("outerError").GetString()!, result.GetProperty("adapterNames").EnumerateArray().Select(x => x.GetString()!).ToArray(),
                    result.GetProperty("laterCalls").GetInt32(), journal, output, Endpoint, _served);
            }
            catch (OperationCanceledException) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); throw; }
        }

        private void Listen()
        {
            _socket.Listen(4);
            _server = Task.Run(async () => {
                while (!_stop.IsCancellationRequested)
                {
                    Socket accepted;
                    try { accepted = await _socket.AcceptAsync(_stop.Token); }
                    catch (OperationCanceledException) { return; }
                    catch (ObjectDisposedException) { return; }
                    _ = Task.Run(async () => {
                        using (accepted)
                        {
                            var buffer = new byte[4096];
                            var size = await accepted.ReceiveAsync(buffer, SocketFlags.None, _stop.Token);
                            var request = Encoding.ASCII.GetString(buffer, 0, size);
                            if (!request.StartsWith("GET /api/channels HTTP/", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected HTTP request");
                            Interlocked.Increment(ref _served);
                            if (_mode == "timeout") { await Task.Delay(Timeout.Infinite, _stop.Token); return; }
                            var body = _mode == "unavailable" ? "c504-response-must-not-leak" : Body;
                            var status = _mode == "unavailable" ? "503 Service Unavailable" : "200 OK";
                            var bytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.ASCII.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
                            await accepted.SendAsync(bytes, SocketFlags.None, _stop.Token);
                        }
                    }, _stop.Token);
                }
            });
        }

        private static string FindProgram(string name)
        {
            foreach (var path in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                var candidate = Path.Combine(path, name);
                if (File.Exists(candidate)) return candidate;
                if (OperatingSystem.IsWindows() && File.Exists(candidate + ".exe")) return candidate + ".exe";
            }
            throw new InvalidOperationException($"Required tool {name} was not found");
        }

        private static string ToPosixPath(string path)
        {
            if (!OperatingSystem.IsWindows()) return path;
            var start = new ProcessStartInfo("cygpath") { RedirectStandardOutput = true };
            start.ArgumentList.Add("-u"); start.ArgumentList.Add(path);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("cygpath did not start");
            var result = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("cygpath failed");
            return result;
        }

        private static void WriteWrapper(string directory, string name, string source)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, source.ReplaceLineEndings("\n"));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public void Dispose()
        {
            _stop.Cancel();
            _socket.Dispose();
            try { _server?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
            _stop.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed record Run(int ShellExit, string[] ShellOutput, string OuterError, string[] AdapterNames,
        int LaterCalls, List<string> Journal, string ChildOutput, string Endpoint, int ServedRequests)
    {
        public int[] CurlExits => Journal.Where(x => x.StartsWith("curl-end|", StringComparison.Ordinal)).Select(x => int.Parse(x.Split('|')[2])).ToArray();
        public string[] SleepArguments => Journal.Where(x => x.StartsWith("sleep|", StringComparison.Ordinal)).Select(x => x[6..]).ToArray();
        public string Detail => $"shell={ShellExit}, curl=[{string.Join(',', CurlExits)}], sleeps=[{string.Join(',', SleepArguments)}], later={LaterCalls}, output={ChildOutput}";
    }
}
