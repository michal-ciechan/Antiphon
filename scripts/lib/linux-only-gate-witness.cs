using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Body-scoped witness for Windows rc exclusions that are Linux-only by construction.
// Each test is judged on its own brace-matched body. Strings, comments and raw
// literals are not code, so a neighbour test's gate cannot leak into the slice.
// Forms used by the 45 rows:
// 1. Top-level if (!OperatingSystem.IsLinux()) skip/throw, including a block that only skips and returns.
// 2. Top-level if (OperatingSystem.IsWindows()) skip/throw.
// 3. Top-level NativeLinuxGate.Require or RequireNativeLinux, including a cross-file C1008HostFixture call.
// 4. Top-level call whose local body is a gate: RequireLinuxJoin, RequireLinuxCustody, RequireUnix,
//    RunProbeAsync, ExternalBuild, RequireLinuxIntegration, C1105AuditContract.
// 5. [Before(Test)] whose body is a gate, including expression-bodied RequireNativeLinux()
//    and RequirePlatform(OperatingSystem.IsLinux() ? "linux" : "other") when that helper throws
//    SkipTestException unless platform is linux.
// 6. Top-level construction of a fixture whose instance constructor starts with the skip.
// 7. Top-level if (!Linux()) return; when Linux() is if (IsLinux()) return true; then an unconditional skip.
// 8. One straight-line preamble call immediately before form 1 (RequireClient).
// No row uses a class-level skip attribute. An instance constructor of the test class would count;
// none of the 45 have one. A return-true helper is accepted only when every other statement skips
// or returns false. A gate nested under another condition is not a gate.

