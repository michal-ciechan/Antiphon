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

    public static string Recovery(string code, string? storedReason = null)
    {
        // Only the bounded numeric envelope is copied from persisted diagnostic text.
        var counts = storedReason is null ? null : System.Text.RegularExpressions.Regex.Match(storedReason,
            "^[a-z_]+: (candidates=[0-9]{1,6} inspected=[0-9]{1,6} commands=[0-9]{1,6}); ");
        var evidence = counts?.Success == true ? " " + counts.Groups[1].Value + "." : "";
        return $"{code}: Same-card base inspection could not finish.{evidence} Resolve the inspection cause and retry, "
            + "or cancel and recreate with an explicit source or fresh target.";
    }
}
