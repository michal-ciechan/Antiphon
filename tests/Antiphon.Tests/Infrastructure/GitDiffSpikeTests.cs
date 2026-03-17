using Antiphon.Server.Infrastructure.Git;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// Story 2.13 — Git Diff Spike (de-risk Epic 5).
///
/// Integration tests that verify path-filtered diffs between stage version tags.
/// These tests exercise real git operations against temporary repos to validate:
/// - Path-filtered diff accurately captures changes in _antiphon/artifacts/
/// - Diff output is parseable (standard unified diff format)
/// - Diff computation completes quickly (NFR5: under 5 seconds for repos under 1GB)
///
/// Findings documented in docs/spike-git-diff-cascade.md.
/// </summary>
public class GitDiffSpikeTests
{
    private static readonly Guid TestWorkflowId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>
    /// Core spike test: compute path-filtered diff between {stage}-v1 and {stage}-v2 tags.
    /// Verifies that only changes in _antiphon/artifacts/ are included.
    /// </summary>
    [Fact]
    public async Task PathFilteredDiff_BetweenStageTags_CapturesOnlyArtifactChanges()
    {
        var (service, repoPath) = await CreateTestRepo();
        try
        {
            // 1. Initialize workflow branches
            await service.InitializeWorkflowBranchesAsync(TestWorkflowId, repoPath, CancellationToken.None);

            // 2. Create stage branch and commit v1 artifact
            await service.CreateStageBranchAsync(TestWorkflowId, "architecture", repoPath, CancellationToken.None);
            await service.CommitArtifactAsync(
                TestWorkflowId, "architecture",
                "# Architecture v1\n\n## Overview\nInitial architecture design.\n\n## Components\n- API Server\n- Database",
                "architecture.md", repoPath, CancellationToken.None);

            // 3. Tag as v1
            var tagV1 = await service.TagStageAsync(TestWorkflowId, "architecture", 1, repoPath, CancellationToken.None);
            tagV1.Should().Be(GitService.GetStageTag(TestWorkflowId, "architecture", 1));

            // 4. Also add a non-artifact file on the same branch (simulating code changes)
            var nonArtifactPath = Path.Combine(repoPath, "src", "Program.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(nonArtifactPath)!);
            await File.WriteAllTextAsync(nonArtifactPath, "// Program.cs v1");
            await RunGit(repoPath, "add src/Program.cs");
            await RunGit(repoPath, "commit -m \"Add non-artifact file\"");

            // 5. Modify the artifact for v2
            await service.CommitArtifactAsync(
                TestWorkflowId, "architecture",
                "# Architecture v2\n\n## Overview\nRevised architecture with event sourcing.\n\n## Components\n- API Server\n- Database\n- Message Queue\n- Event Store",
                "architecture.md", repoPath, CancellationToken.None);

            // 6. Also modify the non-artifact file
            await File.WriteAllTextAsync(nonArtifactPath, "// Program.cs v2 - updated");
            await RunGit(repoPath, "add src/Program.cs");
            await RunGit(repoPath, "commit -m \"Update non-artifact file\"");

            // 7. Tag as v2
            var tagV2 = await service.TagStageAsync(TestWorkflowId, "architecture", 2, repoPath, CancellationToken.None);
            tagV2.Should().Be(GitService.GetStageTag(TestWorkflowId, "architecture", 2));

            // 8. Compute PATH-FILTERED diff (only _antiphon/artifacts/)
            var artifactPath = GitService.GetArtifactDirectory(TestWorkflowId);
            var filteredDiff = await service.GetDiffBetweenTagsAsync(tagV1, tagV2, repoPath, artifactPath, CancellationToken.None);

            // Verify: diff DOES contain artifact changes
            filteredDiff.Should().Contain("Architecture v1");
            filteredDiff.Should().Contain("Architecture v2");
            filteredDiff.Should().Contain("event sourcing");
            filteredDiff.Should().Contain("Message Queue");
            filteredDiff.Should().Contain("Event Store");

            // Verify: diff does NOT contain non-artifact changes
            filteredDiff.Should().NotContain("Program.cs");
            filteredDiff.Should().NotContain("non-artifact");

            // 9. Compare with unfiltered diff (should contain both)
            var unfilteredDiff = await service.GetDiffBetweenTagsAsync(tagV1, tagV2, repoPath, CancellationToken.None);
            unfilteredDiff.Should().Contain("Program.cs");
            unfilteredDiff.Should().Contain("architecture.md");
        }
        finally
        {
            CleanupTestRepo(repoPath);
        }
    }

    /// <summary>
    /// Verifies that the diff output is in standard unified diff format, which is parseable.
    /// </summary>
    [Fact]
    public async Task PathFilteredDiff_OutputIsStandardUnifiedFormat()
    {
        var (service, repoPath) = await CreateTestRepo();
        try
        {
            await service.InitializeWorkflowBranchesAsync(TestWorkflowId, repoPath, CancellationToken.None);
            await service.CreateStageBranchAsync(TestWorkflowId, "design", repoPath, CancellationToken.None);

            // Commit v1
            await service.CommitArtifactAsync(
                TestWorkflowId, "design", "# Design v1\nOriginal design.",
                "design.md", repoPath, CancellationToken.None);
            var tagV1 = await service.TagStageAsync(TestWorkflowId, "design", 1, repoPath, CancellationToken.None);

            // Commit v2
            await service.CommitArtifactAsync(
                TestWorkflowId, "design", "# Design v2\nRevised design with new section.\n\n## New Section\nContent here.",
                "design.md", repoPath, CancellationToken.None);
            var tagV2 = await service.TagStageAsync(TestWorkflowId, "design", 2, repoPath, CancellationToken.None);

            var artifactPath = GitService.GetArtifactDirectory(TestWorkflowId);
            var diff = await service.GetDiffBetweenTagsAsync(tagV1, tagV2, repoPath, artifactPath, CancellationToken.None);

            // Unified diff format markers
            diff.Should().Contain("diff --git");
            diff.Should().Contain("---");
            diff.Should().Contain("+++");
            diff.Should().Contain("@@");

            // The diff should contain added/removed lines (+ and - prefixed)
            diff.Should().Contain("+")
                .And.Contain("-");
        }
        finally
        {
            CleanupTestRepo(repoPath);
        }
    }

    /// <summary>
    /// Verifies that diff computation completes quickly (NFR5: under 5 seconds for repos under 1GB).
    /// </summary>
    [Fact]
    public async Task PathFilteredDiff_CompletesWithinFiveSeconds()
    {
        var (service, repoPath) = await CreateTestRepo();
        try
        {
            await service.InitializeWorkflowBranchesAsync(TestWorkflowId, repoPath, CancellationToken.None);
            await service.CreateStageBranchAsync(TestWorkflowId, "perf-test", repoPath, CancellationToken.None);

            // Create a moderately sized artifact (simulate a real spec document)
            var largeContent = string.Join("\n\n",
                Enumerable.Range(1, 200).Select(i => $"## Section {i}\n\nThis is section {i} of the architecture document. " +
                    $"It contains detailed technical specifications for component {i}, including API contracts, " +
                    $"data models, error handling strategies, and integration patterns.\n\n" +
                    $"```typescript\ninterface Component{i} {{\n  id: string;\n  name: string;\n  config: Record<string, unknown>;\n}}\n```"));

            await service.CommitArtifactAsync(
                TestWorkflowId, "perf-test", $"# Architecture v1\n\n{largeContent}",
                "large-spec.md", repoPath, CancellationToken.None);
            var tagV1 = await service.TagStageAsync(TestWorkflowId, "perf-test", 1, repoPath, CancellationToken.None);

            // Modify a significant portion
            var modifiedContent = largeContent.Replace("architecture document", "REVISED architecture document")
                .Replace("component", "microservice");

            await service.CommitArtifactAsync(
                TestWorkflowId, "perf-test", $"# Architecture v2 (Revised)\n\n{modifiedContent}",
                "large-spec.md", repoPath, CancellationToken.None);
            var tagV2 = await service.TagStageAsync(TestWorkflowId, "perf-test", 2, repoPath, CancellationToken.None);

            var artifactPath = GitService.GetArtifactDirectory(TestWorkflowId);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var diff = await service.GetDiffBetweenTagsAsync(tagV1, tagV2, repoPath, artifactPath, CancellationToken.None);
            sw.Stop();

            // NFR5: under 5 seconds
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
                "path-filtered diff should complete within 5 seconds for repos under 1GB");

            // Verify diff is non-empty and meaningful
            diff.Should().NotBeNullOrWhiteSpace();
            diff.Should().Contain("REVISED architecture document");
            diff.Should().Contain("microservice");
        }
        finally
        {
            CleanupTestRepo(repoPath);
        }
    }

