using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    private const string ManifestFile = "server/Application/Services/LandVerificationBuildGraph.manifest.txt";
    private const string EvaluationFile = "server/Application/Services/LandVerificationBuildGraph.evaluation.txt";

    [Test]
    [Arguments("relative-dir")]
    [Arguments("identity")]
    [Arguments("task-properties")]
    [Arguments("property-body")]
    [Arguments("target-framework")]
    [Arguments("imported-directory")]
    [Arguments("condition-exists")]
    [Arguments("condition-read")]
    [Arguments("review-r2-thisfile")]
    [Arguments("review-r2-project")]
    [Timeout(300_000)]
    public async Task C1483_S5_UpdateRefusesWhenEvaluationRefuses(string variant)
    {
        var body = variant switch
        {
            "relative-dir" => """<Target Name="Hidden"><ItemGroup><_StagedHelperFiles Include="../global.json" /></ItemGroup><PropertyGroup><UsePublishedPackages>%(_StagedHelperFiles.RelativeDir)</UsePublishedPackages></PropertyGroup><ItemGroup><None Include="$(UsePublishedPackages)docs/data.json" /></ItemGroup></Target>""",
            "identity" => """<Target Name="Hidden"><ItemGroup><_AntiphonRepeatLine Include="../README.md" /></ItemGroup><PropertyGroup><PackageTags>%(_AntiphonRepeatLine.Identity)</PackageTags></PropertyGroup><ItemGroup><None Include="$(PackageTags)" /></ItemGroup></Target>""",
            "task-properties" => """<Target Name="Hidden"><MSBuild Projects="App.csproj" Properties="Configuration=Debug;CustomBeforeMicrosoftCommonTargets=../docs/evil.md" /></Target>""",
            "property-body" => """<Target Name="Hidden"><PropertyGroup><_StagedHelperDest>out;CustomBeforeMicrosoftCommonTargets=../docs/evil.md</_StagedHelperDest></PropertyGroup></Target>""",
            "target-framework" => """<Target Name="Hidden"><PropertyGroup><TargetFramework>net9.0;CustomBeforeMicrosoftCommonTargets=../docs/evil.md</TargetFramework></PropertyGroup></Target>""",
            "imported-directory" => """<Import Project="../eng/deep/shared.props" /><ItemGroup><None Include="$(PackageTags)" /></ItemGroup>""",
            "condition-exists" => """<PropertyGroup Condition="Exists('$(MSBuildThisFileDirectory)../docs/flag.md')"><PackageTags>yes</PackageTags></PropertyGroup>""",
            "condition-read" => """<PropertyGroup Condition="$([System.IO.File]::ReadAllText('$(MSBuildThisFileDirectory)../README.md')) == 'hello'"><PackageTags>yes</PackageTags></PropertyGroup>""",
            "review-r2-thisfile" => """<PropertyGroup><PackageTags>x.json;$(MSBuildThisFileDirectory)../docs/flag.md</PackageTags></PropertyGroup>""",
            "review-r2-project" => """<PropertyGroup><PackageTags>x.json;$(MSBuildProjectDirectory)/../docs/flag.md</PackageTags></PropertyGroup>""",
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        var files = new Dictionary<string, string>
        {
            ["App/App.csproj"] = SdkProject(body),
            ["README.md"] = "hello",
            ["docs/flag.md"] = "flag",
            ["docs/data.json"] = "{}",
            ["docs/evil.md"] = "<Project />",
        };
        if (variant == "imported-directory")
            files["eng/deep/shared.props"] = """<Project><PropertyGroup><PackageTags>$(MSBuildProjectDirectory)/../docs/flag.md</PackageTags></PropertyGroup></Project>""";
        await using var repo = await ScriptRepoAsync(files);
        var run = await RunScriptAsync(repo.Root, "-Update");
        run.ExitCode.ShouldBe(1, run.Text);
        if (variant is "relative-dir" or "identity" or "task-properties" or "property-body" or "target-framework")
            run.Text.ShouldContain("Target Hidden (missing pin)");
        if (variant == "imported-directory") run.Text.ShouldContain("docs/flag.md");
        File.Exists(repo.Full(ManifestFile)).ShouldBeFalse(run.Text);
        File.Exists(repo.Full(EvaluationFile)).ShouldBeFalse(run.Text);
    }

    [Test]
    [Timeout(300_000)]
    public async Task C1483_S5_CheckRequiresEvaluationRecord()
    {
        await using var repo = await ScriptRepoAsync(new() { ["App/App.csproj"] = SdkProject("") });
        var update = await RunScriptAsync(repo.Root, "-Update");
        update.ExitCode.ShouldBe(0, update.Text);
        File.Exists(repo.Full(EvaluationFile)).ShouldBeTrue("Update must publish evaluation provenance");
        var original = await File.ReadAllLinesAsync(repo.Full(EvaluationFile));
        original.Length.ShouldBe(3);
        original[0].ShouldBe((await File.ReadAllLinesAsync(repo.Full(ManifestFile)))[0]);
        foreach (var variant in new[] { "valid", "missing", "digest", "sdk", "pins", "extra-line" })
        {
            var lines = original.ToArray();
            if (variant == "digest") lines[0] = new string('0', 64);
            if (variant == "sdk") lines[1] = "0.0.0";
            if (variant == "pins") lines[2] = new string('0', 40);
            if (variant == "extra-line") lines = [.. lines, "unexpected"];
            await File.WriteAllLinesAsync(repo.Full(EvaluationFile), lines);
            if (variant == "missing") File.Delete(repo.Full(EvaluationFile));
            var check = await RunScriptAsync(repo.Root, "-Check");
            check.ExitCode.ShouldBe(variant == "valid" ? 0 : 1, variant + ": " + check.Text);
        }
        var real = await RunScriptAsync(RepoRoot(), "-Check");
        real.ExitCode.ShouldBe(0, real.Text);
    }

    [Test]
    [Timeout(300_000)]
    public async Task C1483_S5_EvaluationFailurePreservesRecords()
    {
        await using var repo = await ScriptRepoAsync(new() { ["App/App.csproj"] = "<Project />" });
        // Missing entry point gives the real evaluator exit 2, after the pre-check approves.
        File.Delete(repo.Full("Antiphon.sln"));
        await AuditRepo.GitAsync(repo.Root, "add", "-A");
        await AuditRepo.GitAsync(repo.Root, "-c", "user.name=c1483", "-c", "user.email=c1483@antiphon.test", "commit", "-qm", "missing solution");
        await File.WriteAllTextAsync(repo.Full(ManifestFile), "old manifest\n");
        await File.WriteAllTextAsync(repo.Full(EvaluationFile), "old evaluation\n");
        var run = await RunScriptAsync(repo.Root, "-Update");
        run.ExitCode.ShouldBe(1, run.Text);
        run.Text.ShouldContain("evaluation-failed:");
        (await File.ReadAllTextAsync(repo.Full(ManifestFile))).ShouldBe("old manifest\n");
        (await File.ReadAllTextAsync(repo.Full(EvaluationFile))).ShouldBe("old evaluation\n");
    }

    private static async Task<AuditRepo> ScriptRepoAsync(Dictionary<string, string> files)
    {
        files[PinsFile] = "";
        files["scripts/audit-land-verification-graph.ps1"] = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "scripts/audit-land-verification-graph.ps1"));
        var precheck = Path.Combine(RepoRoot(), "scripts/audit-land-verification-precheck.ps1");
        if (File.Exists(precheck)) files["scripts/audit-land-verification-precheck.ps1"] = await File.ReadAllTextAsync(precheck);
        var repo = await AuditRepo.CreateAsync(files);
        var output = repo.Full("tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0");
        Directory.CreateDirectory(output);
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(ToolDll())!))
            File.Copy(file, Path.Combine(output, Path.GetFileName(file)));
        return repo;
    }

    private static async Task<ToolRun> RunScriptAsync(string root, string mode)
    {
        var start = new ProcessStartInfo("pwsh") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(root, "scripts/audit-land-verification-graph.ps1"), mode }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(240));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return new ToolRun(process.ExitCode, await stdout + await stderr);
    }
}
