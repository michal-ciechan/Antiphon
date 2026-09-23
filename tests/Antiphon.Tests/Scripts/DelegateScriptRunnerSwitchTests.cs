using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-0604 D-15. -Runner is refused locally, before any POST, for the shapes the runner has no
// design for. Refusing in the script keeps the operator from waiting on a 422 round trip for a
// combination that could never have been admitted.
[Category("Unit")]
public sealed class DelegateScriptRunnerSwitchTests
{
    [Test]
    public void Runner_switch_refuses_shared_and_pins()
    {
        var script = Script();
        var block = Block(script);

        // Each refused combination names itself, so the operator is told which flag lost.
        block.ShouldContain("$Shared -or $ReadOnly");
        block.ShouldContain("-Runner requires a Worktree workspace");
        block.ShouldContain("$OnAgent -or -not [string]::IsNullOrWhiteSpace($Agent)");
        block.ShouldContain("continue an existing process");
        // CARD-0604 Cut B: -SourceLanding on a runner is supported now, but only as a Mutation.
        // Any other role would reserve an execution the remote lane has no design for.
        block.ShouldContain("SourceLanding");
        block.ShouldContain("$Role -ne 'Mutation'");
        block.ShouldContain("-Runner with -SourceLanding requires -Role Mutation");
        block.Contains("-Runner cannot be combined with -SourceLanding", StringComparison.Ordinal)
            .ShouldBeFalse("the Cut A blanket refusal is superseded by Cut B");
        // Every refusal is a hard exit before the body is posted.
        System.Text.RegularExpressions.Regex.Matches(block, @"exit 2").Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Test]
    public void Runner_switch_sends_a_worktree_task()
    {
        var block = Block(Script());
        block.ShouldContain("$body['runnerId'] = $Runner");
        // A remote task is always Worktree: the mirror design has nothing else to fast-forward.
        block.ShouldContain("$body['workspace'] = 'Worktree'");
    }

    [Test]
    public void Runner_is_a_create_only_parameter()
    {
        var script = Script();
        var index = script.IndexOf("[string]$Runner,", StringComparison.Ordinal);
        index.ShouldBeGreaterThan(0, "-Runner is declared");
        // The declaration sits under the Create parameter set, like every other create switch: a
        // -Runner on a settle or land call would be silently ignored otherwise.
        var preceding = script[..index];
        preceding[preceding.LastIndexOf("[Parameter", StringComparison.Ordinal)..]
            .ShouldContain("ParameterSetName = 'Create'");
    }

    private static string Block(string script)
    {
        var start = script.IndexOf("if (-not [string]::IsNullOrWhiteSpace($Runner)) {", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, "the -Runner guard block exists");
        var end = script.IndexOf("$verificationFields", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        return script[start..end];
    }

    private static string Script() =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "delegate.ps1"));
}
