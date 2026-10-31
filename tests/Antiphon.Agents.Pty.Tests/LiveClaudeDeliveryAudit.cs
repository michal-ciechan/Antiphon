using Antiphon.TestSupport.LiveClaude;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Agents.Pty.Tests;

internal static class LiveClaudeDeliveryAudit
{
    internal static readonly (string Path, string Method, string Verdict)[] Callers = [
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractLiveTests.cs", "Submitting_via_two_writes_completes_a_turn", "LiveClaudeDeliveryEvidence.RequireSubmit(request, rows);"),
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlMenuCanaryTests.cs", "C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt", "LiveClaudeDeliveryEvidence.RequireMenu(request, rows, bridge, beforeEsc, afterEsc, afterSubmit);"),
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractTests.cs", "SubmitClaudeAsync", "LiveClaudeDeliveryEvidence.RequireSubmit(request, rows);")
    ];
    internal static void Check(string source, string method, string verdict)
    {
        var body = LiveClaudeStartupAudit.Method(source, method).Body
            ?? throw new InvalidOperationException("C1545 delivery-audit/no-body");
        var target = SyntaxFactory.ParseStatement(verdict);
        var matches = body.DescendantNodes().OfType<ExpressionStatementSyntax>()
            .Where(s => SyntaxFactory.AreEquivalent(s, target)).ToArray();
        if (matches.Length != 1 || matches[0].Parent != body)
            throw new InvalidOperationException("C1545 delivery-audit/outside-catch");
        var oracle = matches[0];
        // Straight-line final call: earlier control flow can only throw/refuse, never guard or
        // swallow the verdict. Local functions, loops, lambdas wrapping it and early returns refuse.
        foreach (var statement in body.Statements.TakeWhile(s => s != oracle))
            if (statement is not (LocalDeclarationStatementSyntax or ExpressionStatementSyntax)
                && statement is not IfStatementSyntax { Statement: ThrowStatementSyntax, Else: null })
                throw new InvalidOperationException("C1545 delivery-audit/reachability");
        var assertions = body.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(c =>
            c.Expression.ToString().Contains("Should", StringComparison.Ordinal)
            || c.Expression.ToString().Contains("Assert", StringComparison.Ordinal)
            || c.Expression.ToString().StartsWith("LiveClaudeDeliveryEvidence.Require", StringComparison.Ordinal)).ToArray();
        if (assertions.Length == 0 || assertions[^1].Parent != oracle)
            throw new InvalidOperationException("C1545 delivery-audit/last-assertion");
        if (assertions.Any(c => c.Parent != oracle))
            throw new InvalidOperationException("C1545 delivery-audit/no-screen-assertion");
        foreach (var after in body.Statements.SkipWhile(s => s != oracle).Skip(1))
            if (after is not ReturnStatementSyntax && !after.ToString().Contains("CleanupAsync", StringComparison.Ordinal))
                throw new InvalidOperationException("C1545 delivery-audit/after-verdict");
        var calls = body.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(c => c.Expression.ToString()).ToArray();
        if (method == "Submitting_via_two_writes_completes_a_turn" && !calls.Contains("ClaudeSubmitContractHarness.SubmitClaudeAsync")
            || method != "Submitting_via_two_writes_completes_a_turn" && !calls.Contains("LiveClaudeDeliveryEvidence.SubmitOnceAsync"))
            throw new InvalidOperationException("C1545 delivery-audit/reader-path");
    }
    internal static void Run()
    {
        foreach (var (path, method, verdict) in Callers) Check(LiveClaudeExecutableAudit.Read(path), method, verdict);
        var fake = LiveClaudeStartupAudit.Method(LiveClaudeExecutableAudit.Read(Callers[2].Path), "Text_and_CR_in_one_write_does_not_submit");
        if (!fake.ToString().Contains("DonePattern.IsMatch", StringComparison.Ordinal))
            throw new InvalidOperationException("C1545 delivery-audit/fake-contract");
        LiveClaudeStartupAudit.Menu(LiveClaudeExecutableAudit.Read(Callers[1].Path));
        LiveClaudeProfileAudit.PtyCallers();
    }
    internal static void Controls()
    {
        for (var i = 0; i < Callers.Length; i++)
        {
            var (path, method, verdict) = Callers[i];
            var original = LiveClaudeStartupAudit.Method(LiveClaudeExecutableAudit.Read(path), method).ToFullString();
            foreach (var (offset, replacement) in new[] {
                (0, "runner.SnapshotScreen().ShouldNotBeNull();"),
                (1, "try { " + verdict + " } catch { }"),
                (2, "runner.SnapshotScreen().ShouldNotBeNull(); " + verdict) })
            {
                var pc = "PC-" + (132 + 3 * i + offset);
                var changed = original.Replace(verdict, replacement, StringComparison.Ordinal);
                Check("class Subject { " + original + " }", method, verdict);
                var red = false;
                try { Check("class Subject { " + changed + " }", method, verdict); }
                catch (InvalidOperationException e) when (e.Message.StartsWith("C1545 delivery-audit/", StringComparison.Ordinal)) { red = true; }
                if (!red) throw new InvalidOperationException("C1545 " + pc + " mutant survived");
                Check("class Subject { " + original + " }", method, verdict);
                Console.WriteLine($"C1545 {pc} assertion-red; restored-green source={LiveClaudeProfile.Hash(original)} patch={LiveClaudeProfile.Hash(verdict + "\n" + replacement)} (owned syntax copy)");
            }
        }
    }
}