    /// <summary>
    /// Verifies that diff works correctly when multiple artifact files change between versions.
    /// </summary>
    [Fact]
    public async Task PathFilteredDiff_HandlesMultipleArtifactFiles()
    {
        var (service, repoPath) = await CreateTestRepo();
        try
        {
            await service.InitializeWorkflowBranchesAsync(TestWorkflowId, repoPath, CancellationToken.None);
            await service.CreateStageBranchAsync(TestWorkflowId, "multi", repoPath, CancellationToken.None);

            // Commit multiple artifacts at v1
            await service.CommitArtifactAsync(
                TestWorkflowId, "multi", "# PRD v1", "prd.md", repoPath, CancellationToken.None);
            await service.CommitArtifactAsync(
                TestWorkflowId, "multi", "# UX Spec v1", "ux-spec.md", repoPath, CancellationToken.None);
            var tagV1 = await service.TagStageAsync(TestWorkflowId, "multi", 1, repoPath, CancellationToken.None);

            // Modify one, add another at v2
            await service.CommitArtifactAsync(
                TestWorkflowId, "multi", "# PRD v2 - Updated", "prd.md", repoPath, CancellationToken.None);
            await service.CommitArtifactAsync(
                TestWorkflowId, "multi", "# Test Plan v1", "test-plan.md", repoPath, CancellationToken.None);
            var tagV2 = await service.TagStageAsync(TestWorkflowId, "multi", 2, repoPath, CancellationToken.None);

            var artifactPath = GitService.GetArtifactDirectory(TestWorkflowId);
            var diff = await service.GetDiffBetweenTagsAsync(tagV1, tagV2, repoPath, artifactPath, CancellationToken.None);

            // Should show changes to prd.md
            diff.Should().Contain("prd.md");
            diff.Should().Contain("PRD v2 - Updated");

            // Should show new test-plan.md
            diff.Should().Contain("test-plan.md");
            diff.Should().Contain("Test Plan v1");

            // ux-spec.md was not changed, so it should NOT appear in the diff
            diff.Should().NotContain("ux-spec.md");
        }
        finally
        {
            CleanupTestRepo(repoPath);
        }
    }

