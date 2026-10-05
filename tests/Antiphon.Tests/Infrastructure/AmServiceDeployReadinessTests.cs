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
            red.OuterError.ShouldContain("exit 7", Case.Sensitive, red.Detail);
            red.ChildOutput.ShouldContain("REMOTE DEPLOY VERDICT: failed", Case.Sensitive, red.Detail);
            red.Journal.Contains("inspect").ShouldBeTrue(red.Detail);
            Console.WriteLine("C504 baseline: 1 expected failure, 1 curl exit 7, 0 sleeps");
        }
        finally { File.Delete(baseline); }

        using (var fixture = new ReadinessFixture(2, "timeout-ready"))
        {
            var run = await fixture.RunAsync("V-1-accepted-timeout", Path.Combine(Root, "scripts", "deploy-am-service.ps1"));
            run.ShellExit.ShouldBe(0, "C1049-timeout-transition-succeeds: " + run.Detail);
            run.CurlExits.ShouldBe(new[] { 28, 28, 0 }, run.Detail);
            run.RequestsBeforeTransition.ShouldBe(2, run.Detail);
            run.ServedRequests.ShouldBe(3, run.Detail);
            AssertSuccess(run, 3, 2, new[] { 28 });
        }

        foreach (var readyAt in new[] { 3, 5 })
        {
            using var fixture = new ReadinessFixture(readyAt - 1, "ready");
            var run = await fixture.RunAsync("V-1", Path.Combine(Root, "scripts", "deploy-am-service.ps1"));
            AssertSuccess(run, readyAt, readyAt - 1);
            run.RequestsBeforeTransition.ShouldBe(0, run.Detail);
            run.ServedRequests.ShouldBe(1, run.Detail);
        }
    }

    [Test]
    public async Task FailsWhenHttpReadinessBudgetIsExhaustedAsync()
    {
        foreach (var mode in new[] { "refused", "timeout", "unavailable" })
        {
            using var fixture = new ReadinessFixture(0, mode);
            var run = await fixture.RunAsync("V-2", Path.Combine(Root, "scripts", "deploy-am-service.ps1"));
            var allowed = mode switch { "refused" => InitialRefusalCodes, "timeout" => new[] { 28 }, _ => new[] { 22 } };
            run.ShellExit.ShouldBe(44, run.Detail);
            run.CurlExits.Length.ShouldBe(5, run.Detail);
            run.CurlExits.ShouldAllBe(code => allowed.Contains(code), run.Detail);
            run.ServedRequests.ShouldBe(mode == "refused" ? 0 : 5, run.Detail);
            run.SleepArguments.ShouldBe(Enumerable.Repeat("2", 4), run.Detail);
            AssertCurlArguments(run);
            run.LaterCalls.ShouldBe(0, run.Detail);
            run.Journal.Contains("migration").ShouldBeFalse(run.Detail);
            run.Journal.Contains("logs").ShouldBeFalse(run.Detail);
            run.OuterError.ShouldContain("HTTP readiness was not confirmed", Case.Sensitive, run.Detail);
            run.OuterError.ShouldContain("/api/channels", Case.Sensitive, run.Detail);
            run.OuterError.ShouldContain("44", Case.Sensitive, run.Detail);
            run.ChildOutput.ShouldContain("REMOTE DEPLOY VERDICT: failed", Case.Sensitive, run.Detail);
            run.ChildOutput.ShouldNotContain("REMOTE DEPLOY VERDICT: ok", Case.Sensitive, run.Detail);
            run.ChildOutput.ShouldNotContain(Body, Case.Sensitive, run.Detail);
            run.ChildOutput.ShouldNotContain("c504-response-must-not-leak", Case.Sensitive, run.Detail);
            run.ShellOutput.ShouldBeEmpty(run.Detail);
            WriteProbeEvidence(run);
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

    [Test]
    public async Task ResolvesGitToolsWithoutUsrBinOnPathAsync()
    {
        if (!OperatingSystem.IsWindows())
            throw new TUnit.Core.Exceptions.SkipTestException("Native Windows Git tool discovery qualification");

        var parentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var parentTools = ResolveTools(parentPath, out var parentError);
        parentTools.ShouldNotBeNull("A complete Git installation must be discoverable before the supplied-PATH proof: " + parentError);
        var usrBin = parentTools!.Directory;
        var gitRoot = Directory.GetParent(usrBin)!.Parent!.FullName;
        Directory.Exists(usrBin).ShouldBeTrue("Git installation-relative usr/bin precondition");
        var suppliedPath = string.Join(Path.PathSeparator,
            parentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Where(path => !SamePath(path, usrBin))
                .Concat(new[] { Path.Combine(gitRoot, "cmd"), Path.Combine(gitRoot, "bin") }));
        FindProgram("cygpath", suppliedPath).ShouldBeNull("Supplied PATH must not resolve cygpath");
        var tools = ResolveTools(suppliedPath, out var error);
        tools.ShouldNotBeNull("C1049-toolchain-resolved-without-usrbin: " + error);
        foreach (var tool in new[] { tools!.Sh, tools.Sleep, tools.Cygpath! })
        {
            Path.IsPathFullyQualified(tool).ShouldBeTrue(tool);
            File.Exists(tool).ShouldBeTrue(tool);
            SamePath(Path.GetDirectoryName(tool)!, usrBin).ShouldBeTrue(tool);
        }
        tools.Curl.ShouldBe(FindProgram("curl", parentPath));
        Console.WriteLine($"C1049 tools: curl={tools.Curl}; sh={tools.Sh}; sleep={tools.Sleep}; cygpath={tools.Cygpath}");

        var directory = Path.Combine(Path.GetTempPath(), "c1049 path with spaces " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "sentinel with spaces.txt");
            await File.WriteAllTextAsync(file, "C1049-real-path-sentinel");
            var start = new ProcessStartInfo(tools.Sh) { RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment["PATH"] = tools.Directory + Path.PathSeparator + suppliedPath;
            foreach (var arg in new[] { "-c", "cat -- \"$1\"", "c1049", ToPosixPath(file, tools) }) start.ArgumentList.Add(arg);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Git sh did not start");
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await child.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); throw; }
            child.ExitCode.ShouldBe(0, await stderr);
            (await stdout).ShouldBe("C1049-real-path-sentinel");
            using var fixture = new ReadinessFixture(0, "ready", suppliedPath);
            var run = await fixture.RunAsync("V-3-sanitized-path", Path.Combine(Root, "scripts", "deploy-am-service.ps1"));
            AssertSuccess(run, 1, 0);
            run.ServedRequests.ShouldBe(1, run.Detail);
            Environment.GetEnvironmentVariable("PATH").ShouldBe(parentPath, "Parent PATH must remain byte-for-byte unchanged");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task ResolvesGitToolsPastIncompleteInstallationAsync()
    {
        if (!OperatingSystem.IsWindows())
            throw new TUnit.Core.Exceptions.SkipTestException("Native Windows Git tool discovery qualification");

        var parentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var directory = Path.Combine(Path.GetTempPath(), "c1060-" + Guid.NewGuid().ToString("N"));
        try
        {
            var incompleteRoot = Path.Combine(directory, "incomplete");
            var completeRoot = Path.Combine(directory, "complete");
            WriteFakeGitInstallation(incompleteRoot, complete: false);
            WriteFakeGitInstallation(completeRoot, complete: true);
            var curlDirectory = Path.Combine(directory, "tools");
            Directory.CreateDirectory(curlDirectory);
            var curl = Path.Combine(curlDirectory, "curl.exe");
            await File.WriteAllTextAsync(curl, "");
            var incompleteCmd = Path.Combine(incompleteRoot, "cmd");
            var privatePath = string.Join(Path.PathSeparator, incompleteCmd, curlDirectory, Path.Combine(completeRoot, "cmd"));
            SamePath(FindProgram("git", privatePath)!, Path.Combine(incompleteCmd, "git.exe"))
                .ShouldBeTrue("The first Git launcher must be the incomplete installation");
            FindProgram("sh", privatePath).ShouldBeNull("No sh launcher is exposed on the private PATH");

            var tools = ResolveTools(privatePath, out var error);
            tools.ShouldNotBeNull("C1060-incomplete-git-skipped: " + error);
            var usrBin = Path.Combine(completeRoot, "usr", "bin");
            SamePath(tools!.Directory, usrBin).ShouldBeTrue(tools.Directory);
            foreach (var tool in new[] { tools.Sh, tools.Sleep, tools.Cygpath! })
            {
                SamePath(Path.GetDirectoryName(tool)!, usrBin).ShouldBeTrue(tool);
                File.Exists(tool).ShouldBeTrue(tool);
            }
            SamePath(tools.Curl, curl).ShouldBeTrue(tools.Curl);
            error.ShouldBeEmpty();

            var parentTools = ResolveTools(parentPath, out var parentError);
            parentTools.ShouldNotBeNull("The Windows lane requires one complete Git installation: " + parentError);
            var realTools = ResolveTools(incompleteCmd + Path.PathSeparator + parentPath, out var realError);
            realTools.ShouldNotBeNull("C1060-incomplete-git-skipped-real-path: " + realError);
            SamePath(realTools!.Directory, parentTools!.Directory)
                .ShouldBeTrue("C1060-incomplete-git-skipped-real-path");
            Environment.GetEnvironmentVariable("PATH").ShouldBe(parentPath, "Parent PATH must remain byte-for-byte unchanged");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task RefusesWhenNoCompleteGitInstallationExistsAsync()
    {
        if (!OperatingSystem.IsWindows())
            throw new TUnit.Core.Exceptions.SkipTestException("Native Windows Git tool discovery qualification");

        var directory = Path.Combine(Path.GetTempPath(), "c1060-" + Guid.NewGuid().ToString("N"));
        try
        {
            var incompleteRoot = Path.Combine(directory, "incomplete");
            WriteFakeGitInstallation(incompleteRoot, complete: false);
            var curlDirectory = Path.Combine(directory, "tools");
            Directory.CreateDirectory(curlDirectory);
            await File.WriteAllTextAsync(Path.Combine(curlDirectory, "curl.exe"), "");
            var privatePath = string.Join(Path.PathSeparator, Path.Combine(incompleteRoot, "cmd"), curlDirectory);
            var tools = ResolveTools(privatePath, out var error);
            tools.ShouldBeNull("C1060-incomplete-only-refused");
            error.ShouldContain("Required Git for Windows prerequisites sh.exe, sleep.exe and cygpath.exe were not found in one installation's usr/bin; expose Git cmd/bin on the supplied PATH", Case.Sensitive);
            error.ShouldContain("Skipped incomplete Git installation " + incompleteRoot + ": missing sleep.exe, cygpath.exe", Case.Insensitive);
            error.ShouldNotContain("missing sh.exe", Case.Insensitive);
            using var fixture = new ReadinessFixture(0, "ready", privatePath);
            var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
                fixture.RunAsync("V-2-no-complete-git", Path.Combine(Root, "scripts", "deploy-am-service.ps1")));
            exception.Message.ShouldBe(error, "C1060-incomplete-only-fixture-throws");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void WriteFakeGitInstallation(string root, bool complete)
    {
        Directory.CreateDirectory(Path.Combine(root, "cmd"));
        var usrBin = Path.Combine(root, "usr", "bin");
        Directory.CreateDirectory(usrBin);
        File.WriteAllText(Path.Combine(root, "cmd", "git.exe"), "");
        foreach (var name in complete ? new[] { "sh.exe", "sleep.exe", "cygpath.exe" } : new[] { "sh.exe" })
            File.WriteAllText(Path.Combine(usrBin, name), "");
    }

    private static int[] InitialRefusalCodes => OperatingSystem.IsWindows() ? new[] { 7, 28 } : new[] { 7 };

    private static void AssertSuccess(Run run, int calls, int sleeps, int[]? initialCodes = null)
    {
        run.ShellExit.ShouldBe(0, run.Detail);
        var allowed = initialCodes ?? InitialRefusalCodes;
        run.CurlExits.Length.ShouldBe(calls, run.Detail);
        run.CurlExits.Take(calls - 1).ShouldAllBe(code => allowed.Contains(code), run.Detail);
        run.CurlExits.Last().ShouldBe(0, run.Detail);
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
        run.ChildOutput.ShouldContain("REMOTE DEPLOY VERDICT: ok", Case.Sensitive, run.Detail);
        WriteProbeEvidence(run);
    }

    private static void WriteProbeEvidence(Run run) => Console.WriteLine(
        $"C1049 probe mode={run.Mode} shell={run.ShellExit} curl=[{string.Join(',', run.CurlExits)}] " +
        $"sleeps=[{string.Join(',', run.SleepArguments)}] served={run.ServedRequests} beforeTransition={run.RequestsBeforeTransition}");

    private static void AssertCurlArguments(Run run)
    {
        var starts = run.Journal.Where(x => x.StartsWith("curl-start|", StringComparison.Ordinal)).ToArray();
        starts.Length.ShouldBe(run.CurlExits.Length, run.Detail);
        starts.Select(x => int.Parse(x.Split('|')[1])).ShouldBe(Enumerable.Range(1, starts.Length), run.Detail);
        foreach (var item in starts)
        {
            item.ShouldContain("--connect-timeout 2", Case.Sensitive, run.Detail);
            item.ShouldContain("--max-time 3", Case.Sensitive, run.Detail);
            item.ShouldContain("-fsS", Case.Sensitive, run.Detail);
            item.ShouldContain(run.Endpoint, Case.Sensitive, run.Detail);
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

    private sealed record ToolPaths(string Curl, string Sh, string Sleep, string? Cygpath, string Directory);

    private static string? FindProgram(string name, string searchPath) => FindPrograms(name, searchPath).FirstOrDefault();

    private static IEnumerable<string> FindPrograms(string name, string searchPath)
    {
        foreach (var entry in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.GetFullPath(Path.Combine(entry.Trim('"'), name));
            if (File.Exists(candidate)) yield return candidate;
            if (OperatingSystem.IsWindows() && File.Exists(candidate + ".exe")) yield return candidate + ".exe";
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left.Trim('"')).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right.Trim('"')).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static ToolPaths? ResolveTools(string originalPath, out string error)
    {
        // Resolve curl before admitting Git tools: native Windows curl remains the measured executable.
        var curl = FindProgram("curl", originalPath);
        error = "Required tool curl was not found on the original PATH";
        if (curl is null) return null;
        if (!OperatingSystem.IsWindows())
        {
            var sh = FindProgram("sh", originalPath);
            var sleep = FindProgram("sleep", originalPath);
            error = "Required tools sh and sleep must be present on PATH";
            return sh is null || sleep is null ? null : new ToolPaths(curl, sh, sleep, null, Path.GetDirectoryName(sh)!);
        }

        var candidates = originalPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => Path.GetFullPath(path.Trim('"'))).ToList();
        foreach (var launcher in FindPrograms("git", originalPath).Concat(FindPrograms("sh", originalPath)))
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(launcher)!);
            // Only installation-relative candidates, never a machine PATH edit or a disk search.
            var ancestor = directory;
            for (var depth = 0; depth < 3 && ancestor is not null; depth++, ancestor = ancestor.Parent)
                candidates.Add(Path.Combine(ancestor.FullName, "usr", "bin"));
        }
        var skipped = new List<string>();
        foreach (var directory in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var info = new DirectoryInfo(directory);
            if (!info.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(info.Parent?.Name, "usr", StringComparison.OrdinalIgnoreCase)) continue;
            var root = info.Parent!.Parent!.FullName;
            if (!File.Exists(Path.Combine(root, "cmd", "git.exe")) && !File.Exists(Path.Combine(root, "bin", "git.exe"))) continue;
            var sh = Path.Combine(directory, "sh.exe");
            var sleep = Path.Combine(directory, "sleep.exe");
            var cygpath = Path.Combine(directory, "cygpath.exe");
            var missing = new[] { "sh.exe", "sleep.exe", "cygpath.exe" }
                .Where(name => !File.Exists(Path.Combine(directory, name))).ToList();
            if (missing.Count == 0)
            {
                error = "";
                return new ToolPaths(curl, sh, sleep, cygpath, directory);
            }
            skipped.Add($"Skipped incomplete Git installation {root}: missing {string.Join(", ", missing)}");
        }
        error = "Required Git for Windows prerequisites sh.exe, sleep.exe and cygpath.exe were not found in one installation's usr/bin; expose Git cmd/bin on the supplied PATH";
        if (skipped.Count > 0) error += ". " + string.Join("; ", skipped);
        return null;
    }

    private static string ToPosixPath(string path, ToolPaths tools)
    {
        if (!OperatingSystem.IsWindows()) return path;
        var start = new ProcessStartInfo(tools.Cygpath!) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-u"); start.ArgumentList.Add("--"); start.ArgumentList.Add(path);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("cygpath did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("cygpath did not finish in 10 seconds");
        }
        var result = stdout.GetAwaiter().GetResult().Trim();
        var error = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException("cygpath failed: " + error);
        return result;
    }

    private sealed class ReadinessFixture : IDisposable
    {
        private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        private readonly CancellationTokenSource _stop = new();
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "c504-" + Guid.NewGuid().ToString("N"));
        private readonly int _refusals;
        private readonly string _mode;
        private readonly string _originalPath;
        private readonly List<Task> _handlers = new();
        private Task? _server;
        private Task? _transition;
        private int _served;
        private int _requestsBeforeTransition = -1;
        private int _ready;

        public ReadinessFixture(int refusals, string mode, string? originalPath = null)
        {
            _refusals = refusals;
            _mode = mode;
            _originalPath = originalPath ?? Environment.GetEnvironmentVariable("PATH") ?? "";
            Directory.CreateDirectory(_directory);
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            if (mode == "timeout-ready" || (mode != "refused" && refusals == 0)) Listen();
        }

        private string Endpoint => $"http://127.0.0.1:{((IPEndPoint)_socket.LocalEndPoint!).Port}/api/channels";
        private string JournalPath => Path.Combine(_directory, "journal.txt");

        private List<string> ReadJournal()
        {
            if (!File.Exists(JournalPath)) return new List<string>();
            // Poll the append-only shell journal without denying its writer's open handle.
            using var stream = new FileStream(JournalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            // A partial final line cannot acknowledge a completed attempt.
            return reader.ReadToEnd().Split('\n').SkipLast(1).Select(line => line.TrimEnd('\r')).ToList();
        }

        public async Task<Run> RunAsync(string caseName, string deploymentScript)
        {
            var tools = ResolveTools(_originalPath, out var toolError)
                ?? throw new InvalidOperationException(toolError);
            var initialCodes = _mode == "timeout-ready" ? new[] { 28 } : InitialRefusalCodes;
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
                admitted=false
                case "$C504_INITIAL_CODES" in *"|$code|"*) admitted=true ;; esac
                if [ "$admitted" = true ] && [ "$n" -eq "$C504_REFUSALS" ] && { [ "$C504_MODE" = ready ] || [ "$C504_MODE" = timeout-ready ]; }; then
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
            {
                WriteWrapper(wrappers, name, "#!/bin/sh\nprintf 'unexpected-native-" + name + "\\n' >> \"$C504_JOURNAL\"\nexit 94\n");
                if (OperatingSystem.IsWindows())
                    File.WriteAllText(Path.Combine(wrappers, name + ".cmd"), "@echo off\r\nexit /b 94\r\n");
            }

            var start = new ProcessStartInfo("pwsh") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(Root, "scripts", "test-deploy-am-service.ps1"), "-ReadinessCase", caseName, "-FixtureDirectory", _directory, "-DeploymentScript", deploymentScript }) start.ArgumentList.Add(arg);
            start.Environment["PATH"] = string.Join(Path.PathSeparator, new[] { wrappers, tools.Directory, _originalPath });
            start.Environment["C504_ENDPOINT"] = Endpoint;
            start.Environment["C504_JOURNAL"] = ToPosixPath(JournalPath, tools);
            start.Environment["C504_MIGRATIONS"] = ToPosixPath(Path.Combine(_directory, "migrations.txt"), tools);
            start.Environment["C504_COUNT"] = ToPosixPath(Path.Combine(_directory, "curl-count.txt"), tools);
            start.Environment["C504_ACK"] = ToPosixPath(Path.Combine(_directory, "listen-ack.txt"), tools);
            start.Environment["C504_REFUSALS"] = _refusals.ToString();
            start.Environment["C504_MODE"] = _mode;
            start.Environment["C504_INITIAL_CODES"] = "|" + string.Join('|', initialCodes) + "|";
            start.Environment["C504_REAL_CURL"] = ToPosixPath(tools.Curl, tools);
            start.Environment["C504_REAL_SLEEP"] = ToPosixPath(tools.Sleep, tools);
            start.Environment["C504_POSIX_FIXTURE"] = ToPosixPath(_directory, tools);
            foreach (var key in start.Environment.Keys.Where(x => x.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) || x.Contains("SECRET", StringComparison.OrdinalIgnoreCase) || x.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) || x.Contains("API_KEY", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("PowerShell did not start");
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(300));
            if ((_mode == "ready" || _mode == "timeout-ready") && _refusals > 0)
                _transition = Task.Run(async () => {
                    try
                    {
                        while (!_stop.IsCancellationRequested)
                        {
                            var journal = ReadJournal();
                            if (initialCodes.Any(code => journal.Contains($"curl-end|{_refusals}|{code}")))
                            {
                                _requestsBeforeTransition = Volatile.Read(ref _served);
                                _requestsBeforeTransition.ShouldBe(_mode == "timeout-ready" ? _refusals : 0, "Socket state before readiness transition");
                                if (_mode == "ready") Listen();
                                else Volatile.Write(ref _ready, 1);
                                File.WriteAllText(Path.Combine(_directory, "listen-ack.txt"), "ready");
                                return;
                            }
                            await Task.Delay(25, _stop.Token);
                        }
                    }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
                });
            try
            {
                await child.WaitForExitAsync(timeout.Token);
                if (_transition is not null)
                {
                    if (File.Exists(Path.Combine(_directory, "listen-ack.txt"))) await _transition.WaitAsync(TimeSpan.FromSeconds(10));
                    else _stop.Cancel(); // The baseline stops after its first refusal.
                }
                var output = await stdout + await stderr;
                child.ExitCode.ShouldBe(0, output);
                output.ShouldContain("PASS C504 " + caseName, Case.Sensitive);
                output.ShouldContain("DEPLOY-AM-SERVICE TESTS EXIT CODE: 0", Case.Sensitive);
                output.ShouldNotContain("FAIL ", Case.Sensitive);
                var result = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, "result.json"))).RootElement;
                var journal = ReadJournal();
                journal.Any(x => x.StartsWith("unexpected-", StringComparison.Ordinal)).ShouldBeFalse(output);
                var shellOutput = result.GetProperty("shellOutput").EnumerateArray().Select(x => x.GetString()!).ToArray();
                return new Run(result.GetProperty("shellExit").GetInt32(), shellOutput,
                    result.GetProperty("outerError").GetString()!, result.GetProperty("adapterNames").EnumerateArray().Select(x => x.GetString()!).ToArray(),
                    result.GetProperty("laterCalls").GetInt32(), journal, output, Endpoint, _served, _requestsBeforeTransition, _mode);
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
                    _handlers.Add(HandleRequestAsync(accepted));
                }
            });
        }

        private async Task HandleRequestAsync(Socket accepted)
        {
            try
            {
                using (accepted)
                {
                    var buffer = new byte[4096];
                    var size = await accepted.ReceiveAsync(buffer, SocketFlags.None, _stop.Token);
                    var request = Encoding.ASCII.GetString(buffer, 0, size);
                    if (!request.StartsWith("GET /api/channels HTTP/", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected HTTP request");
                    Interlocked.Increment(ref _served);
                    if (_mode == "timeout" || (_mode == "timeout-ready" && Volatile.Read(ref _ready) == 0))
                    { await Task.Delay(Timeout.Infinite, _stop.Token); return; }
                    var body = _mode == "unavailable" ? "c504-response-must-not-leak" : Body;
                    var status = _mode == "unavailable" ? "503 Service Unavailable" : "200 OK";
                    var bytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.ASCII.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
                    await accepted.SendAsync(bytes, SocketFlags.None, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
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
            try
            {
                try { Task.WhenAll(new[] { _server, _transition }.OfType<Task>()).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
                finally { Task.WhenAll(_handlers).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
            }
            finally
            {
                _stop.Dispose();
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    private sealed record Run(int ShellExit, string[] ShellOutput, string OuterError, string[] AdapterNames,
        int LaterCalls, List<string> Journal, string ChildOutput, string Endpoint, int ServedRequests,
        int RequestsBeforeTransition, string Mode)
    {
        public int[] CurlExits => Journal.Where(x => x.StartsWith("curl-end|", StringComparison.Ordinal)).Select(x => int.Parse(x.Split('|')[2])).ToArray();
        public string[] SleepArguments => Journal.Where(x => x.StartsWith("sleep|", StringComparison.Ordinal)).Select(x => x[6..]).ToArray();
        public string Detail => $"mode={Mode}, shell={ShellExit}, curl=[{string.Join(',', CurlExits)}], sleeps=[{string.Join(',', SleepArguments)}], served={ServedRequests}, beforeTransition={RequestsBeforeTransition}, later={LaterCalls}, output={ChildOutput}";
    }
}
