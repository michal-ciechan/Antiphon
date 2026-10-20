using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

/// <summary>A real owned child stands in for the compiler, through the production observer.</summary>
internal sealed class LandVerificationWindowHarness : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "c1379-child-" + Guid.NewGuid().ToString("N"));
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public LandingVerifier Verifier { get; }
    public LandVerificationWindowHarness()
    {
        Directory.CreateDirectory(_root);
        var script = Path.Combine(_root, "compiler.ps1");
        File.WriteAllText(script, """
            param([string]$Release, [string]$Results)
            while (-not (Test-Path -LiteralPath $Release)) { Start-Sleep -Milliseconds 20 }
            if ($Results) {
                [IO.Directory]::CreateDirectory($Results) | Out-Null
                [IO.File]::WriteAllText((Join-Path $Results 'run.trx'),
                    '<TestRun><ResultSummary><Counters executed="1" passed="1" failed="0" /></ResultSummary></TestRun>')
            }
            """);
        Verifier = new LandingVerifier
        {
            RunProcess = (cwd, observer, ct, _, args) =>
            {
                var index = Array.IndexOf(args, "--results-directory");
                var results = index >= 0 ? args[index + 1] : "";
                return AgentTaskLandService.RunProcessAsync(cwd, new StartedObserver(observer!, Started), ct,
                    "pwsh", "-NoProfile", "-File", script, Path.Combine(_root, "release"), results);
            },
        };
    }
    public void Release() => File.WriteAllText(Path.Combine(_root, "release"), "joined by owner");
    public ValueTask DisposeAsync()
    {
        Directory.Delete(_root, true);
        return ValueTask.CompletedTask;
    }

    private sealed class StartedObserver(ILandingChildObserver inner, TaskCompletionSource started) : ILandingChildObserver
    {
        public Task BeforeStartAsync(CancellationToken ct) => inner.BeforeStartAsync(ct);
        public async Task StartedAsync(int pid, long ticks, CancellationToken ct)
        {
            await inner.StartedAsync(pid, ticks, ct);
            started.TrySetResult();
        }
        public Task ExitedAsync(CancellationToken ct) => inner.ExitedAsync(ct);
        public void Line(string line) => inner.Line(line);
        public void Completed() => inner.Completed();
    }
}
