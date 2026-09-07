using System.Diagnostics;
using System.Net;
using System.Text;
using Antiphon.Tests.TestHelpers;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CardFileBootstrapCheckTests
{
    [Test]
    [Arguments("[{\"cardFileWarnings\":[\"card_files_ignore_missing\"]}]", "WARN", "card_files_ignore_missing")]
    [Arguments("[{}]", "WARN", "Publication status unavailable")]
    [Arguments("[{\"cardFileWarnings\":[]}]", "PASS", "explicit opt-in")]
    public async Task Actual_bootstrap_privacy_probe_only_GETs_stub_and_warns_without_repair(string json, string level, string expected)
    {
        var root = Directory.CreateTempSubdirectory("c408-bootstrap").FullName;
        using var listener = new HttpListener(); var url = EphemeralHttpListener.BindLoopback(listener);
        var pump = Task.Run(async () => {
            var request = await listener.GetContextAsync();
            request.Request.HttpMethod.ShouldBe("GET"); request.Request.Url!.AbsolutePath.ShouldBe("/api/projects");
            request.Response.ContentType = "application/json"; await request.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(json)); request.Response.Close();
        });
        try
        {
            var script = Path.Combine(root, "probe.ps1"); var ignore = Path.Combine(root, ".gitignore");
            await File.WriteAllTextAsync(ignore, "# owner bytes\n");
            await File.WriteAllTextAsync(script, """
                param([string]$Source)
                $tokens=$null; $errors=$null
                $ast=[System.Management.Automation.Language.Parser]::ParseFile($Source,[ref]$tokens,[ref]$errors)
                if ($errors.Count) { throw 'Parse failed' }
                $probe=$ast.Find({ param($node) $node -is [System.Management.Automation.Language.TryStatementAst] -and $node.Extent.Text.Contains('$privacyProjects = Invoke-RestMethod') },$true)
                if ($null -eq $probe) { throw 'Privacy probe missing' }
                function Write-Check { param($Name,$Status,$Detail,$Remedial) Write-Output "$Name|$Status|$Detail|$Remedial" }
                & ([scriptblock]::Create($probe.Extent.Text))
                """);
            var start = new ProcessStartInfo("pwsh") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-Source", Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/bootstrap-check.ps1") }) start.ArgumentList.Add(arg);
            start.Environment["ANTIPHON_API"] = url.TrimEnd('/');
            using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(deadline.Token); await pump.WaitAsync(deadline.Token); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            process.ExitCode.ShouldBe(0, await error); var text = await output;
            text.ShouldContain("card-files|"+level+"|"); text.ShouldContain(expected);
            (await File.ReadAllTextAsync(ignore)).ShouldBe("# owner bytes\n"); Directory.Exists(Path.Combine(root, "docs/cards")).ShouldBeFalse();
        }
        finally { listener.Close(); Directory.Delete(root, recursive: true); }
    }
}
