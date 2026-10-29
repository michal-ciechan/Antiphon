using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Body-scoped witness for Windows rc exclusions that are Linux-only by construction.
// Each test is judged on its own brace-matched body. Strings, comments and raw
// literals are not code, so a neighbour test's gate cannot leak into the slice.
// Closed list. A test is gated only when one of these is true. Anything else is refused.
// 1. The first statement is if (!OperatingSystem.IsLinux()) skip/throw, or a block that only skips and returns.
// 2. The first statement is if (OperatingSystem.IsWindows()) skip/throw, same block rule.
// 3. The first statement is exactly Gate(...); or await Gate(...); and every resolved body is itself gated.
//    Arguments are identifiers, dots, commas and numbers. No lambda, ternary, &&, ||, delegate or new.
// 4. The same call shape, one level in: RequireLinuxJoin, RequireLinuxCustody, RequireUnix, RunProbeAsync,
//    ExternalBuild, RequireLinuxIntegration, C1105AuditContract, RequireNativeLinux. The callee is read.
//    RequirePlatform counts only as RequirePlatform(OperatingSystem.IsLinux() ? "linux" : "other").
// 5. A [Before(Test)] or [Before(Class)] on that class whose body is one of these forms.
// 6. The first statement is using var x = new Fixture(...); and every instance constructor of Fixture is gated.
// 7. The first statement is if (!Linux()) return; and Linux() is if (IsLinux()) return true; then a skip.
// 8. The first statement is exactly RequireClient(); (the two typeof GetMethod asserts) and the second is form 1.
//    One spelled nest is also accepted: using var x = JsonDocument.Parse(await RunProbeAsync(...));
//    and var (a, b, c) = ExternalBuild(...);. An unresolved helper is not a gate. #if/#else refuses.
//    A char or string the lexer cannot read, or an unbalanced body, refuses the file.

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
            var sources = new string[] { source ?? "" };
            return RowProblemsMany(sources, className, methods);
        }

        public static List<string> RowProblemsMany(string[] sources, string className, string[] methods)
        {
            try
            {
                var files = ParseAll(sources);
                return FileModel.Judge(files, className ?? "", methods);
            }
            catch (Exception ex)
            {
                var list = new List<string>();
                list.Add("parse-failed " + ex.GetType().Name + " " + ex.Message);
                return list;
            }
        }

        static List<FileModel> _cache;
        static string _cacheKey;

        public static List<string> CachedRowProblems(string cacheKey, string[] sources, string className, string[] methods)
        {
            try
            {
                var key = cacheKey ?? "";
                if (_cache == null || _cacheKey != key)
                {
                    _cacheKey = key;
                    _cache = ParseAll(sources);
                }
                return FileModel.Judge(_cache, className ?? "", methods);
            }
            catch (Exception ex)
            {
                var list = new List<string>();
                list.Add("parse-failed " + ex.GetType().Name + " " + ex.Message);
                return list;
            }
        }

        static List<FileModel> ParseAll(string[] sources)
        {
            var files = new List<FileModel>();
            if (sources == null) return files;
            foreach (var source in sources)
            {
                var file = new FileModel(source ?? "");
                file.Parse();
                files.Add(file);
            }
            return files;
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
            public string Namespace;
            public string Class;
            public string Name;
            public bool IsTest;
            public bool IsBefore;
            public bool IsCtor;
            public bool IsStatic;
            public int BodyA;
            public int BodyB;
            public int SrcA;
            public int SrcB;
            public bool Preprocessor;
            public FileModel Owner;
            public List<Attr> Attrs;
        }

        sealed class ClassFact
        {
            public string Namespace;
            public string Name;
            public string BaseName;
            public bool Inherits;
        }

        sealed class FileModel
        {
            readonly string _src;
            readonly List<Tok> _toks = new List<Tok>();
            readonly List<Method> _methods = new List<Method>();
            readonly List<ClassFact> _facts = new List<ClassFact>();
            readonly List<int[]> _conds = new List<int[]>();
            readonly List<string> _condKind = new List<string>();
            readonly Stack<int> _condOpen = new Stack<int>();
            readonly Stack<string> _classes = new Stack<string>();
            string _namespace = "";
            bool _misparse;
            string _misparseDetail = "";
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

            sealed class Group
            {
                public string Namespace;
                public string Name;
                public bool Inherits;
                public readonly List<string> Bases = new List<string>();
                public readonly List<Method> Tests = new List<Method>();
                public readonly List<Method> Befores = new List<Method>();
                public readonly List<Method> Ctors = new List<Method>();
            }

            sealed class GateContext
            {
                public List<FileModel> Files;
                public readonly Dictionary<Method, bool> Cache = new Dictionary<Method, bool>();
                public readonly Stack<Method> Stack = new Stack<Method>();

                public List<Method> Resolve(Method caller, string qualifier, string name)
                {
                    var hits = new List<Method>();
                    if (caller == null || Files == null) return hits;
                    foreach (var file in Files)
                    {
                        foreach (var method in file._methods)
                        {
                            if (method.Name != name) continue;
                            if (qualifier.Length > 0)
                            {
                                if (method.Class == qualifier) hits.Add(method);
                            }
                            else if (method.Class == caller.Class && method.Namespace == caller.Namespace) hits.Add(method);
                        }
                    }
                    return hits;
                }

                public List<Method> Ctors(string typeName)
                {
                    var hits = new List<Method>();
                    if (Files == null) return hits;
                    foreach (var file in Files)
                    {
                        foreach (var method in file._methods)
                        {
                            if (method.IsCtor && !method.IsStatic && method.Class == typeName) hits.Add(method);
                        }
                    }
                    return hits;
                }
            }

            public static List<string> Judge(List<FileModel> files, string className, string[] methods)
            {
                var problems = new List<string>();
                if (files == null) files = new List<FileModel>();
                for (var n = 0; n < files.Count; n++)
                {
                    if (!files[n]._misparse) continue;
                    problems.Add("misparse " + n + " " + files[n]._misparseDetail);
                    return problems;
                }
                var ctx = new GateContext();
                ctx.Files = files;
                var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
                foreach (var file in files)
                {
                    foreach (var fact in file._facts)
                    {
                        var group = TakeGroup(groups, fact.Namespace, fact.Name);
                        if (fact.Inherits) group.Inherits = true;
                        if (fact.BaseName != null && fact.BaseName.Length > 0 && !group.Bases.Contains(fact.BaseName)) group.Bases.Add(fact.BaseName);
                    }
                    foreach (var method in file._methods)
                    {
                        var group = TakeGroup(groups, method.Namespace, method.Class);
                        if (method.IsTest) group.Tests.Add(method);
                        if (method.IsBefore) group.Befores.Add(method);
                        if (method.IsCtor && !method.IsStatic) group.Ctors.Add(method);
                    }
                }
                var selected = new List<Group>();
                foreach (var group in groups.Values)
                {
                    if (Same(group.Name, className)) selected.Add(group);
                }
                selected.Sort(delegate (Group left, Group right)
                {
                    var ns = string.CompareOrdinal(left.Namespace, right.Namespace);
                    if (ns != 0) return ns;
                    return string.CompareOrdinal(left.Name, right.Name);
                });
                var want = new List<string>();
                if (methods != null)
                {
                    foreach (var name in methods)
                    {
                        if (!string.IsNullOrWhiteSpace(name)) want.Add(name);
                    }
                }
                if (want.Count == 0)
                {
                    var seen = 0;
                    foreach (var group in selected)
                    {
                        var tests = Expand(group, groups);
                        seen += tests.Count;
                        Note(tests, groups, ctx, problems);
                    }
                    if (seen == 0) problems.Add("no-tests");
                    return problems;
                }
                foreach (var name in want)
                {
                    var found = new List<Method>();
                    foreach (var group in selected)
                    {
                        foreach (var test in Expand(group, groups))
                        {
                            if (Same(test.Name, name)) found.Add(test);
                        }
                    }
                    if (found.Count == 0) problems.Add("missing " + name);
                    else Note(found, groups, ctx, problems);
                }
                return problems;
            }

            static bool Same(string left, string right)
            {
                return string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);
            }

            static string Key(string ns, string name)
            {
                return (ns ?? "") + "\n" + (name ?? "");
            }

            static Group TakeGroup(Dictionary<string, Group> groups, string ns, string name)
            {
                var key = Key(ns, name);
                Group group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new Group();
                    group.Namespace = ns ?? "";
                    group.Name = name ?? "";
                    groups[key] = group;
                }
                return group;
            }

            static List<Method> Expand(Group group, Dictionary<string, Group> groups)
            {
                var list = new List<Method>();
                Collect(group, groups, list, new HashSet<string>(StringComparer.Ordinal));
                return list;
            }

            static void Collect(Group group, Dictionary<string, Group> groups, List<Method> list, HashSet<string> seen)
            {
                if (group == null || !seen.Add(Key(group.Namespace, group.Name))) return;
                foreach (var test in group.Tests) list.Add(test);
                if (!group.Inherits) return;
                foreach (var baseName in group.Bases)
                {
                    foreach (var other in groups.Values)
                    {
                        if (other.Name == baseName) Collect(other, groups, list, seen);
                    }
                }
            }

            static void Note(List<Method> tests, Dictionary<string, Group> groups, GateContext ctx, List<string> problems)
            {
                foreach (var test in tests)
                {
                    if (Forbidden(test)) problems.Add("forbid " + test.Name);
                    if (test.Preprocessor)
                    {
                        problems.Add("preprocessor " + test.Name);
                        continue;
                    }
                    Group group;
                    groups.TryGetValue(Key(test.Namespace, test.Class), out group);
                    if (!Covered(group, ctx) && !test.Owner.MethodIsGated(test, ctx)) problems.Add("ungated " + test.Name);
                }
            }

            static bool Covered(Group group, GateContext ctx)
            {
                if (group == null) return false;
                foreach (var before in group.Befores)
                {
                    if (!before.Preprocessor && before.Owner.MethodIsGated(before, ctx)) return true;
                }
                if (group.Ctors.Count == 0) return false;
                foreach (var ctor in group.Ctors)
                {
                    if (ctor.Preprocessor || !ctor.Owner.MethodIsGated(ctor, ctx)) return false;
                }
                return true;
            }

            static bool Forbidden(Method method)
            {
                if (method == null || method.Owner == null || method.BodyA >= method.BodyB) return false;
                var raw = method.Owner.Raw(method.BodyA, method.BodyB);
                foreach (Match match in SkipMessage.Matches(raw))
                {
                    if (Forbid.IsMatch(match.Groups[1].Value)) return true;
                }
                return false;
            }

            bool MethodIsGated(Method method, GateContext ctx)
            {
                if (method == null || method.Preprocessor || ctx == null) return false;
                bool cached;
                if (ctx.Cache.TryGetValue(method, out cached)) return cached;
                if (ctx.Stack.Contains(method)) return false;
                ctx.Stack.Push(method);
                var gated = Compute(method, ctx);
                ctx.Stack.Pop();
                ctx.Cache[method] = gated;
                return gated;
            }

            bool Compute(Method method, GateContext ctx)
            {
                var statements = Statements(method.BodyA, method.BodyB);
                if (statements.Count == 0) return false;
                if (StatementIsGate(statements[0], method, ctx)) return true;
                if (statements.Count >= 2 && IsRequireClient(statements[0], method, ctx) && IsOsSkipIf(statements[1])) return true;
                return false;
            }

            bool StatementIsGate(int[] span, Method caller, GateContext ctx)
            {
                if (IsOsSkipIf(span)) return true;
                if (IsHelperReturnIf(span, caller, ctx)) return true;
                if (IsDirectCall(span, caller, ctx)) return true;
                if (IsNewFixture(span, ctx)) return true;
                if (IsDeconstruct(span, caller, ctx)) return true;
                if (IsParsedProbe(span, caller, ctx)) return true;
                return false;
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

            bool IsHelperReturnIf(int[] span, Method caller, GateContext ctx)
            {
                int open;
                if (!IfCondition(span, out open)) return false;
                int close = SkipParens(open, span[1]);
                if (close != open + 6 || close > span[1]) return false;
                if (_toks[open + 1].T != "!" || !IsIdent(_toks[open + 2].T) || _toks[open + 3].T != "(" || _toks[open + 4].T != ")") return false;
                if (!Seq(close, span[1], "return", ";")) return false;
                var hits = ctx.Resolve(caller, "", _toks[open + 2].T);
                if (hits.Count != 1) return false;
                return ReturnTrueShape(hits[0]);
            }

            bool ReturnTrueShape(Method method)
            {
                var owner = method.Owner;
                var statements = owner.Statements(method.BodyA, method.BodyB);
                if (statements.Count < 2) return false;
                if (!owner.IsLinuxReturnTrue(statements[0])) return false;
                var skipped = false;
                for (var n = 1; n < statements.Count; n++)
                {
                    if (owner.IsSkipStatement(statements[n][0], statements[n][1])) { skipped = true; continue; }
                    if (owner.Seq(statements[n][0], statements[n][1], "return", "false", ";")) continue;
                    return false;
                }
                return skipped;
            }

            bool IsRequireClient(int[] span, Method caller, GateContext ctx)
            {
                if (!Seq(span[0], span[1], "RequireClient", "(", ")", ";")) return false;
                var hits = ctx.Resolve(caller, "", "RequireClient");
                if (hits.Count != 1) return false;
                var owner = hits[0].Owner;
                var body = owner.Statements(hits[0].BodyA, hits[0].BodyB);
                if (body.Count != 2) return false;
                return IsTypeofAssert(owner, body[0]) && IsTypeofAssert(owner, body[1]);
            }

            static bool IsTypeofAssert(FileModel owner, int[] span)
            {
                return owner.Seq(span[0], span[1], "typeof", "(", "PhoneHomeRunnerClient", ")", ".", "GetMethod", "(", ")", ".", "ShouldNotBeNull", "(", ")", ";");
            }

            bool IsDirectCall(int[] span, Method caller, GateContext ctx)
            {
                var a = span[0];
                var b = span[1];
                var i = a;
                if (i < b && _toks[i].T == "await") i++;
                string qual;
                string name;
                int open;
                if (!CallAt(i, b, out qual, out name, out open)) return false;
                var close = SkipParens(open, b);
                if (close != b - 1 || _toks[b - 1].T != ";") return false;
                if (name == "RequirePlatform" && qual.Length == 0 && PlatformTernary(open - 1, b) && PlatformHelper(caller, ctx)) return true;
                if (!SimpleArgs(open, close)) return false;
                var hits = ctx.Resolve(caller, qual, name);
                if (hits.Count == 0) return false;
                foreach (var hit in hits)
                {
                    if (!hit.Owner.MethodIsGated(hit, ctx)) return false;
                }
                return true;
            }

            bool IsNewFixture(int[] span, GateContext ctx)
            {
                var a = span[0];
                var b = span[1];
                var i = a;
                if (i < b && _toks[i].T == "await") i++;
                if (i < b && _toks[i].T == "using") i++;
                if (i >= b || _toks[i].T != "var") return false;
                i++;
                if (i >= b || !IsIdent(_toks[i].T)) return false;
                i++;
                if (i >= b || _toks[i].T != "=") return false;
                i++;
                if (i >= b || _toks[i].T != "new") return false;
                i++;
                if (i >= b || !IsIdent(_toks[i].T)) return false;
                var type = _toks[i].T;
                i++;
                if (i >= b || _toks[i].T != "(") return false;
                var close = SkipParens(i, b);
                if (!SimpleArgs(i, close)) return false;
                if (close != b - 1 || _toks[b - 1].T != ";") return false;
                var ctors = ctx.Ctors(type);
                if (ctors.Count == 0) return false;
                foreach (var ctor in ctors)
                {
                    if (!ctor.Owner.MethodIsGated(ctor, ctx)) return false;
                }
                return true;
            }

            bool IsDeconstruct(int[] span, Method caller, GateContext ctx)
            {
                var a = span[0];
                var b = span[1];
                if (b - a < 10 || _toks[a].T != "var" || _toks[a + 1].T != "(") return false;
                var i = a + 2;
                var parts = 0;
                while (parts < 3)
                {
                    if (i >= b || !IsIdent(_toks[i].T)) return false;
                    i++;
                    parts++;
                    if (parts < 3)
                    {
                        if (i >= b || _toks[i].T != ",") return false;
                        i++;
                    }
                }
                if (i >= b || _toks[i].T != ")") return false;
                i++;
                if (i >= b || _toks[i].T != "=") return false;
                i++;
                if (i < b && _toks[i].T == "await") i++;
                string qual;
                string name;
                int open;
                if (!CallAt(i, b, out qual, out name, out open)) return false;
                var close = SkipParens(open, b);
                if (!SimpleArgs(open, close)) return false;
                if (close != b - 1 || _toks[b - 1].T != ";") return false;
                var hits = ctx.Resolve(caller, qual, name);
                if (hits.Count == 0) return false;
                foreach (var hit in hits)
                {
                    if (!hit.Owner.MethodIsGated(hit, ctx)) return false;
                }
                return true;
            }

            bool IsParsedProbe(int[] span, Method caller, GateContext ctx)
            {
                var a = span[0];
                var b = span[1];
                if (b - a < 12) return false;
                if (_toks[a].T != "using" || _toks[a + 1].T != "var" || !IsIdent(_toks[a + 2].T) || _toks[a + 3].T != "=") return false;
                if (_toks[a + 4].T != "JsonDocument" || _toks[a + 5].T != "." || _toks[a + 6].T != "Parse" || _toks[a + 7].T != "(") return false;
                if (_toks[a + 8].T != "await") return false;
                string qual;
                string name;
                int open;
                if (!CallAt(a + 9, b, out qual, out name, out open)) return false;
                if (qual.Length != 0 || name != "RunProbeAsync") return false;
                var close = SkipParens(open, b);
                if (!SimpleArgs(open, close)) return false;
                if (close >= b || _toks[close].T != ")") return false;
                if (close + 1 != b - 1 || _toks[b - 1].T != ";") return false;
                var hits = ctx.Resolve(caller, "", "RunProbeAsync");
                if (hits.Count == 0) return false;
                foreach (var hit in hits)
                {
                    if (!hit.Owner.MethodIsGated(hit, ctx)) return false;
                }
                return true;
            }

            bool CallAt(int i, int b, out string qual, out string name, out int open)
            {
                qual = "";
                name = "";
                open = -1;
                if (i >= b || !IsIdent(_toks[i].T)) return false;
                name = _toks[i].T;
                var j = i;
                while (j + 2 < b && _toks[j + 1].T == "." && IsIdent(_toks[j + 2].T))
                {
                    qual = _toks[j].T;
                    j += 2;
                    name = _toks[j].T;
                }
                if (j + 1 >= b || _toks[j + 1].T != "(") return false;
                open = j + 1;
                return true;
            }

            bool SimpleArgs(int open, int close)
            {
                for (var i = open + 1; i < close - 1; i++)
                {
                    var token = _toks[i].T;
                    if (IsIdent(token) || token == "," || token == "." || IsDigits(token)) continue;
                    return false;
                }
                return true;
            }

            static bool IsDigits(string token)
            {
                if (token == null || token.Length == 0) return false;
                for (var i = 0; i < token.Length; i++)
                {
                    if (token[i] < '0' || token[i] > '9') return false;
                }
                return true;
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

            bool PlatformTernary(int nameIndex, int limit)
            {
                if (nameIndex + 1 >= limit || _toks[nameIndex + 1].T != "(") return false;
                var after = SkipParens(nameIndex + 1, limit);
                var raw = Raw(nameIndex, after);
                return PlatformCall.IsMatch(raw);
            }

            bool PlatformHelper(Method caller, GateContext ctx)
            {
                var hits = ctx.Resolve(caller, "", "RequirePlatform");
                if (hits.Count != 1) return false;
                var owner = hits[0].Owner;
                var statements = owner.Statements(hits[0].BodyA, hits[0].BodyB);
                if (statements.Count == 0) return false;
                return PlatformSkip.IsMatch(owner.Raw(statements[0][0], statements[0][1]));
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
                int i;
                if (b - a >= 4 && _toks[a].T == "Skip" && _toks[a + 1].T == "." && _toks[a + 2].T == "Test" && _toks[a + 3].T == "(")
                {
                    i = SkipParens(a + 3, b);
                }
                else
                {
                    if (a >= b || _toks[a].T != "throw" || a + 1 >= b || _toks[a + 1].T != "new") return false;
                    i = a + 2;
                    var last = "";
                    while (i < b && (IsIdent(_toks[i].T) || _toks[i].T == "."))
                    {
                        if (IsIdent(_toks[i].T)) last = _toks[i].T;
                        i++;
                    }
                    if (last != "SkipTestException" || i >= b || _toks[i].T != "(") return false;
                    i = SkipParens(i, b);
                }
                if (i < b && _toks[i].T == ";") i++;
                return i == b;
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
                if (depth != 0 && limit >= _toks.Count) Misparse("unbalanced-brace");
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
                if (depth != 0 && limit >= _toks.Count) Misparse("unbalanced-paren");
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
                if (kw == "class" || kw == "struct" || kw == "interface" || kw == "enum" || kw == "record") { TypeDecl(attrs); return; }
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
                var parts = new List<string>();
                while (_p < _toks.Count && Tok() != ";" && Tok() != "{")
                {
                    if (IsIdent(Tok())) parts.Add(Take());
                    else _p++;
                }
                var name = string.Join(".", parts.ToArray());
                var full = _namespace.Length == 0 ? name : _namespace + "." + name;
                if (Tok() == ";")
                {
                    _namespace = full;
                    _p++;
                    return;
                }
                if (Tok() == "{")
                {
                    var previous = _namespace;
                    _namespace = full;
                    _p++;
                    while (_p < _toks.Count && Tok() != "}") Declaration();
                    if (Tok() == "}") _p++;
                    _namespace = previous;
                }
            }

            void TypeDecl(List<Attr> attrs)
            {
                var kind = Take();
                if (kind == "record" && (Tok() == "class" || Tok() == "struct")) Take();
                var typeName = Dotted();
                if (Tok() == "<") _p = SkipAngles(_p, _toks.Count);
                if (Tok() == "(") _p = SkipParens(_p, _toks.Count);
                var baseName = "";
                var sawBase = false;
                while (Tok() != "{" && Tok() != ";" && Tok() != "" && Tok() != "}")
                {
                    if (Tok() == "(") { _p = SkipParens(_p, _toks.Count); continue; }
                    if (Tok() == "<") { _p = SkipAngles(_p, _toks.Count); continue; }
                    if (Tok() == "[") { _p = SkipBracesLike(_p, "[", "]"); continue; }
                    if (Tok() == ":" && !sawBase)
                    {
                        sawBase = true;
                        _p++;
                        baseName = Dotted();
                        if (Tok() == "<") _p = SkipAngles(_p, _toks.Count);
                        continue;
                    }
                    if (Tok() == "where") sawBase = true;
                    _p++;
                }
                var fact = new ClassFact();
                fact.Namespace = _namespace;
                fact.Name = typeName;
                fact.BaseName = baseName;
                fact.Inherits = Has(attrs, "InheritsTests") || Has(attrs, "InheritsTestsAttribute");
                _facts.Add(fact);
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
                var frozen = false;
                var method = false;
                while (_p < _toks.Count)
                {
                    var t = Tok();
                    if (t == "<" && paren == 0 && bracket == 0) { angle++; _p++; continue; }
                    if (t == ">" && angle > 0 && paren == 0 && bracket == 0) { angle--; _p++; continue; }
                    if (t == "[" && angle == 0 && paren == 0) { bracket++; _p++; continue; }
                    if (t == "]" && bracket > 0 && paren == 0) { bracket--; _p++; continue; }
                    if (t == "(" && angle == 0 && bracket == 0) { paren++; _p++; continue; }
                    if (t == ")" && paren > 0)
                    {
                        paren--;
                        _p++;
                        if (paren == 0 && angle == 0 && bracket == 0)
                        {
                            sawParen = true;
                            if (lastIdent.Length > 0) frozen = true;
                        }
                        continue;
                    }
                    if (angle == 0 && paren == 0 && bracket == 0 && (t == "{" || t == "=" || t == ";" || t == "}")) break;
                    if (!frozen && angle == 0 && paren == 0 && bracket == 0 && IsIdent(t))
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
                var srcA = 0;
                var srcB = 0;
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
                    srcA = _toks[open].S;
                    srcB = _p > 0 ? _toks[_p - 1].E : srcA;
                }
                else if (Tok() == "=" && Tok(1) == ">")
                {
                    var arrow = _p;
                    _p += 2;
                    bodyA = _p;
                    _p = ConsumeSemi(_p, _toks.Count);
                    bodyB = _p;
                    hasBody = true;
                    srcA = _toks[arrow].S;
                    srcB = bodyB > bodyA ? _toks[bodyB - 1].E : srcA;
                }
                else FinishNonMethod();
                if (!hasBody) return;
                var info = new Method();
                info.Namespace = _namespace;
                info.Class = cls;
                info.Name = lastIdent;
                info.Attrs = attrs;
                info.BodyA = bodyA;
                info.BodyB = bodyB;
                info.SrcA = srcA;
                info.SrcB = srcB;
                info.Preprocessor = InConditional(srcA, srcB);
                info.Owner = this;
                info.IsStatic = mods.Contains("static");
                info.IsCtor = !mods.Contains("static") && lastIdent == cls;
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

            void Misparse(string detail)
            {
                if (_misparse) return;
                _misparse = true;
                _misparseDetail = detail ?? "misparse";
            }

            bool InConditional(int start, int end)
            {
                for (var n = 0; n < _conds.Count; n++)
                {
                    var span = _conds[n];
                    if (start < span[1] && span[0] < end) return true;
                }
                return false;
            }

            void NoteDirective(string word, int mark, int lineEnd)
            {
                if (word == "if" || word == "elif" || word == "else")
                {
                    if ((word == "elif" || word == "else") && _condOpen.Count == 0)
                    {
                        Misparse("unmatched-" + word);
                        return;
                    }
                    _conds.Add(new int[] { mark, _src.Length });
                    _condKind.Add(word);
                    _condOpen.Push(_conds.Count - 1);
                    return;
                }
                if (word != "endif") return;
                if (_condOpen.Count == 0)
                {
                    Misparse("unmatched-endif");
                    return;
                }
                while (_condOpen.Count > 0)
                {
                    var idx = _condOpen.Pop();
                    _conds[idx][1] = lineEnd;
                    if (_condKind[idx] == "if") break;
                }
            }

            static bool IsHex(char c)
            {
                return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            }

            bool TakeHex(ref int i, int min, int max)
            {
                var n = _src.Length;
                var count = 0;
                while (count < max && i < n && IsHex(_src[i]))
                {
                    i++;
                    count++;
                }
                return count >= min;
            }

            bool ConsumeEscape(ref int i)
            {
                var n = _src.Length;
                if (i >= n) return false;
                var c = _src[i];
                i++;
                if (c == 'u') return TakeHex(ref i, 4, 4);
                if (c == 'U') return TakeHex(ref i, 8, 8);
                if (c == 'x') return TakeHex(ref i, 1, 4);
                return true;
            }

            bool TryChar(ref int i)
            {
                var n = _src.Length;
                if (i >= n || _src[i] != '\'') return false;
                i++;
                if (i >= n)
                {
                    Misparse("unterminated-char");
                    return true;
                }
                if (_src[i] == '\\')
                {
                    i++;
                    if (!ConsumeEscape(ref i)) Misparse("bad-char-escape");
                }
                else if (_src[i] == '\n' || _src[i] == '\r' || _src[i] == '\'')
                {
                    Misparse("bad-char");
                    return true;
                }
                else i++;
                if (i >= n || _src[i] != '\'')
                {
                    Misparse("unterminated-char");
                    return true;
                }
                i++;
                return true;
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
                        var mark = i;
                        i++;
                        while (i < n && _src[i] != '\n' && char.IsWhiteSpace(_src[i])) i++;
                        var wordStart = i;
                        while (i < n && (char.IsLetter(_src[i]) || _src[i] == '_')) i++;
                        var word = _src.Substring(wordStart, i - wordStart);
                        while (i < n && _src[i] != '\n') i++;
                        NoteDirective(word, mark, i);
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
                    if (TryChar(ref i)) continue;
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
                    if (!verbatim && c == '\\')
                    {
                        i++;
                        if (!ConsumeEscape(ref i)) Misparse("bad-escape");
                        continue;
                    }
                    if (c == '"')
                    {
                        if (verbatim && i + 1 < n && _src[i + 1] == '"') { i += 2; continue; }
                        i++;
                        return;
                    }
                    i++;
                }
                Misparse("unterminated-string");
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
                Misparse("unterminated-raw");
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
                    if (TryChar(ref i)) continue;
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
