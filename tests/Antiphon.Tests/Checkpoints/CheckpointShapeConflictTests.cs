using System.Diagnostics;
using System.Text;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// One shape rule for both entry points. Restoration may replace a tracked path only
// when the worktree object is the expected regular file or is absent. Every other
// shape stays on disk, exits 2, and must not be called lossless.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointShapeConflictTests : CheckpointTestBase
{
    private const string OutputPath = "bin-probe/";
    private const string TrackedBytes = "TRACKED-ORIGINAL";

    // case: a build moves a pre-existing user directory onto a tracked file and stages its contents.
    // mutation: git restore --worktree of that tracked ancestor deletes the newly unstaged descendants.
    // red: tracked.txt/secret.txt is missing or its bytes differ, or the row says lossless.
    [Test]
    [Arguments("directory-on-file")]
    // case: a build replaces a tracked directory with a pre-existing user file.
    // mutation: restoring the tracked child removes the file now sitting on the directory path.
    // red: the user file is missing or its bytes differ, or the row says lossless.
    [Arguments("file-on-directory")]
    // case: a symlink replaces a tracked file.
    // mutation: restoring the tracked file replaces the symlink.
    // red: tracked.txt is no longer a symlink to link-target.txt, or the row says lossless.
    [Arguments("symlink-on-file")]
    // case: an untracked descendant sits under a path HEAD tracks as a file.
    // mutation: restoring the tracked file deletes the directory and the untracked descendant.
    // red: tracked.txt/user.txt is missing or its bytes differ, or the row says lossless.
    [Arguments("untracked-descendant")]
    // case: an ignored descendant sits under a path HEAD tracks as a file.
    // mutation: restoring the tracked file deletes the directory and the ignored descendant.
    // red: tracked.txt/user.dat is missing or its bytes differ, or the row says lossless.
    [Arguments("ignored-descendant")]
    // case: the build stages a file that is absent from HEAD (round 10).
    // mutation: git restore --worktree of that path deletes bytes HEAD does not contain.
    // red: user-keep.txt is missing, still indexed, or the row omits the lossless sentence.
    [Arguments("staged-untracked")]
    // case: the build modifies a tracked regular file and leaves the path a regular file.
    // mutation: the restore writes something other than the HEAD bytes, or skips the tamper refusal.
    // red: tracked.txt is not TRACKED-ORIGINAL, the exit is not 2, or the lossless sentence is absent.
    [Arguments("modified-tracked")]
    // case: the build deletes a tracked regular file.
    // mutation: the path stays absent, or the restored bytes differ from HEAD.
    // red: tracked.txt is missing or is not TRACKED-ORIGINAL, or the lossless sentence is absent.
    [Arguments("deleted-tracked")]
    // case: a clean build does not touch tracked or pre-existing untracked bytes.
    // mutation: the row restores, refuses, or rewrites a file the build did not need to change.
    // red: the exit is not 0, side.txt or keep.txt changed, or the row says tampered or lossless.
    [Arguments("clean-build")]
    [Timeout(600_000)]
    public async Task shape_conflict_rule(string shape, CancellationToken cancellationToken)
    {
        RequireLinux();
        string? root = null;
        try
        {
            root = BeginRepo(shape);
            PlaceUser(root, shape);
            var tool = await RunToolAsync(root, shape + "-tool", shape, cancellationToken);
            var toolNote = Judge(shape, tool, root, receipt: null);
            ResetWorktree(root);
            PlaceUser(root, shape);
            var script = await RunScriptAsync(root, shape + "-script", shape, cancellationToken);
            RunResult? receipt = null;
            if (shape != "clean-build")
            {
                var receiptPath = Path.Combine(root, ".antiphon", "script-results", shape + "-script-fixed", "source.json");
                receipt = File.Exists(receiptPath)
                    ? await ValidateReceiptAsync(receiptPath, GitOut(root, "rev-parse", "HEAD").Trim(), cancellationToken)
                    : new RunResult(66, "receipt missing\n" + script.Output);
            }

            var scriptNote = Judge(shape, script, root, receipt);
            var report = "tool: " + toolNote + "\n--- tool output ---\n" + tool.Output
                + "\nscript: " + scriptNote + "\n--- script output ---\n" + script.Output
                + (receipt is null ? "" : "\nreceiptExit=" + receipt.Exit + "\n" + receipt.Output);
            toolNote.ShouldBe("ok", report);
            scriptNote.ShouldBe("ok", report);
        }
        finally
        {
            // ContainedCleanup refuses a linked root. The assertion already observed the link.
            if (shape == "symlink-on-file" && root is not null)
            {
                var link = new FileInfo(Path.Combine(root, "tracked.txt"));
                if (link.LinkTarget is not null)
                    link.Delete();
            }
        }
    }

    private static string Judge(string shape, RunResult result, string root, RunResult? receipt)
    {
        var output = result.Output;
        switch (shape)
        {
            case "directory-on-file":
                return FinishConflict(ConflictFile(result, Path.Combine(root, "tracked.txt", "secret.txt"), "USER-SECRET", "tracked.txt", output), receipt);
            case "file-on-directory":
                return FinishConflict(ConflictFile(result, Path.Combine(root, "dir"), "USER-FILE", "dir/a.txt", output), receipt);
            case "symlink-on-file":
            {
                var link = new FileInfo(Path.Combine(root, "tracked.txt"));
                if (link.LinkTarget is null || !link.LinkTarget.Replace('\\', '/').EndsWith("link-target.txt", StringComparison.Ordinal))
                    return "symlink-lost exit=" + result.Exit + " target=" + (link.LinkTarget ?? "<none>");
                var target = Path.Combine(root, "link-target.txt");
                if (!File.Exists(target) || Encoding.ASCII.GetString(File.ReadAllBytes(target)) != "LINK-BYTES")
                    return "link-target-lost";
                return FinishConflict(ConflictText(result, "tracked.txt", output), receipt);
            }
            case "untracked-descendant":
                return FinishConflict(ConflictFile(result, Path.Combine(root, "tracked.txt", "user.txt"), "USER-UNTRACKED", "tracked.txt", output), receipt);
            case "ignored-descendant":
                return FinishConflict(ConflictFile(result, Path.Combine(root, "tracked.txt", "user.dat"), "USER-IGNORED", "tracked.txt", output), receipt);
            case "staged-untracked":
            {
                var full = Path.Combine(root, "user-keep.txt");
                if (!File.Exists(full) || Encoding.ASCII.GetString(File.ReadAllBytes(full)) != "USER-KEEP")
                    return "missing-or-changed exit=" + result.Exit;
                if (GitOut(root, "ls-files", "--", "user-keep.txt").Trim().Length > 0)
                    return "still-indexed";
                if (result.Exit != 2)
                    return "exit=" + result.Exit;
                if (!output.Contains("refused: tampered", StringComparison.Ordinal))
                    return "no-tamper";
                if (!output.Contains("TAMPERED user-keep.txt", StringComparison.Ordinal))
                    return "no-path";
                if (!output.Contains("lossless because the pre-build tracked tree was clean", StringComparison.Ordinal))
                    return "no-lossless";
                if (output.Contains("buildSource=verified", StringComparison.Ordinal))
                    return "verified";
                return Receipt(receipt);
            }
            case "modified-tracked":
            case "deleted-tracked":
            {
                var full = Path.Combine(root, "tracked.txt");
                if (!File.Exists(full) || Encoding.ASCII.GetString(File.ReadAllBytes(full)) != TrackedBytes)
                    return "not-restored exit=" + result.Exit;
                if (result.Exit != 2)
                    return "exit=" + result.Exit;
                if (!output.Contains("refused: tampered", StringComparison.Ordinal))
                    return "no-tamper";
                if (!output.Contains("TAMPERED tracked.txt", StringComparison.Ordinal))
                    return "no-path";
                if (!output.Contains("lossless because the pre-build tracked tree was clean", StringComparison.Ordinal))
                    return "no-lossless";
                if (output.Contains("buildSource=verified", StringComparison.Ordinal))
                    return "verified";
                return Receipt(receipt);
            }
            case "clean-build":
            {
                var side = Path.Combine(root, "side.txt");
                var keep = Path.Combine(root, "keep.txt");
                if (!File.Exists(side) || Encoding.ASCII.GetString(File.ReadAllBytes(side)) != "SIDE-KEEP")
                    return "side-changed";
                if (!File.Exists(keep) || Encoding.ASCII.GetString(File.ReadAllBytes(keep)) != "KEEP")
                    return "keep-changed";
                if (result.Exit != 0)
                    return "exit=" + result.Exit;
                if (output.Contains("refused: tampered", StringComparison.Ordinal)
                    || output.Contains("refused: dirty-tracked", StringComparison.Ordinal)
                    || output.Contains("lossless", StringComparison.Ordinal))
                    return "unexpected-refusal";
                if (!output.Contains("executed=1", StringComparison.Ordinal))
                    return "not-executed";
                return "ok";
            }
            default:
                return "unknown-shape";
        }
    }

    private static string ConflictFile(RunResult result, string path, string expected, string listed, string output)
    {
        if (!File.Exists(path))
            return "missing exit=" + result.Exit;
        if (Encoding.ASCII.GetString(File.ReadAllBytes(path)) != expected)
            return "bytes-changed exit=" + result.Exit;
        return ConflictText(result, listed, output);
    }

    private static string ConflictText(RunResult result, string listed, string output)
    {
        if (result.Exit != 2)
            return "exit=" + result.Exit;
        if (!output.Contains("refused: tampered", StringComparison.Ordinal))
            return "no-tamper";
        if (!output.Contains(listed, StringComparison.Ordinal))
            return "no-path";
        if (!output.Contains("manual restore required", StringComparison.Ordinal))
            return "no-manual";
        if (output.Contains("lossless", StringComparison.Ordinal))
            return "claimed-lossless";
        if (output.Contains("buildSource=verified", StringComparison.Ordinal))
            return "verified";
        return "ok";
    }

    private static string FinishConflict(string note, RunResult? receipt) =>
        note == "ok" ? Receipt(receipt) : note;

    private static string Receipt(RunResult? receipt)
    {
        if (receipt is null)
            return "ok";
        return receipt.Exit == 0 ? "receipt-valid" : "ok";
    }

    private string BeginRepo(string shape)
    {
        var root = TempDir();
        Git(root, "init", "-b", "card1199");
        Git(root, "config", "core.autocrlf", "false");
        var ignore = "bin-*/\nobj/\n.antiphon/\n.checkpoint-test-root.json\n";
        if (shape == "ignored-descendant")
            ignore += "user.dat\n";
        Write(root, ".gitignore", ignore);
        if (shape == "clean-build")
        {
            Write(root, "App/App.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <ImplicitUsings>disable</ImplicitUsings>
                    <Nullable>disable</Nullable>
                    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="TUnit" Version="1.44.0" />
                  </ItemGroup>
                </Project>
                """);
            Write(root, "App/ShapeCleanProbe.cs", """
                public sealed class ShapeCleanProbe
                {
                    [TUnit.Core.Test]
                    public void one() { }
                }
                """);
            Write(root, "keep.txt", "KEEP");
        }
        else if (shape == "file-on-directory")
        {
            WriteProject(root, "rm -rf -- dir && mv -- payload.dat dir && git --git-dir=.git --work-tree=. add -A -- dir");
            Write(root, "dir/a.txt", TrackedBytes);
        }
        else
        {
            var command = shape switch
            {
                "directory-on-file" => "rm -f -- tracked.txt && mv -- user-dir tracked.txt && git --git-dir=.git --work-tree=. add -A -- tracked.txt",
                "symlink-on-file" => "ln -sfn -- link-target.txt tracked.txt && git --git-dir=.git --work-tree=. add -- tracked.txt",
                "untracked-descendant" => "rm -f -- tracked.txt && mv -- user-dir tracked.txt",
                "ignored-descendant" => "rm -f -- tracked.txt && mv -- user-dir tracked.txt",
                "staged-untracked" => "git --git-dir=.git --work-tree=. add -- user-keep.txt",
                "modified-tracked" => "printf '%s' MUTATED > tracked.txt",
                "deleted-tracked" => "rm -f -- tracked.txt",
                _ => throw new InvalidOperationException(shape),
            };
            WriteProject(root, command);
            Write(root, "tracked.txt", TrackedBytes);
        }

        Commit(root);
        return root;
    }

    private static void PlaceUser(string root, string shape)
    {
        switch (shape)
        {
            case "directory-on-file":
                Write(root, "user-dir/secret.txt", "USER-SECRET");
                break;
            case "file-on-directory":
                Write(root, "payload.dat", "USER-FILE");
                break;
            case "symlink-on-file":
                Write(root, "link-target.txt", "LINK-BYTES");
                break;
            case "untracked-descendant":
                Write(root, "user-dir/user.txt", "USER-UNTRACKED");
                break;
            case "ignored-descendant":
                Write(root, "user-dir/user.dat", "USER-IGNORED");
                break;
            case "staged-untracked":
                Write(root, "user-keep.txt", "USER-KEEP");
                break;
            case "clean-build":
                Write(root, "side.txt", "SIDE-KEEP");
                break;
        }
    }

    private void WriteProject(string root, string command)
    {
        var xml = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <ImplicitUsings>disable</ImplicitUsings>
                <Nullable>disable</Nullable>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Marker.cs" />
              </ItemGroup>
              <Target Name="MutateShape" BeforeTargets="Build">
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

    private void ResetWorktree(string root)
    {
        Git(root, "reset", "--hard", "HEAD");
        Git(root, "clean", "-fdx", "-e", ".antiphon");
    }

    private void Commit(string root)
    {
        Git(root, "add", "-A");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "fixture", "--no-verify");
    }

    private async Task<RunResult> RunToolAsync(string root, string name, string shape, CancellationToken cancellationToken)
    {
        var filter = shape == "clean-build" ? "/*/*/ShapeCleanProbe/*" : "/*/*/Unused/*";
        var min = shape == "clean-build" ? "1" : "1";
        var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync(
        [
            "row", "--repo-root", root, "--slots", "off",
            "--name", name, "--project", "App/App.csproj",
            "--output-path", OutputPath, "--filter", filter,
            "--results-root", ".antiphon/tool-results", "--min-executed", min,
        ], new Antiphon.Checkpoints.CheckpointApp.Runtime { Output = output });
        cancellationToken.ThrowIfCancellationRequested();
        return new RunResult(exit, output.ToString());
    }

    private async Task<RunResult> RunScriptAsync(string root, string name, string shape, CancellationToken cancellationToken)
    {
        var filter = shape == "clean-build" ? "/*/*/ShapeCleanProbe/*" : "/*/*/Unused/*";
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
            "-MinExecuted", "1", "-NoSlot",
        })
            start.ArgumentList.Add(arg);
        return await WaitAsync(start, cancellationToken);
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

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(content));
    }

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

    private static void Git(string root, params string[] args) => _ = GitOut(root, args);

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

    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure.");
    }

    private sealed record RunResult(int Exit, string Output);
}
