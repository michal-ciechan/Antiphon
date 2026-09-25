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
        await using var matching = Runtime();
        var started = await matching.StartAsync(Launch(actual), CancellationToken.None);
        started.SessionId.ShouldNotBe(Guid.Empty);
        matching.List().ShouldContain(session => session.SessionId == started.SessionId);

        await using var any = Runtime();
        var unrestricted = await any.StartAsync(Launch(null), CancellationToken.None);
        unrestricted.SessionId.ShouldNotBe(Guid.Empty);
        any.List().ShouldContain(session => session.SessionId == unrestricted.SessionId);
    }

    [Test]
    public async Task Invalid_requirement_is_refused_before_custody()
    {
        await using var runtime = Runtime();
        var refused = await Should.ThrowAsync<RunnerPlatformLaunchException>(() =>
            runtime.StartAsync(Launch("bsd"), CancellationToken.None));
        refused.Code.ShouldBe(RunnerPlatformLaunchGuard.Invalid);
        runtime.List().ShouldBeEmpty();
    }

    private static SessionRunnerRuntime Runtime() => new(
        Options.Create(new SessionRunnerSettings
        {
            SessionLogPath = TestSessionLogRoot.Create("c710-platform-launch"),
            PtyHostLingerHours = 0.02,
            CpuWatchdogEnabled = false,
            PtyBackend = "inbox",
        }),
        NullLogger<SessionRunnerRuntime>.Instance);

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
