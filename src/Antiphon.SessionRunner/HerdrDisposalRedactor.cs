using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>Projects an already-classified observation into text safe for previews and reviewed evidence.</summary>
internal static class HerdrDisposalRedactor
{
    internal const string Mask = "[redacted]";

    internal static HerdrPaneDisposalPreview Project(HerdrPaneDisposalPreview preview) => preview with
    {
        WorkspaceLabel = DisplayText(preview.WorkspaceLabel),
        TabLabel = DisplayText(preview.TabLabel),
        PaneLabel = DisplayText(preview.PaneLabel),
        BackendVersion = DisplayText(preview.BackendVersion)!,
        Claims = preview.Claims.Select(c => c with
        {
            Source = DisplayText(c.Source)!,
            Origin = DisplayText(c.Origin),
            AgentKind = DisplayText(c.AgentKind)
        }).ToArray(),
        Shell = Process(preview.Shell),
        Foreground = preview.Foreground?.Select(p => Process(p)!).ToArray(),
        AffectedProcesses = preview.AffectedProcesses?.Select(p => Process(p)!).ToArray()
    };

    private static string? DisplayText(string? value)
    {
        if (value is null) return null;
        if (value.Contains('/') || value.Contains('\\') || value.Any(char.IsControl)
            || ContainsDrivePrefix(value))
            return Mask;
        return value;
    }

    private static bool ContainsDrivePrefix(string value)
    {
        for (var i = 0; i < value.Length - 1; i++)
        {
            var c = value[i];
            if (((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                && value[i + 1] == ':'
                && (i == 0 || char.IsWhiteSpace(value[i - 1]) || value[i - 1] is '"' or '\''))
                return true;
        }
        return false;
    }

    private static HerdrPaneDisposalProcess? Process(HerdrPaneDisposalProcess? process) =>
        process is null ? null : process with { ExecutableName = Leaf(process.ExecutableName) };

    private static string? Leaf(string? value)
    {
        if (value is null) return null;
        var last = value.LastIndexOfAny(['/', '\\']);
        var leaf = value[(last + 1)..];
        if (leaf.Length >= 2 && char.IsLetter(leaf[0]) && leaf[1] == ':') leaf = leaf[2..];
        return leaf.Length == 0 || leaf.Any(char.IsControl) ? null : leaf;
    }
}
