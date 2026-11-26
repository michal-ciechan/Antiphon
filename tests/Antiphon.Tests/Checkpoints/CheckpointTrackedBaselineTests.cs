using System.Diagnostics;
using System.Text;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// Real builds through the row verb (scripts/run-checkpoint.ps1's body since unit 20) must detect changes and preserve
// resulting user bytes and index.
[Category("Unit")]
[Category("Slow")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class CheckpointTrackedBaselineTests : CheckpointTestBase
{
    private const string OutputPath = "bin-probe/";
    private const string OriginalText = "TRACKED-ORIGINAL";
    private CheckpointFixtureGitEnvironment _git = null!;

    [Test]
    [Timeout(300_000)]
    public async Task local_output_property_tampering_is_reported(CancellationToken cancellationToken)
    {
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
        await AssertDetectedAsync(root, "local-output", victim, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task intermediate_output_path_tampering_is_reported(CancellationToken cancellationToken)
    {
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
        await AssertDetectedAsync(root, "intermediate", victim, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task generated_target_file_tampering_is_reported(CancellationToken cancellationToken)
    {
        var root = BeginRepo();
        Write(root, "App/App.csproj", """
            <Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <Target Name="Restore" />
              <Target Name="Build">
                <WriteLinesToFile File="$(MSBuildProjectDirectory)/GeneratedVersion.txt" Lines="generated version 1" Overwrite="true" />
              </Target>
            </Project>
            """);
        Write(root, "App/Marker.cs", "namespace App; public static class Marker { public static int Value => 1; }\n");
        var victim = Plant(root, "App/GeneratedVersion.txt");
        await AssertDetectedAsync(root, "generated", victim, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task imported_props_reference_tampering_is_reported(CancellationToken cancellationToken)
    {
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
        await AssertDetectedAsync(root, "imported-ref", victim, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task transitive_reference_tampering_is_reported(CancellationToken cancellationToken)
    {
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
        await AssertDetectedAsync(root, "transitive", victim, cancellationToken);
    }

    [Test]
    [Timeout(180_000)]
    public async Task dirty_tracked_tree_is_refused_and_unmodified(CancellationToken cancellationToken)
    {
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
    }

    [Test]
    [Timeout(300_000)]
    public async Task clean_untracked_output_build_exits_zero(CancellationToken cancellationToken)
    {
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
        tool.Output.ShouldContain("Build succeeded", Case.Sensitive, "the build's own output reaches the row's console");
    }

    [Test]
    [Timeout(420_000)]
    public async Task fresh_checkout_run_executes_a_test(CancellationToken cancellationToken)
    {
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
        Write(root, "App/FreshProbe.cs", "public sealed class FreshProbe { [TUnit.Core.Test] public void one() { } }");
        Commit(root);
        Directory.Exists(Path.Combine(root, "App", "obj")).ShouldBeFalse("a fresh checkout has no normal obj cache");

        var tool = await RunToolAsync(root, "fresh-tool", "/*/*/FreshProbe/*", "1", cancellationToken);
        tool.Exit.ShouldBe(0, tool.Output);
        tool.Output.ShouldContain("executed=1", Case.Sensitive);
        tool.Output.ShouldContain("buildSource=verified", Case.Sensitive);
        var report = Directory.GetFiles(Path.Combine(root, ".antiphon", "tool-results"), "report.json", SearchOption.AllDirectories).Single();
        (await ValidateReceiptAsync(report, GitOut(root, "rev-parse", "HEAD").Trim())).Exit.ShouldBe(0, "a fresh green row qualifies");
    }

    // The confined build's baseline is the whole tracked set, not the project's folder (BuildConfinement.Prepare).
    [Test]
    [Timeout(120_000)]
    public void tracked_snapshot_contains_committed_files(CancellationToken cancellationToken)
    {
        var root = BeginRepo();
        foreach (var relative in new[] { "App/Marker.cs", "App/keep.txt", "outside.txt", "notes/readme.txt" })
            Write(root, relative, relative + "\n");
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        Commit(root);
        var prepared = Antiphon.Checkpoints.BuildConfinement.Prepare(root, ["build", "App/App.csproj", "--property:OutputPath=" + OutputPath],
            null, cancellationToken);
        prepared.Baseline.ShouldNotBeNull(prepared.Refusal + " " + prepared.Detail);
        foreach (var relative in new[] { "App/App.csproj", "App/Marker.cs", "App/keep.txt", "outside.txt", "notes/readme.txt", ".gitignore" })
            prepared.Baseline!.Entries.Keys.ShouldContain(relative);
    }

    [Test]
    [Timeout(420_000)]
    public async Task staged_untracked_file_is_kept_and_indexed(CancellationToken cancellationToken)
    {
        // case: the build runs git add on user-keep.txt, which is absent from HEAD.
        // mutation: restore that staged path with --worktree, deleting a path HEAD does not contain.
        // red: user-keep.txt is missing or its bytes differ after the tool or the script row.
        var root = BeginRepo();
        WriteStageProject(root, "git --git-dir=.git --work-tree=. add -- user-keep.txt");
        var bytes = Encoding.ASCII.GetBytes("USER-KEEP");
        await AssertStagedUserFileSurvivesAsync(root, "untracked", "user-keep.txt", bytes, cancellationToken);
    }

    [Test]
    [Timeout(420_000)]
    public async Task staged_ignored_file_is_kept_and_indexed(CancellationToken cancellationToken)
    {
        // case: the build runs git add -f on user-ignored.dat, an ignored path absent from HEAD.
        // mutation: restore that staged path with --worktree, deleting a path HEAD does not contain.
        // red: user-ignored.dat is missing or its bytes differ after the tool or the script row.
        var root = BeginRepo();
        File.AppendAllText(Path.Combine(root, ".gitignore"), "user-ignored.dat\n");
        WriteStageProject(root, "git --git-dir=.git --work-tree=. add -f -- user-ignored.dat");
        var bytes = Encoding.ASCII.GetBytes("IGNORED-KEEP");
        await AssertStagedUserFileSurvivesAsync(root, "ignored", "user-ignored.dat", bytes, cancellationToken);
    }

    [Test]
    [Timeout(420_000)]
    public async Task index_added_file_is_kept_and_indexed(CancellationToken cancellationToken)
    {
        // case: the build runs git update-index --add on fresh-user.txt, a path absent from HEAD.
        // mutation: restore that index path with --worktree, deleting a path HEAD does not contain.
        // red: fresh-user.txt is missing or its bytes differ after the tool or the script row.
        var root = BeginRepo();
        WriteStageProject(root, "git --git-dir=.git --work-tree=. update-index --add -- fresh-user.txt");
        var bytes = Encoding.ASCII.GetBytes("INDEX-KEEP");
        await AssertStagedUserFileSurvivesAsync(root, "indexed", "fresh-user.txt", bytes, cancellationToken);
    }

    // The sampled staged-user fixture project texts omit the SDK import.
    [Test]
    [Timeout(120_000)]
    public void baseline_staged_fixture_sdk_project_text_count()
    {
        var untracked = BeginRepo();
        WriteStageProject(untracked, "git --git-dir=.git --work-tree=. add -- user-keep.txt");
        var ignored = BeginRepo();
        WriteStageProject(ignored, "git --git-dir=.git --work-tree=. add -f -- user-ignored.dat");
        var projects = new[]
        {
            File.ReadAllText(Path.Combine(untracked, "App", "App.csproj")),
            File.ReadAllText(Path.Combine(ignored, "App", "App.csproj")),
        };
        projects.Count(text => text.Contains("Microsoft.NET.Sdk", StringComparison.Ordinal)).ShouldBe(0);
        projects.All(text => text.Contains("DefaultTargets=\"Build\"", StringComparison.Ordinal)).ShouldBeTrue();
    }

    private async Task AssertStagedUserFileSurvivesAsync(string root, string name, string relative, byte[] bytes, CancellationToken cancellationToken)
    {
        Commit(root);
        var full = WriteBytes(root, relative, bytes);
        var head = GitOut(root, "rev-parse", "HEAD").Trim();
        var tool = await RunToolAsync(root, name + "-tool", cancellationToken);
        var toolNote = DescribeKept(tool, full, bytes, root, relative);
        var receipt = Directory.GetFiles(Path.Combine(root, ".antiphon", "tool-results"), "report.json", SearchOption.AllDirectories).Single();
        var validation = await ValidateReceiptAsync(receipt, head);
        var report = "row: " + toolNote + "\nreceiptExit=" + validation.Exit + "\n" + validation.Output;
        toolNote.ShouldBe("kept", report);
        validation.Exit.ShouldBe(2, report);
        validation.Output.ShouldContain("CHECKPOINT SOURCE INVALID reason=report_failed");
    }

    private string DescribeKept(RunResult result, string full, byte[] bytes, string root, string relative)
    {
        if (!File.Exists(full))
            return "missing exit=" + result.Exit + "\n" + result.Output;
        if (!File.ReadAllBytes(full).AsSpan().SequenceEqual(bytes))
            return "bytes-changed exit=" + result.Exit + "\n" + result.Output;
        var listed = GitOut(root, "ls-files", "--", relative).Trim();
        if (listed.Length == 0)
            return "index-was-modified " + listed + "\n" + result.Output;
        if (result.Exit != 2)
            return "exit=" + result.Exit + "\n" + result.Output;
        if (!result.Output.Contains("refused: tampered", StringComparison.Ordinal))
            return "no-tamper\n" + result.Output;
        if (!result.Output.Contains("TAMPERED " + relative, StringComparison.Ordinal))
            return "no-path\n" + result.Output;
        if (result.Output.Contains("lossless", StringComparison.Ordinal) || result.Output.Contains("restored", StringComparison.Ordinal))
            return "false-preservation-claim\n" + result.Output;
        if (result.Output.Contains("buildSource=verified", StringComparison.Ordinal))
            return "verified\n" + result.Output;
        return "kept";
    }

    private void WriteStageProject(string root, string command)
    {
        // Staging is the build. The classic target runs the same Exec without the SDK import.
        var xml = """
            <Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <Target Name="Restore" />
              <Target Name="Build">
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


    // The five historical MSBuild routes retain detection/receipt/path assertions.
    // Automatic worktree/index restoration assertions are deliberately retired.
    private async Task AssertDetectedAsync(string root, string name, Victim victim, CancellationToken cancellationToken)
    {
        Commit(root);
        var head = GitOut(root, "rev-parse", "HEAD").Trim();
        var tool = await RunToolAsync(root, name + "-tool", cancellationToken);
        AssertDetected(tool, victim, head);
        var report = Directory.GetFiles(Path.Combine(root, ".antiphon", "tool-results"), "report.json", SearchOption.AllDirectories).Single();
        (await ValidateReceiptAsync(report, head)).Exit.ShouldBe(2);
    }

    private static void AssertDetected(RunResult result, Victim victim, string head)
    {
        result.Exit.ShouldBe(2, result.Output);
        result.Output.ShouldContain("refused: tampered", Case.Sensitive);
        result.Output.ShouldContain("TAMPERED " + victim.Relative, Case.Sensitive);
        result.Output.ShouldContain("manual command:", Case.Sensitive);
        result.Output.ShouldContain(head, Case.Sensitive);
        result.Output.ShouldNotContain("buildSource=verified", Case.Sensitive);
        var bytes = File.ReadAllBytes(victim.FullPath);
        bytes.ShouldNotBe(victim.Bytes, "the tool must leave the build-result bytes in place");
        if (victim.Relative.EndsWith(".dll")) { bytes[0].ShouldBe((byte)'M'); bytes[1].ShouldBe((byte)'Z'); }
        else System.Text.Encoding.UTF8.GetString(bytes).ShouldContain(victim.Relative.EndsWith(".txt") ? "generated version 1" : "AssemblyVersion");
    }

    // case: report.json collides with a directory made during a passing build;
    // mutation: log receipt failure but preserve exit 0; red: missing receipt reports green.
    // The name is the CARD-1199 roster's (C1323 c1199-roster-37, CP-10/CP-20 filters); the row verb is the script's body.
    [Test]
    [Timeout(420_000)]
    public async Task passing_script_requires_writable_source_receipt(CancellationToken cancellationToken)
    {
        var root = BeginRepo();
        Write(root, "App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="TUnit" Version="1.44.0" /></ItemGroup>
              <Target Name="ReceiptCollision" BeforeTargets="Build">
                <MakeDir Directories="$(MSBuildProjectDirectory)/../.antiphon/tool-results/receipt-row-fixed/report.json" />
              </Target>
            </Project>
            """);
        Write(root, "App/Probe.cs", "public sealed class ReceiptProbe { [TUnit.Core.Test] public void one() {} }");
        Commit(root);
        var result = await RunToolAsync(root, "receipt-row", "/*/*/ReceiptProbe/*", "1", cancellationToken, stamp: "fixed");
        result.Exit.ShouldBe(2, result.Output);
        result.Output.ShouldContain("refused: report.json receipt-write-failed", Case.Sensitive);
        result.Output.ShouldContain("CHECKPOINT receipt-row EXIT CODE: 2", Case.Sensitive);
        File.Exists(Path.Combine(root, ".antiphon/tool-results/receipt-row-fixed/report.json")).ShouldBeFalse();
    }

    private static void AssertDirtyRefused(string output, string keep, byte[] dirty, string marker, byte[] markerBytes)
    {
        File.ReadAllBytes(keep).ShouldBe(dirty, output);
        File.ReadAllBytes(marker).ShouldBe(markerBytes, output);
        output.ShouldContain("refused: dirty-tracked", Case.Sensitive);
        output.ShouldNotContain("Build succeeded", Case.Sensitive);
        output.ShouldNotContain("buildSource=verified", Case.Sensitive);
    }

    // The validate verb, validate-checkpoint-receipt.ps1's body since unit 20, in process.
    private static async Task<RunResult> ValidateReceiptAsync(string receipt, string head)
    {
        var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync(["validate", "--evidence", receipt, "--expected-source-sha", head],
            new Antiphon.Checkpoints.CheckpointApp.Runtime { Output = output });
        return new RunResult(exit, output.ToString());
    }
    private Victim Plant(string root, string relative)
    {
        var bytes = Encoding.ASCII.GetBytes(OriginalText);
        var path = WriteBytes(root, relative, bytes);
        return new Victim(relative.Replace('\\', '/'), path, bytes);
    }

    private string BeginRepo()
    {
        var scope = TempDir();
        var root = Path.Combine(scope, "repo");
        Directory.CreateDirectory(root);
        _git = CheckpointFixtureGitEnvironment.Create(Path.Combine(scope, "git-isolation"));
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

    private async Task<RunResult> RunToolAsync(string root, string name, string filter, string minExecuted, CancellationToken cancellationToken,
        string? stamp = null)
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.OurCode, "checkpoint-tool");
        var output = new StringWriter();
        var runtime = _git.ToolRuntime(output);
        var exit = await Antiphon.Checkpoints.Program.RunAsync(
        [
            "row", "--repo-root", root, "--slots", "off",
            "--name", name, "--project", "App/App.csproj",
            "--output-path", OutputPath, "--filter", filter,
            "--results-root", ".antiphon/tool-results", "--min-executed", minExecuted,
        ], stamp is null ? runtime : new Antiphon.Checkpoints.CheckpointApp.Runtime { Output = output, Driver = runtime.Driver, RowStamp = () => stamp });
        cancellationToken.ThrowIfCancellationRequested();
        return new RunResult(exit, output.ToString());
    }

    private static string WriteBytes(string root, string relative, byte[] bytes)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void Write(string root, string relative, string content)
    {
        if (relative.EndsWith(".csproj", StringComparison.Ordinal)) content = content.Replace("<PropertyGroup>", "<PropertyGroup><DefaultItemExcludes>$(DefaultItemExcludes);bin-*/**</DefaultItemExcludes>", StringComparison.Ordinal);
        WriteBytes(root, relative, Encoding.UTF8.GetBytes(content));
    }

    private string GitOut(string root, params string[] args) => _git.Git(root, args);

    private void Git(string root, params string[] args)
    {
        _ = GitOut(root, args);
    }


    private sealed record Victim(string Relative, string FullPath, byte[] Bytes);

    internal sealed record RunResult(int Exit, string Output);
}
