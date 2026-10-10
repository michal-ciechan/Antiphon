using System.Diagnostics;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0889 D-1. A reader opened the way the harness reads sentinels must not block the shim append.
/// Reverting <c>Open-C889SharedRead</c> to <c>FileShare.Read</c> makes this fail.
/// The pin uses a named <c>EventWaitHandle</c>, which Unix refuses, so it skips off Windows (CARD-1173).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ScriptHarnessSharingTests
{
    private const string HolderSource = """
        $ErrorActionPreference = 'Stop'
        . "$env:C889_FIXTURE"
        $stream = Open-C889SharedRead -Path $env:C889_PIN_FILE
        $gate = New-Object System.Threading.EventWaitHandle($false, ([System.Threading.EventResetMode]::ManualReset), $env:C889_PIN_EVENT)
        [Console]::Out.WriteLine('HELD')
        [Console]::Out.Flush()
        [void]$gate.WaitOne()
        $stream.Dispose()
        $gate.Dispose()
        """;

    private const string AppenderSource = """
        $ErrorActionPreference = 'Stop'
        . "$env:C889_FIXTURE"
        Write-C889SentinelLine -DescriptorDir $env:C889_PIN_DIR -Stream 'stdout' -Text $env:C889_PIN_LINE
        """;

    [Test]
    public async Task Held_reader_accepts_shim_sentinel_append()
    {
        if (!OperatingSystem.IsWindows())
            throw new TUnit.Core.Exceptions.SkipTestException("Named EventWaitHandle sharing pin requires Windows");

        var nonce = Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), "c889-share-" + nonce);
        Directory.CreateDirectory(root);
        var sentinel = Path.Combine(root, "stdout.sentinel");
        await File.WriteAllTextAsync(sentinel, "");
        var line = "C889_SHARE_PIN_" + nonce;
        var eventName = "c889-pin-" + nonce;
        var fixture = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "fixtures", "c889-c578-observation.ps1");
        var holderScript = Path.Combine(root, "holder.ps1");
        var appenderScript = Path.Combine(root, "appender.ps1");
        await File.WriteAllTextAsync(holderScript, HolderSource);
        await File.WriteAllTextAsync(appenderScript, AppenderSource);
        using var release = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        using var holder = Start(holderScript, fixture, root, sentinel, line, eventName);
        var holderError = holder.StandardError.ReadToEndAsync();
        Exception? failure = null;
        try
        {
            using var started = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            string? ready;
            try
            {
                ready = await holder.StandardOutput.ReadLineAsync(started.Token);
            }
            catch (OperationCanceledException)
            {
                ready = null;
            }
            ready.ShouldBe("HELD");
            holder.HasExited.ShouldBeFalse("the harness reader must stay open during the append");
            var appended = await RunAppender(appenderScript, fixture, root, sentinel, line, eventName);
            appended.ExitCode.ShouldBe(0, appended.Text);
            holder.HasExited.ShouldBeFalse("the harness reader must stay open until the append is visible");
            await using var check = new FileStream(sentinel, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(check);
            var text = await reader.ReadToEndAsync();
            text.ShouldContain(line);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            release.Set();
            Stop(holder);
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }

        if (failure is not null)
        {
            var stderr = await holderError;
            throw new InvalidOperationException(failure.Message + "\n" + stderr, failure);
        }
    }

    private static Process Start(string script, string fixture, string root, string sentinel, string line, string eventName)
    {
        var psi = new ProcessStartInfo(ResolvePwsh())
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        psi.Environment["C889_FIXTURE"] = fixture;
        psi.Environment["C889_PIN_DIR"] = root;
        psi.Environment["C889_PIN_FILE"] = sentinel;
        psi.Environment["C889_PIN_LINE"] = line;
        psi.Environment["C889_PIN_EVENT"] = eventName;
        var process = Process.Start(psi) ?? throw new InvalidOperationException("pwsh did not start");
        process.StandardInput.Close();
        return process;
    }

    private static async Task<(int ExitCode, string Text)> RunAppender(
        string script, string fixture, string root, string sentinel, string line, string eventName)
    {
        using var process = Start(script, fixture, root, sentinel, line, eventName);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(budget.Token);
        }
        catch (OperationCanceledException)
        {
            Stop(process);
            return (124, "appender timed out\n" + await stdout + await stderr);
        }
        return (process.ExitCode, await stdout + await stderr);
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static string ResolvePwsh()
    {
        if (!OperatingSystem.IsWindows()) return "pwsh";
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        if (File.Exists(installed) && (File.GetAttributes(installed) & FileAttributes.ReparsePoint) == 0)
            return installed;
        throw new FileNotFoundException("Windows sharing pin requires a regular pwsh.exe under Program Files\\PowerShell\\7.");
    }
}
