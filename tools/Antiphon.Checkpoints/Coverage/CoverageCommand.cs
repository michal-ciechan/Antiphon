namespace Antiphon.Checkpoints.Coverage;
public sealed class CoverageCommand
{
    public int Run(string root, string plan, IReadOnlyList<string>? tests = null, string format = "text", string? checklist = null, TextWriter? output = null)
    {
        var report = new PlanCoverageReport { Plan = plan };
        (output ?? Console.Out).Write(format == "json" ? report.Json() : report.Text());
        return report.ExitCode;
    }
}
