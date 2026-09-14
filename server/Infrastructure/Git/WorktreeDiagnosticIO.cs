using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>Owned diagnostic processes and external calibration resources only.</summary>
public class WorktreeDiagnosticIO(WorktreeNativeIO native)
{
    public virtual bool Supported => OperatingSystem.IsWindows();
    public virtual bool Elevated
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
    // Never let Handle provision its license state or display first-run setup.
    public virtual bool LicenseReady => OperatingSystem.IsWindows()
        && Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Sysinternals\Handle", "EulaAccepted", null) is int accepted
        && accepted == 1;
    public virtual bool Exists(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.Directory) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
    public virtual bool TrustedExecutable(string path) => Path.IsPathFullyQualified(path) && File.Exists(path)
        && (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
    public virtual (string Version, string Identity) ToolIdentity(string path) =>
        (FileVersionInfo.GetVersionInfo(path).FileVersion ?? "unknown", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    public virtual string ScratchRoot => Path.GetTempPath();
    public virtual long? ProcessStart(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.StartTime.ToUniversalTime().Ticks; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    public virtual WorktreeDiagnosticControls CreateControls(string outsideRoot)
    {
        var root = Path.Combine(outsideRoot, "antiphon-handle-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        FileStream? file = null; WorktreeNativeHandle? directory = null;
        try
        {
            var path = Path.Combine(root, "held.bin");
            file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
            directory = native.Open(root, 0x80, 3, 3, 0x02200000, false);
            if (!directory.Succeeded) throw new IOException("directory_control_unavailable");
            return new(root, path, Environment.ProcessId, () => {
                directory.Dispose(); file.Dispose(); Directory.Delete(root, recursive: true);
            });
        }
        catch { directory?.Dispose(); file?.Dispose(); Directory.Delete(root, recursive: true); throw; }
    }

    public virtual async Task<WorktreeDiagnosticOutput> RunAsync(ProcessStartInfo start, WorktreeDiagnosticBytes bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new IOException("diagnostic_start_failed");
        async Task<byte[]> DrainAsync(Stream stream)
        {
            using var retained = new MemoryStream(); var buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, CancellationToken.None)) != 0)
            {
                var keep = bytes.Take(read);
                if (keep > 0) retained.Write(buffer, 0, keep);
            }
            return retained.ToArray();
        }
        var stdout = DrainAsync(process.StandardOutput.BaseStream);
        var stderr = DrainAsync(process.StandardError.BaseStream);
        try { await process.WaitForExitAsync(ct); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        await Task.WhenAll(stdout, stderr);
        return new(process.ExitCode, Encoding.UTF8.GetString(await stdout), bytes.Truncated);
    }
}

public sealed record WorktreeDiagnosticOutput(int ExitCode, string Csv, bool Truncated);
public sealed class WorktreeDiagnosticControls(string root, string file, int pid, Action dispose) : IDisposable
{
    public string Root { get; } = root;
    public string File { get; } = file;
    public int ProcessId { get; } = pid;
    public void Dispose() => dispose();
}
public sealed class WorktreeDiagnosticBytes
{
    private long _consumed;
    public bool Truncated => Interlocked.Read(ref _consumed) > 262144;
    public int Take(int count)
    {
        var total = Interlocked.Add(ref _consumed, count);
        return (int)Math.Clamp(262144 - (total - count), 0, count);
    }
}
