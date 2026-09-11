using System.Reflection;
using System.Text;
using TUnit.Core;

namespace Antiphon.TestSupport;

public sealed record ClassifiedTestClass(
    string FullName,
    string SimpleName,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> TestMethods,
    bool HasMethodLevelSlow,
    bool InheritsTests);

public static class TestClassificationMetadata
{
    public const string Slow = "Slow";
    public const string OptIn = "OptIn";
    public const string Unit = "Unit";
    public const string Integration = "Integration";

    public static IReadOnlyList<ClassifiedTestClass> Read(Assembly assembly)
    {
        var list = new List<ClassifiedTestClass>();
        foreach (var type in assembly.GetTypes())
        {
            if (!type.IsClass || type.IsAbstract)
                continue;
            var methods = GetTestMethods(type);
            if (methods.Count == 0)
                continue;
            var categories = GetCategories(type);
            var methodSlow = methods.Any(m => GetCategories(m).Contains(Slow, StringComparer.Ordinal));
            var inherits = type.GetCustomAttributes(inherit: false)
                .Any(a => a.GetType().Name is "InheritsTestsAttribute" or "InheritsTests");
            list.Add(new ClassifiedTestClass(
                type.FullName ?? type.Name,
                type.Name,
                categories,
                methods.Select(m => m.Name).Distinct(StringComparer.Ordinal).ToArray(),
                methodSlow,
                inherits));
        }
        return list.OrderBy(c => c.FullName, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> GetCategories(MemberInfo member)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attr in member.GetCustomAttributes(inherit: true))
        {
            string? value = null;
            if (attr is CategoryAttribute category)
                value = category.Category;
            else if (attr.GetType().Name is "CategoryAttribute" or "Category")
            {
                value = attr.GetType().GetProperty("Category")?.GetValue(attr) as string
                    ?? attr.GetType().GetProperty("Value")?.GetValue(attr) as string;
            }
            if (string.IsNullOrWhiteSpace(value))
                value = attr.GetType().GetProperty("Value")?.GetValue(attr) as string;
            if (!string.IsNullOrWhiteSpace(value))
                set.Add(value);
        }
        return set.OrderBy(c => c, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<MethodInfo> GetTestMethods(Type type)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var inherits = type.GetCustomAttributes(inherit: false)
            .Any(a => a.GetType().Name is "InheritsTestsAttribute" or "InheritsTests");
        var methods = new List<MethodInfo>();
        foreach (var method in type.GetMethods(flags))
        {
            if (method.DeclaringType != type && !inherits)
                continue;
            if (method.GetCustomAttributes(inherit: true).Any(a => a.GetType().Name is "TestAttribute" or "Test"))
                methods.Add(method);
        }
        return methods;
    }

    public sealed record RegistryEntry(string Name, string Reason, int Line);

    public static IReadOnlyList<RegistryEntry> ReadRegistry(string path)
    {
        var lines = File.ReadAllLines(path);
        var entries = new List<RegistryEntry>();
        string? pendingReason = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
                continue;
            if (trimmed.StartsWith('#'))
            {
                pendingReason = trimmed.TrimStart('#').Trim();
                continue;
            }
            entries.Add(new RegistryEntry(trimmed, pendingReason ?? "", i + 1));
            pendingReason = null;
        }
        return entries;
    }

