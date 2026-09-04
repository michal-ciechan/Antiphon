using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0244: a source census of hand-built dispatcher and reply harnesses so the next clone
/// cannot skip <see cref="DelegationTestServices"/> the way CARD-0299's boot-wedge leftover did.
/// String contains, comments kept; the census file itself is not a harness.
/// </summary>
[Category("Unit")]
public sealed class DelegationHarnessCensusTests
{
    // Negative lookbehind so TryAddScoped / TryAddSingleton (the helper) are not hits.
    private static readonly Regex HandRolledDelegationWorktree =
        new(@"(?<!Try)AddScoped<DelegationWorktreeService>", RegexOptions.Compiled);

    private static readonly Regex HandRolledGitWorkspaceOneLiner =
        new(@"(?<!Try)AddSingleton<GitWorkspaceService>", RegexOptions.Compiled);

    private static readonly HashSet<string> GitWorkspaceOneLinerAllowlist =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "DelegationTestServices.cs",
            "DelegationTestServicesTests.cs",
        };

    [Test]
    public void RuleA_no_hand_rolled_DelegationWorktreeService_scoped_registration()
    {
        var hits = Scan((_, source) => HandRolledDelegationWorktree.IsMatch(source));
        hits.ShouldBeEmpty(
            "AddScoped<DelegationWorktreeService> is illegal in this tree; use AddDelegationWorktreeGraph. Offenders: "
            + Format(hits));
    }

    [Test]
    public void RuleB_dispatcher_harnesses_call_AddDelegationWorktreeGraph()
    {
        var hits = Scan((_, source) =>
            source.Contains("AddScoped<AgentTaskDispatcher>", StringComparison.Ordinal)
            && !source.Contains("AddDelegationWorktreeGraph", StringComparison.Ordinal));
        hits.ShouldBeEmpty(
            "AddScoped<AgentTaskDispatcher> requires AddDelegationWorktreeGraph in the same file. Offenders: "
            + Format(hits));
    }

    [Test]
    public void RuleC_reply_harnesses_register_git_via_helper_or_BridgeQueueHarness()
    {
        var hits = Scan((_, source) =>
            source.Contains("AddSingleton<AgentTaskReplyService>", StringComparison.Ordinal)
            && !source.Contains("AddDelegationWorktreeGraph", StringComparison.Ordinal)
            && !source.Contains("AddGitWorkspaceService", StringComparison.Ordinal)
            && !source.Contains("BridgeQueueHarness", StringComparison.Ordinal));
        hits.ShouldBeEmpty(
            "AddSingleton<AgentTaskReplyService> requires AddDelegationWorktreeGraph, AddGitWorkspaceService, or BridgeQueueHarness. Offenders: "
            + Format(hits));
    }

    [Test]
    public void RuleD_GitWorkspaceService_one_liner_only_in_the_helper_and_its_pin()
    {
        var hits = Scan((path, source) =>
            HandRolledGitWorkspaceOneLiner.IsMatch(source)
            && !GitWorkspaceOneLinerAllowlist.Contains(Path.GetFileName(path)));
        hits.ShouldBeEmpty(
            "AddSingleton<GitWorkspaceService> belongs only in DelegationTestServices.cs and DelegationTestServicesTests.cs. Offenders: "
            + Format(hits));
    }

    private static List<string> Scan(Func<string, string, bool> isHit)
    {
        var testsRoot = Path.Combine(RepoRoot, "tests", "Antiphon.Tests");
        var hits = new List<string>();

        foreach (var path in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(path))
                continue;
            if (string.Equals(Path.GetFileName(path), "DelegationHarnessCensusTests.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var source = File.ReadAllText(path);
            if (isHit(path, source))
                hits.Add(Path.GetRelativePath(RepoRoot, path).Replace('\\', '/'));
        }

        return hits;
    }

    private static string Format(IReadOnlyList<string> hits) =>
        hits.Count == 0 ? "(none)" : string.Join(", ", hits);

    private static bool IsBuildOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p =>
            p.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || p.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("bin-", StringComparison.OrdinalIgnoreCase));
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;

            return dir?.FullName
                ?? throw new DirectoryNotFoundException(
                    "Could not locate repo root (Antiphon.sln) from test base dir.");
        }
    }
}
