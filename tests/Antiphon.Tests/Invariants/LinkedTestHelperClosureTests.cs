using Antiphon.Tests.TestHelpers;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Invariants;

/// <summary>
/// Master ae7bf7368 (2c1b05349): TestDbFixtureLifecycle.cs started using TestDbFailureEvidence from a
/// new file, and Antiphon.Resilience.Tests, which links Antiphon.Tests helpers one file at a time, did
/// not compile. Building Antiphon.Tests alone cannot see that. This census reads every project that
/// links a file from tests/Antiphon.Tests and fails when a linked file names a type the project
/// neither links nor declares. A type counts when its file is named TypeName.cs anywhere under
/// tests/Antiphon.Tests, and also when it is a namespace-level type under TestHelpers whose file
/// has another name (CARD-1996). Nested types are not simple names in another file, so they are
/// not indexed; a TestHelpers glob is not a substitute, because each mirror links a different subset.
/// </summary>
[Category("RepositoryInvariant")]
[Category("Unit")]
public sealed class LinkedTestHelperClosureTests
{
    private static readonly Regex Declaration = new(@"\b(?:class|struct|record|interface|enum)\s+([A-Z]\w*)", RegexOptions.Compiled);
    private static readonly Regex TopLevelToken = new(
        @"namespace\s+[A-Za-z0-9_.]+|\{|\}|(?:(?:public|internal|file|private|protected|static|sealed|abstract|partial|readonly|ref|unsafe)\s+)*(?:class|struct|record|interface|enum)\s+([A-Z]\w*)",
        RegexOptions.Compiled);
    private static readonly Regex Identifier = new(@"\b[A-Z]\w*\b", RegexOptions.Compiled);
    private static readonly Regex Comments = new(@"/\*[\s\S]*?\*/|//[^\n]*", RegexOptions.Compiled);
    private static readonly Regex Strings = new("@?\\$?\"(?:[^\"\\\\\\n]|\\\\.)*\"", RegexOptions.Compiled);

    [Test]
    public void every_linked_antiphon_tests_file_has_its_helper_types_linked()
    {
        var scan = Scan(RepoRoot);
        scan.IndexedTestDbFixtureLifecycle.ShouldBeTrue("type census found the helper files");
        scan.Linking.ShouldContain(
            "tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj",
            "census found the linking projects");
        scan.Missing.Distinct().ShouldBeEmpty(
            "linked helper types missing from their project: " + string.Join("; ", scan.Missing.Distinct()));
    }

    [Test]
    public void differently_named_helper_file_is_reported()
    {
        var root = Path.Combine(Path.GetTempPath(), "card1996-closure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Antiphon.sln"), "");
            var helper = Path.Combine(root, "tests", "Antiphon.Tests", "TestHelpers");
            Directory.CreateDirectory(helper);
            File.WriteAllText(
                Path.Combine(helper, "UsesCrossFileType.cs"),
                "namespace Antiphon.Tests.TestHelpers;\n" +
                "public sealed class UsesCrossFileType\n" +
                "{\n" +
                "    public static int Read() => HiddenHelper.Value;\n" +
                "}\n");
            File.WriteAllText(
                Path.Combine(helper, "NotTheTypeName.cs"),
                "namespace Antiphon.Tests.TestHelpers;\n" +
                "public static class HiddenHelper\n" +
                "{\n" +
                "    public static int Value => 1;\n" +
                "}\n");
            var mirror = Path.Combine(root, "tests", "Mirror.Tests");
            Directory.CreateDirectory(mirror);
            File.WriteAllText(
                Path.Combine(mirror, "Mirror.Tests.csproj"),
                "<Project><ItemGroup><Compile Include=\"..\\Antiphon.Tests\\TestHelpers\\UsesCrossFileType.cs\" /></ItemGroup></Project>\n");

            var missing = Scan(root).Missing;
            missing.ShouldContain(
                item => item.Contains("HiddenHelper", StringComparison.Ordinal)
                    && item.Contains("NotTheTypeName.cs", StringComparison.Ordinal),
                "a type declared in a differently named TestHelpers file must be reported when a mirror links only the using file");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void same_named_helper_file_is_still_reported()
    {
        var root = Path.Combine(Path.GetTempPath(), "card1996-closure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Antiphon.sln"), "");
            var helper = Path.Combine(root, "tests", "Antiphon.Tests", "TestHelpers");
            Directory.CreateDirectory(helper);
            File.WriteAllText(
                Path.Combine(helper, "UsesSameName.cs"),
                "namespace Antiphon.Tests.TestHelpers;\n" +
                "public sealed class UsesSameName\n" +
                "{\n" +
                "    public static int Read() => SameNameHelper.Value;\n" +
                "}\n");
            File.WriteAllText(
                Path.Combine(helper, "SameNameHelper.cs"),
                "namespace Antiphon.Tests.TestHelpers;\n" +
                "public static class SameNameHelper\n" +
                "{\n" +
                "    public static int Value => 1;\n" +
                "}\n");
            var mirror = Path.Combine(root, "tests", "Mirror.Tests");
            Directory.CreateDirectory(mirror);
            File.WriteAllText(
                Path.Combine(mirror, "Mirror.Tests.csproj"),
                "<Project><ItemGroup><Compile Include=\"..\\Antiphon.Tests\\TestHelpers\\UsesSameName.cs\" /></ItemGroup></Project>\n");

