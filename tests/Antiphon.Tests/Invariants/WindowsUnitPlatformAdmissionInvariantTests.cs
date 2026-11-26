using System.Reflection;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Agents;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;
using Entry = Antiphon.Tests.Scripts.WindowsUnitSkipRoster.Entry;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class WindowsUnitPlatformAdmissionInvariantTests
{
    [Test]
    public void C1210_Native_linux_entries_guard_before_work()
    {
        var methods = SourceMethods();
        var observed = WindowsUnitPlatformAdmissionTests.WindowsShapedHostObservations();
        WindowsUnitSkipRoster.Native.Length.ShouldBe(observed.Distinct(StringComparer.Ordinal).Count(), "c1210-native-identity-count");
        WindowsUnitSkipRoster.Native.Sum(entry => entry.Results).ShouldBe(observed.Length, "c1210-native-result-count");
        foreach (var entry in WindowsUnitSkipRoster.Native)
        {
            var matches = methods.Where(method => Identity(method) == entry.Identity).ToArray();
            matches.Length.ShouldBe(1, "c1210-first-statement: " + entry.Identity);
            SyntaxFactory.AreEquivalent(matches[0].Body?.Statements.FirstOrDefault(),
                SyntaxFactory.ParseStatement("NativeLinuxGate.Require(NativeLinuxReason);"))
                .ShouldBeTrue("c1210-first-statement: " + entry.Identity);
        }
    }

    [Test]
    public void C1210_Roster_closure_matches_source_gates()
    {
        var roots = SourceRoots();
        var expected = WindowsUnitSkipRoster.Native.Concat(WindowsUnitSkipRoster.NativeSourceOnly)
            .Concat(WindowsUnitSkipRoster.Existing).ToArray();
        AssertClosure(roots, expected, true);
        Entry[] vectorRoster = [new("Antiphon.Tests.Scripts", "Probe", "Case", 1, "literal")];
        const string healthy = "namespace Antiphon.Tests.Scripts; class Probe { void Case() { C1008HostFixture.RequireNativeLinux(); } }";
        AssertClosure([CSharpSyntaxTree.ParseText(healthy).GetRoot()], vectorRoster, false);
        foreach (var source in new[]
        {
            healthy.Replace("void Case()", "void Unknown()", StringComparison.Ordinal),
            healthy.Replace("C1008HostFixture.RequireNativeLinux()", "global::Antiphon.Tests.TestHelpers.NativeLinuxGate.Require(string.Empty)", StringComparison.Ordinal).Replace("void Case()", "void Unknown()", StringComparison.Ordinal),
            "using Gate = Antiphon.Tests.TestHelpers.NativeLinuxGate; namespace Antiphon.Tests.Scripts; class Probe { void Unknown() { Gate.Require(\"reason\"); } }",
            "using static Antiphon.Tests.TestHelpers.NativeLinuxGate; namespace Antiphon.Tests.Scripts; class Probe { void Unknown() { Require(\"reason\"); } }",
            "namespace Antiphon.Tests.Scripts; class Probe { System.Action Property => () => NativeLinuxGate.Require(\"reason\"); }",
        })
            Should.Throw<ShouldAssertException>(() => AssertClosure([CSharpSyntaxTree.ParseText(source).GetRoot()], vectorRoster, false))
                .Message.ShouldContain("c1210-unrostered-gate");
        Should.Throw<ShouldAssertException>(() => AssertClosure([CSharpSyntaxTree.ParseText(
            healthy.Replace("C1008HostFixture.RequireNativeLinux();", "", StringComparison.Ordinal)).GetRoot()], vectorRoster, false))
            .Message.ShouldContain("c1210-roster-without-gate");
    }

    private static void AssertClosure(SyntaxNode[] roots, Entry[] expected, bool requireLegacyHelper)
    {
        var methods = roots.SelectMany(root => root.DescendantNodes().OfType<MethodDeclarationSyntax>()).ToArray();
        expected.Select(entry => entry.Identity).Distinct().Count().ShouldBe(expected.Length, "c1210-duplicate-roster");
        var actual = new List<string>();
        foreach (var call in roots.SelectMany(root => root.DescendantNodes().OfType<InvocationExpressionSyntax>()))
        {
            var name = GateName(call);
            if (name is null) continue;
            var method = call.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            method.ShouldNotBeNull("c1210-unrostered-gate: call outside a method");
            var identity = Identity(method);
            if (identity == "Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests.C1210_Guard_matches_native_platform"
                && name == "NativeLinuxGate.Require") continue;
            if (identity == "Antiphon.Tests.Scripts.C1008HostFixture.RequireNativeLinux" && name == "NativeLinuxGate.Require") continue;
            if (identity == "Antiphon.Tests.Scripts.C1008PlatformContractTests.C1050_Guard_matches_native_platform"
                && name == "C1008HostFixture.RequireNativeLinux") continue;
            if (identity == "Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105AuditContract"
                && name == "C1008HostFixture.RequireNativeLinux")
            {
                SyntaxFactory.AreEquivalent(method.Body?.Statements.FirstOrDefault(),
                    SyntaxFactory.ParseStatement("C1008HostFixture.RequireNativeLinux();"))
                    .ShouldBeTrue("c1210-roster-without-gate: helper edge");
                continue;
            }
            var entry = expected.SingleOrDefault(entry => entry.Identity == identity);
            entry.ShouldNotBeNull("c1210-unrostered-gate: " + identity);
            var expectedCall = entry.Helper ? "C1105AuditContract" : WindowsUnitSkipRoster.Native.Contains(entry)
                || WindowsUnitSkipRoster.NativeSourceOnly.Contains(entry)
                ? "NativeLinuxGate.Require" : "C1008HostFixture.RequireNativeLinux";
            name.ShouldBe(expectedCall, "c1210-roster-without-gate: " + identity);
            actual.Add(identity);
        }
        actual.Order(StringComparer.Ordinal).ToArray().ShouldBe(
            expected.Select(entry => entry.Identity).Order(StringComparer.Ordinal).ToArray(), "c1210-roster-without-gate");
        if (requireLegacyHelper) methods.Count(method => Identity(method) == "Antiphon.Tests.Scripts.RemoteScriptContractTests.C1105AuditContract"
            && method.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call => call.Expression.ToString() == "C1008HostFixture.RequireNativeLinux"))
            .ShouldBe(1, "c1210-roster-without-gate: helper implementation");
    }

    private static string? GateName(InvocationExpressionSyntax call)
    {
        var expression = call.Expression;
        if (expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "C1105AuditContract") return "C1105AuditContract";
        if (expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Require" or "RequireNativeLinux" }
            && expression is not IdentifierNameSyntax { Identifier.ValueText: "Require" or "RequireNativeLinux" }) return null;
        var root = call.SyntaxTree.GetRoot();
        var uses = root.DescendantNodes().OfType<UsingDirectiveSyntax>().ToArray();
        foreach (var (type, member, canonical) in new[] { ("NativeLinuxGate", "Require", "NativeLinuxGate.Require"),
                     ("C1008HostFixture", "RequireNativeLinux", "C1008HostFixture.RequireNativeLinux") })
        {
            if (expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == member)
            {
                var receiver = access.Expression.ToString();
                if (receiver == type || receiver.EndsWith("." + type, StringComparison.Ordinal)
                    || uses.Any(use => use.Alias?.Name.Identifier.ValueText == receiver
                        && (use.Name?.ToString() == type || use.Name?.ToString().EndsWith("." + type, StringComparison.Ordinal) == true))) return canonical;
            }
            if (expression is IdentifierNameSyntax unqualified && unqualified.Identifier.ValueText == member
                && uses.Any(use => use.StaticKeyword.RawKind != 0
                    && (use.Name?.ToString() == type || use.Name?.ToString().EndsWith("." + type, StringComparison.Ordinal) == true))) return canonical;
        }
        return null;
    }

    private static MethodDeclarationSyntax[] SourceMethods() => SourceRoots()
        .SelectMany(root => root.DescendantNodes().OfType<MethodDeclarationSyntax>()).ToArray();

    private static SyntaxNode[] SourceRoots() => CheckoutSources.Value;

    private static readonly Lazy<SyntaxNode[]> CheckoutSources = new(() => Directory.EnumerateFiles(
            Path.Combine(DelegateScriptRunner.RepoRoot, "tests/Antiphon.Tests"), "*.cs", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(DelegateScriptRunner.RepoRoot, path).Split(Path.DirectorySeparatorChar)
            .Any(part => part == "obj" || part == "bin" || part.StartsWith("bin-", StringComparison.Ordinal)))
        .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot()).ToArray());

    private static string Identity(MethodDeclarationSyntax method) =>
        (method.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "") + "."
        + (method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "") + "." + method.Identifier.ValueText;

    [Test]
    public void C1210_Host_roster_matches_windows_shaped_observation()
    {
        // The Windows child records [NativeLinuxOnly] skips for the classes its filter
        // selects. That observation is the host roster. A Linux-only class the filter
        // does not select is decided once by the shipped Windows exclusion, not by the roster.
        var observed = WindowsUnitPlatformAdmissionTests.WindowsShapedHostObservations();
        var expected = WindowsUnitSkipRoster.Native
            .SelectMany(entry => Enumerable.Repeat(entry.Identity, entry.Results))
            .Order(StringComparer.Ordinal)
            .ToArray();
        observed.ShouldBe(expected, "c1210-host-roster");

        string[] decided = ["VolumeRetirementContentTests", "VolumeRetirementDockerTests", "VolumeRetirementReceiptTests"];
        using var policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "tests/test-execution-policy.json")));
        var exclusions = policy.RootElement.GetProperty("profiles").GetProperty("rc").GetProperty("exclusions");
        foreach (var name in decided)
        {
            var rows = 0;
            foreach (var row in exclusions.EnumerateArray())
            {
                if (row.GetProperty("suite").GetString() != "antiphon" || row.GetProperty("class").GetString() != name) continue;
                if (!row.TryGetProperty("platform", out var platform) || platform.GetString() != "windows") continue;
                rows++;
            }
            rows.ShouldBe(1, "c1210-policy-decision: " + name);
            WindowsUnitSkipRoster.Native.Count(entry => entry.Class == name).ShouldBe(0, "c1210-host-roster: " + name);
        }
    }

    [Test]
    public void C1226_Pin_inspection_methods_skip_off_linux()
    {
        var path = Path.Combine(DelegateScriptRunner.RepoRoot,
            "tests/Antiphon.SessionRunner.Tests/AgentPinWorkspaceStoreTests.cs");
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText is "V32_InspectPosixPaths" or "V13_InspectPathSafety")
            .ToArray();
        methods.Select(method => method.Identifier.ValueText).Order(StringComparer.Ordinal).ToArray().ShouldBe(
            new[] { "V13_InspectPathSafety", "V32_InspectPosixPaths" }, "c1226-methods");
        var skip = SyntaxFactory.ParseStatement(
            "Skip.Unless(OperatingSystem.IsLinux(), \"CARD-1226: CP-4a requires native Linux qualification; POSIX paths and mkfifo cannot run on Windows, so this method skips.\");");
        var qualification = SyntaxFactory.ParseStatement(
            "OperatingSystem.IsLinux().ShouldBeTrue(\"CP-4a requires native Linux qualification\");");
        foreach (var method in methods)
        {
            var name = method.Identifier.ValueText;
            var body = method.Body;
            body.ShouldNotBeNull("c1226-body: " + name);
            var statements = body.Statements;
            SyntaxFactory.AreEquivalent(statements[0], skip).ShouldBeTrue("c1226-skip-first: " + name);
            statements.Any(statement => SyntaxFactory.AreEquivalent(statement, qualification))
                .ShouldBeTrue("c1226-linux-qualification: " + name);
        }
    }

    [Test]
    public void C2068_Windows_rc_selects_native_session_launchers()
    {
        var type = typeof(SessionRunnerRuntimeTests);
        var className = type.FullName!;
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.IsDefined(typeof(TestAttribute), inherit: false))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var nodes = methods.Select(method => NightlyPlanCases.Node(className, type.Name, method, method, "Integration")).ToArray();

        var policy = NightlyPlanCases.Policy();
        NightlyPolicy.ProfileRequiredSuites(policy.Object, "rc").ShouldContain("antiphon");
        var windowsExclusions = NightlyPlanCases.Exclusions(policy, "rc", "antiphon", "windows");
        var linuxExclusions = NightlyPlanCases.Exclusions(policy, "rc", "antiphon", "linux");
        var windows = NightlyCensus.RequiredUids(nodes, [className], windowsExclusions, profileAware: true).Select(PsValue.Str).ToArray();
        var linux = NightlyCensus.RequiredUids(nodes, [className], linuxExclusions, profileAware: true).Select(PsValue.Str).ToArray();

        windows.ShouldBe(methods);
        windows.Except(linux, StringComparer.Ordinal).ToArray().ShouldBe(new[]
        {
            nameof(SessionRunnerRuntimeTests.Session_id_can_be_relaunched_after_exit_but_not_while_running),
            nameof(SessionRunnerRuntimeTests.Session_runner_starts_shell_accepts_input_and_keeps_buffer),
            nameof(SessionRunnerRuntimeTests.StartAsync_that_fails_after_the_host_exists_leaves_no_pty_host),
        });
    }
}
