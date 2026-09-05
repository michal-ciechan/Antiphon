using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0110 S7′: every test class is tagged Unit xor Integration so the category-filter fast
/// lane cannot silently rot. A class with neither or both fails this census by name.
/// </summary>
[Category("Unit")]
public sealed class TestLaneCategoryGuardTests
{
    [Test]
    public void every_test_class_is_tagged_unit_xor_integration()
    {
        var missing = new List<string>();
        var both = new List<string>();
        var methodBoth = new List<string>();
        var testsRoot = Path.Combine(RepoRoot, "tests", "Antiphon.Tests");

        foreach (var path in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(path))
                continue;

            var lines = File.ReadAllLines(path);
            var rel = Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!TryClassName(lines[i], out var name))
                    continue;

                var attrs = PrecedingAttributes(lines, i);
                var classUnit = attrs.Any(a => a.Contains("Category(\"Unit\")", StringComparison.Ordinal));
                var classInteg = attrs.Any(a => a.Contains("Category(\"Integration\")", StringComparison.Ordinal));
                var (hasTest, methodUnit, methodInteg) = ScanBody(lines, i + 1);
                if (!hasTest)
                    continue;

                var id = rel + "::" + name;
                if (!classUnit && !classInteg)
                    missing.Add(id);
                if (classUnit && classInteg)
                    both.Add(id);
                if (classUnit && methodInteg)
                    methodBoth.Add(id + " (Unit class has an Integration method)");
                if (classInteg && methodUnit)
                    methodBoth.Add(id + " (Integration class has a Unit method)");
            }
        }

        missing.ShouldBeEmpty("untagged test classes: " + Format(missing));
        both.ShouldBeEmpty("classes tagged both Unit and Integration: " + Format(both));
        methodBoth.ShouldBeEmpty("methods that inherit one lane and declare the other: " + Format(methodBoth));
    }

    private static bool TryClassName(string line, out string name)
    {
        name = "";
        var trimmed = line.TrimStart();
        const string pub = "public ";
        if (!trimmed.StartsWith(pub, StringComparison.Ordinal))
            return false;
        trimmed = trimmed[pub.Length..];
        if (trimmed.StartsWith("sealed class ", StringComparison.Ordinal))
            trimmed = trimmed["sealed class ".Length..];
        else if (trimmed.StartsWith("class ", StringComparison.Ordinal))
            trimmed = trimmed["class ".Length..];
        else
            return false;
        var end = 0;
        while (end < trimmed.Length && (char.IsLetterOrDigit(trimmed[end]) || trimmed[end] == '_'))
            end++;
        if (end == 0)
            return false;
        name = trimmed[..end];
        return true;
    }

    private static List<string> PrecedingAttributes(string[] lines, int classLine)
    {
        var attrs = new List<string>();
        for (var j = classLine - 1; j >= 0; j--)
        {
            var t = lines[j].Trim();
            if (t.Length == 0 || t.StartsWith("///", StringComparison.Ordinal) || t.StartsWith("//", StringComparison.Ordinal))
                continue;
            if (t.StartsWith('[') && t.Contains(']'))
            {
                attrs.Add(t);
                continue;
            }
            break;
        }
        return attrs;
    }

    private static (bool HasTest, bool MethodUnit, bool MethodInteg) ScanBody(string[] lines, int start)
    {
        var hasTest = false;
        var methodUnit = false;
        var methodInteg = false;
        var pending = new List<string>();
        for (var k = start; k < lines.Length; k++)
        {
            if (TryClassName(lines[k], out _))
                break;
            var t = lines[k].Trim();
            if (t.StartsWith('[') && t.Contains(']'))
            {
                pending.Add(t);
                if (t.Contains("[Test]", StringComparison.Ordinal))
                    hasTest = true;
                continue;
            }
            if (t.StartsWith("public ", StringComparison.Ordinal))
            {
                if (pending.Any(a => a.Contains("[Test]", StringComparison.Ordinal)))
                {
                    hasTest = true;
                    if (pending.Any(a => a.Contains("Category(\"Unit\")", StringComparison.Ordinal)))
                        methodUnit = true;
                    if (pending.Any(a => a.Contains("Category(\"Integration\")", StringComparison.Ordinal)))
                        methodInteg = true;
                }
                pending.Clear();
            }
        }
        return (hasTest, methodUnit, methodInteg);
    }

    private static string Format(IReadOnlyList<string> hits) =>
        hits.Count == 0 ? "(none)" : string.Join(", ", hits);

    private static bool IsBuildOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p =>
            p.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || p.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("bin-", StringComparison.OrdinalIgnoreCase));
    }

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
