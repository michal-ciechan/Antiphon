using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;

namespace Antiphon.Tests.TestHelpers;

// Diagnostic only: no RmShutdown/RmRestart imports, and never controls a reported holder.
internal static class WindowsFileLockOwners
{
    internal static async Task<string> QueryAsync(string[] paths)
    {
        if (!OperatingSystem.IsWindows()) return "holder unknown: Windows required";
        var scratch = Directory.CreateTempSubdirectory("c1515-diagnostic-").FullName;
        Process? child = null;
        DateTime generation = default;
        Task<string>? stdout = null, stderr = null;
        try
        {
            var script = Path.Combine(scratch, "query.ps1");
            await File.WriteAllTextAsync(script, """
                $ErrorActionPreference = 'Stop'
                $assembly = [Reflection.Assembly]::LoadFrom($args[0])
                $type = $assembly.GetType('Antiphon.Tests.TestHelpers.WindowsFileLockOwners', $true)
                $method = $type.GetMethod('RunWorker', [Reflection.BindingFlags]'Public,Static')
                [Console]::Write($method.Invoke($null, [object[]]@($args[1])))
                """, Encoding.ASCII);
            var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = scratch, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-File", script, typeof(WindowsFileLockOwners).Assembly.Location, JsonSerializer.Serialize(paths) })
                start.ArgumentList.Add(arg);
            child = Process.Start(start) ?? throw new IOException("diagnostic_start_failed");
            generation = child.StartTime.ToUniversalTime();
            stdout = child.StandardOutput.ReadToEndAsync(); stderr = child.StandardError.ReadToEndAsync();
            try
            {
                using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await child.WaitForExitAsync(budget.Token);
                await Task.WhenAll(stdout, stderr).WaitAsync(budget.Token);
                return child.ExitCode == 0 ? await stdout : "holder unknown: diagnostic failed " + await stderr;
            }
            catch (OperationCanceledException)
            {
                return "holder unknown: diagnostic timed out; owned child termination/join required";
            }
        }
        finally
        {
            // The only child we may terminate is the exact Process.Start result above.
            if (child is not null)
            {
                try
                {
                    using var join = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    if (!child.HasExited)
                    {
                        if (child.StartTime.ToUniversalTime() != generation) throw new IOException("diagnostic generation changed");
                        child.Kill(entireProcessTree: true);
                    }
                    await child.WaitForExitAsync(join.Token);
                    if (stdout is not null && stderr is not null) await Task.WhenAll(stdout, stderr).WaitAsync(join.Token);
                }
                catch (Exception ex) { throw new DiagnosticCleanupException("Owned diagnostic child cleanup failed; retained " + scratch, ex); }
                finally { child.Dispose(); }
            }
            Directory.Delete(scratch, true);
        }
    }

    internal sealed class DiagnosticCleanupException(string message, Exception inner) : IOException(message, inner);

    public static string RunWorker(string pathsJson)
    {
        var paths = JsonSerializer.Deserialize<string[]>(pathsJson)!;
        var key = new StringBuilder(33);
        var status = RmStartSession(out var session, 0, key);
        if (status != 0) return "holder unknown: RmStartSession=" + status;
        try
        {
            status = RmRegisterResources(session, (uint)paths.Length, paths, 0, IntPtr.Zero, 0, IntPtr.Zero);
            if (status != 0) return "holder unknown: RmRegisterResources=" + status;
            uint count = 0;
            status = RmGetList(session, out var needed, ref count, null, out _);
            for (var attempt = 0; status == 234 && attempt < 3; attempt++)
            {
                if (needed > 512) return "holder unknown: owner list exceeds diagnostic bound";
                var rows = new ProcessInfo[needed];
                count = needed;
                status = RmGetList(session, out needed, ref count, rows, out _);
                if (status != 0) continue;
                if (count == 0) return "holder unknown: empty owner list";
                return string.Join("; ", rows.Take((int)count).Select(Describe));
            }
            return "holder unknown: " + (status == 0 ? "empty owner list" : $"RmGetList={status}; partial query");
        }
        finally { RmEndSession(session); }
    }

    private static string Describe(ProcessInfo row)
    {
        var fileTime = ((long)(uint)row.Process.Start.dwHighDateTime << 32) | (uint)row.Process.Start.dwLowDateTime;
        var start = DateTime.FromFileTimeUtc(fileTime);
        var image = "unknown";
        try
        {
            using var process = Process.GetProcessById(row.Process.Pid);
            if (process.StartTime.ToUniversalTime() == start)
                image = process.MainModule?.FileName ?? "unknown";
            else image = "unknown: generation changed";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { image = "unknown: unreadable"; }
        return $"holder pid={row.Process.Pid} start={start.Ticks} application={row.Application} service={row.Service} image={image}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess { internal int Pid; internal FILETIME Start; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        internal UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Application;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string Service;
        internal int Type;
        internal uint Status, SessionId;
        [MarshalAs(UnmanagedType.Bool)] internal bool Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, uint flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint files, string[] names, uint applications, IntPtr processes, uint services, IntPtr serviceNames);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] ProcessInfo[]? info, out uint reasons);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);
}
