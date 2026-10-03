using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.Checkpoints.Coverage;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class PlanCoverageHandleTests : CheckpointTestBase
{
    [Test]
    [Arguments("project")]
    [Arguments("source")]
    [Arguments("plan")]
    [Arguments("checklist")]
    public void hardlinked_selected_file_is_refused(string kind)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("hardlink sentinel requires Linux link(2)");
        var root = TempDir(); var world = PlanCoverageFixture.WriteWorld(root);
        var path = SelectedPath(kind, root, world);
        var outside = Path.Combine(TempDir(), "outside");
        File.WriteAllText(outside, File.ReadAllText(path)); File.Delete(path);
        if (Link(outside, path) != 0) Skip.Test($"filesystem cannot create hardlink: errno={Marshal.GetLastPInvokeError()}");
        var output = new StringWriter();
        new CoverageCommand().Run(root, world.Plan, checklist: kind == "checklist" ? path : null, output: output)
            .ShouldBe(2, "coverage-hardlink-refused");
        output.ToString().ShouldContain("selected file has multiple links", Case.Sensitive, "coverage-hardlink-finding");
    }

    [Test]
    [Arguments("project")]
    [Arguments("source")]
    [Arguments("plan")]
    [Arguments("checklist")]
    public void oversized_selected_file_is_refused(string kind)
    {
        var root = TempDir(); var world = PlanCoverageFixture.WriteWorld(root);
        var path = SelectedPath(kind, root, world);
        File.AppendAllText(path, new string(' ', (kind is "plan" or "checklist" ? 4 : 1) * 1024 * 1024));
        var output = new StringWriter();
        new CoverageCommand().Run(root, world.Plan, checklist: kind == "checklist" ? path : null, output: output)
            .ShouldBe(2, "coverage-size-limit-refused");
        output.ToString().ShouldContain("selected file exceeds size limit", Case.Sensitive, "coverage-size-limit-finding");
    }

    [Test]
    public async Task in_repo_fifo_project_is_refused_within_15_seconds()
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("FIFO sentinel requires Linux mkfifo");
        var root = TempDir(); var world = PlanCoverageFixture.WriteWorld(root);
        var fifo = Path.Combine(Path.GetDirectoryName(world.Source)!, "Probe.csproj");
        MkFifo(fifo, 0x180).ShouldBe(0, "coverage-in-root-fifo-created");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(typeof(PlanCoverageHandleTests).Assembly.Location, ".runtimeconfig.json"), typeof(CoverageCommand).Assembly.Location, "coverage", "--repo-root", root, "--plan", world.Plan }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; RegisterCheckpointChild(process);
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        var completed = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); completed = true; }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            var text = await stdout; var error = await stderr;
            completed.ShouldBeTrue("coverage-in-root-fifo-never-hangs");
            process.ExitCode.ShouldBe(2, "coverage-in-root-fifo-refused: " + error);
            text.ShouldContain("selected file is not regular", Case.Sensitive, "coverage-in-root-fifo-finding");
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }

    [Test]
    [Arguments(true, 1, "inside", null)]
    [Arguments(false, 1, "inside", "selected file is not regular")]
    [Arguments(true, 2, "inside", "selected file has multiple links")]
    [Arguments(true, 0, "inside", "selected file has multiple links")]
    [Arguments(true, 1, "outside", "opened file is outside root")]
    public void opened_metadata_decision_table(bool regular, int links, string location, string? refusal)
    {
        var root = TempDir();
        var path = location == "inside" ? Path.Combine(root, "file.cs") : Path.Combine(root + "-sibling", "file.cs");
        ConfinedFileReader.Refusal(regular, (ulong)links, path, root).ShouldBe(refusal, "coverage-opened-metadata-decision");
    }

    [Test]
    public void read_uses_the_verified_handle_after_path_replacement()
    {
        var root = TempDir(); var path = Path.Combine(root, "file.cs");
        File.WriteAllText(path, "verified bytes");
        var content = ConfinedFileReader.Read(root, path, 1024, afterVerification: () =>
        {
            File.Move(path, Path.Combine(root, "original.cs"));
            File.WriteAllText(path, "replacement bytes");
        });
        content.ShouldBe("verified bytes", "coverage-read-from-verified-handle");
        File.ReadAllText(path).ShouldBe("replacement bytes", "coverage-path-replacement-exercised");
    }
    private static string SelectedPath(string kind, string root, (string Plan, string Source) world)
    {
        if (kind == "source") return world.Source;
        if (kind == "plan") return world.Plan;
        var path = kind == "project" ? Path.Combine(Path.GetDirectoryName(world.Source)!, "Sample.csproj") : Path.Combine(root, "checklist.json");
        File.WriteAllText(path, kind == "project" ? "<Project />" : PlanCoverageFixture.Checklist(PlanCoverageFixture.Item("label", "target-label")));
        return path;
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)] private static extern int Link(string oldPath, string newPath);
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)] private static extern int MkFifo(string path, uint mode);
}
