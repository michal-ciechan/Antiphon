namespace Antiphon.Checkpoints.Coverage;
public sealed class PlanCoverageReader
{
    public PlanCoverageReport Read(string plan, string text, string? checklist = null) => new() { Plan = plan, PlanSha256 = PlanCoverageReport.Hash(text) };
}
