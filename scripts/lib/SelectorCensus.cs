using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Antiphon.Selector
{
    // No retained filesystem state: each Scan reads the current tree and returns a new census.
    // C# 5 syntax keeps Add-Type compatible with Windows PowerShell 5.1.
    public sealed class SelectorCensus
    {
        public readonly Dictionary<string, List<string>> FilesByClass =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<string>> ClassesByFile =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public readonly HashSet<string> DerivedFiles = new HashSet<string>(StringComparer.Ordinal);

        public static SelectorCensus Scan(string repo, Regex declaration, Regex[] derive)
        {
            var result = new SelectorCensus();
            var root = Path.Combine(repo, "tests");
            if (!Directory.Exists(root)) return result;
            var prefix = repo.Replace('\\', '/').TrimEnd('/') + "/";
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count != 0)
            {
                var dir = stack.Pop();
                foreach (var child in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(child);
                    if (String.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(name, "obj", StringComparison.OrdinalIgnoreCase)) continue;
                    stack.Push(child);
                }
                foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
                {
                    var full = file.Replace('\\', '/');
                    var relative = full.StartsWith(prefix) ? full.Substring(prefix.Length) : full;
                    if (relative.Contains("/bin/") || relative.Contains("/obj/")) continue;
                    var text = File.ReadAllText(file);
                    foreach (Match match in declaration.Matches(text))
                    {
                        var name = match.Groups[1].Value;
                        List<string> files;
                        if (!result.FilesByClass.TryGetValue(name, out files))
                        {
                            files = new List<string>();
                            result.FilesByClass.Add(name, files);
                        }
                        files.Add(relative);
                        List<string> names;
                        if (!result.ClassesByFile.TryGetValue(relative, out names))
                        {
                            names = new List<string>();
                            result.ClassesByFile.Add(relative, names);
                        }
                        if (!names.Contains(name)) names.Add(name);
                    }
                    foreach (var pattern in derive)
                    {
                        if (!pattern.IsMatch(text)) continue;
                        result.DerivedFiles.Add(relative);
                        break;
                    }
                }
            }
            foreach (var files in result.FilesByClass.Values)
            {
                var sorted = new SortedSet<string>(files, StringComparer.Ordinal);
                files.Clear();
                files.AddRange(sorted);
            }
            return result;
        }
    }
}
