using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

// CP-2 is commissioned separately on Windows; portable Code never selects this class.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CodexCliVersionWindowsTests
{
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
        var current = await kit.Attempt(kit.Executable);
        var older = await kit.Attempt(otherNative);
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
            var direct = await kit.Attempt(layout.SiblingNodePath, resolutionCwd: layout.Root, path: "", codexJsPrefix: prefix);
            CodexCliVersionTestFixture.Text(direct, "codexCliVersion").ShouldBe("0.160.0", "C959-v07-node");
            kit.Starts.Last().ArgumentList.ShouldBe([layout.JsPath, "--version"],
                Path.IsPathRooted(prefix) ? "C959-pc-066" : "C959-pc-067");
        }
        kit.Mode = "tree";
        var pending = kit.Attempt(kit.Executable);
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
            var sample = await kit.Attempt(layout.ShimPath, resolutionCwd: layout.Root, path: "");
            CodexCliVersionTestFixture.Text(sample, "codexCliVersion").ShouldBeNull($"C959-pc-{label:000}");
            CodexCliVersionTestFixture.Text(sample, "codexCliVersionError").ShouldNotBeNull("C959-v08-unknown");
            kit.Starts.Count.ShouldBe(0, "C959-v08-no-wrapper-child");
        }
    }
}
