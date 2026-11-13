using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public double ReadMilliseconds { get; private set; }
        public double DeclarationMilliseconds { get; private set; }
        public double DeriveMilliseconds { get; private set; }

        public static SelectorCensus Scan(string repo, Regex declaration, Regex[] derive)
        {
            var result = new SelectorCensus();
            var root = Path.Combine(repo, "tests");
            if (!Directory.Exists(root)) return result;
            var prefix = repo.Replace('\\', '/').TrimEnd('/') + "/";
            long readTicks = 0, declarationTicks = 0, deriveTicks = 0;
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
                    var phaseStart = Stopwatch.GetTimestamp();
                    var text = File.ReadAllText(file);
                    readTicks += Stopwatch.GetTimestamp() - phaseStart;
                    phaseStart = Stopwatch.GetTimestamp();
                    foreach (Match match in DeclarationLines(text, declaration))
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
                    declarationTicks += Stopwatch.GetTimestamp() - phaseStart;
                    phaseStart = Stopwatch.GetTimestamp();
                    foreach (var pattern in derive)
                    {
                        if (!pattern.IsMatch(text)) continue;
                        result.DerivedFiles.Add(relative);
                        break;
                    }
                    deriveTicks += Stopwatch.GetTimestamp() - phaseStart;
                }
            }
            foreach (var files in result.FilesByClass.Values)
            {
                var sorted = new SortedSet<string>(files, StringComparer.Ordinal);
                files.Clear();
                files.AddRange(sorted);
            }
            result.ReadMilliseconds = readTicks * 1000.0 / Stopwatch.Frequency;
            result.DeclarationMilliseconds = declarationTicks * 1000.0 / Stopwatch.Frequency;
            result.DeriveMilliseconds = deriveTicks * 1000.0 / Stopwatch.Frequency;
            return result;
        }

        // The caller's declaration regex is line-bound and requires the literal "class".
        // Only candidate lines need the regex. Keep its verdict (including lexical false
        // positives in raw strings/comments), rather than substituting a different parser.
        private static IEnumerable<Match> DeclarationLines(string text, Regex declaration)
        {
            var offset = 0;
            while (offset < text.Length)
            {
                var word = text.IndexOf("class", offset, StringComparison.Ordinal);
                if (word < 0) yield break;
                var start = text.LastIndexOf('\n', word) + 1;
                var end = text.IndexOf('\n', word);
                if (end < 0) end = text.Length;
                foreach (Match match in declaration.Matches(text.Substring(start, end - start)))
                    yield return match;
                offset = end < text.Length ? end + 1 : end;
            }
        }
    }
}
