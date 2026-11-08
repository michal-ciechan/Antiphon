using Shouldly;
using TUnit.Core;

namespace Antiphon.MarkdownPdf.Tests;

/// <summary>
/// CARD-1740. A solution build assigns Configuration only for projects listed in Antiphon.sln.
/// CopyFakeBrowser reads the producer's Release output, so the producer must be built for Release.
/// </summary>
[Category("Unit")]
public class FakeBrowserSolutionReleaseTests
{
    [Test]
    public void Release_solution_builds_the_fake_browser_producer()
    {
        var sln = File.ReadAllText(FindSolution());
        string? relative = null;
        string? guid = null;
        foreach (var line in sln.Split('\n'))
        {
            const string name = "\"Antiphon.MarkdownPdf.FakeBrowser\"";
            var at = line.IndexOf(name, StringComparison.Ordinal);
            if (at < 0 || !line.Contains("Project(", StringComparison.Ordinal)) continue;
            var parts = line.Split('"');
            // Project("{guid}") = "Name", "relative.csproj", "{project-guid}"
            parts.Length.ShouldBeGreaterThanOrEqualTo(8, line);
            relative = parts[5];
            guid = parts[7].Trim('{', '}').ToUpperInvariant();
            break;
        }

        relative.ShouldNotBeNull(
            "Antiphon.sln must list Antiphon.MarkdownPdf.FakeBrowser so a Release solution build assigns Configuration=Release");
        relative.Replace('\\', '/').ShouldBe(
            "tests/Antiphon.MarkdownPdf.FakeBrowser/Antiphon.MarkdownPdf.FakeBrowser.csproj");
        foreach (var platform in new[] { "Any CPU", "x64", "x86" })
        {
            sln.ShouldContain($"{{{guid}}}.Release|{platform}.ActiveCfg = Release|Any CPU");
            sln.ShouldContain($"{{{guid}}}.Release|{platform}.Build.0 = Release|Any CPU");
        }
    }

    private static string FindSolution()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new DirectoryNotFoundException("Antiphon.sln not found above " + AppContext.BaseDirectory);
        return Path.Combine(dir.FullName, "Antiphon.sln");
    }
}
