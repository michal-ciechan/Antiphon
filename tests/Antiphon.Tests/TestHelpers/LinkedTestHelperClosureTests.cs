using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Master ae7bf7368 (2c1b05349): TestDbFixtureLifecycle.cs started using TestDbFailureEvidence from a
/// new file, and Antiphon.Resilience.Tests, which links Antiphon.Tests helpers one file at a time, did
/// not compile. Building Antiphon.Tests alone cannot see that. This census reads every project that
/// links a file from tests/Antiphon.Tests and fails when a linked file names a type whose own file
/// (TypeName.cs under tests/Antiphon.Tests) the project neither links nor declares itself.
/// </summary>
[Category("Unit")]
public sealed class LinkedTestHelperClosureTests
{
    private static readonly Regex Declaration = new(@"\b(?:class|struct|record|interface|enum)\s+([A-Z]\w*)", RegexOptions.Compiled);
    private static readonly Regex Identifier = new(@"\b[A-Z]\w*\b", RegexOptions.Compiled);
    private static readonly Regex Comments = new(@"/\*[\s\S]*?\*/|//[^\n]*", RegexOptions.Compiled);
    private static readonly Regex Strings = new("@?\\$?\"(?:[^\"\\\\\\n]|\\\\.)*\"", RegexOptions.Compiled);

    [Test]
    public void every_linked_antiphon_tests_file_has_its_helper_types_linked()
    {
        var testsRoot = Path.Combine(RepoRoot, "tests", "Antiphon.Tests") + Path.DirectorySeparatorChar;
        var typeFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles(testsRoot, "*.cs"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (Declarations(file).Contains(name))
                typeFiles[name] = file;
        }
        typeFiles.ShouldContainKey("TestDbFixtureLifecycle", "type census found the helper files");

        var linking = new List<string>();
        var missing = new List<string>();
        foreach (var project in SourceFiles(RepoRoot, "*.csproj"))
        {
            if (project.StartsWith(testsRoot, StringComparison.Ordinal))
                continue;
            var projectDir = Path.GetDirectoryName(project)!;
            var includes = XDocument.Load(project).Descendants()
                .Where(e => e.Name.LocalName == "Compile")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(i => i is not null && !i.Contains('*') && !i.Contains("$(", StringComparison.Ordinal))
                .Select(i => Path.GetFullPath(Path.Combine(projectDir, i!.Replace('\\', '/'))))
                .ToHashSet(StringComparer.Ordinal);
            var linked = includes.Where(i => i.StartsWith(testsRoot, StringComparison.Ordinal)).ToArray();
            if (linked.Length == 0)
                continue;
            var rel = Relative(project);
            linking.Add(rel);

            var compiled = includes.Concat(SourceFiles(projectDir + Path.DirectorySeparatorChar, "*.cs")).Where(File.Exists);
            var own = compiled.SelectMany(Declarations).ToHashSet(StringComparer.Ordinal);
            foreach (var file in linked)
            {
                foreach (Match id in Identifier.Matches(Code(file)))
                {
                    if (own.Contains(id.Value) || !typeFiles.TryGetValue(id.Value, out var owner) || includes.Contains(owner))
                        continue;
                    missing.Add($"{rel}: {Relative(file)} uses {id.Value}; link {Relative(owner)}");
                }
            }
        }

        linking.ShouldContain("tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj", "census found the linking projects");
        missing.Distinct().ShouldBeEmpty("linked helper types missing from their project: " + string.Join("; ", missing.Distinct()));
    }

    private static string Code(string file) => Strings.Replace(Comments.Replace(File.ReadAllText(file), " "), "\"\"");

    private static IEnumerable<string> Declarations(string file) =>
        Declaration.Matches(Code(file)).Select(m => m.Groups[1].Value);

    private static IEnumerable<string> SourceFiles(string dir, string pattern)
    {
        foreach (var file in Directory.EnumerateFiles(dir, pattern))
            yield return Path.GetFullPath(file);
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            if (name is "bin" or "obj" or "node_modules" or ".git" or ".antiphon" || name.StartsWith("bin-", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var file in SourceFiles(sub, pattern))
                yield return file;
        }
    }

    private static string Relative(string path) => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln).");
        }
    }
}
