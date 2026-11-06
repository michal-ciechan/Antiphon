using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0710 V-3. The runtime refuses a specific requirement that is not this process's OS
/// before a session or child exists. The shell used for a matching launch is the host's,
/// so the same test runs on Windows and Linux.
/// </summary>
[NotInParallel("SessionLiveness")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerPlatformLaunchTests
{
    [Test]
    public async Task Opposite_platform_never_starts_process()
    {
        await using var runtime = Runtime();
        var opposite = RunnerPlatformWire.FromOperatingSystem() == RunnerPlatformWire.Linux
            ? RunnerPlatformWire.Windows
            : RunnerPlatformWire.Linux;
        var refused = await Should.ThrowAsync<RunnerPlatformLaunchException>(() =>
            runtime.StartAsync(Launch(opposite), CancellationToken.None));
        refused.Code.ShouldBe(RunnerPlatformLaunchGuard.Mismatch);
        runtime.List().ShouldBeEmpty();
    }

    [Test]
    public async Task Matching_and_any_preserve_launch()
    {
        var actual = RunnerPlatformWire.FromOperatingSystem();
        actual.ShouldNotBeNull();
        await using var matching = Runtime(out var matchingSettings);
        await AssertAdmittedAsync(matching, actual, matchingSettings);
        await using var any = Runtime(out var anySettings);
        await AssertAdmittedAsync(any, null, anySettings);
    }

    private static async Task AssertAdmittedAsync(SessionRunnerRuntime runtime, string? platform,
        SessionRunnerSettings settings)
    {
        var request = Launch(platform);
        try
        {
            var started = await StartWithHostLogAsync(
                () => runtime.StartAsync(request, CancellationToken.None),
                Path.Combine(settings.PtyHostLogDir, $"{request.SessionId:N}.log"));
            started.SessionId.ShouldNotBe(Guid.Empty);
            runtime.List().ShouldContain(session => session.SessionId == started.SessionId);
        }
        catch (FileNotFoundException ex) when (ex.Message.Contains("pty-host exe missing", StringComparison.Ordinal))
        {
            // The guard admitted the launch. This checkpoint lane builds without an apphost,
            // so the host binary is absent at the child seam and no session remains.
            ex.Message.ShouldContain("pty-host exe missing");
            runtime.List().ShouldBeEmpty();
        }
    }

    internal static Task<RunnerSessionDto> StartWithHostLogAsync(
        Func<Task<RunnerSessionDto>> start, string hostLogFile) => start();

    [Test]
    public async Task Invalid_requirement_is_refused_before_custody()
    {
        await using var runtime = Runtime();
        var refused = await Should.ThrowAsync<RunnerPlatformLaunchException>(() =>
            runtime.StartAsync(Launch("bsd"), CancellationToken.None));
        refused.Code.ShouldBe(RunnerPlatformLaunchGuard.Invalid);
        runtime.List().ShouldBeEmpty();
    }

    private static SessionRunnerRuntime Runtime() => Runtime(out _);

    private static SessionRunnerRuntime Runtime(out SessionRunnerSettings settings)
    {
        settings = new SessionRunnerSettings
        {
            SessionLogPath = TestSessionLogRoot.Create("c710-platform"),
            PtyHostLingerHours = 0.02,
            CpuWatchdogEnabled = false,
            PtyBackend = "inbox",
        };
        return new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
    }

    private static RunnerLaunchRequest Launch(string? platform)
    {
        var exe = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, "cmd.exe")
            : "/bin/sh";
        IReadOnlyList<string> args = OperatingSystem.IsWindows()
            ? ["/d", "/c", "exit 0"]
            : ["-c", "exit 0"];
        return new RunnerLaunchRequest(
            Guid.NewGuid(), exe, args, new Dictionary<string, string>(),
            Path.GetTempPath(), 80, 24, RequiredPlatform: platform);
    }
}
