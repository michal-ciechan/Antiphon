using System.Diagnostics;
using System.IO.Abstractions;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-0504/CARD-1049: the am-service deploy's technical verification retries /api/channels. Since unit 50 of
/// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md the deploy is the deploy-am-service command (AmServiceDeploy), run here
/// in process (the deleted driver scripts/test-deploy-am-service-readiness.ps1 ran the script in pwsh): the remote is a recording
/// launcher, except that the verification script the command sends runs locally under Git's sh against a loopback fixture, with
/// real curl and sleep and a journaling docker. The red baseline is that script with the retry replaced by the pre-retry single
/// curl (the frozen tests/Antiphon.Tests/Infrastructure/Fixtures/c504-deploy-am-service-baseline.ps1's verification, byte for byte).
/// The other cases of the deleted scripts/test-deploy-am-service.ps1 are DeployAmServiceTests.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AmServiceDeployReadinessTests
{
    private const string Body = "[{\"channel\":\"telegram\"},{\"channel\":\"slack\"}]";
    private const string TargetHost = "operator@gateway.example.invalid";
    private static string Root => DelegateScriptRunner.RepoRoot;

    // The retry block of AmServiceDeploy.VerifyScript, and what the pre-retry deploy-am-service.ps1 (commit 10fe51b0, blob 3a7f8565, the
    // deleted c504 fixture) ran instead: one curl, no connect timeout, no retry.
    private const string RetryBlock = "attempt=1\nwhile :; do\n  if channels=$(curl --connect-timeout 2 --max-time 3 -fsS http://localhost:18090/api/channels 2>/dev/null); then\n"
        + "    printf '%s\\n' \"$channels\"\n    break\n  fi\n  [ \"$attempt\" -lt 5 ] || exit 44\n  sleep 2\n  attempt=$((attempt + 1))\ndone\n";

    private const string BaselineBlock = "channels=$(curl -fsS http://localhost:18090/api/channels)\nprintf '%s\\n' \"$channels\"\n";

    // The baseline's verification with PollTimeoutSec 10, as that script built it.
    private const string BaselineVerify = """
        set -eu
        cd '/home/mc/antiphon-messaging'
        deadline=$(($(date +%s) + 10))
        while [ "$(docker inspect --format '{{.State.Running}}' am-service 2>/dev/null || true)" != true ]; do [ "$(date +%s)" -lt "$deadline" ] || exit 41; sleep 2; done
        channels=$(curl -fsS http://localhost:18090/api/channels)
        printf '%s\n' "$channels"
        docker compose exec -T am-postgres sh -c 'psql -X -At -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c '\''SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'\'''
        if docker logs --since 5m --tail 100 am-service 2>&1 | grep -Eiq 'Unhandled exception|fail:|crit:'; then exit 42; fi
        """;

    [Test]
    public async Task RetriesUntilHttpReadyAsync()
    {
        AmServiceDeploy.VerifyScript(10).Replace(RetryBlock, BaselineBlock).ShouldBe(BaselineVerify.ReplaceLineEndings("\n"), "the red baseline is the pre-retry verification");
        using (var probe = new ReadinessFixture(2, "ready"))
        {
            var red = await probe.RunAsync("V-1-baseline", baseline: true);
            red.ShellExit.ShouldBe(7, red.Detail);
            red.CurlExits.ShouldBe(new[] { 7 }, red.Detail);
            red.SleepArguments.ShouldBeEmpty(red.Detail);
            red.LaterCalls.ShouldBe(0, red.Detail);
            red.OuterError.ShouldContain("exit 7", Case.Sensitive, red.Detail);
            red.ChildOutput.ShouldContain("REMOTE DEPLOY VERDICT: failed", Case.Sensitive, red.Detail);
            red.Journal.Contains("inspect").ShouldBeTrue(red.Detail);
            Console.WriteLine("C504 baseline: 1 expected failure, 1 curl exit 7, 0 sleeps");
        }

        using (var fixture = new ReadinessFixture(2, "timeout-ready"))
        {
            var run = await fixture.RunAsync("V-1-accepted-timeout");
            run.ShellExit.ShouldBe(0, "C1049-timeout-transition-succeeds: " + run.Detail);
            run.CurlExits.ShouldBe(new[] { 28, 28, 0 }, run.Detail);
            run.RequestsBeforeTransition.ShouldBe(2, run.Detail);
            run.ServedRequests.ShouldBe(3, run.Detail);
            AssertSuccess(run, 3, 2, new[] { 28 });
        }

        foreach (var readyAt in new[] { 3, 5 })
        {
            using var fixture = new ReadinessFixture(readyAt - 1, "ready");
            var run = await fixture.RunAsync("V-1");
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
            var run = await fixture.RunAsync("V-2");
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
        using var fixture = new ReadinessFixture(0, "ready");
        var run = await fixture.RunAsync("V-3");
        AssertSuccess(run, 1, 0);
        run.ServedRequests.ShouldBe(1, run.Detail);
        // The deploy contract after a real verification run (the remaining driver cases are DeployAmServiceTests).
        foreach (var line in new[] { "Retained source backup: /home/mc/antiphon-messaging/build/src.bak-",
                     "Monitor build marker verified: readiness route Ready; sampled offsets numeric; ageSeconds=12.",
                     "Endpoint verified: 2 registered adapter(s): telegram, slack", "Startup-log scan verified: last 100 lines, no startup exception (redacted scan).",
                     AmServiceDeploy.SkippedTrafficLine })
            run.ChildOutput.ShouldContain(line, Case.Sensitive, run.Detail);
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
            var run = await fixture.RunAsync("V-3-sanitized-path");
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
            var exception = await Should.ThrowAsync<InvalidOperationException>(() => fixture.RunAsync("V-2-no-complete-git"));
            exception.Message.ShouldBe(error, "C1060-incomplete-only-fixture-throws");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    // The driver's real-archive rows: the production tar (resolved on PATH, as the command does) archives this checkout's
    // Dockerfile-derived sources without bin, obj or bin-* at any depth.
    [Test]
    public void ArchivesTheRealDockerfileSourcesWithTheProductionTar()
    {
        var directory = Path.Combine(Path.GetTempPath(), "am-service-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var files = new FileSystem();
            var context = Path.Combine(Root, "src");
            var manifest = AmServiceDeploy.Manifest(files, Path.Combine(context, "Antiphon.Messaging.Service", "Dockerfile"), context);
            var host = new NightlyHost
            {
                Clock = TimeProvider.System,
                Git = new FakeGit().Run,
                Launcher = new WatchedLauncher(),
                Processes = new FakeProcessTable(),
                Probe = new FakeHostProbe(),
                Out = TextWriter.Null,
                Error = TextWriter.Null,
                Trace = TextWriter.Null,
                Variables = _ => null,
            };
            var seams = new AmServiceDeploySeams { Http = (_, _) => throw new InvalidOperationException("no HTTP"), TempDirectory = () => directory };
            var archive = AmServiceDeploy.CreateArchive(host, files, seams, context, manifest);
            Path.GetDirectoryName(archive.Path).ShouldBe(directory);
            archive.Entries.ShouldNotBeEmpty("real Dockerfile archive is non-empty");
            archive.Entries.Where(entry => entry.TrimEnd('/').Split('/').Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("obj", StringComparison.OrdinalIgnoreCase) || segment.StartsWith("bin-", StringComparison.OrdinalIgnoreCase)))
                .ShouldBeEmpty("real Dockerfile archive excludes bin/obj/bin-* at every depth");
            manifest.Where(source => !archive.Entries.Any(entry => entry.TrimEnd('/') == source.Path || entry.StartsWith(source.Path + "/", StringComparison.Ordinal)))
                .ShouldBeEmpty("real Dockerfile archive contains every derived source");
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

        public async Task<Run> RunAsync(string caseName, bool baseline = false)
        {
            var tools = ResolveTools(_originalPath, out var toolError)
                ?? throw new InvalidOperationException(toolError);
            Console.WriteLine($"C504 fixture case={caseName} mode={_mode} pid={Environment.ProcessId} directory={_directory} " +
                $"curl={tools.Curl} sh={tools.Sh} sleep={tools.Sleep} cygpath={tools.Cygpath ?? "unused"}");
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

            // The verification shell's environment: this process's, without secrets, with the wrappers and Git's tools first.
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                environment[(string)entry.Key] = (string?)entry.Value ?? "";
            foreach (var key in environment.Keys.Where(x => x.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) || x.Contains("SECRET", StringComparison.OrdinalIgnoreCase) || x.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) || x.Contains("API_KEY", StringComparison.OrdinalIgnoreCase)).ToArray())
                environment.Remove(key);
            environment["PATH"] = string.Join(Path.PathSeparator, new[] { wrappers, tools.Directory, _originalPath });
            environment["C504_ENDPOINT"] = Endpoint;
            environment["C504_JOURNAL"] = ToPosixPath(JournalPath, tools);
            environment["C504_MIGRATIONS"] = ToPosixPath(Path.Combine(_directory, "migrations.txt"), tools);
            environment["C504_COUNT"] = ToPosixPath(Path.Combine(_directory, "curl-count.txt"), tools);
            environment["C504_ACK"] = ToPosixPath(Path.Combine(_directory, "listen-ack.txt"), tools);
            environment["C504_REFUSALS"] = _refusals.ToString();
            environment["C504_MODE"] = _mode;
            environment["C504_INITIAL_CODES"] = "|" + string.Join('|', initialCodes) + "|";
            environment["C504_REAL_CURL"] = ToPosixPath(tools.Curl, tools);
            environment["C504_REAL_SLEEP"] = ToPosixPath(tools.Sleep, tools);
            environment["C504_POSIX_FIXTURE"] = ToPosixPath(_directory, tools);

            var remote = new Remote(_directory, Endpoint, tools, environment, baseline);
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

            // The command, in process: one deploy pass (performDeploy, skipped traffic check, a 10 s container poll).
            var outerError = "";
            AmServiceDeployResult? result = null;
            await Task.Run(() =>
            {
                try { result = AmServiceDeploy.Deploy(Root, true, true, 10, TargetHost, remote.Host, new FileSystem(), remote.Seams); }
                catch (Exception e) { outerError = e.Message; }
            }).WaitAsync(TimeSpan.FromSeconds(300));
            if (_transition is not null)
            {
                if (File.Exists(Path.Combine(_directory, "listen-ack.txt"))) await _transition.WaitAsync(TimeSpan.FromSeconds(10));
                else _stop.Cancel(); // The baseline stops after its first refusal.
            }
            var output = remote.Output;
            Console.WriteLine("C504 verification result: case=" + caseName + " shellExit=" + remote.ShellExit + " laterCalls=" + remote.LaterCalls + " outerError=" + outerError);
            var shellExit = remote.ShellExit ?? throw new InvalidOperationException("Verification shell did not execute. " + outerError + "\n" + output);
            var journal = ReadJournal();
            journal.Any(x => x.StartsWith("unexpected-", StringComparison.Ordinal)).ShouldBeFalse(output);
            return new Run(shellExit, remote.ShellOutput, outerError, result?.AdapterNames.ToArray() ?? Array.Empty<string>(), remote.LaterCalls, journal, output,
                Endpoint, _served, _requestsBeforeTransition, _mode);
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

    /// <summary>
    /// The remote of one readiness run (the deleted readiness driver's runners): ssh answers each preflight and deploy command as a
    /// healthy gateway would, except the technical verification, which runs locally under Git's sh with the working directory and the
    /// /api/channels URL substituted (each exactly once) and its output and exit returned; scp checks the destination; tar writes a
    /// small archive and lists the manifest. The server identity and version are fixed answers.
    /// </summary>
    private sealed class Remote
    {
        private readonly string _directory;
        private readonly string _endpoint;
        private readonly ToolPaths _tools;
        private readonly IReadOnlyDictionary<string, string> _environment;
        private readonly bool _baseline;
        private readonly StringWriter _output = new();
        private readonly IReadOnlyList<AmServiceManifestEntry> _manifest;
        private bool _persisted;

        public Remote(string directory, string endpoint, ToolPaths tools, IReadOnlyDictionary<string, string> environment, bool baseline)
        {
            _directory = directory;
            _endpoint = endpoint;
            _tools = tools;
            _environment = environment;
            _baseline = baseline;
            _manifest = AmServiceDeploy.Manifest(new FileSystem(), Path.Combine(Root, "src", "Antiphon.Messaging.Service", "Dockerfile"), Path.Combine(Root, "src"));
        }

        public int? ShellExit { get; private set; }

        public string[] ShellOutput { get; private set; } = Array.Empty<string>();

        public int LaterCalls { get; private set; }

        public string Output => _output.ToString().ReplaceLineEndings("\n");

        public NightlyHost Host => new()
        {
            Clock = TimeProvider.System,
            Git = new FakeGit().Run,
            Launcher = new RecordingLauncher(Answer),
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = _output,
            Error = _output,
            Trace = TextWriter.Null,
            Variables = _ => null,
        };

        public AmServiceDeploySeams Seams => new()
        {
            Http = (url, _) =>
            {
                if (url.EndsWith("/api/version", StringComparison.Ordinal)) return new(200, "{\"version\":\"0123456789012345678901234567890123456789\"}");
                if (url.EndsWith("/api/channels/consumer", StringComparison.Ordinal))
                    return new(200, "{\"enabled\":true,\"consumerGroup\":\"antiphon-server-bridge\",\"inboundTopic\":\"channels.inbound\",\"brokers\":[{\"host\":\"am-redpanda\",\"port\":9092}]}");
                throw new InvalidOperationException("Unexpected HTTP runner request.");
            },
            TempDirectory = () => _directory,
            Resolve = name => name,
        };

        private static NightlyLaunchResult Lines(int exit, params string[] lines) =>
            new() { ExitCode = exit, LogText = lines.Length == 0 ? "" : string.Join("\n", lines) + "\n" };

        private NightlyLaunchResult Answer(NightlyLaunch launch)
        {
            switch (launch.FileName)
            {
                case "scp":
                    if (!launch.Arguments[1].StartsWith(TargetHost + ":/tmp/am-service-src-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected SCP destination.");
                    return Lines(0);
                case "tar":
                    if (launch.Arguments[0] == "-czf")
                    {
                        File.WriteAllText(Path.Combine(launch.WorkingDirectory, launch.Arguments[1]), "owned fake archive");
                        return Lines(0);
                    }
                    return Lines(0, _manifest.Select(entry => entry.Path).ToArray());
                case "ssh":
                    return Ssh(launch.Arguments[1]);
                default:
                    throw new InvalidOperationException("Unexpected native command: " + launch.FileName);
            }
        }

        private NightlyLaunchResult Ssh(string command)
        {
            if (command.Contains("C0410_GATEWAY_SETTINGS", StringComparison.Ordinal))
                return Lines(0, "{\"AntiphonConsumerGroup\":\"" + (_persisted ? "antiphon-server-bridge" : "antiphon-consumer") + "\",\"ExpectedAntiphonConsumerGroup\":"
                    + (_persisted ? "\"antiphon-server-bridge\"" : "null") + ",\"InboundTopic\":\"channels.inbound\",\"ConsumerGroup\":\"antiphon-messaging-service\",\"BootstrapServers\":\"am-redpanda:9092\"}");
            if (command.Contains("C0410_OVERRIDE", StringComparison.Ordinal)) return Lines(0, "{\"name\":\"docker-compose.override.yml\",\"exists\":false,\"managed\":true}");
            if (command.Contains("rpk cluster info", StringComparison.Ordinal)) return Lines(0, "{\"cluster_name\":\"redpanda.c504\"}");
            if (command.StartsWith("cd /home/mc/antiphon-messaging && docker compose config --no-interpolate", StringComparison.Ordinal))
                return Lines(0, "{\"context\":\"/home/mc/antiphon-messaging/build/src\",\"dockerfile\":\"Antiphon.Messaging.Service/Dockerfile\"}");
            if (command.StartsWith("docker inspect", StringComparison.Ordinal) && command.Contains("&& cd ", StringComparison.Ordinal))
                return Lines(0, "aaaaaaaaaaaa sha256:aaaaaaaaaaaa running", "{\"State\":\"running\"}");
            if (command.Contains("C0410_MANAGED", StringComparison.Ordinal)) { _persisted = true; return Lines(0); }
            if (command.Contains("docker compose build messaging-service", StringComparison.Ordinal)) return Lines(0);
            if (command.Contains("http://localhost:18090/api/channels", StringComparison.Ordinal)) return Shell(command);
            if (command.Contains("/health/inbound-unconsumed", StringComparison.Ordinal))
            {
                LaterCalls++;
                return Lines(0, "{\"state\":\"Ready\",\"watchedGroup\":\"antiphon-server-bridge\",\"expectedGroup\":\"antiphon-server-bridge\",\"topic\":\"channels.inbound\",\"ageSeconds\":12,\"partitions\":[{\"partition\":0,\"status\":\"CommittedOffset\",\"committedNextOffset\":12}]}", "200");
            }
            if (command.Contains("rpk group describe", StringComparison.Ordinal)) { LaterCalls++; return Lines(0, "channels.inbound 0 12 0 12 0"); }
            if (command.StartsWith("docker inspect", StringComparison.Ordinal)) { LaterCalls++; return Lines(0, "bbbbbbbbbbbb sha256:bbbbbbbbbbbb running"); }
            if (command.StartsWith("rm -f -- ", StringComparison.Ordinal)) return Lines(0);
            throw new InvalidOperationException("Unexpected remote runner command.");
        }

        // Invoke-C504Shell: the verification under Git's sh, with the deployment directory and the /api/channels URL substituted.
        private NightlyLaunchResult Shell(string command)
        {
            const string working = "cd '/home/mc/antiphon-messaging'";
            const string url = "http://localhost:18090/api/channels";
            static int Count(string text, string part) => (text.Length - text.Replace(part, "", StringComparison.Ordinal).Length) / part.Length;
            if (Count(command, working) != 1 || Count(command, url) != 1) throw new InvalidOperationException("Verification command substitution count was not one.");
            if (_baseline)
            {
                if (Count(command, RetryBlock) != 1) throw new InvalidOperationException("The retry block is not in the verification exactly once.");
                command = command.Replace(RetryBlock, BaselineBlock, StringComparison.Ordinal);
            }
            var posix = _environment["C504_POSIX_FIXTURE"];
            var text = command.Replace(working, "cd '" + posix + "' ", StringComparison.Ordinal).Replace(url, _endpoint, StringComparison.Ordinal);
            var shellFile = Path.Combine(_directory, "verification.sh");
            File.WriteAllText(shellFile, text.ReplaceLineEndings("\n") + "\n");
            var start = new ProcessStartInfo(_tools.Sh) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("exec sh '" + posix + "/verification.sh' 2>&1");
            start.Environment.Clear();
            foreach (var pair in _environment) start.Environment[pair.Key] = pair.Value;
            Console.WriteLine("C504 shell launch: executable=" + _tools.Sh + " script=" + shellFile);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("sh did not start");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(45000))
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException("Verification shell watchdog expired.");
            }
            Task.WaitAll(new Task[] { stdout, stderr }, 10000);
            var output = stdout.Result.TrimEnd('\r', '\n');
            if (stderr.Result.Length > 0)
                throw new InvalidOperationException("Shell launch wrote unexpected stderr: " + stderr.Result[..Math.Min(4096, stderr.Result.Length)]);
            ShellOutput = output.Length > 0 ? output.Split('\n').Select(line => line.TrimEnd('\r')).ToArray() : Array.Empty<string>();
            ShellExit = process.ExitCode;
            return Lines(process.ExitCode, ShellOutput);
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
