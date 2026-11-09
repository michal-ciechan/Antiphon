using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// scripts/lib/land-audit-tool.ps1: the default land-graph audit tool is rebuilt when it is missing, has no
/// stamp, or its sources changed; it is reused when current; a failed rebuild refuses and keeps the old tool;
/// an explicit -ToolPath is used as given; a scripts copy without the tool's project keeps its DLL. A stand-in
/// dotnet on PATH plays the build, so no real build runs.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandAuditToolResolverTests
{
    private const string Dll = "tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/Antiphon.LandGraphAudit.dll";
    private const string Stamp = "tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/land-audit.source-hash";

    [Test]
    public async Task RebuildsOnlyWhenMissingStampedStaleOrChanged()
    {
        var root = Directory.CreateTempSubdirectory("antiphon-land-tool-").FullName;
        try
        {
            Write(root, "tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj", "<Project />");
            Write(root, "tools/Antiphon.LandGraphAudit/Program.cs", "class P { }");
            Write(root, "tools/Antiphon.LandGraphAudit.Core/Core.cs", "class C { }");

            var first = await ResolveAsync(root, null, fail: false);
            first.Text.ShouldContain("PATH=" + Full(root, Dll), Case.Sensitive, first.Text);
            first.Text.ShouldContain("land audit tool: rebuilding (missing)", Case.Sensitive, first.Text);
            File.ReadAllText(Full(root, Dll)).ShouldBe("built 1");
            File.Exists(Full(root, Stamp)).ShouldBeTrue();

            var current = await ResolveAsync(root, null, fail: false);
            current.Text.ShouldContain("PATH=" + Full(root, Dll), Case.Sensitive, current.Text);
            current.Text.ShouldNotContain("rebuilding", Case.Sensitive, current.Text);

            // A build output in bin/obj does not count as a source.
            Write(root, "tools/Antiphon.LandGraphAudit/obj/Generated.cs", "class G { }");
            (await ResolveAsync(root, null, fail: false)).Text.ShouldNotContain("rebuilding", Case.Sensitive);

            Write(root, "tools/Antiphon.LandGraphAudit.Core/Core.cs", "class C { int x; }");
            var changed = await ResolveAsync(root, null, fail: false);
            changed.Text.ShouldContain("land audit tool: rebuilding (sources changed)", Case.Sensitive, changed.Text);
            File.ReadAllText(Full(root, Dll)).ShouldBe("built 2");

            // A hand-built DLL without a stamp is rebuilt once.
            File.Delete(Full(root, Stamp));
            (await ResolveAsync(root, null, fail: false)).Text.ShouldContain("rebuilding (sources changed)", Case.Sensitive);
            File.ReadAllText(Full(root, Dll)).ShouldBe("built 3");

            // A failed rebuild refuses with the evaluator-missing text and leaves the old tool in place.
            Write(root, "tools/Antiphon.LandGraphAudit/Program.cs", "class P { int y; }");
            var failed = await ResolveAsync(root, null, fail: true);
            failed.Text.ShouldContain("ERROR=built evaluator missing: " + Full(root, Dll) + " (rebuild failed, exit 1)", Case.Sensitive, failed.Text);
            failed.Text.ShouldNotContain("PATH=", Case.Sensitive);
            File.ReadAllText(Full(root, Dll)).ShouldBe("built 3");

            // An explicit tool path is used as given, never rebuilt.
            var explicitTool = Path.Combine(root, "elsewhere.dll");
            File.WriteAllText(explicitTool, "x");
            var given = await ResolveAsync(root, explicitTool, fail: true);
            given.Text.ShouldContain("PATH=" + Path.GetFullPath(explicitTool), Case.Sensitive, given.Text);
            given.Text.ShouldNotContain("rebuilding", Case.Sensitive);
            (await ResolveAsync(root, Path.Combine(root, "absent.dll"), fail: false)).Text
                .ShouldContain("ERROR=built evaluator missing: " + Path.Combine(root, "absent.dll"), Case.Sensitive);

            // A scripts copy without the tool's project keeps whatever DLL it has, and refuses without one.
            Directory.Delete(Path.Combine(root, "tools", "Antiphon.LandGraphAudit"), recursive: true);
            Write(root, Dll, "copied");
            (await ResolveAsync(root, null, fail: true)).Text.ShouldContain("PATH=" + Full(root, Dll), Case.Sensitive);
            File.Delete(Full(root, Dll));
            (await ResolveAsync(root, null, fail: false)).Text.ShouldContain("ERROR=built evaluator missing: " + Full(root, Dll), Case.Sensitive);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Full(string root, string relative) => Path.GetFullPath(Path.Combine(root, relative));

    private static void Write(string root, string relative, string text)
    {
        var path = Full(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // Runs Resolve-LandAuditTool with a stand-in dotnet first on PATH. The stand-in writes the DLL into the
    // -o folder, numbering builds, or exits 1 when the failure switch is set.
    private static async Task<(int Code, string Text)> ResolveAsync(string root, string? toolPath, bool fail)
    {
        var shim = Path.Combine(root, ".shim");
        Directory.CreateDirectory(shim);
        var counter = Path.Combine(shim, "count.txt");
        File.WriteAllText(Path.Combine(shim, "dotnet.ps1"), """
            $out = $null
            for ($i = 0; $i -lt $args.Count; $i++) { if ($args[$i] -eq '-o') { $out = $args[$i + 1] } }
            if ($env:C_LAND_TOOL_FAIL -eq '1') { Write-Output 'error CS0000: stand-in build failed'; exit 1 }
            $n = 1
            if (Test-Path -LiteralPath $env:C_LAND_TOOL_COUNT) { $n = [int](Get-Content -LiteralPath $env:C_LAND_TOOL_COUNT) + 1 }
            Set-Content -LiteralPath $env:C_LAND_TOOL_COUNT -Value $n
            [void][System.IO.Directory]::CreateDirectory($out)
            [System.IO.File]::WriteAllText((Join-Path $out 'Antiphon.LandGraphAudit.dll'), ('built ' + $n))
            exit 0
            """);
        if (OperatingSystem.IsWindows())
            File.WriteAllText(Path.Combine(shim, "dotnet.cmd"), "@pwsh -NoProfile -File \"%~dp0dotnet.ps1\" %*\r\n");
        else
        {
            var script = Path.Combine(shim, "dotnet");
            File.WriteAllText(script, "#!/bin/sh\nexec pwsh -NoProfile -File \"$(dirname \"$0\")/dotnet.ps1\" \"$@\"\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var lib = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "land-audit-tool.ps1");
        var command = ". '" + lib.Replace("'", "''") + "'; $r = Resolve-LandAuditTool '" + root.Replace("'", "''") + "' "
            + (toolPath is null ? "''" : "'" + toolPath.Replace("'", "''") + "'")
            + "; if ($r.ContainsKey('Error')) { 'ERROR=' + $r.Error } else { 'PATH=' + $r.Path }";
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);
        start.Environment["PATH"] = shim + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        start.Environment["C_LAND_TOOL_FAIL"] = fail ? "1" : "0";
        start.Environment["C_LAND_TOOL_COUNT"] = counter;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, stdout + "\n" + await stderr);
    }
}