    /// <summary>
    /// Verifies that diff provides enough context lines for AI cascade updates.
    /// </summary>
    [Fact]
    public async Task PathFilteredDiff_ProvidesContextForAiCascade()
    {
        var (service, repoPath) = await CreateTestRepo();
        try
        {
            await service.InitializeWorkflowBranchesAsync(TestWorkflowId, repoPath, CancellationToken.None);
            await service.CreateStageBranchAsync(TestWorkflowId, "ctx", repoPath, CancellationToken.None);

            var v1Content = """
                # Architecture

                ## Overview
                Monolithic architecture using Express.js.

                ## Data Layer
                PostgreSQL with Prisma ORM.

                ## API Design
                REST endpoints with OpenAPI spec.
                """;

            var v2Content = """
                # Architecture

                ## Overview
                Microservices architecture using ASP.NET Core.

                ## Data Layer
                PostgreSQL with Entity Framework Core.

                ## API Design
                REST endpoints with OpenAPI spec.

                ## New: Event Bus
                RabbitMQ for async communication between services.
                """;

            await service.CommitArtifactAsync(
                TestWorkflowId, "ctx", v1Content, "arch.md", repoPath, CancellationToken.None);
            var tagV1 = await service.TagStageAsync(TestWorkflowId, "ctx", 1, repoPath, CancellationToken.None);

            await service.CommitArtifactAsync(
                TestWorkflowId, "ctx", v2Content, "arch.md", repoPath, CancellationToken.None);
            var tagV2 = await service.TagStageAsync(TestWorkflowId, "ctx", 2, repoPath, CancellationToken.None);

            var artifactPath = GitService.GetArtifactDirectory(TestWorkflowId);
            var diff = await service.GetDiffBetweenTagsAsync(tagV1, tagV2, repoPath, artifactPath, CancellationToken.None);

            // Diff should capture what was removed
            diff.Should().Contain("-Monolithic architecture using Express.js.");
            diff.Should().Contain("-PostgreSQL with Prisma ORM.");

            // Diff should capture what was added
            diff.Should().Contain("+Microservices architecture using ASP.NET Core.");
            diff.Should().Contain("+PostgreSQL with Entity Framework Core.");
            diff.Should().Contain("+## New: Event Bus");
            diff.Should().Contain("+RabbitMQ");

            // Context lines (unchanged lines near changes) should be present for AI understanding
            diff.Should().Contain("## Overview");
            diff.Should().Contain("## Data Layer");
        }
        finally
        {
            CleanupTestRepo(repoPath);
        }
    }

    // --- Helper methods (reused pattern from GitServiceTests) ---

    private static async Task<(GitService Service, string RepoPath)> CreateTestRepo()
    {
        var repoPath = Path.Combine(Path.GetTempPath(), $"antiphon-spike-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repoPath);

        await RunGit(repoPath, "init");
        await RunGit(repoPath, "config user.email \"test@antiphon.dev\"");
        await RunGit(repoPath, "config user.name \"Antiphon Spike Test\"");

        // Create an initial commit so branches can be created
        var readmePath = Path.Combine(repoPath, "README.md");
        await File.WriteAllTextAsync(readmePath, "# Spike Test Repo");
        await RunGit(repoPath, "add .");
        await RunGit(repoPath, "commit -m \"Initial commit\"");

        var service = new GitService(NullLogger<GitService>.Instance);
        return (service, repoPath);
    }

    private static async Task<string> RunGit(string workingDirectory, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }

    private static void CleanupTestRepo(string repoPath)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(repoPath, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(repoPath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup on Windows
        }
    }
}