    public static string AssertRegistryMatches(Assembly assembly, string registryPath, bool repositoryMode)
    {
        var classes = Read(assembly);
        var errors = new List<string>();
        var slow = classes.Where(c => c.Categories.Contains(Slow, StringComparer.Ordinal)).ToArray();
        if (!File.Exists(registryPath))
        {
            if (slow.Length > 0)
                errors.Add("missing registry " + registryPath);
            return Format(errors);
        }
        var entries = ReadRegistry(registryPath);
        var bySimple = classes.ToLookup(c => c.SimpleName, StringComparer.OrdinalIgnoreCase);
        var byFull = classes.ToDictionary(c => c.FullName, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Reason) ||
                (entry.Reason.IndexOf("CARD-", StringComparison.OrdinalIgnoreCase) < 0 &&
                 entry.Reason.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0 &&
                 entry.Reason.IndexOf("real Git", StringComparison.OrdinalIgnoreCase) < 0 &&
                 entry.Reason.IndexOf("measured", StringComparison.OrdinalIgnoreCase) < 0 &&
                 entry.Reason.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) < 0 &&
                 entry.Reason.IndexOf("legacy", StringComparison.OrdinalIgnoreCase) < 0 &&
                 entry.Reason.IndexOf("no Slow", StringComparison.OrdinalIgnoreCase) < 0 &&
                 entry.Reason.IndexOf("capstone", StringComparison.OrdinalIgnoreCase) < 0 &&
                 entry.Reason.IndexOf("aggregate", StringComparison.OrdinalIgnoreCase) < 0 &&
                 entry.Reason.IndexOf("CARD-0484", StringComparison.OrdinalIgnoreCase) < 0 &&
                 entry.Reason.IndexOf("CARD-0487", StringComparison.OrdinalIgnoreCase) < 0))
            {
                errors.Add("missing-reason " + entry.Name);
            }
            if (repositoryMode && entry.Name.IndexOf('.') < 0)
                errors.Add("simple-name-refused " + entry.Name);
            if (!seen.Add(entry.Name))
                errors.Add("duplicate " + entry.Name);
            ClassifiedTestClass? match = null;
            if (byFull.TryGetValue(entry.Name, out var full))
                match = full;
            else if (!repositoryMode)
            {
                var simple = bySimple[entry.Name].ToArray();
                if (simple.Length > 1)
                    errors.Add("ambiguous " + entry.Name);
                else if (simple.Length == 1)
                    match = simple[0];
            }
            if (match is null)
                errors.Add("unknown " + entry.Name);
            else if (!match.Categories.Contains(Slow, StringComparer.Ordinal))
                errors.Add("unmarked-registered " + match.FullName);
        }

        foreach (var cls in classes)
        {
            if (cls.HasMethodLevelSlow)
                errors.Add("method-level-slow " + cls.FullName);
        }

        foreach (var cls in slow)
        {
            var registered = entries.Any(e =>
                string.Equals(e.Name, cls.FullName, StringComparison.OrdinalIgnoreCase) ||
                (!repositoryMode && string.Equals(e.Name, cls.SimpleName, StringComparison.OrdinalIgnoreCase)));
            if (!registered)
                errors.Add("unregistered-marked " + cls.FullName);
        }

        if (string.Equals(assembly.GetName().Name, "Antiphon.Tests", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var cls in classes)
            {
                var unit = cls.Categories.Contains(Unit, StringComparer.Ordinal);
                var integ = cls.Categories.Contains(Integration, StringComparer.Ordinal);
                if (unit == integ)
                    errors.Add("lane-xor " + cls.FullName);
            }
        }

        return Format(errors);
    }

    public static string FindRegistryPath(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? "";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var sln = Path.Combine(dir.FullName, "Antiphon.sln");
            if (File.Exists(sln))
            {
                var relative = name switch
                {
                    "Antiphon.Tests" => Path.Combine("tests", "Antiphon.Tests", "slow-tests-allowlist.txt"),
                    "Antiphon.SessionRunner.Tests" => Path.Combine("tests", "Antiphon.SessionRunner.Tests", "slow-tests-allowlist.txt"),
                    "Antiphon.PtyHost.Tests" => Path.Combine("tests", "Antiphon.PtyHost.Tests", "slow-tests-allowlist.txt"),
                    "Antiphon.Agents.Pty.Tests" => Path.Combine("tests", "Antiphon.Agents.Pty.Tests", "slow-tests-allowlist.txt"),
                    "Antiphon.Messaging.Tests" => Path.Combine("tests", "Antiphon.Messaging.Tests", "slow-tests-allowlist.txt"),
                    "Antiphon.E2E" => Path.Combine("tests", "Antiphon.E2E", "slow-tests-allowlist.txt"),
                    _ => Path.Combine("slow-tests-allowlist.txt")
                };
                return Path.Combine(dir.FullName, relative);
            }
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "slow-tests-allowlist.txt");
    }

    private static string Format(IReadOnlyList<string> errors)
    {
        if (errors.Count == 0)
            return "";
        var sb = new StringBuilder();
        foreach (var e in errors)
            sb.AppendLine(e);
        return sb.ToString();
    }
}
