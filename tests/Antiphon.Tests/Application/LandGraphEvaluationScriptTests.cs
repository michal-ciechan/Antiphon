using System.Diagnostics;
using System.Globalization;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    private const string ManifestFile = "server/Application/Services/LandVerificationBuildGraph.manifest.txt";
    private const string EvaluationFile = "server/Application/Services/LandVerificationBuildGraph.evaluation.txt";

    [Test]
    [Arguments("attribute", true)]
    [Arguments("child", true)]
    [Arguments("attribute", false)]
    [Arguments("child", false)]
    [Timeout(60_000)]
    public async Task C1540_ProjectReferenceMetadataPrecheck(string form, bool reviewed)
    {
        var name = reviewed ? "IsAspireProjectResource" : "UnreviewedGraphMetadata";
        var reference = form == "attribute"
            ? $"<ProjectReference Include=\"../Library/Library.csproj\" {name}=\"false\" />"
            : $"<ProjectReference Include=\"../Library/Library.csproj\"><{name}>false</{name}></ProjectReference>";
        await using var repo = await ScriptRepoAsync(new()
        {
            ["App/App.csproj"] = SdkProject("<ItemGroup>" + reference + "</ItemGroup>"),
            ["Library/Library.csproj"] = SdkProject(""),
        });
        var manifest = repo.Full("precheck-manifest.txt");
        var run = await RunScriptAsync(repo.Root, "-Update", "audit-land-verification-precheck.ps1",
            "-ManifestPath", manifest);
        run.ExitCode.ShouldBe(reviewed ? 0 : 1, run.Text);
        File.Exists(manifest).ShouldBe(reviewed, run.Text);
        if (!reviewed)
        {
            run.Text.ShouldContain("metadata the audit does not allow");
            run.Text.ShouldContain(name);
        }
    }

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
            "imported-directory" => """<Import Project="../eng/deep/shared.props" />""",
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
            files["eng/deep/shared.props"] = """<Project><ItemGroup><None Include="$(MSBuildProjectDirectory)/../docs/flag.md" /></ItemGroup></Project>""";
        await using var repo = await ScriptRepoAsync(files);
        if (variant == "imported-directory")
        {
            var evaluated = await repo.RunToolAsync();
            evaluated.ExitCode.ShouldBe(1, evaluated.Text);
            evaluated.PathText.ShouldContain("docs/flag.md");
        }
        var run = await RunScriptAsync(repo.Root, "-Update");
        run.ExitCode.ShouldBe(1, run.Text);
        if (variant is "relative-dir" or "identity" or "task-properties" or "property-body" or "target-framework")
            run.Text.ShouldContain("Target Hidden (missing pin)");
        if (variant == "imported-directory") run.PathText.ShouldContain("docs/flag.md");
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
        var replacement = await RunScriptAsync(repo.Root, "-Update");
        replacement.ExitCode.ShouldBe(0, replacement.Text);
        (await File.ReadAllLinesAsync(repo.Full(EvaluationFile))).ShouldBe(original);
        foreach (var variant in new[] { "valid", "missing", "digest", "sdk", "sdk-suffix", "malformed-sdk", "pins", "extra-line", "missing-line" })
        {
            var lines = original.ToArray();
            if (variant == "digest") lines[0] = new string('0', 64);
            if (variant == "sdk") lines[1] = "0.0.0";
            if (variant == "sdk-suffix") lines[1] = "10.0.402-not-a-host";
            if (variant == "malformed-sdk") lines[1] = "malformed-sdk";
            if (variant == "pins") lines[2] = new string('0', 40);
            if (variant == "extra-line") lines = [.. lines, "unexpected"];
            if (variant == "missing-line") lines = lines[..2];
            await File.WriteAllLinesAsync(repo.Full(EvaluationFile), lines);
            if (variant == "missing") File.Delete(repo.Full(EvaluationFile));
            var check = await RunScriptAsync(repo.Root, "-Check");
            check.ExitCode.ShouldBe(variant == "valid" ? 0 : 1, variant + ": " + check.Text);
        }
        var real = await RunScriptAsync(RepoRoot(), "-Check", extra: ["-ToolPath", ToolDll()]);
        real.ExitCode.ShouldBe(0, real.Text);
    }

    [Test]
    [Arguments("0.0.0")]
    [Arguments("10.0.402-not-a-host")]
    [Arguments("10.0.203")]
    [Arguments("9.0.999")]
    [Arguments("11.0.100")]
    [Arguments("10.00.402")]
    [Arguments("10.0.0402")]
    [Arguments("10.0.402-preview.1")]
    [Timeout(60_000)]
    public async Task CheckRefusesSdkOutsideCommittedPolicy(string recordedSdk)
    {
        await using var repo = await ScriptRepoAsync(new() { ["App/App.csproj"] = SdkProject("") });
        var update = await RunScriptAsync(repo.Root, "-Update");
        update.ExitCode.ShouldBe(0, update.Text);
        var record = await File.ReadAllLinesAsync(repo.Full(EvaluationFile));
        record[1] = recordedSdk;
        await File.WriteAllLinesAsync(repo.Full(EvaluationFile), record);
        var check = await RunScriptAsync(repo.Root, "-Check");
        check.ExitCode.ShouldBe(1, check.Text);
        check.Text.ShouldContain("SDK provenance");
    }

    [Test]
    [Arguments("approved")]
    [Arguments("missing-tool")]
    [Arguments("sdk-doc-glob")]
    [Timeout(60_000)]
    public async Task CheckDifferentSdkRequiresEvaluation(string variant)
    {
        var project = variant == "sdk-doc-glob" ? "App.csproj" : "App/App.csproj";
        await using var repo = await ScriptRepoAsync(new()
        {
            [project] = variant == "sdk-doc-glob" ? "<Project />" : SdkProject(""),
            ["docs/guide.md"] = "# SDK default item witness",
        });
        var update = await RunScriptAsync(repo.Root, "-Update");
        update.ExitCode.ShouldBe(0, update.Text);
        var record = await File.ReadAllLinesAsync(repo.Full(EvaluationFile));
        if (variant == "sdk-doc-glob")
        {
            // The installed SDK's default None glob reaches docs from a root project.
            // The pre-check admits it; synthetic provenance must not bypass evaluation.
            await File.WriteAllTextAsync(repo.Full(project), SdkProject(""));
            await AuditRepo.GitAsync(repo.Root, "add", project);
            await AuditRepo.GitAsync(repo.Root, "-c", "user.name=c1483", "-c", "user.email=c1483@antiphon.test",
                "commit", "-qm", "SDK default docs glob");
            var pre = await RunScriptAsync(repo.Root, "-Update", "audit-land-verification-precheck.ps1",
                "-ManifestPath", repo.Full(ManifestFile));
            pre.ExitCode.ShouldBe(0, pre.Text);
            record[0] = (await File.ReadAllLinesAsync(repo.Full(ManifestFile)))[0];
        }
        var hostSdk = record[1];
        var differentSdk = hostSdk == "10.0.402" ? "10.0.403" : "10.0.402";
        if (variant == "missing-tool")
            File.Delete(repo.Full("tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/Antiphon.LandGraphAudit.dll"));

        // Provenance equal to the host must not bypass evaluation either.
        foreach (var recordedSdk in new[] { differentSdk, hostSdk })
        {
            record[1] = recordedSdk;
            await File.WriteAllLinesAsync(repo.Full(EvaluationFile), record);
            var manifest = await File.ReadAllBytesAsync(repo.Full(ManifestFile));
            var evaluation = await File.ReadAllBytesAsync(repo.Full(EvaluationFile));
            var pins = await File.ReadAllBytesAsync(repo.Full(PinsFile));

            var check = await RunScriptAsync(repo.Root, "-Check");
            check.ExitCode.ShouldBe(variant == "approved" ? 0 : 1, $"recorded SDK {recordedSdk}: " + check.Text);
            if (variant == "approved") check.Lines.ShouldContain("approved", check.Text);
            if (variant == "missing-tool") check.Text.ShouldContain("built evaluator missing:");
            if (variant == "sdk-doc-glob")
            {
                check.PathText.ShouldContain("Microsoft.NET.Sdk.DefaultItems.props");
                check.PathText.ShouldContain("docs/guide.md");
                check.Text.ShouldContain("land graph evaluation refused (exit 1)");
            }
            (await File.ReadAllBytesAsync(repo.Full(ManifestFile))).ShouldBe(manifest);
            (await File.ReadAllBytesAsync(repo.Full(EvaluationFile))).ShouldBe(evaluation);
            (await File.ReadAllBytesAsync(repo.Full(PinsFile))).ShouldBe(pins);
        }
    }

    [Test]
    [Timeout(300_000)]
    public async Task C1483_S5_EvaluationFailurePreservesRecords()
    {
        foreach (var variant in new[] { "evaluation", "missing-tool", "dirty-project", "dirty-pins" })
        {
            await using var repo = await ScriptRepoAsync(new() { ["App/App.csproj"] = "<Project />" });
            if (variant == "evaluation")
            {
                // Missing entry point gives the real evaluator exit 2, after the pre-check approves.
                File.Delete(repo.Full("Antiphon.sln"));
                await AuditRepo.GitAsync(repo.Root, "add", "-u");
                await AuditRepo.GitAsync(repo.Root, "-c", "user.name=c1483", "-c", "user.email=c1483@antiphon.test", "commit", "-qm", "missing solution");
            }
            if (variant == "missing-tool") File.Delete(repo.Full("tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/Antiphon.LandGraphAudit.dll"));
            if (variant == "dirty-project") await File.AppendAllTextAsync(repo.Full("App/App.csproj"), "<!-- dirty -->");
            if (variant == "dirty-pins") await File.AppendAllTextAsync(repo.Full(PinsFile), "# dirty\n");
            await File.WriteAllTextAsync(repo.Full(ManifestFile), "old manifest\n");
            await File.WriteAllTextAsync(repo.Full(EvaluationFile), "old evaluation\n");
            var run = await RunScriptAsync(repo.Root, "-Update");
            run.ExitCode.ShouldBe(1, variant + ": " + run.Text);
            run.Text.ShouldContain(variant switch
            {
                "evaluation" => "evaluation-failed:",
                "missing-tool" => "built evaluator missing:",
                _ => "source differs from HEAD",
            });
            (await File.ReadAllTextAsync(repo.Full(ManifestFile))).ShouldBe("old manifest\n");
            (await File.ReadAllTextAsync(repo.Full(EvaluationFile))).ShouldBe("old evaluation\n");
        }
    }

    [Test]
    [Timeout(300_000)]
    public async Task C1528_UpdateRefusalPreservesRecords()
    {
        foreach (var variant in new[] { "present", "projected", "metadata" })
        foreach (var priorRecords in new[] { false, true })
        {
            var fixture = FilesystemFixtures().Single(f => f.Id == variant);
            var files = fixture.Inputs();
            // Use the wrapper's established property/item/metadata names. This isolates the
            // evaluator decision from the older pre-check's separate name allowlists.
            var unsafeProject = files["App/App.csproj"].Replace("Probe", "None")
                .Replace("Rel", "PackagePath").Replace("Body", "PackagePath")
                .Replace("<EnableDefaultItems>false</EnableDefaultItems>", "")
                .Replace("sentinel.txt", "sentinel.json");
            files.Remove("App/sentinel.txt");
            files["App/sentinel.json"] = "{}";
            files["App/App.csproj"] = SdkProject("");
            await using var repo = await ScriptRepoAsync(files);
            byte[]? manifest = null;
            byte[]? evaluation = null;
            if (priorRecords)
            {
                var initial = await RunScriptAsync(repo.Root, "-Update");
                initial.ExitCode.ShouldBe(0, initial.Text);
                manifest = await File.ReadAllBytesAsync(repo.Full(ManifestFile));
                evaluation = await File.ReadAllBytesAsync(repo.Full(EvaluationFile));
            }
            await File.WriteAllTextAsync(repo.Full("App/App.csproj"), unsafeProject);
            await AuditRepo.GitAsync(repo.Root, "add", "App/App.csproj");
            await AuditRepo.GitAsync(repo.Root, "-c", "user.name=c1528", "-c", "user.email=c1528@antiphon.test",
                "commit", "-qm", "unsafe evaluation expression");
            var pre = await RunScriptAsync(repo.Root, "-Update", "audit-land-verification-precheck.ps1",
                "-ManifestPath", repo.Full("precheck-manifest.txt"));
            pre.ExitCode.ShouldBe(0, variant + ": the pre-check must admit this witness: " + pre.Text);
            var run = await RunScriptAsync(repo.Root, "-Update");
            run.ExitCode.ShouldBe(1, variant + ": " + run.Text);
            run.Text.ShouldContain("filesystem-function", Case.Sensitive, run.Text);
            run.Text.ShouldContain("land graph evaluation refused (exit 1)", Case.Sensitive, run.Text);
            if (priorRecords)
            {
                (await File.ReadAllBytesAsync(repo.Full(ManifestFile))).ShouldBe(manifest!);
                (await File.ReadAllBytesAsync(repo.Full(EvaluationFile))).ShouldBe(evaluation!);
            }
            else
            {
                File.Exists(repo.Full(ManifestFile)).ShouldBeFalse(run.Text);
                File.Exists(repo.Full(EvaluationFile)).ShouldBeFalse(run.Text);
            }
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task EvaluationBudgetDerivationUsesTwiceMeasuredP95AndWindowsFloor()
    {
        var linux = await PrintBudgetAsync(22328, 8000, false);
        linux.Pass.ShouldBe(45);
        linux.Copy.ShouldBe(16);
        linux.Total.ShouldBe(106);
        linux.Grace.ShouldBe(60);
        linux.Ceiling.ShouldBe(166);

        var windows = await PrintBudgetAsync(22328, 8000, true);
        windows.Pass.ShouldBe(120);
        windows.Copy.ShouldBe(16);
        windows.Total.ShouldBe(256);
        windows.Grace.ShouldBe(60);
        windows.Ceiling.ShouldBe(316);

        var wider = await PrintBudgetAsync(70000, 1000, true);
        wider.Pass.ShouldBe(140);
        wider.Copy.ShouldBe(2);
        wider.Total.ShouldBe(282);
        wider.Grace.ShouldBe(60);
        wider.Ceiling.ShouldBe(342);

        var floor = await PrintBudgetAsync(0, 0, false);
        floor.Pass.ShouldBe(1);
        floor.Copy.ShouldBe(1);
        floor.Total.ShouldBe(3);
        floor.Grace.ShouldBe(60);
        floor.Ceiling.ShouldBe(63);

        var host = await PrintBudgetAsync(null, null, null);
        host.Pass.ShouldBe(OperatingSystem.IsWindows() ? 120 : 45);
        host.Copy.ShouldBe(5);
        host.Total.ShouldBe(host.Pass * 2 + host.Copy);
        host.Grace.ShouldBe(60);
        host.Ceiling.ShouldBe(host.Total + host.Grace);
        var script = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "scripts", "audit-land-verification-graph.ps1"));
        script.ShouldContain("StartsWith('ceiling=')", Case.Sensitive);
        script.ShouldNotContain("$grace = 30", Case.Sensitive);
        var budgetSource = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "tools", "Antiphon.LandGraphAudit", "EvaluationBudget.cs"));
        budgetSource.ShouldContain("public const int MeasuredCopyP95Milliseconds = 2253;", Case.Sensitive);
    }

    [Test]
    [Timeout(180_000)]
    public async Task EvaluationBudgetRefusesSlowDotnetAndAcceptsFastDotnet()
    {
        if (OperatingSystem.IsWindows())
            throw new SkipTestException("The slow evaluator is a PATH shim named dotnet; Windows process start resolves dotnet.exe.");

        await using var repo = await ScriptRepoAsync(new() { ["App/App.csproj"] = SdkProject("") });
        var update = await RunScriptAsync(repo.Root, "-Update");
        update.ExitCode.ShouldBe(0, update.Text);
        var sdk = (await File.ReadAllLinesAsync(repo.Full(EvaluationFile)))[1];

        var slow = await RunCheckWithFakeDotnetAsync(repo.Root, sdk, sleepSeconds: 30, budgetSeconds: 1);
        slow.ExitCode.ShouldBe(1, slow.Text);
        slow.Text.ShouldContain("evaluation-failed: evaluating Antiphon.sln exceeded 1 s", Case.Sensitive);
        slow.Text.ShouldContain("land graph evaluation refused (exit 2)", Case.Sensitive);
        slow.Text.ShouldNotContain("approved", Case.Sensitive);

        var fast = await RunCheckWithFakeDotnetAsync(repo.Root, sdk, sleepSeconds: 0, budgetSeconds: 30);
        fast.ExitCode.ShouldBe(0, fast.Text);
        fast.Text.ShouldContain("approved", Case.Sensitive);
    }

    // 70% + 70% exceeds one pass budget and fits two separate pass clocks.
    [Test]
    [Timeout(120_000)]
    public async Task PerPassClocksApproveWhenEachPassUsesSeventyPercent()
    {
        await using var repo = await TinyAuditRepoAsync();
        var run = await repo.RunToolAsync(WitnessArgs(2000, 10_000, 1400, 0, 1400));
        run.ExitCode.ShouldBe(0, run.Text);
        run.Text.ShouldContain("approved", Case.Sensitive);
        run.Text.ShouldNotContain("exceeded", Case.Sensitive);
        AbsentCanary(run.Text);
    }

    [Test]
    [Timeout(120_000)]
    public async Task Pass1TimeoutExits2AndNamesThePass()
    {
        await using var repo = await TinyAuditRepoAsync();
        var run = await repo.RunToolAsync(WitnessArgs(1000, 10_000, 2500, 0, 0));
        run.ExitCode.ShouldBe(2, run.Text);
        run.Text.ShouldContain("pass 1 exceeded", Case.Sensitive);
        run.Text.ShouldNotContain("approved", Case.Sensitive);
    }

    [Test]
    [Timeout(120_000)]
    public async Task Pass2TimeoutExits2NamesThePassAndRemovesCanary()
    {
        await using var repo = await TinyAuditRepoAsync();
        var run = await repo.RunToolAsync(WitnessArgs(1000, 10_000, 0, 0, 2500));
        run.ExitCode.ShouldBe(2, run.Text);
        run.Text.ShouldContain("pass 2 exceeded", Case.Sensitive);
        run.Text.ShouldNotContain("approved", Case.Sensitive);
        AbsentCanary(run.Text);
    }

    [Test]
    [Timeout(120_000)]
    public async Task CopyTimeoutExits2NamesTheCopyAndRemovesCanary()
    {
        await using var repo = await TinyAuditRepoAsync();
        var run = await repo.RunToolAsync(WitnessArgs(10_000, 1000, 0, 4000, 0));
        run.ExitCode.ShouldBe(2, run.Text);
        run.Text.ShouldContain("copy exceeded", Case.Sensitive);
        run.Text.ShouldNotContain("approved", Case.Sensitive);
        AbsentCanary(run.Text);
    }

    [Test]
    [Timeout(120_000)]
    public async Task CanaryCopyIsIndependentOfTheSource()
    {
        const string original = "class Program { static void Main() {} }\n";
        await using var repo = await AuditRepo.CreateAsync(new()
        {
            ["App/Program.cs"] = original,
            ["App/App.csproj"] = SdkProject(""),
        });
        var run = await repo.RunToolAsync([.. WitnessArgs(5000, 15_000, 0, 0, 0), "--touch-canary", "App/Program.cs"]);
        run.ExitCode.ShouldBe(0, run.Text);
        run.Text.ShouldContain("canary-independent: App/Program.cs", Case.Sensitive);
        (await File.ReadAllTextAsync(repo.Full("App/Program.cs"))).ShouldBe(original);
    }

    [Test]
    [Timeout(120_000)]
    public async Task KillDuringPass2LeavesParentListingUnchanged()
    {
        var parent = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "c1483-parent-" + Guid.NewGuid().ToString("N")[..12])).FullName;
        var record = Path.Combine(Path.GetTempPath(), "antiphon-landgraph-canary-record-" + Guid.NewGuid().ToString("N") + ".txt");
        var repo = await TinyAuditRepoAsync();
        try
        {
            var moved = Path.Combine(parent, "repo");
            Directory.Move(repo.Root, moved);
            var before = Directory.GetFileSystemEntries(parent).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = moved,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(ToolDll());
            foreach (var arg in WitnessArgs(180_000, 60_000, 0, 0, 60_000)) psi.ArgumentList.Add(arg);
            psi.ArgumentList.Add("--canary-record");
            psi.ArgumentList.Add(record);
            psi.ArgumentList.Add("--repo");
            psi.ArgumentList.Add(moved);
            psi.ArgumentList.Add("--solution");
            psi.ArgumentList.Add("Antiphon.sln");
            using var process = Process.Start(psi)!;
            var stderr = process.StandardError.ReadToEndAsync();
            string? canary = null;
            var enteredPass2 = false;
            using var sawPass2 = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            while (await process.StandardOutput.ReadLineAsync(sawPass2.Token) is { } line)
            {
                if (line.StartsWith("canary=", StringComparison.Ordinal)) canary = line["canary=".Length..].Trim();
                if (line == "phase=pass2") { enteredPass2 = true; break; }
            }
            enteredPass2.ShouldBeTrue("the kill must happen after pass 2 has started");
            canary.ShouldNotBeNull();
            Directory.Exists(canary).ShouldBeTrue(canary);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            try { await stderr; } catch (Exception) { }
            Directory.Exists(canary).ShouldBeTrue("SIGKILL leaves the canary for the script");
            var script = Path.Combine(RepoRoot(), "scripts", "audit-land-verification-graph.ps1");
            var cleanup = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-RemoveRecordedCanary", record, "-RemoveRecordedCanaryRepo", moved })
                cleanup.ArgumentList.Add(arg);
            using var cleaner = Process.Start(cleanup)!;
            var cleanupText = await cleaner.StandardOutput.ReadToEndAsync() + await cleaner.StandardError.ReadToEndAsync();
            await cleaner.WaitForExitAsync();
            cleaner.ExitCode.ShouldBe(0, cleanupText);
            Directory.Exists(canary).ShouldBeFalse(canary);
            Directory.GetFileSystemEntries(parent).OrderBy(path => path, StringComparer.Ordinal).ToArray().ShouldBe(before);
        }
        finally
        {
            if (Directory.Exists(parent)) StartRefGit.DeleteDirectory(parent);
            if (Directory.Exists(repo.Root)) StartRefGit.DeleteDirectory(repo.Root);
            if (File.Exists(record)) File.Delete(record);
        }
    }

    [Test]
    [Timeout(300_000)]
    public async Task ScriptKillDuringPass2LeavesParentListingUnchanged()
    {
        var parent = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "c1483-parent-" + Guid.NewGuid().ToString("N")[..12])).FullName;
        var repo = await ScriptRepoAsync(new()
        {
            ["App/Program.cs"] = "class Program { static void Main() {} }\n",
            ["App/App.csproj"] = SdkProject(""),
        });
        var moved = Path.Combine(parent, "repo");
        try
        {
            Directory.Move(repo.Root, moved);
            var before = Directory.GetFileSystemEntries(parent).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var update = await RunScriptAsync(moved, "-Update");
            update.ExitCode.ShouldBe(0, update.Text);
            var check = await RunCheckWitnessAsync(moved, 45, "180000,60000,0,0,120000");
            check.ExitCode.ShouldBe(1, check.Text);
            check.Text.ShouldContain("phase=pass2", Case.Sensitive);
            check.Text.ShouldContain("land graph evaluation refused (exit 2)", Case.Sensitive);
            Directory.GetFileSystemEntries(parent).OrderBy(path => path, StringComparer.Ordinal).ToArray().ShouldBe(before);
        }
        finally
        {
            if (Directory.Exists(parent)) StartRefGit.DeleteDirectory(parent);
            if (Directory.Exists(repo.Root)) StartRefGit.DeleteDirectory(repo.Root);
        }
    }

    private static async Task<AuditRepo> TinyAuditRepoAsync() =>
        await AuditRepo.CreateAsync(new()
        {
            ["App/Program.cs"] = "class Program { static void Main() {} }\n",
            ["App/App.csproj"] = SdkProject(""),
        });

    private static string[] WitnessArgs(int passBudget, int copyBudget, int hold1, int holdCopy, int hold2) =>
    [
        "--witness-clocks",
        "--pass-budget-ms", passBudget.ToString(CultureInfo.InvariantCulture),
        "--copy-budget-ms", copyBudget.ToString(CultureInfo.InvariantCulture),
        "--hold-pass1-ms", hold1.ToString(CultureInfo.InvariantCulture),
        "--hold-copy-ms", holdCopy.ToString(CultureInfo.InvariantCulture),
        "--hold-pass2-ms", hold2.ToString(CultureInfo.InvariantCulture),
    ];

    private static void AbsentCanary(string text)
    {
        var line = text.Split('\n').Select(item => item.Trim()).First(item => item.StartsWith("canary=", StringComparison.Ordinal));
        var path = line["canary=".Length..].Trim();
        path.ShouldNotBeNullOrWhiteSpace();
        Directory.Exists(path).ShouldBeFalse(path);
    }

    private static async Task<ToolRun> RunCheckWitnessAsync(string root, int budgetSeconds, string witnessClock)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var script = Path.Combine(root, "scripts", "audit-land-verification-graph.ps1");
        var command = "Set-StrictMode -Version Latest; & '" + script.Replace("'", "''") + "' -Check -EvaluationBudgetSeconds "
            + budgetSeconds.ToString(CultureInfo.InvariantCulture) + " -WitnessClock '" + witnessClock + "'";
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", command }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        return new ToolRun(process.ExitCode, await stdout + await stderr);
    }

    private static async Task<(int Pass, int Copy, int Total, int Grace, int Ceiling)> PrintBudgetAsync(int? passMs, int? copyMs, bool? windows)
    {
        var dll = ToolDll();
        File.Exists(dll).ShouldBeTrue(dll);
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add("--print-budget");
        if (passMs is int pass)
        {
            psi.ArgumentList.Add("--measured-pass-p95-ms");
            psi.ArgumentList.Add(pass.ToString(CultureInfo.InvariantCulture));
        }
        if (copyMs is int copy)
        {
            psi.ArgumentList.Add("--measured-copy-p95-ms");
            psi.ArgumentList.Add(copy.ToString(CultureInfo.InvariantCulture));
        }
        if (windows is bool windowsHost)
        {
            psi.ArgumentList.Add("--windows");
            psi.ArgumentList.Add(windowsHost ? "true" : "false");
        }
        using var process = Process.Start(psi)!;
        var text = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, text);
        int Read(string name)
        {
            var line = text.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith(name + "=", StringComparison.Ordinal));
            return int.Parse(line[(name.Length + 1)..], CultureInfo.InvariantCulture);
        }
        return (Read("pass"), Read("copy"), Read("total"), Read("grace"), Read("ceiling"));
    }

    private static async Task<ToolRun> RunCheckWithFakeDotnetAsync(string root, string sdk, int sleepSeconds, int budgetSeconds)
    {
        var shimDir = Path.Combine(Path.GetTempPath(), "c1483-dotnet-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(shimDir);
        var shim = Path.Combine(shimDir, "dotnet");
        await File.WriteAllTextAsync(shim, "#!/bin/sh\nif [ \"$1\" = \"--version\" ]; then\n  printf '%s\\n' \"" + sdk + "\"\n  exit 0\nfi\nif [ \"" + sleepSeconds.ToString(CultureInfo.InvariantCulture) + "\" != \"0\" ]; then\n  sleep " + sleepSeconds.ToString(CultureInfo.InvariantCulture) + "\nfi\nprintf 'approved\\n'\nexit 0\n");
        File.SetUnixFileMode(shim, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var start = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.Environment["PATH"] = shimDir + ":" + start.Environment["PATH"];
            var script = Path.Combine(root, "scripts", "audit-land-verification-graph.ps1");
            var command = "Set-StrictMode -Version Latest; & '" + script.Replace("'", "''") + "' -Check -EvaluationBudgetSeconds " + budgetSeconds.ToString(CultureInfo.InvariantCulture);
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", command }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            return new ToolRun(process.ExitCode, await stdout + await stderr);
        }
        finally
        {
            if (Directory.Exists(shimDir)) Directory.Delete(shimDir, recursive: true);
        }
    }

    internal static async Task<AuditRepo> ScriptRepoAsync(Dictionary<string, string> files)
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

    internal static async Task<ToolRun> RunScriptAsync(string root, string mode,
        string script = "audit-land-verification-graph.ps1", params string[] extra)
    {
        var start = new ProcessStartInfo("pwsh") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        // Exercise absent/empty/value wrapper inputs under strict PowerShell semantics too.
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        if (mode is not ("-Update" or "-Check")) throw new ArgumentOutOfRangeException(nameof(mode));
        var command = "Set-StrictMode -Version Latest; & " + Quote(Path.Combine(root, "scripts", script)) + " " + mode;
        if (extra.Length != 0)
        {
            if (extra.Length != 2 || extra[0] is not ("-ManifestPath" or "-ToolPath")) throw new ArgumentException("unsupported script arguments");
            command += " " + extra[0] + " " + Quote(extra[1]);
        }
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", command }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(360));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return new ToolRun(process.ExitCode, await stdout + await stderr);
    }
}
