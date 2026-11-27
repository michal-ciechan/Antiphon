// Test-source reads for the expected-skip scans (NightlyExpectedSkips), and the source-gap check itself
// (Get-NightlyExpectedSkipSourceGaps when it was PowerShell).
//
// ReadText is File.ReadAllText with a process-wide cache. A cached text is reused only while the file keeps the
// length and last-write time it had when read, and a text is cached only when that write time was already more
// than RacySeconds old at read time (git's racy-clean rule): any later rewrite stamps a newer time, so an edit
// that keeps the length cannot be missed. A nightly run scans the same tree several times (each in-process
// invocation of nightly-tests.ps1, and the source-gap, reason-gap and class-map walks of each); only the first
// read of each file pays for the I/O.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

        /// <summary>
        /// Roster identities with no matching qualified [Test] method in the test source tree.
        /// </summary>
        public static string[] ExpectedSkipSourceGaps(string testsRoot, string[] tests)
        {
            if (tests == null || tests.Length == 0) return Array.Empty<string>();
            var identities = tests.Select(test => test ?? "").ToArray();
            if (!Directory.Exists(testsRoot)) return identities;
            var missing = new HashSet<string>(identities, StringComparer.Ordinal);
            var classNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var identity in missing)
            {
                var methodDot = identity.LastIndexOf('.');
                if (methodDot <= 0) continue;
                var classDot = identity.LastIndexOf('.', methodDot - 1);
                classNames.Add(identity.Substring(classDot + 1, methodDot - classDot - 1));
            }
            var pending = new Stack<string>();
            pending.Push(testsRoot);
            while (pending.Count > 0 && missing.Count > 0)
            {
                var dir = pending.Pop();
                var name = Path.GetFileName(dir);
                if (string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("bin-", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var sub in Directory.EnumerateDirectories(dir)) pending.Push(sub);
                foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
                {
                    var text = ReadText(file);
                    if (!classNames.Any(candidate => text.Contains(candidate, StringComparison.Ordinal))) continue;
                    foreach (var type in CSharpSyntaxTree.ParseText(text).GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
                    {
                        if (!classNames.Contains(type.Identifier.ValueText)) continue;
                        var containers = type.AncestorsAndSelf().Reverse().Select(node => node switch
                        {
                            BaseNamespaceDeclarationSyntax ns => string.Concat(ns.Name.DescendantTokens().Select(token => token.ValueText)),
                            TypeDeclarationSyntax declaringType => declaringType.Identifier.ValueText,
                            _ => null,
                        }).Where(part => part != null);
                        var qualifiedClass = string.Join(".", containers);
                        foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
                        {
                            if (method.AttributeLists.SelectMany(list => list.Attributes)
                                .Any(attribute => attribute.Name.GetLastToken().ValueText is "Test" or "TestAttribute"))
                                missing.Remove(qualifiedClass + "." + method.Identifier.ValueText);
                        }
                    }
                }
            }
            return identities.Where(missing.Contains).ToArray();
        }
    }
}
