using System.Reflection;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class DelegateBranchNameTests
{
    // Tests precede the contracts type. Missing production API is an assertion failure,
    // so the pre-slice build still executes this roster rather than failing compilation.
    private static Type Parser => typeof(RunnerCapabilityFeatures).Assembly
        .GetType("Antiphon.SessionRunner.Contracts.DelegateBranchName")
        .ShouldNotBeNull("shared branch parser must exist");
    private static object? Parse(string? value, bool expected, string label)
    {
        object?[] args = [value, null];
        ((bool)Parser.GetMethod("TryParse")!.Invoke(null, args)!).ShouldBe(expected, label + ": " + value);
        WorktreeManager.TryParseDelegateTaskBranch(value, out var id).ShouldBe(expected, "manager " + label);
        if (expected) id.ShouldBe((string)args[1]!.GetType().GetProperty("ShortId")!.GetValue(args[1])!);
        return args[1];
    }
    private static string? Field(object value, string name) => (string?)value.GetType().GetProperty(name)!.GetValue(value);

    [Test]
    public void C1213_BuildCardStage()
    {
        // case -> mutation -> red: every role token; omit stage -> exact built-name differs.
        var stages = new[] { "custom", "plan", "code", "review", "debug", "coverage", "docs", "commit", "test", "deploy", "merge", "check", "distill", "diagnose", "investigate", "testdesign", "mutation", "orch" };
        foreach (var stage in stages)
            Parser.GetMethod("Build")!.Invoke(null, [Guid.Parse("6327ca04-0000-0000-0000-000000000000"), "CARD-1213", stage])
                .ShouldBe("feat/card-1213-" + stage + "-6327ca04", "built-name");
        foreach (var card in new string?[] { null, "E03-003", "CARD-123456789", "CARD-１２１３", "CARD-1213\n", " CARD-1213" })
            Parser.GetMethod("Build")!.Invoke(null, [Guid.Parse("6327ca04-0000-0000-0000-000000000000"), card, "code"])
                .ShouldBe("feat/card-task-6327ca04", "fallback-branch");
        Parser.GetMethod("Build")!.Invoke(null, [Guid.Parse("6327ca04-0000-0000-0000-000000000000"), "CARD-1213", "Code"])
            .ShouldBe("feat/card-task-6327ca04", "invalid-stage-fallback");
    }
    [Test]
    public void C1213_ParseBothForms()
    {
        // remove either alternative -> legacy/new-accepted red; remove normalization -> normalized-id red.
        foreach (var branch in new[] { "feat/card-task-deadbeef", "refs/heads/feat/card-task-DEADBEEF", "feat/card-1213-code-deadbeef", "refs/heads/feat/card-1213-task-DEADBEEF" })
            Field(Parse(branch, true, "both-forms-accepted")!, "ShortId").ShouldBe("deadbeef", "normalized-id");
    }
    [Test]
    public void C1213_RejectEnvelopeJunk()
    {
        // remove whole-match length -> final LF admitted.
        foreach (var branch in new string?[] { null, "", " feat/card-task-deadbeef", "feat/card-task-deadbeef ", "xfeat/card-task-deadbeef", "feat/card-task-deadbeefx", "feat/card-task-deadbeef\0", "feat/card-task-deadbeef\r", "feat/card-task-deadbeef\n", "feat/card-1213-code-deadbeef\n" })
            Parse(branch, false, "envelope-rejected");
    }
    [Test]
    public void C1213_NumberBounds()
    {
        // widen {1,8} -> nine digits admitted.
        foreach (var number in new[] { "0", "00001213", "99999999" })
            Field(Parse("feat/card-" + number + "-code-deadbeef", true, "number-accepted")!, "CardNumber").ShouldBe(number);
        foreach (var number in new[] { "", "123456789" }) Parse("feat/card-" + number + "-code-deadbeef", false, "number-rejected");
    }
    [Test]
    public void C1213_StageBounds()
    {
        // widen first/max stage character -> digit-first/17 chars admitted.
        foreach (var stage in new[] { "a", "a123456789012345", "testdesign", "task", "unknown" })
            Field(Parse("feat/card-1213-" + stage + "-deadbeef", true, "stage-accepted")!, "Stage").ShouldBe(stage);
        foreach (var stage in new[] { "", "1code", "a1234567890123456", "co-de" }) Parse("feat/card-1213-" + stage + "-deadbeef", false, "stage-rejected");
    }
    [Test]
    public void C1213_HexBounds()
    {
        // widen {8}/hex alphabet -> invalid tail admitted.
        Parse("feat/card-task-ABCD1234", true, "tail-accepted");
        foreach (var tail in new[] { "abcdefg1", "deadbee", "deadbeeff", "deadbeeé" }) Parse("feat/card-task-" + tail, false, "tail-rejected");
    }
    [Test]
    public void C1213_CaseAndUnicode()
    {
        // IgnoreCase/non-ASCII classes -> alphabet-rejected red.
        foreach (var branch in new[] { "Feat/card-task-deadbeef", "feat/CARD-task-deadbeef", "feat/card-1213-Plan-deadbeef", "feat/card-１２１３-code-deadbeef", "feat/card-١٢١٣-code-deadbeef", "feat/card-1213-сode-deadbeef", "feat/card-1213-códe-deadbeef" }) Parse(branch, false, "alphabet-rejected");
    }
    [Test]
    public void C1213_PathInjection()
    {
        // arbitrary stage -> slash/refspec admitted.
        foreach (var branch in new[] { "feat/card-1213-co/de-deadbeef", "feat/card-1213-../-deadbeef", "feat/card-1213-co\\de-deadbeef", "feat/card-task-deadbeef:refs/heads/master", "refs/tags/feat/card-task-deadbeef", "refs/heads/refs/heads/feat/card-task-deadbeef", "feat/card-task-deadbeef/", new string('a', 201) }) Parse(branch, false, "path-rejected");
    }
    [Test]
    public void C1213_FormsAreDisjoint()
    {
        // populate legacy fields -> legacy-fields red.
        var legacy = Parse("feat/card-task-deadbeef", true, "legacy-accepted")!;
        Field(legacy, "CardNumber").ShouldBeNull("legacy-fields");
        Field(legacy, "Stage").ShouldBeNull("legacy-fields");
        var modern = Parse("feat/card-1213-task-deadbeef", true, "new-accepted")!;
        Field(modern, "CardNumber").ShouldBe("1213", "new-fields");
        Field(modern, "Stage").ShouldBe("task", "new-fields");
        Parse("feat/card-task-code-deadbeef", false, "disjoint-near-candidate");
    }
}
