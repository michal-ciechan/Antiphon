using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using Antiphon.SessionRunner;
using Microsoft.Extensions.Options;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CodexCliVersionProbeTests
{
    [Test]
    [NotInParallel]
    public async Task C1031_Stderr_notice_preserves_version()
    {
        using var kit = new CodexCliVersionTestFixture { Mode = "nonzero" };
        var probe = (CodexCliVersionProbe)kit.Probe!;
        await kit.Refresh();
        probe.Snapshot.CodexCliVersionError.ShouldBe("nonzero_exit");
        var failure = probe.Snapshot;
        kit.Clock.Advance(TimeSpan.FromMinutes(1));
        kit.Mode = "notice-held";
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var capturedOut = new StringWriter();
        using var capturedError = new StringWriter();
        try
        {
            Console.SetOut(capturedOut);
            Console.SetError(capturedError);
            var pending = kit.Refresh();
            try
            {
                await kit.WaitForReceiptAsync("notice-held");
                kit.Clock.Advance(TimeSpan.FromSeconds(1));
                pending.IsCompleted.ShouldBeFalse("C1031-completed-at held child");
                probe.Snapshot.ShouldBe(failure, "C1031-completed-at no early publication");
            }
            finally
            {
                File.WriteAllText(Path.Combine(kit.Root, "receipts-2", "release"), "release");
                await pending.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
        probe.Snapshot.CodexCliVersion.ShouldBe("0.160.0", "C1031-notice-version");
        probe.Snapshot.CodexCliVersionError.ShouldBe("stderr_output", "C1031-fixed-diagnostic");
        probe.Snapshot.CodexCliVersionCheckedAtUtc.ShouldBe(CodexCliVersionTestFixture.T.AddMinutes(1).AddSeconds(1),
            "C1031-completed-at");
        probe.Snapshot.CodexCliLauncherFingerprint.ShouldNotBeNull();
        probe.Snapshot.CodexCliLauncherFingerprint.Length.ShouldBe(64);
        probe.Snapshot.CodexCliLauncherFingerprint.All(Uri.IsHexDigit).ShouldBeTrue();
        kit.Children.Last().HasExited.ShouldBeTrue();
        kit.AuthOpens.ShouldBe(0);
        foreach (var secret in new[] { @"C:\Users\C1031\private", "/home/C1031/private", "C1031-token-canary" })
        {
            (capturedOut.ToString() + capturedError).ShouldNotContain(secret, customMessage: "C1031-no-console-leak");
            foreach (var json in new[] { JsonSerializer.Serialize(probe.Snapshot), kit.Local().GetRawText(), kit.Registration().GetRawText() })
                json.ShouldNotContain(secret, customMessage: "C1031-fixed-diagnostic no raw data");
        }
        var completed = probe.Snapshot.CodexCliVersionCheckedAtUtc;
        kit.Clock.Advance(TimeSpan.FromMinutes(1));
        for (var read = 0; read < 10; read++)
        {
            kit.Local().GetProperty("codexCliVersionCheckedAtUtc").GetDateTimeOffset().ShouldBe(completed!.Value);
            kit.Registration().GetProperty("codexCliVersionCheckedAtUtc").GetDateTimeOffset().ShouldBe(completed.Value);
        }
        kit.Starts.Count.ShouldBe(2);
        kit.Mode = "success";
        await kit.Refresh();
        probe.Snapshot.CodexCliVersion.ShouldBe("0.160.0");
        probe.Snapshot.CodexCliVersionError.ShouldBeNull("C1031-clear-advisory");
        probe.Snapshot.CodexCliVersionCheckedAtUtc.ShouldBe(CodexCliVersionTestFixture.T.AddMinutes(2).AddSeconds(1));
    }

    [Test]
    public void C959_Parses_and_orders_versions()
    {
        // Late binding permits this behavioral contract to run on the unchanged baseline.
        // An absent parser is unknown evidence, never a successful synthetic version.
        object? Parse(string? output) => typeof(RunnerCapabilitiesDto).Assembly
            .GetType("Antiphon.SessionRunner.Contracts.CodexCliVersion")?
            .GetMethod("ParseBanner", BindingFlags.Public | BindingFlags.Static)?
            .Invoke(null, [output]);
        foreach (var value in new[] { "0.156.1", "0.159.0", "0.159.1", "0.160.0", "0.9.0", "0.1000.0",
                     "0.159.1-beta.1", "0.160.0-beta.1", "0.159.1+build.7" })
        {
            foreach (var terminator in new[] { "", "\n", "\r\n" })
                (Parse("codex-cli " + value + terminator)?.ToString())
                    .ShouldBe(value, "C959-v01-numeric " + value);
        }
        foreach (var banner in new[] { "codex 0.160.0", "codex version 0.160.0",
                     "codex-cli version v0.160.0", "CODEX-CLI 0.160.0" })
            (Parse(banner)?.ToString()).ShouldBe("0.160.0", "C959-v01-banner " + banner);

        var invalid = new (string? Output, int Guard)[]
        {
            ("codex-cli 0.159", 5), ("codex-cli 2147483648.0.0", 6),
            ("codex-cli 00.159.1", 7), ("codex-cli 0.159.1-beta.01", 8),
            ("codex-cli 0.159.1-bad_id", 9), ("codex-cli 0.159.1+build..7", 10),
            ("prefix codex-cli 0.160.0", 12), ("codex-cli 0.160.0 extra", 13),
            (null, 11), ("", 11), ("  ", 11), ("0.160.0", 12),
            ("codex-cli -1.159.1", 5),
            ("codex-cli 0.159.1-", 10), ("codex-cli 0.159.1+", 10),
            ("codex-cli 0.159.1-.beta", 10), ("codex-cli 0.159.1+_", 9)
        };
        foreach (var (output, guard) in invalid)
            Parse(output).ShouldBeNull($"C1031-whole-line C959-pc-{guard:000} {output}");
        foreach (var separator in new[] { "\n", "\r\n" })
        foreach (var terminator in new[] { "", separator })
            (Parse(string.Join(separator, "notice", "", "codex-cli banana", "codex-cli 0.160.0", "notice",
                    "codex-cli 00.160.0") + terminator)?.ToString()).ShouldBe("0.160.0", "C1031-lines");
        (Parse("codex-cli 0.159.1\ncodex-cli 0.160.0\n")?.ToString()).ShouldBe("0.159.1", "C1031-first");
        (Parse("codex-cli 0.160.0\ncodex-cli 0.160.0\n")?.ToString()).ShouldBe("0.160.0", "C1031-lines duplicates");
        (Parse("codex-cli 0.160.0\n\n")?.ToString()).ShouldBe("0.160.0", "C1031-lines blank");
        foreach (var output in new[] { "prefix codex-cli 0.160.0", " codex-cli 0.160.0", "codex-cli 0.160.0 ",
                     "codex-cli 0.160.0\r", "codex-cli 0.160.0 extra", "0.160.0" })
            Parse(output).ShouldBeNull("C1031-whole-line " + output);

        foreach (var (left, right, expected, guard) in new[]
                 {
                     ("0.9.0", "0.159.1", -1, 1), ("0.1000.0", "0.159.1", 1, 1),
                     ("0.159.1-beta.1", "0.159.1", -1, 2),
                     ("0.160.0-beta.1", "0.159.1", 1, 2),
                     ("0.159.1-beta.2", "0.159.1-beta.10", -1, 3),
                     ("0.159.1+build.7", "0.159.1", 0, 4),
                     ("0.159.0", "0.159.1", -1, 1), ("0.159.1", "0.159.1", 0, 1)
                 })
        {
            var a = Parse("codex-cli " + left);
            var b = Parse("codex-cli " + right);
            a.ShouldNotBeNull("C959-v01-left " + left);
            b.ShouldNotBeNull("C959-v01-right " + right);
            Math.Sign(((IComparable)a!).CompareTo(b)).ShouldBe(expected, $"C959-pc-{guard:000} {left}/{right}");
            Math.Sign(((IComparable)b!).CompareTo(a)).ShouldBe(-expected, "C959-v01-reverse " + left);
        }
    }

    [Test]
    [NotInParallel]
    public async Task C959_Probe_is_version_only_and_auth_free()
    {
        using var kit = new CodexCliVersionTestFixture { Mode = "stdin" };
        using var inherited = new SyntheticProbeEnvironment(kit.Root);
        var sample = await kit.Attempt();
        CodexCliVersionTestFixture.Text(sample, "codexCliVersion").ShouldBe("0.160.0", "C959-v02-argv");
        var start = kit.Starts.Single();
        start.ArgumentList.ToArray().ShouldBe(["--version"], "C959-pc-014");
        start.UseShellExecute.ShouldBeFalse("C959-pc-016");
        foreach (var (name, label) in new[] { ("OPENAI_API_KEY", 17), ("ANTIPHON_TASK_TOKEN", 18),
                     ("HTTPS_PROXY", 19), ("HTTP_PROXY", 19), ("NODE_OPTIONS", 20), ("NODE_PATH", 20),
                     ("BASH_ENV", 21), ("ENV", 21), ("DOTNET_STARTUP_HOOKS", 21) })
            start.Environment.ContainsKey(name).ShouldBeFalse($"C959-pc-{label:000} {name}");
        start.Environment["CODEX_HOME"].ShouldNotBe(Environment.GetEnvironmentVariable("CODEX_HOME"), "C959-pc-022");
        kit.EmptyHomes.Single().ShouldBeTrue("C959-pc-022 empty");
        start.WorkingDirectory.ShouldBe(Path.GetDirectoryName(start.Environment["CODEX_HOME"]!), "C959-pc-023 neutral scratch");
        start.WorkingDirectory.ShouldNotBe(kit.Root, "C959-pc-023");
        File.Exists(Path.Combine(kit.Root, "receipts-1", "stdin-eof")).ShouldBeTrue("C959-pc-024");
        kit.AuthOpens.ShouldBe(0, "C959-pc-025");
        CodexCliVersionTestFixture.Text(sample, "codexCliVersionError").ShouldBeNull("C959-pc-199");
        var absolute = await kit.Attempt(kit.Executable);
        CodexCliVersionTestFixture.Text(absolute, "codexCliVersion").ShouldBe("0.160.0", "C959-v02-native");
        var a = Path.Combine(kit.Root, "A");
        var b = Path.Combine(kit.Root, "B");
        Directory.CreateDirectory(a); Directory.CreateDirectory(b);
        var executableName = OperatingSystem.IsWindows() ? "codex.exe" : "codex";
        var firstExe = Path.Combine(a, executableName);
        var secondExe = Path.Combine(b, executableName);
        File.Copy(kit.Executable, firstExe); File.Copy(kit.Executable, secondExe);
        kit.ChildMode = info => info.FileName == secondExe ? "version-old" : "success";
        var pathA = a + Path.PathSeparator + b;
        var pathB = b + Path.PathSeparator + a;
        var first = await kit.Attempt(executableName, path: pathA);
        CodexCliVersionTestFixture.Text(first, "codexCliVersion").ShouldBe("0.160.0", "C959-v02-path-A");
        kit.Starts.Last().FileName.ShouldBe(firstExe, "C959-v02-selected-A");
        var second = await kit.Attempt(executableName, path: pathB);
        CodexCliVersionTestFixture.Text(second, "codexCliVersion").ShouldBe("0.156.1", "C959-v02-path-B");
        kit.Starts.Last().FileName.ShouldBe(secondExe, "C959-v02-selected-B");
        CodexCliVersionTestFixture.Text(second, "codexCliLauncherFingerprint")
            .ShouldNotBe(CodexCliVersionTestFixture.Text(first, "codexCliLauncherFingerprint"), "C959-v02-path-invalidates");
        var explicitB = await kit.Attempt(secondExe, path: pathA);
        CodexCliVersionTestFixture.Text(explicitB, "codexCliVersion").ShouldBe("0.156.1", "C959-pc-153");
        foreach (var evidence in new[] { first, second, explicitB })
            evidence.GetRawText().Contains("C959-env-canary", StringComparison.Ordinal).ShouldBeFalse("C959-pc-199");
        kit.ChildMode = info => info.FileName == kit.Executable ? "version-old" : "success";
        await kit.Refresh();
        var selectedCurrent = await kit.Attempt(firstExe);
        CodexCliVersionTestFixture.Text(selectedCurrent, "codexCliVersion").ShouldBe("0.160.0", "C959-pc-154");
        kit.ChildMode = _ => "success";
        await kit.Refresh();
        var missingSelected = await kit.Attempt(Path.Combine(kit.Root, "missing-codex"));
        CodexCliVersionTestFixture.Text(missingSelected, "codexCliVersion").ShouldBeNull("C959-pc-155");
        CodexCliVersionTestFixture.Text(missingSelected, "codexCliVersionError").ShouldBe("executable_missing", "C959-pc-155 error");
        var beforeCwds = kit.Starts.Count;
        var cwdA = await kit.Attempt(kit.Executable, resolutionCwd: a);
        var cwdB = await kit.Attempt(kit.Executable, resolutionCwd: b);
        CodexCliVersionTestFixture.Text(cwdA, "codexCliLauncherFingerprint")
            .ShouldNotBe(CodexCliVersionTestFixture.Text(cwdB, "codexCliLauncherFingerprint"), "C959-pc-205");
        kit.Starts.Count.ShouldBe(beforeCwds + 2, "C959-pc-205 two real attempts");
        var missingCwd = await kit.Attempt(executableName, resolutionCwd: Path.Combine(kit.Root, "future-worktree"));
        CodexCliVersionTestFixture.Text(missingCwd, "codexCliVersionError").ShouldBe("launcher_unverified", "C959-pc-158");
        kit.Starts.Count.ShouldBe(beforeCwds + 2, "C959-pc-158 no ancestor fallback child");
        using var diagnostic = new CodexCliVersionTestFixture { Mode = "diagnostic" };
        var previousError = Console.Error;
        using var captured = new StringWriter();
        JsonElement failed;
        try
        {
            Console.SetError(captured);
            failed = await diagnostic.Attempt();
        }
        finally { Console.SetError(previousError); }
        captured.ToString().ShouldNotContain("C959-diagnostic-sentinel", customMessage: "C959-pc-026");
        failed.GetRawText().ShouldNotContain("C959-diagnostic-sentinel", customMessage: "C959-pc-199");
        CodexCliVersionTestFixture.Text(failed, "codexCliVersionError").ShouldBe("stderr_output");
        CodexCliVersionTestFixture.Text(failed, "codexCliVersion").ShouldBe("0.160.0", "C1031-diagnostic-version");
    }

    [Test]
    public async Task C959_Failure_bounds_and_cleanup()
    {
        using var kit = new CodexCliVersionTestFixture { Mode = "nonzero" };
        foreach (var (mode, error, label) in new[] { ("nonzero", "nonzero_exit", 28),
                     ("stdout-flood", "output_truncated", 30),
                     ("stderr-flood", "invalid_output", 31) })
        {
            kit.Mode = mode;
            var sample = await kit.Attempt();
            CodexCliVersionTestFixture.Text(sample, "codexCliVersionError").ShouldBe(error, $"C959-pc-{label:000}");
            CodexCliVersionTestFixture.Text(sample, "codexCliVersion").ShouldBeNull("C959-v03-failure " + mode);
            kit.Children.Last().HasExited.ShouldBeTrue("C959-v03-reaped " + mode);
        }
        var missing = await kit.Attempt(Path.Combine(kit.Root, "missing"));
        CodexCliVersionTestFixture.Text(missing, "codexCliVersionError").ShouldBe("executable_missing", "C959-pc-027");

        foreach (var mode in new[] { "timeout", "tree" })
        {
            using var held = new CodexCliVersionTestFixture { Mode = mode };
            var pending = held.Attempt();
            await held.WaitForReceiptAsync(mode == "tree" ? "leaf" : "timeout");
            pending.IsCompleted.ShouldBeFalse("C959-v03-held");
            held.Clock.Advance(TimeSpan.FromSeconds(5));
            (await CompletedAsync(pending, TimeSpan.FromSeconds(10))).ShouldBeTrue("C959-pc-032 deadline");
            var timedOut = await pending;
            CodexCliVersionTestFixture.Text(timedOut, "codexCliVersionError").ShouldBe("timeout", "C959-pc-032");
            CodexCliVersionTestFixture.Text(timedOut, "codexCliVersion").ShouldBeNull("C959-v03-timeout");
            held.ReceiptIsAlive(mode).ShouldBeFalse("C959-v03-parent");
            if (mode == "tree") held.ReceiptIsAlive("leaf").ShouldBeFalse("C959-pc-033");
        }
        using (var held = new CodexCliVersionTestFixture { Mode = "tree" })
        {
            using var caller = new CancellationTokenSource();
            var pending = held.Attempt(ct: caller.Token);
            await held.WaitForReceiptAsync("leaf");
            caller.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(async () => await pending, "C959-pc-037");
            held.ReceiptIsAlive("leaf").ShouldBeFalse("C959-v03-cancel-reaped");
        }
        using (var held = new CodexCliVersionTestFixture { Mode = "tree" })
        {
            var probe = (CodexCliVersionProbe)held.Probe!;
            var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            probe.StopTreeAsync = (_, _) => { cleanupEntered.TrySetResult(); return release.Task; };
            var pending = held.Attempt();
            await held.WaitForReceiptAsync("leaf");
            held.Clock.Advance(TimeSpan.FromSeconds(5));
            await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            held.Clock.Advance(TimeSpan.FromSeconds(2));
            (await CompletedAsync(pending, TimeSpan.FromSeconds(5))).ShouldBeTrue("C959-pc-034 deadline");
            var unknown = await pending;
            CodexCliVersionTestFixture.Text(unknown, "codexCliVersion").ShouldBeNull("C959-pc-035");
            CodexCliVersionTestFixture.Text(unknown, "codexCliVersionError").ShouldBe("cleanup_unconfirmed", "C959-pc-034");
            probe.OwnedCleanupCount.ShouldBe(1, "C959-pc-036");
            release.TrySetResult(false);
            var limit = Stopwatch.StartNew();
            while (probe.OwnedCleanupCount != 0 && limit.Elapsed < TimeSpan.FromSeconds(5))
            {
                await probe.ReapAsync();
                await Task.Delay(10);
            }
            probe.OwnedCleanupCount.ShouldBe(0, "C959-v03-reaper");
            held.ReceiptIsAlive("leaf").ShouldBeFalse("C959-v03-cleanup-release");
        }
        // The parent really exits while its descendant still owns redirected pipes.
        // Use the production clock here so both deadline continuations are independently armed.
        using (var exitedParent = new CodexCliVersionTestFixture(TimeProvider.System) { Mode = "parent-exits" })
        {
            var pending = exitedParent.Attempt();
            await exitedParent.WaitForReceiptAsync("leaf");
            await exitedParent.Children.Single().WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            pending.IsCompleted.ShouldBeFalse("C959-parent-exit-is-not-pipe-completion");
            var unresolved = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            CodexCliVersionTestFixture.Text(unresolved, "codexCliVersion").ShouldBeNull("C959-pc-035 exited-parent");
            CodexCliVersionTestFixture.Text(unresolved, "codexCliVersionError").ShouldBe("cleanup_unconfirmed", "C959-pc-034 exited-parent");
            var probe = (CodexCliVersionProbe)exitedParent.Probe!;
            probe.OwnedCleanupCount.ShouldBe(1, "C959-pc-036 exited-parent retains ownership");
            exitedParent.ReceiptIsAlive("leaf").ShouldBeTrue("C959-parent-exit-real-unresolved-child");
            exitedParent.RescueLeaves();
            var deadline = Stopwatch.StartNew();
            while (probe.OwnedCleanupCount != 0 && deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                await probe.ReapAsync();
                await Task.Delay(10);
            }
            probe.OwnedCleanupCount.ShouldBe(0, "C959-parent-exit-cleanup-completed");
        }
    }

    [Test]
    public async Task C1031_Truncated_notices_preserve_complete_version()
    {
        using var kit = new CodexCliVersionTestFixture();
        foreach (var (mode, version, error, label) in new (string, string?, string?, string)[]
        {
            ("stderr-4096", "0.160.0", "stderr_output", "C1031-byte-cap"),
            ("stderr-4097", "0.160.0", "output_truncated", "C1031-byte-cap C1031-stderr-overflow C1031-truncation-priority"),
            ("stdout-notices", "0.160.0", "output_truncated", "C1031-stdout-overflow C1031-truncation-priority"),
            ("outside-cap", null, "output_truncated", "C1031-byte-cap outside"),
            ("fragment", null, "output_truncated", "C1031-no-fragment"),
            ("lf-cap", "0.160.0", null, "C1031-byte-cap LF"),
            ("lf-cap-overflow", "0.160.0", "output_truncated", "C1031-byte-cap LF overflow"),
            ("crlf-cap", "0.160.0", null, "C1031-byte-cap CRLF"),
            ("crlf-split", null, "output_truncated", "C1031-no-fragment CRLF"),
            ("eof-cap", "0.160.0", null, "C1031-byte-cap EOF"),
            ("eof-stderr-overflow", "0.160.0", "output_truncated", "C1031-stderr-overflow EOF"),
            ("both-floods", "0.160.0", "output_truncated", "C1031-both-drained"),
        })
        {
            kit.Mode = mode;
            var pending = kit.Attempt();
            (await CompletedAsync(pending, TimeSpan.FromSeconds(10))).ShouldBeTrue(label + " bounded completion");
            var sample = await pending;
            CodexCliVersionTestFixture.Text(sample, "codexCliVersion").ShouldBe(version, label);
            CodexCliVersionTestFixture.Text(sample, "codexCliVersionError").ShouldBe(error, label);
            kit.Children.Last().HasExited.ShouldBeTrue(label + " child exited");
        }
    }

    [Test]
    public async Task C1031_Invalid_or_failed_output_stays_unknown()
    {
        using var kit = new CodexCliVersionTestFixture();
        foreach (var (mode, error, label) in new[]
        {
            ("nonzero-notice", "nonzero_exit", "C1031-exit-precedence"),
            ("nonzero-stdout", "nonzero_exit", "C1031-exit-precedence stdout"),
            ("nonzero-stderr", "nonzero_exit", "C1031-exit-precedence stderr"),
            ("nonzero-both", "nonzero_exit", "C1031-exit-precedence both"),
            ("stderr-banner", "invalid_output", "C1031-stdout-only"),
            ("malformed-stdout", "invalid_output", "C1031-stdout-only malformed"),
            ("empty", "invalid_output", "C1031-no-version-token empty"),
            ("stderr-flood", "invalid_output", "C1031-no-version-token"),
            ("stdout-flood", "output_truncated", "C1031-no-version-token stdout"),
        })
        {
            kit.Mode = mode;
            var sample = await kit.Attempt().WaitAsync(TimeSpan.FromSeconds(10));
            CodexCliVersionTestFixture.Text(sample, "codexCliVersion").ShouldBeNull(label);
            CodexCliVersionTestFixture.Text(sample, "codexCliVersionError").ShouldBe(error, label);
            kit.Children.Last().HasExited.ShouldBeTrue(label + " child exited");
        }
    }

    [Test]
    public async Task C959_Refresh_replaces_evidence()
    {
        using var kit = new CodexCliVersionTestFixture();
        await kit.Refresh();
        CodexCliVersionTestFixture.Text(kit.Local(), "codexCliVersion").ShouldBe("0.160.0", "C959-v04-replaced");
        var at = CodexCliVersionTestFixture.Text(kit.Local(), "codexCliVersionCheckedAtUtc");
        kit.Clock.Advance(TimeSpan.FromMinutes(1));
        for (var i = 0; i < 10; i++)
            CodexCliVersionTestFixture.Text(kit.Local(), "codexCliVersionCheckedAtUtc").ShouldBe(at, "C959-pc-041");
        kit.Starts.Count.ShouldBe(1, "C959-pc-060");
        kit.Mode = "nonzero";
        kit.Clock.Advance(TimeSpan.FromMinutes(4));
        await kit.Refresh();
        CodexCliVersionTestFixture.Text(kit.Local(), "codexCliVersion").ShouldBeNull("C959-pc-039");
        CodexCliVersionTestFixture.Text(kit.Local(), "codexCliVersionError").ShouldBe("nonzero_exit", "C959-v04-failed");
        var samples = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => kit.Attempt(force: false)));
        kit.Starts.Count.ShouldBe(2, "C959-pc-043");
        samples.All(s => CodexCliVersionTestFixture.Text(s, "codexCliVersion") is null).ShouldBeTrue("C959-v04-single-flight");

        using (var reboot = new CodexCliVersionTestFixture())
        {
            var sameInstallation = await reboot.Attempt(kit.Executable, resolutionCwd: kit.Root);
            CodexCliVersionTestFixture.Text(sameInstallation, "codexCliLauncherFingerprint")
                .ShouldNotBe(CodexCliVersionTestFixture.Text(samples[0], "codexCliLauncherFingerprint"), "C959-pc-049 same installation, new boot");
        }
        var oldFingerprint = CodexCliVersionTestFixture.Text(samples[0], "codexCliLauncherFingerprint");
        File.SetLastWriteTimeUtc(kit.Executable, File.GetLastWriteTimeUtc(kit.Executable).AddSeconds(1));
        var changed = await kit.Attempt(force: false);
        kit.Starts.Count.ShouldBe(3, "C959-pc-048");
        CodexCliVersionTestFixture.Text(changed, "codexCliLauncherFingerprint").ShouldNotBe(oldFingerprint, "C959-v04-mtime");
        using (var append = File.Open(kit.Executable, FileMode.Append)) append.WriteByte(0);
        await kit.Attempt(force: false);
        kit.Starts.Count.ShouldBe(4, "C959-pc-047");

        using (var many = new CodexCliVersionTestFixture { Mode = "timeout" })
        {
            using var caller = new CancellationTokenSource();
            var probe = (CodexCliVersionProbe)many.Probe!;
            var first = many.Attempt(force: false, ct: caller.Token);
            await many.WaitForReceiptAsync("timeout");
            var same = Enumerable.Range(0, 19).Select(_ => many.Attempt(force: false, ct: caller.Token)).ToArray();
            many.Starts.Count.ShouldBe(1, "C959-pc-043 simultaneous");
            var secondPath = Path.Combine(many.Root, "codex-second");
            File.Copy(many.Executable, secondPath);
            var second = many.Attempt(secondPath, ct: caller.Token);
            many.Clock.Advance(TimeSpan.FromSeconds(1));
            var busy = await second.WaitAsync(TimeSpan.FromSeconds(5));
            CodexCliVersionTestFixture.Text(busy, "codexCliVersionError").ShouldBe("probe_busy", "C959-pc-046");
            many.Starts.Count.ShouldBe(1, "C959-pc-044");
            caller.Cancel();
            foreach (var wait in new[] { first }.Concat(same))
                await Should.ThrowAsync<OperationCanceledException>(async () => await wait);
            probe.CacheCount.ShouldBeLessThanOrEqualTo(32, "C959-pc-045");
        }
        using (var many = new CodexCliVersionTestFixture())
        {
            for (var i = 0; i < 32; i++)
            {
                var exe = Path.Combine(many.Root, "codex-" + i);
                File.Copy(many.Executable, exe);
                var entry = await many.Attempt(exe, force: false);
                CodexCliVersionTestFixture.Text(entry, "codexCliVersion").ShouldBe("0.160.0", "C959-v04-cache-fill " + i);
            }
            var overflow = Path.Combine(many.Root, "codex-32");
            File.Copy(many.Executable, overflow);
            var refused = await many.Attempt(overflow, force: false);
            CodexCliVersionTestFixture.Text(refused, "codexCliVersionError").ShouldBe("probe_busy", "C959-pc-045");
            ((CodexCliVersionProbe)many.Probe!).CacheCount.ShouldBe(32, "C959-v04-cache-size");
            many.Starts.Count.ShouldBe(32, "C959-v04-cache-starts");
        }
        using (var startup = new CodexCliVersionTestFixture { Mode = "timeout" })
        {
            var gate = new PhoneHomeAdoptionGate();
            var ready = gate.WaitAsync(CancellationToken.None);
            var attempt = CodexCliVersionRoutes.PrepareAdvertisementAsync((CodexCliVersionProbe)startup.Probe!, gate, CancellationToken.None);
            await startup.WaitForReceiptAsync("timeout");
            ready.IsCompleted.ShouldBeFalse("C959-pc-038");
            startup.Clock.Advance(TimeSpan.FromSeconds(5));
            await attempt.WaitAsync(TimeSpan.FromSeconds(10));
            ready.IsCompleted.ShouldBeTrue("C959-v04-ready-after-budget");
            ((CodexCliVersionProbe)startup.Probe!).Snapshot.CodexCliVersionCheckedAtUtc
                .ShouldBe(CodexCliVersionTestFixture.T.AddSeconds(5), "C959-pc-040");
        }
        using (var periodic = new CodexCliVersionTestFixture())
        {
            await periodic.Refresh();
            using var service = new CodexCliVersionRefreshService((CodexCliVersionProbe)periodic.Probe!, periodic.Clock,
                Options.Create(new CodexCliVersionSettings()));
            await service.StartAsync(CancellationToken.None);
            await service.Ready.WaitAsync(TimeSpan.FromSeconds(5));
            periodic.Mode = "nonzero";
            periodic.Clock.Advance(TimeSpan.FromMinutes(5));
            var limit = Stopwatch.StartNew();
            while (periodic.Starts.Count < 2 && limit.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
            periodic.Starts.Count.ShouldBe(2, "C959-pc-042");
            limit.Restart();
            while (CodexCliVersionTestFixture.Text(periodic.Local(), "codexCliVersion") is not null
                   && limit.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
            CodexCliVersionTestFixture.Text(periodic.Local(), "codexCliVersionError").ShouldBe("nonzero_exit", "C959-v04-periodic-failure");
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task C959_Local_and_registration_share_snapshot()
    {
        using var kit = new CodexCliVersionTestFixture();
        await kit.Refresh();
        foreach (var (mode, version, error) in new[] { ("success", "0.160.0", (string?)null),
                     ("notice", "0.160.0", "stderr_output"), ("stderr-4097", "0.160.0", "output_truncated"),
                     ("nonzero", (string?)null, "nonzero_exit") })
        {
            kit.Mode = mode;
            await kit.Refresh();
            var expectedSample = ((CodexCliVersionProbe)kit.Probe!).Snapshot;
            var startsBeforeReads = kit.Starts.Count;
            for (var read = 0; read < 10; read++) _ = kit.Local();
            kit.Starts.Count.ShouldBe(startsBeforeReads, "C959-pc-060");
            var local = kit.Local();
            var remote = kit.Registration();
            local.GetProperty("codexCliVersionCheckedAtUtc").GetDateTimeOffset().ShouldBe(CodexCliVersionTestFixture.T, "C959-pc-051");
            remote.GetProperty("codexCliVersionCheckedAtUtc").GetDateTimeOffset().ShouldBe(CodexCliVersionTestFixture.T, "C959-pc-055");
            CodexCliVersionTestFixture.Text(local, "codexCliLauncherFingerprint").ShouldBe(expectedSample.CodexCliLauncherFingerprint, "C959-pc-053");
            CodexCliVersionTestFixture.Text(remote, "codexCliLauncherFingerprint").ShouldBe(expectedSample.CodexCliLauncherFingerprint, "C959-pc-057");
            local.TryGetProperty("codexCliVersion", out _).ShouldBeTrue("C959-v05-local");
            CodexCliVersionTestFixture.Text(local, "codexCliVersion").ShouldBe(version, "C959-pc-050");
            CodexCliVersionTestFixture.Text(remote, "codexCliVersion").ShouldBe(version, "C959-pc-054");
            CodexCliVersionTestFixture.Text(local, "codexCliVersionError").ShouldBe(error, "C959-pc-052");
            CodexCliVersionTestFixture.Text(remote, "codexCliVersionError").ShouldBe(error, "C959-pc-056");
            foreach (var field in new[] { "codexCliVersionCheckedAtUtc", "codexCliLauncherFingerprint" })
            {
                CodexCliVersionTestFixture.Text(local, field).ShouldNotBeNull("C959-v05-field " + field);
                CodexCliVersionTestFixture.Text(remote, field).ShouldBe(CodexCliVersionTestFixture.Text(local, field), "C959-v05-parity " + field);
            }
            local.GetProperty("features").EnumerateArray().Select(v => v.GetString()).ShouldContain("codex-cli-version-v1", "C959-pc-058");
            remote.GetProperty("features").EnumerateArray().Select(v => v.GetString()).ShouldContain("codex-cli-version-v1", "C959-pc-059");
        }
        JsonSerializer.Deserialize<RunnerCapabilitiesDto>("{\"ptyBackend\":\"InboxConhost\",\"ptyBackendRequested\":\"inbox\",\"ptyBackendReason\":\"test\",\"ptyBackendFellBack\":false,\"future\":17}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldNotBeNull("C959-v05-legacy");
    }

    private static async Task<bool> CompletedAsync(Task task, TimeSpan timeout)
    {
        try { await task.WaitAsync(timeout); return true; }
        catch (TimeoutException) { return false; }
    }

    private sealed class SyntheticProbeEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = [];
        public SyntheticProbeEnvironment(string root)
        {
            foreach (var key in new[] { "OPENAI_API_KEY", "ANTIPHON_TASK_TOKEN", "HTTP_PROXY", "HTTPS_PROXY",
                         "NODE_OPTIONS", "NODE_PATH", "BASH_ENV", "ENV", "DOTNET_STARTUP_HOOKS", "CODEX_HOME" })
            {
                _previous[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, key == "CODEX_HOME" ? Path.Combine(root, "parent-home") : "C959-env-canary");
            }
            Directory.CreateDirectory(Path.Combine(root, "parent-home"));
            File.WriteAllText(Path.Combine(root, "parent-home", "auth.json"), "C959-env-canary");
        }
        public void Dispose()
        {
            foreach (var pair in _previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
