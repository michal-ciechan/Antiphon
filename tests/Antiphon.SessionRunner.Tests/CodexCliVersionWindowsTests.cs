using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

// CP-4 is commissioned separately on native Windows; portable Code never selects this class.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CodexCliVersionWindowsTests
{
    [Test]
    public async Task C1031_Npm_notice_preserves_version()
    {
        OperatingSystem.IsWindows().ShouldBeTrue("C1031-windows-notice native host");
        using var kit = new CodexCliVersionTestFixture();
        Directory.Exists(kit.EmptyPath).ShouldBeTrue("C1031-sealed-path exists");
        Directory.EnumerateFileSystemEntries(kit.EmptyPath).ShouldBeEmpty("C1031-sealed-path empty");
        foreach (var (mode, advisory) in new[] { ("notice", "stderr_output"), ("stderr-4097", "output_truncated") })
        {
            kit.Mode = mode;
            using var layout = new CodexNpmLayout(rootName: "C1031 npm rôot α " + Guid.NewGuid().ToString("N"));
            File.Copy(Environment.ProcessPath!, layout.SiblingNodePath!, true);
            var pathNodeDir = layout.PathNodeDir();
            var pathNode = Path.Combine(pathNodeDir, "node.exe");
            File.Copy(Environment.ProcessPath!, pathNode, true);
            async Task Check(string executable, string path, string expectedExecutable, string[] argv, string? prefix = null)
            {
                var sample = await kit.Attempt(executable, resolutionCwd: layout.Root, path: path,
                    pathExt: ".EXE;.CMD", codexJsPrefix: prefix);
                CodexCliVersionTestFixture.Text(sample, "codexCliVersion").ShouldBe("0.160.0", "C1031-windows-notice");
                CodexCliVersionTestFixture.Text(sample, "codexCliVersionError").ShouldBe(advisory, "C1031-windows-notice diagnostic");
                sample.GetProperty("codexCliVersionCheckedAtUtc").GetDateTimeOffset().ShouldBe(CodexCliVersionTestFixture.T);
                kit.Starts.Last().FileName.ShouldBe(expectedExecutable, "C1031-windows-notice executable");
                kit.Starts.Last().ArgumentList.ToArray().ShouldBe(argv, "C1031-windows-notice argv");
                kit.Children.Last().HasExited.ShouldBeTrue("C1031-windows-notice ownership");
                kit.AuthOpens.ShouldBe(0, "C1031-windows-notice auth-free");
            }
            await Check(layout.ShimPath, kit.EmptyPath, layout.SiblingNodePath!, [layout.JsPath, "--version"]);
            foreach (var prefix in new[] { layout.JsPath, Path.GetRelativePath(layout.Root, layout.JsPath) })
                await Check(layout.SiblingNodePath!, kit.EmptyPath, layout.SiblingNodePath!, [layout.JsPath, "--version"], prefix);
            await Check(kit.Executable, kit.EmptyPath, kit.Executable, ["--version"]);
            File.Delete(layout.SiblingNodePath!);
            await Check(layout.ShimPath, pathNodeDir, pathNode, [layout.JsPath, "--version"]);
        }
    }

    [Test]
    public async Task C959_Npm_probe_uses_launch_resolution()
    {
        OperatingSystem.IsWindows().ShouldBeTrue("C959-v06-Windows-host");
        using var kit = new CodexCliVersionTestFixture();
        using var layout = new CodexNpmLayout(rootName: "C959 npm rôot α " + Guid.NewGuid().ToString("N"));
        File.Copy(Environment.ProcessPath!, layout.SiblingNodePath!, true);
        var pathNodeDir = layout.PathNodeDir();
        File.Copy(Environment.ProcessPath!, Path.Combine(pathNodeDir, "node.exe"), true);
        foreach (var sibling in new[] { true, false })
        {
            if (!sibling) File.Delete(layout.SiblingNodePath!);
            var env = new Dictionary<string, string> { ["PATH"] = pathNodeDir, ["PATHEXT"] = ".EXE;.CMD" };
            var launch = CodexWindowsLaunchPolicy.Apply(new RunnerLaunchRequest(Guid.NewGuid(), layout.ShimPath,
                ["--no-alt-screen"], env, layout.Root, 80, 24, TranscriptFormat: TranscriptFormats.Codex), false);
            var sample = await kit.Attempt(layout.ShimPath, resolutionCwd: layout.Root, path: pathNodeDir, pathExt: ".EXE;.CMD");
            CodexCliVersionTestFixture.Text(sample, "codexCliVersion").ShouldBe("0.160.0", "C959-v06-sibling");
            kit.Starts.Last().FileName.ShouldBe(launch.Exe, sibling ? "C959-pc-061" : "C959-pc-063");
            kit.Starts.Last().FileName.ShouldNotBe(layout.ShimPath, "C959-pc-062");
            kit.Starts.Last().ArgumentList.ShouldBe([launch.Args[0], "--version"], "C959-pc-064");
            launch.Args[0].ShouldBe(layout.JsPath, "C959-v06-js-prefix");
        }
    }

    [Test]
    public async Task C959_Native_and_direct_node_probe()
    {
        OperatingSystem.IsWindows().ShouldBeTrue("C959-v07-Windows-host");
        using var kit = new CodexCliVersionTestFixture();
        var otherNative = Path.Combine(kit.Root, "other", "codex.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(otherNative)!);
        File.Copy(kit.Executable, otherNative);
        kit.ChildMode = info => info.FileName == otherNative ? "version-old" : "success";
        var current = await kit.Attempt(kit.Executable, path: kit.EmptyPath);
        var older = await kit.Attempt(otherNative, path: kit.EmptyPath);
        CodexCliVersionTestFixture.Text(current, "codexCliVersion").ShouldBe("0.160.0", "C959-v07-current");
        CodexCliVersionTestFixture.Text(older, "codexCliVersion").ShouldBe("0.156.1", "C959-pc-065");
        kit.Starts.Last().FileName.ShouldBe(otherNative, "C959-v07-selected-native");
        kit.Starts.Last().ArgumentList.ShouldBe(["--version"], "C959-v07-native-argv");
        CodexCliVersionTestFixture.Text(older, "codexCliLauncherFingerprint")
            .ShouldNotBe(CodexCliVersionTestFixture.Text(current, "codexCliLauncherFingerprint"), "C959-v07-distinct-install");
        using var layout = new CodexNpmLayout();
        File.Copy(Environment.ProcessPath!, layout.SiblingNodePath!, true);
        kit.ChildMode = null;
        foreach (var prefix in new[] { layout.JsPath, Path.GetRelativePath(layout.Root, layout.JsPath) })
        {
            var direct = await kit.Attempt(layout.SiblingNodePath, resolutionCwd: layout.Root, path: kit.EmptyPath, codexJsPrefix: prefix);
            CodexCliVersionTestFixture.Text(direct, "codexCliVersion").ShouldBe("0.160.0", "C959-v07-node");
            kit.Starts.Last().ArgumentList.ShouldBe([layout.JsPath, "--version"],
                Path.IsPathRooted(prefix) ? "C959-pc-066" : "C959-pc-067");
        }
        kit.Mode = "tree";
        var pending = kit.Attempt(kit.Executable, path: kit.EmptyPath);
        await kit.WaitForReceiptAsync("leaf");
        kit.Clock.Advance(TimeSpan.FromSeconds(5));
        var timedOut = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        CodexCliVersionTestFixture.Text(timedOut, "codexCliVersion").ShouldBeNull("C959-v07-cleanup-unknown");
        kit.ReceiptIsAlive("leaf").ShouldBeFalse("C959-pc-068");
    }

    [Test]
    public async Task C959_Unverified_launcher_is_unknown()
    {
        OperatingSystem.IsWindows().ShouldBeTrue("C959-v08-Windows-host");
        using var kit = new CodexCliVersionTestFixture();
        foreach (var (node, js, native, shim, label) in new[]
        {
            (false, true, true, (string?)null, 69), (true, false, true, (string?)null, 70),
            (true, true, false, (string?)null, 71),
            (true, true, true, CodexWindowsLaunchPolicy.StockNpmShimText + "\necho modified\n", 72),
        })
        {
            using var layout = new CodexNpmLayout(siblingNode: node, js: js, native: native, shimText: shim);
            if (node) File.Copy(Environment.ProcessPath!, layout.SiblingNodePath!, true);
            var sample = await kit.Attempt(layout.ShimPath, resolutionCwd: layout.Root, path: kit.EmptyPath);
            CodexCliVersionTestFixture.Text(sample, "codexCliVersion").ShouldBeNull($"C959-pc-{label:000}");
            CodexCliVersionTestFixture.Text(sample, "codexCliVersionError").ShouldNotBeNull("C959-v08-unknown");
            kit.Starts.Count.ShouldBe(0, "C959-v08-no-wrapper-child");
        }
    }
}
