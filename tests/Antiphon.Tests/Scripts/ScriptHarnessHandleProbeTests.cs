using System.Diagnostics;
using System.Text.RegularExpressions;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ScriptHarnessHandleProbeTests
{
    [Test]
    [Arguments(0, false)]
    [Arguments(23, true)]
    public async Task Early_exit_reports_process_and_receipt_evidence(int exitCode, bool partialReceipt)
    {
        if (!OperatingSystem.IsLinux())
            throw new TUnit.Core.Exceptions.SkipTestException("This fake child uses a Linux shell.");

        var directory = Directory.CreateTempSubdirectory("handle-probe-witness-").FullName;
        var nonce = Guid.NewGuid().ToString("N");
        try
        {
            var start = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var script = Path.Combine(directory, "fake-handle-probe.sh");
            File.WriteAllText(script, partialReceipt
                ? "printf entered > \"$1/first-instruction\"; printf partial > \"$1/probe-receipt.tmp\"; " + OutputAndExit
                : "exit 0");
            start.ArgumentList.Add(script);
            start.ArgumentList.Add(directory);
            var startedUtc = DateTime.UtcNow;
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Fake handle probe did not start.");
            try
            {
                var stdout = new ScriptHarnessHandleProbe.OutputTail(child.StandardOutput);
                var stderr = new ScriptHarnessHandleProbe.OutputTail(child.StandardError);
                var error = await Should.ThrowAsync<InvalidOperationException>(() =>
                    ScriptHarnessHandleProbe.WaitAsync(directory, nonce, () => child.HasExited
                        ? new(child.Id, child.ExitCode, startedUtc, child.ExitTime.ToUniversalTime())
                        : null, start.ArgumentList.ToArray(), stdout, stderr));

                child.ExitCode.ShouldBe(exitCode);
                error.Message.ShouldContain($"exit code={exitCode}");
                Regex.IsMatch(error.Message, @"lifetime=[0-9]+(?:\.[0-9]+)? ms").ShouldBeTrue();
                error.Message.ShouldContain($"start time={startedUtc:O}");
                error.Message.ShouldContain("arguments=[");
                error.Message.ShouldContain("fake-handle-probe");
                error.Message.ShouldNotContain(start.FileName);
                error.Message.ShouldContain("receipt exists=False");
                error.Message.ShouldContain($"partial receipt exists={partialReceipt}");
                error.Message.ShouldContain($"first instruction marker exists={partialReceipt}");
                error.Message.ShouldContain("stdout");
                error.Message.ShouldContain("stderr");
                if (partialReceipt)
                {
                    error.Message.ShouldContain("stdout-tail");
                    error.Message.ShouldContain("stderr-tail");
                    error.Message.ShouldNotContain("stdout-head");
                    error.Message.ShouldNotContain("stderr-head");
                    error.Message.Length.ShouldBeLessThan(10_000);
                }
                else
                {
                    Regex.Matches(error.Message, @"\(empty\)").Count.ShouldBe(2);
                }

                var artifact = TestContext.Current!.Output.Artifacts.Single(a => a.DisplayName == $"handle-probe-{nonce}.txt");
                File.ReadAllText(artifact.File.FullName).ShouldBe(error.Message);
            }
            finally
            {
                if (!child.HasExited) child.Kill();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public async Task First_receipt_reporting_a_leaked_handle_fails_the_inheritance_assertion()
    {
        var directory = Directory.CreateTempSubdirectory("handle-probe-leak-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "probe-receipt"), "fake-nonce False False True 10 11 12");
            var receipt = await ScriptHarnessHandleProbe.WaitAsync(directory, "fake-nonce", () => null, [],
                new(StreamReader.Null), new(StreamReader.Null));
            var error = Should.Throw<ShouldAssertException>(() =>
                ScriptHarnessHandleProbe.AssertUnrelatedEventWasNotInherited(receipt));
            error.Message.ShouldContain("Child cannot signal the unrelated inheritable event.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private const string OutputAndExit = "printf 'stdout-head%8000sstdout-tail' ''; " +
        "printf 'stderr-head%8000sstderr-tail' '' >&2; exit 23";
}
