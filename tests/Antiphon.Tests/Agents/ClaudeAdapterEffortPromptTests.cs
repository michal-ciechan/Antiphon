using System.Text.Json;
using Antiphon.Agents.Pty;
using Antiphon.Agents.Pty.Tests;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Integration"), NotInParallel, ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeAdapterEffortPromptTests
{
    [Test]
    public async Task Local_probe_failure_logs_token_elapsed_and_writes()
    {
        var scratch = Directory.CreateTempSubdirectory("c449-deaf-").FullName;
        var script = Path.Combine(scratch, "deaf.ps1");
        await File.WriteAllTextAsync(script, """
            [Console]::Write("Claude ready`n> `n? for shortcuts")
            while ($true) { [void][Console]::ReadKey($true) }
            """);
        var settings = RunnerClaudeAdapterEffortPromptTests.Settings(1000);
        settings.ClaudeInputProbeRetypeIntervalMs = 100;
        var logger = new RunnerClaudeAdapterEffortPromptTests.StartupLogger();
        await using var adapter = new ClaudeAdapter(Options.Create(settings), logger);
        var spec = RunnerClaudeAdapterEffortPromptTests.Spec() with
        { Exe = "pwsh.exe", Cwd = scratch, Args = ["-NoProfile", "-File", script] };
        try
        {
            await adapter.StartAsync(spec, CancellationToken.None);
            (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
            var entry = logger.Errors.ShouldHaveSingleItem();
            var token = ComposerInputProbe.TokenFor(spec.SessionId!.Value);
            entry.Fields.ShouldContainKey("Token");
            entry.Fields["Token"].ShouldBe(token);
            entry.Fields["Writes"].ShouldBe(3);
            ((double)entry.Fields["Elapsed"]!).ShouldBeGreaterThan(0);
            entry.Message.ShouldContain(token);
            entry.Message.ShouldContain("3 write(s)");
            adapter.LaunchBlock.ShouldBeNull();
        }
        finally
        {
            if (adapter.Pid is not null)
            {
                (await adapter.KillAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).ShouldBeTrue();
                await adapter.Exited.WaitAsync(TimeSpan.FromSeconds(5));
            }
            await adapter.DisposeAsync();
            DeleteScratch(scratch);
        }
    }

    [Test, Arguments("xhigh"), Arguments("high")]
    public Task Local_adapter_keeps_requested_effort_and_probes_after_clearance(string requested) => RunAsync(false, requested);
    [Test]
    public Task Local_adapter_names_an_uncleared_effort_dialog() => RunAsync(true);

    [Test]
    public async Task Local_clear_failure_retains_evidence_before_scratch_cleanup()
    {
        var observation = new FailureObservation();
        await Should.ThrowAsync<ShouldAssertException>(() => RunAsync(false, "xhigh", true, observation));
        Directory.Exists(observation.Scratch).ShouldBeFalse();
        observation.Artifact.ShouldNotBeNull("readiness failure must retain evidence before scratch cleanup");
        var evidence = await File.ReadAllTextAsync(observation.Artifact);
        evidence.ShouldContain(observation.Token!);
        evidence.ShouldContain("NeverCleared");
        evidence.ShouldContain("xhigh");
        evidence.ShouldContain("ShouldAssertException");
        evidence.ShouldContain("cleanup: complete");
        evidence.Split("raw:\n")[1].Split("\nrendered:")[0].ShouldContain(observation.Token!);
        evidence.Split("rendered:\n")[1].Split("\ntrace:")[0].ShouldContain(observation.Token!);
        using var trace = JsonDocument.Parse(evidence.Split("trace:\n")[1].Split("\nlogger:")[0]);
        trace.RootElement.GetProperty("applied").GetString().ShouldBe("xhigh");
        trace.RootElement.GetProperty("composer").GetString().ShouldContain(observation.Token!);
    }

    private sealed class FailureObservation
    {
        internal string Scratch = "";
        internal string? Token;
        internal string? Artifact;
    }

    private static async Task RunAsync(bool stuck, string requested = "xhigh", bool neverClear = false, FailureObservation? observation = null)
    {
        var prior = Environment.GetEnvironmentVariable("ANTIPHON_PTY_BACKEND");
        var scratch = Path.Combine(Path.GetTempPath(), "c449-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        if (observation is not null) observation.Scratch = scratch;
        var capture = Path.Combine(scratch, "capture.json");
        var trace = Path.Combine(scratch, "trace.json");
        await File.WriteAllTextAsync(capture, JsonSerializer.Serialize(new
        {
            screen = EffortTestScreen.Capture(0),
            composer = ScriptedClaudeInputGuard.Composer("{{composer}}"),
        }));
        Environment.SetEnvironmentVariable("ANTIPHON_PTY_BACKEND", "modern");
        var settings = RunnerClaudeAdapterEffortPromptTests.Settings();
        // D-6: this real PowerShell fixture owns its clearance allowance. Runner fakes retain 500 ms.
        settings.ClaudeInputProbeClearTimeoutMs = 3000;
        var logger = new RunnerClaudeAdapterEffortPromptTests.StartupLogger();
        var adapter = new ClaudeAdapter(Options.Create(settings), logger);
        var spec = RunnerClaudeAdapterEffortPromptTests.Spec() with
        {
            Exe = "pwsh.exe", Cwd = scratch,
            Args = ["-NoProfile", "-File", Path.Combine(AppContext.BaseDirectory, "Agents", "Fixtures", "claude-effort-dialog.ps1"), "--effort", requested],
            Env = new Dictionary<string,string> { ["C449_CAPTURE"] = capture, ["C449_TRACE"] = trace, ["C449_STUCK"] = stuck ? "1" : "0", ["C449_REQUESTED"] = requested,
                ["C1516_CLEAR_DELAY_MS"] = Environment.GetEnvironmentVariable("C1516_CLEAR_DELAY_MS") ?? "0", ["C1516_NEVER_CLEAR"] = neverClear ? "1" : "0" }
        };
        var token = ComposerInputProbe.TokenFor(spec.SessionId!.Value);
        if (observation is not null) observation.Token = token;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Exception? primary = null, cleanup = null;
        string? artifact = null;
        string snapshot = "terminal observation unavailable: startup did not complete";
        await TestOwnedPtyHost.ScopeAsync(async () =>
        {
            try
            {
                await adapter.StartAsync(spec, CancellationToken.None);
                clock.Restart();
                using var cts = new CancellationTokenSource();
                var task = adapter.WaitForReadyAsync(cts.Token);
                try
                {
                    (await Task.WhenAny(task, Task.Delay(16000))).ShouldBe(task, "local adapter completed before independent watchdog");
                    if (neverClear)
                    {
                        (await task).ShouldBeFalse("controlled failure stays false");
                        adapter.LaunchBlock.ShouldBeNull();
                        adapter.SnapshotRenderedScreen().ShouldContain(token);
                        logger.Messages.ShouldContain(message => message.Contains("NeverCleared"));
                        using var failedTrace = JsonDocument.Parse(await EffortFixtureCompletion.ReadTraceAsync(trace));
                        failedTrace.RootElement.GetProperty("applied").GetString().ShouldBe(requested);
                        failedTrace.RootElement.GetProperty("composer").GetString().ShouldContain(token);
                    }
                    (await task).ShouldBe(!stuck, adapter.SnapshotRenderedScreen());
                    var text = stuck ? await EffortFixtureCompletion.ReadTraceAsync(trace)
                        : await EffortFixtureCompletion.WaitAsync(adapter.SnapshotRenderedScreen,
                            () => EffortFixtureCompletion.ReadTraceAsync(trace), () => clock.Elapsed,
                            (span, ct) => Task.Delay(span, ct), cts.Token);
                    using var json = JsonDocument.Parse(text);
                    if (stuck)
                    {
                        adapter.LaunchBlock!.Kind.ShouldBe(AgentLaunchBlockKind.EffortDialogNotCleared);
                        json.RootElement.GetProperty("applied").ValueKind.ShouldBe(JsonValueKind.Null);
                    }
                    else
                    {
                        json.RootElement.GetProperty("applied").GetString().ShouldBe(requested);
                        json.RootElement.GetProperty("composer").GetString().ShouldBe("");
                        var keys = json.RootElement.GetProperty("keys").EnumerateArray().Select(k => k.GetString()).ToArray();
                        keys[0].ShouldBe("j"); keys[1].ShouldBe("Enter"); keys.Last().ShouldBe("Ctrl+U");
                        adapter.LaunchBlock.ShouldBeNull();
                        EffortFixtureCompletion.EmptyComposer(adapter.SnapshotRenderedScreen()).ShouldBeTrue("rendered composer empty");
                        if (int.TryParse(spec.Env["C1516_CLEAR_DELAY_MS"], out var delay) && delay > 0)
                        {
                            var received = json.RootElement.GetProperty("clearReceived").GetDateTime();
                            var painted = json.RootElement.GetProperty("clearPainted").GetDateTime();
                            (painted - received).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(delay), "delayed clear reached");
                            Console.WriteLine($"C1516 delayed-clear received={received:O} painted={painted:O} requested-ms={delay}");
                        }
                    }
                }
                finally { await cts.CancelAsync(); try { await task; } catch (OperationCanceledException) { } }
            }
            catch (Exception ex) { primary = ex; throw; }
            finally
            {
                // Retain terminal state while the peer is still alive, even for a later teardown failure.
                try
                {
                    snapshot = await EffortFixtureEvidence.CaptureAsync(requested, spec.SessionId.Value, token, clock.Elapsed,
                        adapter.SnapshotRawOutput, adapter.SnapshotRenderedScreen, trace,
                        JsonSerializer.Serialize(new { logger.Messages, Errors = logger.Errors.Select(x => new { x.Message, x.Fields }) }));
                }
                catch (Exception ex) { snapshot += "\nevidence capture failed: " + ex; }
                try { artifact = await EffortFixtureEvidence.PublishAsync(snapshot, primary); }
                catch (Exception ex) { Console.WriteLine("C1516 evidence publication failed: " + ex); }
            }
        }, async () =>
        {
            try
            {
                await TestOwnedPtyHost.ScopeAsync(async () =>
                {
                    if (adapter.Pid is not null)
                    {
                        (await adapter.KillAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).ShouldBeTrue();
                        await adapter.Exited.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                }, () => TestOwnedPtyHost.ScopeAsync(() => adapter.DisposeAsync().AsTask(),
                    () => { DeleteScratch(scratch); return Task.CompletedTask; }));
            }
            catch (Exception ex) { cleanup = ex; throw; }
            finally
            {
                Environment.SetEnvironmentVariable("ANTIPHON_PTY_BACKEND", prior);
                if (artifact is not null)
                {
                    try
                    {
                        await EffortFixtureEvidence.CompleteAsync(artifact, primary, cleanup);
                        if (observation is not null) observation.Artifact = artifact;
                    }
                    catch (Exception ex) { Console.WriteLine("C1516 evidence publication failed: " + ex); }
                }
            }
        });
    }
    internal static void DeleteScratch(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(full).StartsWith("c449-")) throw new InvalidOperationException("Unexpected test scratch path");
        Directory.Delete(full, true);
    }
}
