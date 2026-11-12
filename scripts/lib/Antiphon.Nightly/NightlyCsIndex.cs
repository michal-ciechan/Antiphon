// The C# source index behind Get-NightlyReachableSkipTexts (nightly-coverage.ps1): the class-to-file map of
// tests/**/*.cs, the per-file method parse and the reachable-skip walk, ported from the PowerShell functions
// that remain the fallback (Update-NightlyCsClassFiles, Import-NightlyCsFileMethods, Get-NightlyCsMethods,
// Get-NightlyCsCalls, Get-NightlyCsSkipLiterals and the walk itself). It returns the same texts in the same
// order, so it keeps PowerShell's comparisons where they differ from C#'s defaults:
//   - @{} keys (class map, method map, parsed files, visited set, call keywords) are OrdinalIgnoreCase;
//   - string -eq / -ceq compare with the invariant culture (linguistic), case-insensitive or not;
//   - char -eq ignores case ('n' also matches 'N' in a string escape);
//   - -match / -notmatch is a RegexOptions.IgnoreCase regex;
//   - .StartsWith(string) is the current culture, as PowerShell's method call is.
// Files are parsed lazily, in the order the walk asks for their classes, as the PowerShell index does: the
// order of a method's records (and so which targets a budget keeps) depends on that order. One instance lives
// as long as the PowerShell index did ($script:NightlyCsMethodIndex, once per repository root per script run).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>One parsed method: the PowerShell index's record.</summary>
    public sealed class NightlyCsMethod
    {
        public string Namespace { get; set; }
        public string Class { get; set; }
        public string Method { get; set; }
        public string File { get; set; }
        public string Text { get; set; }
        public string Body { get; set; }
        public string BodyMask { get; set; }
    }

    /// <summary>One call site: Get-NightlyCsCalls' record.</summary>
    public sealed class NightlyCsCall
    {
        public string Name { get; set; }
        public string Qualifier { get; set; }
    }

    public sealed class NightlyCsMethodIndex
    {
        private const string Modifiers = "public|private|internal|protected|static|async|override|virtual|sealed|new|unsafe|partial|extern|required|file";
        private static readonly Regex ClassRx = new Regex(@"\b(?:class|struct|record)\s+([A-Za-z_][A-Za-z0-9_]*)\b");
        private static readonly Regex NamespaceRx = new Regex(@"\bnamespace\s+([A-Za-z_][A-Za-z0-9_\.]*)");
        private static readonly Regex MethodRx = new Regex(@"(?m)^[ \t]*(?:\[[^\]]*\][ \t\r\n]*)*(?:(?:" + Modifiers + @")[ \t]+)+", RegexOptions.Compiled);
        private static readonly Regex MethodNameRx = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.IgnoreCase);
        private static readonly Regex SkipRx = new Regex(@"Skip\.Test\(\s*""((?:\\.|[^""\\])*)""|SkipTestException\(\s*""((?:\\.|[^""\\])*)""|NativeLinuxGate\.Require\(\s*""((?:\\.|[^""\\])*)""|NativeLinuxOnly\(\s*""((?:\\.|[^""\\])*)""|new\s+LiveClaudeRefusal\(\s*""((?:\\.|[^""\\])*)""");
        private static readonly HashSet<string> CallKeywords = new HashSet<string>(new[] {
            "if", "for", "while", "switch", "catch", "using", "lock", "return", "new", "throw", "foreach", "sizeof",
            "typeof", "nameof", "when", "fixed", "checked", "unchecked" }, StringComparer.OrdinalIgnoreCase);
        private static readonly NightlyCsMethod[] NoMethods = new NightlyCsMethod[0];

        private readonly string _testsRoot;
        private readonly Dictionary<string, List<string>> _classFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<NightlyCsMethod>> _methods = new Dictionary<string, List<NightlyCsMethod>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _parsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _mapped;

        /// <param name="testsRoot">Join-Path $RepoRoot 'tests', fully qualified.</param>
        public NightlyCsMethodIndex(string testsRoot) { _testsRoot = testsRoot; }

        public bool Mapped => _mapped;

        /// <summary>PowerShell's string -eq (ignoreCase) / -ceq: an invariant-culture comparison.</summary>
        private static bool PsEquals(string a, string b, bool ignoreCase) =>
            CultureInfo.InvariantCulture.CompareInfo.Compare(a, b, ignoreCase ? CompareOptions.IgnoreCase : CompareOptions.None) == 0;

        private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>Update-NightlyCsClassFiles.</summary>
        public void MapClassFiles()
        {
            if (_mapped) return;
            if (Directory.Exists(_testsRoot))
            {
                var pending = new Stack<string>();
                pending.Push(_testsRoot);
                while (pending.Count > 0)
                {
                    var dir = pending.Pop();
                    var name = Path.GetFileName(dir);
                    if (PsEquals(name, "bin", true) || PsEquals(name, "obj", true) || name.StartsWith("bin-", StringComparison.Ordinal)) continue;
                    foreach (var sub in Directory.EnumerateDirectories(dir)) pending.Push(sub);
                    foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
                    {
                        var text = System.IO.File.ReadAllText(file);
                        foreach (Match match in ClassRx.Matches(text))
                        {
                            var className = match.Groups[1].Value;
                            if (!_classFiles.TryGetValue(className, out var files))
                            {
                                files = new List<string>();
                                _classFiles[className] = files;
                            }
                            if (!files.Contains(file)) files.Add(file);
                        }
                    }
                }
            }
            _mapped = true;
        }

        /// <summary>The class map's keys and files (for comparison with the PowerShell index).</summary>
        public KeyValuePair<string, string[]>[] ClassFileEntries()
        {
            var result = new List<KeyValuePair<string, string[]>>();
            foreach (var pair in _classFiles) result.Add(new KeyValuePair<string, string[]>(pair.Key, pair.Value.ToArray()));
            return result.ToArray();
        }

        /// <summary>The method map's keys and records (for comparison with the PowerShell index).</summary>
        public KeyValuePair<string, NightlyCsMethod[]>[] MethodEntries()
        {
            var result = new List<KeyValuePair<string, NightlyCsMethod[]>>();
            foreach (var pair in _methods) result.Add(new KeyValuePair<string, NightlyCsMethod[]>(pair.Key, pair.Value.ToArray()));
            return result.ToArray();
        }

        /// <summary>ConvertFrom-NightlyCsStringLiteral.</summary>
        public static string StringLiteral(string raw)
        {
            raw = raw ?? "";
            var sb = new StringBuilder(raw.Length);
            var i = 0;
            while (i < raw.Length)
            {
                if (raw[i] == '\\' && i + 1 < raw.Length)
                {
                    var nch = raw[i + 1];
                    if (nch == 'n' || nch == 'N') sb.Append('\n');
                    else if (nch == 'r' || nch == 'R') sb.Append('\r');
                    else if (nch == 't' || nch == 'T') sb.Append('\t');
                    else if (nch == '"') sb.Append('"');
                    else if (nch == '\\') sb.Append('\\');
                    else sb.Append(nch);
                    i += 2;
                    continue;
                }
                sb.Append(raw[i]);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>ConvertTo-NightlyCsStructuralMask.</summary>
        public static string Mask(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var n = text.Length;
            var chars = text.ToCharArray();
            var i = 0;
            while (i < n)
            {
                var c = chars[i];
                if (c == '/' && i + 1 < n && chars[i + 1] == '/')
                {
                    while (i < n && chars[i] != '\n') { chars[i] = ' '; i++; }
                    continue;
                }
                if (c == '/' && i + 1 < n && chars[i + 1] == '*')
                {
                    chars[i] = ' '; i++;
                    if (i < n) { chars[i] = ' '; i++; }
                    while (i < n)
                    {
                        if (chars[i] == '*' && i + 1 < n && chars[i + 1] == '/')
                        {
                            chars[i] = ' '; chars[i + 1] = ' '; i += 2;
                            break;
                        }
                        if (chars[i] != '\n') chars[i] = ' ';
                        i++;
                    }
                    continue;
                }
                if (c == '\'')
                {
                    chars[i] = ' '; i++;
                    while (i < n)
                    {
                        var q = chars[i];
                        chars[i] = ' ';
                        i++;
                        if (q == '\\')
                        {
                            if (i < n) { chars[i] = ' '; i++; }
                            continue;
                        }
                        if (q == '\'' || q == '\n') break;
                    }
                    continue;
                }
                if (c == '@' && i + 1 < n && chars[i + 1] == '"')
                {
                    chars[i] = ' '; chars[i + 1] = ' '; i += 2;
                    while (i < n)
                    {
                        if (chars[i] == '"')
                        {
                            if (i + 1 < n && chars[i + 1] == '"')
                            {
                                chars[i] = ' '; chars[i + 1] = ' '; i += 2;
                                continue;
                            }
                            chars[i] = ' '; i++;
                            break;
                        }
                        if (chars[i] != '\n') chars[i] = ' ';
                        i++;
                    }
                    continue;
                }
                var dollars = 0;
                var k = i;
                if (c == '$')
                {
                    while (k < n && chars[k] == '$') { dollars++; k++; }
                }
                if (k < n && chars[k] == '"')
                {
                    var qcount = 0;
                    while (k < n && chars[k] == '"') { qcount++; k++; }
                    if (qcount == 2 && dollars == 0)
                    {
                        chars[i] = ' '; chars[i + 1] = ' '; i += 2;
                        continue;
                    }
                    if (qcount >= 3 || qcount == 1)
                    {
                        while (i < k) { chars[i] = ' '; i++; }
                        if (qcount >= 3)
                        {
                            while (i < n)
                            {
                                if (chars[i] == '"')
                                {
                                    var run = 0;
                                    var r = i;
                                    while (r < n && chars[r] == '"') { run++; r++; }
                                    if (run >= qcount)
                                    {
                                        while (i < r) { chars[i] = ' '; i++; }
                                        break;
                                    }
                                }
                                if (chars[i] != '\n') chars[i] = ' ';
                                i++;
                            }
                            continue;
                        }
                        while (i < n)
                        {
                            var q = chars[i];
                            chars[i] = ' ';
                            i++;
                            if (q == '\\')
                            {
                                if (i < n) { chars[i] = ' '; i++; }
                                continue;
                            }
                            if (q == '"' || q == '\n') break;
                        }
                        continue;
                    }
                }
                i++;
            }
            return new string(chars);
        }

        /// <summary>Get-NightlyCsMatchEnd.</summary>
        public static int MatchEnd(string mask, int open, char openChar, char closeChar)
        {
            var depth = 0;
            var n = mask.Length;
            for (var i = open; i < n; i++)
            {
                var ch = mask[i];
                if (ch == openChar) depth++;
                else if (ch == closeChar)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private sealed class NamespaceSpan
        {
            public string Name;
            public int Start;
            public int End;
            public bool FileScoped;
        }

        private sealed class ClassSpan
        {
            public string Name;
            public string Namespace;
            public int Open;
            public int End;
        }

        /// <summary>Import-NightlyCsFileMethods.</summary>
        public void ImportFile(string path)
        {
            if (!_parsed.Add(path)) return;
            if (!System.IO.File.Exists(path)) return;
            var text = System.IO.File.ReadAllText(path);
            var mask = Mask(text);
            var n = mask.Length;
            var namespaces = new List<NamespaceSpan>();
            foreach (Match match in NamespaceRx.Matches(mask))
            {
                var nsName = match.Groups[1].Value;
                var j = match.Index + match.Length;
                while (j < n && char.IsWhiteSpace(mask[j])) j++;
                if (j >= n) continue;
                if (mask[j] == ';')
                {
                    var start = j;
                    foreach (var prior in namespaces)
                    {
                        if (prior.FileScoped && prior.End >= start) prior.End = start - 1;
                    }
                    namespaces.Add(new NamespaceSpan { Name = nsName, Start = start, End = n - 1, FileScoped = true });
                }
                else if (mask[j] == '{')
                {
                    var end = MatchEnd(mask, j, '{', '}');
                    if (end < 0) end = n - 1;
                    namespaces.Add(new NamespaceSpan { Name = nsName, Start = j, End = end, FileScoped = false });
                }
            }
            var classes = new List<ClassSpan>();
            foreach (Match match in ClassRx.Matches(mask))
            {
                var className = match.Groups[1].Value;
                var j = match.Index + match.Length;
                var paren = 0;
                var angle = 0;
                var bodyOpen = -1;
                while (j < n)
                {
                    var ch = mask[j];
                    if (ch == '(') paren++;
                    else if (ch == ')') { if (paren > 0) paren--; }
                    else if (ch == '<') angle++;
                    else if (ch == '>') { if (angle > 0) angle--; }
                    else if (ch == '{' && paren == 0 && angle == 0) { bodyOpen = j; break; }
                    else if (ch == ';' && paren == 0 && angle == 0) break;
                    j++;
                }
                if (bodyOpen < 0) continue;
                var bodyEnd = MatchEnd(mask, bodyOpen, '{', '}');
                if (bodyEnd < 0) continue;
                var ns = "";
                var nsStart = -1;
                foreach (var item in namespaces)
                {
                    if (match.Index >= item.Start && match.Index <= item.End && item.Start >= nsStart)
                    {
                        ns = item.Name;
                        nsStart = item.Start;
                    }
                }
                classes.Add(new ClassSpan { Name = className, Namespace = ns, Open = bodyOpen, End = bodyEnd });
            }
            foreach (Match match in MethodRx.Matches(mask))
            {
                ClassSpan owner = null;
                var ownerOpen = -1;
                foreach (var classInfo in classes)
                {
                    if (match.Index > classInfo.Open && match.Index < classInfo.End && classInfo.Open >= ownerOpen)
                    {
                        owner = classInfo;
                        ownerOpen = classInfo.Open;
                    }
                }
                if (owner == null) continue;
                var pos = match.Index + match.Length;
                var limit = pos + 8000;
                if (limit > n) limit = n;
                var parenAt = -1;
                var sigAngle = 0;
                // The PowerShell scan also tests a paren depth it never raises; it is always 0.
                while (pos < limit)
                {
                    var ch = mask[pos];
                    if (ch == '(' && sigAngle == 0) { parenAt = pos; break; }
                    if (ch == '<') sigAngle++;
                    else if (ch == '>') { if (sigAngle > 0) sigAngle--; }
                    else if ((ch == '{' || ch == ';' || ch == '=') && sigAngle == 0) break;
                    pos++;
                }
                if (parenAt < 0) continue;
                var j = parenAt - 1;
                while (j >= 0 && char.IsWhiteSpace(mask[j])) j--;
                if (j >= 0 && mask[j] == '>')
                {
                    var angle = 1;
                    j--;
                    while (j >= 0 && angle > 0)
                    {
                        if (mask[j] == '>') angle++;
                        else if (mask[j] == '<') angle--;
                        j--;
                    }
                    while (j >= 0 && char.IsWhiteSpace(mask[j])) j--;
                }
                if (j < 0) continue;
                var nameEnd = j;
                while (j >= 0 && IsIdentChar(mask[j])) j--;
                if (nameEnd <= j) continue;
                var methodName = mask.Substring(j + 1, nameEnd - j);
                if (!MethodNameRx.IsMatch(methodName)) continue;
                var closeParen = MatchEnd(mask, parenAt, '(', ')');
                if (closeParen < 0) continue;
                var k = closeParen + 1;
                while (k < n && char.IsWhiteSpace(mask[k])) k++;
                var bodyStart = k;
                var bodyEnd = k;
                if (k < n && mask[k] == '{')
                {
                    var braceEnd = MatchEnd(mask, k, '{', '}');
                    if (braceEnd < 0) continue;
                    bodyEnd = braceEnd + 1;
                }
                else if (k + 1 < n && mask[k] == '=' && mask[k + 1] == '>')
                {
                    bodyStart = k;
                    var pDepth = 0;
                    var bDepth = 0;
                    while (k < n)
                    {
                        var ch = mask[k];
                        if (ch == '(') pDepth++;
                        else if (ch == ')') { if (pDepth > 0) pDepth--; }
                        else if (ch == '{') bDepth++;
                        else if (ch == '}') { if (bDepth > 0) bDepth--; }
                        else if (ch == ';' && pDepth == 0 && bDepth == 0) { bodyEnd = k + 1; break; }
                        k++;
                    }
                    if (bodyEnd <= bodyStart) continue;
                }
                else if (k < n && mask[k] == ';')
                {
                    bodyEnd = k + 1;
                }
                else
                {
                    continue;
                }
                var sliceEnd = bodyEnd;
                if (sliceEnd > text.Length) sliceEnd = text.Length;
                var record = new NightlyCsMethod
                {
                    Namespace = owner.Namespace,
                    Class = owner.Name,
                    Method = methodName,
                    File = path,
                    Text = text.Substring(match.Index, sliceEnd - match.Index),
                    Body = text.Substring(bodyStart, sliceEnd - bodyStart),
                    BodyMask = mask.Substring(bodyStart, sliceEnd - bodyStart),
                };
                var key = record.Class + "." + record.Method;
                if (!_methods.TryGetValue(key, out var list))
                {
                    list = new List<NightlyCsMethod>();
                    _methods[key] = list;
                }
                list.Add(record);
            }
        }

        /// <summary>Get-NightlyCsMethods.</summary>
        public NightlyCsMethod[] GetMethods(string className, string methodName, string ns)
        {
            className = className ?? "";
            methodName = methodName ?? "";
            ns = ns ?? "";
            MapClassFiles();
            if (_classFiles.TryGetValue(className, out var files))
            {
                foreach (var file in files.ToArray()) ImportFile(file);
            }
            if (!_methods.TryGetValue(className + "." + methodName, out var list)) return NoMethods;
            var all = list.ToArray();
            if (string.IsNullOrWhiteSpace(ns)) return all;
            var matched = new List<NightlyCsMethod>();
            foreach (var record in all)
            {
                if (PsEquals(record.Namespace, ns, false)) matched.Add(record);
            }
            return matched.ToArray();
        }

        /// <summary>Get-NightlyCsSkipLiterals.</summary>
        public static string[] SkipLiterals(string text, bool foldRefusal)
        {
            if (string.IsNullOrEmpty(text)) return new string[0];
            var found = new List<string>();
            foreach (Match match in SkipRx.Matches(text))
            {
                var group = 0;
                if (match.Groups[1].Success) group = 1;
                else if (match.Groups[2].Success) group = 2;
                else if (match.Groups[3].Success) group = 3;
                else if (match.Groups[4].Success) group = 4;
                else if (match.Groups[5].Success) group = 5;
                if (group == 0) continue;
                var literal = StringLiteral(match.Groups[group].Value);
                if (group == 5)
                {
                    if (foldRefusal) found.Add("20 " + literal);
                }
                else
                {
                    found.Add(literal);
                }
            }
            return found.ToArray();
        }

        /// <summary>Get-NightlyCsCalls.</summary>
        public static NightlyCsCall[] Calls(string bodyMask)
        {
            if (string.IsNullOrEmpty(bodyMask)) return new NightlyCsCall[0];
            var calls = new List<NightlyCsCall>();
            var n = bodyMask.Length;
            for (var i = 0; i < n; i++)
            {
                if (bodyMask[i] != '(') continue;
                var j = i - 1;
                while (j >= 0 && char.IsWhiteSpace(bodyMask[j])) j--;
                if (j >= 0 && bodyMask[j] == '>')
                {
                    var depth = 1;
                    j--;
                    while (j >= 0 && depth > 0)
                    {
                        if (bodyMask[j] == '>') depth++;
                        else if (bodyMask[j] == '<') depth--;
                        j--;
                    }
                    while (j >= 0 && char.IsWhiteSpace(bodyMask[j])) j--;
                }
                if (j < 0 || !IsIdentChar(bodyMask[j])) continue;
                var end = j;
                while (j >= 0 && IsIdentChar(bodyMask[j])) j--;
                var name = bodyMask.Substring(j + 1, end - j);
                if (CallKeywords.Contains(name)) continue;
                var qual = "";
                var k = j;
                while (k >= 0 && char.IsWhiteSpace(bodyMask[k])) k--;
                if (k >= 0 && bodyMask[k] == '.')
                {
                    k--;
                    while (k >= 0 && char.IsWhiteSpace(bodyMask[k])) k--;
                    if (k >= 0 && IsIdentChar(bodyMask[k]))
                    {
                        var qEnd = k;
                        while (k >= 0 && IsIdentChar(bodyMask[k])) k--;
                        qual = bodyMask.Substring(k + 1, qEnd - k);
                    }
                }
                if (PsEquals(qual, "this", true)) qual = "";
                calls.Add(new NightlyCsCall { Name = name, Qualifier = qual });
            }
            return calls.ToArray();
        }

        private struct WalkItem
        {
            public NightlyCsMethod Method;
            public int Depth;
        }

        /// <summary>
        /// Get-NightlyReachableSkipTexts after its identity split: the skip texts reachable from
        /// Namespace.Class.Method through at most three call levels, with the same budgets (24 same-class
        /// helpers, 12 gate calls, 12 other calls) and the same breadth-first order.
        /// </summary>
        public string[] ReachableSkipTexts(string className, string methodName, string ns, bool fold)
        {
            var starts = GetMethods(className, methodName, ns);
            var texts = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<WalkItem>();
            foreach (var start in starts)
            {
                if (start == null) continue;
                queue.Enqueue(new WalkItem { Method = start, Depth = 0 });
            }
            var followedOther = 0;
            var followedSame = 0;
            var followedGate = 0;
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                var current = item.Method;
                if (current == null) continue;
                var visit = current.File + "|" + current.Namespace + "|" + current.Class + "|" + current.Method + "|" + current.Text.Length.ToString(CultureInfo.InvariantCulture);
                if (!visited.Add(visit)) continue;
                texts.AddRange(SkipLiterals(current.Text, fold));
                if (item.Depth >= 3) continue;
                foreach (var call in Calls(current.BodyMask))
                {
                    var callName = call.Name;
                    var sameClass = string.IsNullOrWhiteSpace(call.Qualifier);
                    var gate = PsEquals(callName, "Resolve", false) || PsEquals(callName, "CreateAsync", false)
                        || callName.StartsWith("Require", StringComparison.CurrentCulture);
                    if (sameClass) { if (followedSame >= 24) continue; }
                    else if (gate) { if (followedGate >= 12) continue; }
                    else { if (followedOther >= 12) continue; }
                    var targets = sameClass
                        ? GetMethods(current.Class, callName, current.Namespace)
                        : GetMethods(call.Qualifier, callName, "");
                    foreach (var target in targets)
                    {
                        if (target == null) continue;
                        if (sameClass)
                        {
                            if (followedSame >= 24) break;
                            followedSame++;
                        }
                        else if (gate)
                        {
                            if (followedGate >= 12) break;
                            followedGate++;
                        }
                        else
                        {
                            if (followedOther >= 12) break;
                            followedOther++;
                        }
                        queue.Enqueue(new WalkItem { Method = target, Depth = item.Depth + 1 });
                    }
                }
            }
            return texts.ToArray();
        }
    }
}
