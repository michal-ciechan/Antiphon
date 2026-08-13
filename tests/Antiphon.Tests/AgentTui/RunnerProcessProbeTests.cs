using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.AgentTui;

[NotInParallel("RunnerProcessProbe")]
public sealed class RunnerProcessProbeTests
{
    [Test]
    public async Task Probe_excludes_undeclared_host_environment_and_keeps_declared_values()
    {
        const string hostCanaryName = "ANTIPHON_PROBE_HOST_CANARY";
        var previous = Environment.GetEnvironmentVariable(hostCanaryName);
        var scratch = CreateScratch();
        try
        {
            Environment.SetEnvironmentVariable(hostCanaryName, "synthetic-host-only-value");
            var script = WriteHelper(scratch);
            var result = await CreateProbe().RunAsync(
                Request(script, ["environment", hostCanaryName, "PROBE_ORDINARY"]),
                CancellationToken.None);

            result.ExitCode.ShouldBe(0);
            result.StandardOutput.ReplaceLineEndings("\n").TrimEnd('\n')
                .ShouldBe("host=False\ndeclared=True");
        }
        finally
        {
            Environment.SetEnvironmentVariable(hostCanaryName, previous);
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Probe_preserves_argument_boundaries_without_shell_interpretation()
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var canaryPath = Path.Combine(scratch, "shell-canary.txt");
            var arguments = new[]
            {
                "plain value",
                $"; [IO.File]::WriteAllText('{canaryPath}', 'pwned')",
                "$(Write-Output should-not-run)",
                "& whoami"
            };
            var probe = CreateProbe(maxOutputBytes: 4096);

            var result = await probe.RunAsync(
                Request(script, ["arguments", canaryPath, .. arguments]),
                CancellationToken.None);

            result.ExitCode.ShouldBe(0);
            result.TimedOut.ShouldBeFalse();
            File.Exists(canaryPath).ShouldBeFalse();
            result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => Encoding.UTF8.GetString(Convert.FromBase64String(line)))
                .ShouldBe(arguments);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Probe_caps_combined_stdout_and_stderr_and_signals_truncation()
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var probe = CreateProbe(maxOutputBytes: 1024);

            var result = await probe.RunAsync(
                Request(script, ["oversized"]),
                CancellationToken.None);

            result.OutputTruncated.ShouldBeTrue();
            (Encoding.UTF8.GetByteCount(result.StandardOutput)
             + Encoding.UTF8.GetByteCount(result.StandardError)).ShouldBeLessThanOrEqualTo(1024);
            result.ExitCode.ShouldBe(0);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Truncated_output_is_discarded_instead_of_returning_secret_fragments()
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var syntheticValue = new string('S', 256);
            var request = Request(script, ["raw-value", syntheticValue]) with
            {
                SecretValues = [syntheticValue]
            };

            var result = await CreateProbe(maxOutputBytes: 32).RunAsync(
                request,
                CancellationToken.None);

            result.OutputTruncated.ShouldBeTrue();
            result.StandardOutput.ShouldBeEmpty();
            result.StandardError.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Invalid_utf8_output_is_discarded_as_unusable()
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var result = await CreateProbe().RunAsync(
                Request(script, ["invalid-utf8"]),
                CancellationToken.None);

            result.OutputTruncated.ShouldBeTrue();
            result.StandardOutput.ShouldBeEmpty();
            result.StandardError.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Probe_timeout_kills_the_entire_process_tree()
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "timeout-pids.txt");
            var reaper = new RunnerProcessReaper();
            var probe = CreateProbe(reaper, timeoutSeconds: 1);

            var result = await probe.RunAsync(
                Request(script, ["tree", pidPath]),
                CancellationToken.None);

            result.TimedOut.ShouldBeTrue();
            result.Cancelled.ShouldBeFalse();
            result.CleanupConfirmed.ShouldBeTrue();
            var pids = await ReadPidsAsync(pidPath);
            foreach (var pid in pids)
                await AssertProcessExitedAsync(pid);
            await reaper.WaitForEmptyAsync(TimeSpan.FromSeconds(5));
            reaper.TrackedProcessCount.ShouldBe(0);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Probe_cancellation_kills_the_tree_and_redacts_secret_canaries()
    {
        const string secretCanary = "synthetic-probe-secret-canary";
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "cancel-pids.txt");
            var reaper = new RunnerProcessReaper();
            var probe = CreateProbe(reaper, timeoutSeconds: 10);
            using var cancellation = new CancellationTokenSource();
            var request = Request(script, ["tree-with-secret", pidPath, secretCanary]) with
            {
                SecretValues = [secretCanary]
            };

            var operation = probe.RunAsync(request, cancellation.Token);
            await WaitForFileAsync(pidPath);
            cancellation.Cancel();
            var result = await operation;

            result.Cancelled.ShouldBeTrue();
            result.TimedOut.ShouldBeFalse();
            result.CleanupConfirmed.ShouldBeTrue();
            result.StandardOutput.ShouldNotContain(secretCanary);
            result.StandardError.ShouldNotContain(secretCanary);
            result.SensitiveOutputDetected.ShouldBeTrue();
            var pids = await ReadPidsAsync(pidPath);
            foreach (var pid in pids)
                await AssertProcessExitedAsync(pid);
            await reaper.WaitForEmptyAsync(TimeSpan.FromSeconds(5));
            reaper.TrackedProcessCount.ShouldBe(0);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Unconfirmed_primary_cleanup_is_owned_and_completed_by_the_reaper()
    {
        var scratch = CreateScratch();
        var reaperEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReaper = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "reaper-pids.txt");
            var reaper = new RunnerProcessReaper(async (process, cancellationToken) =>
            {
                reaperEntered.TrySetResult();
                await releaseReaper.Task.WaitAsync(cancellationToken);
                return await RunnerProcessCleanup.StopTreeAsync(process, cancellationToken);
            });
            var probe = CreateProbe(
                reaper,
                timeoutSeconds: 1,
                seams: new RunnerProcessProbeSeams
                {
                    StopTreeAsync = (_, _) => Task.FromResult(false)
                });

