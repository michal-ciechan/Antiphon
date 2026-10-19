namespace Antiphon.Server.Application.Services;

public static class DispatchInspectionDiagnostics
{
    public static string? InspectionReason(string? reason)
    {
        if (reason is null) return null;
        foreach (var code in new[] { "inspection_timeout", "candidate_limit", "git_command_limit", "git_inspection_error" })
            if (reason.StartsWith(code + ":", StringComparison.Ordinal)) return code;
        return null;
    }

    public static string Recovery(string code) =>
        $"{code}: Same-card base inspection could not finish. Resolve the inspection cause and retry, "
        + "or cancel and recreate with an explicit source or fresh target.";
}
