using System.Diagnostics;
using System.Text;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// Real dotnet builds in scratch git repositories. The guarantee is a clean tracked
// baseline plus a post-build restore, so these cases must stay red when a build
// can leave a tracked file overwritten.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointTrackedBaselineTests : CheckpointTestBase
{
    private const string OutputPath = "bin-probe/";
    private const string OriginalText = "TRACKED-ORIGINAL";

    [Test]
    [Timeout(300_000)]
    public async Task local_output_property_is_restored(CancellationToken cancellationToken)
    {
        RequireLinux();
        var root = BeginRepo();
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk" TreatAsLocalProperty="OutputPath">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                <OutputPath>legacy/</OutputPath>
              </PropertyGroup>
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Value => 1; }\n");
        var victim = Plant(root, "App/legacy/App.dll");
        await AssertRestoredAsync(root, "local-output", victim, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task intermediate_output_path_is_restored(CancellationToken cancellationToken)
    {
        RequireLinux();
        var root = BeginRepo();
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                <IntermediateOutputPath>generated/</IntermediateOutputPath>
              </PropertyGroup>
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Value => 1; }\n");
        var victim = Plant(root, "App/generated/App.AssemblyInfo.cs");
        await AssertRestoredAsync(root, "intermediate", victim, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task generated_target_file_is_restored(CancellationToken cancellationToken)
    {
        RequireLinux();
        var root = BeginRepo();
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
              </PropertyGroup>
              <Target Name="GenerateVersion" BeforeTargets="BeforeBuild">
                <WriteLinesToFile File="$(MSBuildProjectDirectory)/GeneratedVersion.txt" Lines="generated version 1" Overwrite="true" />
              </Target>
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Value => 1; }\n");
        var victim = Plant(root, "App/GeneratedVersion.txt");
        await AssertRestoredAsync(root, "generated", victim, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task imported_props_reference_is_restored(CancellationToken cancellationToken)
    {
        RequireLinux();
        var root = BeginRepo();
        Write(root, "Lib/Lib.csproj", """
            <Project Sdk="Microsoft.NET.Sdk" TreatAsLocalProperty="OutputPath">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                <OutputPath>legacy/</OutputPath>
              </PropertyGroup>
            </Project>
            """);
        Write(root, "Lib/Marker.cs", "namespace Lib; public static class Marker { public static int Value => 7; }\n");
        Write(root, "App/References.props", """
            <Project>
              <ItemGroup>
                <ProjectReference Include="../Lib/Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
              </PropertyGroup>
              <Import Project="References.props" />
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Use() => Lib.Marker.Value; }\n");
        var victim = Plant(root, "Lib/legacy/Lib.dll");
        await AssertRestoredAsync(root, "imported-ref", victim, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task transitive_reference_is_restored(CancellationToken cancellationToken)
    {
        RequireLinux();
        var root = BeginRepo();
        Write(root, "Lib/Lib.csproj", """
            <Project Sdk="Microsoft.NET.Sdk" TreatAsLocalProperty="OutputPath">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                <OutputPath>legacy/</OutputPath>
              </PropertyGroup>
            </Project>
            """);
        Write(root, "Lib/Marker.cs", "namespace Lib; public static class Marker { public static int Value => 7; }\n");
        Write(root, "Mid/Mid.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
              </PropertyGroup>
              <ItemGroup><ProjectReference Include="../Lib/Lib.csproj" /></ItemGroup>
            </Project>
            """);
        Write(root, "Mid/M.cs", "namespace Mid; public static class M { public static int Use() => Lib.Marker.Value; }\n");
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
              </PropertyGroup>
              <ItemGroup><ProjectReference Include="../Mid/Mid.csproj" /></ItemGroup>
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Use() => Mid.M.Use(); }\n");
        var victim = Plant(root, "Lib/legacy/Lib.dll");
        await AssertRestoredAsync(root, "transitive", victim, cancellationToken);
    }

    [Test]
    [Timeout(180_000)]
    public async Task dirty_tracked_tree_is_refused_and_unmodified(CancellationToken cancellationToken)
    {
        RequireLinux();
        var root = BeginRepo();
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
              </PropertyGroup>
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Value => 1; }\n");
        Write(root, "keep.txt", OriginalText);
        Commit(root);
        var keep = Path.Combine(root, "keep.txt");
        var marker = Path.Combine(root, "App", "Marker.cs");
        var markerBytes = File.ReadAllBytes(marker);
        File.WriteAllText(keep, "DIRTY-BEFORE-BUILD");
        var dirty = File.ReadAllBytes(keep);

        var tool = await RunToolAsync(root, "dirty-tool", cancellationToken);
        AssertDirtyRefused(tool.Output, keep, dirty, marker, markerBytes);
        tool.Exit.ShouldBe(2, tool.Output);

        File.WriteAllBytes(keep, dirty);
        var script = await RunScriptAsync(root, "dirty-script", cancellationToken);
        AssertDirtyRefused(script.Output, keep, dirty, marker, markerBytes);
        script.Exit.ShouldBe(2, script.Output);
    }

    [Test]
    [Timeout(300_000)]
    public async Task clean_untracked_output_build_exits_zero(CancellationToken cancellationToken)
    {
        RequireLinux();
        var root = BeginRepo();
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
              </PropertyGroup>
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Value => 1; }\n");
        Write(root, "keep.txt", OriginalText);
        Commit(root);
        var keep = File.ReadAllBytes(Path.Combine(root, "keep.txt"));

        var tool = await RunToolAsync(root, "clean-tool", cancellationToken);
        File.ReadAllBytes(Path.Combine(root, "keep.txt")).ShouldBe(keep, tool.Output);
        tool.Output.ShouldNotContain("refused: tampered", Case.Sensitive);
        tool.Output.ShouldNotContain("refused: dirty-tracked", Case.Sensitive);
        tool.Output.ShouldContain("no TRX", Case.Sensitive);
        Directory.Exists(Path.Combine(root, "App", "bin-probe")).ShouldBeTrue(tool.Output);

        var script = await RunScriptAsync(root, "clean-script", cancellationToken);
        File.ReadAllBytes(Path.Combine(root, "keep.txt")).ShouldBe(keep, script.Output);
        script.Output.ShouldNotContain("refused: tampered", Case.Sensitive);
        script.Output.ShouldNotContain("refused: dirty-tracked", Case.Sensitive);
        script.Output.ShouldContain("DOTNET build EXIT CODE: 0", Case.Sensitive);
    }

    [Test]
    [Timeout(420_000)]
    public async Task fresh_checkout_run_executes_a_test(CancellationToken cancellationToken)
    {
        RequireLinux();
        var root = BeginRepo();
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <ImplicitUsings>disable</ImplicitUsings>
                <Nullable>disable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="TUnit" Version="1.44.0" />
              </ItemGroup>
            </Project>
            """);
        Write(root, "App/FreshProbe.cs", """
            public sealed class FreshProbe
            {
                [TUnit.Core.Test]
                public void one() { }
            }
            """);
        Commit(root);
        Directory.Exists(Path.Combine(root, "App", "obj")).ShouldBeFalse("a fresh checkout has no normal obj cache");

        var tool = await RunToolAsync(root, "fresh-tool", "/*/*/FreshProbe/*", "1", cancellationToken);
        tool.Exit.ShouldBe(0, tool.Output);
        tool.Output.ShouldContain("executed=1", Case.Sensitive);
        tool.Output.ShouldContain("buildSource=verified", Case.Sensitive);

        var script = await RunScriptAsync(root, "fresh-script", "/*/*/FreshProbe/*", "1", cancellationToken);
        script.Exit.ShouldBe(0, script.Output);
        script.Output.ShouldContain("executed=1", Case.Sensitive);
        script.Output.ShouldContain("buildSource=verified", Case.Sensitive);
    }

    [Test]
    [Timeout(60_000)]
    public async Task tracked_snapshot_contains_committed_files(CancellationToken cancellationToken)
    {
        RequireLinux();
        var root = BeginRepo();
        foreach (var relative in new[] { "App/App.csproj", "App/Marker.cs", "App/keep.txt", "outside.txt", "notes/readme.txt" })
            Write(root, relative, relative + "\n");
        Commit(root);
        var script = Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "run-checkpoint.ps1");
        var probe = await RunPwshAsync(root, $$"""
            $ErrorActionPreference = 'Stop'
            $tokens = $null
            $parseErrors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile({{Quote(script)}}, [ref]$tokens, [ref]$parseErrors)
            if ($parseErrors -and @($parseErrors).Count -gt 0) { throw ($parseErrors | Out-String) }
            $names = @(
                'Get-CheckpointTrackedSnapshot',
                'Get-CheckpointUntrackedManifest',
                'Join-CheckpointRepoPath',
                'Get-CheckpointContentStamp',
                'Get-CheckpointStampLink',
                'Get-CheckpointSha256',
                'Get-CheckpointRepoRelative',
                'Invoke-CheckpointGit',
                'Resolve-CheckpointProjectDirectory',
                'Test-CheckpointProjectExtension')
            $buffer = New-Object System.Text.StringBuilder
            foreach ($name in $names) {
                $fn = $null
                foreach ($candidate in $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) {
                    if ($candidate.Name -eq $name) { $fn = $candidate; break }
                }
                if ($null -eq $fn) { throw "missing $name" }
                [void]$buffer.AppendLine($fn.Extent.Text)
            }
            Invoke-Expression $buffer.ToString()
            $root = {{Quote(root)}}
            $project = Join-Path $root 'App/App.csproj'
            $shot = Get-CheckpointTrackedSnapshot -Projects @($project)
            if ($null -ne $shot.Error) { throw $shot.Error }
            @($shot.Hashes.Keys) | Sort-Object | ConvertTo-Json -Compress
            """, cancellationToken);
        probe.Exit.ShouldBe(0, probe.Output);
        foreach (var relative in new[] { "App/App.csproj", "App/Marker.cs", "App/keep.txt", "outside.txt", "notes/readme.txt" })
            probe.Output.ShouldContain(relative, Case.Sensitive);
    }

    [Test]
    [Timeout(420_000)]
    public async Task staged_untracked_file_is_kept_and_unstaged(CancellationToken cancellationToken)
    {
        // case: the build runs git add on user-keep.txt, which is absent from HEAD.
        // mutation: restore that staged path with --worktree, deleting a path HEAD does not contain.
        // red: user-keep.txt is missing or its bytes differ after the tool or the script row.
        RequireLinux();
        var root = BeginRepo();
        WriteStageProject(root, "git --git-dir=.git --work-tree=. add -- user-keep.txt");
        var bytes = Encoding.ASCII.GetBytes("USER-KEEP");
        await AssertStagedUserFileSurvivesAsync(root, "untracked", "user-keep.txt", bytes, cancellationToken);
    }

    [Test]
    [Timeout(420_000)]
    public async Task staged_ignored_file_is_kept_and_unstaged(CancellationToken cancellationToken)
    {
        // case: the build runs git add -f on user-ignored.dat, an ignored path absent from HEAD.
        // mutation: restore that staged path with --worktree, deleting a path HEAD does not contain.
        // red: user-ignored.dat is missing or its bytes differ after the tool or the script row.
        RequireLinux();
        var root = BeginRepo();
        File.AppendAllText(Path.Combine(root, ".gitignore"), "user-ignored.dat\n");
        WriteStageProject(root, "git --git-dir=.git --work-tree=. add -f -- user-ignored.dat");
        var bytes = Encoding.ASCII.GetBytes("IGNORED-KEEP");
        await AssertStagedUserFileSurvivesAsync(root, "ignored", "user-ignored.dat", bytes, cancellationToken);
    }

    [Test]
    [Timeout(420_000)]
    public async Task index_added_file_is_kept_and_unstaged(CancellationToken cancellationToken)
    {
        // case: the build runs git update-index --add on fresh-user.txt, a path absent from HEAD.
        // mutation: restore that index path with --worktree, deleting a path HEAD does not contain.
        // red: fresh-user.txt is missing or its bytes differ after the tool or the script row.
        RequireLinux();
        var root = BeginRepo();
        WriteStageProject(root, "git --git-dir=.git --work-tree=. update-index --add -- fresh-user.txt");
        var bytes = Encoding.ASCII.GetBytes("INDEX-KEEP");
        await AssertStagedUserFileSurvivesAsync(root, "indexed", "fresh-user.txt", bytes, cancellationToken);
    }

    private async Task AssertStagedUserFileSurvivesAsync(string root, string name, string relative, byte[] bytes, CancellationToken cancellationToken)
    {
        Commit(root);
        var full = WriteBytes(root, relative, bytes);
        var head = GitOut(root, "rev-parse", "HEAD").Trim();
        var tool = await RunToolAsync(root, name + "-tool", cancellationToken);
        var toolNote = DescribeKept(tool, full, bytes, root, relative);
        if (!File.Exists(full) || !File.ReadAllBytes(full).AsSpan().SequenceEqual(bytes))
            File.WriteAllBytes(full, bytes);
        var marker = Path.Combine(root, "App", "Marker.cs");
        File.SetLastWriteTimeUtc(marker, DateTime.UtcNow);
        var built = Path.Combine(root, "App", "bin-probe");
        if (Directory.Exists(built))
            Directory.Delete(built, true);
        var script = await RunScriptAsync(root, name + "-script", cancellationToken);
        var scriptNote = DescribeKept(script, full, bytes, root, relative);
        var receipt = Path.Combine(root, ".antiphon", "script-results", name + "-script-fixed", "source.json");
        var validation = File.Exists(receipt)
            ? await ValidateReceiptAsync(receipt, head, cancellationToken)
            : new RunResult(66, "receipt missing\n" + script.Output);
        var report = "tool: " + toolNote + "\nscript: " + scriptNote + "\nreceiptExit=" + validation.Exit + "\n" + validation.Output;
        toolNote.ShouldBe("kept", report);
        scriptNote.ShouldBe("kept", report);
        validation.Exit.ShouldNotBe(0, report);
    }

    private string DescribeKept(RunResult result, string full, byte[] bytes, string root, string relative)
    {
        if (!File.Exists(full))
            return "missing exit=" + result.Exit + "\n" + result.Output;
        if (!File.ReadAllBytes(full).AsSpan().SequenceEqual(bytes))
            return "bytes-changed exit=" + result.Exit + "\n" + result.Output;
        var listed = GitOut(root, "ls-files", "--", relative).Trim();
        if (listed.Length > 0)
            return "still-indexed " + listed + "\n" + result.Output;
        if (result.Exit != 2)
            return "exit=" + result.Exit + "\n" + result.Output;
        if (!result.Output.Contains("refused: tampered", StringComparison.Ordinal))
            return "no-tamper\n" + result.Output;
        if (!result.Output.Contains("TAMPERED " + relative, StringComparison.Ordinal))
            return "no-path\n" + result.Output;
        if (!result.Output.Contains("lossless because the pre-build tracked tree was clean", StringComparison.Ordinal))
            return "no-lossless\n" + result.Output;
        if (result.Output.Contains("buildSource=verified", StringComparison.Ordinal))
            return "verified\n" + result.Output;
        return "kept";
    }

    private void WriteStageProject(string root, string command)
    {
        var xml = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
              </PropertyGroup>
              <Target Name="StageUserPath" BeforeTargets="Build">
                <Exec WorkingDirectory="__ROOT__" Command="__CMD__" />
              </Target>
            </Project>
            """;
        xml = xml.Replace("__ROOT__", XmlAttr(root), StringComparison.Ordinal)
            .Replace("__CMD__", XmlAttr(command), StringComparison.Ordinal);
        Write(root, "App/App.csproj", xml);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Value => 1; }\n");
    }

    private static string XmlAttr(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal);

    private async Task AssertRestoredAsync(string root, string name, Victim victim, CancellationToken cancellationToken)
    {
        Commit(root);
        var head = GitOut(root, "rev-parse", "HEAD").Trim();
        var tool = await RunToolAsync(root, name + "-tool", cancellationToken);
        AssertTamper(tool, victim, head);
        // The tool restore leaves this tracked output newer than its inputs, so the
        // next build can skip the write. Age the file; git does not track mtime.
        File.SetLastWriteTimeUtc(victim.FullPath, DateTime.UnixEpoch);
        var script = await RunScriptAsync(root, name + "-script", cancellationToken);
        AssertTamper(script, victim, head);
        var receipt = Path.Combine(root, ".antiphon", "script-results", name + "-script-fixed", "source.json");
        File.Exists(receipt).ShouldBeTrue(script.Output);
        var validation = await ValidateReceiptAsync(receipt, head, cancellationToken);
        validation.Exit.ShouldNotBe(0, validation.Output);
    }

    private static void AssertTamper(RunResult result, Victim victim, string head)
    {
        File.ReadAllBytes(victim.FullPath).ShouldBe(victim.Bytes, result.Output);
        result.Exit.ShouldBe(2, result.Output);
        result.Output.ShouldContain("refused: tampered", Case.Sensitive);
        result.Output.ShouldContain("TAMPERED " + victim.Relative, Case.Sensitive);
        result.Output.ShouldContain(head, Case.Sensitive);
        result.Output.ShouldNotContain("buildSource=verified", Case.Sensitive);
    }

    private static void AssertDirtyRefused(string output, string keep, byte[] dirty, string marker, byte[] markerBytes)
    {
        File.ReadAllBytes(keep).ShouldBe(dirty, output);
        File.ReadAllBytes(marker).ShouldBe(markerBytes, output);
        output.ShouldContain("refused: dirty-tracked", Case.Sensitive);
        output.ShouldNotContain("Build succeeded", Case.Sensitive);
        output.ShouldNotContain("buildSource=verified", Case.Sensitive);
    }

    private async Task<RunResult> ValidateReceiptAsync(string receipt, string head, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[]
        {
            "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "validate-checkpoint-receipt.ps1"),
            "-Evidence", receipt, "-ExpectedSourceSha", head,
        })
            start.ArgumentList.Add(arg);
        return await WaitAsync(start, cancellationToken);
    }

    private Victim Plant(string root, string relative)
    {
        var bytes = Encoding.ASCII.GetBytes(OriginalText);
        var path = WriteBytes(root, relative, bytes);
        return new Victim(relative.Replace('\\', '/'), path, bytes);
    }

    private string BeginRepo()
    {
        var root = TempDir();
        Git(root, "init", "-b", "card1199");
        Write(root, ".gitignore", "bin-*/\nobj/\n.antiphon/\n.checkpoint-test-root.json\n");
        return root;
    }

    private void Commit(string root)
    {
        Git(root, "add", "-A");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "fixture", "--no-verify");
    }

    private async Task<RunResult> RunToolAsync(string root, string name, CancellationToken cancellationToken) =>
        await RunToolAsync(root, name, "/*/*/Unused/*", "1", cancellationToken);

    private async Task<RunResult> RunToolAsync(string root, string name, string filter, string minExecuted, CancellationToken cancellationToken)
    {
        var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync(
        [
            "row", "--repo-root", root, "--slots", "off",
            "--name", name, "--project", "App/App.csproj",
            "--output-path", OutputPath, "--filter", filter,
            "--results-root", ".antiphon/tool-results", "--min-executed", minExecuted,
        ], new Antiphon.Checkpoints.CheckpointApp.Runtime { Output = output });
        cancellationToken.ThrowIfCancellationRequested();
        return new RunResult(exit, output.ToString());
    }

    private async Task<RunResult> RunScriptAsync(string root, string name, CancellationToken cancellationToken) =>
        await RunScriptAsync(root, name, "/*/*/Unused/*", "1", cancellationToken);

    private async Task<RunResult> RunScriptAsync(string root, string name, string filter, string minExecuted, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["C585_STAMP"] = "fixed";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        foreach (var arg in new[]
        {
            "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(CheckpointFixtures.RepoRoot, "scripts", "run-checkpoint.ps1"),
            "-Name", name, "-Project", "App/App.csproj", "-OutputPath", OutputPath,
            "-Filter", filter, "-ResultsRoot", ".antiphon/script-results",
            "-MinExecuted", minExecuted, "-NoSlot",
        })
            start.ArgumentList.Add(arg);
        return await WaitAsync(start, cancellationToken);
    }

    private async Task<RunResult> RunPwshAsync(string root, string command, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
            start.ArgumentList.Add(arg);
        return await WaitAsync(start, cancellationToken);
    }

    private async Task<RunResult> WaitAsync(ProcessStartInfo start, CancellationToken cancellationToken)
    {
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        RegisterCheckpointChild(process);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new RunResult(process.ExitCode, (await stdoutTask) + (await stderrTask));
    }

    private static string WriteBytes(string root, string relative, byte[] bytes)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void Write(string root, string relative, string content) =>
        WriteBytes(root, relative, Encoding.UTF8.GetBytes(content));

    private static string GitOut(string root, params string[] args)
    {
        var start = GitStart(root, args);
        using var process = Process.Start(start);
        process.ShouldNotBeNull();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000).ShouldBeTrue(stdout + stderr);
        process.ExitCode.ShouldBe(0, stdout + stderr);
        return stdout;
    }

    private static void Git(string root, params string[] args)
    {
        _ = GitOut(root, args);
    }

    private static ProcessStartInfo GitStart(string root, string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment.Remove("GIT_DIR");
        start.Environment.Remove("GIT_WORK_TREE");
        start.Environment.Remove("GIT_INDEX_FILE");
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        return start;
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("CARD-1199 tracked-baseline proof runs on Linux; Windows is unverified.");
    }

    private sealed record Victim(string Relative, string FullPath, byte[] Bytes);

    private sealed record RunResult(int Exit, string Output);
}
