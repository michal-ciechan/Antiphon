// Test-source reads for the expected-skip scans (nightly-coverage.ps1), and the compiled form of
// Get-NightlyExpectedSkipSourceGaps, whose PowerShell body stays as the fallback (Windows PowerShell 5.1, no core).
//
// ReadText is File.ReadAllText with a process-wide cache. A cached text is reused only while the file keeps the
// length and last-write time it had when read, and a text is cached only when that write time was already more
// than RacySeconds old at read time (git's racy-clean rule): any later rewrite stamps a newer time, so an edit
// that keeps the length cannot be missed. A nightly run scans the same tree several times (each in-process
// invocation of nightly-tests.ps1, and the source-gap, reason-gap and class-map walks of each); only the first
// read of each file pays for the I/O.
//
// ExpectedSkipSourceGaps keeps the PowerShell comparisons: @{} keys are OrdinalIgnoreCase (the first spelling
// inserted is the one searched for), string .LastIndexOf('.') is the current culture, and the two patterns are
// case-sensitive [regex]::IsMatch patterns.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    public static class NightlyTestSources
    {
        public const double RacySeconds = 3;

        private sealed class Entry
        {
            public long Length;
            public long WriteTicks;
            public string Text;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Entry> Texts = new Dictionary<string, Entry>(StringComparer.Ordinal);

        /// <summary>File.ReadAllText(path), reusing an earlier read while the file is provably unchanged.</summary>
        public static string ReadText(string path)
        {
            var before = new FileInfo(path);
            if (!before.Exists) return File.ReadAllText(path);
            var length = before.Length;
            var ticks = before.LastWriteTimeUtc.Ticks;
            lock (Gate)
            {
                if (Texts.TryGetValue(path, out var hit) && hit.Length == length && hit.WriteTicks == ticks) return hit.Text;
            }
            var text = File.ReadAllText(path);
            var after = new FileInfo(path);
            var settled = after.Exists && after.Length == length && after.LastWriteTimeUtc.Ticks == ticks
                && (DateTime.UtcNow - after.LastWriteTimeUtc).TotalSeconds > RacySeconds;
            lock (Gate)
            {
                if (settled) Texts[path] = new Entry { Length = length, WriteTicks = ticks, Text = text };
                else Texts.Remove(path);
            }
            return text;
        }

        /// <summary>Drops every cached text (tests).</summary>
        public static void Clear()
        {
            lock (Gate) Texts.Clear();
        }

        private sealed class ClassBucket
        {
            public string Name;
            public Regex Declaration;
            public bool SeenClass;
            public readonly Dictionary<string, bool> Methods = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> MethodOrder = new List<string>();
            public readonly Dictionary<string, Regex> MethodPatterns = new Dictionary<string, Regex>(StringComparer.Ordinal);
        }

        private static bool Split(string test, out string className, out string method)
        {
            className = null;
            method = null;
            var methodDot = test.LastIndexOf(".", StringComparison.CurrentCulture);
            if (methodDot < 1) return false;
            method = test.Substring(methodDot + 1);
            var head = test.Substring(0, methodDot);
            var classDot = head.LastIndexOf(".", StringComparison.CurrentCulture);
            className = classDot < 0 ? head : head.Substring(classDot + 1);
            return true;
        }

        /// <summary>
        /// Get-NightlyExpectedSkipSourceGaps for the already platform-filtered roster tests: each test whose class
        /// is not declared in tests/**/*.cs, or whose method name is never followed by '(' in a file declaring it.
        /// </summary>
        public static string[] ExpectedSkipSourceGaps(string testsRoot, string[] tests)
        {
            if (tests == null || tests.Length == 0) return new string[0];
            var byClass = new Dictionary<string, ClassBucket>(StringComparer.OrdinalIgnoreCase);
            var classOrder = new List<ClassBucket>();
            foreach (var raw in tests)
            {
                var test = raw ?? string.Empty;
                if (!Split(test, out var className, out var method)) continue;
                if (!byClass.TryGetValue(className, out var bucket))
                {
                    bucket = new ClassBucket
                    {
                        Name = className,
                        Declaration = new Regex(@"\bclass\s+" + Regex.Escape(className) + @"\b"),
                    };
                    byClass[className] = bucket;
                    classOrder.Add(bucket);
                }
                if (!bucket.Methods.ContainsKey(method))
                {
                    bucket.MethodOrder.Add(method);
                    bucket.MethodPatterns[method] = new Regex(@"\b" + Regex.Escape(method) + @"\s*\(");
                }
                bucket.Methods[method] = false;
            }
            if (Directory.Exists(testsRoot))
            {
                var pending = new Stack<string>();
                pending.Push(testsRoot);
                while (pending.Count > 0)
                {
                    var dir = pending.Pop();
                    var name = Path.GetFileName(dir);
                    if (string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("bin-", StringComparison.Ordinal)) continue;
                    foreach (var sub in Directory.EnumerateDirectories(dir)) pending.Push(sub);
                    foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
                    {
                        var text = ReadText(file);
                        foreach (var bucket in classOrder)
                        {
                            // Both patterns hold the name literally (case-sensitive): a file without it cannot match.
                            if (text.IndexOf(bucket.Name, StringComparison.Ordinal) < 0) continue;
                            if (!bucket.Declaration.IsMatch(text)) continue;
                            bucket.SeenClass = true;
                            foreach (var method in bucket.MethodOrder)
                            {
                                if (bucket.Methods[method]) continue;
                                if (bucket.MethodPatterns[method].IsMatch(text)) bucket.Methods[method] = true;
                            }
                        }
                    }
                }
            }
            var gaps = new List<string>();
            foreach (var raw in tests)
            {
                var test = raw ?? string.Empty;
                if (!Split(test, out var className, out var method)) { gaps.Add(test); continue; }
                if (!byClass.TryGetValue(className, out var bucket) || !bucket.SeenClass
                    || !bucket.Methods.TryGetValue(method, out var found) || !found) gaps.Add(test);
            }
            return gaps.ToArray();
        }
    }
}
