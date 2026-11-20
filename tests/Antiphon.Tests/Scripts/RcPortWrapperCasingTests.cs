using System.Diagnostics;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-2010 A-2. ConvertTo-Json of a wrapper result stays camelCase.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcPortWrapperCasingTests
{
    [Test]
    [Timeout(540_000)]
    public async Task WrapperJsonStaysCamelCase()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var script = Path.Combine(root, "scripts", "test-rc-port-casing.ps1");
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(520));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("scripts/test-rc-port-casing.ps1 did not finish within 520s");
        }
        var output = await stdout + await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException("casing exit " + process.ExitCode + " " + output);
    }
}
