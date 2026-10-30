using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

// Detect only: the build's resulting bytes and index must survive both entry points.
// Each row guards against automatic restore/delete or a false preservation claim.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointShapeConflictTests : CheckpointTestBase
{
    private const string OutputPath = "bin-probe/";
    private const string TrackedBytes = "TRACKED-ORIGINAL";

    [Test]
    [Timeout(240_000)]
    public Task shape_directory_on_file(CancellationToken cancellationToken)
    {
        RequireLinux();
        return AssertShapeAsync("directory-on-file", cancellationToken);
    }

    [Test]
    [Timeout(240_000)]
    public Task shape_file_on_directory(CancellationToken cancellationToken)
    {
        RequireLinux();
        return AssertShapeAsync("file-on-directory", cancellationToken);
    }

    [Test]
    [Timeout(240_000)]
    public Task shape_symlink_on_file(CancellationToken cancellationToken)
    {
        RequireLinux();
        return AssertShapeAsync("symlink-on-file", cancellationToken);
    }

    [Test]
    [Timeout(120_000)]
    public Task shape_untracked_descendant(CancellationToken cancellationToken)
    {
        RequireLinux();
        return AssertShapeAsync("untracked-descendant", cancellationToken);
    }

    [Test]
    [Timeout(120_000)]
    public Task shape_ignored_descendant(CancellationToken cancellationToken)
    {
        RequireLinux();
        return AssertShapeAsync("ignored-descendant", cancellationToken);
    }

    [Test]
    [Timeout(120_000)]
    public Task shape_staged_untracked(CancellationToken cancellationToken) =>
        AssertShapeAsync("staged-untracked", cancellationToken);

    [Test]
    [Timeout(120_000)]
    public Task shape_modified_tracked(CancellationToken cancellationToken)
    {
        RequireLinux();
        return AssertShapeAsync("modified-tracked", cancellationToken);
    }

    [Test]
    [Timeout(240_000)]
    public Task shape_deleted_tracked(CancellationToken cancellationToken)
    {
        RequireLinux();
        return AssertShapeAsync("deleted-tracked", cancellationToken);
    }

    [Test]
    [Timeout(240_000)]
    public Task shape_clean_build(CancellationToken cancellationToken) =>
        AssertShapeAsync("clean-build", cancellationToken);

    [Test]
    [Timeout(120_000)]
    public Task shape_regular_user_move(CancellationToken cancellationToken)
    {
        RequireLinux();
        return AssertShapeAsync("regular-user-move", cancellationToken);
    }

    [Test]
    [Timeout(120_000)]
    public Task shape_deleted_untracked(CancellationToken cancellationToken)
    {
        RequireLinux();
        return AssertShapeAsync("deleted-untracked", cancellationToken);
    }

    [Test]
    [Timeout(120_000)]
    public Task shape_changed_untracked(CancellationToken cancellationToken)
    {
        RequireLinux();
        return AssertShapeAsync("changed-untracked", cancellationToken);
    }

    [Test]
    [Timeout(120_000)]
    public Task shape_existing_stamp(CancellationToken cancellationToken) =>
        AssertShapeAsync("existing-stamp", cancellationToken);

    [Test]
    [Timeout(120_000)]
    public Task shape_compiler_collision(CancellationToken cancellationToken) =>
        AssertShapeAsync("compiler-collision", cancellationToken);

    [Test]
    [Timeout(120_000)]
    public Task shape_existing_output_directory(CancellationToken cancellationToken) =>
        AssertShapeAsync("existing-output-directory", cancellationToken);

    [Test]
    [Timeout(120_000)]
    public Task shape_existing_output_symlink(CancellationToken cancellationToken) =>
        AssertShapeAsync("existing-output-symlink", cancellationToken);

    private async Task AssertShapeAsync(string shape, CancellationToken cancellationToken)
    {
        string? root = null;
        try
        {
            root = BeginRepo(shape);
            PlaceUser(root, shape);
            var tool = await RunToolAsync(root, shape + "-tool", shape, cancellationToken);
            var toolReceipt = ReadReceipt(Path.Combine(root, ".antiphon/tool-results"), shape + "-tool", tool.Exit, GitOut(root, "rev-parse", "HEAD").Trim());
            var toolNote = Judge(shape, tool, root, toolReceipt);
            if (shape == "symlink-on-file") new FileInfo(Path.Combine(root, "tracked.txt")).Delete();
            if (shape == "existing-output-symlink") new DirectoryInfo(Path.Combine(root, "App/bin-probe")).Delete();
            root = BeginRepo(shape);
            PlaceUser(root, shape);
            var script = await RunScriptAsync(root, shape + "-script", shape, cancellationToken);
            RunResult? receipt = null;
            if (shape != "clean-build")
            {
                var receiptPath = Path.Combine(root, ".antiphon", "script-results", shape + "-script-fixed", "source.json");
                receipt = ReadReceipt(Path.GetDirectoryName(receiptPath)!, shape + "-script", script.Exit, GitOut(root, "rev-parse", "HEAD").Trim());
                if (receipt.Exit == 2)
                {
                    var validation = await ValidateReceiptAsync(receiptPath, GitOut(root, "rev-parse", "HEAD").Trim(), cancellationToken);
                    validation.Exit.ShouldBe(2, validation.Output);
                    validation.Output.ShouldContain("CHECKPOINT SOURCE INVALID reason=source_ineligible");
                }
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
            if (shape == "existing-output-symlink" && root is not null)
                new DirectoryInfo(Path.Combine(root, "App/bin-probe")).Delete();
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
                if (File.ReadAllText(Path.Combine(root, "tracked.txt/unstaged.txt")) != "USER-UNSTAGED") return "unstaged-bytes-lost";
                if (GitOut(root, "ls-files", "--", "tracked.txt/secret.txt").Trim().Length == 0) return "staged-index-changed";
                if (GitOut(root, "ls-files", "--", "tracked.txt/unstaged.txt").Trim().Length != 0) return "unstaged-index-changed";
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
                if (GitOut(root, "ls-files", "--", "user-keep.txt").Trim().Length == 0)
                    return "index-was-modified";
                if (result.Exit != 2)
                    return "exit=" + result.Exit;
                if (!output.Contains("refused: tampered", StringComparison.Ordinal))
                    return "no-tamper";
                if (!output.Contains("TAMPERED user-keep.txt", StringComparison.Ordinal))
                    return "no-path";
                if (output.Contains("lossless", StringComparison.Ordinal) || output.Contains("restored", StringComparison.Ordinal))
                    return "false-preservation-claim";
                if (output.Contains("buildSource=verified", StringComparison.Ordinal))
                    return "verified";
                return Receipt(receipt);
            }
            case "modified-tracked":
            case "deleted-tracked":
            {
                var full = Path.Combine(root, "tracked.txt");
                if (shape == "modified-tracked" && (!File.Exists(full) || File.ReadAllText(full) != "MUTATED"))
                    return "tool-rewrote-build-bytes exit=" + result.Exit;
                if (shape == "deleted-tracked" && File.Exists(full))
                    return "tool-recreated-deleted-file";
                if (result.Exit != 2)
                    return "exit=" + result.Exit;
                if (!output.Contains("refused: tampered", StringComparison.Ordinal))
                    return "no-tamper";
                if (!output.Contains("TAMPERED tracked.txt", StringComparison.Ordinal))
                    return "no-path";
                if (output.Contains("lossless", StringComparison.Ordinal) || output.Contains("restored", StringComparison.Ordinal))
                    return "false-preservation-claim";
                if (output.Contains("buildSource=verified", StringComparison.Ordinal))
                    return "verified";
                return Receipt(receipt);
            }
            case "deleted-untracked":
            case "changed-untracked":
                if (shape == "deleted-untracked" && File.Exists(Path.Combine(root, "user-move.txt"))) return "tool-recreated-user-file";
                if (shape == "changed-untracked" && File.ReadAllText(Path.Combine(root, "user-move.txt")) != "CHANGED-USER") return "tool-rewrote-user-file";
                return FinishConflict(ConflictText(result, "user-move.txt", output), receipt);
            case "regular-user-move":
                return FinishConflict(ConflictFile(result, Path.Combine(root, "tracked.txt"), "USER-MOVED", "tracked.txt", output), receipt);
            case "existing-stamp":
            case "compiler-collision":
            case "existing-output-directory":
            case "existing-output-symlink":
            {
                var file = shape == "existing-stamp" ? "checkpoint-build-source.json" : "App.dll";
                if (shape != "existing-output-directory" && (!File.Exists(Path.Combine(root, "App", "bin-probe", file)) || File.ReadAllText(Path.Combine(root, "App", "bin-probe", file)) != "USER-OUTPUT"))
                    return "pre-existing-user-output-lost";
                if (File.ReadAllText(Path.Combine(root, "tracked.txt")) != TrackedBytes) return "build-changed-source-on-collision";
                var reason = shape == "existing-stamp" ? "pre-existing-build-source" : shape == "existing-output-symlink" ? "reparse-point" : "pre-existing-output";
                var namedRefusal = output.Contains("refused: " + reason, StringComparison.Ordinal)
                    || shape == "existing-output-symlink" && output.Contains("refused: link-component", StringComparison.Ordinal);
                if (result.Exit != 2 || !namedRefusal) return "collision-not-refused-with-reason " + reason;
                if (File.Exists(Path.Combine(root, ".antiphon", "c1199-mutation-reached.txt"))) return "mutation-marker-present";
                if (shape == "existing-output-symlink" && new DirectoryInfo(Path.Combine(root, "App/bin-probe")).LinkTarget is null) return "output-link-changed";
                if (output.Contains("Build succeeded", StringComparison.Ordinal)) return "build-ran-on-collision";
                if (output.Contains("lossless", StringComparison.Ordinal) || output.Contains("restored", StringComparison.Ordinal)) return "false-preservation-claim";
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
        if (output.Contains("lossless", StringComparison.Ordinal) || output.Contains("restored", StringComparison.Ordinal))
            return "claimed-lossless";
        if (output.Contains("buildSource=verified", StringComparison.Ordinal))
            return "verified";
        return "ok";
    }

    private static string FinishConflict(string note, RunResult? receipt) =>
        note == "ok" ? Receipt(receipt) : note;

    // F3 control: removing the emitted receipt must not qualify a negative validation.
    [Test]
    public void missing_receipt_is_not_negative_success() { Receipt(null).ShouldNotBe("ok"); }

    private static string Receipt(RunResult? receipt)
    {
        if (receipt is null) return "receipt-missing";
        return receipt.Exit == 2 && receipt.Output == "source_ineligible" ? "ok" : "receipt-invalid: " + receipt.Output;
    }

    private static RunResult ReadReceipt(string directory, string name, int expectedExit, string head)
    {
        var path = name.EndsWith("-tool", StringComparison.Ordinal)
            ? Directory.GetFiles(directory, "report.json", SearchOption.AllDirectories).SingleOrDefault()
            : Path.Combine(directory, "source.json");
        if (path is null || !File.Exists(path)) return new RunResult(66, "receipt missing");
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(path)!, ".checkpoint-output-owner.json"))) return new RunResult(66, "receipt directory not freshly owned");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var receipt = doc.RootElement;
        var source = name.EndsWith("-tool", StringComparison.Ordinal) ? receipt.GetProperty("source") : receipt;
        if (source.GetProperty("start").GetProperty("commit").GetString() != head) return new RunResult(66, "receipt source mismatch");
        if (receipt.GetProperty("exitCode").GetInt32() != expectedExit) return new RunResult(66, "receipt exit mismatch");
        if (name.EndsWith("-tool", StringComparison.Ordinal))
        {
            if (expectedExit == 2)
            {
                if (receipt.GetProperty("verdict").GetString() != "INVALID" || receipt.GetProperty("reason").GetString() != "source_ineligible") return new RunResult(66, "receipt verdict mismatch");
                var model = JsonSerializer.Deserialize<Antiphon.Checkpoints.ReportModel>(File.ReadAllText(path), Antiphon.Checkpoints.ReportWriter.Json)!;
                // A failed run is rejected before source eligibility (CARD-1370).
                // Keep both contracts: the tampered source cannot qualify, and the
                // complete failed report must retain its higher-priority refusal.
                if (Antiphon.Checkpoints.ReportValidator.IsSourceEligible(model.Source, head)) return new RunResult(66, "tampered source qualified");
                if (Antiphon.Checkpoints.ReportValidator.Validate(model, head, [name]) != "report_failed") return new RunResult(66, "negative qualification mismatch");
            }
            if (!receipt.GetProperty("runId").GetString()!.StartsWith(name + "-", StringComparison.Ordinal)
                || receipt.GetProperty("rows")[0].GetProperty("id").GetString() != name)
                return new RunResult(66, "receipt identity mismatch");
        }
        else if (receipt.GetProperty("name").GetString() != name) return new RunResult(66, "receipt identity mismatch");
        return new RunResult(expectedExit, expectedExit == 2 ? "source_ineligible" : "valid");
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
                "directory-on-file" => "rm -f -- tracked.txt && mv -- user-dir tracked.txt && git --git-dir=.git --work-tree=. add -A -- tracked.txt && git --git-dir=.git --work-tree=. restore --staged -- tracked.txt/unstaged.txt",
                "symlink-on-file" => "ln -sfn -- link-target.txt tracked.txt && git --git-dir=.git --work-tree=. add -- tracked.txt",
                "untracked-descendant" => "rm -f -- tracked.txt && mv -- user-dir tracked.txt",
                "ignored-descendant" => "rm -f -- tracked.txt && mv -- user-dir tracked.txt",
                "staged-untracked" => "git --git-dir=.git --work-tree=. add -- user-keep.txt",
                "modified-tracked" => "printf '%s' MUTATED > tracked.txt",
                "deleted-tracked" => "rm -f -- tracked.txt",
                "regular-user-move" => "mv -f -- user-move.txt tracked.txt",
                "deleted-untracked" => "rm -f -- user-move.txt && printf %s MUTATED > tracked.txt",
                "changed-untracked" => "printf %s CHANGED-USER > user-move.txt && printf %s MUTATED > tracked.txt",
                "existing-stamp" or "compiler-collision" or "existing-output-directory" or "existing-output-symlink" => "printf %s MUTATED > tracked.txt",
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
                Write(root, "user-dir/unstaged.txt", "USER-UNSTAGED");
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
            case "deleted-untracked":
            case "changed-untracked":
            case "regular-user-move":
                Write(root, "user-move.txt", "USER-MOVED");
                break;
            case "existing-stamp":
                Write(root, "App/bin-probe/checkpoint-build-source.json", "USER-OUTPUT");
                break;
            case "compiler-collision":
                Write(root, "App/bin-probe/App.dll", "USER-OUTPUT");
                break;
            case "existing-output-directory":
                Directory.CreateDirectory(Path.Combine(root, "App/bin-probe"));
                break;
            case "existing-output-symlink":
                Write(root, "outside/App.dll", "USER-OUTPUT");
                Directory.CreateSymbolicLink(Path.Combine(root, "App/bin-probe"), Path.Combine(root, "outside"));
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
                <MakeDir Directories="__ROOT__/.antiphon" />
                <WriteLinesToFile File="__ROOT__/.antiphon/c1199-mutation-reached.txt" Lines="reached" Overwrite="true" />
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

    internal async Task<RunResult> WaitAsync(ProcessStartInfo start, CancellationToken cancellationToken)
    {
        var result = await CheckpointFixtureProcess.RunAsync(start, cancellationToken);
        return new RunResult(result.Exit, result.Stdout + result.Stderr);
    }

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(content));
    }

    private static string GitOut(string root, params string[] args) => CheckpointFixtureProcess.Git(root, args);

    private static void Git(string root, params string[] args) => _ = GitOut(root, args);

    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux())
            Skip.Test("CARD-1199 shape-conflict proof runs on Linux; Windows junctions remain an R2 disclosure.");
    }

    internal sealed record RunResult(int Exit, string Output);
}