            var result = await probe.RunAsync(
                Request(script, ["tree", pidPath]),
                CancellationToken.None);

            result.TimedOut.ShouldBeTrue();
            result.CleanupConfirmed.ShouldBeFalse();
            result.Error.ShouldBe(
                "The probe process cleanup could not be confirmed; background cleanup is continuing.");
            await reaperEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            reaper.TrackedProcessCount.ShouldBe(1);

            releaseReaper.TrySetResult();
            await reaper.WaitForEmptyAsync(TimeSpan.FromSeconds(5));
            reaper.TrackedProcessCount.ShouldBe(0);
            var pids = await ReadPidsAsync(pidPath);
            foreach (var pid in pids)
                await AssertProcessExitedAsync(pid);
        }
        finally
        {
            releaseReaper.TrySetResult();
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Reaper_host_stop_kills_every_already_started_tracked_process()
    {
        var scratch = CreateScratch();
        var reaperEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReaper = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "host-stop-pids.txt");
            var reaper = new RunnerProcessReaper(async (_, cancellationToken) =>
            {
                reaperEntered.TrySetResult();
                await releaseReaper.Task.WaitAsync(cancellationToken);
                return false;
            });
            var probe = CreateProbe(
                reaper,
                timeoutSeconds: 1,
                seams: new RunnerProcessProbeSeams
                {
                    StopTreeAsync = (_, _) => Task.FromResult(false)
                });

            var result = await probe.RunAsync(
                Request(script, ["tree", pidPath]),
                CancellationToken.None);
            result.CleanupConfirmed.ShouldBeFalse();
            await reaperEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var stopping = reaper.StopAsync(shutdown.Token);
            var pids = await ReadPidsAsync(pidPath);
            foreach (var pid in pids)
                await AssertProcessExitedAsync(pid);
            await Task.Delay(100);
            stopping.IsCompleted.ShouldBeFalse();

            releaseReaper.TrySetResult();
            await stopping;
            await reaper.WaitForEmptyAsync(TimeSpan.FromSeconds(5));
            reaper.TrackedProcessCount.ShouldBe(0);
        }
        finally
        {
            releaseReaper.TrySetResult();
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Reaper_shutdown_waits_for_a_starting_guard_then_kills_and_rejects_future_starts()
    {
        var scratch = CreateScratch();
        var startEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startCalls = 0;
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "shutdown-late-start-pids.txt");
            var reaper = new RunnerProcessReaper();
            var probe = CreateProbe(
                reaper,
                timeoutSeconds: 1,
                seams: new RunnerProcessProbeSeams
                {
                    StartProcessAsync = async (guard, _) => await Task.Run(() =>
                    {
                        var started = guard.TryStart();
                        if (started)
                            startedPid.TrySetResult(guard.Process.Id);
                        return started;
                    }),
                    StartCommitted = () =>
                    {
                        Interlocked.Increment(ref startCalls);
                        startEntered.TrySetResult();
                        releaseStart.Task.GetAwaiter().GetResult();
                    }
                });

            var operation = probe.RunAsync(
                Request(script, ["tree", pidPath]),
                CancellationToken.None);
            await startEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var stopping = reaper.StopAsync(shutdown.Token);
            stopping.IsCompleted.ShouldBeFalse();

            releaseStart.TrySetResult();
            var startedProcessId = await startedPid.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await AssertProcessExitedAsync(startedProcessId);
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            await stopping;
            reaper.TrackedProcessCount.ShouldBe(0);
            if (File.Exists(pidPath))
            {
                foreach (var pid in await ReadPidsAsync(pidPath))
                    await AssertProcessExitedAsync(pid);
            }

            var rejected = await probe.RunAsync(
                Request(script, ["credential", "unused"]),
                CancellationToken.None);
            rejected.Started.ShouldBeFalse();
            rejected.Error.ShouldBe(
                "The runner process probe is unavailable because the host is shutting down.");
            startCalls.ShouldBe(1);
        }
        finally
        {
            releaseStart.TrySetResult();
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Unexpected_exception_after_registration_transfers_process_to_reaper_cleanup()
    {
        var scratch = CreateScratch();
        var pidPath = Path.Combine(scratch, "unexpected-exception-pids.txt");
        try
        {
            var script = WriteHelper(scratch);
            var reaper = new RunnerProcessReaper();
            var probe = CreateProbe(
                reaper,
                seams: new RunnerProcessProbeSeams
                {
                    StartProcessAsync = async (guard, _) =>
                    {
                        guard.TryStart().ShouldBeTrue();
                        await WaitForFileAsync(pidPath);
                        throw new ApplicationException("synthetic unexpected post-registration failure");
                    }
                });

            await Should.ThrowAsync<ApplicationException>(() => probe.RunAsync(
                Request(script, ["tree", pidPath]),
                CancellationToken.None));

            await reaper.WaitForEmptyAsync(TimeSpan.FromSeconds(2));
            reaper.TrackedProcessCount.ShouldBe(0);
            foreach (var processId in await ReadPidsAsync(pidPath))
                await AssertProcessExitedAsync(processId);
        }
        finally
        {
            if (File.Exists(pidPath))
            {
                foreach (var processId in await ReadPidsAsync(pidPath))
                    StopProcessIfRunning(processId);
            }
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Blocking_path_inspection_observes_the_one_second_deadline()
    {
        var probe = CreateProbe(
            new RunnerProcessReaper(),
            timeoutSeconds: 1,
            seams: new RunnerProcessProbeSeams
            {
                CheckExecutableAsync = async (_, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return new RunnerPathCheck(true, "Unexpected completion.");
                }
            });

        var elapsed = Stopwatch.StartNew();
        var result = await probe.CheckExecutableAsync("synthetic-runner", CancellationToken.None);
        elapsed.Stop();

        result.IsAvailable.ShouldBeFalse();
        result.Message.ShouldContain("deadline", Case.Insensitive);
        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Late_process_start_after_the_one_second_deadline_is_reaped()
    {
        var scratch = CreateScratch();
        var startEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "late-start-pids.txt");
            var reaper = new RunnerProcessReaper();
            var probe = CreateProbe(
                reaper,
                timeoutSeconds: 1,
                seams: new RunnerProcessProbeSeams
                {
                    StartProcessAsync = async (process, _) =>
                    {
                        startEntered.TrySetResult();
                        await releaseStart.Task;
                        var started = process.TryStart();
                        await WaitForFileAsync(pidPath);
                        return started;
                    }
                });

            var operation = probe.RunAsync(
                Request(script, ["tree", pidPath]),
                CancellationToken.None);
            await startEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var elapsed = Stopwatch.StartNew();
            var result = await operation;
            elapsed.Stop();

            result.TimedOut.ShouldBeTrue();
            result.Started.ShouldBeFalse();
            result.CleanupConfirmed.ShouldBeFalse();
            elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
            reaper.TrackedProcessCount.ShouldBe(1);

            releaseStart.TrySetResult();
            await reaper.WaitForEmptyAsync(TimeSpan.FromSeconds(5));
            var pids = await ReadPidsAsync(pidPath);
            foreach (var pid in pids)
                await AssertProcessExitedAsync(pid);
            reaper.TrackedProcessCount.ShouldBe(0);
        }
        finally
        {
            releaseStart.TrySetResult();
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Bounded_startup_that_exits_on_stdin_close_is_clean()
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "clean-stop-pid.txt");
            var probe = CreateProbe(timeoutSeconds: 5);
            var request = Request(script, ["stdin-close", pidPath]) with
            {
                StopAfter = TimeSpan.FromMilliseconds(250)
            };

            var result = await probe.RunAsync(request, CancellationToken.None);

            result.TimedOut.ShouldBeFalse();
            result.Cancelled.ShouldBeFalse();
            result.CleanlyStopped.ShouldBeTrue();
            result.CleanupConfirmed.ShouldBeTrue();
            result.Error.ShouldBeNull();
            result.ExitCode.ShouldBe(0);
            await AssertProcessExitedAsync((await ReadPidsAsync(pidPath)).Single());
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Probe_redacts_credential_shaped_output_without_persisting_raw_values()
    {
        const string canary = "credential-shaped-canary";
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var probe = CreateProbe(maxOutputBytes: 4096);

            var result = await probe.RunAsync(
                Request(script, ["credential", canary]),
                CancellationToken.None);

            result.SensitiveOutputDetected.ShouldBeTrue();
            result.StandardOutput.ShouldNotContain(canary);
            result.StandardError.ShouldNotContain(canary);
            result.StandardOutput.ShouldNotContain("API_TOKEN", Case.Insensitive);
            result.StandardError.ShouldNotContain("password", Case.Insensitive);
            result.StandardOutput.ShouldNotContain("OPENAI_API_KEY", Case.Insensitive);
            result.StandardError.ShouldNotContain("SERVICE_TOKEN", Case.Insensitive);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Probe_redacts_common_credential_names_in_assignments_and_json()
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var result = await CreateProbe().RunAsync(
                Request(script, ["common-credentials"]),
                CancellationToken.None);

            result.SensitiveOutputDetected.ShouldBeTrue();
            result.StandardOutput.ShouldNotContain("AWS_SECRET_ACCESS_KEY", Case.Insensitive);
            result.StandardOutput.ShouldNotContain("DATABASE_URL", Case.Insensitive);
            result.StandardError.ShouldNotContain("PRIVATE_KEY", Case.Insensitive);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    [Arguments("ApiKey")]
    [Arguments("ApiToken")]
    [Arguments("AccessToken")]
    [Arguments("RefreshToken")]
    [Arguments("SecretAccessKey")]
    [Arguments("PrivateKey")]
    [Arguments("DatabaseUrl")]
    [Arguments("ConnectionString")]
    [Arguments("AuthToken")]
    [Arguments("Authorization")]
    [Arguments("Password")]
    [Arguments("Secret")]
    public async Task Probe_redacts_camel_case_credential_suffixes_in_plain_and_json_assignments(
        string suffix)
    {
        const string plainValue = "synthetic-camel-plain-value";
        const string jsonValue = "synthetic-camel-json-value";
        var credentialName = $"client{suffix}";
        var output = $"{credentialName}={plainValue}\n{{\"{credentialName}\":\"{jsonValue}\"}}";
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var result = await CreateProbe().RunAsync(
                Request(script, ["raw-value", output]),
                CancellationToken.None);

            result.SensitiveOutputDetected.ShouldBeTrue();
            result.StandardOutput.ShouldNotContain(credentialName);
            result.StandardOutput.ShouldNotContain(plainValue);
            result.StandardOutput.ShouldNotContain(jsonValue);
            result.StandardError.ShouldNotContain(credentialName);
            result.StandardError.ShouldNotContain(plainValue);
            result.StandardError.ShouldNotContain(jsonValue);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    [Arguments("CLIENT_API_KEY")]
    [Arguments("client-api-token")]
    public async Task Probe_preserves_snake_and_dash_credential_redaction(string credentialName)
    {
        const string plainValue = "synthetic-delimited-plain-value";
        const string jsonValue = "synthetic-delimited-json-value";
        var output = $"{credentialName}={plainValue}\n{{\"{credentialName}\":\"{jsonValue}\"}}";
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var result = await CreateProbe().RunAsync(
                Request(script, ["raw-value", output]),
                CancellationToken.None);

            result.SensitiveOutputDetected.ShouldBeTrue();
            result.StandardOutput.ShouldNotContain(credentialName);
            result.StandardOutput.ShouldNotContain(plainValue);
            result.StandardOutput.ShouldNotContain(jsonValue);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    [Arguments("clientSecretHint")]
    [Arguments("authTokenType")]
    [Arguments("servicePasswordPolicy")]
    [Arguments("apiKeyLabel")]
    [Arguments("notasecret")]
    public async Task Probe_leaves_non_credential_near_misses_unchanged(string name)
    {
        var output = $"{name}=synthetic-near-miss-plain\n{{\"{name}\":\"synthetic-near-miss-json\"}}";
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var result = await CreateProbe().RunAsync(
                Request(script, ["raw-value", output]),
                CancellationToken.None);

            result.SensitiveOutputDetected.ShouldBeFalse();
            result.StandardOutput.ShouldBe(output);
            result.StandardError.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    [Arguments("clientSecret=unknown-secret", "*", false, "=", "", "")]
    [Arguments("prefix abcdef suffix", "prefix * suffix", false, "abc", "abcdef", "")]
    [Arguments("prefix abcdef suffix", "prefix * suffix", true, "abcdef", "abc", "")]
    [Arguments("abcabcdef", "*", false, "bcde", "abc", "abcdef")]
    [Arguments("aaaaa", "*", true, "aaa", "", "")]
    public async Task Probe_redacts_the_union_of_exact_secret_occurrences_without_order_or_overlap_leaks(
        string output,
        string expected,
        bool writeToStandardError,
        string firstSecret,
        string secondSecret,
        string thirdSecret)
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var request = Request(
                script,
                [writeToStandardError ? "raw-error" : "raw-value", output]) with
            {
                SecretValues = new[] { firstSecret, secondSecret, thirdSecret }
                    .Where(secret => secret.Length > 0)
                    .ToArray()
            };

            var result = await CreateProbe().RunAsync(request, CancellationToken.None);

            result.SensitiveOutputDetected.ShouldBeTrue();
            var actual = writeToStandardError ? result.StandardError : result.StandardOutput;
            actual.ShouldBe(expected);
            foreach (var secret in request.SecretValues)
                actual.ShouldNotContain(secret);
            (writeToStandardError ? result.StandardOutput : result.StandardError).ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    [Arguments(
        "clientApiKey=synthetic-credential-first status=ready note='ordinary value !@#$%^&*()'",
        "* status=ready note='ordinary value !@#$%^&*()'",
        false)]
    [Arguments(
        "status=ready,clientAuthToken=synthetic-credential-middle,count=2",
        "status=ready,*,count=2",
        false)]
    [Arguments(
        "status=ready message='ordinary value: punctuation !?' clientPassword=synthetic-credential-last",
        "status=ready message='ordinary value: punctuation !?' *",
        true)]
    [Arguments(
        """{"clientSecret":"synthetic-credential-json-first","status":"ok"}""",
        """{*,"status":"ok"}""",
        false)]
    [Arguments(
        """{"status":"ok","clientSecret":"synthetic-credential-quote-\"-slash-\\-end","note":"ordinary value, with punctuation!"}""",
        """{"status":"ok",*,"note":"ordinary value, with punctuation!"}""",
        false)]
    [Arguments(
        """{"status":"ok","clientAuthorization":"synthetic-credential-json-last"}""",
        """{"status":"ok",*}""",
        true)]
    [Arguments(
        "status=ok clientApiKey='synthetic-credential-one with spaces' clientPassword=\"synthetic-credential-two,with punctuation!\" tail='ordinary value'",
        "status=ok * * tail='ordinary value'",
        false)]
    [Arguments(
        "message=ordinary value with punctuation! clientRefreshToken=synthetic-credential-refresh status=ok",
        "message=ordinary value with punctuation! * status=ok",
        false)]
    [Arguments(
        "status=ok clientSecretHint='ordinary secret hint' authTokenType=ordinary servicePasswordPolicy=standard apiKeyLabel=public clientPrivateKey=synthetic-credential-private",
        "status=ok clientSecretHint='ordinary secret hint' authTokenType=ordinary servicePasswordPolicy=standard apiKeyLabel=public *",
        false)]
    [Arguments(
        """{"clientApiKey":"synthetic-credential-json-one","status":"ok","clientPassword":"synthetic-credential-json-two"}""",
        """{*,"status":"ok",*}""",
        false)]
    [Arguments(
        "Password=synthetic-credential-semicolon-first;Server=db;Encrypt=true",
        "*;Server=db;Encrypt=true",
        false)]
    [Arguments(
        "Server=db;servicePasswordPolicy=standard;Password=synthetic-credential-semicolon-middle;Encrypt=true",
        "Server=db;servicePasswordPolicy=standard;*;Encrypt=true",
        false)]
    [Arguments(
        "Server=db;User=app;clientSecret=synthetic-credential-semicolon-last",
        "Server=db;User=app;*",
        true)]
    [Arguments(
        "Server=db;Password=synthetic-credential-semicolon-one;clientApiKey=synthetic-credential-semicolon-two;Mode=read",
        "Server=db;*;*;Mode=read",
        false)]
    [Arguments(
        "status=ok&clientAuthToken=synthetic-credential-query&mode=read",
        "status=ok&*&mode=read",
        false)]
    [Arguments(
        "status=ok|clientPrivateKey=synthetic-credential-log|mode=read",
        "status=ok|*|mode=read",
        true)]
    [Arguments(
        "status=ok;clientPassword=\"synthetic-credential-quoted;&|value\";mode=read",
        "status=ok;*;mode=read",
        false)]
    [Arguments(
        "note='clientPassword=ordinary;clientApiKey=ordinary'|clientSecret=synthetic-credential-embedded|status=ok",
        "note='clientPassword=ordinary;clientApiKey=ordinary'|*|status=ok",
        false)]
    [Arguments(
        "Password=synthetic-credential-correct synthetic-credential-horse synthetic-credential-battery!",
        "*",
        false)]
    [Arguments(
        "Password=\"synthetic-credential-unterminated synthetic-credential-tail",
        "*",
        false)]
    [Arguments(
        "clientSecret='synthetic-credential-unterminated synthetic-credential-tail",
        "*",
        true)]
    [Arguments(
        "Password=\"synthetic-credential-top-secret status=synthetic-credential-still-secret tail",
        "*",
        false)]
    [Arguments(
        "clientSecret='synthetic-credential-top-secret mode=synthetic-credential-still-secret tail",
        "*",
        true)]
    [Arguments(
        "Password=\"synthetic-credential-top-secret clientSecret=synthetic-credential-still-secret tail",
        "*",
        false)]
    [Arguments(
        "clientSecret=synthetic-credential-alpha\tsynthetic-credential-beta\t synthetic-credential-gamma?",
        "*",
        true)]
    [Arguments(
        "Password=synthetic-credential-first synthetic-credential-value status=ok mode=read",
        "* status=ok mode=read",
        false)]
    [Arguments(
        "status=ok Password=synthetic-credential-middle synthetic-credential-value mode=read",
        "status=ok * mode=read",
        true)]
    [Arguments(
        "status=ok mode=read clientSecret=synthetic-credential-last synthetic-credential-value!",
        "status=ok mode=read *",
        false)]
    [Arguments(
        "status=ok Password=synthetic-credential-one synthetic-credential-two clientApiKey=synthetic-credential-three synthetic-credential-four mode=read",
        "status=ok * * mode=read",
        false)]
    [Arguments(
        "Password=synthetic-credential-before synthetic-credential-semicolon;Server=db",
        "*;Server=db",
        false)]
    [Arguments(
        "Password=synthetic-credential-line-one synthetic-credential-suffix\nstatus=ok clientSecret=synthetic-credential-line-two synthetic-credential-suffix\nnote=done",
        "*\nstatus=ok *\nnote=done",
        true)]
    public async Task Probe_redacts_each_credential_assignment_without_consuming_neighbors(
        string output,
        string expected,
        bool writeToStandardError)
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var result = await CreateProbe().RunAsync(
                Request(script, [writeToStandardError ? "raw-error" : "raw-value", output]),
                CancellationToken.None);

            result.SensitiveOutputDetected.ShouldBeTrue();
            var actual = writeToStandardError ? result.StandardError : result.StandardOutput;
            actual.ShouldBe(expected);
            actual.ShouldNotContain("synthetic-credential");
            (writeToStandardError ? result.StandardOutput : result.StandardError).ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Probe_redacts_many_same_line_assignments_near_the_output_limit_within_budget()
    {
        const int repetitionCount = 4_000;
        const int benchmarkIterations = 100;
        const int benchmarkRounds = 5;
        const string assignment = "token=synthetic ";
        const string lineDelimitedAssignment = "token=synthetic\n";
        var rawOutputBytes = Encoding.UTF8.GetByteCount(assignment) * repetitionCount;
        rawOutputBytes.ShouldBeGreaterThan(60 * 1024);
        rawOutputBytes.ShouldBeLessThan(AgentTuiSettings.MaximumProbeOutputBytes);
        (Encoding.UTF8.GetByteCount(lineDelimitedAssignment) * repetitionCount).ShouldBe(rawOutputBytes);

        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var elapsed = Stopwatch.StartNew();
            var result = await CreateProbe(timeoutSeconds: 10).RunAsync(
                Request(script, ["repeated-raw-value", assignment, repetitionCount.ToString()]),
                CancellationToken.None);
            elapsed.Stop();

            result.OutputTruncated.ShouldBeFalse();
            result.SensitiveOutputDetected.ShouldBeTrue();
            result.StandardOutput.ShouldNotBeEmpty();
            result.StandardOutput.All(character => character is '*' or ' ').ShouldBeTrue();
            result.StandardOutput.ShouldNotContain("token", Case.Insensitive);
            result.StandardOutput.ShouldNotContain("synthetic", Case.Insensitive);
            result.StandardError.ShouldBeEmpty();
            elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));

            var redactorMethod = typeof(RunnerProcessProbe).GetMethod(
                "RedactCredentialAssignments",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            redactorMethod.ShouldNotBeNull();
            var redact = redactorMethod.CreateDelegate<Func<string, string>>();
            var sameLineOutput = string.Concat(Enumerable.Repeat(assignment, repetitionCount));
            var lineDelimitedOutput = string.Concat(
                Enumerable.Repeat(lineDelimitedAssignment, repetitionCount));
            redact(sameLineOutput).ShouldNotContain("synthetic", Case.Insensitive);
            redact(lineDelimitedOutput).ShouldNotContain("synthetic", Case.Insensitive);

            for (var warmup = 0; warmup < 20; warmup++)
            {
                redact(lineDelimitedOutput);
                redact(sameLineOutput);
            }

            var bestLineDelimited = TimeSpan.MaxValue;
            var bestSameLine = TimeSpan.MaxValue;
            for (var round = 0; round < benchmarkRounds; round++)
            {
                var lineFirst = round % 2 == 0;
                var first = MeasureRedaction(
                    redact,
                    lineFirst ? lineDelimitedOutput : sameLineOutput,
                    benchmarkIterations);
                var second = MeasureRedaction(
                    redact,
                    lineFirst ? sameLineOutput : lineDelimitedOutput,
                    benchmarkIterations);
                bestLineDelimited = TimeSpan.FromTicks(Math.Min(
                    bestLineDelimited.Ticks,
                    (lineFirst ? first : second).Ticks));
                bestSameLine = TimeSpan.FromTicks(Math.Min(
                    bestSameLine.Ticks,
                    (lineFirst ? second : first).Ticks));
            }

            bestLineDelimited.ShouldBeLessThan(TimeSpan.FromSeconds(10));
            bestSameLine.ShouldBeLessThan(TimeSpan.FromSeconds(10));
            var linearBudget = TimeSpan.FromTicks((long)(bestLineDelimited.Ticks * 1.35));
            bestSameLine.ShouldBeLessThan(
                linearBudget,
                $"same-line {bestSameLine.TotalMilliseconds:F0} ms; "
                + $"line-delimited {bestLineDelimited.TotalMilliseconds:F0} ms");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }

        static TimeSpan MeasureRedaction(
            Func<string, string> redact,
            string output,
            int iterations)
        {
            var measurement = Stopwatch.StartNew();
            for (var iteration = 0; iteration < iterations; iteration++)
                redact(output);
            measurement.Stop();
            return measurement.Elapsed;
        }
    }

    [Test]
    public async Task Executable_path_rejects_an_existing_non_executable_file()
    {
        var scratch = CreateScratch();
        var path = Path.Combine(scratch, OperatingSystem.IsWindows() ? "not-executable.txt" : "not-executable");
        try
        {
            await File.WriteAllTextAsync(path, "ordinary");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var result = await CreateProbe().CheckExecutableAsync(path, CancellationToken.None);

            result.IsAvailable.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Executable_path_rejects_execute_access_denied_to_current_identity()
    {
        var scratch = CreateScratch();
        var path = Path.Combine(scratch, OperatingSystem.IsWindows() ? "denied.exe" : "denied");
        FileSystemAccessRule? denyRule = null;
        try
        {
            await File.WriteAllTextAsync(path, "ordinary");
            if (OperatingSystem.IsWindows())
            {
                denyRule = DenyCurrentWindowsFileAccess(path, FileSystemRights.ExecuteFile);
                if (CanOpenWindowsPath(path, WindowsFileExecute, directory: false))
                    throw new SkipTestException("The current Windows identity retained execute access after an explicit deny ACL.");
            }
            else
            {
                SkipIfPrivilegedUnixIdentity();
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite
                    | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            var result = await CreateProbe().CheckExecutableAsync(path, CancellationToken.None);

            result.IsAvailable.ShouldBeFalse();
        }
        finally
        {
            if (OperatingSystem.IsWindows() && denyRule is not null)
                RestoreWindowsFileAccess(path, denyRule);
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Required_file_rejects_a_file_without_read_access()
    {
        var scratch = CreateScratch();
        var path = Path.Combine(scratch, "locked-wrapper.ps1");
        try
        {
            await File.WriteAllTextAsync(path, "ordinary");
            await using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var result = await CreateProbe().CheckFileAsync(path, CancellationToken.None);

            result.IsAvailable.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Required_file_rejects_read_access_denied_to_current_identity()
    {
        var scratch = CreateScratch();
        var path = Path.Combine(scratch, "denied-wrapper.ps1");
        FileSystemAccessRule? denyRule = null;
        try
        {
            await File.WriteAllTextAsync(path, "ordinary");
            if (OperatingSystem.IsWindows())
            {
                denyRule = DenyCurrentWindowsFileAccess(path, FileSystemRights.ReadData);
                if (CanOpenWindowsPath(path, WindowsFileReadData, directory: false))
                    throw new SkipTestException("The current Windows identity retained read access after an explicit deny ACL.");
            }
            else
            {
                SkipIfPrivilegedUnixIdentity();
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }

            var result = await CreateProbe().CheckFileAsync(path, CancellationToken.None);

            result.IsAvailable.ShouldBeFalse();
        }
        finally
        {
            if (OperatingSystem.IsWindows() && denyRule is not null)
                RestoreWindowsFileAccess(path, denyRule);
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Working_directory_requires_search_access_on_unix_and_exists_on_windows()
    {
        var scratch = CreateScratch();
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(scratch, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var result = await CreateProbe().CheckDirectoryAsync(scratch, CancellationToken.None);

            result.IsAvailable.ShouldBe(OperatingSystem.IsWindows());
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    scratch,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Working_directory_rejects_traverse_access_denied_to_current_identity()
    {
        var scratch = CreateScratch();
        FileSystemAccessRule? denyRule = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                denyRule = DenyCurrentWindowsDirectoryAccess(
                    scratch,
                    FileSystemRights.ListDirectory | FileSystemRights.Traverse);
                if (CanOpenWindowsPath(
                        scratch,
                        WindowsFileListDirectory | WindowsFileTraverse,
                        directory: true))
                {
                    throw new SkipTestException(
                        "The current Windows identity retained directory traversal access after an explicit deny ACL.");
                }
            }
            else
            {
                SkipIfPrivilegedUnixIdentity();
                File.SetUnixFileMode(
                    scratch,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite
                    | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            var result = await CreateProbe().CheckDirectoryAsync(scratch, CancellationToken.None);

            result.IsAvailable.ShouldBeFalse();
        }
        finally
        {
            if (OperatingSystem.IsWindows() && denyRule is not null)
                RestoreWindowsDirectoryAccess(scratch, denyRule);
            else if (!OperatingSystem.IsWindows() && Directory.Exists(scratch))
            {
                File.SetUnixFileMode(
                    scratch,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Helper_resolves_the_current_powershell_executable_cross_platform()
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            (await File.ReadAllTextAsync(script)).ShouldNotContain("pwsh.exe", Case.Insensitive);

            var result = await CreateProbe().RunAsync(
                Request(script, ["child-executable"]),
                CancellationToken.None);

            result.ExitCode.ShouldBe(0);
            var executable = result.StandardOutput.Trim();
            File.Exists(executable).ShouldBeTrue();
            Path.GetFileName(executable).ShouldBe(
                OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
                StringCompareShould.IgnoreCase);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public async Task Bounded_startup_reports_forced_tree_termination_as_not_clean()
    {
        var scratch = CreateScratch();
        try
        {
            var script = WriteHelper(scratch);
            var pidPath = Path.Combine(scratch, "forced-stop-pids.txt");
            var probe = CreateProbe(timeoutSeconds: 5);
            var request = Request(script, ["tree", pidPath]) with
            {
                StopAfter = TimeSpan.FromSeconds(1)
            };

            var result = await probe.RunAsync(request, CancellationToken.None);

            result.TimedOut.ShouldBeFalse();
            result.Cancelled.ShouldBeFalse();
            result.CleanlyStopped.ShouldBeFalse();
            result.CleanupConfirmed.ShouldBeTrue();
            result.Error.ShouldBe("The probe process required forced cleanup.");
            var pids = await ReadPidsAsync(pidPath);
            foreach (var pid in pids)
                await AssertProcessExitedAsync(pid);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static RunnerProcessProbe CreateProbe(
        int timeoutSeconds = 5,
        int maxOutputBytes = 64 * 1024) =>
        new(Options.Create(new AgentTuiSettings
        {
            ProbeTimeoutSeconds = timeoutSeconds,
            MaxProbeOutputBytes = maxOutputBytes
        }));

    private static RunnerProcessProbe CreateProbe(
        RunnerProcessReaper reaper,
        int timeoutSeconds = 5,
        int maxOutputBytes = 64 * 1024,
        RunnerProcessProbeSeams? seams = null) =>
        new(
            Options.Create(new AgentTuiSettings
            {
                ProbeTimeoutSeconds = timeoutSeconds,
                MaxProbeOutputBytes = maxOutputBytes
            }),
            reaper,
            seams);

    private const uint WindowsFileReadData = 0x0001;
    private const uint WindowsFileListDirectory = 0x0001;
    private const uint WindowsFileExecute = 0x0020;
    private const uint WindowsFileTraverse = 0x0020;
    private const uint WindowsFileFlagBackupSemantics = 0x02000000;

    [SupportedOSPlatform("windows")]
    private static FileSystemAccessRule DenyCurrentWindowsFileAccess(
        string path,
        FileSystemRights rights)
    {
        var info = new FileInfo(path);
        FileSystemAccessRule? rule = null;
        try
        {
            var restricted = info.GetAccessControl(AccessControlSections.Access);
            rule = new FileSystemAccessRule(
                CurrentWindowsUser(),
                rights,
                AccessControlType.Deny);
            restricted.AddAccessRule(rule);
            info.SetAccessControl(restricted);
            return rule;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or PlatformNotSupportedException)
        {
            if (rule is not null)
                RestoreWindowsFileAccess(path, rule);
            throw new SkipTestException($"A Windows file deny ACL could not be constructed: {exception.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static FileSystemAccessRule DenyCurrentWindowsDirectoryAccess(
        string path,
        FileSystemRights rights)
    {
        var info = new DirectoryInfo(path);
        FileSystemAccessRule? rule = null;
        try
        {
            var restricted = info.GetAccessControl(AccessControlSections.Access);
            rule = new FileSystemAccessRule(
                CurrentWindowsUser(),
                rights,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Deny);
            restricted.AddAccessRule(rule);
            info.SetAccessControl(restricted);
            return rule;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or PlatformNotSupportedException)
        {
            if (rule is not null)
                RestoreWindowsDirectoryAccess(path, rule);
            throw new SkipTestException($"A Windows directory deny ACL could not be constructed: {exception.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier CurrentWindowsUser()
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        return identity.User
               ?? throw new SkipTestException("The current Windows user SID is unavailable.");
    }

    [SupportedOSPlatform("windows")]
    private static void RestoreWindowsFileAccess(string path, FileSystemAccessRule denyRule)
    {
        var info = new FileInfo(path);
        var security = info.GetAccessControl(AccessControlSections.Access);
        security.RemoveAccessRuleSpecific(denyRule);
        info.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void RestoreWindowsDirectoryAccess(string path, FileSystemAccessRule denyRule)
    {
        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl(AccessControlSections.Access);
        security.RemoveAccessRuleSpecific(denyRule);
        info.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static bool CanOpenWindowsPath(string path, uint access, bool directory)
    {
        using var handle = CreateFileW(
            path,
            access,
            FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            directory ? WindowsFileFlagBackupSemantics : 0,
            IntPtr.Zero);
        return !handle.IsInvalid;
    }

    [UnsupportedOSPlatform("windows")]
    private static void SkipIfPrivilegedUnixIdentity()
    {
        if (GetEffectiveUserId() == 0)
        {
            throw new SkipTestException(
                "The privileged Unix identity cannot be denied access by owner mode bits.");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    private static RunnerProcessRequest Request(string script, IReadOnlyList<string> helperArguments) =>
        new(
            PowerShellExecutable(),
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, .. helperArguments],
            Path.GetDirectoryName(script),
            new Dictionary<string, string> { ["PROBE_ORDINARY"] = "ordinary" },
            SecretValues: []);

    private static string PowerShellExecutable()
    {
        var alias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            "pwsh.exe");
        return File.Exists(alias) ? alias : "pwsh";
    }

    private static string CreateScratch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"antiphon-runner-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteHelper(string scratch)
    {
        var path = Path.Combine(scratch, "probe-helper.ps1");
        File.WriteAllText(path, """
            $ErrorActionPreference = 'Stop'
            $mode = $args[0]
            if ($mode -eq 'environment') {
                [Console]::Out.WriteLine(('host=' + [bool](Test-Path ('Env:' + $args[1]))))
                [Console]::Out.WriteLine(('declared=' + ([Environment]::GetEnvironmentVariable($args[2]) -eq 'ordinary')))
                exit 0
            }
            if ($mode -eq 'arguments') {
                for ($i = 2; $i -lt $args.Count; $i++) {
                    $bytes = [Text.Encoding]::UTF8.GetBytes([string]$args[$i])
                    [Console]::Out.WriteLine([Convert]::ToBase64String($bytes))
                }
                exit 0
            }
            if ($mode -eq 'oversized') {
                [Console]::Out.Write(('O' * 8192))
                [Console]::Error.Write(('E' * 8192))
                exit 0
            }
            if ($mode -eq 'raw-value') {
                [Console]::Out.Write($args[1])
                exit 0
            }
            if ($mode -eq 'raw-error') {
                [Console]::Error.Write($args[1])
                exit 0
            }
            if ($mode -eq 'repeated-raw-value') {
                [Console]::Out.Write(([string]$args[1]) * [int]$args[2])
                exit 0
            }
            if ($mode -eq 'invalid-utf8') {
                $bytes = [byte[]](0xC3, 0x28)
                $stream = [Console]::OpenStandardOutput()
                $stream.Write($bytes, 0, $bytes.Length)
                $stream.Flush()
                exit 0
            }
            if ($mode -eq 'credential') {
                [Console]::Out.WriteLine(('API_TOKEN=' + $args[1]))
                [Console]::Out.WriteLine(('OPENAI_API_KEY=' + $args[1]))
                [Console]::Out.WriteLine(('{"OPENAI_API_KEY":"' + $args[1] + '"}'))
                [Console]::Error.WriteLine(('password: ' + $args[1]))
                [Console]::Error.WriteLine(('SERVICE_TOKEN=' + $args[1]))
                [Console]::Error.WriteLine(('{"SERVICE_TOKEN":"' + $args[1] + '"}'))
                exit 0
            }
            if ($mode -eq 'common-credentials') {
                [Console]::Out.WriteLine('AWS_SECRET_ACCESS_KEY=synthetic-common-value')
                [Console]::Out.WriteLine('{"DATABASE_URL":"synthetic-json-value"}')
                [Console]::Error.WriteLine('PRIVATE_KEY=synthetic-private-value')
                exit 0
            }
            if ($mode -eq 'child-executable') {
                [Console]::Out.WriteLine([Diagnostics.Process]::GetCurrentProcess().MainModule.FileName)
                exit 0
            }
            if ($mode -eq 'tree' -or $mode -eq 'tree-with-secret') {
                $pwsh = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
                $child = Start-Process -FilePath $pwsh -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Seconds 60') -PassThru
                [IO.File]::WriteAllText($args[1], ($PID.ToString() + ',' + $child.Id.ToString()))
                if ($mode -eq 'tree-with-secret') {
                    [Console]::Out.WriteLine($args[2])
                    [Console]::Error.WriteLine(('token=' + $args[2]))
                }
                Start-Sleep -Seconds 60
                exit 0
            }
            if ($mode -eq 'stdin-close') {
                [IO.File]::WriteAllText($args[1], $PID.ToString())
                [void][Console]::In.ReadLine()
                exit 0
            }
            throw 'Unknown helper mode.'
            """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static async Task WaitForFileAsync(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(path) && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        File.Exists(path).ShouldBeTrue();
    }

    private static async Task<int[]> ReadPidsAsync(string path)
    {
        await WaitForFileAsync(path);
        return (await File.ReadAllTextAsync(path))
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();
    }

    private static async Task AssertProcessExitedAsync(int pid)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited)
                    return;
            }
            catch (ArgumentException)
            {
                return;
            }

            await Task.Delay(50);
        }

        using var survivor = Process.GetProcessById(pid);
        survivor.HasExited.ShouldBeTrue($"Probe process {pid} survived cancellation or timeout.");
    }

    private static void StopProcessIfRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
            // The process already exited.
        }
    }
}
