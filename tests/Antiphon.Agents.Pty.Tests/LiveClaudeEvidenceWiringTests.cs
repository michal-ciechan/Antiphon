using Antiphon.TestSupport.LiveClaude;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>Portable sanitizer witnesses. No native process, provider or live admission.</summary>
[Category("Unit"), Timeout(30_000)]
[ParallelLimiter<ProcessSpawnLimit>]
public class LiveClaudeEvidenceWiringTests
{
    private static readonly (string Path, string Method)[] Callers =
    [
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs", "Control_auto_connects_a_bridge_session_before_the_first_user"),
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs", "Settings_file_prevents_auto_connect_and_keeps_startup_records"),
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs", "Remote_control_slash_command_still_arms_after_settings_off"),
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractLiveTests.cs", "Submitting_via_two_writes_completes_a_turn"),
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractTests.cs", "SubmitClaudeAsync"),
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlMenuCanaryTests.cs", "C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt"),
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlMenuCanaryTests.cs", "One_Esc_dismisses_the_remote_control_management_menu")
    ];

    [Test]
    public void EvidenceIsSanitized()
    {
        var sanitizer = typeof(LiveClaudeExecutable).Assembly.GetType(
            "Antiphon.TestSupport.LiveClaude.LiveClaudeEvidenceSanitizer");
        if (sanitizer?.GetMethod("Project") is null)
            throw new InvalidOperationException("C1545 V-5/entry required sanitizer is absent");
        throw new InvalidOperationException("C1545 V-5/link payloadHasSentinel");
    }

    [Test]
    public void CanarySinksCannotBypassSanitizer()
    {
        var recognized = 0;
        var rawConsole = 0;
        var rawAssertion = 0;
        var rawSkip = 0;
        var rawActual = 0;
        foreach (var caller in Callers)
        {
            var body = LiveClaudeExecutableAudit.Method(LiveClaudeExecutableAudit.Read(caller.Path), caller.Method).Body
                ?? throw new InvalidOperationException("C1545 V-6/no-body");
            var tainted = Tainted(body);
            recognized += body.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Count(call => call.Expression.ToString().StartsWith("LiveClaudeEvidence.", StringComparison.Ordinal));
            foreach (var call in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = call.Expression.ToString();
                var sinks = call.ArgumentList.Arguments.Any(arg => UnsafeText(arg.Expression, tainted))
                    || UnsafeText(call.Expression, tainted);
                if (!sinks) continue;
                if (name.Contains("WriteLine", StringComparison.Ordinal)) rawConsole++;
                else if (name.Contains("SkipTestException", StringComparison.Ordinal) || name == "SkipTestException") rawSkip++;
                else if (name.Contains("Should", StringComparison.Ordinal))
                {
                    if (UnsafeText(call.Expression, tainted)) rawActual++;
                    if (call.ArgumentList.Arguments.Any(arg => UnsafeText(arg.Expression, tainted))) rawAssertion++;
                }
            }
            foreach (var thrown in body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                if (thrown.Type.ToString() != "SkipTestException") continue;
                if (thrown.ArgumentList?.Arguments.Any(arg => UnsafeText(arg.Expression, tainted)) == true) rawSkip++;
            }
        }
        if (rawConsole != 0)
            throw new InvalidOperationException("C1545 V-6/raw-console unprotectedSuccessSinkCount");
        if (rawAssertion != 0)
            throw new InvalidOperationException("C1545 V-6/raw-assertion unprotectedAssertionSinkCount");
        if (rawSkip != 0)
            throw new InvalidOperationException("C1545 V-6/raw-skip unprotectedSkipSinkCount");
        if (rawActual != 0)
            throw new InvalidOperationException("C1545 V-6/raw-actual rawAssertionOperandCount");
        if (recognized == 0)
            throw new InvalidOperationException("C1545 V-6/raw-console unprotectedSuccessSinkCount");
        var menu = LiveClaudeExecutableAudit.Method(
            LiveClaudeExecutableAudit.Read(Callers[6].Path), Callers[6].Method).ToString();
        if (!menu.Contains("LiveClaudeEvidence.Success", StringComparison.Ordinal)
            || !menu.Contains("LiveClaudeEvidence.Assertion", StringComparison.Ordinal)
            || !menu.Contains("LiveClaudeEvidence.Skip", StringComparison.Ordinal))
            throw new InvalidOperationException("C1545 V-6/raw-console unprotectedSuccessSinkCount");
    }

    private static HashSet<string> Tainted(BlockSyntax body)
    {
        var tainted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var local in body.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (local.Initializer?.Value.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                .Any(call => call.Expression.ToString().EndsWith("SnapshotScreen", StringComparison.Ordinal)) == true)
                tainted.Add(local.Identifier.ValueText);
        }
        return tainted;
    }

    private static bool UnsafeText(ExpressionSyntax expression, HashSet<string> tainted)
    {
        if (expression is InvocationExpressionSyntax call
            && call.Expression.ToString().StartsWith("LiveClaudeEvidence.", StringComparison.Ordinal))
            return false;
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            var textJoin = node is InterpolatedStringExpressionSyntax
                || node is BinaryExpressionSyntax binary && binary.OperatorToken.Text == "+";
            if (textJoin && node.DescendantNodes().Any(inner => CarriesScreen(inner, tainted)))
                return true;
        }
        if (expression is InvocationExpressionSyntax should
            && should.Expression.ToString().Contains("Should", StringComparison.Ordinal)
            && should.Expression.DescendantNodesAndSelf().Any(node => CarriesScreen(node, tainted)))
            return true;
        return false;
    }

    private static bool CarriesScreen(SyntaxNode node, HashSet<string> tainted) =>
        node is InvocationExpressionSyntax screen
            && screen.Expression.ToString().EndsWith("SnapshotScreen", StringComparison.Ordinal)
        || node is IdentifierNameSyntax name && tainted.Contains(name.Identifier.ValueText);
}
