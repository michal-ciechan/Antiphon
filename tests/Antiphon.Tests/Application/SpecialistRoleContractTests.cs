using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0352 S1: the next specialist cannot be forgotten at one of fifty <c>AgentTaskRole.Check</c>
/// comparison sites. Check-specific semantics (creating a Check row, classifying a Check report,
/// the Check reporting contract) stay on an allowlist; everything else must use
/// <see cref="AgentTaskRoles"/>.
/// </summary>
[Category("Unit")]
public class SpecialistRoleContractTests
{
    private static readonly string[] Allowlist =
    [
        "Domain/Enums/AgentTaskEnums.cs",
        "Application/Services/AgentTaskCheckService.cs",
        "Application/Services/AgentTaskReplyService.cs",
        "Application/Services/DelegationReportFormatter.cs",
        "Application/Services/CheckInterpreterProvisioner.cs",
    ];

    [Test]
    public void IsSpecialist_covers_check_distill_and_diagnose_only()
    {
        foreach (var role in Enum.GetValues<AgentTaskRole>())
        {
            var expected = role is AgentTaskRole.Check or AgentTaskRole.Distill or AgentTaskRole.Diagnose;
            AgentTaskRoles.IsSpecialist(role).ShouldBe(expected, role.ToString());
        }
    }

    [Test]
    public void no_Check_comparison_survives_outside_the_allowlist()
    {
        var root = Path.Combine(FindRepoRoot(), "server");
        var hits = new List<string>();

        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (Allowlist.Contains(relative, StringComparer.OrdinalIgnoreCase))
                continue;

            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("*", StringComparison.Ordinal))
                    continue;

                var code = StripLineComment(lines[i]);
                if (code.Contains("AgentTaskRole.Check", StringComparison.Ordinal))
                    hits.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
            }
        }

        hits.ShouldBeEmpty(
            "AgentTaskRole.Check comparisons belong in the allowlist; everywhere else uses AgentTaskRoles:"
            + Environment.NewLine + string.Join(Environment.NewLine, hits));
    }

    private static string StripLineComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"')
                inString = !inString;
            else if (!inString && line[i] == '/' && line[i + 1] == '/')
                return line[..i];
        }

        return line;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Antiphon repository root.");
    }
}
