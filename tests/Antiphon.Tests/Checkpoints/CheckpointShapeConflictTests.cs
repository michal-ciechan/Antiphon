using System.Diagnostics;
using System.Text;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// Detect-and-report only. The tool must not restore, unstage, delete, or overwrite
// anything outside the output directory it creates for the run. A build that changes
// tracked state exits 2 with refused: tampered, an INVALID receipt, the changed
// paths, and a manual git command. A pre-existing stamp, compiler output, output
// directory, or output symlink is refused before the build. Neither entry point
// may print "lossless" or "restored".
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointShapeConflictTests : CheckpointTestBase
{
    private const string OutputPath = "bin-probe/";
    private const string Honest = "nothing was modified by the tool";

    // case: a build moves a user directory with one staged and one unstaged child onto a tracked file.
    // mutation: git restore --worktree of that tracked ancestor deletes both children.
    // red: tracked.txt/secret.txt or tracked.txt/note.txt is missing or differs, or the row says lossless or restored.
    [Test]
    [Arguments("directory-on-file")]
    // case: a build replaces a tracked directory with a pre-existing user file.
    // mutation: restoring the tracked child removes the file now sitting on the directory path.
    // red: the user file is missing or its bytes differ, or the row says lossless or restored.
    [Arguments("file-on-directory")]
    // case: a symlink replaces a tracked file.
    // mutation: restoring the tracked file replaces the symlink.
    // red: tracked.txt is no longer a symlink to link-target.txt, or the row says lossless or restored.
    [Arguments("symlink-on-file")]
    // case: an untracked descendant sits under a path HEAD tracks as a file.
    // mutation: restoring the tracked file deletes the directory and the untracked descendant.
    // red: tracked.txt/user.txt is missing or its bytes differ, or the row says lossless or restored.
    [Arguments("untracked-descendant")]
    // case: an ignored descendant sits under a path HEAD tracks as a file.
    // mutation: restoring the tracked file deletes the directory and the ignored descendant.
    // red: tracked.txt/user.dat is missing or its bytes differ, or the row says lossless or restored.
    [Arguments("ignored-descendant")]
    // case: the build stages a file that is absent from HEAD.
    // mutation: git restore --staged unstages it, and the row claims the tree is lossless.
    // red: user-keep.txt is missing, no longer indexed, or the row says lossless or restored.
    [Arguments("staged-untracked")]
    // case: the build modifies a tracked regular file and leaves the path a regular file.
    // mutation: git restore writes HEAD bytes back over the build's edit and claims lossless.
    // red: tracked.txt is TRACKED-ORIGINAL again, or the row says lossless or restored.
    [Arguments("modified-tracked")]
    // case: the build deletes a tracked regular file.
    // mutation: git restore recreates TRACKED-ORIGINAL and claims lossless.
    // red: tracked.txt exists again, or the row says lossless or restored.
    [Arguments("deleted-tracked")]
    // case: a clean build does not touch tracked or pre-existing untracked bytes.
    // mutation: the row restores, refuses, or rewrites a file the build did not need to change, or omits the receipt.
    // red: the exit is not 0, side.txt or keep.txt changed, the row says tampered, lossless, or restored, or the receipt is missing.
    [Arguments("clean-build")]
    // case: a user file already occupies checkpoint-build-source.json.
    // mutation: the row deletes that file before the baseline and then claims lossless.
    // red: the stamp bytes differ, or the row does not refuse stamp-exists.
    [Arguments("stamp-exists")]
    // case: a user file already occupies the compiler output App.dll.
    // mutation: the build overwrites those bytes.
    // red: App.dll bytes differ, or the row does not refuse compiler-output-exists.
    [Arguments("compiler-output-exists")]
    // case: the output directory already exists and is empty.
    // mutation: the row builds inside it instead of refusing the existing directory.
    // red: the directory gains a file, or the row does not refuse output-exists.
    [Arguments("output-exists")]
    // case: the output path is a symlink to a directory outside the repository.
    // mutation: the build follows the link and writes into the outside directory.
    // red: the outside sentinel changes or gains App.dll, or the row does not refuse output-symlink.
    [Arguments("output-symlink")]
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
            var toolReceipt = await ReadReceiptAsync(root, "tool-results", shape + "-tool", cancellationToken);
            var toolNote = Judge(shape, tool, root, toolReceipt);
            ResetWorktree(root);
            PlaceUser(root, shape);
            var script = await RunScriptAsync(root, shape + "-script", shape, cancellationToken);
            var scriptReceipt = await ReadReceiptAsync(root, "script-results", shape + "-script-fixed", cancellationToken);
            var scriptNote = Judge(shape, script, root, scriptReceipt);
            var report = "tool: " + toolNote + "\n--- tool output ---\n" + tool.Output
                + "\nscript: " + scriptNote + "\n--- script output ---\n" + script.Output
                + "\ntoolReceipt=" + FormatReceipt(toolReceipt)
                + "\nscriptReceipt=" + FormatReceipt(scriptReceipt);
            toolNote.ShouldBe("ok", report);
            scriptNote.ShouldBe("ok", report);
        }
        finally
        {
            if (shape == "symlink-on-file" && root is not null)
            {
                var link = new FileInfo(Path.Combine(root, "tracked.txt"));
                if (link.LinkTarget is not null)
                    link.Delete();
            }

            if (shape == "output-symlink" && root is not null)
            {
                var link = new DirectoryInfo(Path.Combine(root, "App", "bin-probe"));
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
                return Finish(ConflictFile(result, Path.Combine(root, "tracked.txt", "secret.txt"), "USER-SECRET", "tracked.txt")
                    ?? ConflictFile(result, Path.Combine(root, "tracked.txt", "note.txt"), "USER-NOTE", "tracked.txt")
                    ?? (Indexed(root, "tracked.txt/secret.txt") ? null : "secret-unstaged")
                    ?? Tamper(result, "tracked.txt", output)
                    ?? Receipt(receipt, invalid: true));
            case "file-on-directory":
                return Finish(ConflictFile(result, Path.Combine(root, "dir"), "USER-FILE", "dir")
                    ?? Tamper(result, "dir", output)
                    ?? Receipt(receipt, invalid: true));
            case "symlink-on-file":
            {
                var link = new FileInfo(Path.Combine(root, "tracked.txt"));
                if (link.LinkTarget is null || !link.LinkTarget.Replace('\\', '/').EndsWith("link-target.txt", StringComparison.Ordinal))
                    return "symlink-lost exit=" + result.Exit + " target=" + (link.LinkTarget ?? "<none>");
                var target = Path.Combine(root, "link-target.txt");
                if (!File.Exists(target) || Encoding.ASCII.GetString(File.ReadAllBytes(target)) != "LINK-BYTES")
                    return "link-target-lost";
                return Finish(Tamper(result, "tracked.txt", output) ?? Receipt(receipt, invalid: true));
            }
            case "untracked-descendant":
                return Finish(ConflictFile(result, Path.Combine(root, "tracked.txt", "user.txt"), "USER-UNTRACKED", "tracked.txt")
                    ?? Tamper(result, "tracked.txt", output)
                    ?? Receipt(receipt, invalid: true));
            case "ignored-descendant":
                return Finish(ConflictFile(result, Path.Combine(root, "tracked.txt", "user.dat"), "USER-IGNORED", "tracked.txt")
                    ?? Tamper(result, "tracked.txt", output)
                    ?? Receipt(receipt, invalid: true));
            case "staged-untracked":
            {
                var full = Path.Combine(root, "user-keep.txt");
                if (!File.Exists(full) || Encoding.ASCII.GetString(File.ReadAllBytes(full)) != "USER-KEEP")
                    return "missing-or-changed exit=" + result.Exit;
                if (!Indexed(root, "user-keep.txt"))
                    return "unstaged exit=" + result.Exit;
                return Finish(Tamper(result, "user-keep.txt", output) ?? Receipt(receipt, invalid: true));
            }
            case "modified-tracked":
            {
                var full = Path.Combine(root, "tracked.txt");
                if (!File.Exists(full) || Encoding.ASCII.GetString(File.ReadAllBytes(full)) != "MUTATED")
                    return "tool-rewrote-tracked exit=" + result.Exit;
                return Finish(Tamper(result, "tracked.txt", output) ?? Receipt(receipt, invalid: true));
            }
            case "deleted-tracked":
            {
                if (File.Exists(Path.Combine(root, "tracked.txt")))
                    return "tool-restored-tracked exit=" + result.Exit;
                return Finish(Tamper(result, "tracked.txt", output) ?? Receipt(receipt, invalid: true));
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
                    || output.Contains("refused: dirty-tracked", StringComparison.Ordinal))
                    return "unexpected-refusal";
                if (!output.Contains("executed=1", StringComparison.Ordinal))
                    return "not-executed";
                var wording = Wording(output);
                if (wording is not null)
                    return wording;
                return Receipt(receipt, invalid: false) ?? "ok";
            }
            case "stamp-exists":
                return Finish(Preexisting(result, Path.Combine(root, "App", "bin-probe", "checkpoint-build-source.json"), "USER-STAMP", "stamp-exists", output)
                    ?? Receipt(receipt, invalid: true));
            case "compiler-output-exists":
                return Finish(Preexisting(result, Path.Combine(root, "App", "bin-probe", "App.dll"), "USER-DLL", "compiler-output-exists", output)
                    ?? Receipt(receipt, invalid: true));
            case "output-exists":
            {
                var dir = Path.Combine(root, "App", "bin-probe");
                if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any())
                    return "output-dir-changed exit=" + result.Exit;
                return Finish(Named(result, "output-exists", output) ?? Receipt(receipt, invalid: true));
            }
            case "output-symlink":
            {
                var link = new DirectoryInfo(Path.Combine(root, "App", "bin-probe"));
                if (link.LinkTarget is null)
                    return "symlink-replaced exit=" + result.Exit;
                var sentinel = Path.Combine(Path.GetTempPath(), "c1199-" + Path.GetFileName(root), "sentinel.txt");
                if (!File.Exists(sentinel) || Encoding.ASCII.GetString(File.ReadAllBytes(sentinel)) != "OUTSIDE")
                    return "outside-changed";
                if (File.Exists(Path.Combine(Path.GetDirectoryName(sentinel)!, "App.dll")))
                    return "outside-written";
                return Finish(Named(result, "output-symlink", output) ?? Receipt(receipt, invalid: true));
            }
            default:
                return "unknown-shape";
        }
    }

    private static string? Wording(string output)
    {
        if (output.Contains("lossless", StringComparison.Ordinal))
            return "claimed-lossless";
        if (output.Contains("restored", StringComparison.Ordinal))
            return "claimed-restored";
        return null;
    }

    private static string? ConflictFile(RunResult result, string path, string expected, string listed)
    {
        if (!File.Exists(path))
            return "missing " + listed + " exit=" + result.Exit;
        if (Encoding.ASCII.GetString(File.ReadAllBytes(path)) != expected)
            return "bytes-changed " + listed + " exit=" + result.Exit;
        return null;
    }

    private static string? Tamper(RunResult result, string listed, string output)
    {
        if (result.Exit != 2)
            return "exit=" + result.Exit;
        if (!output.Contains("refused: tampered", StringComparison.Ordinal))
            return "no-tamper";
        if (!output.Contains("TAMPERED " + listed, StringComparison.Ordinal))
            return "no-path";
        if (!output.Contains(Honest, StringComparison.Ordinal))
            return "no-honest";
        if (!output.Contains("git -C ", StringComparison.Ordinal) || !output.Contains("restore --", StringComparison.Ordinal))
            return "no-command";
        var wording = Wording(output);
        if (wording is not null)
            return wording;
        if (output.Contains("buildSource=verified", StringComparison.Ordinal))
            return "verified";
        return null;
    }

    private static string? Named(RunResult result, string reason, string output)
    {
        if (result.Exit != 2)
            return "exit=" + result.Exit;
        if (!output.Contains("refused: " + reason, StringComparison.Ordinal))
            return "no-reason";
        if (!output.Contains(Honest, StringComparison.Ordinal))
            return "no-honest";
        var wording = Wording(output);
        if (wording is not null)
            return wording;
        if (output.Contains("buildSource=verified", StringComparison.Ordinal))
            return "verified";
        return null;
    }

    private static string? Preexisting(RunResult result, string path, string expected, string reason, string output)
    {
        if (!File.Exists(path))
            return "missing exit=" + result.Exit;
        if (Encoding.ASCII.GetString(File.ReadAllBytes(path)) != expected)
            return "bytes-changed exit=" + result.Exit;
        return Named(result, reason, output);
    }

    private static string Finish(string? note) => note ?? "ok";

    private static string? Receipt(RunResult? receipt, bool invalid)
    {
        if (receipt is null)
            return "receipt-missing";
        if (invalid)
        {
            if (receipt.Exit == 0 || receipt.Output.Contains("CHECKPOINT SOURCE VALID", StringComparison.Ordinal))
                return "receipt-valid";
            if (!receipt.Output.Contains("CHECKPOINT SOURCE INVALID", StringComparison.Ordinal))
                return "receipt-not-invalid";
            return null;
        }

        if (receipt.Exit != 0 || !receipt.Output.Contains("CHECKPOINT SOURCE VALID", StringComparison.Ordinal))
            return "receipt-not-valid exit=" + receipt.Exit;
        return null;
    }

    private static string FormatReceipt(RunResult? receipt) =>
        receipt is null ? "missing" : "exit=" + receipt.Exit + " " + receipt.Output;

    private static bool Indexed(string root, string relative) =>
        GitOut(root, "ls-files", "--", relative).Trim().Length > 0;

    private string BeginRepo(string shape)
    {
        var root = TempDir();
        Git(root, "init", "-b", "card1199");
        Git(root, "config", "core.autocrlf", "false");
        var ignore = "bin-*/\nobj/\n.antiphon/\n.checkpoint-test-root.json\n";
        if (shape == "ignored-descendant")
            ignore += "user.dat\n";
        Write(root, ".gitignore", ignore);
        if (shape is "clean-build" or "stamp-exists" or "compiler-output-exists" or "output-exists" or "output-symlink")
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
            if (shape == "clean-build")
            {
                Write(root, "keep.txt", "KEEP");
                Write(root, "side.txt", "SIDE-KEEP");
            }
        }
        else if (shape == "file-on-directory")
        {
            WriteProject(root, "rm -rf -- dir && mv -- payload.dat dir && git --git-dir=.git --work-tree=. add -A -- dir");
            Write(root, "dir/a.txt", "TRACKED-ORIGINAL");
        }
        else
        {
            var command = shape switch
            {
                "directory-on-file" => "rm -f -- tracked.txt && mv -- user-dir tracked.txt && git --git-dir=.git --work-tree=. add -- tracked.txt/secret.txt",
                "symlink-on-file" => "ln -sfn -- link-target.txt tracked.txt && git --git-dir=.git --work-tree=. add -- tracked.txt",
                "untracked-descendant" => "rm -f -- tracked.txt && mv -- user-dir tracked.txt",
                "ignored-descendant" => "rm -f -- tracked.txt && mv -- user-dir tracked.txt",
                "staged-untracked" => "git --git-dir=.git --work-tree=. add -- user-keep.txt",
                "modified-tracked" => "printf '%s' MUTATED > tracked.txt",
                "deleted-tracked" => "rm -f -- tracked.txt",
                _ => throw new InvalidOperationException(shape),
            };
            WriteProject(root, command);
            Write(root, "tracked.txt", "TRACKED-ORIGINAL");
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
                Write(root, "user-dir/note.txt", "USER-NOTE");
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
            case "stamp-exists":
                Write(root, "App/bin-probe/checkpoint-build-source.json", "USER-STAMP");
                break;
            case "compiler-output-exists":
                Write(root, "App/bin-probe/App.dll", "USER-DLL");
                break;
            case "output-exists":
                Directory.CreateDirectory(Path.Combine(root, "App", "bin-probe"));
                break;
            case "output-symlink":
            {
                var outside = Path.Combine(Path.GetTempPath(), "c1199-" + Path.GetFileName(root));
                Directory.CreateDirectory(outside);
                File.WriteAllBytes(Path.Combine(outside, "sentinel.txt"), Encoding.ASCII.GetBytes("OUTSIDE"));
                Directory.CreateSymbolicLink(Path.Combine(root, "App", "bin-probe"), outside);
                break;
            }
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
        var outside = Path.Combine(Path.GetTempPath(), "c1199-" + Path.GetFileName(root));
        if (Directory.Exists(outside))
            Directory.Delete(outside, recursive: true);
    }

    private void Commit(string root)
    {
        Git(root, "add", "-A");
        Git(root, "-c", "user.email=card-1199@example.invalid", "-c", "user.name=card-1199",
            "-c", "commit.gpgsign=false", "commit", "-m", "fixture", "--no-verify");
    }

    private async Task<RunResult> RunToolAsync(string root, string name, string shape, CancellationToken cancellationToken)
    {
        var filter = shape is "clean-build" or "stamp-exists" or "compiler-output-exists" or "output-exists" or "output-symlink"
            ? "/*/*/ShapeCleanProbe/*"
            : "/*/*/Unused/*";
        var output = new StringWriter();
        var exit = await Antiphon.Checkpoints.Program.RunAsync(
        [
            "row", "--repo-root", root, "--slots", "off",
            "--name", name, "--project", "App/App.csproj",
            "--output-path", OutputPath, "--filter", filter,
            "--results-root", ".antiphon/tool-results", "--min-executed", "1",
        ], new Antiphon.Checkpoints.CheckpointApp.Runtime { Output = output });
        cancellationToken.ThrowIfCancellationRequested();
        return new RunResult(exit, output.ToString());
    }

    private async Task<RunResult> RunScriptAsync(string root, string name, string shape, CancellationToken cancellationToken)
    {
        var filter = shape is "clean-build" or "stamp-exists" or "compiler-output-exists" or "output-exists" or "output-symlink"
            ? "/*/*/ShapeCleanProbe/*"
            : "/*/*/Unused/*";
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

    private async Task<RunResult?> ReadReceiptAsync(string root, string results, string name, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(root, ".antiphon", results);
        if (!Directory.Exists(dir))
            return null;
        string? receipt = null;
        foreach (var candidate in Directory.EnumerateDirectories(dir))
        {
            if (!Path.GetFileName(candidate).StartsWith(name, StringComparison.Ordinal))
                continue;
            var path = Path.Combine(candidate, "source.json");
            if (File.Exists(path))
                receipt = path;
        }

        var direct = Path.Combine(dir, name, "source.json");
        if (File.Exists(direct))
            receipt = direct;
        if (receipt is null)
            return null;
        return await ValidateReceiptAsync(receipt, GitOut(root, "rev-parse", "HEAD").Trim(), cancellationToken);
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
