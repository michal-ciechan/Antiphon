using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public sealed class RunnerPlatformLaunchDiagnosticsTests
{
    [Test]
    public async Task Closed_launch_pipe_retains_original_failure_and_bounded_host_log()
    {
        var root = Path.Combine(Path.GetTempPath(), $"antiphon-platform-diagnostics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var log = Path.Combine(root, "host.log");
            File.WriteAllText(log, "discarded-prefix" + new string('x', 10_000) + "native-launch-failure");
            var original = new EndOfStreamException("pty-host pipe closed.");
            var attempts = 0;
            var failure = await Should.ThrowAsync<EndOfStreamException>(() =>
                RunnerPlatformLaunchTests.StartWithHostLogAsync(() =>
                {
                    attempts++;
                    return Task.FromException<RunnerSessionDto>(original);
                }, log));

            attempts.ShouldBe(1, "diagnostics must never retry or admit a failed launch");
            failure.Message.ShouldContain("native-launch-failure");
            failure.Message.ShouldContain("pty-host pipe closed.");
            failure.Message.ShouldNotContain("discarded-prefix");
            failure.Message.Length.ShouldBeLessThan(9000);
            failure.InnerException.ShouldBeSameAs(original);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task Missing_host_log_keeps_the_original_launch_failure()
    {
        var original = new EndOfStreamException("pty-host pipe closed.");
        var missing = Path.Combine(Path.GetTempPath(), $"antiphon-missing-host-{Guid.NewGuid():N}.log");
        var failure = await Should.ThrowAsync<EndOfStreamException>(() =>
            RunnerPlatformLaunchTests.StartWithHostLogAsync(
                () => Task.FromException<RunnerSessionDto>(original), missing));

        failure.Message.ShouldContain("pty-host pipe closed.");
        failure.Message.ShouldContain("host log unavailable (FileNotFoundException)");
        failure.InnerException.ShouldBeSameAs(original);
    }
}
