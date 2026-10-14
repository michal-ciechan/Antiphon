using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointShadowCustodyTests : CheckpointTestBase
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task separate_wait_removes_only_the_sealed_tool_copy(int exit)
    {
        var run = await CompletedRun(exit);
        var evidence = Directory.GetFiles(run).ToDictionary(path => path, File.ReadAllBytes);
        var result = Wait(run);
        result.Exit.ShouldBe(exit, result.Stdout + result.Stderr);
        result.Stdout.ShouldContain("TOOL CLEANUP Removed");
        Directory.Exists(Path.Combine(run, "tool")).ShouldBeFalse();
        foreach (var (path, bytes) in evidence) File.ReadAllBytes(path).ShouldBe(bytes);
        Wait(run).Exit.ShouldBe(exit, "already absent is idempotent in another process");
    }

    [Test]
    [Arguments("added")]
    [Arguments("changed")]
    [Arguments("forged-ledger")]
    [Arguments("missing")]
    [Arguments("malformed")]
    [Arguments("foreign")]
    [Arguments("escape")]
    [Arguments("linked")]
    [Arguments("hardlinked")]
    [Arguments("replaced")]
    public async Task separate_wait_retains_unproven_copy_and_outside_bytes(string variant)
    {
        // The real cross-process control must clear the same cleanup path first.
        var control = await CompletedRun(0);
        var accepted = Wait(control);
        accepted.Exit.ShouldBe(0, accepted.Stdout + accepted.Stderr);
        Directory.Exists(Path.Combine(control, "tool")).ShouldBeFalse();

        var run = await CompletedRun(0);
        var tool = Path.Combine(run, "tool");
        var outside = Path.Combine(run, "outside.txt");
        File.WriteAllText(outside, "OUTSIDE");
        var user = Path.Combine(tool, "user.txt");
        var authority = Path.Combine(run, "tool-copy-authority.json");
        if (variant is "added" or "forged-ledger") File.WriteAllText(user, "USER");
        if (variant == "changed") File.WriteAllText(Path.Combine(tool, "sentinel.txt"), "evil-tool-source");
        if (variant == "forged-ledger")
            File.WriteAllText(Path.Combine(tool, ".checkpoint-output-ledger.json"), "{\"user.txt\":\"file-size:4\"}");
        if (variant == "missing") File.Delete(authority);
        if (variant == "malformed") File.WriteAllText(authority, "{");
        if (variant == "foreign") File.Copy(Path.Combine(await CompletedRun(0), "tool-copy-authority.json"), authority, true);
        if (variant == "escape")
        {
            var record = JsonNode.Parse(File.ReadAllText(authority))!;
            record["entries"]![0]!["path"] = "../outside.txt";
            File.WriteAllText(authority, record.ToJsonString());
            // Even a caller able to replace the control record cannot authorize an escaping path.
            var journalPath = Path.Combine(run, RunOwnershipStore.FileName);
            var journal = JsonNode.Parse(File.ReadAllText(journalPath))!;
            journal["toolCopy"]!["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(authority)));
            File.WriteAllText(journalPath, journal.ToJsonString());
        }
        if (variant == "linked") File.CreateSymbolicLink(user, outside);
        if (variant == "hardlinked")
        {
            if (OperatingSystem.IsWindows()) CreateHardLink(user, outside, IntPtr.Zero).ShouldBeTrue();
            else Link(outside, user).ShouldBe(0);
        }
        if (variant == "replaced")
        {
            var original = Path.Combine(run, "original-tool");
            Directory.Move(tool, original);
            Directory.CreateDirectory(tool);
            foreach (var file in Directory.GetFiles(original, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(tool, Path.GetRelativePath(original, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
        var reportBytes = File.ReadAllBytes(Path.Combine(run, "report.json"));
        try
        {
            var result = Wait(run);
            result.Exit.ShouldBe(2, variant + ": " + result.Stdout + result.Stderr);
            Directory.Exists(tool).ShouldBeTrue(variant);
            File.ReadAllText(outside).ShouldBe("OUTSIDE");
            File.ReadAllBytes(Path.Combine(run, "report.json")).ShouldBe(reportBytes);
            if (variant is "added" or "forged-ledger") File.ReadAllText(user).ShouldBe("USER");
            if (variant == "changed") File.ReadAllText(Path.Combine(tool, "sentinel.txt")).ShouldBe("evil-tool-source");
        }
        finally { if (variant is "linked" or "hardlinked") File.Delete(user); }
    }

    private async Task<string> CompletedRun(int exit)
    {
        var repo = TempDir();
        var source = TinyToolDirectory();
        Directory.CreateDirectory(Path.Combine(source, "fr"));
        File.WriteAllText(Path.Combine(source, "fr", "resource.txt"), "RESOURCE");
        var result = await CheckpointApp.StartAsync(new CheckpointManifest
        {
            Checkpoints = [new CheckpointSpec { Id = "CP-1", After = ["all"], Command = "true", EstimatedMinutes = 1 }],
        }, new RunRequest { Commit = new string('a', 40), Branch = "fixture" }, repo, TextWriter.Null,
            new CheckpointApp.Runtime
            {
                EnvironmentLookup = _ => null, ToolDirectory = source,
                SourceCapture = _ => CheckpointFixtures.CleanSource(),
                LaunchWithOutcome = _ => new LaunchOutcome(LaunchKind.Started, int.MaxValue,
                    new ProcessIdentityProbe().Current() with { Pid = int.MaxValue, StartUtcTicks = 1 }),
            });
        result.ExitCode.ShouldBe(0);
        var receiptSource = new SourceEvidence();
        ReportWriter.WriteFiles(result.RunDirectory, new ReportModel
        { RunId = Path.GetFileName(result.RunDirectory), Source = receiptSource, ExitCode = exit, Verdict = exit == 0 ? "GREEN" : "RED" });
        SafeWrite.Require(new RunStateStore().Write(Path.Combine(result.RunDirectory, "state.json"), new RunState
        { RunId = Path.GetFileName(result.RunDirectory), Source = receiptSource, Phase = "done", ExitCode = exit }));
        return result.RunDirectory;
    }

    private static CheckpointFixtureProcess.Output Wait(string run)
    {
        var testAssembly = typeof(CheckpointShadowCustodyTests).Assembly.Location;
        var start = new ProcessStartInfo("dotnet");
        foreach (var arg in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(testAssembly, ".runtimeconfig.json"),
            "--depsfile", Path.ChangeExtension(testAssembly, ".deps.json"), typeof(Antiphon.Checkpoints.Program).Assembly.Location,
            "wait", "--repo-root", Path.GetDirectoryName(run)!, "--run", run, "--max-wait", "1s" })
            start.ArgumentList.Add(arg);
        return CheckpointFixtureProcess.Run(start, 15_000);
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string oldPath, string newPath);
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newPath, string oldPath, IntPtr reserved);
}
