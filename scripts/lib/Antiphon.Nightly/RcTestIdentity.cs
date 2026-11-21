// CARD-2009: the test roster identities of one C# file. A roster key is namespace.class|method|Arguments row, not the
// [Test] count, so a net-zero rename, a move between files and an added [Arguments] row all change it. The scan skips
// comments and every C# string form (regular, verbatim, interpolated, raw and interpolated raw), so a quote or brace
// inside a string never ends a scope early. A form the scan drops is counted: more [Test attributes than matched
// methods, or more Arguments( openers than captured rows, marks the file incomplete, and an incomplete file selects.
#nullable disable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    public sealed class RcTestIdentityParse
    {
        public Dictionary<string, int> Identities { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public int Matched { get; set; }
        public int CapturedArguments { get; set; }
        public int TestAttributes { get; set; }
        public int ArgumentOpeners { get; set; }

        /// <summary>A form the scanner dropped must select. Equal empty maps are not proof of no change.</summary>
        public bool Incomplete => TestAttributes > Matched || ArgumentOpeners > CapturedArguments;
    }

    public static class RcTestIdentity
    {
        private static readonly Regex TestAttribute = new Regex(@"\[Test(?![A-Za-z0-9_])", RegexOptions.CultureInvariant);
        // The attribute run test matches like PowerShell -match: case-insensitive.
        private static readonly Regex TestAttributeAnyCase = new Regex(@"\[Test(?![A-Za-z0-9_])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex ArgumentsOpener = new Regex(@"(?:\[|,)\s*Arguments\(", RegexOptions.CultureInvariant);
        private static readonly Regex Namespace = new Regex(@"namespace\s+([A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.CultureInvariant);
        private static readonly Regex BatchSuffix = new Regex(@"_Batch\d+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly HashSet<string> Modifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "public", "private", "protected", "internal", "static", "async", "sealed", "override", "virtual", "new", "unsafe", "partial", "file",
        };

        /// <summary>The text without whole-line comments (lines that start with //, * or /*), one '\n' per kept line.</summary>
        public static string CommentStripped(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var kept = new StringBuilder();
            foreach (var line in text.Split('\n'))
            {
                var trim = line.Trim().TrimEnd('\r');
                if (trim.StartsWith("//", StringComparison.Ordinal) || trim.StartsWith("*", StringComparison.Ordinal) || trim.StartsWith("/*", StringComparison.Ordinal)) continue;
                kept.Append(line.TrimEnd('\r')).Append('\n');
            }
            return kept.ToString();
        }

        /// <summary>True when the two texts differ in roster identity, or either side has a form the scan dropped.</summary>
        public static bool Changed(string before, string after)
        {
            var left = Parse(before);
            var right = Parse(after);
            if (left.Incomplete || right.Incomplete) return true;
            if (left.Identities.Count != right.Identities.Count) return true;
            foreach (var pair in left.Identities)
            {
                if (!right.Identities.TryGetValue(pair.Key, out var count) || count != pair.Value) return true;
            }
            return false;
        }

        /// <summary>
        /// The identities of one file. An attribute run may contain ']', span lines and precede a generic or qualified
        /// Task. The enclosing class is the open brace scope, so a nested class declared above does not steal the method.
        /// </summary>
        public static RcTestIdentityParse Parse(string text)
        {
            var parse = new RcTestIdentityParse();
            var source = CommentStripped(text);
            if (source.Length == 0) return parse;
            // Counted on the comment-stripped text, not inside the scan, so a skipped region still shows as
            // testAttributes or argumentOpeners above the capture.
            parse.TestAttributes = TestAttribute.Matches(source).Count;
            parse.ArgumentOpeners = ArgumentsOpener.Matches(source).Count;
            var ns = Namespace.Match(source);
            var nsName = ns.Success ? ns.Groups[1].Value : "";
            var classNames = new List<string>();
            var classDepths = new List<int>();
            var depth = 0;
            string pending = null;
            var i = 0;
            while (i < source.Length)
            {
                var c = source[i];
                if (c == '/' || c == '"' || c == '\'' || c == '@' || c == '$')
                {
                    var skip = SkipNonCode(source, i);
                    if (skip >= 0) { i = skip; continue; }
                }
                if (c == 'c' || c == 'r')
                {
                    if (TryReadClassAt(source, i, out var className, out var next)) { pending = className; i = next; continue; }
                }
                if (c == '{')
                {
                    depth++;
                    if (pending != null) { classNames.Add(pending); classDepths.Add(depth); pending = null; }
                    i++;
                    continue;
                }
                if (c == '}')
                {
                    if (classDepths.Count > 0 && classDepths[classDepths.Count - 1] == depth)
                    {
                        classNames.RemoveAt(classNames.Count - 1);
                        classDepths.RemoveAt(classDepths.Count - 1);
                    }
                    if (depth > 0) depth--;
                    pending = null;
                    i++;
                    continue;
                }
                if (c != '[') { i++; continue; }
                var pos = i;
                var attr = new StringBuilder();
                var any = false;
                while (pos < source.Length && source[pos] == '[')
                {
                    var end = CloseDelimiter(source, pos, '[', ']');
                    if (end < 0) break;
                    attr.Append(source, pos, end - pos + 1);
                    any = true;
                    pos = SkipWhitespace(source, end + 1);
                }
                if (!any) { i++; continue; }
                var attrText = attr.ToString();
                if (!TestAttributeAnyCase.IsMatch(attrText) || !TryMethodSignature(source, pos, out var method, out var open)) { i = pos; continue; }
                parse.Matched++;
                var rows = ArgumentInners(attrText);
                parse.CapturedArguments += rows.Count;
                var type = classNames.Count > 0 ? classNames[classNames.Count - 1] : "";
                var bare = nsName.Length > 0 && type.Length > 0 ? nsName + "." + type : type;
                var name = BatchSuffix.Replace(method, "");
                if (rows.Count == 0) rows.Add("");
                foreach (var argument in rows)
                {
                    var id = bare + "|" + name + "|" + argument;
                    parse.Identities.TryGetValue(id, out var seen);
                    parse.Identities[id] = seen + 1;
                }
                i = open + 1;
            }
            return parse;
        }

        // [Arguments( and the comma-list form [Test, Arguments( are both rows.
        private static List<string> ArgumentInners(string attrText)
        {
            var rows = new List<string>();
            var i = 0;
            while (i < attrText.Length)
            {
                var found = ArgumentsOpener.Match(attrText, i);
                if (!found.Success) break;
                var open = found.Index + found.Length - 1;
                var close = CloseDelimiter(attrText, open, '(', ')');
                if (close < 0) break;
                rows.Add(attrText.Substring(open + 1, close - open - 1));
                i = close + 1;
            }
            return rows;
        }

        private static bool TryReadClassAt(string text, int index, out string name, out int next)
        {
            name = null;
            next = index;
            if (index > 0)
            {
                var prev = text[index - 1];
                if (prev == '_' || char.IsLetterOrDigit(prev)) return false;
            }
            if (!ReadIdentifier(text, index, out var kind, out var after) || (kind != "class" && kind != "record")) return false;
            var at = SkipWhitespace(text, after);
            if (!ReadIdentifier(text, at, out var found, out after)) return false;
            if (kind == "record" && (found == "class" || found == "struct"))
            {
                at = SkipWhitespace(text, after);
                if (!ReadIdentifier(text, at, out found, out after)) return false;
            }
            name = found;
            next = after;
            return true;
        }

        private static bool TryMethodSignature(string text, int index, out string name, out int open)
        {
            name = null;
            open = -1;
            var i = SkipWhitespace(text, index);
            while (ReadIdentifier(text, i, out var word, out var after) && Modifiers.Contains(word)) i = SkipWhitespace(text, after);
            var typeEnd = ReturnTypeEnd(text, i);
            if (typeEnd < 0) return false;
            i = SkipWhitespace(text, typeEnd);
            if (!ReadIdentifier(text, i, out var found, out var next)) return false;
            i = SkipWhitespace(text, next);
            if (i < text.Length && text[i] == '<')
            {
                var close = CloseDelimiter(text, i, '<', '>');
                if (close < 0) return false;
                i = SkipWhitespace(text, close + 1);
            }
            if (i >= text.Length || text[i] != '(') return false;
            name = found;
            open = i;
            return true;
        }

        // void, or a Task/ValueTask (qualified, generic, nullable). Any other return type is not a test method.
        private static int ReturnTypeEnd(string text, int index)
        {
            if (!ReadIdentifier(text, index, out var ident, out var i)) return -1;
            if (ident == "void") return i;
            var last = ident;
            while (i < text.Length && text[i] == '.')
            {
                if (!ReadIdentifier(text, i + 1, out var part, out var next)) return -1;
                last = part;
                i = next;
            }
            if (last != "Task" && last != "ValueTask") return -1;
            if (i < text.Length && text[i] == '<')
            {
                var close = CloseDelimiter(text, i, '<', '>');
                if (close < 0) return -1;
                i = close + 1;
            }
            if (i < text.Length && text[i] == '?') i++;
            return i;
        }

        private static bool ReadIdentifier(string text, int index, out string identifier, out int next)
        {
            identifier = null;
            next = index;
            if (index >= text.Length) return false;
            var c = text[index];
            if (c != '_' && !char.IsLetter(c)) return false;
            var j = index + 1;
            while (j < text.Length && (text[j] == '_' || char.IsLetterOrDigit(text[j]))) j++;
            identifier = text.Substring(index, j - index);
            next = j;
            return true;
        }

        private static int SkipWhitespace(string text, int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
            return index;
        }

        private static int Run(string text, int index, char c)
        {
            var run = 0;
            while (index + run < text.Length && text[index + run] == c) run++;
            return run;
        }

        private static int CloseDelimiter(string text, int openAt, char open, char close)
        {
            var depth = 0;
            var i = openAt;
            while (i < text.Length)
            {
                var skip = SkipNonCode(text, i);
                if (skip >= 0) { i = skip; continue; }
                var c = text[i];
                if (c == open) depth++;
                else if (c == close)
                {
                    depth--;
                    if (depth == 0) return i;
                }
                i++;
            }
            return -1;
        }

        /// <summary>The index just past a comment or string literal that starts at index, or -1 when none starts there.</summary>
        private static int SkipNonCode(string text, int index)
        {
            if (index >= text.Length) return -1;
            var c = text[index];
            if (c == '/' && index + 1 < text.Length)
            {
                var next = text[index + 1];
                if (next == '/')
                {
                    var end = text.IndexOf('\n', index);
                    return end < 0 ? text.Length : end + 1;
                }
                if (next == '*')
                {
                    var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    return end < 0 ? text.Length : end + 2;
                }
            }
            if (c == '@')
            {
                var j = index + 1;
                var interpolated = false;
                if (j < text.Length && text[j] == '$') { interpolated = true; j++; }
                if (j < text.Length && text[j] == '"')
                {
                    if (Run(text, j, '"') >= 3) return SkipRawString(text, j, interpolated ? 1 : 0);
                    return SkipVerbatimString(text, j, interpolated);
                }
                return -1;
            }
            if (c == '$')
            {
                var dollars = Run(text, index, '$');
                var j = index + dollars;
                var verbatim = false;
                if (j < text.Length && text[j] == '@') { verbatim = true; j++; }
                if (j >= text.Length || text[j] != '"') return -1;
                if (Run(text, j, '"') >= 3) return SkipRawString(text, j, dollars);
                if (verbatim) return SkipVerbatimString(text, j, true);
                if (dollars == 1) return SkipQuoted(text, j);
                return -1;
            }
            if (c == '"')
            {
                if (Run(text, index, '"') >= 3) return SkipRawString(text, index, 0);
                return SkipQuoted(text, index);
            }
            if (c == '\'') return SkipQuoted(text, index);
            return -1;
        }

        private static int SkipQuoted(string text, int index)
        {
            var quote = text[index];
            var i = index + 1;
            while (i < text.Length)
            {
                if (text[i] == '\\') { i += 2; continue; }
                if (text[i] == quote) return i + 1;
                i++;
            }
            return text.Length;
        }

        // Index is the opening brace of an interpolation hole. Nested code is scanned.
        private static int SkipBraceHole(string text, int index)
        {
            var i = index + 1;
            var depth = 1;
            while (i < text.Length && depth > 0)
            {
                var skip = SkipNonCode(text, i);
                if (skip >= 0) { i = skip; continue; }
                var c = text[i];
                if (c == '{') depth++;
                else if (c == '}') depth--;
                i++;
            }
            return i;
        }

        // C# verbatim: "" is one quote, a backslash is not an escape, @" is not a closer.
        private static int SkipVerbatimString(string text, int quoteAt, bool interpolated)
        {
            var i = quoteAt + 1;
            while (i < text.Length)
            {
                var c = text[i];
                if (interpolated && c == '{')
                {
                    if (i + 1 < text.Length && text[i + 1] == '{') { i += 2; continue; }
                    i = SkipBraceHole(text, i);
                    continue;
                }
                if (interpolated && c == '}' && i + 1 < text.Length && text[i + 1] == '}') { i += 2; continue; }
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { i += 2; continue; }
                    return i + 1;
                }
                i++;
            }
            return text.Length;
        }

        // Index is the first character of the expression, after the opening brace run.
        private static int SkipRawHole(string text, int index, int dollars)
        {
            var i = index;
            var depth = 0;
            while (i < text.Length)
            {
                var skip = SkipNonCode(text, i);
                if (skip >= 0) { i = skip; continue; }
                if (text[i] == '{') { depth++; i++; continue; }
                if (text[i] == '}')
                {
                    if (depth > 0) { depth--; i++; continue; }
                    var run = Run(text, i, '}');
                    if (run >= dollars) return i + dollars;
                    i += run;
                    continue;
                }
                i++;
            }
            return text.Length;
        }

        // A raw literal's closer is the same run of quotes that opened it.
        private static int SkipRawString(string text, int quoteAt, int dollars)
        {
            var quotes = Run(text, quoteAt, '"');
            if (quotes < 3) return -1;
            var i = quoteAt + quotes;
            while (i < text.Length)
            {
                if (dollars > 0 && text[i] == '{')
                {
                    var run = Run(text, i, '{');
                    if (run >= dollars) { i = SkipRawHole(text, i + dollars, dollars); continue; }
                    i += run;
                    continue;
                }
                if (text[i] == '"')
                {
                    var run = Run(text, i, '"');
                    if (run >= quotes) return i + quotes;
                    i += run;
                    continue;
                }
                i++;
            }
            return text.Length;
        }
    }
}
