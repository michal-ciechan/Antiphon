namespace Antiphon.Checkpoints.Coverage;
public sealed class PlanCoverageAnalyzer
{
    public PlanCoverageReport Analyze(string plan, string text, IReadOnlyList<CoverageSource> sources, string? checklist = null) => new PlanCoverageReader().Read(plan, text, checklist);
}
