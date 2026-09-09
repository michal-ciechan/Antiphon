using System.Text.Json;
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
    [Test, Arguments("xhigh"), Arguments("high")]
    public Task Local_adapter_keeps_requested_effort_and_probes_after_clearance(string requested) => RunAsync(false, requested);
    [Test]
    public Task Local_adapter_names_an_uncleared_effort_dialog() => RunAsync(true);

    private static async Task RunAsync(bool stuck, string requested = "xhigh")
    {
        var prior = Environment.GetEnvironmentVariable("ANTIPHON_PTY_BACKEND");
        var scratch = Path.Combine(Path.GetTempPath(), "c449-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var capture = Path.Combine(scratch, "capture.json");
        var trace = Path.Combine(scratch, "trace.json");
        await File.WriteAllTextAsync(capture, JsonSerializer.Serialize(new { screen = EffortTestScreen.Capture(0) }));
        Environment.SetEnvironmentVariable("ANTIPHON_PTY_BACKEND", "modern");
        await using var adapter = new ClaudeAdapter(Options.Create(RunnerClaudeAdapterEffortPromptTests.Settings()));
        try
        {
            var spec = RunnerClaudeAdapterEffortPromptTests.Spec() with
            {
                Exe = "pwsh.exe", Cwd = scratch,
                Args = ["-NoProfile", "-File", Path.Combine(AppContext.BaseDirectory, "Agents", "Fixtures", "claude-effort-dialog.ps1"), "--effort", requested],
                Env = new Dictionary<string,string> { ["C449_CAPTURE"] = capture, ["C449_TRACE"] = trace, ["C449_STUCK"] = stuck ? "1" : "0", ["C449_REQUESTED"] = requested }
            };
            await adapter.StartAsync(spec, CancellationToken.None);
            using var cts = new CancellationTokenSource();
            var task = adapter.WaitForReadyAsync(cts.Token);
            try
            {
                (await Task.WhenAny(task, Task.Delay(16000))).ShouldBe(task, "local adapter completed before independent watchdog");
                (await task).ShouldBe(!stuck, adapter.SnapshotRenderedScreen());
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(trace));
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
                }
            }
            finally { await cts.CancelAsync(); try { await task; } catch (OperationCanceledException) { } }
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTIPHON_PTY_BACKEND", prior);
            if (adapter.Pid is not null)
            {
                (await adapter.KillAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).ShouldBeTrue();
                await adapter.Exited.WaitAsync(TimeSpan.FromSeconds(5));
            }
            await adapter.DisposeAsync();
            DeleteScratch(scratch);
        }
    }

    internal static void DeleteScratch(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(full).StartsWith("c449-")) throw new InvalidOperationException("Unexpected test scratch path");
        Directory.Delete(full, true);
    }
}
