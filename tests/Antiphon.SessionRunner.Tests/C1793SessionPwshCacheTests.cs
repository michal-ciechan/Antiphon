using System.Diagnostics;
using System.Globalization;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1793. The runner's own XDG_CACHE_HOME reached every pty child, so every agent's pwsh
/// shared one JIT profile. Each pty-host session now gets its own cache, owned by its host and
/// deleted when the session exits; a cache the launch names itself is left alone.
/// </summary>
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Integration")]
public class C1793SessionPwshCacheTests
{
    private const string Variable = "XDG_CACHE_HOME";

    [Test]
    public async Task Each_pty_session_gets_a_host_owned_cache_that_its_exit_deletes()
    {
        var runtime = Runtime();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var firstOut = OutFile();
        var secondOut = OutFile();
        var started = new List<(Guid Id, RunnerSessionDto Dto)>();
        try
        {
            var firstDto = await runtime.StartAsync(Launch(first, firstOut, new Dictionary<string, string>()), CancellationToken.None);
            started.Add((first, firstDto));
            var firstTicks = HostStartTicks(firstDto);
            var secondDto = await runtime.StartAsync(Launch(second, secondOut, new Dictionary<string, string>()), CancellationToken.None);
            started.Add((second, secondDto));
            var secondTicks = HostStartTicks(secondDto);

            var firstSeen = (await ReadWhenWrittenAsync(firstOut)).Split('|');
            var secondSeen = (await ReadWhenWrittenAsync(secondOut)).Split('|');
            var runnerCache = Environment.GetEnvironmentVariable(Variable);
            foreach (var (seen, id, dto, ticks) in new[] { (firstSeen, first, firstDto, firstTicks), (secondSeen, second, secondDto, secondTicks) })
            {
                var directory = seen[0];
                Path.GetDirectoryName(Path.GetFullPath(directory)).ShouldBe(
                    Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar));
                Path.GetFileName(directory).ShouldStartWith("antiphon-pwsh-cache-session-" + id.ToString("N") + "-");
                directory.ShouldNotBe(runnerCache);
                var owner = seen[1].Split('@');
                owner[0].ShouldBe(dto.HostPid?.ToString(CultureInfo.InvariantCulture), "the host owns the session cache");
                owner.Length.ShouldBe(2, seen[1]);
                var recorded = long.Parse(owner[1], CultureInfo.InvariantCulture);
                Math.Abs(recorded - ticks).ShouldBeLessThanOrEqualTo(Antiphon.Hosting.PwshPrivateCache.StartMatchToleranceTicks);
            }
            firstSeen[0].ShouldNotBe(secondSeen[0]);

            await WaitGoneAsync(firstSeen[0]);
            await WaitGoneAsync(secondSeen[0]);
        }
        finally
        {
            await TearDownAsync(runtime, started);
            DeleteQuietly(firstOut);
            DeleteQuietly(secondOut);
        }
    }

    [Test]
    public async Task A_launch_that_names_its_cache_keeps_it()
    {
        var runtime = Runtime();
        var id = Guid.NewGuid();
        var output = OutFile();
        var named = Directory.CreateTempSubdirectory("c1793-named-cache-").FullName;
        var started = new List<(Guid Id, RunnerSessionDto Dto)>();
        try
        {
            var dto = await runtime.StartAsync(
                Launch(id, output, new Dictionary<string, string> { [Variable] = named }), CancellationToken.None);
            started.Add((id, dto));

            var seen = (await ReadWhenWrittenAsync(output)).Split('|');
            seen[0].ShouldBe(named);
            await WaitExitedAsync(runtime, id);
            Directory.Exists(named).ShouldBeTrue(named);
            Directory.GetDirectories(Path.GetTempPath(), "antiphon-pwsh-cache-session-" + id.ToString("N") + "-*")
                .ShouldBeEmpty();
        }
        finally
        {
            await TearDownAsync(runtime, started);
            DeleteQuietly(output);
            try { Directory.Delete(named, recursive: true); } catch { /* Best effort. */ }
        }
    }

    // The runner-child environment family (CARD-0628) already owns a registered pty test root.
    private static SessionRunnerRuntime Runtime() =>
        new(Options.Create(new SessionRunnerSettings
        {
            SessionLogPath = TestSessionLogRoot.Create("c628-token"),
            PtyHostLingerHours = 0.02,
            PtyBackend = "inbox",
        }), NullLogger<SessionRunnerRuntime>.Instance);

    private static RunnerLaunchRequest Launch(Guid id, string output, Dictionary<string, string> env)
    {
        var script = "$cache = $env:XDG_CACHE_HOME; $owner = ''; " +
            "$marker = if ($cache) { Join-Path $cache 'owner.pid' } else { '' }; " +
            "if ($marker -and (Test-Path -LiteralPath $marker)) { $owner = (Get-Content -Raw -LiteralPath $marker).Trim() }; " +
            $"[System.IO.File]::WriteAllText('{output}.tmp', $cache + '|' + $owner); " +
            $"[System.IO.File]::Move('{output}.tmp', '{output}')";
        return new RunnerLaunchRequest(id, ResolvePwsh(), ["-NoProfile", "-NonInteractive", "-Command", script],
            env, Path.GetTempPath(), Cols: 100, Rows: 25);
    }

    private static long HostStartTicks(RunnerSessionDto dto)
    {
        dto.HostPid.ShouldNotBeNull();
        using var host = Process.GetProcessById(dto.HostPid.Value);
        return host.StartTime.ToUniversalTime().Ticks;
    }

    private static string ResolvePwsh()
    {
        var name = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }
        throw new FileNotFoundException("pwsh is not on PATH.");
    }

    private static string OutFile() => Path.Combine(Path.GetTempPath(), $"c1793-cache-{Guid.NewGuid():N}.txt");

    private static async Task<string> ReadWhenWrittenAsync(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
                return await File.ReadAllTextAsync(path);
            await Task.Delay(100);
        }
        throw new TimeoutException($"the child never wrote {path} within 30s");
    }

    private static async Task WaitGoneAsync(string directory)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (Directory.Exists(directory))
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"session cache {directory} outlived its session by 30s");
            await Task.Delay(100);
        }
    }

    private static async Task WaitExitedAsync(SessionRunnerRuntime runtime, Guid id)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!runtime.SessionForTests(id).HasExited)
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"session {id} did not exit within 30s");
            await Task.Delay(100);
        }
    }

    private static async Task TearDownAsync(SessionRunnerRuntime runtime, List<(Guid Id, RunnerSessionDto Dto)> started)
    {
        foreach (var (id, dto) in started)
        {
            try { await TestSessionTeardown.KillAndAwaitHostExitAsync(runtime, id, dto.HostPid); }
            catch { /* The session may already have exited; the pid kill below is the backstop. */ }
        }
        await runtime.DisposeAsync();
        foreach (var (_, dto) in started)
        {
            if (dto.Pid is not int pid) continue;
            try { Process.GetProcessById(pid).Kill(entireProcessTree: true); }
            catch { /* Already gone. */ }
        }
    }

    private static void DeleteQuietly(string path)
    {
        try { File.Delete(path); } catch { /* Best effort. */ }
    }
}
