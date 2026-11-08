using Antiphon.GitInProcess;
using System.Diagnostics;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-1432 S1 (R3, V-1/V-2): no server git child runs auto gc or auto maintenance.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ServerGitProcessTests
{
    [Test]
    public async Task C1432_land_git_reads_gc_auto_zero()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await fixture.RequiredAsync(fixture.Repository, "config", "gc.auto", "6700");
        await fixture.RequiredAsync(fixture.Repository, "config", "maintenance.auto", "true");

        (await ReadAsync(fixture, "gc.auto")).ShouldBe("0", "the repository's own gc.auto=6700 must be overridden");
        (await ReadAsync(fixture, "maintenance.auto")).ShouldBe("false");
        // The fixture's GIT_CONFIG_COUNT=3 entries were appended to, not replaced.
        (await ReadAsync(fixture, "commit.gpgSign")).ShouldBe("false");
    }

    [Test]
    public void C1432_append_preserves_existing_entries()
    {
        var start = new ProcessStartInfo("git");
        start.Environment["GIT_CONFIG_COUNT"] = "2";
        start.Environment["GIT_CONFIG_KEY_0"] = "commit.gpgSign";
        start.Environment["GIT_CONFIG_VALUE_0"] = "false";
        start.Environment["GIT_CONFIG_KEY_1"] = "core.hooksPath";
        start.Environment["GIT_CONFIG_VALUE_1"] = "/none";
        start.ArgumentList.Add("status");

        ServerGitProcess.DisableAutoMaintenance(start);

        start.Environment["GIT_CONFIG_COUNT"].ShouldBe("4");
        start.Environment["GIT_CONFIG_KEY_0"].ShouldBe("commit.gpgSign");
        start.Environment["GIT_CONFIG_VALUE_0"].ShouldBe("false");
        start.Environment["GIT_CONFIG_KEY_1"].ShouldBe("core.hooksPath");
        start.Environment["GIT_CONFIG_VALUE_1"].ShouldBe("/none");
        start.Environment["GIT_CONFIG_KEY_2"].ShouldBe("gc.auto");
        start.Environment["GIT_CONFIG_VALUE_2"].ShouldBe("0");
        start.Environment["GIT_CONFIG_KEY_3"].ShouldBe("maintenance.auto");
        start.Environment["GIT_CONFIG_VALUE_3"].ShouldBe("false");
        start.ArgumentList.ShouldBe(["status"], "the argument vector the test seams read is unchanged");

        var empty = new ProcessStartInfo("git");
        empty.Environment.Remove("GIT_CONFIG_COUNT");
        ServerGitProcess.DisableAutoMaintenance(empty);
        empty.Environment["GIT_CONFIG_COUNT"].ShouldBe("2");
        empty.Environment["GIT_CONFIG_KEY_0"].ShouldBe("gc.auto");
        empty.Environment["GIT_CONFIG_KEY_1"].ShouldBe("maintenance.auto");
    }

    [Test]
    [Arguments("x")]
    [Arguments("-1")]
    [Arguments(" ")]
    public void C1432_invalid_count_throws(string count)
    {
        var start = new ProcessStartInfo("git");
        start.Environment["GIT_CONFIG_COUNT"] = count;
        Should.Throw<InvalidOperationException>(() => ServerGitProcess.DisableAutoMaintenance(start))
            .Message.ShouldBe("git_config_count_invalid");
        start.Environment["GIT_CONFIG_COUNT"].ShouldBe(count, "an invalid count is never overwritten");
    }

    // D-2: GitTool (agent-supplied git) and the runner are deliberately excluded.
    private static readonly (string File, int Constructions, int Launches)[] Roster =
    [
        ("server/Infrastructure/Git/LandingGit.cs", 3, 2),
        ("server/Infrastructure/Git/GitService.cs", 2, 2),
        ("server/Infrastructure/Git/WorktreeManager.cs", 2, 1),
        ("server/Infrastructure/Git/CardFileRepository.cs", 1, 1),
        ("server/Application/Services/GitWorkspaceService.cs", 3, 2),
        ("server/Application/Services/DelegationWorktreeService.cs", 1, 1),
        ("server/Application/Services/DelegationWorkspaceResolver.cs", 1, 1),
        ("server/Application/Services/OrchestratorWorkspaceLayout.cs", 1, 1),
        ("server/Application/Services/AgentTaskReplyService.cs", 1, 1),
        ("server/Infrastructure/ExternalChanges/ChangeDetectionService.cs", 1, 1),
    ];

    [Test]
    public void C1432_every_server_git_start_site_disables_auto_maintenance()
    {
        Roster.Sum(r => r.Constructions).ShouldBe(16, "thirteen launches plus three non-launching eligibility probes");
        Roster.Sum(r => r.Launches).ShouldBe(13, "T-1 names thirteen server git start sites");
        var root = RepositoryRoot();
        var missing = new List<string>();
        foreach (var (file, constructions, launches) in Roster)
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            var audit = AuditStartSites(text, file);
            audit.Constructions.ShouldBe(constructions, $"{file} ProcessStartInfo constructions");
            audit.Launches.ShouldBe(launches, $"{file} actual launches (eligibility probes are separate)");
            missing.AddRange(audit.Failures);
        }
        missing.ShouldBeEmpty();
    }

    [Test]
    [Arguments("LandingGit.cs", "InProcessEligible")]
    [Arguments("WorktreeManager.cs", "InProcessReadsEligible")]
    [Arguments("GitWorkspaceService.cs", "InProcessReadsEligible")]
    public void C1432_scanner_accepts_a_non_launching_probe_and_two_guarded_launches(string file, string method)
    {
        var source = ScannerSource.Replace("InProcessEligible", method);
        var audit = AuditStartSites(source, file);
        audit.Constructions.ShouldBe(3);
        audit.Launches.ShouldBe(2);
        audit.Failures.ShouldBeEmpty();
    }

    [Test]
    [Arguments("ExecuteProcessAsync")]
    [Arguments("RunGitInputBytesAsync")]
    public void C1432_scanner_names_each_unguarded_launch(string method)
    {
        var source = ScannerSource.Replace($"ServerGitProcess.DisableAutoMaintenance({(method == "ExecuteProcessAsync" ? "start" : "input")});", "");
        var failures = ScanStartSites(source, "LandingGit.cs");
        failures.Count.ShouldBe(1);
        failures[0].ShouldContain(method);
    }

    [Test]
    public void C1432_scanner_rejects_a_third_unguarded_launch()
    {
        var source = ScannerSource.Replace("// extra site", "void ThirdLaunch() { var third = new ProcessStartInfo(\"git\"); Process.Start(third); }");
        var failures = ScanStartSites(source, "LandingGit.cs");
        failures.Count.ShouldBe(1);
        failures[0].ShouldContain("ThirdLaunch");
    }

    [Test]
    [Arguments("comment")]
    [Arguments("wrong-target")]
    [Arguments("late")]
    [Arguments("conditional")]
    [Arguments("reconfigured")]
    public void C1432_scanner_rejects_guards_that_do_not_protect_the_launch(string shape)
    {
        const string guard = "ServerGitProcess.DisableAutoMaintenance(start);";
        var replacement = shape switch
        {
            "comment" => "/* " + guard + " */",
            "wrong-target" => "ServerGitProcess.DisableAutoMaintenance(other);",
            "late" => "",
            "conditional" => "if (enabled) { " + guard + " }",
            _ => guard + " ConfigureProcess(start);",
        };
        var source = ScannerSource.Replace(guard, replacement);
        if (shape == "late") source = source.Replace("StartProcess(start);", "StartProcess(start); " + guard);
        var failures = ScanStartSites(source, "LandingGit.cs");
        failures.Count.ShouldBe(1);
        failures[0].ShouldContain("ExecuteProcessAsync/start");
    }

    [Test]
    [Arguments("LandingGit.cs", "InProcessEligible")]
    [Arguments("WorktreeManager.cs", "InProcessReadsEligible")]
    [Arguments("GitWorkspaceService.cs", "InProcessReadsEligible")]
    public void C1432_scanner_rejects_a_launching_probe(string file, string method)
    {
        foreach (var launch in new[] { "Process.Start(probe);", "StartProcess(probe);",
            "var child = new Process(); child.StartInfo = probe; child.Start();", "LaunchElsewhere(probe);", "saved = probe;" })
        {
            var source = ScannerSource.Replace("InProcessEligible", method)
                .Replace("ConfigureProcess(probe);", "ConfigureProcess(probe); " + launch);
            var failures = ScanStartSites(source, file);
            failures.Count.ShouldBe(1, launch);
            failures[0].ShouldContain(method);
            failures[0].ShouldContain("probe");
        }
    }

    // CARD-1776 gave the LandingGit probe ApplyIdentity so the probe and the child agree on
    // GIT_CONFIG_GLOBAL; CARD-1787 made the helper scrub the inherited git env first.
    private const string IdentityHelper = """
        void ApplyIdentity(ProcessStartInfo start) { if (_identityFile is null) return; ServerGitIdentity.ScrubInherited(start.Environment); start.Environment["GIT_CONFIG_GLOBAL"] = _identityFile; }
        """;

    private static string ProbeWithIdentity(string helper)
        => ScannerSource.Replace("ConfigureProcess(probe);", "ConfigureProcess(probe); ApplyIdentity(probe);")
            .Replace("// extra site", helper);

    [Test]
    public void C1776_scanner_accepts_the_land_probe_identity_helper()
    {
        var audit = AuditStartSites(ProbeWithIdentity(IdentityHelper), "LandingGit.cs");
        audit.Constructions.ShouldBe(3);
        audit.Launches.ShouldBe(2);
        audit.Failures.ShouldBeEmpty();
    }

    [Test]
    public void C1776_scanner_rejects_an_identity_helper_that_launches_or_escapes()
    {
        foreach (var leak in new[] { "Process.Start(start);", "StartProcess(start);", "saved = start;", "LaunchElsewhere(start);",
            "var child = new Process(); child.StartInfo = start; child.Start();" })
        {
            var helper = IdentityHelper.Replace("ServerGitIdentity.ScrubInherited", leak + " ServerGitIdentity.ScrubInherited");
            var failures = ScanStartSites(ProbeWithIdentity(helper), "LandingGit.cs");
            failures.Count.ShouldBe(1, leak);
            failures[0].ShouldContain("InProcessEligible/probe");
        }
    }

    [Test]
    public void C1776_scanner_rejects_an_identity_helper_it_cannot_inspect()
    {
        // Undeclared, overloaded, or allowed in a file whose probe never had one.
        ScanStartSites(ProbeWithIdentity(""), "LandingGit.cs").Count.ShouldBe(1);
        ScanStartSites(ProbeWithIdentity(IdentityHelper + " void ApplyIdentity(ProcessStartInfo a, bool b) { }"), "LandingGit.cs")
            .Count.ShouldBe(1);
        var other = ProbeWithIdentity(IdentityHelper).Replace("InProcessEligible", "InProcessReadsEligible");
        ScanStartSites(other, "WorktreeManager.cs").Count.ShouldBe(1);
    }

    private const string ScannerSource = """
        class LandingGit {
            bool InProcessEligible() { var probe = new ProcessStartInfo("git"); ConfigureProcess(probe); ServerGitProcess.DisableAutoMaintenance(probe); return InProcessGitReads.ChildReadsSameRepository(probe); }
            void ExecuteProcessAsync() { var start = new ProcessStartInfo("git"); ConfigureProcess(start); ServerGitProcess.DisableAutoMaintenance(start); StartProcess(start); }
            void RunGitInputBytesAsync() { var input = new ProcessStartInfo("git"); ConfigureProcess(input); ServerGitProcess.DisableAutoMaintenance(input); Process.Start(input); }
            // extra site
        }
        """;

    private static List<string> ScanStartSites(string text, string file)
        => AuditStartSites(text, file).Failures;

    private static (int Constructions, int Launches, List<string> Failures) AuditStartSites(string text, string file)
    {
        var missing = new List<string>();
        var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview));
        foreach (var error in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
            missing.Add($"{file}: invalid C# source: {error}");
        var starts = tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Where(node => node.Type.ToString() is "ProcessStartInfo" or "System.Diagnostics.ProcessStartInfo").ToList();
        var launchCount = 0;
        foreach (var start in starts)
        {
            var method = start.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            var target = start.Parent switch
            {
                EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable } => variable.Identifier.ValueText,
                AssignmentExpressionSyntax assignment when assignment.Right == start => assignment.Left.ToString(),
                _ => null,
            };
            var site = $"{file}:{LineOf(text, start.SpanStart)} {method?.Identifier.ValueText ?? "<outside method>"}/{target ?? "<unknown target>"}";
            if (method is null || target is null)
            {
                missing.Add(site + ": unrecognized construction ownership");
                continue;
            }
            var calls = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
            if (target == "probe" && (Path.GetFileName(file), method.Identifier.ValueText) is
                ("LandingGit.cs", "InProcessEligible")
                or ("WorktreeManager.cs", "InProcessReadsEligible")
                or ("GitWorkspaceService.cs", "InProcessReadsEligible"))
            {
                // Only these exact constructions are allowed to be non-launching. Their shared
                // eligibility helper only inspects the executable and environment. Forbid launches
                // anywhere in each method, including through an aliased Process.StartInfo.
                var escapes = method.DescendantNodes().OfType<IdentifierNameSyntax>()
                    .Where(id => id.Identifier.ValueText == "probe")
                    .Any(id => !(id.Parent is MemberAccessExpressionSyntax member && member.Expression == id)
                        && !(id.Parent is ArgumentSyntax argument && argument.Parent?.Parent is InvocationExpressionSyntax call
                            && call.Expression.ToString() is "ConfigureProcess" or "ServerGitProcess.DisableAutoMaintenance"
                                or "InProcessGitReads.ChildReadsSameRepository"
                                or "Antiphon.GitInProcess.InProcessGitReads.ChildReadsSameRepository"));
                if (calls.Any(call => LaunchTarget(call) is not null) || escapes)
                    missing.Add(site + ": eligibility probe must never launch or escape to a launch helper");
                continue;
            }

            var launches = calls.Where(call => LaunchTarget(call) == target).ToList();
            launchCount += launches.Count;
            if (launches.Count != 1)
                missing.Add(site + $": expected one actual launch, found {launches.Count}");
            foreach (var launch in launches)
            {
                var configuration = calls.Where(call => call.Expression.ToString() == "ConfigureProcess"
                    && call.ArgumentList.Arguments.SingleOrDefault()?.Expression.ToString() == target).ToList();
                var guarded = calls.Any(call => call.Expression.ToString() == "ServerGitProcess.DisableAutoMaintenance"
                    && call.ArgumentList.Arguments.SingleOrDefault()?.Expression.ToString() == target
                    && PrecedesOnSamePath(call, launch)
                    && configuration.All(config => PrecedesOnSamePath(config, call)));
                if (!guarded)
                    missing.Add(site + $": missing own auto-maintenance guard after configuration and before {launch.Expression}");
            }
        }
        return (starts.Count, launchCount, missing);
    }

    private static bool PrecedesOnSamePath(InvocationExpressionSyntax before, InvocationExpressionSyntax after)
        => before.Parent is ExpressionStatementSyntax { Parent: BlockSyntax block }
            && before.SpanStart < after.SpanStart && after.Ancestors().Contains(block);

    private static string? LaunchTarget(InvocationExpressionSyntax call)
    {
        if (call.Expression is IdentifierNameSyntax { Identifier.ValueText: "StartProcess" }
            || call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "StartProcess" })
            return call.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString();
        if (call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Start" } member)
            return null;
        if (member.Expression.ToString() is "Process" or "System.Diagnostics.Process")
            return call.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString();
        return call.ArgumentList.Arguments.Count == 0 ? member.Expression + ".StartInfo" : null;
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static async Task<string> ReadAsync(LandingGitFixture fixture, string key)
    {
        var result = await fixture.Git.RunAsync(fixture.Repository, ["config", "--get", key], CancellationToken.None);
        result.Succeeded.ShouldBeTrue(key);
        return result.Output.Trim();
    }
}
