using System.Diagnostics;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public class C1022BackendLaunchTests
{
    [Test]
    public Task Unset_owned_host_runs_modern() => LaunchAsync(null, null, false);

    [Test]
    public Task Configured_owned_host_runs_modern() => LaunchAsync("modern", null, false);

    [Test]
    public Task Declared_modern_overrides_inherited_inbox() => LaunchAsync("modern", "inbox", true);

    private static async Task LaunchAsync(string? configured, string? ambient, bool direct)
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows native modern host proof");
        ConPtyRedistributable.TryLocate(out _, out var why).ShouldBeTrue(why);
        var previous = Environment.GetEnvironmentVariable(PtyBackendPolicy.EnvVar);
        var root = TestSessionLogRoot.Create("backend-seam");
        var sessionId = Guid.NewGuid();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PtyBackendPolicy.ConfigKey] = configured,
        }).Build();
        var settings = new SessionRunnerSettings
        {
            SessionLogPath = root,
            PtyHostLingerHours = 0.02,
            PtyBackend = direct ? configured : PtyBackendConfiguration.EffectiveRequest(config[PtyBackendPolicy.ConfigKey], ambient),
        };
        var handles = new List<Process>();
        Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, ambient);
        var runtime = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
        var owned = new TestOwnedPtyHost(settings.ResolvedPtyHostDir);
        try
        {
            try
            {
                var request = new RunnerLaunchRequest(sessionId, Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    ["/d", "/q", "/k", "@echo C1022-READY"], new Dictionary<string, string>(), root, 100, 25);
                var dto = await runtime.StartAsync(request, CancellationToken.None);
                owned.Capture(sessionId, dto.Backend, false);
                var logPath = Path.Combine(settings.PtyHostLogDir, $"{sessionId:N}.log");
                var watch = Stopwatch.StartNew();
                string log = "";
                while (watch.Elapsed < TimeSpan.FromSeconds(20))
                {
                    if (File.Exists(logPath))
                    {
                        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var reader = new StreamReader(stream);
                        log = await reader.ReadToEndAsync();
                        if (log.Contains("pty backend:")) break;
                    }
                    await Task.Delay(100);
                }
                log.ShouldContain("pty backend: ModernConPty", customMessage: "host-modern");
                log.ShouldContain($"requested '{configured ?? ""}'", customMessage: "host-request");
                log.ShouldContain("Microsoft.Windows.Console.ConPTY 1.24.260710001");
                var console = owned.Captured.Where(p => Path.GetFileName(p.Process.Image)
                    .Equals("OpenConsole.exe", StringComparison.OrdinalIgnoreCase)).ShouldHaveSingleItem();
                var dll = Path.Combine(Path.GetDirectoryName(console.Process.Image)!, "conpty.dll");
                var hashes = ConPtyRedistributable.VerifyShippedHashes(dll);
                hashes.Ok.ShouldBeTrue(hashes.Detail);
                var build = RunnerBuildIdentity.Resolve();
                var sha = build.CommitSha.ShouldNotBeNull("runner build SHA is required");
                var hostIdentity = owned.Captured.Single(p => p.Host).Process;
                using var host = Process.GetProcessById(hostIdentity.Pid);
                var loadedDll = host.Modules.Cast<ProcessModule>().Single(module =>
                    module.ModuleName.Equals("conpty.dll", StringComparison.OrdinalIgnoreCase)).FileName;
                string.Equals(loadedDll, dll, StringComparison.OrdinalIgnoreCase)
                    .ShouldBeTrue("host-modern: loaded DLL belongs to the observed OpenConsole pair");
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                    sessionId, hostIdentity.Pid, hostIdentity.StartedUtc, hostIdentity.Image,
                    childPid = dto.Pid, childStartedUtc = dto.StartedAt,
                    runnerBuild = DescribeBuild(typeof(SessionRunnerRuntime).Assembly.Location, sha),
                    hostBuild = DescribeBuild(Path.Combine(Path.GetDirectoryName(hostIdentity.Image)!, "Antiphon.PtyHost.dll"), sha),
                    loadedDll, openConsole = console.Process.Image, hashes = hashes.Detail,
                }));
                foreach (var cap in new[] { runtime.DescribeCapabilities(build, [SessionBackends.PtyHost], []),
                             new PhoneHomeRuntimeAdapter(runtime, build).Capabilities() })
                {
                    cap.PtyBackend.ShouldBe("ModernConPty", "host-modern");
                    cap.PtyBackendRequested.ShouldBe(configured ?? "", "host-request");
                    cap.PtyBackendFellBack.ShouldBeFalse();
                    cap.PtyBackendDeprecated.ShouldBe(false);
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(cap));
                }
                foreach (var process in owned.Captured) handles.Add(Process.GetProcessById(process.Process.Pid));
                Console.WriteLine($"C1022_HOST session={sessionId} host={dto.HostPid} child={dto.Pid} console={console.Process.Pid} started={console.Process.StartedUtc:O} path={console.Process.Image} {hashes.Detail}\n{log}");
            }
            finally
            {
                await owned.DisposeAsync(true,
                    () => owned.Capture(sessionId, SessionBackends.PtyHost, false),
                    async ct => { await runtime.KillAllAsync(TimeSpan.FromSeconds(2), ct); },
                    async () => await runtime.DisposeAsync());
            }
            foreach (var process in handles)
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                process.HasExited.ShouldBeTrue("owned host/child/OpenConsole physically exited");
            }
        }
        finally
        {
            foreach (var process in handles) process.Dispose();
            Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, previous);
            await DeleteRootAsync(root);
        }
    }

    private static async Task DeleteRootAsync(string root)
    {
        // Owned teardown above has already killed/waited the host tree and released its
        // handles. Windows can still briefly deny deletion of an exited host's images.
        // Retry only this fixture's directory; never hide a persistent cleanup failure.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
                Console.WriteLine($"C1022_CLEANUP deleted={root} attempts={attempt}");
                return;
            }
            catch (Exception ex) when (attempt < 20 && ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"C1022_CLEANUP retry={attempt} root={root} error={ex.GetType().Name}: {ex.Message}");
                await Task.Delay(100);
            }
        }
    }

    private static object DescribeBuild(string path, string expectedSha)
    {
        var version = FileVersionInfo.GetVersionInfo(path).ProductVersion;
        version.ShouldNotBeNull().ShouldContain(expectedSha, customMessage: "loaded host/runner build SHA");
        using var stream = File.OpenRead(path);
        return new { path, version, sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream)) };
    }
}
