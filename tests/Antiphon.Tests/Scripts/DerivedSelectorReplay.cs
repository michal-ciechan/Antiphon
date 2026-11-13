using System.Diagnostics;
using System.Text.Json;

namespace Antiphon.Tests.Scripts;

/// <summary>One owned host for the class; every request gets a new PowerShell runspace.</summary>
public sealed class DerivedSelectorReplay : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private Task<string>? _stderr;
    private Exception? _failure;
    private int _requests;
    private bool _disposed;

    public async Task<(int Exit, string Stdout, string Text)> SelectAsync(
        string root, string script, string[] args)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await _gate.WaitAsync(deadline.Token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_failure is not null)
                throw new InvalidOperationException("Selector replay host already failed.", _failure);
            if (_process is null)
            {
                var repo = new DirectoryInfo(AppContext.BaseDirectory);
                while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Antiphon.sln")))
                    repo = repo.Parent;
                var checkout = repo?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
                var start = new ProcessStartInfo("pwsh")
                {
                    WorkingDirectory = checkout,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(checkout, "scripts", "selector-replay-host.ps1") })
                    start.ArgumentList.Add(arg);
                _process = Process.Start(start) ?? throw new InvalidOperationException("Selector host did not start.");
                _stderr = _process.StandardError.ReadToEndAsync();
            }

            var id = (++_requests).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var line = JsonSerializer.Serialize(new { Id = id, Root = root, Script = script, Args = args });
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), deadline.Token);
            await _process.StandardInput.FlushAsync(deadline.Token);
            var response = await _process.StandardOutput.ReadLineAsync(deadline.Token)
                ?? throw new InvalidOperationException("Selector replay host exited before its response.");
            using var document = JsonDocument.Parse(response);
            var payload = document.RootElement;
            if (payload.GetProperty("Id").GetString() != id || payload.GetProperty("ProcessId").GetInt32() != _process.Id)
                throw new InvalidOperationException("Selector replay response identity mismatch.");
            var stdout = payload.GetProperty("Stdout").GetString() ?? "";
            var stderr = payload.GetProperty("Stderr").GetString() ?? "";
            return (payload.GetProperty("Exit").GetInt32(), stdout, stdout + stderr);
        }
        catch (Exception failure)
        {
            _failure = failure;
            try { await StopAsync(kill: true); }
            catch (Exception cleanup) { throw new AggregateException(failure, cleanup); }
            throw;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            await StopAsync(kill: false);
            Console.WriteLine("SELECTOR_HOST requests=" + _requests + " stopped=true");
        }
        finally { _gate.Release(); }
    }

    private async Task StopAsync(bool kill)
    {
        if (_process is null) return;
        var process = _process;
        if (!process.HasExited)
        {
            if (kill) process.Kill(entireProcessTree: true);
            else process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        var diagnostics = _stderr is null ? "" : await _stderr.WaitAsync(TimeSpan.FromSeconds(5));
        var exit = process.ExitCode;
        process.Dispose();
        _process = null;
        if (!kill && (exit != 0 || !string.IsNullOrWhiteSpace(diagnostics)))
            throw new InvalidOperationException("Selector host teardown failed: exit=" + exit + " " + diagnostics);
    }
}
