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
            Directory.Delete(root, true);
        }
    }
}
