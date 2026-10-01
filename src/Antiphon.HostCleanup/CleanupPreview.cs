namespace Antiphon.HostCleanup;

public interface ICleanupPreviewOutput
{
    ValueTask WriteAsync(string path, CleanupPlan plan, CancellationToken cancellationToken);
}

public static class CleanupPreview
{
    public static ValueTask WritePlanFileAsync(CleanupPlan plan, string path,
        ICleanupPreviewOutput output, CancellationToken cancellationToken = default)
    {
        if (!CleanupPath.IsSafe(path) ||
            plan.Decisions.Any(decision => CleanupPath.IsSameOrChild(path, decision.Path) ||
                CleanupPath.IsSameOrChild(decision.Path, path)))
            throw new ArgumentException("Plan output must be outside all candidate roots.", nameof(path));
        return output.WriteAsync(path, plan, cancellationToken);
    }
}
