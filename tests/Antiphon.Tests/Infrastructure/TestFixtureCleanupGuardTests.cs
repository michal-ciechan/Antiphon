using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class TestFixtureCleanupGuardTests
{
    private static readonly Regex GitFixture = new(
        """
        \b(?:git(?:\.exe)?\s+(?:-C\b[^\r\n]*?\s+)?(?:init|clone)\b|Repository\s*\.\s*(?:Init|Initialize|Clone)\s*\(|
        (?:\w*Git\w*|Run|Execute)\s*\([^;{}]{0,200}"(?:init|clone|worktree)(?:"|\s)|
        (?:GitFixture|ScratchGitRepo|GitTreeTemplate|DelegationScratchRepo|LandDeliveryFixture|CompletedCardRunnerFixture|LandingGitFixture)\b)
        """, RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Keep exceptions tied to a path and delete argument. The count prevents a new call with
    // the same argument text from inheriting an existing exception.
    private const string Application = "tests/Antiphon.Tests/Application/";
    private const string Checkpoints = "tests/Antiphon.Tests/Checkpoints/";
    private const string Infrastructure = "tests/Antiphon.Tests/Infrastructure/";
    private const string Scripts = "tests/Antiphon.Tests/Scripts/";
    private const string TestHelpers = "tests/Antiphon.Tests/TestHelpers/";
    private const string Runner = "tests/Antiphon.SessionRunner.Tests/";
    private const string E2E = "tests/Antiphon.E2E/";

    private static readonly Dictionary<(string Path, string Target), (int Count, string Reason)> Allowed = new()
    {
        [(E2E + "LandDeliveryFixtureProbeTests.cs", "f.Root")] = (9, "Receipt and mock-verifier probes create evidence folders; Git setup is never called."),
        [(E2E + "LandDeliveryFixtureProbeTests.cs", "root")] = (4, "Receipt and mock-verifier probes create evidence folders; Git setup is never called."),
        [("tests/Antiphon.GitInProcess.Tests/LibGit2VsGitWorktreeListTests.cs", "gone")] = (1, "Removes a linked checkout to exercise stale registration."),
        [(Runner + "BlockedParkWireTests.cs", "_root")] = (2, "Runtime and transcript scratch roots. The separately owned Git fixture disposes itself."),
        [(Runner + "C1677RetentionScratch.cs", "m.Path")] = (1, "Removes a linked checkout to exercise stale registration."),
        [(Runner + "C1677TempSweepNativeTests.cs", "entry + \"/inner\"")] = (1, "Swaps a scratch child for a link during the cleanup witness."),
        [(Runner + "CompletedCardWorkspaceS5Tests.cs", "stale")] = (1, "Removes linked checkouts to exercise stale registration."),
        [(Runner + "CompletedCardWorkspaceS5Tests.cs", "absent.Fixture.Checkout")] = (1, "Removes linked checkouts to exercise stale registration."),
        [(Runner + "CompletedCardWorkspaceS5Tests.cs", "path")] = (1, "Swaps a build-output directory for a link."),
        [(Runner + "LegacyWorkspaceRemovalGateTests.cs", "path")] = (1, "Removes a linked checkout to exercise the missing-checkout branch."),
        [(Runner + "LegacyWorkspaceRemovalGateTests.cs", "f.Checkout")] = (1, "Removes a linked checkout to exercise the missing-checkout branch."),
        [(Runner + "RunnerWorkspaceServiceTests.cs", "root")] = (1, "Session runtime scratch root."),
        [(Runner + "RunnerWorkspaceServiceTests.cs", "outside")] = (3, "Outside directories for spill-path containment tests."),
        [("tests/Antiphon.Tests/Agents/RunnerGrokAdapterReadyTestsPty.cs", "root")] = (1, "Pty scratch root. The separate linked checkout has its own Git removal."),
        [(Application + "AdvisoryChangePolicyTests.Correction.cs", "w.ProducerRoot")] = (1, "Producer-pack report files. The selection repository has a separate root."),
        [(Application + "AgentTaskDeadSessionReconciliationTests.cs", "projectsRoot")] = (1, "Project discovery stubs and a plain working directory."),
        [(Application + "AgentTaskDeadSessionReconciliationTests.cs", "cwd")] = (1, "Project discovery stubs and a plain working directory."),
        [(Application + "AgentTaskDeliveryWatchdogTests.cs", "path")] = (1, "Synthetic project discovery paths and plain working directories."),
        [(Application + "AgentTaskDispatchFailureTests.cs", "Path")] = (1, "Plain workspace for a rejected dispatch."),
        [(Application + "AgentTaskLandSourceFreshnessTests.cs", "_root")] = (1, "Build-slot witness script and invocation marker."),
        [(Application + "AgentTaskOverdueDeadlineTests.cs", "path")] = (1, "Synthetic project discovery paths and plain working directories."),
        [(Application + "AgentTaskReplyIntegrationTests.cs", "h.TempRoot")] = (1, "Reply harness, attachments and plain workspaces; Git fixtures own separate roots."),
        [(Application + "AgentTaskReplyIntegrationTests.cs", "fileRoot")] = (1, "Reply harness, attachments and plain workspaces; Git fixtures own separate roots."),
        [(Application + "AgentTaskReplyIntegrationTests.cs", "Path")] = (1, "Reply harness, attachments and plain workspaces; Git fixtures own separate roots."),
        [(Application + "AgentTaskServiceIntegrationTests.cs", "Path")] = (1, "Branch for plain workspaces. The Git branch disposes ScratchGitRepo."),
        [(Application + "AgentTaskStandingAgentDispatchTests.cs", "worktreeRoot")] = (1, "Empty worktree parent and plain workspace."),
        [(Application + "AgentTaskStandingAgentDispatchTests.cs", "Path")] = (1, "Empty worktree parent and plain workspace."),
        [(Application + "AgentTaskWorktreeBaseCreateTests.cs", "temporaryPath")] = (1, "Plain rejected caller directory or empty provider home."),
        [(Application + "BlockedTaskParkResumeTests.cs", "f.SourcePath")] = (2, "Removes a linked checkout to exercise parked source disappearance."),
        [(Application + "CapacityAdmissionLifecycleTests.cs", "Path")] = (1, "Plain workspace for capacity admission."),
        [(Application + "CardTaskFileServiceTests.cs", "world.BoardDir(repo, \"removed-board\")")] = (1, "Board files and non-repository scratch. Repositories are disposed separately."),
        [(Application + "CardTaskFileServiceTests.cs", "dir")] = (1, "Board files and non-repository scratch. Repositories are disposed separately."),
        [(Application + "CodexCliDeliveryWorld.cs", "root")] = (1, "Harness and run evidence roots; Git is disposed separately."),
        [(Application + "CodexCliObservationTests.cs", "root")] = (1, "Session evidence and transcript roots."),
        [(Application + "DefaultRunnerPinTests.cs", "Path")] = (1, "Plain temporary caller root."),
        [(Application + "DelegationWorktreeTests.V25.cs", "first.WorktreePath!")] = (1, "Removes a linked checkout to simulate missing registered path."),
        [(Application + "DelegationWorktreeTests.cs", "Path")] = (1, "Plain temporary workspace."),
        [(Application + "LandGraphLandRefreshTests.cs", "root")] = (1, "Synthetic loose-object witness explicitly resets its one read-only file before fallback cleanup."),
        [(Application + "LandWarmArtifactsTests.cs", "cold.Path")] = (2, "Cold/warm build artifact directories."),
        [(Application + "LandWarmArtifactsTests.cs", "path")] = (1, "Cold/warm build artifact directories."),
        [(Application + "PlanCatalogServiceTests.cs", "_base")] = (1, "Synthetic document catalog with no initialized Git repository."),
        [(Application + "RepairSourceSettlementTests.cs", "repair.WorktreePath!")] = (1, "Removes a linked checkout to simulate missing source."),
        [(Application + "TaskProgressGitTests.cs", "broken")] = (1, "Invalid repository path used to test failure reporting."),
        [(Application + "TaskProgressPolicyFileArmTests.cs", "broken")] = (1, "Invalid repository path used to test failure reporting."),
        [(Application + "WorktreeDefaultAdmissionTests.cs", "plain")] = (2, "Plain directories for the non-repository path."),
        [(Application + "WorktreeDefaultCallerTests.cs", "path")] = (1, "Plain caller, report and state paths; ScratchGitRepo disposes the actual repositories."),
        [(Application + "WorktreeRetirementRaceTests.cs", "root")] = (1, "Synthetic attribute witness explicitly resets its one read-only file before fallback cleanup."),
        [(Checkpoints + "CheckpointOutputConfinementTests.cs", "path")] = (1, "Shim and results directories. Fixture Git root has separate attribute-aware teardown."),
        [(Checkpoints + "CheckpointSourceStateTests.cs", "Path.Combine(repo.Root, \"folder\")")] = (1, "Ordinary tracked child changed from directory to file as the test stimulus."),
        [(Checkpoints + "CheckpointTrackedBaselineTests.cs", "built")] = (1, "Generated build output used for a baseline test."),
        [(Checkpoints + "OutputCleanupTests.cs", "outside")] = (3, "Outside sentinels and link-parent holders. Repository fixture teardown is separate."),
        [(Checkpoints + "OutputCleanupTests.cs", "linkRoot")] = (1, "Outside sentinels and link-parent holders. Repository fixture teardown is separate."),
        [(Infrastructure + "DindRunnerContractTests.cs", "root.FullName")] = (1, "Claude settings and onboarding scratch directory."),
        [(Infrastructure + "LandWorkspaceTests.cs", "path")] = (1, "Removes a linked checkout to exercise its recreation."),
        [(Infrastructure + "WorktreeGuardedCleanupTests.cs", "directory")] = (2, "Replaces test directories with links during deletion."),
        [(Infrastructure + "WorktreeManagerTests.cs", "first.Path")] = (1, "Removes linked checkouts or interrupted worktree directories as the stimulus."),
        [(Infrastructure + "WorktreeManagerTests.cs", "missing.Path")] = (1, "Removes linked checkouts or interrupted worktree directories as the stimulus."),
        [(Infrastructure + "WorktreeManagerTests.cs", "interrupted.Path")] = (1, "Removes linked checkouts or interrupted worktree directories as the stimulus."),
        [(Infrastructure + "WorktreeManagerTests.cs", "worktree.Path")] = (1, "Removes linked checkouts or interrupted worktree directories as the stimulus."),
        [(Scripts + "HostJqPrerequisiteScriptTests.cs", "Linked")] = (1, "Linked checkout lives outside the repository root and has only a Git pointer."),
        [(Scripts + "NightlyCoreGoldenTests.cs", "timing")] = (1, "Timing journal temporary directory."),
        [(Scripts + "PostLandEarlyWarningTests.cs", "root")] = (1, "Process coordination marker directory."),
        [(Scripts + "RemoteScriptContractTests.cs", "directory")] = (1, "Script staging and diagnostics directories; Git setup is elsewhere."),
        [(Scripts + "RemoteScriptContractTests.cs", "path")] = (1, "Script staging and diagnostics directories; Git setup is elsewhere."),
        [(Scripts + "RemoteScriptContractTests.cs", "_directory")] = (1, "Script staging and diagnostics directories; Git setup is elsewhere."),
        [(Scripts + "RemoteScriptContractTests.cs", "scratch")] = (1, "Script staging and diagnostics directories; Git setup is elsewhere."),
        [(Scripts + "RollingVolumeRecycleScriptTests.cs", "root")] = (1, "Preview and wrapper scratch directories containing coordination state."),
        [(Scripts + "RollingVolumeRecycleScriptTests.cs", "Root")] = (1, "Preview and wrapper scratch directories containing coordination state."),
        [(Scripts + "VolumeRetirementContentTests.cs", "root")] = (7, "Exchange scripts, stubbed audit results and proof files."),
        [(Scripts + "VolumeRetirementHostProofTests.cs", "scratch")] = (1, "Shim scratch and synthetic cleanup-ownership witnesses."),
        [(Scripts + "VolumeRetirementHostProofTests.cs", "root")] = (1, "Shim scratch and synthetic cleanup-ownership witnesses."),
        [(TestHelpers + "LandingFixtureGitInProcessTests.cs", "Path.Combine(gitDir, \"sequencer\")")] = (1, "Synthetic sequencer marker, removed to allow the next assertion."),
        [(TestHelpers + "OwnedTempLeakTests.cs", "root")] = (3, "Synthetic scratch and bridge cleanup witnesses without repositories."),
        [(TestHelpers + "ReportWorkspace.cs", "Path")] = (1, "Plain report scratch. The Git workspace has a separate disposer."),
        [("tests/Antiphon.Tests/Review/ReviewMirrorCheckoutTests.cs", "root")] = (1, "The runner test's plain scratch root: a recording checkout stands in for git, so no repository is created. The real-git class in the same file cleans up through TryDelete."),
    };

    [Test]
    public void Git_fixtures_use_read_only_safe_directory_cleanup()
    {
        var root = DockerStackDocuments.RepoRoot;
        var files = TestSources(Path.Combine(root, "tests")).Order(StringComparer.Ordinal).ToArray();
        files.ShouldNotBeEmpty();

        var violations = new List<string>();
        var usedExceptions = new Dictionary<(string Path, string Target), int>();
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            if (!source.Contains("Delete", StringComparison.Ordinal) || !GitFixture.IsMatch(source))
                continue;

            var path = Path.GetRelativePath(root, file).Replace('\\', '/');
            var syntax = CSharpSyntaxTree.ParseText(source);
            foreach (var call in syntax.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var target = RawRecursiveDeleteTarget(call);
                if (target is null || ClearsReadOnlyBeforeDelete(call, target))
                    continue;

                var key = (path, target);
                var used = usedExceptions.GetValueOrDefault(key);
                if (Allowed.TryGetValue(key, out var exception) && used < exception.Count)
                {
                    usedExceptions[key] = used + 1;
                    continue;
                }

                var line = syntax.GetLineSpan(call.Span).StartLinePosition.Line + 1;
                violations.Add($"{path}:{line} ({target})");
            }
        }

        violations.Count.ShouldBe(0, "Use ReadOnlyDirectory.Delete for Git fixture trees.\n" + string.Join('\n', violations));
    }

    private static string? RawRecursiveDeleteTarget(InvocationExpressionSyntax call)
    {
        if (call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Delete" } member)
            return null;

        var args = call.ArgumentList.Arguments;
        var receiver = member.Expression.ToString();
        if (receiver == "Directory" || receiver.EndsWith(".Directory", StringComparison.Ordinal))
        {
            if (args.Count != 2)
                return null;
            var recursive = args.SingleOrDefault(arg => arg.NameColon?.Name.Identifier.ValueText == "recursive") ?? args[1];
            var path = args[0] == recursive ? args[1] : args[0];
            return recursive.Expression.IsKind(SyntaxKind.TrueLiteralExpression) ? path.Expression.ToString() : null;
        }

        // DirectoryInfo.Delete has a single recursive argument.
        return args.Count == 1 && args[0].Expression.IsKind(SyntaxKind.TrueLiteralExpression)
            ? receiver : null;
    }

    private static bool ClearsReadOnlyBeforeDelete(InvocationExpressionSyntax delete, string target)
    {
        if (delete.Parent is not ExpressionStatementSyntax { Parent: BlockSyntax block } statement)
            return false;

        foreach (var previous in block.Statements.TakeWhile(node => node.SpanStart < statement.SpanStart))
        {
            if (previous is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax clear }
                && clear.Expression.ToString() == "ReadOnlyDirectory.ClearReadOnly"
                && clear.ArgumentList.Arguments.Count == 1
                && clear.ArgumentList.Arguments[0].Expression.ToString() == target)
                return true;

            if (previous is not ForEachStatementSyntax { Expression: InvocationExpressionSyntax enumerate } loop)
                continue;

            // Some fixtures already normalize every file under the exact root before deleting it.
            var args = enumerate.ArgumentList.Arguments;
            var body = loop.Statement is BlockSyntax { Statements.Count: 1 } braces ? braces.Statements[0] : loop.Statement;
            if (enumerate.Expression.ToString() is "Directory.EnumerateFiles" or "System.IO.Directory.EnumerateFiles"
                && args.Count == 3
                && args[0].Expression.ToString() == target
                && args[1].Expression is LiteralExpressionSyntax pattern && pattern.Token.ValueText == "*"
                && args[2].Expression.ToString() == "SearchOption.AllDirectories"
                && body is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax set }
                && set.Expression.ToString() == "File.SetAttributes"
                && set.ArgumentList.Arguments.Count == 2
                && set.ArgumentList.Arguments[0].Expression.ToString() == loop.Identifier.ValueText
                && set.ArgumentList.Arguments[1].Expression.ToString() == "FileAttributes.Normal")
                return true;
        }

        return false;
    }

    private static IEnumerable<string> TestSources(string directory)
    {
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", options))
            yield return file;

        foreach (var child in Directory.EnumerateDirectories(directory, "*", options))
        {
            var name = Path.GetFileName(child);
            if (name is "bin" or "obj" || name.StartsWith("bin-", StringComparison.Ordinal) || name.StartsWith('.'))
                continue;
            foreach (var file in TestSources(child))
                yield return file;
        }
    }
}
