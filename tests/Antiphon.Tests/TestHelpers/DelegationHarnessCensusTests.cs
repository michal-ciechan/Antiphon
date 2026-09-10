using System.Text;
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
        var hits = Scan((_, source) => DelegationHarnessOwnership.IsBareDispatcherRegistration(source));
        hits.ShouldBeEmpty(
            "AddScoped<AgentTaskDispatcher> requires AddDelegationWorktreeGraph or construction of a recognized graph owner. Offenders: "
            + Format(hits));
    }

    [Test]
    public void C475_RuleBRejectsBareDispatcher()
    {
        const string source = """
            var services = new ServiceCollection();
            services.AddScoped<AgentTaskDispatcher>();
            """;
        DelegationHarnessOwnership.IsBareDispatcherRegistration(source).ShouldBeTrue();
        DelegationHarnessOwnership.OwnsDispatcherGraph(source).ShouldBeFalse(
            "bare AddScoped<AgentTaskDispatcher> cannot be classified as graph ownership");
    }

    [Test]
    public void C475_RuleBRejectsMentionOnlyHelper()
    {
        DelegationHarnessOwnership.OwnsDispatcherGraph("""
            // new LandingSafetyHarness()
            services.AddScoped<AgentTaskDispatcher>();
            """).ShouldBeFalse("comments are not construction");
        DelegationHarnessOwnership.OwnsDispatcherGraph("""
            var name = "LandingSafetyHarness";
            services.AddScoped<AgentTaskDispatcher>();
            """).ShouldBeFalse("string mentions are not construction");
        DelegationHarnessOwnership.OwnsDispatcherGraph("""
            _ = typeof(LandingSafetyHarness);
            services.AddScoped<AgentTaskDispatcher>();
            """).ShouldBeFalse("type-only mentions are not construction");
        DelegationHarnessOwnership.OwnsDispatcherGraph("""
            // LandingProtocolHarness owns the graph
            services.AddScoped<AgentTaskDispatcher>();
            """).ShouldBeFalse();
        DelegationHarnessOwnership.IsBareDispatcherRegistration("""
            // new LandingSafetyHarness()
            services.AddScoped<AgentTaskDispatcher>();
            """).ShouldBeTrue();
    }

    [Test]
    public void C475_RecognizedOwnersActuallyRegisterTheGraph()
    {
        foreach (var file in new[] { "LandingSafetyHarness.cs", "LandingProtocolHarness.cs" })
        {
            var path = Path.Combine(RepoRoot, "tests", "Antiphon.Tests", "TestHelpers", file);
            File.Exists(path).ShouldBeTrue(file + " must exist as a recognized graph owner");
            var source = File.ReadAllText(path);
            var code = DelegationHarnessOwnership.StripCommentsAndStrings(source);
            code.ShouldContain("AddDelegationWorktreeGraph", Case.Sensitive,
                file + " must actually invoke AddDelegationWorktreeGraph, not merely mention it");
            Regex.IsMatch(code, @"AddDelegationWorktreeGraph\s*\(").ShouldBeTrue(
                file + " must call AddDelegationWorktreeGraph(");
        }
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

/// <summary>
/// CARD-0475 S1: RuleB ownership is actual helper construction or graph registration, never a
/// comment, string, type mention or filename allowlist.
/// </summary>
internal static class DelegationHarnessOwnership
{
    private static readonly Regex Construction = new(
        @"\bnew\s+(LandingSafetyHarness|LandingProtocolHarness)\s*[\(<]",
        RegexOptions.Compiled);

    private static readonly Regex GraphCall = new(
        @"AddDelegationWorktreeGraph\s*\(",
        RegexOptions.Compiled);

    public static bool OwnsDispatcherGraph(string source)
    {
        var code = StripCommentsAndStrings(source);
        return GraphCall.IsMatch(code) || Construction.IsMatch(code);
    }

    public static bool IsBareDispatcherRegistration(string source)
    {
        var code = StripCommentsAndStrings(source);
        return code.Contains("AddScoped<AgentTaskDispatcher>", StringComparison.Ordinal)
            && !OwnsDispatcherGraph(source);
    }

    public static string StripCommentsAndStrings(string source)
    {
        var sb = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            if (i + 2 < source.Length && source[i] == '"' && source[i + 1] == '"' && source[i + 2] == '"')
            {
                i += 3;
                while (i + 2 < source.Length && !(source[i] == '"' && source[i + 1] == '"' && source[i + 2] == '"'))
                    i++;
                i += 2;
                sb.Append(' ');
                continue;
            }
            if (source[i] == '@' && i + 1 < source.Length && source[i + 1] == '"')
            {
                i += 2;
                while (i < source.Length)
                {
                    if (source[i] == '"' && i + 1 < source.Length && source[i + 1] == '"') { i += 2; continue; }
                    if (source[i] == '"') break;
                    i++;
                }
                sb.Append(' ');
                continue;
            }
            if (source[i] == '"')
            {
                i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\' && i + 1 < source.Length) { i += 2; continue; }
                    if (source[i] == '"') break;
                    i++;
                }
                sb.Append(' ');
                continue;
            }
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') i++;
                if (i < source.Length) sb.Append('\n');
                continue;
            }
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                i++;
                continue;
            }
            sb.Append(source[i]);
        }
        return sb.ToString();
    }
}