            var missing = Scan(root).Missing;
            missing.ShouldContain(
                item => item.Contains("SameNameHelper", StringComparison.Ordinal)
                    && item.Contains("SameNameHelper.cs", StringComparison.Ordinal),
                "a type whose file is named for the type is still reported when the mirror does not link that file");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private readonly record struct ClosureScan(
        IReadOnlyList<string> Linking,
        IReadOnlyList<string> Missing,
        bool IndexedTestDbFixtureLifecycle);

    private static ClosureScan Scan(string repoRoot)
    {
        var testsRoot = Path.Combine(repoRoot, "tests", "Antiphon.Tests") + Path.DirectorySeparatorChar;
        var helpersRoot = testsRoot + "TestHelpers" + Path.DirectorySeparatorChar;
        var typeFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in SourceFiles(testsRoot, "*.cs"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (Declarations(file).Contains(name))
                AddTypeFile(typeFiles, name, file);
            if (!file.StartsWith(helpersRoot, StringComparison.Ordinal))
                continue;
            foreach (var declared in TopLevelDeclarations(file))
                AddTypeFile(typeFiles, declared, file);
        }

        var linking = new List<string>();
        var missing = new List<string>();
        foreach (var project in SourceFiles(repoRoot, "*.csproj"))
        {
            if (project.StartsWith(testsRoot, StringComparison.Ordinal))
                continue;
            var projectDir = Path.GetDirectoryName(project)!;
            var includes = XDocument.Load(project).Descendants()
                .Where(element => element.Name.LocalName == "Compile")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(include => include is not null && !include.Contains('*') && !include.Contains("$(", StringComparison.Ordinal))
                .Select(include => Path.GetFullPath(Path.Combine(projectDir, include!.Replace('\\', '/'))))
                .ToHashSet(StringComparer.Ordinal);
            var linked = includes.Where(include => include.StartsWith(testsRoot, StringComparison.Ordinal)).ToArray();
            if (linked.Length == 0)
                continue;
            var rel = Relative(repoRoot, project);
            linking.Add(rel);

            var compiled = includes.Concat(SourceFiles(projectDir + Path.DirectorySeparatorChar, "*.cs")).Where(File.Exists);
            var own = compiled.SelectMany(Declarations).ToHashSet(StringComparer.Ordinal);
            foreach (var file in linked)
            {
                foreach (Match id in Identifier.Matches(Code(file)))
                {
                    if (own.Contains(id.Value) || !typeFiles.TryGetValue(id.Value, out var owners))
                        continue;
                    foreach (var owner in owners)
                    {
                        if (includes.Contains(owner))
                            continue;
                        missing.Add($"{rel}: {Relative(repoRoot, file)} uses {id.Value}; link {Relative(repoRoot, owner)}");
                    }
                }
            }
        }

        return new ClosureScan(linking, missing, typeFiles.ContainsKey("TestDbFixtureLifecycle"));
    }

    private static void AddTypeFile(Dictionary<string, List<string>> typeFiles, string typeName, string file)
    {
        if (!typeFiles.TryGetValue(typeName, out var files))
        {
            files = [];
            typeFiles[typeName] = files;
        }
        if (!files.Contains(file, StringComparer.Ordinal))
            files.Add(file);
    }

    private static IEnumerable<string> TopLevelDeclarations(string file)
    {
        var code = Code(file);
        var depth = 0;
        var namespaceDepth = 0;
        foreach (Match match in TopLevelToken.Matches(code))
        {
            var text = match.Value;
            if (text == "{")
            {
                depth++;
                continue;
            }
            if (text == "}")
            {
                if (depth > 0)
                    depth--;
                if (namespaceDepth > depth)
                    namespaceDepth = depth;
                continue;
            }
            if (text.StartsWith("namespace", StringComparison.Ordinal))
            {
                var index = match.Index + match.Length;
                while (index < code.Length && char.IsWhiteSpace(code[index]))
                    index++;
                if (index < code.Length && code[index] == '{')
                    namespaceDepth = depth + 1;
                continue;
            }
            if (depth == namespaceDepth && match.Groups[1].Success)
                yield return match.Groups[1].Value;
        }
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

    private static string Relative(string repoRoot, string path) => Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

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
