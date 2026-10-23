using System.Diagnostics;
using Shouldly;

namespace Antiphon.Tests.Application;

/// <summary>A locally inherited child with explicit readiness/release and a 30-second outer bound.</summary>
internal sealed class PermissionWaitChild : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _stderr;
    private bool _released;
    private PermissionWaitChild(Process process) { _process = process; _stderr = process.StandardError.ReadToEndAsync(); }
    internal bool Exited => _process.HasExited;
    internal static async Task<PermissionWaitChild> StartAsync(CancellationToken ct)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "[Console]::Out.WriteLine('ready'); if ([Console]::In.ReadLine() -ne 'release') { exit 2 }" }) start.ArgumentList.Add(arg);
        var child = new PermissionWaitChild(Process.Start(start) ?? throw new IOException("child start failed"));
        try
        {
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
            bound.CancelAfter(TimeSpan.FromSeconds(30));
            (await child._process.StandardOutput.ReadLineAsync(bound.Token)).ShouldBe("ready");
            return child;
        }
        catch { await child.DisposeAsync(); throw; }
    }
    internal async Task ReleaseAsync()
    {
        if (_released) return;
        _released = true;
        if (!_process.HasExited) await _process.StandardInput.WriteLineAsync("release");
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _process.WaitForExitAsync(bound.Token);
        await Task.WhenAll(_process.StandardOutput.ReadToEndAsync(), _stderr);
        _process.ExitCode.ShouldBe(0, await _stderr);
    }
    public async ValueTask DisposeAsync()
    {
        try { await ReleaseAsync(); }
        finally
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
            await _stderr;
            _process.Dispose();
        }
    }
}
