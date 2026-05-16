using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Services;

public static partial class UnifiedDiffParser
{
    public static IReadOnlyList<BranchDiffFileDto> Parse(string diff)
    {
        var files = new List<BranchDiffFileDto>();
        if (string.IsNullOrWhiteSpace(diff))
            return files;

        var lines = diff
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        string? currentFile = null;
        var patchLines = new List<string>();
        var additions = 0;
        var deletions = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                FlushFile();
                var match = DiffGitRegex().Match(line);
                currentFile = match.Success ? match.Groups[2].Value : line;
                patchLines.Add(line);
                continue;
            }

            if (currentFile is null)
                continue;

            patchLines.Add(line);
            if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal))
                additions++;
            else if (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal))
                deletions++;
        }

        FlushFile();
        return files;

        void FlushFile()
        {
            if (currentFile is null)
                return;

            files.Add(new BranchDiffFileDto(currentFile, additions, deletions, string.Join('\n', patchLines)));
            currentFile = null;
            patchLines.Clear();
            additions = 0;
            deletions = 0;
        }
    }

    [GeneratedRegex(@"diff --git a/(.+) b/(.+)", RegexOptions.Compiled)]
    private static partial Regex DiffGitRegex();
}