namespace RcPlatformWitness
{
    public static class LinuxOnlyGate
    {
        static readonly Regex Forbid = new Regex("unverified|junction|case-sensitive|case-twin|opt-in", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex SkipMessage = new Regex("Skip(?:TestException|\\.Test)\\(\\s*(?:@)?\\$?\"([^\"]*)\"", RegexOptions.CultureInvariant);
        static readonly Regex PlatformCall = new Regex("^RequirePlatform\\s*\\(\\s*OperatingSystem\\.IsLinux\\(\\)\\s*\\?\\s*\"linux\"\\s*:\\s*\"other\"\\s*\\)\\s*;?$", RegexOptions.CultureInvariant);
        static readonly Regex PlatformSkip = new Regex("^if\\s*\\(\\s*platform\\s*!=\\s*\"linux\"\\s*\\)\\s*throw\\s+new\\s+(?:[\\w.]+\\.)?SkipTestException\\s*\\(", RegexOptions.CultureInvariant);

        public static List<string> RowProblems(string source, string className, string[] methods)
        {
            try
            {
                var file = new FileModel(source ?? "");
                file.Parse();
                return file.Problems(className ?? "", methods);
            }
            catch (Exception ex)
            {
                var list = new List<string>();
                list.Add("parse-failed " + ex.GetType().Name + " " + ex.Message);
                return list;
            }
        }

        sealed class Tok
        {
            public string T;
            public int S;
            public int E;
            public Tok(string t, int s, int e) { T = t; S = s; E = e; }
        }

        sealed class Attr
        {
            public string Name;
            public string Args;
            public Attr(string name, string args) { Name = name; Args = args; }
        }

        sealed class Method
        {
            public string Class;
            public string Name;
            public bool IsTest;
            public bool IsBefore;
            public bool IsCtor;
            public bool IsStatic;
            public int BodyA;
            public int BodyB;
            public List<Attr> Attrs;
        }

        sealed class FileModel
        {
            readonly string _src;
            readonly List<Tok> _toks = new List<Tok>();
            readonly List<Method> _methods = new List<Method>();
            readonly Stack<string> _classes = new Stack<string>();
            int _p;

            static readonly HashSet<string> Mods = new HashSet<string>(StringComparer.Ordinal)
            {
                "public","private","protected","internal","static","sealed","abstract","override","virtual",
                "async","extern","unsafe","partial","new","required","readonly","volatile","file","ref","event","fixed"
            };

            public FileModel(string source) { _src = source ?? ""; }

            public void Parse()
            {
                Scan();
                while (_p < _toks.Count)
                {
                    var before = _p;
                    Declaration();
                    if (_p == before) _p++;
                }
            }

            public List<string> Problems(string className, string[] methods)
            {
                var problems = new List<string>();
                var want = new List<string>();
                if (methods != null)
                {
                    foreach (var name in methods)
                    {
                        if (!string.IsNullOrWhiteSpace(name)) want.Add(name);
                    }
                }
                var classGated = ClassIsGated(className);
                if (want.Count == 0)
                {
                    var seen = 0;
                    foreach (var method in _methods)
                    {
                        if (!method.IsTest || method.Class != className) continue;
                        seen++;
                        Judge(method, classGated, problems);
                    }
                    if (seen == 0) problems.Add("no-tests");
                    return problems;
                }
                foreach (var name in want)
                {
                    Method found = null;
                    foreach (var method in _methods)
                    {
                        if (method.Class == className && method.Name == name && method.IsTest) { found = method; break; }
                    }
                    if (found == null)
                    {
                        foreach (var method in _methods)
                        {
                            if (method.Class == className && method.Name == name) { found = method; break; }
                        }
                    }
                    if (found == null) problems.Add("missing " + name);
                    else Judge(found, classGated, problems);
                }
                return problems;
            }

            void Judge(Method method, bool classGated, List<string> problems)
            {
                var gated = classGated || MethodIsGated(method);
                if (!gated) problems.Add("ungated " + method.Name);
                if (Forbidden(method)) problems.Add("forbid " + method.Name);
            }

            bool ClassIsGated(string className)
            {
                var ctors = new List<Method>();
                foreach (var method in _methods)
                {
                    if (method.Class != className) continue;
                    if (method.IsBefore && MethodIsGated(method)) return true;
                    if (method.IsCtor && !method.IsStatic) ctors.Add(method);
                }
                if (ctors.Count == 0) return false;
                foreach (var ctor in ctors)
                {
                    if (!MethodIsGated(ctor)) return false;
                }
                return true;
            }

            bool Forbidden(Method method)
            {
                if (method.BodyA >= method.BodyB) return false;
                var raw = Raw(method.BodyA, method.BodyB);
                foreach (Match match in SkipMessage.Matches(raw))
                {
                    if (Forbid.IsMatch(match.Groups[1].Value)) return true;
                }
                return false;
            }

            readonly Stack<Method> _stack = new Stack<Method>();
            readonly Dictionary<Method, bool> _cache = new Dictionary<Method, bool>();

            bool MethodIsGated(Method method)
            {
                if (method == null) return false;
                bool cached;
                if (_cache.TryGetValue(method, out cached)) return cached;
                if (_stack.Contains(method)) return false;
                _stack.Push(method);
                var gated = Compute(method);
                _stack.Pop();
                _cache[method] = gated;
                return gated;
            }

            bool Compute(Method method)
            {
                var statements = Statements(method.BodyA, method.BodyB);
                if (statements.Count == 0) return false;
                if (StatementIsGate(statements[0], method.Class)) return true;
                if (statements.Count >= 2 && IsPreamble(statements[0], method.Class) && StatementIsGate(statements[1], method.Class)) return true;
                return false;
            }

            bool StatementIsGate(int[] span, string caller)
            {
                if (IsOsSkipIf(span)) return true;
                if (IsHelperReturnIf(span, caller)) return true;
                if (!StartsWithControl(span)) return HasGatedCall(span, caller);
                return false;
            }

            bool IsPreamble(int[] span, string caller)
            {
                if (StartsWithControl(span)) return false;
                string name;
                if (!SoleLocalCall(span, out name)) return false;
                var hits = Resolve("", name, caller);
                if (hits.Count != 1) return false;
                return StraightLine(hits[0]);
            }

            bool StraightLine(Method method)
            {
                var statements = Statements(method.BodyA, method.BodyB);
                foreach (var span in statements)
                {
                    if (StartsWithControl(span)) return false;
                }
                return statements.Count > 0;
            }

            bool SoleLocalCall(int[] span, out string name)
            {
                name = "";
                string found = "";
                var calls = 0;
                WalkCalls(span, delegate (string qualifier, string called, int index, bool isNew)
                {
                    if (isNew || qualifier.Length > 0) calls += 2;
                    else { calls++; found = called; }
                });
                if (calls != 1) return false;
                name = found;
                return true;
            }

            bool IsOsSkipIf(int[] span)
            {
                int open;
                if (!IfCondition(span, out open)) return false;
                int close = SkipParens(open, span[1]);
                if (close > span[1]) return false;
                if (!ExactOsCondition(open + 1, close - 1)) return false;
                return SkipOnly(close, span[1]);
            }

            bool ExactOsCondition(int a, int b)
            {
                return Seq(a, b, "!", "OperatingSystem", ".", "IsLinux", "(", ")")
                    || Seq(a, b, "OperatingSystem", ".", "IsWindows", "(", ")");
            }

            bool IsHelperReturnIf(int[] span, string caller)
            {
                int open;
                if (!IfCondition(span, out open)) return false;
                int close = SkipParens(open, span[1]);
                if (close != open + 6 || close > span[1]) return false;
                if (_toks[open + 1].T != "!" || !IsIdent(_toks[open + 2].T) || _toks[open + 3].T != "(" || _toks[open + 4].T != ")") return false;
                if (!Seq(close, span[1], "return", ";")) return false;
                var helper = _toks[open + 2].T;
                var hits = Resolve("", helper, caller);
                if (hits.Count != 1) return false;
                return ReturnTrueShape(hits[0]);
            }

            bool ReturnTrueShape(Method method)
            {
                var statements = Statements(method.BodyA, method.BodyB);
                if (statements.Count < 2) return false;
                if (!IsLinuxReturnTrue(statements[0])) return false;
                var skipped = false;
                for (var n = 1; n < statements.Count; n++)
                {
                    if (IsSkipStatement(statements[n][0], statements[n][1])) { skipped = true; continue; }
                    if (Seq(statements[n][0], statements[n][1], "return", "false", ";")) { skipped = true; continue; }
                    return false;
                }
                return skipped;
            }

            bool IsLinuxReturnTrue(int[] span)
            {
                int open;
                if (!IfCondition(span, out open)) return false;
                int close = SkipParens(open, span[1]);
                if (!Seq(open + 1, close - 1, "OperatingSystem", ".", "IsLinux", "(", ")")) return false;
                if (Seq(close, span[1], "return", "true", ";")) return true;
                return Seq(close, span[1], "{", "return", "true", ";", "}");
            }

            bool HasGatedCall(int[] span, string caller)
            {
                var gated = false;
                WalkCalls(span, delegate (string qualifier, string called, int index, bool isNew)
                {
                    if (gated) return;
                    if (isNew)
                    {
                        if (AllCtorsGated(called)) gated = true;
                        return;
                    }
                    if (called == "RequirePlatform" && qualifier.Length == 0 && PlatformTernary(index, span[1]) && PlatformHelper(caller))
                    {
                        gated = true;
                        return;
                    }
                    var hits = Resolve(qualifier, called, caller);
                    if (hits.Count > 0)
                    {
                        if (AllGated(hits)) gated = true;
                        return;
                    }
                    if (KnownExternal(qualifier, called)) gated = true;
                });
                return gated;
            }

            bool AllGated(List<Method> hits)
            {
                if (hits.Count == 0) return false;
                foreach (var hit in hits)
                {
                    if (!MethodIsGated(hit)) return false;
                }
                return true;
            }

            bool AllCtorsGated(string typeName)
            {
                var hits = new List<Method>();
                foreach (var method in _methods)
                {
                    if (method.IsCtor && !method.IsStatic && method.Class == typeName && method.Name == typeName) hits.Add(method);
                }
                return AllGated(hits);
            }

            bool PlatformTernary(int nameIndex, int limit)
            {
                if (nameIndex + 1 >= limit || _toks[nameIndex + 1].T != "(") return false;
                var after = SkipParens(nameIndex + 1, limit);
                var raw = Raw(nameIndex, after);
                return PlatformCall.IsMatch(raw);
            }

            bool PlatformHelper(string caller)
            {
                var hits = Resolve("", "RequirePlatform", caller);
                if (hits.Count != 1) return false;
                var statements = Statements(hits[0].BodyA, hits[0].BodyB);
                if (statements.Count == 0) return false;
                return PlatformSkip.IsMatch(Raw(statements[0][0], statements[0][1]));
            }

            static bool KnownExternal(string qualifier, string name)
            {
                if (name == "Require" && qualifier == "NativeLinuxGate") return true;
                if (name == "RequireNativeLinux") return true;
                if (name == "RequireUnix" || name == "RequireLinux" || name == "RequireLinuxJoin" || name == "RequireLinuxCustody") return true;
                return false;
            }

            List<Method> Resolve(string qualifier, string name, string caller)
            {
                var hits = new List<Method>();
                if (qualifier.Length > 0)
                {
                    foreach (var method in _methods)
                    {
                        if (method.Name == name && method.Class == qualifier) hits.Add(method);
                    }
                    return hits;
                }
                foreach (var method in _methods)
                {
                    if (method.Name == name && method.Class == caller) hits.Add(method);
                }
                if (hits.Count > 0) return hits;
                foreach (var method in _methods)
                {
                    if (method.Name == name) hits.Add(method);
                }
                return hits;
            }

            delegate void CallSink(string qualifier, string called, int index, bool isNew);

            void WalkCalls(int[] span, CallSink sink)
            {
                var brace = 0;
                var a = span[0];
                var b = span[1];
                for (var i = a; i < b; i++)
                {
                    var t = _toks[i].T;
                    if (t == "{") { brace++; continue; }
                    if (t == "}") { if (brace > 0) brace--; continue; }
                    if (brace != 0) continue;
                    var isNew = i > a && _toks[i - 1].T == "new";
                    if (!IsIdent(t)) continue;
                    var name = t;
                    var qualifier = "";
                    var j = i;
                    while (j + 2 < b && _toks[j + 1].T == "." && IsIdent(_toks[j + 2].T))
                    {
                        qualifier = _toks[j].T;
                        j += 2;
                        name = _toks[j].T;
                    }
                    var after = j + 1;
                    if (after < b && _toks[after].T == "<") after = SkipAngles(after, b);
                    if (after >= b || _toks[after].T != "(") continue;
                    sink(isNew ? "" : qualifier, isNew ? t : name, isNew ? i : j, isNew);
                    i = j;
                }
            }

            bool StartsWithControl(int[] span)
            {
                if (span[0] >= span[1]) return false;
                var t = _toks[span[0]].T;
                if (t == "await" && span[0] + 1 < span[1] && _toks[span[0] + 1].T == "using" && (span[0] + 2 >= span[1] || _toks[span[0] + 2].T != "(")) return false;
                if (t == "using" && (span[0] + 1 >= span[1] || _toks[span[0] + 1].T != "(")) return false;
                return t == "if" || t == "while" || t == "for" || t == "foreach" || t == "lock" || t == "switch"
                    || t == "try" || t == "do" || t == "using" || t == "fixed" || t == "checked" || t == "unchecked" || t == "unsafe";
            }

            bool IfCondition(int[] span, out int open)
            {
                open = -1;
                if (span[0] >= span[1] || _toks[span[0]].T != "if") return false;
                if (span[0] + 1 >= span[1] || _toks[span[0] + 1].T != "(") return false;
                open = span[0] + 1;
                var elseAt = ElseIndex(span);
                return elseAt < 0;
            }

            int ElseIndex(int[] span)
            {
                var brace = 0;
                var paren = 0;
                for (var i = span[0]; i < span[1]; i++)
                {
                    var t = _toks[i].T;
                    if (t == "{") brace++;
                    else if (t == "}") { if (brace > 0) brace--; }
                    else if (t == "(") paren++;
                    else if (t == ")") { if (paren > 0) paren--; }
                    else if (t == "else" && brace == 0 && paren == 0) return i;
                }
                return -1;
            }

            bool SkipOnly(int a, int b)
            {
                if (a >= b) return false;
                if (_toks[a].T == "{")
                {
                    var innerA = a + 1;
                    var innerB = b;
                    if (innerB > innerA && _toks[innerB - 1].T == "}") innerB--;
                    var statements = Statements(innerA, innerB);
                    if (statements.Count == 0) return false;
                    var skipped = false;
                    foreach (var span in statements)
                    {
                        if (IsSkipStatement(span[0], span[1]) || Seq(span[0], span[1], "return", ";") || Seq(span[0], span[1], "return", "false", ";"))
                        {
                            if (IsSkipStatement(span[0], span[1])) skipped = true;
                            continue;
                        }
                        return false;
                    }
                    return skipped;
                }
                return IsSkipStatement(a, b);
            }

            bool IsSkipStatement(int a, int b)
            {
                if (b - a >= 4 && _toks[a].T == "Skip" && _toks[a + 1].T == "." && _toks[a + 2].T == "Test" && _toks[a + 3].T == "(") return true;
                if (a >= b || _toks[a].T != "throw" || a + 1 >= b || _toks[a + 1].T != "new") return false;
                var i = a + 2;
                var last = "";
                while (i < b && (IsIdent(_toks[i].T) || _toks[i].T == "."))
                {
                    if (IsIdent(_toks[i].T)) last = _toks[i].T;
                    i++;
                }
                return last == "SkipTestException" && i < b && _toks[i].T == "(";
            }

            bool Seq(int a, int b, params string[] expect)
            {
                if (b - a != expect.Length) return false;
                for (var n = 0; n < expect.Length; n++)
                {
                    if (_toks[a + n].T != expect[n]) return false;
                }
                return true;
            }

            List<int[]> Statements(int a, int b)
            {
                var list = new List<int[]>();
                var i = a;
                while (i < b)
                {
                    var start = i;
                    i = ConsumeStatement(i, b);
                    if (i <= start) i = start + 1;
                    list.Add(new[] { start, i });
                }
                return list;
            }

            int ConsumeStatement(int i, int limit)
            {
                if (i >= limit) return i;
                var t = _toks[i].T;
                if (t == "using" && (i + 1 >= limit || _toks[i + 1].T != "(")) return ConsumeSemi(i, limit);
                if (t == "await" && i + 1 < limit && _toks[i + 1].T == "using" && (i + 2 >= limit || _toks[i + 2].T != "(")) return ConsumeSemi(i, limit);
                if (IsControl(t)) return ConsumeControl(i, limit);
                return ConsumeSemi(i, limit);
            }

            static bool IsControl(string t)
            {
                return t == "if" || t == "while" || t == "for" || t == "foreach" || t == "lock" || t == "switch"
                    || t == "try" || t == "do" || t == "using" || t == "fixed" || t == "checked" || t == "unchecked" || t == "unsafe";
            }

            int ConsumeControl(int i, int limit)
            {
                var kw = _toks[i].T;
                i++;
                if (kw == "do")
                {
                    i = ConsumeEmbedded(i, limit);
                    if (i < limit && _toks[i].T == "while")
                    {
                        i++;
                        if (i < limit && _toks[i].T == "(") i = SkipParens(i, limit);
                        if (i < limit && _toks[i].T == ";") i++;
                    }
                    return i;
                }
                if (kw == "try")
                {
                    i = ConsumeEmbedded(i, limit);
                    while (i < limit && (_toks[i].T == "catch" || _toks[i].T == "finally"))
                    {
                        var kind = _toks[i].T;
                        i++;
                        if (kind == "catch" && i < limit && _toks[i].T == "(") i = SkipParens(i, limit);
                        if (kind == "catch" && i < limit && _toks[i].T == "when")
                        {
                            i++;
                            if (i < limit && _toks[i].T == "(") i = SkipParens(i, limit);
                        }
                        i = ConsumeEmbedded(i, limit);
                    }
                    return i;
                }
                if (i < limit && _toks[i].T == "(") i = SkipParens(i, limit);
                i = ConsumeEmbedded(i, limit);
                if (kw == "if")
                {
                    while (i < limit && _toks[i].T == "else")
                    {
                        i++;
                        if (i < limit && _toks[i].T == "if")
                        {
                            i++;
                            if (i < limit && _toks[i].T == "(") i = SkipParens(i, limit);
                        }
                        i = ConsumeEmbedded(i, limit);
                    }
                }
                return i;
            }

            int ConsumeEmbedded(int i, int limit)
            {
                if (i >= limit) return i;
                if (_toks[i].T == "{") return SkipBraces(i, limit);
                return ConsumeStatement(i, limit);
            }

            int ConsumeSemi(int i, int limit)
            {
                var paren = 0;
                var brace = 0;
                var bracket = 0;
                while (i < limit)
                {
                    var t = _toks[i].T;
                    if (t == "(") paren++;
                    else if (t == ")") { if (paren > 0) paren--; }
                    else if (t == "{") brace++;
                    else if (t == "}")
                    {
                        if (brace > 0) brace--;
                        else return i;
                    }
                    else if (t == "[") bracket++;
                    else if (t == "]") { if (bracket > 0) bracket--; }
                    else if (t == ";" && paren == 0 && brace == 0 && bracket == 0) return i + 1;
                    i++;
                }
                return i;
            }

            int SkipBraces(int i, int limit)
            {
                var depth = 0;
                while (i < limit)
                {
                    var t = _toks[i].T;
                    if (t == "{") depth++;
                    else if (t == "}")
                    {
                        depth--;
                        i++;
                        if (depth == 0) return i;
                        continue;
                    }
                    i++;
                }
                return i;
            }

            int SkipParens(int i, int limit)
            {
                var depth = 0;
                while (i < limit)
                {
                    var t = _toks[i].T;
                    if (t == "(") depth++;
                    else if (t == ")")
                    {
                        depth--;
                        i++;
                        if (depth == 0) return i;
                        continue;
                    }
                    i++;
                }
                return i;
            }

            int SkipAngles(int i, int limit)
            {
                var depth = 0;
                while (i < limit)
                {
                    var t = _toks[i].T;
                    if (t == "<") depth++;
                    else if (t == ">")
                    {
                        depth--;
                        i++;
                        if (depth == 0) return i;
                        continue;
                    }
                    else if (depth == 0) return i;
                    i++;
                }
                return i;
            }

            string Raw(int a, int b)
            {
                if (a >= b || a < 0 || b > _toks.Count) return "";
                var start = _toks[a].S;
                var end = _toks[b - 1].E;
                if (end < start || start < 0 || end > _src.Length) return "";
                return _src.Substring(start, end - start);
            }

            void Declaration()
            {
                var attrs = Attributes();
                var mods = ModifiersOf();
                var kw = Tok();
                if (kw == "namespace") { Namespace(); return; }
                if (kw == "class" || kw == "struct" || kw == "interface" || kw == "enum" || kw == "record") { TypeDecl(); return; }
                if (kw == "using" || kw == "extern" || kw == "delegate") { _p = ConsumeSemi(_p, _toks.Count); return; }
                if (kw == "global" && Tok(1) == "using") { _p = ConsumeSemi(_p, _toks.Count); return; }
                if (kw == "}") return;
                if (_classes.Count > 0) { Member(attrs, mods); return; }
                if (kw == ";") { _p++; return; }
                _p++;
            }

            void Namespace()
            {
                _p++;
                while (_p < _toks.Count && Tok() != ";" && Tok() != "{") _p++;
                if (Tok() == ";") { _p++; return; }
                if (Tok() == "{")
                {
                    _p++;
                    while (_p < _toks.Count && Tok() != "}") Declaration();
                    if (Tok() == "}") _p++;
                }
            }

            void TypeDecl()
            {
                var kind = Take();
                if (kind == "record" && (Tok() == "class" || Tok() == "struct")) Take();
                var typeName = Dotted();
                if (Tok() == "<") _p = SkipAngles(_p, _toks.Count);
                if (Tok() == "(") _p = SkipParens(_p, _toks.Count);
                while (Tok() != "{" && Tok() != ";" && Tok() != "" && Tok() != "}")
                {
                    if (Tok() == "(") { _p = SkipParens(_p, _toks.Count); continue; }
                    if (Tok() == "<") { _p = SkipAngles(_p, _toks.Count); continue; }
                    if (Tok() == "[") { _p = SkipBracesLike(_p, "[", "]"); continue; }
                    _p++;
                }
                if (Tok() == ";") { _p++; return; }
                if (Tok() != "{") return;
                _p++;
                _classes.Push(typeName);
                while (_p < _toks.Count && Tok() != "}")
                {
                    var before = _p;
                    Declaration();
                    if (_p == before) _p++;
                }
                if (Tok() == "}") _p++;
                if (_classes.Count > 0) _classes.Pop();
            }

            void Member(List<Attr> attrs, List<string> mods)
            {
                var angle = 0;
                var paren = 0;
                var bracket = 0;
                var lastIdent = "";
                var sawParen = false;
                var method = false;
                while (_p < _toks.Count)
                {
                    var t = Tok();
                    if (t == "<" && paren == 0) { angle++; _p++; continue; }
                    if (t == ">" && angle > 0 && paren == 0) { angle--; _p++; continue; }
                    if (t == "[" && angle == 0 && paren == 0) { bracket++; _p++; continue; }
                    if (t == "]" && bracket > 0 && paren == 0) { bracket--; _p++; continue; }
                    if (t == "(" && angle == 0 && bracket == 0) { paren++; _p++; continue; }
                    if (t == ")" && paren > 0)
                    {
                        paren--;
                        _p++;
                        if (paren == 0 && angle == 0 && bracket == 0) sawParen = true;
                        continue;
                    }
                    if (angle == 0 && paren == 0 && bracket == 0 && (t == "{" || t == "=" || t == ";" || t == "}")) break;
                    if (angle == 0 && paren == 0 && bracket == 0 && IsIdent(t))
                    {
                        if (sawParen) { lastIdent = t; method = true; sawParen = false; }
                        else lastIdent = t;
                    }
                    _p++;
                }
                if (sawParen) method = true;
                if (!method || lastIdent.Length == 0 || _classes.Count == 0)
                {
                    FinishNonMethod();
                    return;
                }
                var cls = _classes.Peek();
                var bodyA = 0;
                var bodyB = 0;
                var hasBody = false;
                if (Tok() == "{")
                {
                    var open = _p;
                    _p++;
                    bodyA = _p;
                    _p = SkipBraces(open, _toks.Count);
                    bodyB = _p - 1;
                    if (bodyB < bodyA) bodyB = bodyA;
                    hasBody = true;
                }
                else if (Tok() == "=" && Tok(1) == ">")
                {
                    _p += 2;
                    bodyA = _p;
                    _p = ConsumeSemi(_p, _toks.Count);
                    bodyB = _p;
                    hasBody = true;
                }
                else FinishNonMethod();
                if (!hasBody) return;
                var info = new Method();
                info.Class = cls;
                info.Name = lastIdent;
                info.Attrs = attrs;
                info.BodyA = bodyA;
                info.BodyB = bodyB;
                info.IsStatic = mods.Contains("static");
                info.IsCtor = lastIdent == cls;
                info.IsTest = Has(attrs, "Test") || Has(attrs, "TestAttribute");
                info.IsBefore = BeforeKind(attrs);
                _methods.Add(info);
            }

            void FinishNonMethod()
            {
                if (Tok() == "{") _p = SkipBraces(_p, _toks.Count);
                else if (Tok() == "=") _p = ConsumeSemi(_p, _toks.Count);
                else if (Tok() == ";") _p++;
            }

            static bool BeforeKind(List<Attr> attrs)
            {
                foreach (var attr in attrs)
                {
                    if (attr.Name != "Before" && attr.Name != "BeforeAttribute") continue;
                    if (attr.Args == "Test" || attr.Args == "Class") return true;
                }
                return false;
            }

            static bool Has(List<Attr> attrs, string name)
            {
                foreach (var attr in attrs)
                {
                    if (attr.Name == name) return true;
                }
                return false;
            }

            List<Attr> Attributes()
            {
                var attrs = new List<Attr>();
                while (Tok() == "[")
                {
                    foreach (var attr in AttributeList()) attrs.Add(attr);
                }
                return attrs;
            }

            List<Attr> AttributeList()
            {
                var attrs = new List<Attr>();
                if (Tok() != "[") return attrs;
                _p++;
                while (Tok() != "]" && Tok() != "")
                {
                    var target = Tok();
                    if (IsIdent(target) && Tok(1) == ":" && Tok(2) != ":" &&
                        (target == "assembly" || target == "module" || target == "return" || target == "param" ||
                         target == "field" || target == "method" || target == "type" || target == "property" || target == "event"))
                    {
                        _p += 2;
                    }
                    var name = Dotted();
                    if (Tok() == "<") _p = SkipAngles(_p, _toks.Count);
                    var args = "";
                    if (Tok() == "(")
                    {
                        var open = _p;
                        _p = SkipParens(_p, _toks.Count);
                        args = ArgText(open + 1, _p - 1);
                    }
                    if (name.Length > 0) attrs.Add(new Attr(name, args));
                    if (Tok() == ",") { _p++; continue; }
                    break;
                }
                if (Tok() == "]") _p++;
                return attrs;
            }

            string ArgText(int a, int b)
            {
                var text = "";
                for (var i = a; i < b; i++)
                {
                    if (!IsIdent(_toks[i].T)) continue;
                    if (text.Length > 0) text += " ";
                    text += _toks[i].T;
                }
                return text;
            }

            List<string> ModifiersOf()
            {
                var mods = new List<string>();
                while (Mods.Contains(Tok())) mods.Add(Take());
                return mods;
            }

            string Dotted()
            {
                if (!IsIdent(Tok())) return "";
                var name = Take();
                while (true)
                {
                    if (Tok() == "." && IsIdent(Tok(1))) { _p++; name = Take(); continue; }
                    if (Tok() == ":" && Tok(1) == ":" && IsIdent(Tok(2))) { _p += 2; name = Take(); continue; }
                    break;
                }
                return name;
            }

            int SkipBracesLike(int i, string open, string close)
            {
                var depth = 0;
                while (i < _toks.Count)
                {
                    if (_toks[i].T == open) depth++;
                    else if (_toks[i].T == close)
                    {
                        depth--;
                        i++;
                        if (depth == 0) return i;
                        continue;
                    }
                    i++;
                }
                return i;
            }

            string Tok(int k = 0)
            {
                var j = _p + k;
                return j >= 0 && j < _toks.Count ? _toks[j].T : "";
            }

            string Take()
            {
                var t = Tok();
                if (_p < _toks.Count) _p++;
                return t;
            }

            static bool IsIdent(string t)
            {
                return t.Length > 0 && (char.IsLetter(t[0]) || t[0] == '_');
            }

            void Scan()
            {
                var n = _src.Length;
                var i = 0;
                if (n > 0 && _src[0] == '\uFEFF') i = 1;
                var lineStart = true;
                while (i < n)
                {
                    var c = _src[i];
                    if (c == '\n') { lineStart = true; i++; continue; }
                    if (char.IsWhiteSpace(c)) { i++; continue; }
                    if (lineStart && c == '#')
                    {
                        while (i < n && _src[i] != '\n') i++;
                        continue;
                    }
                    lineStart = false;
                    if (c == '/' && i + 1 < n && _src[i + 1] == '/')
                    {
                        while (i < n && _src[i] != '\n') i++;
                        continue;
                    }
                    if (c == '/' && i + 1 < n && _src[i + 1] == '*')
                    {
                        i += 2;
                        while (i + 1 < n && !(_src[i] == '*' && _src[i + 1] == '/')) i++;
                        if (i + 1 < n) i += 2;
                        else i = n;
                        continue;
                    }
                    if (TryString(ref i)) continue;
                    if (c == '\'')
                    {
                        i++;
                        if (i < n && _src[i] == '\\') i += 2;
                        else if (i < n) i++;
                        if (i < n && _src[i] == '\'') i++;
                        continue;
                    }
                    if (char.IsLetter(c) || c == '_' || (c == '@' && i + 1 < n && char.IsLetter(_src[i + 1])))
                    {
                        var start = i;
                        if (c == '@') i++;
                        while (i < n && (char.IsLetterOrDigit(_src[i]) || _src[i] == '_')) i++;
                        var ident = _src.Substring(c == '@' ? start + 1 : start, i - (c == '@' ? start + 1 : start));
                        _toks.Add(new Tok(ident, start, i));
                        continue;
                    }
                    _toks.Add(new Tok(c.ToString(), i, i + 1));
                    i++;
                }
            }

            bool TryString(ref int i)
            {
                var n = _src.Length;
                if (i + 2 < n && _src[i] == '@' && _src[i + 1] == '$' && _src[i + 2] == '"') { i += 3; Quoted(ref i, true, true); return true; }
                if (i + 2 < n && _src[i] == '$' && _src[i + 1] == '@' && _src[i + 2] == '"') { i += 3; Quoted(ref i, true, true); return true; }
                if (i + 1 < n && _src[i] == '@' && _src[i + 1] == '"') { i += 2; Quoted(ref i, true, false); return true; }
                if (i < n && _src[i] == '$')
                {
                    var d = 0;
                    while (i + d < n && _src[i + d] == '$') d++;
                    if (i + d >= n || _src[i + d] != '"') return false;
                    var q = 0;
                    while (i + d + q < n && _src[i + d + q] == '"') q++;
                    if (q >= 3) { i += d + q; RawString(ref i, q, d); return true; }
                    if (q == 1) { i += d + 1; Quoted(ref i, false, true); return true; }
                    return false;
                }
                if (i >= n || _src[i] != '"') return false;
                var quotes = 0;
                while (i + quotes < n && _src[i + quotes] == '"') quotes++;
                if (quotes >= 3) { i += quotes; RawString(ref i, quotes, 0); return true; }
                if (quotes == 2) { i += 2; return true; }
                i++;
                Quoted(ref i, false, false);
                return true;
            }

            void Quoted(ref int i, bool verbatim, bool interpolated)
            {
                var n = _src.Length;
                while (i < n)
                {
                    var c = _src[i];
                    if (interpolated && c == '{')
                    {
                        if (i + 1 < n && _src[i + 1] == '{') { i += 2; continue; }
                        i++;
                        Hole(ref i, 1);
                        continue;
                    }
                    if (interpolated && c == '}' && i + 1 < n && _src[i + 1] == '}') { i += 2; continue; }
                    if (!verbatim && c == '\\') { i = Math.Min(n, i + 2); continue; }
                    if (c == '"')
                    {
                        if (verbatim && i + 1 < n && _src[i + 1] == '"') { i += 2; continue; }
                        i++;
                        return;
                    }
                    i++;
                }
            }

            void RawString(ref int i, int quotes, int dollars)
            {
                var n = _src.Length;
                while (i < n)
                {
                    if (dollars > 0 && _src[i] == '{')
                    {
                        var run = 0;
                        while (i + run < n && _src[i + run] == '{') run++;
                        if (run >= dollars) { i += dollars; Hole(ref i, dollars); continue; }
                    }
                    if (_src[i] == '"')
                    {
                        var run = 0;
                        while (i + run < n && _src[i + run] == '"') run++;
                        if (run >= quotes) { i += quotes; return; }
                    }
                    i++;
                }
            }

            void Hole(ref int i, int closer)
            {
                var n = _src.Length;
                var brace = 0;
                var paren = 0;
                var bracket = 0;
                while (i < n)
                {
                    if (TryString(ref i)) continue;
                    var c = _src[i];
                    if (c == '/' && i + 1 < n && _src[i + 1] == '/') { while (i < n && _src[i] != '\n') i++; continue; }
                    if (c == '/' && i + 1 < n && _src[i + 1] == '*')
                    {
                        i += 2;
                        while (i + 1 < n && !(_src[i] == '*' && _src[i + 1] == '/')) i++;
                        if (i + 1 < n) i += 2;
                        continue;
                    }
                    if (c == '\'')
                    {
                        i++;
                        if (i < n && _src[i] == '\\') i += 2;
                        else if (i < n) i++;
                        if (i < n && _src[i] == '\'') i++;
                        continue;
                    }
                    if (c == '{') { brace++; i++; continue; }
                    if (c == '(') { paren++; i++; continue; }
                    if (c == '[') { bracket++; i++; continue; }
                    if (c == ')') { if (paren > 0) paren--; i++; continue; }
                    if (c == ']') { if (bracket > 0) bracket--; i++; continue; }
                    if (c == '}')
                    {
                        if (brace > 0) { brace--; i++; continue; }
                        for (var k = 0; k < closer; k++)
                        {
                            if (i >= n || _src[i] != '}') return;
                            i++;
                        }
                        return;
                    }
                    i++;
                }
            }
        }
    }
}
