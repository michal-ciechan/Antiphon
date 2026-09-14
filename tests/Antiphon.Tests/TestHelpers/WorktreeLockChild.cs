using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

/// <summary>A locally inherited holder. Readiness and release are pipe messages, never sleeps.</summary>
internal sealed class WorktreeLockChild : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly StreamWriter _writer;
    private readonly Process _child;
    private readonly Task<string> _stdout, _stderr;
    private readonly string _scratch;
    private bool _released;
    public int ProcessId => _child.Id;
    public long StartTicks { get; }
    public bool Exited => _child.HasExited;

    private WorktreeLockChild(NamedPipeServerStream pipe, Process child, string scratch)
    {
        _pipe = pipe; _child = child; _scratch = scratch;
        _writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        _stdout = child.StandardOutput.ReadToEndAsync(); _stderr = child.StandardError.ReadToEndAsync();
        StartTicks = child.StartTime.ToUniversalTime().Ticks;
    }

    public static async Task<WorktreeLockChild> StartAsync(string mode, string target)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "antiphon-c443-child-" + Guid.NewGuid().ToString("N"));
        if (WorktreeNativeIO.Within(scratch, target)) throw new InvalidOperationException("Child controls must be outside target");
        Directory.CreateDirectory(scratch);
        var pipeName = "antiphon-c443-" + Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var script = Path.Combine(scratch, "holder.ps1");
        await File.WriteAllTextAsync(script, """
            $ErrorActionPreference = 'Stop'
            $assembly = [Reflection.Assembly]::LoadFrom($args[0])
            $type = $assembly.GetType('Antiphon.Tests.TestHelpers.WorktreeLockChild', $true)
            $method = $type.GetMethod('RunWorkerAsync', [Reflection.BindingFlags]'Public,Static')
            $task = $method.Invoke($null, [object[]]@($args[1], $args[2], $args[3]))
            $task.GetAwaiter().GetResult()
            """, Encoding.ASCII);
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = scratch, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-File", script, typeof(WorktreeLockChild).Assembly.Location, mode, target, pipeName })
            start.ArgumentList.Add(arg);
        var child = new WorktreeLockChild(pipe, Process.Start(start) ?? throw new IOException("holder_start_failed"), scratch);
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await pipe.WaitForConnectionAsync(budget.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            var ready = JsonSerializer.Deserialize<Ready>((await reader.ReadLineAsync(budget.Token))!)!;
            if (ready.Pid != child.ProcessId || ready.StartTicks != child.StartTicks || ready.Mode != mode)
                throw new InvalidOperationException("holder_readiness_identity_mismatch");
            return child;
        }
        catch { await child.DisposeAsync(); throw; }
    }

    public async Task ReleaseAsync()
    {
        if (_released) return;
        _released = true;
        if (_pipe.IsConnected && !_child.HasExited) await _writer.WriteLineAsync("release");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _child.WaitForExitAsync(budget.Token);
        await Task.WhenAll(_stdout, _stderr);
        if (_child.ExitCode != 0) throw new IOException("holder_exit_failed: " + await _stderr);
    }

    public async ValueTask DisposeAsync()
    {
        try { await ReleaseAsync(); }
        finally
        {
            if (!_child.HasExited) _child.Kill(entireProcessTree: true);
            await _child.WaitForExitAsync(); await Task.WhenAll(_stdout, _stderr);
            _writer.Dispose(); _pipe.Dispose(); _child.Dispose();
            var full = Path.GetFullPath(_scratch);
            if (!WorktreeNativeIO.Within(full, Path.GetTempPath()) || !Path.GetFileName(full).StartsWith("antiphon-c443-child-", StringComparison.Ordinal))
                throw new InvalidOperationException("Child cleanup escaped owned scratch");
            Directory.Delete(full, recursive: true);
        }
    }

    public static async Task RunWorkerAsync(string mode, string target, string pipeName)
    {
        var originalCwd = Environment.CurrentDirectory;
        IDisposable? held = null;
        try
        {
            if (mode == "cwd") Environment.CurrentDirectory = target;
            else if (mode == "directory")
            {
                var directory = new WorktreeNativeIO().Open(target, 0x80, 3, 3, 0x02200000, false);
                held = directory;
                if (!directory.Succeeded) throw new IOException("holder_directory_open_failed");
            }
            else if (mode is "file" or "delete-sharing")
                held = new FileStream(target, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | (mode == "delete-sharing" ? FileShare.Delete : 0));
            else throw new ArgumentException("unknown_holder_mode");
            using var process = Process.GetCurrentProcess();
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(30000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new Ready(process.Id, process.StartTime.ToUniversalTime().Ticks, mode)));
            if (await reader.ReadLineAsync() != "release") throw new IOException("holder_release_missing");
        }
        finally { Environment.CurrentDirectory = originalCwd; held?.Dispose(); }
    }
    private sealed record Ready(int Pid, long StartTicks, string Mode);
}
