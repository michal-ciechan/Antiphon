using Antiphon.Tests.Application;
using Antiphon.Tests.Infrastructure;
using Antiphon.TestSupport;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;
using SkipTestException = TUnit.Core.Exceptions.SkipTestException;

namespace Antiphon.Tests.TestHelpers;

// CARD-0588: three fixtures asserted Windows and went red on Linux. Each now skips off Windows
// with a named reason before any work, and keeps its Windows prerequisite assertions.
[Category("Unit")]
public sealed class NativeWindowsFixtureGateTests
{
    private const string WorktreeLockReason = "CARD-0588: C443 native sharing qualification needs Windows DELETE-access sharing (native error 32); WorktreeNativeIO is unsupported off Windows.";
    private const string LandDeliveryReason = "CARD-0588: the C467 land-delivery fixture drives the native fakegrok.exe through the shipped Windows ConPTY pair.";
    private const string CanaryReason = "CARD-0588: the C419 apply canary pins the modern ConPTY reply ceiling (ReplyInlineMaxChars 14400), which is Windows-only.";

    private static readonly (string File, string Class, string Method, string Reason)[] Sites =
    [
        ("tests/Antiphon.Tests/Infrastructure/WorktreeLockDiagnosticsWindowsTests.cs", "WorktreeLockDiagnosticsWindowsTests", "NativeSharingAsync", WorktreeLockReason),
        ("tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs", "LandDeliveryFixture", "InitializeAsync", LandDeliveryReason),
        ("tests/Antiphon.E2E/OutputDistillationApplyCanaryTests.cs", "OutputDistillationApplyCanaryTests", "Real_apply_and_long_fallback_reach_parent_and_files_survive_cleanup", CanaryReason),
    ];

    [Test]
    public void C588_Gate_skips_off_windows_with_named_reason()
    {
        Should.Throw<SkipTestException>(() => NativeWindowsGate.Require("c588-literal", isWindows: false))
            .Message.ShouldBe("c588-literal");
        Should.NotThrow(() => NativeWindowsGate.Require("c588-literal", isWindows: true));
    }

    [Test]
    public async Task C588_Worktree_lock_native_fixture_skips_before_work_off_windows()
    {
        if (OperatingSystem.IsWindows())
        {
            // On Windows the fixture runs for real in its own class; here only the admission arm is observable.
            Should.NotThrow(() => NativeWindowsGate.Require(WorktreeLockReason));
            return;
        }
        var fixture = new WorktreeLockDiagnosticsWindowsTests();
        (await Should.ThrowAsync<SkipTestException>(() => fixture.C443_NativeFileSharing32())).Message.ShouldBe(WorktreeLockReason);
        (await Should.ThrowAsync<SkipTestException>(() => fixture.C443_NativeRootDirectorySharing32())).Message.ShouldBe(WorktreeLockReason);
    }

    [Test]
    [Arguments("WorktreeLockDiagnosticsWindowsTests")]
    [Arguments("LandDeliveryFixture")]
    [Arguments("OutputDistillationApplyCanaryTests")]
    public void C588_Named_fixture_gates_before_work_and_keeps_windows_prerequisites(string fixture)
    {
        var site = Sites.Single(s => s.Class == fixture);
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, site.File))).GetRoot();
        var type = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == site.Class);
        var reason = type.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(f => f.Declaration.Variables, (f, v) => (Field: f, Variable: v))
            .SingleOrDefault(x => x.Variable.Identifier.ValueText == "NativeWindowsReason");
        reason.Variable.ShouldNotBeNull("c588-reason-const: " + site.Class);
        reason.Field.Modifiers.Any(SyntaxKind.ConstKeyword).ShouldBeTrue("c588-reason-const: " + site.Class);
        var literal = reason.Variable.Initializer?.Value as LiteralExpressionSyntax;
        literal.ShouldNotBeNull("c588-reason-literal: " + site.Class);
        literal.Token.ValueText.ShouldBe(site.Reason, "c588-reason-literal: " + site.Class);
        var method = type.Members.OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == site.Method);
        SyntaxFactory.AreEquivalent(method.Body?.Statements.FirstOrDefault(),
            SyntaxFactory.ParseStatement("NativeWindowsGate.Require(NativeWindowsReason);"))
            .ShouldBeTrue("c588-gate-first: " + site.Class + "." + site.Method);
        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(i => i.ToString()).ToArray();
        invocations.ShouldNotContain(i => i.Contains("IsWindows()", StringComparison.Ordinal) && i.Contains("ShouldBeTrue", StringComparison.Ordinal),
            "c588-no-windows-assertion: " + site.Class + "." + site.Method);
        if (site.Class != "WorktreeLockDiagnosticsWindowsTests")
            invocations.ShouldContain("ConPtyRedistributable.TryLocate(out _, out var why).ShouldBeTrue(why)",
                "c588-windows-prerequisite-kept: " + site.Class + "." + site.Method);
    }
}
