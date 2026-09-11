using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class ScopedVerificationInstructionTests
{
    private static string Compose(AgentTaskRole role) =>
        InstructionBundleComposer.Compose(InstructionBundles.ForDelegate(AgentTaskKind.Worker, role)).Text;

    [Test]
    public void C487_G127()
    {
        var code = Compose(AgentTaskRole.Code);
        code.ShouldNotContain("skip the Unit lane");
        code.ShouldContain("Unit plus the named affected");
        File.ReadAllText(Path.Combine(RepoRoot, "docs", "testing-and-build.md"))
            .ShouldContain("does not activate that plan's reduced-dispatch policy");
    }

    [Test]
    public void C487_G128()
    {
        var code = Compose(AgentTaskRole.Code);
        code.ShouldNotContain("credit nightly without a current monitor");
        File.ReadAllText(Path.Combine(RepoRoot, "docs", "testing-and-build.md"))
            .ShouldContain("Do not credit this as an operational full-suite backstop");
    }

    [Test]
    public void C487_G129()
    {
        Compose(AgentTaskRole.Code).ShouldContain("Unit plus the named affected integration classes");
        Compose(AgentTaskRole.Review).ShouldContain("Read-only");
    }

    [Test]
    public void C487_G130()
    {
        Compose(AgentTaskRole.TestDesign).ShouldContain("### Positive controls");
        Compose(AgentTaskRole.Plan).ShouldContain("## Verification design");
    }

    [Test]
    public void C487_G131()
    {
        Compose(AgentTaskRole.TestDesign).ShouldContain("Every guard that protects a safety-critical assertion gets a PC-n positive control");
    }

    [Test]
    public void C487_G132()
    {
        File.ReadAllText(Path.Combine(RepoRoot, "docs", "superpowers", "plans", "2026-09-11-card-0487-scoped-dispatch-testing-plan.md"))
            .ShouldContain("do not write only \"rest nightly\"");
    }

    [Test]
    public void C487_G133()
    {
        File.ReadAllText(Path.Combine(RepoRoot, "docs", "superpowers", "plans", "2026-09-11-card-0487-scoped-dispatch-testing-plan.md"))
            .ShouldContain("A skipped required manual test remains pending, not green");
    }

    [Test]
    public void C487_G134()
    {
        Compose(AgentTaskRole.Code).ShouldContain("Report every PC-n/variant pending");
    }

    [Test]
    public void C487_G135()
    {
        Compose(AgentTaskRole.TestDesign).ShouldContain("Every guard that protects a safety-critical assertion gets a PC-n positive control");
        Compose(AgentTaskRole.Mutation).ShouldContain("missing PC");
    }

    [Test]
    public void C487_G136()
    {
        var code = Compose(AgentTaskRole.Code);
        code.ShouldNotContain("Run each PC-n as red-then-green");
        code.ShouldContain("pending for Mutation");
        Compose(AgentTaskRole.Mutation).ShouldContain("intended assertion red");
    }

    [Test]
    public void C487_G137()
    {
        Compose(AgentTaskRole.TestDesign).ShouldContain("producer");
        File.ReadAllText(Path.Combine(RepoRoot, "docs", "testing-and-build.md"))
            .ShouldContain("Asynchronous outcome delivery verification");
    }

    [Test]
    public void C487_G138()
    {
        Compose(AgentTaskRole.TestDesign).ShouldContain("do not rewrite the fix design");
    }

    [Test]
    public void C487_G139()
    {
        Compose(AgentTaskRole.TestDesign).ShouldContain("ordinary V/R floor (Code)");
        Compose(AgentTaskRole.TestDesign).ShouldContain("PC floor (Mutation)");
    }

    [Test]
    public void C487_G140()
    {
        foreach (var key in new[]
                 {
                     InstructionBundles.StageCode, InstructionBundles.StageMutation,
                     InstructionBundles.StageReview, InstructionBundles.StageTestDesign
                 })
        {
            foreach (var ch in InstructionBundles.TextOf(key))
                ((int)ch).ShouldBeLessThan(128, key);
        }
    }

    [Test]
    public void C487_G141()
    {
        foreach (var key in new[]
                 {
                     InstructionBundles.StageInvestigate, InstructionBundles.StagePlan,
                     InstructionBundles.StageTestDesign, InstructionBundles.StageCode,
                     InstructionBundles.StageMutation, InstructionBundles.StageReview
                 })
        {
            InstructionBundles.TextOf(key).Length.ShouldBeLessThanOrEqualTo(2500, key);
        }
    }

    [Test]
    public void C487_G142()
    {
        Compose(AgentTaskRole.Code).ShouldContain("next: mutation when implementation and ordinary V/R are complete, even with zero PCs");
        Compose(AgentTaskRole.Review).ShouldContain("PC evidence read-only");
    }

    [Test]
    public void C487_G143()
    {
        Compose(AgentTaskRole.Code).ShouldContain("original Code task ID");
        Compose(AgentTaskRole.Mutation).ShouldContain("original Code task ID");
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("repo");
        }
    }
}
