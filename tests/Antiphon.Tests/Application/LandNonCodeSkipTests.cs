using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandNonCodeSkipTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1537_SeedPreservesCommittedGraphWithCrLfCheckout(bool missingWorkingFile)
    {
        await using var source = new LandingGitFixture();
        await using var destination = new LandingGitFixture();
        await source.InitializeAsync();
        await destination.InitializeAsync();
        await source.RequiredAsync(source.Repository, "config", "core.autocrlf", "false");
        await File.WriteAllTextAsync(Path.Combine(source.Repository, "Directory.Build.props"), "<Project>\n</Project>\n");
        await File.WriteAllBytesAsync(Path.Combine(source.Repository, "executable.targets"), [0, 255, 10, 13, 0]);
        await source.RequiredAsync(source.Repository, "add", "--", "Directory.Build.props", "executable.targets");
        await source.RequiredAsync(source.Repository, "update-index", "--chmod=+x", "executable.targets");
        await source.RequiredAsync(source.Repository, "commit", "-m", "committed graph bytes and modes");
        await source.RequiredAsync(source.Repository, "config", "core.autocrlf", "true");
        await source.RequiredAsync(source.Repository, "config", "core.eol", "crlf");
        var props = Path.Combine(source.Repository, "Directory.Build.props");
        File.Delete(props);
        await source.RequiredAsync(source.Repository, "checkout", "--", "Directory.Build.props");
        (await File.ReadAllTextAsync(props)).ShouldBe("<Project>\r\n</Project>\r\n",
            "the real Git checkout must reproduce Windows CRLF bytes on every OS");
        if (missingWorkingFile) File.Delete(props);

        await destination.RequiredAsync(destination.Repository, "config", "core.autocrlf", "false");
        await destination.RequiredAsync(destination.Repository, "config", "core.filemode", "false");
        await CopyGraphAsync(destination, source.Repository);
        await destination.RequiredAsync(destination.Repository, "commit", "-m", "seeded graph");
        var paths = new[] { "Directory.Build.props", "executable.targets" };
        var expected = await source.RequiredAsync(source.Repository, ["ls-tree", "-r", "-z", "HEAD", "--", .. paths]);
        var actual = await destination.RequiredAsync(destination.Repository, ["ls-tree", "-r", "-z", "HEAD", "--", .. paths]);
        actual.ShouldBe(expected, "seeding must preserve committed blob IDs and executable modes, independent of checkout bytes");
    }

    [Test]
    public async Task C1380_DocsOnlyRebasedLandRecordsNonCodeSkip()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await SeedGraphAsync(h);
        await h.AddDocsOnlySourceAsync();
        var target = await h.Fixture.PushIndependentAsync("advanced-target");
        await h.RequestAsync();
        await h.RunQueuedAsync();

        var op = (await h.OperationAsync())!;
        h.Verifier.Calls.ShouldBe(0, "a docs-only rebased candidate must not invoke the verifier or its build slot; " +
            $"publication={op.Publication}; reason={op.LastReason}; command={op.VerificationCommand}");
        op.TargetBeforeSha.ShouldBe(target);
        op.RebasedSourceSha.ShouldNotBe(op.OriginalSourceSha);
        op.VerificationPassed.ShouldBeFalse();
        op.VerificationSkipReason.ShouldBe("non-code");
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        await using var db = h.CreateContext();
        var row = await db.StageOutcomes.SingleAsync(o => o.SubjectTaskId == h.Fixture.TaskId && o.Stage == OrchestrationStage.Verify);
        row.Outcome.ShouldBe(StageOutcomeKind.Skipped);
        row.Detail.ShouldBe("non-code");
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(op.RebasedSourceSha);
    }

    [Test]
    public async Task C1380_CSharpChangeBuilds()
    {
        await using var h = await CreateAsync();
        await ChangeAsync(h, "server/Feature.cs", "public class Feature {}");
        await LandAndAssertAsync(h, build: true);
    }

    [Test]
    public async Task C1380_FailedBuildRefusesPublication()
    {
        await using var h = await CreateAsync();
        await ChangeAsync(h, "server/Broken.cs", "not valid C#");
        var target = await h.Fixture.PushIndependentAsync("advanced-target");
        h.Verifier.Passed = false;
        h.Fixture.Git.Trace.Clear();
        await h.RequestAsync();
        await h.RunQueuedAsync();
        h.Verifier.Calls.ShouldBe(1);
        var op = (await h.OperationAsync())!;
        op.VerificationPassed.ShouldBeFalse();
        op.VerificationSkipReason.ShouldBeNull();
        op.LastReason.ShouldBe("verification_failed");
        op.Publication.ShouldBe(LandPublicationOutcome.Refused);
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(target);
        await using var db = h.CreateContext();
        var verify = await db.StageOutcomes.SingleAsync(o => o.SubjectTaskId == h.Fixture.TaskId && o.Stage == OrchestrationStage.Verify);
        verify.Outcome.ShouldBe(StageOutcomeKind.Found);
        verify.Detail.ShouldContain("build failed");
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push");
    }

    [Test]
    [Arguments("server/Bundles/new.md")]
    [Arguments("docs/data.json")]
    [Arguments("tests/Antiphon.Tests/fixture.md")]
    [Arguments("tools/Example/fixture.md")]
    [Arguments("notes.md")]
    [Arguments("client/src/index.ts")]
    [Arguments("new.csproj")]
    [Arguments("new.props")]
    [Arguments("new.targets")]
    [Arguments("new.sln")]
    [Arguments("Directory.Build.rsp")]
    [Arguments("global.json")]
    [Arguments("packages.lock.json")]
    [Arguments("NuGet.config")]
    [Arguments("package-lock.json")]
    [Arguments("scripts/new.ps1")]
    public async Task C1380_NonAllowlistedInputsBuild(string path)
    {
        await using var h = await CreateAsync();
        await ChangeAsync(h, path, "changed build input");
        await LandAndAssertAsync(h, build: true);
    }

    [Test]
    [Arguments("add")]
    [Arguments("edit")]
    [Arguments("delete")]
    [Arguments("rename")]
    [Arguments("README")]
    [Arguments("empty")]
    public async Task C1380_RegularDocumentationChangesSkip(string variant)
    {
        await using var h = await CreateAsync();
        if (variant is "edit" or "delete" or "rename")
        {
            await h.Fixture.PushIndependentAsync("existing-doc", "docs/existing.md", "original documentation\n");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "fetch", "origin", "master");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "rebase", "FETCH_HEAD");
        }
        switch (variant)
        {
            case "delete": File.Delete(Path.Combine(h.Fixture.Source, "docs/existing.md")); break;
            case "rename":
                await h.Fixture.RequiredAsync(h.Fixture.Source, "mv", "docs/existing.md", "docs/renamed.md"); break;
            case "empty": break;
            default:
                var path = variant == "README" ? "README.md" : variant == "edit" ? "docs/existing.md" : "docs/new.md";
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(h.Fixture.Source, path))!);
                await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, path), "updated documentation\n");
                break;
        }
        await CommitAsync(h);
        await LandAndAssertAsync(h, build: false);
    }

    [Test]
    [Arguments("into-docs")]
    [Arguments("out-of-docs")]
    [Arguments("symlink")]
    [Arguments("mode")]
    public async Task C1380_RenameBothNamesAndFileModesAreChecked(string variant)
    {
        await using var h = await CreateAsync();
        var original = variant == "into-docs" ? "feature.txt" : "docs/existing.md";
        await h.Fixture.PushIndependentAsync("existing-file", original, "unchanged content\n");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "fetch", "origin", "master");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "rebase", "FETCH_HEAD");
        Directory.CreateDirectory(Path.Combine(h.Fixture.Source, "docs"));
        if (variant == "into-docs")
            await h.Fixture.RequiredAsync(h.Fixture.Source, "mv", original, "docs/renamed.md");
        else if (variant == "out-of-docs")
            await h.Fixture.RequiredAsync(h.Fixture.Source, "mv", original, "feature.txt");
        else if (variant == "symlink")
        {
            var path = Path.Combine(h.Fixture.Source, original);
            File.Delete(path);
            File.CreateSymbolicLink(path, "../keep.txt");
        }
        else
        {
            // core.fileMode=false keeps the checkout portable while the committed mode is explicit.
            await h.Fixture.RequiredAsync(h.Fixture.Source, "config", "core.fileMode", "false");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "update-index", "--chmod=+x", original);
        }
        await CommitAsync(h);
        await LandAndAssertAsync(h, build: true);
    }

    [Test]
    [Arguments("force")]
    [Arguments("filter")]
    [Arguments("rc")]
    public async Task C1380_OverridesAndReleaseTargetsBuild(string variant)
    {
        await using var h = await CreateAsync(force: variant == "force");
        var target = "refs/heads/master";
        if (variant == "rc")
        {
            target = "refs/heads/rc/fixture";
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "branch", "rc/fixture", "master");
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", "master:" + target);
            await h.SetRoleAsync(AgentTaskRole.Code, target);
        }
        await h.AddDocsOnlySourceAsync();
        await LandAndAssertAsync(h, build: true, filter: variant == "filter" ? "/*/*/Selected/*" : null, target: target);
        if (variant == "filter") h.Verifier.Invocations.Single().Filter.ShouldBe("/*/*/Selected/*");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1380_TargetGraphDriftAndNewProjectsBuild(bool newProject)
    {
        await using var h = await CreateAsync();
        await h.AddDocsOnlySourceAsync();
        var path = newProject ? "server/extra.csproj" : "server/Antiphon.Server.csproj";
        await h.Fixture.PushIndependentAsync("graph-drift", path,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><EmbeddedResource Include=\"../docs/**/*.md\" /></ItemGroup></Project>");
        await LandAndAssertAsync(h, build: true);
        (await h.OperationAsync())!.VerificationCommand.ShouldNotBeNull().ShouldContain("unrecognized-build-graph");
    }

    [Test]
    public async Task C1380_UnknownRepositoryBuilds()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddDocsOnlySourceAsync();
        await LandAndAssertAsync(h, build: true);
        (await h.OperationAsync())!.VerificationCommand.ShouldNotBeNull().ShouldContain("unrecognized-build-graph");
    }

    [Test]
    [Arguments("failed")]
    [Arguments("truncated")]
    [Arguments("malformed")]
    [Arguments("graph-failed")]
    public async Task C1380_UnreadableOrMalformedGitCannotSkip(string variant)
    {
        await using var h = await CreateAsync();
        await h.AddDocsOnlySourceAsync();
        var intercepted = false;
        h.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args[0] == (variant == "graph-failed" ? "ls-tree" : "diff-tree"))
            {
                intercepted = true;
                return Task.FromResult<LandingGitResult?>(variant switch
                {
                    "truncated" => new(0, ":000000 100644 " + new string('0', 40) + " " + new string('a', 40) + " A\0docs/new.md", ""),
                    "malformed" => new(0, "invalid\0docs/new.md\0", ""),
                    _ => new(1, "", "unreadable")
                });
            }
            return Task.FromResult<LandingGitResult?>(null);
        };
        await LandAndAssertAsync(h, build: true);
        intercepted.ShouldBeTrue();
    }

    [Test]
    public async Task C1380_UnchangedBaseStillBuildsCode()
    {
        await using var h = await CreateAsync();
        await ChangeAsync(h, "Feature.cs", "public class Feature {}");
        await LandAndAssertAsync(h, build: true);
        var op = (await h.OperationAsync())!;
        op.RebasedSourceSha.ShouldBe(op.OriginalSourceSha);
    }

    [Test]
    public async Task C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff()
    {
        await using var h = await CreateAsync();
        await ChangeAsync(h, "Feature.cs", "public class Feature {}");
        var code = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        await h.AddDocsOnlySourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", code + ":refs/heads/master");
        await h.Fixture.PushIndependentAsync("advanced-target");
        await LandAndAssertAsync(h, build: false);
        var op = (await h.OperationAsync())!;
        h.Fixture.Git.Trace.ShouldContain(a => a[0] == "diff-tree"
            && a.Contains(op.TargetBeforeSha) && a.Contains(op.RebasedSourceSha!));
    }

    [Test]
    public async Task C1385_UnrelatedScriptChangeStillSkips()
    {
        await using var h = await CreateAsync();
        await h.AddDocsOnlySourceAsync();
        await h.Fixture.PushIndependentAsync("unrelated-script", "scripts/unrelated-card-1385.ps1", "# unrelated script\n");
        await LandAndAssertAsync(h, build: false);
        var command = (await h.OperationAsync())!.VerificationCommand;
        command.ShouldNotBeNull();
        command.ShouldContain("Skipped(non-code)");
        command.ShouldContain("graph=");
    }

    [Test]
    [Arguments("10.0.300", null)]
    [Arguments("10.0.402", null)]
    [Arguments("10.0.403", null)]
    [Arguments("malformed-sdk", "SDK provenance")]
    [Arguments("digest", "digest")]
    [Arguments("pins", "pins SHA")]
    [Arguments("missing-line", "three lines")]
    public async Task EvaluationRecordPreservesSdkProvenanceWithoutBindingTheCheckingHost(string variant, string? refusal)
    {
        var root = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />",
            ["server/Application/Services/LandVerificationBuildGraph.pins.txt"] = "# fixture pins\n",
        });
        await root.RunAsync(async () =>
        {
            var generated = await RunAuditAsync(root, "en-US", "-Update");
            generated.Code.ShouldBe(0, generated.Stdout + generated.Stderr);
            // The existing lightweight fixtures run the pre-check. Exercise the real approval checker here,
            // with an explicit synthetic approval record, without building/evaluating another project.
            File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "audit-land-verification-precheck.ps1"),
                Path.Combine(root, "scripts", "audit-land-verification-precheck.ps1"));
            File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "audit-land-verification-graph.ps1"),
                Path.Combine(root, "scripts", "audit-land-verification-graph.ps1"), overwrite: true);
            await GitAsync(root, "add", "--", "scripts");
            await GitAsync(root, "commit", "-m", "real approval checker");
            var manifest = await File.ReadAllLinesAsync(AuditManifest(root));
            var pins = Encoding.UTF8.GetString(await GitBytesAsync(root, "rev-parse",
                "HEAD:server/Application/Services/LandVerificationBuildGraph.pins.txt")).Trim();
            var record = new[] { manifest[0], refusal is null ? variant : "10.0.300", pins };
            if (variant == "malformed-sdk") record[1] = variant;
            if (variant == "digest") record[0] = new string('0', 64);
            if (variant == "pins") record[2] = new string('0', 40);
            if (variant == "missing-line") record = record[..2];
            await File.WriteAllLinesAsync(Path.Combine(root, "server", "Application", "Services",
                "LandVerificationBuildGraph.evaluation.txt"), record);

            var checkedRecord = await RunAuditAsync(root, "en-US", "-Check");
            checkedRecord.Code.ShouldBe(refusal is null ? 0 : 1, checkedRecord.Stdout + checkedRecord.Stderr);
            if (refusal is not null) (checkedRecord.Stdout + checkedRecord.Stderr).ShouldContain(refusal);
        });
    }

    [Test]
    public async Task C1385_RegistryMatchesNarrowGraph()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var raw = await ReadHeadTreeAsync(root);
        raw.EndsWith('\0').ShouldBeTrue();
        var graph = new LandVerificationBuildGraph();
        var selected = new List<string>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in raw.Split('\0').SkipLast(1))
        {
            var tab = row.IndexOf('\t');
            tab.ShouldBeGreaterThanOrEqualTo(0);
            var metadata = row[..tab].Split(' ');
            var path = row[(tab + 1)..];
            metadata.Length.ShouldBe(3);
            paths.Add(path).ShouldBeTrue(path);
            if (!graph.Includes(path)) continue;
            (metadata[0] is "100644" or "100755").ShouldBeTrue(path);
            metadata[1].ShouldBe("blob", path);
            selected.Add(row);
        }

        var digest = LandVerificationBuildGraph.FingerprintOf(selected);
        digest.ShouldBe(LandVerificationBuildGraph.Fingerprint,
            $"selected definitions={selected.Count}; audited definitions={LandVerificationBuildGraph.AuditedCount}; " +
            "run scripts/audit-land-verification-graph.ps1 -Check to capture the admission refusal");
        LandVerificationBuildGraph.AuditedCount.ShouldBe(selected.Count);

        var manifestLines = await File.ReadAllLinesAsync(Path.Combine(root, "server", "Application", "Services",
            "LandVerificationBuildGraph.manifest.txt"));
        manifestLines.Length.ShouldBe(2);
        manifestLines[0].ShouldBe(digest);
        int.Parse(manifestLines[1], CultureInfo.InvariantCulture).ShouldBe(selected.Count);

        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(root, "scripts", "audit-land-verification-graph.ps1"));
        start.ArgumentList.Add("-Check");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, stdout + stderr);
        stdout.ShouldContain("digest=" + digest);
        stdout.ShouldContain("count=" + selected.Count.ToString(CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task C1390_UpdateRefusesDocsMarkdownInput()
    {
        var root = await InitAuditRepositoryAsync("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <EmbeddedResource Include="../docs/**/*.md" />
              </ItemGroup>
            </Project>
            """);
        await root.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(root, "en-US", "-Update");
            ran.Code.ShouldBe(1, ran.Stdout + ran.Stderr);
            ran.Stderr.ShouldContain("cannot approve it");
            ran.Stderr.ShouldContain("docs/**/*.md");
            File.Exists(AuditManifest(root)).ShouldBeFalse();
        });
    }

    [Test]
    public async Task C1400_UpdateRefusesCaseVariantDocsInputs()
    {
        string[] includes = ["../Docs/**/*.md", "../docs/**/*.MD", "../readme.md"];
        foreach (var include in includes)
        {
            var root = await InitAuditRepositoryAsync($"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <EmbeddedResource Include="{include}" />
                  </ItemGroup>
                </Project>
                """);
            await root.RunAsync(async () =>
            {
                var ran = await RunAuditAsync(root, "en-US", "-Update");
                ran.Code.ShouldBe(1, include + " " + ran.Stdout + ran.Stderr);
                ran.Stderr.ShouldContain("cannot approve it");
                ran.Stderr.IndexOf(include, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
                File.Exists(AuditManifest(root)).ShouldBeFalse();
            });
        }
    }

    [Test]
    public async Task C1400_UpdateRefusesImportedPropsDocsInclude()
    {
        var explicitImport = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../eng/x/shared.props" />
                </Project>
                """,
            ["eng/x/shared.props"] = """
                <Project>
                  <ItemGroup>
                    <Content Include="../docs/**/*.md" />
                  </ItemGroup>
                </Project>
                """,
        });
        await explicitImport.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(explicitImport, "en-US", "-Update");
            ran.Code.ShouldBe(1, ran.Stdout + ran.Stderr);
            ran.Stderr.ShouldContain("cannot approve it");
            ran.Stderr.IndexOf("eng/x/shared.props", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            ran.Stderr.IndexOf("../docs/**/*.md", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            File.Exists(AuditManifest(explicitImport)).ShouldBeFalse();
        });

        var directoryBuild = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["Directory.Build.props"] = """
                <Project>
                  <ItemGroup>
                    <None Include="$(MSBuildProjectDirectory)/../docs/**/*.md" />
                  </ItemGroup>
                </Project>
                """,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                </Project>
                """,
        });
        await directoryBuild.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(directoryBuild, "en-US", "-Update");
            ran.Code.ShouldBe(1, ran.Stdout + ran.Stderr);
            ran.Stderr.ShouldContain("cannot approve it");
            ran.Stderr.IndexOf("Directory.Build.props", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            ran.Stderr.IndexOf("$(MSBuildProjectDirectory)/../docs/**/*.md", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            File.Exists(AuditManifest(directoryBuild)).ShouldBeFalse();
        });
    }

    [Test]
    public async Task C1400_UpdateRefusesPercentEncodedDocsInclude()
    {
        var root = await InitAuditRepositoryAsync("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <None Include="../%64ocs/**/*.md" />
              </ItemGroup>
            </Project>
            """);
        await root.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(root, "en-US", "-Update");
            ran.Code.ShouldBe(1, ran.Stdout + ran.Stderr);
            ran.Stderr.ShouldContain("cannot approve it");
            ran.Stderr.IndexOf("../%64ocs/**/*.md", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            File.Exists(AuditManifest(root)).ShouldBeFalse();
        });
    }

    [Test]
    public async Task C1400_UpdateRefusesAdditionalFilesDocsInclude()
    {
        var root = await InitAuditRepositoryAsync("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <AdditionalFiles Include="../docs/**/*.md" />
              </ItemGroup>
            </Project>
            """);
        await root.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(root, "en-US", "-Update");
            ran.Code.ShouldBe(1, ran.Stdout + ran.Stderr);
            ran.Stderr.ShouldContain("cannot approve it");
            ran.Stderr.IndexOf("AdditionalFiles", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            ran.Stderr.IndexOf("../docs/**/*.md", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            File.Exists(AuditManifest(root)).ShouldBeFalse();
        });
    }

    [Test]
    public async Task C1400_RepairAnchoredImportRefusesDocsInclude()
    {
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="$(MSBuildThisFileDirectory)../eng/x/shared.props" />
                </Project>
                """,
            ["eng/x/shared.props"] = """
                <Project>
                  <ItemGroup>
                    <Content Include="../docs/**/*.md" />
                  </ItemGroup>
                </Project>
                """,
        }, "eng/x/shared.props", "../docs/**/*.md");
    }

    [Test]
    public async Task C1400_RepairNestedAnchoredImportRefusesDocsInclude()
    {
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="$(MSBuildProjectDirectory)/../eng/mid.props" />
                </Project>
                """,
            ["eng/mid.props"] = """
                <Project>
                  <Import Project="$(MSBuildThisFileDirectory)x/shared.props" />
                </Project>
                """,
            ["eng/x/shared.props"] = """
                <Project>
                  <ItemGroup>
                    <Content Include="../docs/**/*.md" />
                  </ItemGroup>
                </Project>
                """,
        }, "eng/x/shared.props", "../docs/**/*.md");
    }

    [Test]
    public async Task C1400_RepairRepoRootPropertyImportRefuses()
    {
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["build/x.props"] = """
                <Project>
                  <ItemGroup>
                    <None Include="../docs/*.md" />
                  </ItemGroup>
                </Project>
                """,
            ["B/B.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="$(RepoRoot)build/x.props" />
                </Project>
                """,
            ["src/App/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../../build/x.props" />
                </Project>
                """,
        }, "cannot approve it");
    }

    [Test]
    public async Task C1400_RepairSplitAnchoredImportRefusesDocsInclude()
    {
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["build/x.props"] = """
                <Project>
                  <ItemGroup>
                    <None Include="../docs/*.md" />
                  </ItemGroup>
                </Project>
                """,
            ["B/B.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="$(MSBuildThisFileDirectory)../build/x.props" />
                </Project>
                """,
            ["src/App/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../../build/x.props" />
                </Project>
                """,
        }, "build/x.props", "../docs/*.md");
    }

    [Test]
    public async Task C1400_RepairOwnDirectoryStillRefusesWhenImported()
    {
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["build/x.props"] = """
                <Project>
                  <ItemGroup>
                    <None Include="../docs/*.md" />
                  </ItemGroup>
                </Project>
                """,
            ["src/App/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../../build/x.props" />
                </Project>
                """,
        }, "build/x.props", "../docs/*.md");
    }

    [Test]
    public async Task C1400_RepairUnresolvedImportRefuses()
    {
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["safe/note.props"] = """
                <Project>
                  <ItemGroup>
                    <None Include="notes.txt" />
                  </ItemGroup>
                </Project>
                """,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="$(RepoRoot)safe/note.props" />
                </Project>
                """,
        }, "$(RepoRoot)safe/note.props");
    }

    [Test]
    public async Task C1409_ImportWithItemOrMetadataReferenceRefuses()
    {
        foreach (var reference in new[] { "@(SharedProps)", "%(Shared.Path)" })
        {
            var root = await InitAuditTreeAsync(new Dictionary<string, string>
            {
                ["eng/shared.props"] = """
                    <Project>
                      <ItemGroup>
                        <None Include="notes.txt" />
                      </ItemGroup>
                    </Project>
                    """,
                ["server/App.csproj"] =
                    "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <Import Project=\"" + reference + "\" />\n</Project>\n",
            });
            await root.RunAsync(async () =>
            {
                var ran = await RunAuditAsync(root, "en-US", "-Update");
                ran.Code.ShouldBe(1, reference + ": " + ran.Stdout + ran.Stderr);
                ran.Stderr.ShouldContain("cannot approve it", customMessage: reference);
                ran.Stderr.IndexOf("server/App.csproj Import Project=" + reference, StringComparison.Ordinal)
                    .ShouldBeGreaterThanOrEqualTo(0, reference + ": " + ran.Stderr);
                File.Exists(AuditManifest(root)).ShouldBeFalse(reference);
            });
        }
    }

    [Test]
    public async Task C1409_ImportWithUnsupportedAttributeRefuses()
    {
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/Sdk.props"] = """
                <Project>
                  <ItemGroup>
                    <None Include="notes.txt" />
                  </ItemGroup>
                </Project>
                """,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="Sdk.props" Sdk="Some.Sdk" />
                </Project>
                """,
        }, "server/App.csproj Import Project=Sdk.props", "Sdk=Some.Sdk");
    }

    [Test]
    public async Task C1409_MissingImportTargetRefuses()
    {
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../eng/missing.props" />
                </Project>
                """,
        }, "server/App.csproj Import Project=../eng/missing.props", "-> eng/missing.props");
    }

    [Test]
    public async Task C1409_ImportOutsideRepositoryRefuses()
    {
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../../outside.props" />
                </Project>
                """,
        }, "server/App.csproj Import Project=../../outside.props", "outside the repository");
    }

    [Test]
    public async Task C1409_UninspectedImportTargetRefuses()
    {
        const string items = """
            <Project>
              <ItemGroup>
                <Content Include="../docs/**/*.md" />
              </ItemGroup>
            </Project>
            """;
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["eng/docs.items"] = items,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../eng/docs.items" />
                </Project>
                """,
        }, "server/App.csproj Import Project=../eng/docs.items", "-> eng/docs.items");
    }

    // CARD-1409 repair (Review 23aedc52 D2): a wildcard Import is refused outright. Land digests only
    // definition files, so a later non-definition match of an accepted wildcard would never be re-audited.
    [Test]
    public async Task C1409_WildcardImportRefuses()
    {
        const string safe = """
            <Project>
              <ItemGroup>
                <None Include="notes.txt" />
              </ItemGroup>
            </Project>
            """;
        const string items = """
            <Project>
              <ItemGroup>
                <Content Include="../docs/**/*.md" />
              </ItemGroup>
            </Project>
            """;
        // Matches nothing and was accepted (the former C1409_EmptyWildcardImportIsAccepted).
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../eng/*.props" />
                </Project>
                """,
        }, "wildcard", "server/App.csproj Import Project=../eng/*.props");
        // Matches one tracked, harmless .props and was accepted and followed.
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["eng/shared.props"] = safe,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../eng/*.props" />
                </Project>
                """,
        }, "wildcard", "server/App.csproj Import Project=../eng/*.props");
        // A ? wildcard is a wildcard too.
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["eng/shared.props"] = safe,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../eng/shared.?rops" />
                </Project>
                """,
        }, "wildcard", "server/App.csproj Import Project=../eng/shared.?rops");
        // The uninspected wildcard match formerly in C1409_UninspectedImportTargetRefuses refuses as a wildcard.
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["eng/docs.items"] = items,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../eng/*.items" />
                </Project>
                """,
        }, "wildcard", "server/App.csproj Import Project=../eng/*.items");
    }

    // CARD-1409 repair (Review 23aedc52 D1): MSBuild splits an Import Project on ';' into several imports.
    [Test]
    public async Task C1409_SemicolonImportAuditsEveryPart()
    {
        // Before the repair the whole text is one wildcard pattern that matches nothing, so the tracked
        // eng/docs.items part is never inspected and -Update exits 0.
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["eng/docs.items"] = """
                <Project>
                  <ItemGroup>
                    <Content Include="../docs/**/*.md" />
                  </ItemGroup>
                </Project>
                """,
            ["eng/shared.props"] = """
                <Project>
                  <ItemGroup>
                    <None Include="notes.json" />
                  </ItemGroup>
                </Project>
                """,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../eng/docs.items;../eng/*.props" />
                </Project>
                """,
        }, "server/App.csproj Import Project=../eng/docs.items;../eng/*.props", "-> eng/docs.items");
        // Every part is followed: the second part's include is harmless from eng/x and selects docs/**/*.md
        // from the importing root project. Before the repair the list was one garbled missing path.
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["eng/a.props"] = """
                <Project>
                  <ItemGroup>
                    <None Include="notes.json" />
                  </ItemGroup>
                </Project>
                """,
            ["eng/x/docs.props"] = """
                <Project>
                  <ItemGroup>
                    <Content Include="docs/**/*.md" />
                  </ItemGroup>
                </Project>
                """,
            ["App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="eng/a.props;eng/x/docs.props" />
                </Project>
                """,
        }, "eng/x/docs.props Content Include=docs/**/*.md");
    }

    // CARD-1409 repair (Review 23aedc52 D3): the nearest Directory.Packages.props is imported into every
    // project and its includes resolve from the project folder, like Directory.Build.props.
    [Test]
    public async Task C1409_DirectoryPackagesPropsIsImportedByTheNearestProject()
    {
        // CARD-1409 repair 3 (Review 31c77116): a relative include that leaves the repository from the file's own
        // directory now refuses outright, so the import is witnessed through an Import the audit follows only when
        // the file is reached: eng/docs.items is not project XML and refuses as an uninspected import target.
        const string importsDocs = """
            <Project>
              <Import Project="eng/docs.items" />
            </Project>
            """;
        const string docsItems = """
            <Project>
              <ItemGroup>
                <None Include="../docs/**/*.md" />
              </ItemGroup>
            </Project>
            """;
        const string safe = """
            <Project>
              <ItemGroup>
                <PackageReference Include="Shouldly" Version="4.0.0" />
              </ItemGroup>
            </Project>
            """;
        const string project = """
            <Project Sdk="Microsoft.NET.Sdk">
            </Project>
            """;
        // The root file is the nearest Directory.Packages.props for server/App.csproj, so its import is followed.
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["Directory.Packages.props"] = importsDocs,
            ["eng/docs.items"] = docsItems,
            ["server/App.csproj"] = project,
        }, "Directory.Packages.props Import Project=eng/docs.items", "-> eng/docs.items");
        // Only the nearest file is imported: server/Directory.Packages.props shadows the root one, whose import is
        // never followed; the root file is still parsed against the allow-lists and counted.
        var nearest = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["Directory.Packages.props"] = importsDocs,
            ["eng/docs.items"] = docsItems,
            ["server/Directory.Packages.props"] = safe,
            ["server/sub/App.csproj"] = project,
        });
        await nearest.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(nearest, "en-US", "-Update");
            ran.Code.ShouldBe(0, ran.Stdout + ran.Stderr);
            ran.Stdout.ShouldContain("count=3");
            File.Exists(AuditManifest(nearest)).ShouldBeTrue();
        });
    }

    // CARD-1409 repair (Review 23aedc52 D4): a tracked <project>.user file is imported by its project, so the
    // audit parses it, follows its imports and counts it in the hashed inventory.
    [Test]
    public async Task C1409_ProjectUserFileIsAuditedAndImported()
    {
        const string project = """
            <Project Sdk="Microsoft.NET.Sdk">
            </Project>
            """;
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = project,
            ["server/App.csproj.user"] = """
                <Project>
                  <ItemGroup>
                    <Content Include="../docs/**/*.md" />
                  </ItemGroup>
                </Project>
                """,
        }, "server/App.csproj.user Content Include=../docs/**/*.md");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["eng/docs.items"] = """
                <Project>
                  <ItemGroup>
                    <Content Include="../docs/**/*.md" />
                  </ItemGroup>
                </Project>
                """,
            ["server/App.csproj"] = project,
            ["server/App.csproj.user"] = """
                <Project>
                  <Import Project="../eng/docs.items" />
                </Project>
                """,
        }, "server/App.csproj.user Import Project=../eng/docs.items", "-> eng/docs.items");
        var harmless = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = project,
            ["server/App.csproj.user"] = """
                <Project>
                  <PropertyGroup>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """,
        });
        await harmless.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(harmless, "en-US", "-Update");
            ran.Code.ShouldBe(0, ran.Stdout + ran.Stderr);
            ran.Stdout.ShouldContain("count=2");
            File.Exists(AuditManifest(harmless)).ShouldBeTrue();
        });
    }

    // CARD-1409 repair (Review 8989ece8 F1): Microsoft.Common.props/.targets import
    // $(MSBuildProjectExtensionsPath)$(MSBuildProjectFile).*.props and .*.targets (obj/ by default), which the
    // audit does not follow. A tracked definition file under any obj/ segment is refused by its path alone,
    // whatever it contains, and the server inventory already digests it.
    [Test]
    public async Task C1409_ObjExtensionDefinitionFileRefuses()
    {
        const string project = """
            <Project Sdk="Microsoft.NET.Sdk">
            </Project>
            """;
        const string docs = """
            <Project>
              <ItemGroup>
                <None Include="../docs/**/*.md" />
              </ItemGroup>
            </Project>
            """;
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = project,
            ["server/obj/App.csproj.evil.props"] = docs,
        }, "under an obj/ directory", "server/obj/App.csproj.evil.props");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = project,
            ["server/obj/App.csproj.evil.targets"] = docs,
        }, "under an obj/ directory", "server/obj/App.csproj.evil.targets");
        // Path-based: a harmless file, an upper-case segment and a nested project under obj/ refuse too.
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = project,
            ["server/OBJ/Debug/App.csproj.harmless.props"] = """
                <Project>
                  <PropertyGroup>
                    <Harmless>true</Harmless>
                  </PropertyGroup>
                </Project>
                """,
        }, "under an obj/ directory", "server/OBJ/Debug/App.csproj.harmless.props");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = project,
            ["obj/Generated.csproj"] = project,
        }, "under an obj/ directory", "obj/Generated.csproj");

        var graph = new LandVerificationBuildGraph();
        graph.Includes("server/obj/App.csproj.evil.props").ShouldBeTrue();
        graph.Includes("server/obj/App.csproj.evil.targets").ShouldBeTrue();
        graph.Includes("server/obj/project.assets.json").ShouldBeFalse();
    }

    // CARD-1409 repair (Review 8989ece8 F1): a project or imported definition file that redirects the
    // extension or intermediate directory cannot be evaluated, so it is refused wherever it is set.
    [Test]
    public async Task C1409_IntermediateOutputRedirectRefuses()
    {
        const string project = """
            <Project Sdk="Microsoft.NET.Sdk">
            </Project>
            """;
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <BaseIntermediateOutputPath>../build/obj/</BaseIntermediateOutputPath>
                  </PropertyGroup>
                </Project>
                """,
        }, "redirects the intermediate directory", "server/App.csproj BaseIntermediateOutputPath");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["Directory.Build.props"] = """
                <Project>
                  <PropertyGroup>
                    <MSBuildProjectExtensionsPath>$(MSBuildThisFileDirectory)ext/</MSBuildProjectExtensionsPath>
                  </PropertyGroup>
                </Project>
                """,
            ["server/App.csproj"] = project,
        }, "redirects the intermediate directory", "Directory.Build.props MSBuildProjectExtensionsPath");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["eng/shared.props"] = """
                <Project>
                  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
                    <intermediateoutputpath>$(MSBuildProjectDirectory)/tmp/</intermediateoutputpath>
                  </PropertyGroup>
                </Project>
                """,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../eng/shared.props" />
                </Project>
                """,
        }, "redirects the intermediate directory", "eng/shared.props intermediateoutputpath");
        // Reading the property is not setting it: the real Directory.Build.targets expands
        // $(IntermediateOutputPath) inside another property and stays approved.
        var reads = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["Directory.Build.targets"] = """
                <Project>
                  <PropertyGroup>
                    <_AntiphonCleanFileChars>$([System.IO.File]::ReadAllText('$(IntermediateOutputPath)$(CleanFile)').Length)</_AntiphonCleanFileChars>
                  </PropertyGroup>
                </Project>
                """,
            ["server/App.csproj"] = project,
        });
        await reads.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(reads, "en-US", "-Update");
            ran.Code.ShouldBe(0, ran.Stdout + ran.Stderr);
            ran.Stdout.ShouldContain("count=2");
            File.Exists(AuditManifest(reads)).ShouldBeTrue();
        });
    }

    // CARD-1409 repair (Review 31c77116 R1): a tracked .rsp file is parsed, not only hashed. Every
    // non-comment line must be one allow-listed switch; a property, an @file and an unknown switch refuse.
    [Test]
    public async Task C1409_ResponseFileSwitchesAreAllowListed()
    {
        const string project = """
            <Project Sdk="Microsoft.NET.Sdk">
            </Project>
            """;
        foreach (var line in new[] { "-p:BaseIntermediateOutputPath=ext/", "-p:_DirectoryBuildPropsFile=docs/build.md", "@docs/flags.md" })
        {
            await AssertUpdateRefusesAsync(new Dictionary<string, string>
            {
                ["Directory.Build.rsp"] = "# comment\n-nodeReuse:false\n" + line + "\n",
                ["server/App.csproj"] = project,
            }, "response file switch the audit does not allow", "Directory.Build.rsp " + line);
        }
        // Any tracked .rsp is parsed, whatever its name; a value outside the allowed set and a target switch refuse.
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["eng/build.rsp"] = "-property:CustomAfterMicrosoftCommonTargets=../docs/x.md\n",
            ["server/App.csproj"] = project,
        }, "response file switch the audit does not allow", "eng/build.rsp -property:CustomAfterMicrosoftCommonTargets=../docs/x.md");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["Directory.Build.rsp"] = "-nodeReuse:maybe\n",
            ["server/App.csproj"] = project,
        }, "response file switch the audit does not allow", "Directory.Build.rsp -nodeReuse:maybe");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["Directory.Build.rsp"] = "/t:Build\n",
            ["server/App.csproj"] = project,
        }, "response file switch the audit does not allow", "Directory.Build.rsp /t:Build");
        // The allowed switches, comments, blank lines, CRLF and a BOM are approved.
        var allowed = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["Directory.Build.rsp"] = "﻿# CARD-0589 D-2\r\n\r\n-nodeReuse:false\r\n-maxcpucount\r\n/m:4\r\n-verbosity:minimal\r\n-v:q\r\n-nologo\r\n",
            ["server/App.csproj"] = project,
        });
        await allowed.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(allowed, "en-US", "-Update");
            ran.Code.ShouldBe(0, ran.Stdout + ran.Stderr);
            ran.Stdout.ShouldContain("count=2");
            File.Exists(AuditManifest(allowed)).ShouldBeTrue();
        });
    }

    // CARD-1409 repair (Review 31c77116 R2): a property name outside the audit's allow-list refuses wherever it
    // is set, including one no review listed, so the design is default-deny rather than a name deny-list.
    [Test]
    public async Task C1409_UnknownPropertyRefuses()
    {
        const string project = """
            <Project Sdk="Microsoft.NET.Sdk">
            </Project>
            """;
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <CustomAfterMicrosoftCommonTargets>../docs/evil.md</CustomAfterMicrosoftCommonTargets>
                  </PropertyGroup>
                </Project>
                """,
        }, "property the audit does not allow", "server/App.csproj CustomAfterMicrosoftCommonTargets");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["Directory.Build.props"] = """
                <Project>
                  <PropertyGroup>
                    <CustomBeforeMicrosoftCommonProps>$(MSBuildThisFileDirectory)docs/evil.md</CustomBeforeMicrosoftCommonProps>
                  </PropertyGroup>
                </Project>
                """,
            ["server/App.csproj"] = project,
        }, "property the audit does not allow", "Directory.Build.props CustomBeforeMicrosoftCommonProps");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["eng/shared.props"] = """
                <Project>
                  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
                    <DirectoryBuildTargetsPath>../docs/evil.md</DirectoryBuildTargetsPath>
                  </PropertyGroup>
                </Project>
                """,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../eng/shared.props" />
                </Project>
                """,
        }, "property the audit does not allow", "eng/shared.props DirectoryBuildTargetsPath");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Target Name="Hook" BeforeTargets="Build">
                    <PropertyGroup>
                      <_DirectoryBuildTargetsFile>../docs/evil.md</_DirectoryBuildTargetsFile>
                    </PropertyGroup>
                  </Target>
                </Project>
                """,
        }, "property the audit does not allow", "server/App.csproj _DirectoryBuildTargetsFile");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = project,
            ["server/App.csproj.user"] = """
                <Project>
                  <PropertyGroup>
                    <BeforeMicrosoftNETSdkTargets>../docs/evil.md</BeforeMicrosoftNETSdkTargets>
                  </PropertyGroup>
                </Project>
                """,
        }, "property the audit does not allow", "server/App.csproj.user BeforeMicrosoftNETSdkTargets");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["Directory.Packages.props"] = """
                <Project>
                  <PropertyGroup>
                    <DirectoryPackagesPropsPath>docs/evil.md</DirectoryPackagesPropsPath>
                  </PropertyGroup>
                </Project>
                """,
            ["server/App.csproj"] = project,
        }, "property the audit does not allow", "Directory.Packages.props DirectoryPackagesPropsPath");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <PublishProfile>../docs/evil.md</PublishProfile>
                  </PropertyGroup>
                </Project>
                """,
        }, "property the audit does not allow", "server/App.csproj PublishProfile");
        // Not in any review's list: default-deny, not a deny-list. Case does not matter.
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <_antiphonFutureImportHook>../docs/evil.md</_antiphonFutureImportHook>
                  </PropertyGroup>
                </Project>
                """,
        }, "property the audit does not allow", "server/App.csproj _antiphonFutureImportHook");
        // A task Output that sets a property is setting it.
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Target Name="Hook" BeforeTargets="Build">
                    <Exec Command="echo ../docs/evil.md" ConsoleToMSBuild="true">
                      <Output TaskParameter="ConsoleOutput" PropertyName="CustomAfterMicrosoftCSharpTargets" />
                    </Exec>
                  </Target>
                </Project>
                """,
        }, "property the audit does not allow", "server/App.csproj CustomAfterMicrosoftCSharpTargets");
        // Allow-listed names are approved in any letter case.
        var allowed = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <nullable>enable</nullable>
                    <AssemblyName>App</AssemblyName>
                    <RootNamespace>App</RootNamespace>
                  </PropertyGroup>
                </Project>
                """,
        });
        await allowed.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(allowed, "en-US", "-Update");
            ran.Code.ShouldBe(0, ran.Stdout + ran.Stderr);
            ran.Stdout.ShouldContain("count=1");
            File.Exists(AuditManifest(allowed)).ShouldBeTrue();
        });
    }

    // CARD-1409 repair (Review 31c77116 R3): every item type is classified. Analyzer-config and file items follow the
    // docs-markdown rule, a ProjectReference must name a tracked project file, a package-style include is a name, and an
    // item type outside the allow-list refuses by its type.
    [Test]
    public async Task C1409_UnknownItemTypeRefuses()
    {
        const string project = """
            <Project Sdk="Microsoft.NET.Sdk">
            </Project>
            """;
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["docs/style.md"] = "# style",
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <EditorConfigFiles Include="../docs/style.md" />
                  </ItemGroup>
                </Project>
                """,
        }, "includes docs markdown", "server/App.csproj EditorConfigFiles Include=../docs/style.md");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["Directory.Build.props"] = """
                <Project>
                  <ItemGroup>
                    <GlobalAnalyzerConfigFiles Include="$(MSBuildThisFileDirectory)docs/analyzers.md" />
                  </ItemGroup>
                </Project>
                """,
            ["server/App.csproj"] = project,
        }, "includes docs markdown", "Directory.Build.props GlobalAnalyzerConfigFiles Include=$(MSBuildThisFileDirectory)docs/analyzers.md");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["docs/x.md"] = "# x",
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="../docs/x.md" />
                  </ItemGroup>
                </Project>
                """,
        }, "project reference is not a tracked project file", "server/App.csproj ProjectReference Include=../docs/x.md -> docs/x.md");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="../lib/Missing.csproj" />
                  </ItemGroup>
                </Project>
                """,
        }, "project reference is not a tracked project file", "server/App.csproj ProjectReference Include=../lib/Missing.csproj -> lib/Missing.csproj");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["lib/Lib.csproj"] = project,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="../lib/*.csproj" />
                  </ItemGroup>
                </Project>
                """,
        }, "item include cannot be classified", "server/App.csproj ProjectReference Include=../lib/*.csproj");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="../docs/x.md" Version="1.0.0" />
                  </ItemGroup>
                </Project>
                """,
        }, "item include cannot be classified", "server/App.csproj PackageReference Include=../docs/x.md");
        // Types outside the allow-list refuse by type, whatever they include.
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Reference Include="Evil" HintPath="../docs/x.md" />
                  </ItemGroup>
                </Project>
                """,
        }, "item type the audit does not allow", "server/App.csproj Reference");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["eng/shared.targets"] = """
                <Project>
                  <Target Name="Hook" BeforeTargets="Build">
                    <ItemGroup>
                      <_AntiphonFutureFiles Include="$(MSBuildThisFileDirectory)../docs/**/*.txt" />
                    </ItemGroup>
                  </Target>
                </Project>
                """,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../eng/shared.targets" />
                </Project>
                """,
        }, "item type the audit does not allow", "eng/shared.targets _AntiphonFutureFiles");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Target Name="Hook" BeforeTargets="Build">
                    <MSBuild Projects="$(MSBuildProjectFullPath)" Targets="Other">
                      <Output TaskParameter="TargetOutputs" ItemName="_AntiphonFutureOutputs" />
                    </MSBuild>
                  </Target>
                </Project>
                """,
        }, "item type the audit does not allow", "server/App.csproj _AntiphonFutureOutputs");
        // The allow-listed types with well-formed includes are approved.
        var allowed = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["lib/Lib.csproj"] = project,
            ["server/.editorconfig"] = "root = true",
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="../lib/Lib.csproj" />
                    <PackageReference Include="Shouldly" Version="4.3.0" />
                    <FrameworkReference Include="Microsoft.AspNetCore.App" />
                    <InternalsVisibleTo Include="App.Tests" />
                    <EditorConfigFiles Include=".editorconfig" />
                    <GlobalAnalyzerConfigFiles Include="$(MSBuildThisFileDirectory)analyzers.globalconfig" />
                    <Compile Include="../lib/Shared.cs" Link="Shared.cs" />
                    <None Include="slow-tests-allowlist.txt" CopyToOutputDirectory="PreserveNewest" />
                  </ItemGroup>
                </Project>
                """,
        });
        await allowed.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(allowed, "en-US", "-Update");
            ran.Code.ShouldBe(0, ran.Stdout + ran.Stderr);
            ran.Stdout.ShouldContain("count=2");
            File.Exists(AuditManifest(allowed)).ShouldBeTrue();
        });
    }

    // CARD-1409 repair (Review 31c77116, default-deny): a construct the audit cannot classify refuses: a Project
    // child element or SDK outside the allow-list, an item reference inside an include, and a file-item include
    // that leaves the repository.
    [Test]
    public async Task C1409_UnclassifiedConstructRefuses()
    {
        const string project = """
            <Project Sdk="Microsoft.NET.Sdk">
            </Project>
            """;
        foreach (var (element, body) in new[]
        {
            ("UsingTask", "<UsingTask TaskName=\"Evil\" AssemblyFile=\"../docs/evil.dll\" />"),
            ("ImportGroup", "<ImportGroup><Import Project=\"../eng/a.props\" /></ImportGroup>"),
            ("Choose", "<Choose><When Condition=\"true\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></When></Choose>"),
            ("ItemDefinitionGroup", "<ItemDefinitionGroup><Compile><Visible>false</Visible></Compile></ItemDefinitionGroup>"),
            ("ProjectExtensions", "<ProjectExtensions><VisualStudio /></ProjectExtensions>"),
        })
        {
            await AssertUpdateRefusesAsync(new Dictionary<string, string>
            {
                ["eng/a.props"] = "<Project />",
                ["server/App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\">" + body + "</Project>",
            }, "element the audit does not classify", "server/App.csproj " + element);
        }
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Evil.Sdk/1.0.0">
                </Project>
                """,
        }, "SDK the audit does not allow", "server/App.csproj Sdk=Evil.Sdk/1.0.0");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Sdk Name="Evil.Sdk" Version="1.0.0" />
                </Project>
                """,
        }, "SDK the audit does not allow", "server/App.csproj Sdk=Evil.Sdk");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Content Include="@(Docs)" />
                  </ItemGroup>
                </Project>
                """,
        }, "item include cannot be classified", "server/App.csproj Content Include=@(Docs)");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <None Include="%(Docs.FullPath)" />
                  </ItemGroup>
                </Project>
                """,
        }, "item include cannot be classified", "server/App.csproj None Include=%(Docs.FullPath)");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Include="../../outside/Shared.cs" Link="Shared.cs" />
                  </ItemGroup>
                </Project>
                """,
        }, "item include cannot be classified", "server/App.csproj Compile Include=../../outside/Shared.cs");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <None Include="/etc/hosts" />
                  </ItemGroup>
                </Project>
                """,
        }, "item include cannot be classified", "server/App.csproj None Include=/etc/hosts");
        // The real repository's shapes are approved: the Web SDK, the Aspire SDK element, a target with Exec
        // and task outputs, a conditional import and a %(RecursiveDir) link.
        var allowed = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["tests/Shared/Repeat.targets"] = """
                <Project>
                  <Target Name="Repeat" BeforeTargets="CoreCompile">
                    <ItemGroup>
                      <Compile Include="$(MSBuildThisFileDirectory)RepeatEvidence.cs" />
                    </ItemGroup>
                  </Target>
                </Project>
                """,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <Sdk Name="Aspire.AppHost.Sdk" Version="9.3.0" />
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                    <SourceRevisionId Condition="'$(SourceRevisionId)' == ''">unknown</SourceRevisionId>
                  </PropertyGroup>
                  <ItemGroup>
                    <Content Include="..\Fixtures\**\*" Link="Fixtures\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
                  </ItemGroup>
                  <Import Project="$(MSBuildThisFileDirectory)../tests/Shared/Repeat.targets" Condition="'$(AntiphonCheckpointRepeat)' != ''" />
                  <Target Name="SetSourceRevisionIdFromGit" BeforeTargets="InitializeSourceControlInformation">
                    <Exec Command="git rev-parse HEAD" ConsoleToMSBuild="true" ContinueOnError="true">
                      <Output TaskParameter="ConsoleOutput" PropertyName="_GitHeadSha" />
                    </Exec>
                    <PropertyGroup>
                      <_GitHeadShaTrimmed>$([System.String]::Copy('$(_GitHeadSha)').Trim())</_GitHeadShaTrimmed>
                    </PropertyGroup>
                  </Target>
                </Project>
                """,
        });
        await allowed.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(allowed, "en-US", "-Update");
            ran.Code.ShouldBe(0, ran.Stdout + ran.Stderr);
            ran.Stdout.ShouldContain("count=2");
            File.Exists(AuditManifest(allowed)).ShouldBeTrue();
        });
    }

    // CARD-1409 repair 4 (Review 937bb1c9 F1): a docs glob is classified by what it resolves to, not by
    // sample names. Every wildcard pattern whose first segment can be docs/ refuses, a pattern is expanded
    // against the tracked tree and a non-docs markdown or text match outside the allow-list refuses too.
    [Test]
    public async Task C1409_DocsGlobRefusesByResolvedPath()
    {
        foreach (var glob in new[] { "../docs/guide.*", "../docs/g*", "../docs/*guide*", "../docs/guide.m?", "../docs/**/guide*", "../docs/s*/**/*.md", "../docs/future/*.md", "../**/*.md" })
        {
            await AssertUpdateRefusesAsync(new Dictionary<string, string>
            {
                ["docs/guide.md"] = "# guide",
                ["docs/sub/s.md"] = "# s",
                ["server/App.csproj"] = $"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <ItemGroup>
                        <EmbeddedResource Include="{glob}" />
                      </ItemGroup>
                    </Project>
                    """,
            }, "includes docs markdown", "server/App.csproj EmbeddedResource Include=" + glob);
        }

        // A glob outside docs/ that selects a tracked markdown file the allow-list does not name refuses by the match.
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/fixtures/notes.md"] = "# notes",
            ["server/fixtures/a.jsonl"] = "{}",
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Content Include="fixtures/**" />
                  </ItemGroup>
                </Project>
                """,
        }, "markdown or text document", "server/App.csproj Content Include=fixtures/** -> server/fixtures/notes.md");

        // The real repository's embedded markdown, its sibling allow-list files and a fixture glob with no document stay approved.
        var allowed = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["server/Bundles/a.md"] = "# a",
            ["server/Bundles/README.md"] = "# readme",
            ["server/Fixtures/x.jsonl"] = "{}",
            ["server/slow-tests-allowlist.txt"] = "",
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <EmbeddedResource Include="Bundles\*.md" Exclude="Bundles\README.md" />
                    <Content Include="Fixtures\*" />
                    <None Include="slow-tests-allowlist.txt" CopyToOutputDirectory="PreserveNewest" />
                  </ItemGroup>
                </Project>
                """,
        });
        await allowed.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(allowed, "en-US", "-Update");
            ran.Code.ShouldBe(0, ran.Stdout + ran.Stderr);
            ran.Stdout.ShouldContain("count=1");
        });
    }

    // CARD-1409 repair 4 (Review 937bb1c9 F1, real repository): widening the card-0449 captures include of
    // tests/Antiphon.Tests/Antiphon.Tests.csproj to '...card-0449-claude-effort-dialog-*' selects the
    // readiness plan markdown and refuses; the unchanged file passes in the same scratch tree.
    [Test]
    public async Task C1409_RealTestProjectWidenedToPlanGlobRefuses()
    {
        const string captures = "../../docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-captures.json";
        const string widened = "../../docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-*";
        var files = await BuildRealTestProjectTreeAsync();
        var real = files[RealTestProjectPath];
        real.ShouldContain(captures);
        await AssertUpdateApprovesAsync(files, DefinitionCount(files));

        files[RealTestProjectPath] = real.Replace(captures, widened, StringComparison.Ordinal);
        await AssertUpdateRefusesAsync(files, "includes docs markdown",
            RealTestProjectPath + " EmbeddedResource Include=" + widened,
            "docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-readiness-plan.md");
    }

    // CARD-1483 (Review cff23aba D-1): the S1 helper metadata is allowed only as the exact tree literal, in
    // attribute form, on an Include. HelperTargetFramework reaches an MSBuild task Properties string, which
    // splits on ';', so 'net10.0;CustomBeforeMicrosoftCommonTargets=...docs...' would import a docs file.
    // The _StagedHelperFramework property that carries it is pinned to its two tree values the same way.
    // The real tree approves; each other shape refuses naming the metadata or the property.
    [Test]
    [Arguments("tree")]
    [Arguments("htf-semicolon")]
    [Arguments("htf-docs-import")]
    [Arguments("htf-net9")]
    [Arguments("htf-leading-space")]
    [Arguments("htf-case")]
    [Arguments("htf-child")]
    [Arguments("htf-remove")]
    [Arguments("skip-false")]
    [Arguments("skip-semicolon")]
    [Arguments("stf-net9")]
    [Arguments("stf-semicolon")]
    [Arguments("stf-child")]
    [Arguments("prop-toplevel")]
    [Arguments("prop-intarget")]
    [Arguments("prop-output")]
    public async Task C1483_PinnedHelperMetadataRefusesOtherShapes(string shape)
    {
        const string helper = """<StagedHelper Include="$(MSBuildThisFileDirectory)..\..\tools\Antiphon.LandGraphAudit\Antiphon.LandGraphAudit.csproj" Folder="land-graph-audit" Glob="**\*" """;
        const string htf = """HelperTargetFramework="net10.0" />""";
        const string skip = """SkipGetTargetFrameworkProperties="true" """;
        const string stf = """SetTargetFramework="TargetFramework=net10.0" />""";
        const string helperLabel = @"StagedHelper Include=$(MSBuildThisFileDirectory)..\..\tools\Antiphon.LandGraphAudit\Antiphon.LandGraphAudit.csproj";
        const string evil = @"net10.0;CustomBeforeMicrosoftCommonTargets=$(MSBuildThisFileDirectory)..\..\docs\evil.md";
        const string inTarget = "<_StagedHelperFramework>%(StagedHelper.HelperTargetFramework)</_StagedHelperFramework>";
        const string referenceLabel = @"ProjectReference Include=..\..\tools\Antiphon.LandGraphAudit\Antiphon.LandGraphAudit.csproj";
        var files = await BuildRealTestProjectTreeAsync();
        var real = files[RealTestProjectPath];
        real.ShouldContain(helper + htf);
        real.ShouldContain(skip + stf);
        real.ShouldContain(inTarget);
        if (shape == "tree")
        {
            await AssertUpdateApprovesAsync(files, DefinitionCount(files));
            return;
        }

        string HelperValue(string value) => real.Replace(helper + htf, helper + "HelperTargetFramework=\"" + value + "\" />", StringComparison.Ordinal);
        string ReferenceValue(string name, string value, string tail) =>
            real.Replace(skip + stf, name == "skip" ? "SkipGetTargetFrameworkProperties=\"" + value + "\" " + stf : skip + "SetTargetFramework=\"" + value + "\"" + tail, StringComparison.Ordinal);
        var (edited, label, name) = shape switch
        {
            "htf-semicolon" => (HelperValue("net10.0;X=Y"), helperLabel, "HelperTargetFramework"),
            "htf-docs-import" => (HelperValue(evil), helperLabel, "HelperTargetFramework"),
            "htf-net9" => (HelperValue("net9.0"), helperLabel, "HelperTargetFramework"),
            "htf-leading-space" => (HelperValue(" net10.0"), helperLabel, "HelperTargetFramework"),
            "htf-case" => (HelperValue("NET10.0"), helperLabel, "HelperTargetFramework"),
            "htf-child" => (real.Replace(helper + htf, helper + "><HelperTargetFramework>net10.0</HelperTargetFramework></StagedHelper>", StringComparison.Ordinal), helperLabel, "HelperTargetFramework"),
            "htf-remove" => (real.Replace("</Project>", """  <ItemGroup><StagedHelper Remove="$(MSBuildThisFileDirectory)..\..\tools\Antiphon.LandGraphAudit\Antiphon.LandGraphAudit.csproj" HelperTargetFramework="net10.0" /></ItemGroup>""" + "\n</Project>", StringComparison.Ordinal),
                @"StagedHelper Remove=$(MSBuildThisFileDirectory)..\..\tools\Antiphon.LandGraphAudit\Antiphon.LandGraphAudit.csproj", "HelperTargetFramework"),
            "skip-false" => (ReferenceValue("skip", "false", ""), referenceLabel, "SkipGetTargetFrameworkProperties"),
            "skip-semicolon" => (ReferenceValue("skip", "true;X=Y", ""), referenceLabel, "SkipGetTargetFrameworkProperties"),
            "stf-net9" => (ReferenceValue("stf", "TargetFramework=net9.0", " />"), referenceLabel, "SetTargetFramework"),
            "stf-semicolon" => (ReferenceValue("stf", @"TargetFramework=net10.0;CustomBeforeMicrosoftCommonTargets=$(MSBuildThisFileDirectory)..\..\docs\evil.md", " />"), referenceLabel, "SetTargetFramework"),
            "stf-child" => (real.Replace(skip + stf, skip + "><SetTargetFramework>TargetFramework=net10.0</SetTargetFramework></ProjectReference>", StringComparison.Ordinal), referenceLabel, "SetTargetFramework"),
            "prop-toplevel" => (real.Replace("<IsTestProject>", "<_StagedHelperFramework>" + evil + "</_StagedHelperFramework><IsTestProject>", StringComparison.Ordinal), "", "_StagedHelperFramework"),
            "prop-intarget" => (real.Replace(inTarget, "<_StagedHelperFramework>" + evil + "</_StagedHelperFramework>", StringComparison.Ordinal), "", "_StagedHelperFramework"),
            "prop-output" => (real.Replace(inTarget, inTarget + "</PropertyGroup><CreateProperty Value=\"" + evil + "\"><Output TaskParameter=\"Value\" PropertyName=\"_StagedHelperFramework\" /></CreateProperty><PropertyGroup>", StringComparison.Ordinal), "", "_StagedHelperFramework"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
        edited.ShouldNotBe(real);
        files[RealTestProjectPath] = edited;
        files["docs/evil.md"] = "# evil";
        if (label.Length == 0)
            await AssertUpdateRefusesAsync(files, "value the audit does not allow", RealTestProjectPath + " " + name);
        else
            await AssertUpdateRefusesAsync(files, "metadata the audit does not allow", RealTestProjectPath + " " + label + " " + name);
    }

    private const string RealTestProjectPath = "tests/Antiphon.Tests/Antiphon.Tests.csproj";

    // The V-20 scratch tree: the real tests/Antiphon.Tests project, the real root Directory.Build.* files and
    // tests/Shared/CheckpointRepeat.targets, a stub project at every ProjectReference and the two card-0449 docs files.
    private static async Task<Dictionary<string, string>> BuildRealTestProjectTreeAsync()
    {
        var repo = DelegateScriptRunner.RepoRoot;
        var real = await File.ReadAllTextAsync(Path.Combine(repo, RealTestProjectPath.Replace('/', Path.DirectorySeparatorChar)));
        var files = new Dictionary<string, string>
        {
            ["Directory.Build.props"] = await File.ReadAllTextAsync(Path.Combine(repo, "Directory.Build.props")),
            ["Directory.Build.targets"] = await File.ReadAllTextAsync(Path.Combine(repo, "Directory.Build.targets")),
            ["Directory.Build.rsp"] = await File.ReadAllTextAsync(Path.Combine(repo, "Directory.Build.rsp")),
            ["tests/Shared/CheckpointRepeat.targets"] = await File.ReadAllTextAsync(Path.Combine(repo, "tests", "Shared", "CheckpointRepeat.targets")),
            ["docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-captures.json"] = "{}",
            ["docs/superpowers/plans/2026-09-08-card-0449-claude-effort-dialog-readiness-plan.md"] = "# plan",
        };
        foreach (Match reference in Regex.Matches(real, "<ProjectReference Include=\"([^\"]+)\""))
        {
            var relative = reference.Groups[1].Value.Replace('\\', '/');
            var stack = new List<string> { "tests", "Antiphon.Tests" };
            foreach (var part in relative.Split('/'))
            {
                if (part == "..") stack.RemoveAt(stack.Count - 1);
                else if (part != ".") stack.Add(part);
            }
            files[string.Join('/', stack)] = """<Project Sdk="Microsoft.NET.Sdk"></Project>""";
        }
        files[RealTestProjectPath] = real;
        return files;
    }

    private static int DefinitionCount(IReadOnlyDictionary<string, string> files)
    {
        var graph = new LandVerificationBuildGraph();
        return files.Keys.Count(graph.Includes);
    }

    // CARD-1409 repair 4 (Review 937bb1c9 F2): a $(...) in a file-item include is expanded from the
    // definition files' own property values and the result is classified; a property the audit cannot
    // resolve refuses by name, and a build-output or package-cache root is accepted only without a '..' tail.
    [Test]
    public async Task C1409_PropertyDrivenIncludeIsResolved()
    {
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["docs/guide.md"] = "# guide",
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Description>../docs/**/*.md</Description>
                  </PropertyGroup>
                  <ItemGroup>
                    <EmbeddedResource Include="$(Description)" />
                  </ItemGroup>
                </Project>
                """,
        }, "includes docs markdown", "server/App.csproj EmbeddedResource Include=$(Description)");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["docs/guide.md"] = "# guide",
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <RootNamespace>../do</RootNamespace>
                  </PropertyGroup>
                  <ItemGroup>
                    <EmbeddedResource Include="$(RootNamespace)cs/*.md" />
                  </ItemGroup>
                </Project>
                """,
        }, "includes docs markdown", "server/App.csproj EmbeddedResource Include=$(RootNamespace)cs/*.md");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["docs/guide.md"] = "# guide",
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <EmbeddedResource Include="$(MSBuildStartupDirectory)/docs/**" />
                  </ItemGroup>
                </Project>
                """,
        }, "cannot resolve", "server/App.csproj EmbeddedResource Include=$(MSBuildStartupDirectory)/docs/** $(MSBuildStartupDirectory)");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Target Name="Gen">
                    <ItemGroup>
                      <Compile Include="$(IntermediateOutputPath)../../docs/guide.md" />
                    </ItemGroup>
                  </Target>
                </Project>
                """,
        }, "cannot be classified", "server/App.csproj Compile Include=$(IntermediateOutputPath)../../docs/guide.md");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Content Include="$(PkgSome_Package)\build\a.dll" />
                  </ItemGroup>
                </Project>
                """,
        }, "cannot resolve", "server/App.csproj Content Include=$(PkgSome_Package)\\build\\a.dll $(PkgSome_Package)");

        // The real repository's shapes: an intermediate-output file, a GeneratePathProperty package root, an
        // MSBuild task output transform with a metadata glob, and GetDirectoryName over a task output property.
        var allowed = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["lib/Lib.csproj"] = """<Project Sdk="Microsoft.NET.Sdk"></Project>""",
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Some.Package" Version="1.0.0" GeneratePathProperty="true" />
                    <Content Include="$(PkgSome_Package)\runtimes\win-x64\native\a.dll" />
                    <StagedHelper Include="$(MSBuildThisFileDirectory)../lib/Lib.csproj" Folder="lib" Glob="**\*" />
                  </ItemGroup>
                  <Target Name="Stage">
                    <MSBuild Projects="@(StagedHelper)" Targets="GetTargetPath">
                      <Output TaskParameter="TargetOutputs" ItemName="_StagedHelperTarget" />
                    </MSBuild>
                    <MSBuild Projects="../lib/Lib.csproj" Targets="GetTargetPath">
                      <Output TaskParameter="TargetOutputs" PropertyName="FakeBrowserTargetPath" />
                    </MSBuild>
                    <PropertyGroup>
                      <_StagedHelperDir>@(_StagedHelperTarget->'%(RootDir)%(Directory)')</_StagedHelperDir>
                      <_StagedHelperGlob>%(StagedHelper.Glob)</_StagedHelperGlob>
                      <FakeBrowserOutputDir>$([System.IO.Path]::GetDirectoryName('$(FakeBrowserTargetPath)'))</FakeBrowserOutputDir>
                      <_AntiphonRepeatFile>$(IntermediateOutputPath)CheckpointRepeat.g.cs</_AntiphonRepeatFile>
                    </PropertyGroup>
                    <ItemGroup>
                      <_StagedHelperFiles Include="$(_StagedHelperDir)$(_StagedHelperGlob)" />
                      <FakeBrowserFile Include="$(FakeBrowserOutputDir)/Antiphon.MarkdownPdf.FakeBrowser.*" />
                      <Compile Include="$(_AntiphonRepeatFile)" />
                    </ItemGroup>
                  </Target>
                </Project>
                """,
        });
        await allowed.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(allowed, "en-US", "-Update");
            ran.Code.ShouldBe(0, ran.Stdout + ran.Stderr);
            ran.Stdout.ShouldContain("count=2");
        });
    }

    // CARD-1409 repair 4 (Review 937bb1c9 F3): item metadata names are allow-listed per item type; a
    // ProjectReference global-property metadata (AdditionalProperties, Properties) and any unknown name refuse.
    [Test]
    public async Task C1409_ReferenceMetadataIsAllowListed()
    {
        const string lib = """<Project Sdk="Microsoft.NET.Sdk"></Project>""";
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["lib/Lib.csproj"] = lib,
            ["docs/build.md"] = "# build",
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="../lib/Lib.csproj" AdditionalProperties="CustomAfterMicrosoftCommonTargets=$(MSBuildThisFileDirectory)../docs/build.md" />
                  </ItemGroup>
                </Project>
                """,
        }, "metadata the audit does not allow", "server/App.csproj ProjectReference Include=../lib/Lib.csproj AdditionalProperties");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["lib/Lib.csproj"] = lib,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="../lib/Lib.csproj">
                      <Properties>CustomAfterMicrosoftCommonTargets=../docs/build.md</Properties>
                    </ProjectReference>
                  </ItemGroup>
                </Project>
                """,
        }, "metadata the audit does not allow", "server/App.csproj ProjectReference Include=../lib/Lib.csproj Properties");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Shouldly" Version="4.3.0" SetConfiguration="Configuration=Docs" />
                  </ItemGroup>
                </Project>
                """,
        }, "metadata the audit does not allow", "server/App.csproj PackageReference Include=Shouldly SetConfiguration");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Include="A.cs" DependentUpon="../docs/x.md" />
                  </ItemGroup>
                </Project>
                """,
        }, "metadata the audit does not allow", "server/App.csproj Compile Include=A.cs DependentUpon");

        var allowed = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["lib/Lib.csproj"] = lib,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="../lib/Lib.csproj" Aliases="global,lib" ReferenceOutputAssembly="false" Private="false" />
                    <PackageReference Include="Shouldly" Version="4.3.0" PrivateAssets="all" ExcludeAssets="runtime">
                      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
                    </PackageReference>
                    <Compile Include="../lib/Shared.cs" Link="Shared.cs" />
                    <Content Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="Never" Link="cfg/appsettings.json" />
                    <EmbeddedResource Include="Runtime/x.json" LogicalName="X.json" />
                    <None Include="icon.png" Pack="true" PackagePath="" />
                    <Content Update="Runtime/y.json">
                      <TargetPath>y.json</TargetPath>
                    </Content>
                  </ItemGroup>
                </Project>
                """,
        });
        await allowed.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(allowed, "en-US", "-Update");
            ran.Code.ShouldBe(0, ran.Stdout + ran.Stderr);
            ran.Stdout.ShouldContain("count=2");
        });
    }

    // CARD-1409 repair 4 (Review 937bb1c9, disclosed D-18 items closed cheaply): a property whose value is a
    // docs path, or that reads a file from anywhere but the build output through a property function, refuses.
    [Test]
    public async Task C1409_PropertyValueNamingDocsRefuses()
    {
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["docs/guide.md"] = "# guide",
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Description>$([System.IO.File]::ReadAllText('$(MSBuildThisFileDirectory)../docs/guide.md'))</Description>
                  </PropertyGroup>
                </Project>
                """,
        }, "property value", "server/App.csproj Description", "ReadAllText");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["docs/guide.md"] = "# guide",
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <PackageReadmeFile>../docs/guide.md</PackageReadmeFile>
                  </PropertyGroup>
                </Project>
                """,
        }, "property value", "server/App.csproj PackageReadmeFile", "docs/guide.md");
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["Directory.Build.props"] = """
                <Project>
                  <PropertyGroup>
                    <Description>$([System.IO.Directory]::GetFileSystemEntries('docs'))</Description>
                  </PropertyGroup>
                </Project>
                """,
            ["server/App.csproj"] = """<Project Sdk="Microsoft.NET.Sdk"></Project>""",
        }, "property value", "Directory.Build.props Description", "GetFileSystemEntries");

        // The real repository's shapes: a ledger read under the intermediate directory, a package readme beside
        // its project, a description with a slash, and a self-referencing exclude list stay approved.
        var allowed = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["Directory.Build.props"] = """
                <Project>
                  <PropertyGroup>
                    <DefaultItemExcludes>$(DefaultItemExcludes);workspace/**;bin-*/**</DefaultItemExcludes>
                  </PropertyGroup>
                </Project>
                """,
            ["Directory.Build.targets"] = """
                <Project>
                  <Target Name="Ledger" BeforeTargets="Build">
                    <PropertyGroup>
                      <_AntiphonCleanFileChars>$([System.IO.File]::ReadAllText('$(IntermediateOutputPath)$(CleanFile)').Length)</_AntiphonCleanFileChars>
                    </PropertyGroup>
                  </Target>
                </Project>
                """,
            ["src/Pack.props"] = """
                <Project>
                  <PropertyGroup>
                    <PackageReadmeFile>README.md</PackageReadmeFile>
                    <Description>Typed Kafka producer/consumer client for the bridge.</Description>
                  </PropertyGroup>
                  <ItemGroup>
                    <None Include="README.md" Pack="true" PackagePath="" />
                  </ItemGroup>
                </Project>
                """,
            ["src/Lib/README.md"] = "# lib",
            ["src/Lib/Lib.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../Pack.props" />
                </Project>
                """,
        });
        await allowed.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(allowed, "en-US", "-Update");
            ran.Code.ShouldBe(0, ran.Stdout + ran.Stderr);
            ran.Stdout.ShouldContain("count=4");
        });
    }

    // CARD-1409 repair 5 (Review f0fb9035 F1, D-28): the Windows land host strips trailing dots and spaces per
    // segment and opens '::$DATA' as the file itself, so a resolved segment that would change under the host's
    // rules refuses naming the segment; the unchanged real test project still passes in the V-20 scratch tree.
    [Test]
    public async Task C1409_WindowsPathNormalisationRefusesBySegment()
    {
        foreach (var (include, segment) in new[]
        {
            ("../README.md.", "README.md."),
            ("../docs./guide.md.", "docs."),
            ("../docs./*.md", "docs."),
            ("../README.md::$DATA", "README.md::$DATA"),
            ("../README.md. .", "README.md. ."),
        })
        {
            await AssertUpdateRefusesAsync(new Dictionary<string, string>
            {
                ["README.md"] = "# root",
                ["docs/guide.md"] = "# guide",
                ["server/App.csproj"] = $"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <ItemGroup>
                        <EmbeddedResource Include="{include}" />
                      </ItemGroup>
                    </Project>
                    """,
            }, "Windows land host", "server/App.csproj EmbeddedResource Include=" + include, "segment=" + segment);
        }

        var files = await BuildRealTestProjectTreeAsync();
        var real = files[RealTestProjectPath];
        await AssertUpdateApprovesAsync(files, DefinitionCount(files));
        files[RealTestProjectPath] = real.Replace("</Project>",
            "  <ItemGroup><EmbeddedResource Include=\"../../README.md.\" /></ItemGroup>\n</Project>", StringComparison.Ordinal);
        await AssertUpdateRefusesAsync(files, "Windows land host",
            RealTestProjectPath + " EmbeddedResource Include=../../README.md.", "segment=README.md.");

        await AssertUpdateApprovesAsync(new Dictionary<string, string>
        {
            ["lib/Shared.cs"] = "// shared",
            ["server/Fixtures/x.jsonl"] = "{}",
            ["server/slow-tests-allowlist.txt"] = "",
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Compile Include="..\lib\Shared.cs" Link="Shared.cs" />
                    <Content Include="Fixtures\*" />
                    <None Include="slow-tests-allowlist.txt" CopyToOutputDirectory="PreserveNewest" />
                  </ItemGroup>
                </Project>
                """,
        }, 1);
    }

    // CARD-1409 repair 5 (Review f0fb9035 F2, D-29): an item Update sets metadata too, so its values join the
    // census that %(Item.Metadata) and the Pkg* path properties expand; the real StagedHelper Update climbs out
    // of the output root and refuses, the real **\* glob and a PackageReference Update stay approved.
    [Test]
    public async Task C1409_ItemUpdateMetadataFeedsTheCensus()
    {
        var files = await BuildRealTestProjectTreeAsync();
        files[RealTestProjectPath] = files[RealTestProjectPath].Replace("</Project>",
            """  <ItemGroup><StagedHelper Update="$(MSBuildThisFileDirectory)..\..\src\Antiphon.FakeGrok\Antiphon.FakeGrok.csproj" Glob="..\..\..\..\..\docs\**\*.md" /></ItemGroup>"""
            + "\n</Project>", StringComparison.Ordinal);
        await AssertUpdateRefusesAsync(files, "cannot be classified",
            RealTestProjectPath + " _StagedHelperFiles Include=$(_StagedHelperDir)$(_StagedHelperGlob)");

        const string lib = """<Project Sdk="Microsoft.NET.Sdk"></Project>""";
        static string Staged(string updateGlob) => $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <StagedHelper Include="$(MSBuildThisFileDirectory)../lib/Lib.csproj" Folder="lib" Glob="*" />
                <PackageReference Include="Some.Package" Version="1.0.0" />
                <PackageReference Update="Some.Package" GeneratePathProperty="true" />
                <Content Include="$(PkgSome_Package)\build\a.dll" />
              </ItemGroup>
              <Target Name="Stage">
                <ItemGroup>
                  <StagedHelper Update="$(MSBuildThisFileDirectory)../lib/Lib.csproj" Glob="{{updateGlob}}" />
                </ItemGroup>
                <MSBuild Projects="@(StagedHelper)" Targets="GetTargetPath">
                  <Output TaskParameter="TargetOutputs" ItemName="_StagedHelperTarget" />
                </MSBuild>
                <PropertyGroup>
                  <_StagedHelperDir>@(_StagedHelperTarget->'%(RootDir)%(Directory)')</_StagedHelperDir>
                  <_StagedHelperGlob>%(StagedHelper.Glob)</_StagedHelperGlob>
                </PropertyGroup>
                <ItemGroup>
                  <_StagedHelperFiles Include="$(_StagedHelperDir)$(_StagedHelperGlob)" />
                </ItemGroup>
              </Target>
            </Project>
            """;
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["docs/guide.md"] = "# guide",
            ["lib/Lib.csproj"] = lib,
            ["server/App.csproj"] = Staged(@"..\..\docs\**\*.md"),
        }, "cannot be classified", "server/App.csproj _StagedHelperFiles Include=$(_StagedHelperDir)$(_StagedHelperGlob)");
        await AssertUpdateApprovesAsync(new Dictionary<string, string>
        {
            ["lib/Lib.csproj"] = lib,
            ["server/App.csproj"] = Staged(@"**\*"),
        }, 2);
    }

    // CARD-1409 repair 5 (D-31, V-26): every row of the plan's normaliser (N-) and item-operation (O-) tables has
    // a witness. A 'table' row runs here with the row's outcome; a named row must name an existing C1409_ method;
    // an entry without a row fails too, so the tables and this test move together.
    [Test]
    public async Task C1409_NormaliserAndItemOperationTablesHaveWitnesses()
    {
        var plan = await File.ReadAllTextAsync(Path.Combine(DelegateScriptRunner.RepoRoot, "docs", "superpowers", "plans",
            "2026-10-22-card-1409-import-fail-closed-plan.md"));
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match row in Regex.Matches(plan, @"^\| ((?:N|O)-\d+) \|(.*)\|\s*$", RegexOptions.Multiline))
        {
            var cells = row.Groups[2].Value.Split('|');
            rows[row.Groups[1].Value] = cells[^1].Trim().Trim('`');
        }
        rows.Count.ShouldBeGreaterThan(0, "the plan has no N-/O- table rows");
        var witnesses = TableWitnesses();
        foreach (var (id, witness) in rows)
        {
            if (witness == "table")
            {
                witnesses.ContainsKey(id).ShouldBeTrue($"plan row {id} has no table witness in this test");
                continue;
            }
            witness.StartsWith("C1409_", StringComparison.Ordinal).ShouldBeTrue($"plan row {id} names witness {witness}");
            typeof(LandNonCodeSkipTests).GetMethod(witness).ShouldNotBeNull($"plan row {id} names a missing witness {witness}");
        }
        foreach (var id in witnesses.Keys)
            rows.ContainsKey(id).ShouldBeTrue($"table witness {id} has no plan row");

        foreach (var (id, run) in witnesses.OrderBy(w => w.Key, StringComparer.Ordinal))
        {
            try
            {
                await run();
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"table row {id} failed: {e.Message}", e);
            }
        }
    }

    // CARD-1409 repair 5 (Review f0fb9035 disclosure, D-30): a tracked symbolic link a path item reaches as a
    // file or passes through as a directory refuses naming the link; a link elsewhere changes nothing.
    [Test]
    public async Task C1409_TrackedSymlinkUnderItemPatternRefuses()
    {
        static Dictionary<string, string> Tree(string include) => new()
        {
            ["docs/guide.md"] = "# guide",
            ["server/App.csproj"] = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <Content Include="{include}" />
                  </ItemGroup>
                </Project>
                """,
        };
        foreach (var include in new[] { "fixtures/**", "fixtures/guide.md" })
        {
            await AssertUpdateRefusesWithEntriesAsync(Tree(include), [("120000", "server/fixtures", "../docs")],
                "symbolic link", "server/App.csproj Content Include=" + include + " -> server/fixtures");
        }
        await AssertUpdateRefusesWithEntriesAsync(Tree("*.jsonl"), [("120000", "server/notes.jsonl", "../docs/guide.md")],
            "symbolic link", "server/App.csproj Content Include=*.jsonl -> server/notes.jsonl");
        var control = Tree("fixtures/**");
        control["server/fixtures/a.jsonl"] = "{}";
        await AssertUpdateApprovesAsync(control, 1, [("120000", "tools/link", "../docs")]);
    }

    // CARD-1409 repair 6 (Review c64987ea F1, D-33): MSBuild expands properties and metadata, splits on ';' and
    // decodes %XX once, in that order; the audit runs that one pipeline for every path text, so an escape a
    // property value splices in reaches the README and docs rules instead of staying a literal segment.
    [Test]
    public async Task C1409_EscapeInExpandedTextIsDecodedOnce()
    {
        var files = await BuildRealTestProjectTreeAsync();
        var real = files[RealTestProjectPath];
        await AssertUpdateApprovesAsync(files, DefinitionCount(files));
        files[RealTestProjectPath] = real.Replace("</Project>",
            "  <PropertyGroup><PackageTags>$(RootNamespace)/%2E%2E/%2E%2E/%2E%2E/README.md</PackageTags></PropertyGroup>\n"
            + "  <ItemGroup><None Include=\"$(PackageTags)\" /></ItemGroup>\n</Project>", StringComparison.Ordinal);
        await AssertUpdateRefusesAsync(files, "includes docs markdown",
            RealTestProjectPath + " None Include=$(PackageTags) -> README.md");

        static Dictionary<string, string> Fixture(string item) => new()
        {
            ["README.md"] = "# root",
            ["docs/data.json"] = "{}",
            ["server/App.csproj"] = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Description>sub</Description>
                    <PackageTags>$(Description)/%2E%2E/%2E%2E/README.md</PackageTags>
                    <PackageReadmeFile>$(Description)/%2E%2E/%2E%2E/%64ocs/data.json</PackageReadmeFile>
                  </PropertyGroup>
                  <ItemGroup>
                    {item}
                  </ItemGroup>
                </Project>
                """,
        };
        await AssertUpdateRefusesAsync(Fixture("""<None Include="$(PackageTags)" />"""),
            "includes docs markdown", "server/App.csproj None Include=$(PackageTags) -> README.md");
        await AssertUpdateRefusesAsync(Fixture("""<EmbeddedResource Include="$(PackageReadmeFile)" />"""),
            "includes docs markdown", "server/App.csproj EmbeddedResource Include=$(PackageReadmeFile) -> docs/data.json");
    }

    // CARD-1409 repair 7 (Review 7db88932 R1, D-36): the D-25 skip of a value part that carries an item, metadata
    // or property-function reference is per part, as it was before repair 6, so every other part of the same
    // value is still classified; a value whose only part is a reference is left alone, as at fd20e8dcc.
    [Test]
    public async Task C1409_PropertyValuePartWithReferenceSkipsOnlyThatPart()
    {
        var files = await BuildRealTestProjectTreeAsync();
        files["docs/guide.md"] = "# guide";
        var real = files[RealTestProjectPath];
        await AssertUpdateApprovesAsync(files, DefinitionCount(files));
        foreach (var reference in new[] { "@(None)", "$([System.String]::Copy(''))", "%(None.Identity)" })
        {
            files[RealTestProjectPath] = real.Replace("</Project>",
                "  <PropertyGroup><PackageReadmeFile>../../docs/guide.md;" + reference + "</PackageReadmeFile></PropertyGroup>\n</Project>",
                StringComparison.Ordinal);
            await AssertUpdateRefusesAsync(files, "property value", RealTestProjectPath + " PackageReadmeFile -> docs/guide.md");
        }

        static Dictionary<string, string> Fixture(string value) => new()
        {
            ["docs/guide.md"] = "# guide",
            ["server/App.csproj"] = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <PackageTags>{value}</PackageTags>
                  </PropertyGroup>
                </Project>
                """,
        };
        // The reference may come first: the part after it is still classified.
        await AssertUpdateRefusesAsync(Fixture("@(None);../docs/guide.md"), "property value", "server/App.csproj PackageTags -> docs/guide.md");
        await AssertUpdateRefusesAsync(Fixture("%(None.Identity);../docs/guide.md"), "property value", "server/App.csproj PackageTags -> docs/guide.md");
        // A value whose only part is a reference, and a value whose other part is not a document, stay approved.
        await AssertUpdateApprovesAsync(Fixture("@(None)"), 1);
        await AssertUpdateApprovesAsync(Fixture("%(None.Identity)"), 1);
        await AssertUpdateApprovesAsync(Fixture("$([System.String]::Copy('../docs/guide.md'))"), 1);
        await AssertUpdateApprovesAsync(Fixture("@(None);src/x.json"), 1);
    }

    // CARD-1409 repair 8 (Review d43d1e4a R2, D-39): MSBuild expands a property wherever it stands and splits
    // afterwards, so a later ';'-part that starts with a directory property is that directory's path, as at
    // fd20e8dcc, for a property value and an import alike; a later part with any other reference is left as it was.
    [Test]
    public async Task C1409_LaterPartDirectoryPropertyIsExpanded()
    {
        var files = await BuildRealTestProjectTreeAsync();
        files["docs/guide.md"] = "# guide";
        var real = files[RealTestProjectPath];
        foreach (var directory in new[] { "$(MSBuildThisFileDirectory)", "$(MSBuildProjectDirectory)/" })
        {
            files[RealTestProjectPath] = real.Replace("</Project>",
                "  <PropertyGroup><PackageReadmeFile>x.json;" + directory + "../../docs/guide.md</PackageReadmeFile></PropertyGroup>\n</Project>",
                StringComparison.Ordinal);
            await AssertUpdateRefusesAsync(files, "property value", RealTestProjectPath + " PackageReadmeFile -> docs/guide.md");
        }

        static Dictionary<string, string> Fixture(string value, string import = "") => new()
        {
            ["docs/guide.md"] = "# guide",
            ["eng/shared.props"] = "<Project />",
            ["server/App.csproj"] = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Description>sub</Description>
                    <PackageTags>{value}</PackageTags>
                  </PropertyGroup>
                  {import}
                </Project>
                """,
        };
        await AssertUpdateRefusesAsync(Fixture("src/x.json;$(MSBuildThisFileDirectory)../docs/guide.md"),
            "property value", "server/App.csproj PackageTags -> docs/guide.md");
        await AssertUpdateRefusesAsync(Fixture("src/x.json;$(MSBuildProjectDirectory)/../docs/guide.md"),
            "property value", "server/App.csproj PackageTags -> docs/guide.md");
        // A later part with any other property reference is left alone, as at fd20e8dcc.
        await AssertUpdateApprovesAsync(Fixture("src/x.json;$(Description)/../../docs/guide.md"), 2);
        await AssertUpdateApprovesAsync(Fixture("src/x.json;$(Undefined)../docs/guide.md"), 2);
        // An import's later part is resolved from its directory property and followed.
        await AssertUpdateRefusesAsync(Fixture("x", """<Import Project="../eng/shared.props;$(MSBuildThisFileDirectory)../eng/missing.props" />"""),
            "import target is not a tracked project file",
            "server/App.csproj Import Project=../eng/shared.props;$(MSBuildThisFileDirectory)../eng/missing.props -> eng/missing.props");
        await AssertUpdateApprovesAsync(Fixture("x", """<Import Project="../eng/shared.props;$(MSBuildProjectDirectory)/../eng/shared.props" />"""), 2);
        await AssertUpdateRefusesAsync(Fixture("x", """<Import Project="../eng/shared.props;$(Description)/../eng/shared.props" />"""),
            "import cannot be resolved");
    }

    // CARD-1409 repair 9 (Review 4ecdb753 R3, plan D-42): MSBuild stores $(MSBuildProjectDirectory) escaped, so a
    // folder named a%2Fb is not a/b; a definition path holding a character MSBuild escapes refuses by name.
    [Test]
    public async Task C1409_DefinitionPathWithEscapedCharacterRefuses()
    {
        static Dictionary<string, string> Project(string path, string value, string import = "", string? extra = null)
        {
            var files = new Dictionary<string, string>
            {
                ["docs/guide.md"] = "# guide",
                [path] = "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                    (value.Length == 0 ? "" : "  <PropertyGroup><PackageReadmeFile>" + value + "</PackageReadmeFile></PropertyGroup>\n") +
                    import + "\n</Project>\n",
            };
            if (extra is not null) files[extra] = "<Project />";
            return files;
        }
        const string projectDirectory = "$(MSBuildProjectDirectory)/../../docs/guide.md";
        const string refusal = "definition file path holds a character MSBuild escapes";
        // The R3 shapes: approved at e6ac1c606, refused at fd20e8dcc, and real MSBuild selects the root docs/guide.md.
        await AssertUpdateRefusesAsync(Project("server/a%2Fb/App.csproj", projectDirectory), refusal, "server/a%2Fb/App.csproj char=%");
        await AssertUpdateRefusesAsync(Project("server/a%2Fb/App.csproj", "x.json;" + projectDirectory), refusal, "server/a%2Fb/App.csproj char=%");
        await AssertUpdateRefusesAsync(Project("server/a%2fb/App.csproj", projectDirectory), refusal, "server/a%2fb/App.csproj char=%");
        // A folder holding a reference: approved at e6ac1c606, refused at fd20e8dcc (the repair 9 sweep).
        await AssertUpdateRefusesAsync(Project("server/a$(Description)b/App.csproj", projectDirectory), refusal, "server/a$(Description)b/App.csproj char=$");
        await AssertUpdateRefusesAsync(Project("server/a@(None)b/App.csproj", projectDirectory), refusal, "server/a@(None)b/App.csproj char=@");
        // An imported file in such a folder refuses by its own path.
        await AssertUpdateRefusesAsync(Project("server/App.csproj", "", """  <Import Project="../eng/a%252Fb/shared.props" />""", "eng/a%2Fb/shared.props"),
            refusal, "eng/a%2Fb/shared.props char=%");
        // Controls with their fd20e8dcc outcomes: a real server/ab/c/ folder reads server/docs/guide.md, and the
        // unescaped $(MSBuildThisFileDirectory) form in the a%2Fb folder still refuses.
        await AssertUpdateApprovesAsync(Project("server/ab/c/App.csproj", projectDirectory), 1);
        await AssertUpdateRefusesAsync(Project("server/a%2Fb/App.csproj", "$(MSBuildThisFileDirectory)../../docs/guide.md"), "server/a%2Fb/App.csproj");
    }

    // One entry per 'table' row of the plan's N- and O-tables (D-28, D-29). Keys are the row ids.
    private static Dictionary<string, Func<Task>> TableWitnesses()
    {
        const string lib = """<Project Sdk="Microsoft.NET.Sdk"></Project>""";
        static string Project(string body) => "<Project Sdk=\"Microsoft.NET.Sdk\">\n" + body + "\n</Project>\n";
        static string Items(string items) => Project("  <ItemGroup>\n    " + items + "\n  </ItemGroup>");
        static string Target(string body) => Project("  <Target Name=\"T\">\n    " + body + "\n  </Target>");
        // A StagedHelper fixture: the declarations, the task that fills _StagedHelperTarget, and the real
        // $(_StagedHelperDir)$(_StagedHelperGlob) include that %(StagedHelper.Glob) feeds.
        static string Staged(string declarations, string fill = """<MSBuild Projects="@(StagedHelper)" Targets="GetTargetPath"><Output TaskParameter="TargetOutputs" ItemName="_StagedHelperTarget" /></MSBuild>""") =>
            Project("  <ItemGroup>\n    " + declarations + "\n  </ItemGroup>\n  <Target Name=\"T\">\n    " + fill +
                "\n    <PropertyGroup><_StagedHelperDir>@(_StagedHelperTarget->'%(RootDir)%(Directory)')</_StagedHelperDir><_StagedHelperGlob>%(StagedHelper.Glob)</_StagedHelperGlob></PropertyGroup>" +
                "\n    <ItemGroup><_StagedHelperFiles Include=\"$(_StagedHelperDir)$(_StagedHelperGlob)\" /></ItemGroup>\n  </Target>");
        static Dictionary<string, string> Tree(string project, params (string Path, string Content)[] extra)
        {
            var files = new Dictionary<string, string>
            {
                ["docs/guide.md"] = "# guide",
                ["README.md"] = "# root",
                ["server/App.csproj"] = project,
            };
            foreach (var (path, content) in extra) files[path] = content;
            return files;
        }
        static Task Refuses(string project, params string[] needles) => AssertUpdateRefusesAsync(Tree(project), needles);
        static Task Approves(string project, int count) => AssertUpdateApprovesAsync(Tree(project), count);
        const string stagedFiles = "_StagedHelperFiles Include=$(_StagedHelperDir)$(_StagedHelperGlob)";

        return new Dictionary<string, Func<Task>>(StringComparer.Ordinal)
        {
            ["N-1"] = async () =>
            {
                await Refuses(Items("""<EmbeddedResource Include="../docs./guide.md" />"""), "Windows land host", "server/App.csproj EmbeddedResource Include=../docs./guide.md", "segment=docs.");
                await Refuses(Items("""<EmbeddedResource Include="../README.md." />"""), "Windows land host", "segment=README.md.");
            },
            ["N-2"] = () => Refuses(Items("""<EmbeddedResource Include="../README.md. ." />"""), "Windows land host", "segment=README.md. ."),
            ["N-3"] = () => Refuses(Items("""<EmbeddedResource Include="../docs/.../guide.md" />"""), "Windows land host", "segment=..."),
            ["N-4"] = async () =>
            {
                await Refuses(Items("""<EmbeddedResource Include="../README.md::$DATA" />"""), "Windows land host", "segment=README.md::$DATA");
                await Refuses(Items("""<EmbeddedResource Include="../docs/guide.md:stream" />"""), "Windows land host", "segment=guide.md:stream");
                await AssertUpdateRefusesAsync(Tree(Project("""  <Import Project="../eng/shared.props:x" />"""), ("eng/shared.props", "<Project />")),
                    "Windows land host", "server/App.csproj Import Project=../eng/shared.props:x", "segment=shared.props:x");
            },
            ["N-5"] = async () =>
            {
                await Refuses(Items("""<EmbeddedResource Include="../DOCS~1/guide.md" />"""), "Windows land host", "segment=DOCS~1");
                // Untracked on purpose: a tracked LIB~1.csproj is refused earlier as a definition path (N-15).
                await Refuses(Items("""<ProjectReference Include="../lib/LIB~1.csproj" />"""),
                    "Windows land host", "server/App.csproj ProjectReference Include=../lib/LIB~1.csproj", "segment=LIB~1.csproj");
            },
            ["N-6"] = () => Refuses(Items("""<EmbeddedResource Include="../DOCS/Guide.MD" />"""), "includes docs markdown", "server/App.csproj EmbeddedResource Include=../DOCS/Guide.MD"),
            ["N-7"] = () => Refuses(Items("""<EmbeddedResource Include="..\docs\guide.md" />"""), "includes docs markdown", "server/App.csproj EmbeddedResource Include=..\\docs\\guide.md"),
            ["N-8"] = () => Refuses(Items("""<EmbeddedResource Include=".//../docs/./guide.md" />"""), "includes docs markdown", "server/App.csproj EmbeddedResource Include=.//../docs/./guide.md"),
            ["N-9"] = async () =>
            {
                await Refuses(Items("""<EmbeddedResource Include="sub/../../docs/guide.md" />"""), "includes docs markdown", "Include=sub/../../docs/guide.md");
                await Refuses(Items("""<EmbeddedResource Include="../../outside.md" />"""), "cannot be classified", "Include=../../outside.md");
                await Refuses(Items("""<EmbeddedResource Include="../docs/*/../guide.md" />"""), "includes docs markdown", "Include=../docs/*/../guide.md");
            },
            ["N-10"] = async () =>
            {
                await Refuses(Items("""<EmbeddedResource Include="C:/repo/README.md" />"""), "cannot be classified", "Include=C:/repo/README.md");
                await Refuses(Items("""<EmbeddedResource Include="C:README.md" />"""), "Windows land host", "segment=C:README.md");
            },
            ["N-11"] = () => Refuses(Target("""<ItemGroup><Compile Include="$(IntermediateOutputPath)docs./x.md" /></ItemGroup>"""),
                "Windows land host", "server/App.csproj Compile Include=$(IntermediateOutputPath)docs./x.md", "segment=docs."),
            ["N-12"] = () => AssertUpdateRefusesWithEntriesAsync(Tree(Items("""<Content Include="fixtures/**" />""")), [("100644", "server/fixtures/notes.md.", "# notes")],
                "Windows land host", "server/App.csproj Content Include=fixtures/** -> server/fixtures/notes.md.", "segment=notes.md."),
            ["N-13"] = () => Refuses(Items("""<EmbeddedResource Include="Bundles\*.md" Exclude="Bundles\README.md." />"""),
                "Windows land host", "server/App.csproj EmbeddedResource Exclude=Bundles\\README.md.", "segment=README.md."),
            ["N-14"] = () => Refuses(Project("""  <PropertyGroup><PackageReadmeFile>../docs./guide.md</PackageReadmeFile></PropertyGroup>"""),
                "property value", "server/App.csproj PackageReadmeFile", "segment=docs."),
            ["N-15"] = () => AssertUpdateRefusesWithEntriesAsync(Tree(Items("""<Compile Include="A.cs" />""")), [("100644", "server/obj./evil.props", "<Project />")],
                "Windows land host", "server/obj./evil.props", "segment=obj."),
            ["O-1"] = () => Refuses(Items("""<EmbeddedResource Include="../docs/guide.md" LogicalName="g" />"""), "includes docs markdown", "server/App.csproj EmbeddedResource Include=../docs/guide.md"),
            ["O-2"] = () => Refuses(Target("""<ItemGroup><Compile Include="../docs/guide.md" /></ItemGroup>"""), "includes docs markdown", "server/App.csproj Compile Include=../docs/guide.md"),
            ["O-3"] = async () =>
            {
                await AssertUpdateApprovesAsync(Tree(Items("""<Content Include="fixtures/**" Exclude="fixtures/*.md" />"""), ("server/fixtures/notes.md", "# n"), ("server/fixtures/a.jsonl", "{}")), 1);
                await AssertUpdateApprovesAsync(Tree(Items("""<Content Include="Fixtures\*" Exclude="../docs/**;$(Undefined)" />"""), ("server/Fixtures/x.jsonl", "{}")), 1);
            },
            ["O-4"] = () => Refuses(Items("""<Content Update="../docs/guide.md" />"""), "includes docs markdown", "server/App.csproj Content Include=../docs/guide.md"),
            ["O-5"] = () => Refuses(Target("""<ItemGroup><Content Update="../docs/guide.md" /></ItemGroup>"""), "includes docs markdown", "server/App.csproj Content Include=../docs/guide.md"),
            ["O-6"] = async () =>
            {
                await AssertUpdateRefusesAsync(Tree(Staged("""<StagedHelper Include="$(MSBuildThisFileDirectory)../lib/Lib.csproj" Folder="lib" Glob="*" /><StagedHelper Update="$(MSBuildThisFileDirectory)../lib/Lib.csproj" Glob="../../docs/**/*.md" />"""), ("lib/Lib.csproj", lib)),
                    "cannot be classified", "server/App.csproj " + stagedFiles);
                await Approves(Items("""<PackageReference Include="Some.Package" Version="1.0.0" /><PackageReference Update="Some.Package" GeneratePathProperty="true" /><Content Include="$(PkgSome_Package)\build\a.dll" />"""), 1);
            },
            ["O-7"] = async () =>
            {
                await Approves(Items("""<None Remove="../docs/**/*.md" />"""), 1);
                await Refuses(Items("""<None Remove="x.json" Pack="true" />"""), "metadata the audit does not allow", "server/App.csproj None Remove=x.json Pack");
            },
            ["O-8"] = () => Approves(Target("""<ItemGroup><_StagedHelperTarget Remove="@(_StagedHelperTarget)" /></ItemGroup>"""), 1),
            ["O-9"] = async () =>
            {
                await Refuses(Target("""<ItemGroup><Compile Include="A.cs" KeepMetadata="Link" /></ItemGroup>"""), "operation the audit does not evaluate", "server/App.csproj Compile Include=A.cs KeepMetadata");
                await Refuses(Target("""<ItemGroup><Compile Include="A.cs" RemoveMetadata="Link" /></ItemGroup>"""), "operation the audit does not evaluate", "server/App.csproj Compile Include=A.cs RemoveMetadata");
            },
            ["O-10"] = () => Refuses(Target("""<ItemGroup><Compile Include="A.cs" KeepDuplicates="false" /></ItemGroup>"""), "operation the audit does not evaluate", "server/App.csproj Compile Include=A.cs KeepDuplicates"),
            ["O-11"] = async () =>
            {
                await Refuses(Target("""<ItemGroup><None Remove="@(None)" MatchOnMetadata="Link" /></ItemGroup>"""), "operation the audit does not evaluate", "server/App.csproj None Remove=@(None) MatchOnMetadata");
                await Refuses(Target("""<ItemGroup><None Remove="@(None)" MatchOnMetadataOptions="CaseInsensitive" /></ItemGroup>"""), "operation the audit does not evaluate", "server/App.csproj None Remove=@(None) MatchOnMetadataOptions");
            },
            ["O-12"] = () => Refuses(Items("""<Content Include="@(Docs->'%(FullPath)')" />"""), "cannot be classified", "server/App.csproj Content Include=@(Docs->'%(FullPath)')"),
            ["O-13"] = () => Refuses(Items("""<None Update="%(Docs.FullPath)" />"""), "cannot be classified", "server/App.csproj None Include=%(Docs.FullPath)"),
            ["O-14"] = async () =>
            {
                await Refuses(Items("""<Compile Include="A.cs" DependentUpon="x.md" />"""), "metadata the audit does not allow", "server/App.csproj Compile Include=A.cs DependentUpon");
                await Approves(Items("""<Compile Include="A.cs" Link="B.cs" />"""), 1);
            },
            ["O-15"] = async () =>
            {
                await AssertUpdateRefusesAsync(Tree(Items("""<ProjectReference Include="../lib/Lib.csproj"><Properties>X=1</Properties></ProjectReference>"""), ("lib/Lib.csproj", lib)),
                    "metadata the audit does not allow", "server/App.csproj ProjectReference Include=../lib/Lib.csproj Properties");
                await Approves(Items("""<PackageReference Include="Shouldly" Version="4.3.0"><IncludeAssets>runtime</IncludeAssets></PackageReference>"""), 1);
            },
            ["O-16"] = async () =>
            {
                await Refuses(Project("""  <ItemGroup Condition="'$(Never)' == 'true'"><EmbeddedResource Include="../docs/guide.md" /></ItemGroup>"""), "includes docs markdown", "Include=../docs/guide.md");
                await Refuses(Items("""<EmbeddedResource Include="../docs/guide.md" Condition="false" />"""), "includes docs markdown", "Include=../docs/guide.md");
            },
            ["O-17"] = () => Refuses(Items("""<Compile Link="x.cs" />"""), "operation the audit does not evaluate", "server/App.csproj Compile Include= (none)"),
            ["O-18"] = () => Refuses(Project("""  <ItemDefinitionGroup><StagedHelper><Glob>../../docs/**/*.md</Glob></StagedHelper></ItemDefinitionGroup>"""), "element the audit does not classify", "server/App.csproj ItemDefinitionGroup"),
            ["O-19"] = async () =>
            {
                await Refuses(Target("""<Exec Command="echo"><Output TaskParameter="ConsoleOutput" ItemName="_AntiphonFutureOutputs" /></Exec>"""), "item type the audit does not allow", "server/App.csproj _AntiphonFutureOutputs");
                await AssertUpdateRefusesAsync(Tree(Staged("""<StagedHelper Include="$(MSBuildThisFileDirectory)../lib/Lib.csproj" Folder="lib" Glob="*" />""",
                        """<Exec Command="echo"><Output TaskParameter="ConsoleOutput" ItemName="_StagedHelperTarget" /></Exec>"""), ("lib/Lib.csproj", lib)),
                    "cannot resolve", "server/App.csproj " + stagedFiles);
            },
            ["O-20"] = () => Refuses(Items("""<Content Include="a.json" Foo="bar" />"""), "metadata the audit does not allow", "server/App.csproj Content Include=a.json Foo"),
            ["O-21"] = async () =>
            {
                await AssertUpdateRefusesAsync(Tree(Staged("""<StagedHelper Include="$(MSBuildThisFileDirectory)../lib/Lib.csproj" Folder="lib" Glob="%(Filename)" />"""), ("lib/Lib.csproj", lib)),
                    "cannot resolve", "server/App.csproj " + stagedFiles, "%(Filename)");
                await AssertUpdateRefusesAsync(Tree(Staged("""<StagedHelper Include="$(MSBuildThisFileDirectory)../lib/Lib.csproj" Folder="lib" Glob="$(Undefined)" />"""), ("lib/Lib.csproj", lib)),
                    "cannot resolve", "server/App.csproj " + stagedFiles, "$(Undefined)");
            },
            // CARD-1409 repair 6 (Review c64987ea F1, D-33): the escape table. Every path text is expanded, split on
            // ';' and decoded once, in MSBuild's order; N-16 is its own case (C1409_EscapeInExpandedTextIsDecodedOnce).
            ["N-17"] = () => Refuses(Items("""<EmbeddedResource Include="%2E%2E%2Fdocs%5Cguide.md" />"""), "includes docs markdown", "server/App.csproj EmbeddedResource Include=%2E%2E%2Fdocs%5Cguide.md -> docs/guide.md"),
            ["N-18"] = async () =>
            {
                await Refuses(Items("""<EmbeddedResource Include="../docs/guide.md%3Bx.json" />"""), "includes docs markdown", "Include=../docs/guide.md%3Bx.json -> docs/guide.md;x.json");
                await Refuses(Project("""  <PropertyGroup><RootNamespace>a.json%3B..</RootNamespace></PropertyGroup><ItemGroup><EmbeddedResource Include="$(RootNamespace)/docs/guide.md" /></ItemGroup>"""),
                    "Windows land host", "Include=$(RootNamespace)/docs/guide.md", "segment=a.json;..");
                await Refuses(Project("""  <PropertyGroup><RootNamespace>a.json;..</RootNamespace></PropertyGroup><ItemGroup><EmbeddedResource Include="$(RootNamespace)/docs/guide.md" /></ItemGroup>"""),
                    "includes docs markdown", "Include=$(RootNamespace)/docs/guide.md -> docs/guide.md");
            },
            ["N-19"] = async () =>
            {
                await Approves(Items("""<EmbeddedResource Include="%252E%252E/%252E%252E/docs/guide.json" />"""), 1);
                await Refuses(Items("""<EmbeddedResource Include="%2E%2E/docs/guide.md" />"""), "includes docs markdown", "Include=%2E%2E/docs/guide.md -> docs/guide.md");
            },
            ["N-20"] = () => AssertUpdateRefusesAsync(Tree(Staged("""<StagedHelper Include="$(MSBuildThisFileDirectory)../lib/Lib.csproj" Folder="lib" Glob="%2E%2E/%2E%2E/docs/**/*.md" />"""), ("lib/Lib.csproj", lib)),
                "cannot be classified", "server/App.csproj " + stagedFiles),
            ["N-21"] = async () =>
            {
                await Refuses(Target("""<PropertyGroup><FakeBrowserOutputDir>$([System.IO.Path]::GetDirectoryName('$(FakeBrowserTargetPath)%2E%2E'))</FakeBrowserOutputDir></PropertyGroup><ItemGroup><FakeBrowserFile Include="$(FakeBrowserOutputDir)/x.dll" /></ItemGroup>"""),
                    "cannot resolve", "server/App.csproj FakeBrowserFile Include=$(FakeBrowserOutputDir)/x.dll");
                await Refuses(Project("""  <PropertyGroup><_AntiphonCleanFileChars>$([System.IO.File]::ReadAllText('$(IntermediateOutputPath)%2E%2E/%2E%2E/docs/guide.md').Length)</_AntiphonCleanFileChars></PropertyGroup>"""),
                    "property value", "server/App.csproj _AntiphonCleanFileChars ReadAllText(");
            },
            ["N-22"] = () => Refuses(Items("""<EmbeddedResource Include="%2e%2e/docs/guide.md" />"""), "includes docs markdown", "Include=%2e%2e/docs/guide.md -> docs/guide.md"),
            ["N-23"] = async () =>
            {
                await Refuses(Items("""<EmbeddedResource Include="../docs/g%GGide.md" />"""), "includes docs markdown", "-> docs/g%GGide.md");
                await Refuses(Items("""<EmbeddedResource Include="../docs/guide.md%2" />"""), "includes docs markdown", "-> docs/guide.md%2");
                await Refuses(Items("""<EmbeddedResource Include="../docs/guide.md%" />"""), "includes docs markdown", "-> docs/guide.md%");
            },
            ["N-24"] = async () =>
            {
                await Refuses(Items("""<None Include="a%00.json" />"""), "cannot be classified", "server/App.csproj None Include=a%00.json");
                await AssertUpdateRefusesAsync(Tree(Project("""  <Import Project="../eng/shared%00.props" />"""), ("eng/shared.props", "<Project />")),
                    "import cannot be resolved", "server/App.csproj Import Project=../eng/shared%00.props");
            },
            ["N-25"] = async () =>
            {
                await Refuses(Items("""<EmbeddedResource Include="../docs/%2A.md" />"""), "includes docs markdown", "Include=../docs/%2A.md");
                await AssertUpdateRefusesAsync(Tree(Items("""<Content Include="fixtures/**" Exclude="fixtures/%2A.md" />"""), ("server/fixtures/notes.md", "# n")),
                    "markdown or text document", "server/App.csproj Content Include=fixtures/** -> server/fixtures/notes.md");
            },
            ["N-26"] = async () =>
            {
                await Refuses(Items("""<EmbeddedResource Include="../%24(X)/../docs/guide.md" />"""), "includes docs markdown", "Include=../%24(X)/../docs/guide.md -> docs/guide.md");
                await Refuses(Project("""  <PropertyGroup><Description>%24(Y)</Description></PropertyGroup><ItemGroup><EmbeddedResource Include="../$(Description)/../docs/guide.md" /></ItemGroup>"""),
                    "includes docs markdown", "Include=../$(Description)/../docs/guide.md -> docs/guide.md");
            },
            ["N-27"] = async () =>
            {
                await AssertUpdateApprovesAsync(Tree(Project("""  <Import Project="%2E%2E/eng/%73hared.props" /><ItemGroup><ProjectReference Include="../lib/%4Cib.csproj" /></ItemGroup>"""),
                    ("eng/shared.props", "<Project />"), ("lib/Lib.csproj", lib)), 3);
                await AssertUpdateRefusesAsync(Tree(Project("""  <Import Project="../eng/shared.props%3Bx.props" />"""), ("eng/shared.props", "<Project />")),
                    "not a tracked project file", "server/App.csproj Import Project=../eng/shared.props%3Bx.props -> eng/shared.props;x.props");
            },
            ["N-28"] = async () =>
            {
                await Refuses(Project("""  <PropertyGroup><PackageReadmeFile>%2E%2E/docs/guide.md</PackageReadmeFile></PropertyGroup>"""), "property value", "server/App.csproj PackageReadmeFile", "-> docs/guide.md");
                await Refuses(Project("""  <PropertyGroup><PackageReadmeFile>%2E%2E%2Fdocs%2Fguide%2Emd</PackageReadmeFile></PropertyGroup>"""), "property value", "server/App.csproj PackageReadmeFile", "-> docs/guide.md");
            },
            ["N-29"] = async () =>
            {
                await Refuses(Items("""<PackageReference Include="Some%2FPackage" Version="1.0.0" />"""), "cannot be classified", "server/App.csproj PackageReference Include=Some%2FPackage");
                await Approves(Items("""<PackageReference Include="Some%2EPackage" Version="1.0.0" GeneratePathProperty="true" /><Content Include="$(PkgSome_Package)\build\a.dll" />"""), 1);
            },
        };
    }

    [Test]
    public async Task C1409_ProjectUserFileIsInTheHashedInventory()
    {
        var graph = new LandVerificationBuildGraph();
        graph.Includes("server/Antiphon.Server.csproj.user").ShouldBeTrue();
        graph.Includes("tests/Example/Example.vbproj.user").ShouldBeTrue();
        graph.Includes("tests/Example/Example.CSPROJ.USER").ShouldBeTrue();
        graph.Includes("server/settings.user").ShouldBeFalse();
        graph.Includes("Antiphon.sln.user").ShouldBeFalse();
        graph.Includes("server/Antiphon.Server.csproj.user.bak").ShouldBeFalse();

        // A tracked .user file already on the target drifts the digest, so a docs-only land builds.
        await using var h = await CreateAsync();
        await h.AddDocsOnlySourceAsync();
        await h.Fixture.PushIndependentAsync("user-file", "server/Antiphon.Server.csproj.user",
            "<Project><ItemGroup><EmbeddedResource Include=\"../docs/**/*.md\" /></ItemGroup></Project>");
        await LandAndAssertAsync(h, build: true);
        (await h.OperationAsync())!.VerificationCommand.ShouldNotBeNull().ShouldContain("unrecognized-build-graph");
    }

    [Test]
    public async Task C1390_MissingNulFailsUnderNonInvariantCulture()
    {
        var root = await InitAuditRepositoryAsync("<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n");
        await root.RunAsync(async () =>
        {
            var updated = await RunAuditAsync(root, "en-US", "-Update");
            updated.Code.ShouldBe(0, updated.Stdout + updated.Stderr);
            var tree = await GitBytesAsync(root, "ls-tree", "-r", "-z", "HEAD");
            tree.Length.ShouldBeGreaterThan(0);
            tree[^1].ShouldBe((byte)0);
            var stripped = Path.Combine(root, "tree-without-nul.bin");
            await File.WriteAllBytesAsync(stripped, tree[..^1]);
            var bin = Path.Combine(root, "bin");
            Directory.CreateDirectory(bin);
            var realGit = WhichGit();
            if (OperatingSystem.IsWindows())
            {
                await File.WriteAllTextAsync(Path.Combine(bin, "git.cmd"),
                    "@echo off\r\n" +
                    "if /I \"%~1\"==\"ls-tree\" (\r\n" +
                    "  pwsh -NoProfile -NonInteractive -Command \"$b=[System.IO.File]::ReadAllBytes('" + stripped.Replace("'", "''") + "'); [Console]::OpenStandardOutput().Write($b,0,$b.Length)\"\r\n" +
                    "  exit /b 0\r\n" +
                    ")\r\n" +
                    "\"" + realGit + "\" %*\r\n");
            }
            else
            {
                await File.WriteAllTextAsync(Path.Combine(bin, "git"),
                    "#!/bin/sh\n" +
                    "if [ \"$1\" = \"ls-tree\" ]; then\n" +
                    "  cat \"" + stripped + "\"\n" +
                    "  exit 0\n" +
                    "fi\n" +
                    "exec \"" + realGit + "\" \"$@\"\n");
                File.SetUnixFileMode(Path.Combine(bin, "git"), UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }
            var path = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            var ran = await RunAuditAsync(root, "en-US", new Dictionary<string, string?> { ["PATH"] = path }, "-Check");
            Console.WriteLine($"C1390_MISSING_NUL platform={Environment.OSVersion.Platform}; git={realGit}; " +
                $"shim={Path.Combine(bin, OperatingSystem.IsWindows() ? "git.cmd" : "git")}; " +
                $"tree-bytes={tree.Length}; supplied-bytes={tree.Length - 1}; audit-exit={ran.Code}");
            ran.Code.ShouldBe(1, ran.Stdout + ran.Stderr);
            ran.Stderr.ShouldContain("missing its trailing NUL");
        });
    }

    [Test]
    public async Task C1390_MalformedManifestForcesTheBuild()
    {
        await using var h = await CreateAsync();
        await h.AddDocsOnlySourceAsync();
        using (LandVerificationBuildGraph.UseManifestText("not-a-digest\n1\n"))
            await LandAndAssertAsync(h, build: true);
        (await h.OperationAsync())!.VerificationCommand.ShouldNotBeNull().ShouldContain("unrecognized-build-graph");
    }

    [Test]
    public async Task C1380_AlreadyPresentDoesNotPromiseANewBuildEvenWhenForced()
    {
        await using var h = new LandingSafetyHarness();
        h.ConfigureServices = services => services.AddSingleton<IOptions<LandingSettings>>(
            Options.Create(new LandingSettings { ForceVerificationBuild = true }));
        await h.InitializeAsync();
        await h.RequestAsync();
        await h.RunQueuedAsync();
        h.Verifier.Calls.ShouldBe(0);
        (await h.OperationAsync())!.Publication.ShouldBe(LandPublicationOutcome.AlreadyPresent);
    }

    private static async Task<LandingSafetyHarness> CreateAsync(bool force = false)
    {
        var h = new LandingSafetyHarness();
        h.ConfigureServices = services => services.AddSingleton<IOptions<LandingSettings>>(
            Options.Create(new LandingSettings { ForceVerificationBuild = force }));
        try
        {
            await h.InitializeAsync();
            await SeedGraphAsync(h);
            return h;
        }
        catch { await h.DisposeAsync(); throw; }
    }

    private static async Task ChangeAsync(LandingSafetyHarness h, string path, string content)
    {
        var full = Path.Combine(h.Fixture.Source, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content);
        await CommitAsync(h);
    }

    private static async Task CommitAsync(LandingSafetyHarness h)
    {
        await h.Fixture.RequiredAsync(h.Fixture.Source, "add", ".");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "--allow-empty", "-m", "candidate change");
    }

    private static async Task LandAndAssertAsync(LandingSafetyHarness h, bool build, string? filter = null,
        string target = "refs/heads/master")
    {
        await h.RequestAsync(filter);
        await h.RunQueuedAsync(filter);
        var op = (await h.OperationAsync())!;
        h.Verifier.Calls.ShouldBe(build ? 1 : 0,
            $"publication={op.Publication}; reason={op.LastReason}; command={op.VerificationCommand}");
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        op.VerificationPassed.ShouldBe(build);
        if (build) op.VerificationSkipReason.ShouldBeNull();
        else op.VerificationSkipReason.ShouldBe("non-code");
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", target)).Trim().ShouldBe(op.RebasedSourceSha);
        await using var db = h.CreateContext();
        var verify = await db.StageOutcomes.SingleAsync(o => o.SubjectTaskId == h.Fixture.TaskId && o.Stage == OrchestrationStage.Verify);
        verify.Outcome.ShouldBe(build ? StageOutcomeKind.Clean : StageOutcomeKind.Skipped);
        if (!build) verify.Detail.ShouldBe("non-code");
    }

    // Seed the real committed graph, independently of checkout line endings or missing worktree files.
    private static async Task SeedGraphAsync(LandingSafetyHarness h)
    {
        await CopyGraphAsync(h.Fixture, DelegateScriptRunner.RepoRoot);
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "-m", "audited build graph");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", "master");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "rebase", "master");
    }

    private static async Task CopyGraphAsync(LandingGitFixture fixture, string root)
    {
        var rows = (await fixture.RequiredAsync(root, "ls-tree", "-r", "-z", "HEAD"))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var graph = new LandVerificationBuildGraph();
        var graphPaths = new List<string>();
        var graphRows = new List<string>();
        var executablePaths = new List<string>();
        var regularPaths = new List<string>();
        foreach (var row in rows)
        {
            var tab = row.IndexOf('\t');
            tab.ShouldBeGreaterThanOrEqualTo(0);
            var path = row[(tab + 1)..];
            if (!graph.Includes(path)) continue;
            var metadata = row[..tab].Split(' ');
            metadata.Length.ShouldBe(3);
            metadata[1].ShouldBe("blob", path);
            (metadata[0] is "100644" or "100755").ShouldBeTrue(path);
            graphPaths.Add(path);
            graphRows.Add(row);
            (metadata[0] == "100755" ? executablePaths : regularPaths).Add(path);
            var destination = Path.Combine(fixture.Repository, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, await GitBytesAsync(root, "cat-file", "blob", metadata[2]));
        }
        // The real tracked inventory includes historical scripts beneath ignored .antiphon/.
        // Do not let destination line-ending settings reinterpret committed bytes.
        graphPaths.ShouldNotBeEmpty();
        await fixture.RequiredAsync(fixture.Repository, ["-c", "core.autocrlf=false", "add", "-f", "--", .. graphPaths]);
        // Index modes work on Windows and with core.filemode=false, without trusting checkout permissions.
        if (regularPaths.Count > 0)
            await fixture.RequiredAsync(fixture.Repository, ["update-index", "--chmod=-x", "--", .. regularPaths]);
        if (executablePaths.Count > 0)
            await fixture.RequiredAsync(fixture.Repository, ["update-index", "--chmod=+x", "--", .. executablePaths]);
        var tree = (await fixture.RequiredAsync(fixture.Repository, "write-tree")).Trim();
        var seededRows = (await fixture.RequiredAsync(fixture.Repository, "ls-tree", "-r", "-z", tree))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(row => graph.Includes(row[(row.IndexOf('\t') + 1)..]));
        seededRows.ShouldBe(graphRows, "the fixture graph must match committed blob IDs and modes before testing a skip");
    }

    private static async Task<string> ReadHeadTreeAsync(string root)
    {
        var start = new ProcessStartInfo("git", "ls-tree -r -z HEAD")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, stderr);
        return stdout;
    }

    private static string AuditManifest(string root) => Path.Combine(root, "server", "Application", "Services",
        "LandVerificationBuildGraph.manifest.txt");

    private static Task AssertUpdateRefusesAsync(IReadOnlyDictionary<string, string> files, params string[] needles) =>
        AssertUpdateRefusesWithEntriesAsync(files, null, needles);

    // CARD-1409 repair 5: entries are index-only tracked blobs (mode, path, content) added through
    // git update-index --cacheinfo, so a symbolic link or a path no host could materialise is still at HEAD.
    private static async Task AssertUpdateRefusesWithEntriesAsync(IReadOnlyDictionary<string, string> files,
        IReadOnlyList<(string Mode, string Path, string Content)>? entries, params string[] needles)
    {
        var root = await InitAuditTreeAsync(files, entries);
        await root.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(root, "en-US", "-Update");
            ran.Code.ShouldBe(1, ran.Stdout + ran.Stderr);
            ran.Stderr.ShouldContain("cannot approve it");
            foreach (var needle in needles)
                ran.Stderr.IndexOf(needle, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            File.Exists(AuditManifest(root)).ShouldBeFalse();
        });
    }

    private static async Task AssertUpdateApprovesAsync(IReadOnlyDictionary<string, string> files, int count,
        IReadOnlyList<(string Mode, string Path, string Content)>? entries = null)
    {
        var root = await InitAuditTreeAsync(files, entries);
        await root.RunAsync(async () =>
        {
            var ran = await RunAuditAsync(root, "en-US", "-Update");
            ran.Code.ShouldBe(0, ran.Stdout + ran.Stderr);
            ran.Stdout.ShouldContain("count=" + count.ToString(CultureInfo.InvariantCulture));
        });
    }

    private static async Task<AuditGitFixture> InitAuditRepositoryAsync(string project)
    {
        var root = new AuditGitFixture();
        Directory.CreateDirectory(Path.Combine(root, "server", "Application", "Services"));
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        await File.WriteAllTextAsync(Path.Combine(root, "server", "App.csproj"), project);
        File.Copy(
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "audit-land-verification-precheck.ps1"),
            Path.Combine(root, "scripts", "audit-land-verification-graph.ps1"));
        await GitAsync(root, "init", "-b", "master");
        await GitAsync(root, "config", "user.email", "c1390@example.invalid");
        await GitAsync(root, "config", "user.name", "CARD-1390");
        await GitAsync(root, "add", "--", "server/App.csproj", "scripts/audit-land-verification-graph.ps1");
        await GitAsync(root, "commit", "-m", "fixture");
        return root;
    }

    private static async Task<AuditGitFixture> InitAuditTreeAsync(IReadOnlyDictionary<string, string> files,
        IReadOnlyList<(string Mode, string Path, string Content)>? entries = null)
    {
        var root = new AuditGitFixture();
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        File.Copy(
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "audit-land-verification-precheck.ps1"),
            Path.Combine(root, "scripts", "audit-land-verification-graph.ps1"));
        foreach (var (relative, content) in files)
        {
            var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, content);
        }

        await GitAsync(root, "init", "-b", "master");
        await GitAsync(root, "config", "user.email", "c1400@example.invalid");
        await GitAsync(root, "config", "user.name", "CARD-1400");
        await GitAsync(root, "add", "--", ".");
        foreach (var (mode, path, content) in entries ?? [])
        {
            var oid = await GitHashObjectAsync(root, content);
            await GitAsync(root, "update-index", "--add", "--cacheinfo", mode + "," + oid + "," + path);
        }
        await GitAsync(root, "commit", "-m", "fixture");
        return root;
    }

    private static async Task<string> GitHashObjectAsync(string root, string content)
    {
        var start = GitStart(root, "hash-object", "-w", "--stdin");
        start.RedirectStandardInput = true;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        await process.StandardInput.WriteAsync(content);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        process.ExitCode.ShouldBe(0, await stdout + await stderr);
        return (await stdout).Trim();
    }

    private static string WhichGit()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? "git.exe" : "git");
            if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("git was not found on PATH.");
    }

    private static string RenderAuditArgument(string arg)
    {
        if (arg.Length > 1 && arg[0] == '-' && arg.IndexOfAny([' ', '\t', '\'', '"']) < 0)
            return arg;
        return "'" + arg.Replace("'", "''") + "'";
    }

    private static Task<(int Code, string Stdout, string Stderr)> RunAuditAsync(string root, string culture, params string[] args) =>
        RunAuditAsync(root, culture, environment: null, args);

    private static async Task<(int Code, string Stdout, string Stderr)> RunAuditAsync(
        string root, string culture, IReadOnlyDictionary<string, string?>? environment, params string[] args)
    {
        var script = Path.Combine(root, "scripts", "audit-land-verification-graph.ps1");
        // A switch must stay unquoted. '-Update' is a positional string and the script rejects it.
        var renderedArgs = string.Join(' ', args.Select(RenderAuditArgument));
        var command =
            "$c = [System.Globalization.CultureInfo]::GetCultureInfo('" + culture + "'); " +
            "[System.Threading.Thread]::CurrentThread.CurrentCulture = $c; " +
            "[System.Threading.Thread]::CurrentThread.CurrentUICulture = $c; " +
            "& '" + script.Replace("'", "''") + "' " + renderedArgs;
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
                start.Environment[name] = value;
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout, await stderr);
    }

    private static async Task GitAsync(string root, params string[] args)
    {
        var start = GitStart(root, args);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        process.ExitCode.ShouldBe(0, await stdout + await stderr);
    }

    private static async Task<byte[]> GitBytesAsync(string root, params string[] args)
    {
        var start = GitStart(root, args);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        using var body = new MemoryStream();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardOutput.BaseStream.CopyToAsync(body);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        process.ExitCode.ShouldBe(0, await stderr);
        return body.ToArray();
    }

    private static ProcessStartInfo GitStart(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return start;
    }
}
