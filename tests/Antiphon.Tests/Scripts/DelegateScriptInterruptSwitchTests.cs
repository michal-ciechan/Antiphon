using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>CARD-0491 V-4. -Interrupt is refine-only and the two statuses print apart.</summary>
[Category("Unit")]
public sealed class DelegateScriptInterruptSwitchTests
{
    [Test]
    public void Interrupt_is_a_refine_only_switch()
    {
        var script = Script();
        var decls = System.Text.RegularExpressions.Regex.Matches(script, @"\[switch\]\$Interrupt");
        decls.Count.ShouldBe(1, "-Interrupt is declared once");
        var index = script.IndexOf("[switch]$Interrupt", StringComparison.Ordinal);
        var preceding = script[..index];
        var attribute = preceding[preceding.LastIndexOf("[Parameter", StringComparison.Ordinal)..];
        attribute.ShouldContain("ParameterSetName = 'Refine'");
        attribute.ShouldNotContain("ParameterSetName = 'Create'");
        attribute.ShouldNotContain("ParameterSetName = 'Reply'");
        attribute.ShouldNotContain("ParameterSetName = 'Continue'");
    }

    [Test]
    public void Interrupt_posts_the_flag_and_a_fresh_request_id()
    {
        var body = RefineCase(Script());
        body.ShouldContain("interruptCurrentTurn = [bool]$Interrupt");
        body.ShouldContain("requestId = [guid]::NewGuid()");
    }

    [Test]
    public void Interrupt_output_prints_interruptWritten_and_refinementDelivered_on_separate_lines()
    {
        var body = RefineCase(Script());
        body.ShouldContain("will land between its turns");
        var written = body.Split('\n').Single(line => line.Contains("$summary.interruptWritten", StringComparison.Ordinal));
        var delivered = body.Split('\n').Single(line => line.Contains("$summary.refinementDelivered", StringComparison.Ordinal));
        written.ShouldContain("Write-Output");
        delivered.ShouldContain("Write-Output");
        written.ShouldNotContain("refinementDelivered");
        delivered.ShouldNotContain("interruptWritten");
    }

    private static string RefineCase(string script)
    {
        var start = script.IndexOf("'Refine' {", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0);
        var end = script.IndexOf("'Finding' {", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        return script[start..end];
    }

    private static string Script() =>
        File.ReadAllText(Path.Combine(Application.DelegateScriptRunner.RepoRoot, "scripts", "delegate.ps1"));
}
