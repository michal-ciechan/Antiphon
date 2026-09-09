using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.FakeLlmApi;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Agents;

[Category("Integration"), NotInParallel("Headed"), ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeEffortPromptCanaryTests
{
    [Test]
    public async Task Real_effort_picker_preserves_xhigh_and_reaches_an_empty_composer()
    {
        HeadedClaudeGate.SkipIfNotEligible();
        RealCliStubGate.SkipIfNotEligible(AgentKind.ClaudeCode);
        var exe = RealCliStubGate.ResolveClaudeOrThrow();
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new SkipTestException("V-21 unverified: actual Claude executable unavailable; wrapper isolation not assumed");
        var scratch = Path.Combine(Path.GetTempPath(), "c449-canary-" + Guid.NewGuid().ToString("N"));
        var config = Path.Combine(scratch, "config");
        Directory.CreateDirectory(scratch);
        var evidence = Path.Combine(AppContext.BaseDirectory, "TestResults", "c449-real-picker-evidence.json");
        Directory.CreateDirectory(Path.GetDirectoryName(evidence)!);
        var frames = new List<object>();
        var keys = new List<object>();
        var status = "V-21 unverified: actual effort menu did not appear within 45 seconds";
        var version = "unknown";
        var clock = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Claude = true }, cts.Token);
        var syntheticKey = "stub-c449-" + Guid.NewGuid().ToString("N");
        RealCliStubClaudeConfig.SeedOnboarding(config, syntheticKey, scratch);
        var overlay = RealCliStubEnv.ForClaude(stub.BaseUrl, syntheticKey, config);
        await using var runner = new PtyAgentRunner("modern");
        string Sanitize(string screen) => screen.Replace(syntheticKey, "[synthetic-key]").Replace(syntheticKey[^20..], "[synthetic-key-tail]");
        Task<string> Snapshot(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var screen = runner.SnapshotScreen();
            frames.Add(new { ms = clock.ElapsedMilliseconds, screen = Sanitize(screen) });
            return Task.FromResult(screen);
        }
        async Task Write(string key, CancellationToken ct)
        {
            keys.Add(new { ms = clock.ElapsedMilliseconds, key });
            await runner.WriteAsync(key, ct);
        }
        try
        {
            await runner.StartAsync(exe, ["--model", "fable", "--effort", "xhigh", "--dangerously-skip-permissions"],
                scratch, overlay.Env.ToDictionary(k => k.Key, v => v.Value), cols: 120, rows: 30, ct: cts.Token);
            runner.Backend!.Backend.ShouldBe(PtyBackend.ModernConPty);
            var found = false;
            while (clock.Elapsed < TimeSpan.FromSeconds(45) && !runner.Exited.IsCompleted)
            {
                var screen = await Snapshot(cts.Token);
                var match = Regex.Match(screen, @"Claude Code v([\d.]+)");
                if (match.Success) version = match.Groups[1].Value;
                if (ClaudeEffortPrompt.Parse(screen) is not null) { found = true; break; }
                await Task.Delay(250, cts.Token);
            }
            if (!found) throw new SkipTestException(status + "; see sanitized evidence for visible prerequisite/UI");
            status = "V-21 failed: target picker appeared; acceptance pending";
            var resolved = await ClaudeEffortPrompt.ResolveAsync(Snapshot, Write, new("xhigh"), TimeSpan.FromSeconds(15), cts.Token);
            resolved.Cleared.ShouldBeTrue(resolved.Detail);
            var observed = ClaudeEffortPrompt.CurrentEffort(await Snapshot(cts.Token));
            var probe = await ComposerInputProbe.RunAsync("zzc449live", Snapshot, Write,
                ComposerProbeOptions.FromMilliseconds((int)(120000 - clock.ElapsedMilliseconds), 100, 10000, 2000), null,
                ClaudeBlockingPromptDetector.IsBlocked, cts.Token);
            probe.Responsive.ShouldBeTrue();
            runner.SnapshotScreen().ShouldNotContain("zzc449live");
            if (observed is null)
            {
                status = "V-21 unverified applied effort: selection/clearance and composer round trip passed; banner absent";
                throw new SkipTestException(status);
            }
            observed.ShouldBe("xhigh");
            status = "V-21 passed: real picker kept xhigh and completed composer round trip";
        }
        finally
        {
            await cts.CancelAsync();
            if (runner.Pid is not null)
            {
                (await runner.KillAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
                await runner.Exited.WaitAsync(TimeSpan.FromSeconds(5));
            }
            await runner.DisposeAsync();
            var chatRequests = stub.Requests.All.Where(r => r.Method == "POST" && r.Path == "/v1/messages")
                .Select(r => new { r.Method, r.Path }).ToArray();
            await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new { status, version, backend = runner.Backend?.Backend.ToString(), effort = "xhigh", keys, frames, chatRequests }));
            ClaudeAdapterEffortPromptTests.DeleteScratch(scratch);
            chatRequests.ShouldBeEmpty("no submitted user/probe turn may reach the stub");
        }
    }
}
