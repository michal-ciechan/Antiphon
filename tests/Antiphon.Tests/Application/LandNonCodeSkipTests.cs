using System.Diagnostics;
using System.Globalization;
using System.Text;
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
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandNonCodeSkipTests
{
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

        h.Verifier.Calls.ShouldBe(0, "a docs-only rebased candidate must not invoke the verifier or its build slot");
        var op = (await h.OperationAsync())!;
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
        digest.ShouldBe(LandVerificationBuildGraph.Fingerprint);
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
        try
        {
            var ran = await RunAuditAsync(root, "en-US", "-Update");
            ran.Code.ShouldBe(1, ran.Stdout + ran.Stderr);
            ran.Stderr.ShouldContain("cannot approve it");
            ran.Stderr.ShouldContain("docs/**/*.md");
            File.Exists(AuditManifest(root)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
            try
            {
                var ran = await RunAuditAsync(root, "en-US", "-Update");
                ran.Code.ShouldBe(1, include + " " + ran.Stdout + ran.Stderr);
                ran.Stderr.ShouldContain("cannot approve it");
                ran.Stderr.IndexOf(include, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
                File.Exists(AuditManifest(root)).ShouldBeFalse();
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
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
        try
        {
            var ran = await RunAuditAsync(explicitImport, "en-US", "-Update");
            ran.Code.ShouldBe(1, ran.Stdout + ran.Stderr);
            ran.Stderr.ShouldContain("cannot approve it");
            ran.Stderr.IndexOf("eng/x/shared.props", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            ran.Stderr.IndexOf("../docs/**/*.md", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            File.Exists(AuditManifest(explicitImport)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(explicitImport, recursive: true);
        }

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
        try
        {
            var ran = await RunAuditAsync(directoryBuild, "en-US", "-Update");
            ran.Code.ShouldBe(1, ran.Stdout + ran.Stderr);
            ran.Stderr.ShouldContain("cannot approve it");
            ran.Stderr.IndexOf("Directory.Build.props", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            ran.Stderr.IndexOf("$(MSBuildProjectDirectory)/../docs/**/*.md", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            File.Exists(AuditManifest(directoryBuild)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(directoryBuild, recursive: true);
        }
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
        try
        {
            var ran = await RunAuditAsync(root, "en-US", "-Update");
            ran.Code.ShouldBe(1, ran.Stdout + ran.Stderr);
            ran.Stderr.ShouldContain("cannot approve it");
            ran.Stderr.IndexOf("../%64ocs/**/*.md", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            File.Exists(AuditManifest(root)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
        try
        {
            var ran = await RunAuditAsync(root, "en-US", "-Update");
            ran.Code.ShouldBe(1, ran.Stdout + ran.Stderr);
            ran.Stderr.ShouldContain("cannot approve it");
            ran.Stderr.IndexOf("AdditionalFiles", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            ran.Stderr.IndexOf("../docs/**/*.md", StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            File.Exists(AuditManifest(root)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
            try
            {
                var ran = await RunAuditAsync(root, "en-US", "-Update");
                ran.Code.ShouldBe(1, reference + ": " + ran.Stdout + ran.Stderr);
                ran.Stderr.ShouldContain("cannot approve it", customMessage: reference);
                ran.Stderr.IndexOf("server/App.csproj Import Project=" + reference, StringComparison.Ordinal)
                    .ShouldBeGreaterThanOrEqualTo(0, reference + ": " + ran.Stderr);
                File.Exists(AuditManifest(root)).ShouldBeFalse(reference);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
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
        await AssertUpdateRefusesAsync(new Dictionary<string, string>
        {
            ["eng/docs.items"] = items,
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../eng/*.items" />
                </Project>
                """,
        }, "server/App.csproj Import Project=../eng/*.items", "-> eng/docs.items");
    }

    [Test]
    public async Task C1409_EmptyWildcardImportIsAccepted()
    {
        var root = await InitAuditTreeAsync(new Dictionary<string, string>
        {
            ["server/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../eng/*.props" />
                </Project>
                """,
        });
        try
        {
            var ran = await RunAuditAsync(root, "en-US", "-Update");
            ran.Code.ShouldBe(0, ran.Stdout + ran.Stderr);
            ran.Stdout.ShouldContain("count=1");
            File.Exists(AuditManifest(root)).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task C1390_MissingNulFailsUnderNonInvariantCulture()
    {
        var root = await InitAuditRepositoryAsync("<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n");
        try
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
            ran.Code.ShouldBe(1, ran.Stdout + ran.Stderr);
            ran.Stderr.ShouldContain("missing its trailing NUL");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
        h.Verifier.Calls.ShouldBe(build ? 1 : 0);
        var op = (await h.OperationAsync())!;
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

    // Copy real audited build entrypoints into the scratch repo; do not inject a policy verdict.
    private static async Task SeedGraphAsync(LandingSafetyHarness h)
    {
        var root = DelegateScriptRunner.RepoRoot;
        var paths = (await h.Fixture.RequiredAsync(root, "ls-files", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var graph = new LandVerificationBuildGraph();
        var graphPaths = new List<string>();
        foreach (var path in paths)
        {
            if (!graph.Includes(path)) continue;
            graphPaths.Add(path);
            var destination = Path.Combine(h.Fixture.Repository, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(root, path), destination);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, File.GetUnixFileMode(Path.Combine(root, path)));
        }
        // The real tracked inventory includes historical scripts beneath ignored .antiphon/.
        // Stage only these exact copied inputs, including ones the scratch fixture's ignore rules hide.
        await h.Fixture.RequiredAsync(h.Fixture.Repository, ["add", "-f", "--", .. graphPaths]);
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "-m", "audited build graph");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", "master");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "rebase", "master");
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

    private static async Task AssertUpdateRefusesAsync(IReadOnlyDictionary<string, string> files, params string[] needles)
    {
        var root = await InitAuditTreeAsync(files);
        try
        {
            var ran = await RunAuditAsync(root, "en-US", "-Update");
            ran.Code.ShouldBe(1, ran.Stdout + ran.Stderr);
            ran.Stderr.ShouldContain("cannot approve it");
            foreach (var needle in needles)
                ran.Stderr.IndexOf(needle, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, ran.Stderr);
            File.Exists(AuditManifest(root)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> InitAuditRepositoryAsync(string project)
    {
        var root = Directory.CreateTempSubdirectory("c1390-audit-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "server", "Application", "Services"));
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        await File.WriteAllTextAsync(Path.Combine(root, "server", "App.csproj"), project);
        File.Copy(
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "audit-land-verification-graph.ps1"),
            Path.Combine(root, "scripts", "audit-land-verification-graph.ps1"));
        await GitAsync(root, "init", "-b", "master");
        await GitAsync(root, "config", "user.email", "c1390@example.invalid");
        await GitAsync(root, "config", "user.name", "CARD-1390");
        await GitAsync(root, "add", "--", "server/App.csproj", "scripts/audit-land-verification-graph.ps1");
        await GitAsync(root, "commit", "-m", "fixture");
        return root;
    }

    private static async Task<string> InitAuditTreeAsync(IReadOnlyDictionary<string, string> files)
    {
        var root = Directory.CreateTempSubdirectory("c1400-audit-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        File.Copy(
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "audit-land-verification-graph.ps1"),
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
        await GitAsync(root, "commit", "-m", "fixture");
        return root;
    }

    private static string WhichGit()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, "git");
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
