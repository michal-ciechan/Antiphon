// CARD-1820 S1R5: the PowerShell reader of the release script closure (ReleaseImpactScripts). It is a tokenizer and a
// recursive-descent parser for the PowerShell that the closure reads, written when scripts/lib/release-impact-scripts.ps1
// moved to C# (unit 17 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). The PowerShell used
// System.Management.Automation's Parser; the core cannot, because the test process does not load that assembly.
//
// It builds the nodes the closure inspects, with PowerShell's kinds, extents (start and end offsets) and parents:
// commands with their invocation operator, elements and parameters (and a parameter's colon argument); assignments
// (an expression right side is the CommandExpression itself, as in PowerShell); parameters; foreach loops; string
// constants (bare words, quoted and here-strings, with escapes); expandable strings with their nested variables and
// subexpressions (a bare word such as $PSScriptRoot/lib/x.ps1 is one, as in PowerShell); parentheses, subexpressions,
// arrays, hashtables, script blocks, casts, type literals, member access and calls, indexes, and the unary, binary and
// ternary operators with PowerShell's precedence (the comma binds tighter than the binary operators).
// The command-mode rules are PowerShell's: a generic token runs to a blank or one of {}();,|& and drops its quotes; a
// parameter name ends at ':', '.' or '['; '--' ends the parameters; '-1' and '[int]5' are bare words.
//
// Anything it does not read is a syntax failure (Failed, the closure's 'parse' reason): an unterminated string, comment
// or block, an unexpected token, and the statements it does not model (class, enum, data, configuration, workflow).
// PowerShell's semantic parse checks (a duplicate parameter, param() beside a function's argument list) are not made.
// The reader only reads text: it never runs, loads or resolves anything. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The node kinds the PowerShell reader builds (PowerShell's Ast types without the suffix).</summary>
    internal enum PsKind
    {
        ScriptBlock, NamedBlock, ParamBlock, Parameter, Attribute, NamedAttributeArgument, TypeConstraint,
        StatementBlock, Pipeline, PipelineChain, Command, CommandExpression, CommandParameter, Redirection,
        Assignment, If, While, DoLoop, For, ForEach, Switch, Try, Catch, Trap, FunctionDefinition, Flow, Using,
        StringConstant, ExpandableString, Constant, Variable, Paren, SubExpression, ArrayExpression, Hashtable,
        ScriptBlockExpression, Convert, TypeExpression, Attributed, Binary, Unary, Ternary, ArrayLiteral, Member,
        InvokeMember, Index,
    }

    /// <summary>
    /// One node. Value: a string constant's value, a variable's user path, a parameter's name, a type's name, an
    /// operator. Left/Right: a binary or assignment's operands, a member access's target and member. Child: the inner
    /// node of a paren, subexpression, convert, command expression, parameter argument, script block expression,
    /// redirection, unary, and the variable of a parameter or foreach. Items: pipeline and command elements, statements,
    /// array elements, call arguments and an expandable string's nested expressions.
    /// </summary>
    internal sealed class PsAst
    {
        internal PsAst(PsKind kind, int start)
        {
            Kind = kind;
            Start = start;
            End = start;
        }

        internal readonly PsKind Kind;
        internal readonly List<PsAst> Children = new List<PsAst>();
        internal readonly List<PsAst> Items = new List<PsAst>();
        internal PsAst Parent;
        internal int Start;
        internal int End;
        internal string Value;
        internal char Invocation;
        internal bool Static;
        internal bool Splatted;
        internal bool HasTraps;
        internal PsAst Left;
        internal PsAst Right;
        internal PsAst Child;
        internal PsAst Condition;

        internal PsAst Add(PsAst child)
        {
            if (child == null) return null;
            child.Parent = this;
            Children.Add(child);
            return child;
        }

        internal PsAst AddItem(PsAst child)
        {
            if (child == null) return null;
            Add(child);
            Items.Add(child);
            return child;
        }

        /// <summary>Every node under this one and itself, depth first in source order (PowerShell's FindAll).</summary>
        internal List<PsAst> FindAll()
        {
            var nodes = new List<PsAst>();
            var stack = new Stack<PsAst>();
            stack.Push(this);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                nodes.Add(node);
                for (int i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
            }
            return nodes;
        }

        /// <summary>PowerShell's CommandAst.GetCommandName: the first element's value when it is a string constant.</summary>
        internal string CommandName =>
            Kind == PsKind.Command && Items.Count > 0 && Items[0].Kind == PsKind.StringConstant ? Items[0].Value : null;
    }

    /// <summary>Reads PowerShell source into <see cref="PsAst"/> nodes; never runs anything.</summary>
    internal sealed class PsReader
    {
        private const int MaxNesting = 200;

        private static readonly HashSet<string> UnaryWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "not", "bnot", "split", "isplit", "csplit", "join",
        };

        private static readonly HashSet<string> NamedBlocks = new HashSet<string>(StringComparer.Ordinal)
        {
            "begin", "process", "end", "dynamicparam", "clean",
        };

        // Binary operator precedence, PowerShell's order: logical, bitwise, null-coalescing, comparison, additive,
        // multiplicative, format, range.
        private const int Logical = 1, Bitwise = 2, Coalesce = 3, Comparison = 5, Additive = 6, Multiplicative = 7,
            Format = 8, Range = 9;

        private static readonly Dictionary<string, int> DashOperators = BuildDashOperators();

        private static readonly Regex NumberLiteral = new Regex(
            @"^(0x[0-9a-f]+|0b[01]+|(\d+\.?\d*|\.\d+)(e[+-]?\d+)?)(ul|us|uy|[ldusyn])?(kb|mb|gb|tb|pb)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly string _s;
        private readonly int _n;
        private int _p;
        private int _nesting;
        private bool _failed;

        private PsReader(string text)
        {
            _s = text;
            _n = text.Length;
        }

        private sealed class TooDeep : Exception
        {
        }

        /// <summary>Parses a whole file. Failed is true when the reader met anything it does not read.</summary>
        internal static PsAst Parse(string text, out bool failed)
        {
            var reader = new PsReader(text ?? "");
            PsAst root;
            try
            {
                root = reader.ParseScriptBlock(0, '\0');
                root.End = reader._n;
            }
            catch (TooDeep)
            {
                reader._failed = true;
                root = new PsAst(PsKind.ScriptBlock, 0) { End = reader._n };
            }
            failed = reader._failed;
            return root;
        }

        private static Dictionary<string, int> BuildDashOperators()
        {
            var operators = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["and"] = Logical, ["or"] = Logical, ["xor"] = Logical,
                ["band"] = Bitwise, ["bor"] = Bitwise, ["bxor"] = Bitwise, ["shl"] = Bitwise, ["shr"] = Bitwise,
                ["is"] = Comparison, ["isnot"] = Comparison, ["as"] = Comparison, ["join"] = Comparison,
                ["f"] = Format,
            };
            foreach (var name in new[]
                     {
                         "eq", "ne", "gt", "ge", "lt", "le", "like", "notlike", "match", "notmatch", "replace", "contains",
                         "notcontains", "in", "notin", "split",
                     })
            {
                operators[name] = Comparison;
                operators["i" + name] = Comparison;
                operators["c" + name] = Comparison;
            }
            return operators;
        }

        // ---- characters ----

        private char Peek(int k = 0)
        {
            int i = _p + k;
            return i < _n ? _s[i] : '\0';
        }

        private bool AtEnd => _p >= _n;

        private void Fail() => _failed = true;

        private void Enter()
        {
            if (++_nesting > MaxNesting) throw new TooDeep();
        }

        private void Leave() => _nesting--;

        private static bool IsNewline(char c) => c == '\n' || c == '\r';

        private static bool IsBlank(char c) =>
            c == ' ' || c == '\t' || c == '\f' || c == '\v' || c == (char)0x00a0 || c == (char)0x0085 || c == (char)0xfeff
            || (c > '\u007f' && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator);

        private static bool IsDash(char c) => c == '-' || c == (char)0x2013 || c == (char)0x2014 || c == (char)0x2015;

        private static bool IsSingleQuote(char c) => c == '\'' || c == (char)0x2018 || c == (char)0x2019 || c == (char)0x201a || c == (char)0x201b;

        private static bool IsDoubleQuote(char c) => c == '"' || c == (char)0x201c || c == (char)0x201d || c == (char)0x201e;

        private static bool IsVarChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '?';

        private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        // A command-mode (generic) token ends at the end, a blank, a newline or one of these.
        private static bool EndsGeneric(char c) => c == '\0' || IsBlank(c) || IsNewline(c) || "{}();,|&".IndexOf(c) >= 0;

        // An element of a command ends here (a closing bracket of an enclosing construct, a separator, a pipe).
        private bool AtCommandEnd()
        {
            char c = Peek();
            return AtEnd || IsNewline(c) || c == ';' || c == ')' || c == '}' || c == '|' || c == '&';
        }

        // Blanks, comments and line continuations; newlines too when asked.
        private void Skip(bool newlines)
        {
            while (_p < _n)
            {
                char c = _s[_p];
                if (IsBlank(c))
                {
                    _p++;
                    continue;
                }
                if (c == '`' && IsNewline(Peek(1)))
                {
                    _p += Peek(1) == '\r' && Peek(2) == '\n' ? 3 : 2;
                    continue;
                }
                if (IsNewline(c))
                {
                    if (!newlines) return;
                    _p++;
                    continue;
                }
                if (c == '#')
                {
                    while (_p < _n && !IsNewline(_s[_p])) _p++;
                    continue;
                }
                if (c == '<' && Peek(1) == '#')
                {
                    int close = _s.IndexOf("#>", _p + 2, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        Fail();
                        _p = _n;
                        return;
                    }
                    _p = close + 2;
                    continue;
                }
                return;
            }
        }

        private void SkipSeparators()
        {
            while (true)
            {
                Skip(true);
                if (Peek() != ';') return;
                _p++;
            }
        }

        // The first character after trivia at a position, without moving.
        private char PeekAfter(int position)
        {
            int save = _p;
            bool failed = _failed;
            _p = position;
            Skip(true);
            char c = Peek();
            _p = save;
            _failed = failed;
            return c;
        }

        private bool Expect(char c)
        {
            if (Peek() == c && !AtEnd)
            {
                _p++;
                return true;
            }
            Fail();
            return false;
        }

        // A keyword-shaped word at a position: ASCII letters that end where a generic token ends (or at '[').
        private string WordAt(int position, out int end)
        {
            int i = position;
            while (i < _n && IsAsciiLetter(_s[i])) i++;
            end = i;
            if (i == position) return null;
            char next = i < _n ? _s[i] : '\0';
            if (!EndsGeneric(next) && next != '[') return null;
            return _s.Substring(position, i - position).ToLowerInvariant();
        }

        private string LettersAt(int position)
        {
            int i = position;
            while (i < _n && char.IsLetter(_s[i])) i++;
            return _s.Substring(position, i - position).ToLowerInvariant();
        }

        // ---- script blocks and statements ----

        // A script block's body up to (not including) its closing character ('\0' for a file).
        private PsAst ParseScriptBlock(int start, char close)
        {
            Enter();
            var block = new PsAst(PsKind.ScriptBlock, start);
            SkipSeparators();
            while (WordAt(_p, out _) == "using")
            {
                block.Add(ParseUsing());
                SkipSeparators();
            }
            int save = _p;
            bool failed = _failed;
            var attributes = new List<PsAst>();
            while (Peek() == '[')
            {
                attributes.Add(ParseAttributeOrType());
                Skip(true);
            }
            if (WordAt(_p, out int paramEnd) == "param" && PeekAfter(paramEnd) == '(')
            {
                var param = new PsAst(PsKind.ParamBlock, attributes.Count > 0 ? attributes[0].Start : _p);
                foreach (var attribute in attributes) param.Add(attribute);
                _p = paramEnd;
                Skip(true);
                _p++;
                ParseParameterList(param, ')');
                Expect(')');
                param.End = _p;
                block.Add(param);
                SkipSeparators();
            }
            else
            {
                _p = save;
                _failed = failed;
            }
            if (IsNamedBlockStart())
            {
                while (true)
                {
                    SkipSeparators();
                    if (AtEnd || (close != '\0' && Peek() == close)) break;
                    int before = _p;
                    if (IsNamedBlockStart())
                    {
                        string name = WordAt(_p, out int nameEnd);
                        var named = new PsAst(PsKind.NamedBlock, _p) { Value = name };
                        _p = nameEnd;
                        Skip(true);
                        Expect('{');
                        ParseStatementList(named, '}');
                        Expect('}');
                        named.End = _p;
                        block.Add(named);
                        continue;
                    }
                    // A statement outside the named blocks.
                    Fail();
                    var stray = new PsAst(PsKind.NamedBlock, _p);
                    stray.AddItem(ParseStatement());
                    stray.End = _p;
                    block.Add(stray);
                    if (_p == before) _p++;
                }
            }
            else
            {
                var named = new PsAst(PsKind.NamedBlock, _p);
                ParseStatementList(named, close);
                named.End = _p;
                block.Add(named);
            }
            block.End = _p;
            Leave();
            return block;
        }

        private bool IsNamedBlockStart()
        {
            string word = WordAt(_p, out int end);
            return word != null && NamedBlocks.Contains(word) && PeekAfter(end) == '{';
        }

        // Statements up to (not including) the closing character ('\0' for the end of the text).
        private void ParseStatementList(PsAst owner, char close)
        {
            while (true)
            {
                SkipSeparators();
                if (AtEnd)
                {
                    if (close != '\0') Fail();
                    return;
                }
                char c = Peek();
                if (close != '\0' && c == close) return;
                if (c == ')' || c == '}')
                {
                    Fail();
                    _p++;
                    continue;
                }
                int before = _p;
                var statement = ParseStatement();
                if (statement != null)
                {
                    owner.AddItem(statement);
                    if (statement.Kind == PsKind.Trap) owner.HasTraps = true;
                }
                int after = _p;
                Skip(false);
                c = Peek();
                bool separated = AtEnd || IsNewline(c) || c == ';' || c == close;
                if (!separated) Fail();
                if (_p == before)
                {
                    Fail();
                    _p++;
                }
                else if (separated)
                {
                    _p = after;
                }
            }
        }

        private PsAst ParseStatement()
        {
            Enter();
            try
            {
                Skip(false);
                // A loop label.
                if (Peek() == ':' && (char.IsLetterOrDigit(Peek(1)) || Peek(1) == '_'))
                {
                    int i = _p + 1;
                    while (i < _n && (char.IsLetterOrDigit(_s[i]) || _s[i] == '_')) i++;
                    if (i >= _n || EndsGeneric(_s[i]))
                    {
                        _p = i;
                        Skip(false);
                    }
                }
                int start = _p;
                string keyword = WordAt(_p, out int end);
                switch (keyword)
                {
                    case "if": return ParseIf(start, end);
                    case "switch": return ParseSwitch(start, end);
                    case "foreach": return ParseForEach(start, end);
                    case "for": return ParseFor(start, end);
                    case "while": return ParseWhile(start, end);
                    case "do": return ParseDo(start, end);
                    case "function":
                    case "filter":
                        return ParseFunction(start, end);
                    case "try": return ParseTry(start, end);
                    case "trap": return ParseTrap(start, end);
                    case "return":
                    case "throw":
                    case "exit":
                        return ParseFlow(start, end, keyword, true);
                    case "break":
                    case "continue":
                        return ParseFlow(start, end, keyword, false);
                    case "using":
                        // Only allowed before everything else in a script.
                        Fail();
                        return ParseUsing();
                    case "workflow":
                        // Not PowerShell 7 syntax.
                        Fail();
                        return ParseFunction(start, end);
                    case "data":
                    case "class":
                    case "enum":
                    case "configuration":
                    case "param":
                    case "else":
                    case "elseif":
                    case "catch":
                    case "finally":
                    case "until":
                        // Not modelled, or out of place: read on as a command so its nodes are still seen.
                        Fail();
                        break;
                }
                return ParsePipelineChain();
            }
            finally
            {
                Leave();
            }
        }

        private PsAst ParseIf(int start, int keywordEnd)
        {
            var node = new PsAst(PsKind.If, start);
            _p = keywordEnd;
            while (true)
            {
                Skip(true);
                if (!Expect('(')) break;
                Skip(true);
                node.Add(ParsePipelineChain());
                Skip(true);
                Expect(')');
                Skip(true);
                node.Add(ParseStatementBlock());
                int save = _p;
                Skip(true);
                string next = WordAt(_p, out int nextEnd);
                if (next == "elseif")
                {
                    _p = nextEnd;
                    continue;
                }
                if (next == "else")
                {
                    _p = nextEnd;
                    Skip(true);
                    node.Add(ParseStatementBlock());
                    break;
                }
                _p = save;
                break;
            }
            node.End = _p;
            return node;
        }

        private PsAst ParseStatementBlock()
        {
            var block = new PsAst(PsKind.StatementBlock, _p);
            if (Expect('{'))
            {
                ParseStatementList(block, '}');
                Expect('}');
            }
            block.End = _p;
            return block;
        }

        private PsAst ParseSwitch(int start, int keywordEnd)
        {
            var node = new PsAst(PsKind.Switch, start);
            _p = keywordEnd;
            bool file = false;
            while (true)
            {
                Skip(true);
                if (!IsDash(Peek()) || !char.IsLetter(Peek(1))) break;
                string option = LettersAt(_p + 1);
                _p += 1 + option.Length;
                if ("file".StartsWith(option, StringComparison.Ordinal))
                {
                    file = true;
                    Skip(true);
                    node.Add(ParseCommandArgument(false, true));
                }
            }
            Skip(true);
            if (!file && Expect('('))
            {
                Skip(true);
                node.Add(ParsePipelineChain());
                Skip(true);
                Expect(')');
            }
            Skip(true);
            PsAst defaultBody = null;
            if (Expect('{'))
            {
                while (true)
                {
                    SkipSeparators();
                    if (AtEnd)
                    {
                        Fail();
                        break;
                    }
                    if (Peek() == '}')
                    {
                        _p++;
                        break;
                    }
                    int before = _p;
                    if (WordAt(_p, out int defaultEnd) == "default" && PeekAfter(defaultEnd) == '{')
                    {
                        _p = defaultEnd;
                        Skip(true);
                        defaultBody = ParseStatementBlock();
                        continue;
                    }
                    node.Add(ParseCommandPrimary(false, true));
                    Skip(true);
                    node.Add(ParseStatementBlock());
                    if (_p == before)
                    {
                        Fail();
                        _p++;
                    }
                }
            }
            // PowerShell visits the default clause last.
            node.Add(defaultBody);
            node.End = _p;
            return node;
        }

        private PsAst ParseForEach(int start, int keywordEnd)
        {
            var node = new PsAst(PsKind.ForEach, start);
            _p = keywordEnd;
            Skip(true);
            while (IsDash(Peek()) && char.IsLetter(Peek(1)))
            {
                _p += 1 + LettersAt(_p + 1).Length;
                Skip(true);
            }
            if (Expect('('))
            {
                Skip(true);
                var variable = Peek() == '$' ? TryScanVariable() : null;
                if (variable == null) Fail();
                else
                {
                    node.Child = variable;
                    node.Add(variable);
                }
                Skip(true);
                if (WordAt(_p, out int inEnd) == "in") _p = inEnd;
                else Fail();
                Skip(true);
                var condition = ParsePipelineChain();
                node.Condition = condition;
                node.Add(condition);
                Skip(true);
                Expect(')');
            }
            Skip(true);
            node.Add(ParseStatementBlock());
            node.End = _p;
            return node;
        }

        private PsAst ParseFor(int start, int keywordEnd)
        {
            var node = new PsAst(PsKind.For, start);
            _p = keywordEnd;
            Skip(true);
            if (Expect('('))
            {
                for (int part = 0; part < 3; part++)
                {
                    Skip(true);
                    if (Peek() != ';' && Peek() != ')') node.Add(ParsePipelineChain());
                    Skip(true);
                    if (part < 2 && Peek() == ';') _p++;
                }
                Skip(true);
                Expect(')');
            }
            Skip(true);
            node.Add(ParseStatementBlock());
            node.End = _p;
            return node;
        }

        private PsAst ParseWhile(int start, int keywordEnd)
        {
            var node = new PsAst(PsKind.While, start);
            _p = keywordEnd;
            Skip(true);
            if (Expect('('))
            {
                Skip(true);
                if (Peek() != ')') node.Add(ParsePipelineChain());
                Skip(true);
                Expect(')');
            }
            Skip(true);
            node.Add(ParseStatementBlock());
            node.End = _p;
            return node;
        }

        private PsAst ParseDo(int start, int keywordEnd)
        {
            var node = new PsAst(PsKind.DoLoop, start);
            _p = keywordEnd;
            Skip(true);
            node.Add(ParseStatementBlock());
            Skip(true);
            string word = WordAt(_p, out int wordEnd);
            if (word == "while" || word == "until")
            {
                _p = wordEnd;
                Skip(true);
                if (Expect('('))
                {
                    Skip(true);
                    node.Add(ParsePipelineChain());
                    Skip(true);
                    Expect(')');
                }
            }
            else Fail();
            node.End = _p;
            return node;
        }

        private PsAst ParseFunction(int start, int keywordEnd)
        {
            var node = new PsAst(PsKind.FunctionDefinition, start);
            _p = keywordEnd;
            Skip(true);
            int nameStart = _p;
            while (_p < _n && !EndsGeneric(_s[_p])) _p++;
            if (_p == nameStart) Fail();
            node.Value = _s.Substring(nameStart, _p - nameStart);
            Skip(true);
            if (Peek() == '(')
            {
                _p++;
                ParseParameterList(node, ')');
                Expect(')');
                Skip(true);
            }
            if (Expect('{'))
            {
                node.Add(ParseScriptBlock(_p - 1, '}'));
                Expect('}');
            }
            node.End = _p;
            return node;
        }

        private void ParseParameterList(PsAst owner, char close)
        {
            while (true)
            {
                Skip(true);
                if (AtEnd || Peek() == close) return;
                var parameter = new PsAst(PsKind.Parameter, _p);
                while (Peek() == '[')
                {
                    parameter.Add(ParseAttributeOrType());
                    Skip(true);
                }
                var variable = Peek() == '$' ? TryScanVariable() : null;
                if (variable == null) Fail();
                else
                {
                    parameter.Child = variable;
                    parameter.Add(variable);
                }
                parameter.End = _p;
                Skip(true);
                if (Peek() == '=')
                {
                    _p++;
                    Skip(true);
                    var value = ParseExpression(true);
                    parameter.Add(value);
                    parameter.End = _p;
                    Skip(true);
                }
                owner.Add(parameter);
                if (Peek() == ',')
                {
                    _p++;
                    continue;
                }
                if (Peek() != close) Fail();
                return;
            }
        }

        // '[' starts an attribute ([Name(arguments)]) or a type ([Name], [Name[]], [List[string]]).
        private PsAst ParseAttributeOrType()
        {
            int start = _p;
            _p++;
            int nameStart = _p;
            int depth = 0;
            while (_p < _n)
            {
                char c = _s[_p];
                if (c == '[') depth++;
                else if (c == ']')
                {
                    if (depth == 0) break;
                    depth--;
                }
                else if (c == '(' && depth == 0) break;
                else if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '`' || c == '+' || c == ',' || c == ' ' || c == '\t'))
                    break;
                _p++;
            }
            string name = _s.Substring(nameStart, _p - nameStart).Trim();
            if (name.Length == 0) Fail();
            if (Peek() == '(')
            {
                var attribute = new PsAst(PsKind.Attribute, start) { Value = name };
                _p++;
                ParseAttributeArguments(attribute);
                Skip(true);
                Expect(')');
                Skip(false);
                Expect(']');
                attribute.End = _p;
                return attribute;
            }
            Expect(']');
            return new PsAst(PsKind.TypeConstraint, start) { Value = name, End = _p };
        }

        private void ParseAttributeArguments(PsAst attribute)
        {
            while (true)
            {
                Skip(true);
                if (AtEnd || Peek() == ')') return;
                int start = _p;
                if (char.IsLetter(Peek()) || Peek() == '_')
                {
                    int i = _p;
                    while (i < _n && (char.IsLetterOrDigit(_s[i]) || _s[i] == '_')) i++;
                    string name = _s.Substring(_p, i - _p);
                    _p = i;
                    Skip(true);
                    if (Peek() == '=')
                    {
                        var named = new PsAst(PsKind.NamedAttributeArgument, start) { Value = name };
                        _p++;
                        Skip(true);
                        named.Add(ParseExpression(true));
                        named.End = _p;
                        attribute.Add(named);
                    }
                    else if (Peek() == ',' || Peek() == ')')
                    {
                        attribute.Add(new PsAst(PsKind.NamedAttributeArgument, start) { Value = name, End = i });
                    }
                    else
                    {
                        _p = start;
                        attribute.Add(ParseExpression(true));
                    }
                }
                else attribute.Add(ParseExpression(true));
                Skip(true);
                if (Peek() == ',')
                {
                    _p++;
                    continue;
                }
                if (Peek() != ')') Fail();
                return;
            }
        }

        private PsAst ParseFlow(int start, int keywordEnd, string keyword, bool pipeline)
        {
            var node = new PsAst(PsKind.Flow, start) { Value = keyword };
            _p = keywordEnd;
            int save = _p;
            Skip(false);
            char c = Peek();
            if (AtEnd || IsNewline(c) || c == ';' || c == '}' || c == ')') _p = save;
            else if (pipeline) node.Add(ParsePipelineChain());
            else node.Add(ParseCommandArgument(false, true));
            node.End = _p;
            return node;
        }

        private PsAst ParseTrap(int start, int keywordEnd)
        {
            var node = new PsAst(PsKind.Trap, start);
            _p = keywordEnd;
            Skip(true);
            if (Peek() == '[')
            {
                node.Add(ParseAttributeOrType());
                Skip(true);
            }
            node.Add(ParseStatementBlock());
            node.End = _p;
            return node;
        }

        private PsAst ParseTry(int start, int keywordEnd)
        {
            var node = new PsAst(PsKind.Try, start);
            _p = keywordEnd;
            Skip(true);
            node.Add(ParseStatementBlock());
            bool clauses = false;
            while (true)
            {
                int save = _p;
                Skip(true);
                string word = WordAt(_p, out int wordEnd);
                if (word == "catch")
                {
                    clauses = true;
                    var clause = new PsAst(PsKind.Catch, _p);
                    _p = wordEnd;
                    while (true)
                    {
                        Skip(true);
                        if (Peek() != '[') break;
                        clause.Add(ParseAttributeOrType());
                        Skip(true);
                        if (Peek() != ',') break;
                        _p++;
                    }
                    Skip(true);
                    clause.Add(ParseStatementBlock());
                    clause.End = _p;
                    node.Add(clause);
                    continue;
                }
                if (word == "finally")
                {
                    clauses = true;
                    _p = wordEnd;
                    Skip(true);
                    node.Add(ParseStatementBlock());
                    break;
                }
                _p = save;
                break;
            }
            if (!clauses) Fail();
            node.End = _p;
            return node;
        }

        private PsAst ParseUsing()
        {
            var node = new PsAst(PsKind.Using, _p);
            WordAt(_p, out int end);
            _p = end;
            int last = _p;
            while (true)
            {
                Skip(false);
                if (AtCommandEnd()) break;
                int before = _p;
                node.Add(ParseCommandArgument(false, true));
                if (_p == before)
                {
                    Fail();
                    _p++;
                }
                last = _p;
            }
            _p = last;
            node.End = _p;
            return node;
        }

        // ---- pipelines and commands ----

        private PsAst ParsePipelineChain()
        {
            int start = _p;
            var left = ParsePipeline();
            while (true)
            {
                int save = _p;
                Skip(false);
                if ((Peek() == '&' && Peek(1) == '&') || (Peek() == '|' && Peek(1) == '|'))
                {
                    _p += 2;
                    Skip(true);
                    var chain = new PsAst(PsKind.PipelineChain, left?.Start ?? start);
                    chain.Add(left);
                    chain.Add(ParsePipeline());
                    chain.End = _p;
                    left = chain;
                    continue;
                }
                if (Peek() == '&')
                {
                    // A background job.
                    _p++;
                    break;
                }
                _p = save;
                break;
            }
            return left;
        }

        private PsAst ParsePipeline()
        {
            Skip(false);
            int start = _p;
            var pipeline = new PsAst(PsKind.Pipeline, start);
            PsAst first;
            if (IsInvocation()) first = ParseCommand();
            else if (IsExpressionStart())
            {
                var expression = ParseExpression(false);
                if (expression == null)
                {
                    Fail();
                    if (_p == start) _p++;
                    pipeline.End = _p;
                    return pipeline;
                }
                int save = _p;
                Skip(false);
                string op = AssignmentOperator();
                if (op != null)
                {
                    var assignment = new PsAst(PsKind.Assignment, expression.Start) { Value = op, Left = expression };
                    assignment.Add(expression);
                    _p += op.Length;
                    Skip(true);
                    var right = ParseStatement();
                    // PowerShell's right side is the expression itself when it is one.
                    if (right != null && right.Kind == PsKind.Pipeline && right.Items.Count == 1
                        && right.Items[0].Kind == PsKind.CommandExpression && right.Items[0].Children.Count == 1)
                        right = right.Items[0];
                    assignment.Right = right;
                    assignment.Add(right);
                    if (right == null) Fail();
                    assignment.End = right?.End ?? _p;
                    return assignment;
                }
                _p = save;
                first = WrapExpression(expression);
            }
            else first = ParseCommand();
            pipeline.AddItem(first);
            int end = _p;
            while (true)
            {
                Skip(false);
                if (!(Peek() == '|' && Peek(1) != '|'))
                {
                    // A line that starts with '|' continues the pipeline.
                    Skip(true);
                    if (!(Peek() == '|' && Peek(1) != '|')) break;
                }
                _p++;
                Skip(true);
                PsAst element;
                if (IsInvocation()) element = ParseCommand();
                else if (IsExpressionStart())
                {
                    // Expressions are only allowed as the first element of a pipeline.
                    Fail();
                    var expression = ParseExpression(false);
                    element = expression == null ? null : WrapExpression(expression);
                }
                else element = ParseCommand();
                if (element == null)
                {
                    Fail();
                    break;
                }
                pipeline.AddItem(element);
                end = _p;
            }
            _p = end;
            pipeline.End = end;
            return pipeline;
        }

        private PsAst WrapExpression(PsAst expression)
        {
            var node = new PsAst(PsKind.CommandExpression, expression.Start) { Child = expression };
            node.Add(expression);
            node.End = expression.End;
            while (true)
            {
                int save = _p;
                Skip(false);
                if (!IsRedirection())
                {
                    _p = save;
                    break;
                }
                node.Add(ParseRedirection());
                node.End = _p;
            }
            return node;
        }

        private string AssignmentOperator()
        {
            char c = Peek(), n = Peek(1);
            if (c == '=') return "=";
            if (n == '=')
            {
                if (c == '+' || c == '*' || c == '/' || c == '%') return c + "=";
                if (IsDash(c)) return "-=";
            }
            if (c == '?' && n == '?' && Peek(2) == '=') return "??=";
            return null;
        }

        private bool IsInvocation()
        {
            char c = Peek(), n = Peek(1);
            if (c == '&') return n != '&';
            if (c == '.')
                return IsBlank(n) || n == '$' || n == '(' || n == '{' || IsSingleQuote(n) || IsDoubleQuote(n) || (n == '`' && IsNewline(Peek(2)));
            return false;
        }

        // Whether a pipeline element at the current position is an expression (else a command).
        private bool IsExpressionStart()
        {
            char c = Peek(), n = Peek(1);
            if (c == '$') return n == '(' || n == '{' || IsVarChar(n) || n == '$' || n == '^';
            if (c == '@') return n == '(' || n == '{' || IsSingleQuote(n) || IsDoubleQuote(n);
            if (IsSingleQuote(c) || IsDoubleQuote(c) || c == '(' || c == '{' || c == '[' || c == ',' || c == '!') return true;
            if (char.IsDigit(c) || (c == '.' && char.IsDigit(n))) return NumberLiteral.IsMatch(NumberRun(_p));
            if (c == '+') return n == '+' || char.IsDigit(n) || n == '$' || n == '(';
            if (IsDash(c))
            {
                if (IsDash(n)) return Peek(2) == '$';
                if (char.IsDigit(n) || n == '.' || n == '$' || n == '(') return true;
                if (char.IsLetter(n)) return UnaryWords.Contains(LettersAt(_p + 1));
            }
            return false;
        }

        private string NumberRun(int position)
        {
            int i = position;
            while (i < _n)
            {
                char c = _s[i];
                if (char.IsLetterOrDigit(c) || c == '_') i++;
                else if (c == '.' && i + 1 < _n && char.IsDigit(_s[i + 1])) i++;
                else break;
            }
            return _s.Substring(position, i - position);
        }

        private PsAst ParseCommand()
        {
            int start = _p;
            var command = new PsAst(PsKind.Command, start);
            if (IsInvocation())
            {
                command.Invocation = Peek();
                _p++;
            }
            var redirections = new List<PsAst>();
            bool endOfParameters = false;
            int end = _p;
            while (true)
            {
                Skip(false);
                if (AtCommandEnd()) break;
                int before = _p;
                if (IsRedirection())
                {
                    redirections.Add(ParseRedirection());
                    end = _p;
                    continue;
                }
                if (Peek() == '-' && Peek(1) == '-' && Peek(2) == '%' && EndsGeneric(Peek(3)))
                {
                    // Stop parsing: the rest of the line is one verbatim argument.
                    _p += 3;
                    while (_p < _n && IsBlank(_s[_p])) _p++;
                    int verbatim = _p;
                    while (_p < _n && !IsNewline(_s[_p])) _p++;
                    command.AddItem(new PsAst(PsKind.StringConstant, verbatim) { Value = _s.Substring(verbatim, _p - verbatim), End = _p });
                    end = _p;
                    break;
                }
                bool first = command.Items.Count == 0 && command.Invocation == '\0';
                var element = ParseCommandArgument(first, endOfParameters || first);
                if (element == null || _p == before)
                {
                    Fail();
                    if (_p == before) _p++;
                    end = _p;
                    continue;
                }
                if (element.Kind == PsKind.CommandParameter && element.Value == "-" && element.Child == null) endOfParameters = true;
                command.AddItem(element);
                end = _p;
            }
            _p = end;
            foreach (var redirection in redirections) command.Add(redirection);
            if (command.Items.Count == 0)
            {
                Fail();
                return null;
            }
            command.End = end;
            return command;
        }

        private bool IsRedirection()
        {
            char c = Peek(), n = Peek(1);
            if (c == '>' || c == '<') return true;
            return (c == '*' || (c >= '1' && c <= '6')) && n == '>';
        }

        private PsAst ParseRedirection()
        {
            var node = new PsAst(PsKind.Redirection, _p);
            if (Peek() == '<')
            {
                // Reserved for future use.
                Fail();
                _p++;
                node.End = _p;
                return node;
            }
            if (Peek() != '>') _p++;
            _p++;
            if (Peek() == '>') _p++;
            else if (Peek() == '&' && Peek(1) >= '1' && Peek(1) <= '6')
            {
                _p += 2;
                node.End = _p;
                return node;
            }
            Skip(false);
            var target = ParseCommandArgument(false, true);
            if (target == null) Fail();
            else
            {
                node.Child = target;
                node.Add(target);
            }
            node.End = _p;
            return node;
        }

        // A command element, with a comma-separated array of them (a, b) when it is not the command name.
        private PsAst ParseCommandArgument(bool first, bool noParameters)
        {
            var element = ParseCommandPrimary(first, noParameters);
            if (first || element == null || element.Kind == PsKind.CommandParameter) return element;
            int end = _p;
            Skip(false);
            if (Peek() != ',')
            {
                _p = end;
                return element;
            }
            var array = new PsAst(PsKind.ArrayLiteral, element.Start);
            array.AddItem(element);
            while (Peek() == ',')
            {
                _p++;
                Skip(true);
                var next = ParseCommandPrimary(false, true);
                if (next == null)
                {
                    Fail();
                    end = _p;
                    break;
                }
                array.AddItem(next);
                end = _p;
                Skip(false);
            }
            _p = end;
            array.End = end;
            return array;
        }

        private PsAst ParseCommandPrimary(bool first, bool noParameters)
        {
            Enter();
            try
            {
                if (AtCommandEnd()) return null;
                int start = _p;
                char c = Peek(), n = Peek(1);
                if (IsDash(c) && !noParameters) return ParseParameterOrGeneric();
                if (IsSingleQuote(c)) return ParsePostfix(ScanSingleQuoted(), true);
                if (IsDoubleQuote(c)) return ParsePostfix(ScanDoubleQuoted(), true);
                if (c == '@')
                {
                    if ((IsSingleQuote(n) || IsDoubleQuote(n)) && IsHereStringOpen()) return ParsePostfix(ScanHereString(), true);
                    if (n == '(') return ParsePostfix(ParseArrayExpression(), true);
                    if (n == '{') return ParsePostfix(ParseHashtable(), true);
                    if (IsVarChar(n)) return ScanSplat();
                    return ScanGeneric();
                }
                if (c == '$')
                {
                    if (n == '(') return ParsePostfix(ParseSubExpression(), true);
                    var variable = TryScanVariable();
                    if (variable == null) return ScanGeneric();
                    char after = Peek();
                    if (after == '.' || after == '[' || (after == ':' && Peek(1) == ':')) return ParsePostfix(variable, true);
                    if (EndsGeneric(after)) return variable;
                    // $PSScriptRoot/lib/x.ps1 is one bare word with a nested variable.
                    _p = start;
                    return ScanGeneric();
                }
                if (c == '(') return ParsePostfix(ParseParen(), true);
                if (c == '{') return ParsePostfix(ParseScriptBlockExpression(), true);
                if (c == ',')
                {
                    // A missing argument.
                    Fail();
                    _p++;
                    return null;
                }
                return ScanGeneric();
            }
            finally
            {
                Leave();
            }
        }

        private PsAst ParseParameterOrGeneric()
        {
            int start = _p;
            char n = Peek(1);
            if (IsDash(n))
            {
                if (!EndsGeneric(Peek(2))) return ScanGeneric();
                _p += 2;
                return new PsAst(PsKind.CommandParameter, start) { Value = "-", End = _p };
            }
            if (!(char.IsLetter(n) || n == '_' || n == '?')) return ScanGeneric();
            int i = _p + 1;
            while (i < _n)
            {
                char c = _s[i];
                if (EndsGeneric(c) || c == ':' || c == '.' || c == '[') break;
                if (IsSingleQuote(c) || IsDoubleQuote(c) || c == '$' || c == '`') return ScanGeneric();
                i++;
            }
            var parameter = new PsAst(PsKind.CommandParameter, start) { Value = _s.Substring(start + 1, i - start - 1) };
            _p = i;
            if (Peek() == ':')
            {
                _p++;
                Skip(false);
                var argument = ParseCommandArgument(false, true);
                if (argument == null) Fail();
                else
                {
                    parameter.Child = argument;
                    parameter.Add(argument);
                }
            }
            parameter.End = _p;
            return parameter;
        }

        // A command-mode token: runs to a blank or one of {}();,|&, drops its quotes and keeps nested $ expressions.
        private PsAst ScanGeneric()
        {
            int start = _p;
            var value = new StringBuilder();
            var nested = new List<PsAst>();
            bool plain = true;
            while (_p < _n)
            {
                char c = _s[_p];
                if (EndsGeneric(c)) break;
                if (c == '`')
                {
                    if (_p + 1 >= _n)
                    {
                        _p++;
                        plain = false;
                        break;
                    }
                    if (IsNewline(_s[_p + 1])) break;
                    AppendEscape(value);
                    plain = false;
                    continue;
                }
                if (IsSingleQuote(c))
                {
                    value.Append(ScanSingleQuotedBody());
                    plain = false;
                    continue;
                }
                if (IsDoubleQuote(c))
                {
                    ScanDoubleQuotedBody(value, nested);
                    plain = false;
                    continue;
                }
                if (c == '$')
                {
                    int at = _p;
                    var inner = Peek(1) == '(' ? ParseSubExpression() : TryScanVariable();
                    if (inner != null)
                    {
                        nested.Add(inner);
                        value.Append(_s, at, _p - at);
                        continue;
                    }
                }
                value.Append(c);
                _p++;
            }
            if (_p == start)
            {
                Fail();
                _p++;
                return new PsAst(PsKind.StringConstant, start) { Value = _s.Substring(start, 1), End = _p };
            }
            if (nested.Count > 0)
            {
                var expandable = new PsAst(PsKind.ExpandableString, start) { Value = value.ToString(), End = _p };
                foreach (var inner in nested) expandable.AddItem(inner);
                return expandable;
            }
            string text = value.ToString();
            var kind = plain && NumberLiteral.IsMatch(text) ? PsKind.Constant : PsKind.StringConstant;
            return new PsAst(kind, start) { Value = text, End = _p };
        }

        // ---- strings and variables ----

        private void AppendEscape(StringBuilder value)
        {
            // At a backtick with a character after it.
            char e = _s[_p + 1];
            _p += 2;
            switch (e)
            {
                case '0': value.Append('\0'); return;
                case 'a': value.Append('\a'); return;
                case 'b': value.Append('\b'); return;
                case 'e': value.Append('\u001b'); return;
                case 'f': value.Append('\f'); return;
                case 'n': value.Append('\n'); return;
                case 'r': value.Append('\r'); return;
                case 't': value.Append('\t'); return;
                case 'v': value.Append('\v'); return;
                case 'u':
                    if (Peek() == '{')
                    {
                        int close = _s.IndexOf('}', _p);
                        if (close > _p && int.TryParse(_s.Substring(_p + 1, close - _p - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)
                            && code >= 0 && code <= 0x10ffff)
                        {
                            value.Append(char.ConvertFromUtf32(code >= 0xd800 && code <= 0xdfff ? 0xfffd : code));
                            _p = close + 1;
                            return;
                        }
                        Fail();
                    }
                    value.Append('u');
                    return;
                default: value.Append(e); return;
            }
        }

        private PsAst ScanSingleQuoted()
        {
            int start = _p;
            string body = ScanSingleQuotedBody();
            return new PsAst(PsKind.StringConstant, start) { Value = body, End = _p };
        }

        private string ScanSingleQuotedBody()
        {
            var value = new StringBuilder();
            _p++;
            while (true)
            {
                if (_p >= _n)
                {
                    Fail();
                    return value.ToString();
                }
                char c = _s[_p];
                if (IsSingleQuote(c))
                {
                    if (_p + 1 < _n && IsSingleQuote(_s[_p + 1]))
                    {
                        value.Append(c);
                        _p += 2;
                        continue;
                    }
                    _p++;
                    return value.ToString();
                }
                value.Append(c);
                _p++;
            }
        }

        private PsAst ScanDoubleQuoted()
        {
            int start = _p;
            var value = new StringBuilder();
            var nested = new List<PsAst>();
            ScanDoubleQuotedBody(value, nested);
            if (nested.Count == 0) return new PsAst(PsKind.StringConstant, start) { Value = value.ToString(), End = _p };
            var node = new PsAst(PsKind.ExpandableString, start) { Value = value.ToString(), End = _p };
            foreach (var inner in nested) node.AddItem(inner);
            return node;
        }

        private void ScanDoubleQuotedBody(StringBuilder value, List<PsAst> nested)
        {
            _p++;
            while (true)
            {
                if (_p >= _n)
                {
                    Fail();
                    return;
                }
                char c = _s[_p];
                if (c == '`')
                {
                    if (_p + 1 >= _n)
                    {
                        _p++;
                        Fail();
                        return;
                    }
                    AppendEscape(value);
                    continue;
                }
                if (IsDoubleQuote(c))
                {
                    if (_p + 1 < _n && IsDoubleQuote(_s[_p + 1]))
                    {
                        value.Append(c);
                        _p += 2;
                        continue;
                    }
                    _p++;
                    return;
                }
                if (c == '$')
                {
                    int at = _p;
                    var inner = Peek(1) == '(' ? ParseSubExpression() : TryScanVariable();
                    if (inner != null)
                    {
                        nested.Add(inner);
                        value.Append(_s, at, _p - at);
                        continue;
                    }
                }
                value.Append(c);
                _p++;
            }
        }

        // '@' and a quote, then only blanks to the end of the line.
        private bool IsHereStringOpen()
        {
            int i = _p + 2;
            while (i < _n && IsBlank(_s[i])) i++;
            return i < _n && IsNewline(_s[i]);
        }

        private PsAst ScanHereString()
        {
            int start = _p;
            char quote = _s[_p + 1];
            bool expandable = IsDoubleQuote(quote);
            int i = _p + 2;
            while (i < _n && IsBlank(_s[i])) i++;
            if (i < _n && _s[i] == '\r') i++;
            if (i < _n && _s[i] == '\n') i++;
            int contentStart = i;
            // The terminator is the quote and '@' at the start of a line.
            int close = -1;
            for (int j = contentStart; j + 1 < _n; j++)
            {
                if (_s[j + 1] == '@' && (expandable ? IsDoubleQuote(_s[j]) : IsSingleQuote(_s[j])) && IsNewline(_s[j - 1]))
                {
                    close = j;
                    break;
                }
            }
            int contentEnd;
            if (close < 0)
            {
                Fail();
                contentEnd = _n;
            }
            else
            {
                contentEnd = close;
                if (contentEnd > contentStart && _s[contentEnd - 1] == '\n') contentEnd--;
                if (contentEnd > contentStart && _s[contentEnd - 1] == '\r') contentEnd--;
            }
            var nested = new List<PsAst>();
            string value;
            if (expandable)
            {
                var text = new StringBuilder();
                _p = contentStart;
                while (_p < contentEnd)
                {
                    char c = _s[_p];
                    if (c == '`' && _p + 1 < contentEnd)
                    {
                        AppendEscape(text);
                        continue;
                    }
                    if (c == '$')
                    {
                        int at = _p;
                        var inner = Peek(1) == '(' ? ParseSubExpression() : TryScanVariable();
                        if (inner != null)
                        {
                            nested.Add(inner);
                            text.Append(_s, at, _p - at);
                            continue;
                        }
                    }
                    text.Append(c);
                    _p++;
                }
                value = text.ToString();
            }
            else value = _s.Substring(contentStart, contentEnd - contentStart);
            _p = close < 0 ? _n : Math.Max(_p, close + 2);
            if (nested.Count == 0) return new PsAst(PsKind.StringConstant, start) { Value = value, End = _p };
            var node = new PsAst(PsKind.ExpandableString, start) { Value = value, End = _p };
            foreach (var inner in nested) node.AddItem(inner);
            return node;
        }

        // At '$': ${name}, a special variable ($$, $^, $?), or a name with an optional scope or drive (env:PATH).
        private PsAst TryScanVariable()
        {
            int start = _p;
            char n = Peek(1);
            string name;
            int end;
            if (n == '{')
            {
                var text = new StringBuilder();
                int i = _p + 2;
                while (i < _n && _s[i] != '}')
                {
                    if (_s[i] == '`' && i + 1 < _n)
                    {
                        text.Append(_s[i + 1]);
                        i += 2;
                        continue;
                    }
                    text.Append(_s[i]);
                    i++;
                }
                if (i >= _n)
                {
                    Fail();
                    end = _n;
                }
                else end = i + 1;
                name = text.ToString();
            }
            else if (n == '$' || n == '^' || n == '?')
            {
                name = n.ToString();
                end = _p + 2;
            }
            else if (IsVarChar(n))
            {
                int i = _p + 1;
                while (i < _n && IsVarChar(_s[i])) i++;
                if (i + 1 < _n && _s[i] == ':' && IsVarChar(_s[i + 1]))
                {
                    i++;
                    while (i < _n && IsVarChar(_s[i])) i++;
                }
                name = _s.Substring(_p + 1, i - _p - 1);
                end = i;
            }
            else return null;
            _p = end;
            return new PsAst(PsKind.Variable, start) { Value = name, End = end };
        }

        private PsAst ScanSplat()
        {
            int start = _p;
            int i = _p + 1;
            while (i < _n && IsVarChar(_s[i])) i++;
            _p = i;
            return new PsAst(PsKind.Variable, start) { Value = _s.Substring(start + 1, i - start - 1), Splatted = true, End = i };
        }

        // ---- grouping ----

        private PsAst ParseSubExpression()
        {
            int start = _p;
            _p += 2;
            var block = new PsAst(PsKind.StatementBlock, _p);
            ParseStatementList(block, ')');
            block.End = _p;
            Expect(')');
            var node = new PsAst(PsKind.SubExpression, start) { Child = block };
            node.Add(block);
            node.End = _p;
            return node;
        }

        private PsAst ParseArrayExpression()
        {
            int start = _p;
            _p += 2;
            var block = new PsAst(PsKind.StatementBlock, _p);
            ParseStatementList(block, ')');
            block.End = _p;
            Expect(')');
            var node = new PsAst(PsKind.ArrayExpression, start) { Child = block };
            node.Add(block);
            node.End = _p;
            return node;
        }

        private PsAst ParseParen()
        {
            var node = new PsAst(PsKind.Paren, _p);
            _p++;
            Skip(true);
            if (Peek() == ')') Fail();
            else
            {
                var inner = ParsePipelineChain();
                node.Child = inner;
                node.Add(inner);
                Skip(true);
            }
            Expect(')');
            node.End = _p;
            return node;
        }

        private PsAst ParseScriptBlockExpression()
        {
            var node = new PsAst(PsKind.ScriptBlockExpression, _p);
            _p++;
            var block = ParseScriptBlock(_p, '}');
            Expect('}');
            node.Child = block;
            node.Add(block);
            node.End = _p;
            return node;
        }

        private PsAst ParseHashtable()
        {
            var node = new PsAst(PsKind.Hashtable, _p);
            _p += 2;
            while (true)
            {
                SkipSeparators();
                if (AtEnd)
                {
                    Fail();
                    break;
                }
                if (Peek() == '}')
                {
                    _p++;
                    break;
                }
                int before = _p;
                node.Add(ParseHashKey());
                Skip(false);
                if (!Expect('='))
                {
                    if (_p == before) _p++;
                    continue;
                }
                Skip(true);
                node.Add(ParseStatement());
                int after = _p;
                Skip(false);
                char c = Peek();
                if (!(AtEnd || IsNewline(c) || c == ';' || c == '}')) Fail();
                _p = after;
                if (_p == before)
                {
                    Fail();
                    _p++;
                }
            }
            node.End = _p;
            return node;
        }

        private PsAst ParseHashKey()
        {
            char c = Peek();
            if (IsSingleQuote(c)) return ScanSingleQuoted();
            if (IsDoubleQuote(c)) return ScanDoubleQuoted();
            if (c == '$') return ParsePostfix(Peek(1) == '(' ? ParseSubExpression() : TryScanVariable(), false);
            if (c == '(') return ParseParen();
            if (char.IsDigit(c)) return ScanNumber();
            int start = _p;
            while (_p < _n && !IsBlank(_s[_p]) && !IsNewline(_s[_p]) && "=;}".IndexOf(_s[_p]) < 0) _p++;
            if (_p == start)
            {
                Fail();
                return null;
            }
            return new PsAst(PsKind.StringConstant, start) { Value = _s.Substring(start, _p - start), End = _p };
        }

        // ---- expressions ----

        private PsAst ParseExpression(bool noComma)
        {
            Enter();
            try
            {
                var condition = ParseBinary(Logical, noComma);
                if (condition == null) return null;
                int save = _p;
                Skip(false);
                if (_p > save && Peek() == '?' && Peek(1) != '?' && Peek(1) != '.' && Peek(1) != '[')
                {
                    var ternary = new PsAst(PsKind.Ternary, condition.Start);
                    ternary.Add(condition);
                    _p++;
                    Skip(true);
                    ternary.Add(ParseExpression(noComma));
                    Skip(true);
                    if (Expect(':'))
                    {
                        Skip(true);
                        ternary.Add(ParseExpression(noComma));
                    }
                    ternary.End = _p;
                    return ternary;
                }
                _p = save;
                return condition;
            }
            finally
            {
                Leave();
            }
        }

        private PsAst ParseBinary(int minLevel, bool noComma)
        {
            var left = ParseArrayLiteral(noComma);
            if (left == null) return null;
            while (true)
            {
                int save = _p;
                Skip(false);
                string op = BinaryOperator(out int level, out int length);
                if (op == null || level < minLevel)
                {
                    _p = save;
                    return left;
                }
                _p += length;
                Skip(true);
                var right = ParseBinary(level + 1, noComma);
                if (right == null)
                {
                    Fail();
                    return left;
                }
                var binary = new PsAst(PsKind.Binary, left.Start) { Value = op, Left = left, Right = right };
                binary.Add(left);
                binary.Add(right);
                binary.End = right.End;
                left = binary;
            }
        }

        private string BinaryOperator(out int level, out int length)
        {
            char c = Peek(), n = Peek(1);
            level = 0;
            length = 1;
            if (c == '+' && n != '+' && n != '=')
            {
                level = Additive;
                return "+";
            }
            if (IsDash(c))
            {
                if (char.IsLetter(n))
                {
                    string word = LettersAt(_p + 1);
                    if (!DashOperators.TryGetValue(word, out level)) return null;
                    length = 1 + word.Length;
                    return "-" + word;
                }
                if (n == '=' || IsDash(n)) return null;
                level = Additive;
                return "-";
            }
            if ((c == '*' || c == '/' || c == '%') && n != '=')
            {
                level = Multiplicative;
                return c.ToString();
            }
            if (c == '.' && n == '.')
            {
                level = Range;
                length = 2;
                return "..";
            }
            if (c == '?' && n == '?' && Peek(2) != '=')
            {
                level = Coalesce;
                length = 2;
                return "??";
            }
            return null;
        }

        private PsAst ParseArrayLiteral(bool noComma)
        {
            var first = ParseUnary();
            if (first == null || noComma) return first;
            int end = _p;
            Skip(false);
            if (Peek() != ',')
            {
                _p = end;
                return first;
            }
            var array = new PsAst(PsKind.ArrayLiteral, first.Start);
            array.AddItem(first);
            while (Peek() == ',')
            {
                _p++;
                Skip(true);
                var next = ParseUnary();
                if (next == null)
                {
                    Fail();
                    end = _p;
                    break;
                }
                array.AddItem(next);
                end = _p;
                Skip(false);
            }
            _p = end;
            array.End = end;
            return array;
        }

        private PsAst ParseUnary()
        {
            Enter();
            try
            {
                int start = _p;
                char c = Peek(), n = Peek(1);
                if (c == '!') return Unary("!", start, 1);
                if (c == ',') return Unary(",", start, 1);
                if (c == '+') return Unary(n == '+' ? "++" : "+", start, n == '+' ? 2 : 1);
                if (IsDash(c))
                {
                    if (IsDash(n)) return Unary("--", start, 2);
                    if (char.IsLetter(n))
                    {
                        string word = LettersAt(_p + 1);
                        if (UnaryWords.Contains(word)) return Unary("-" + word, start, 1 + word.Length);
                        // An operator or parameter where an operand belongs.
                        Fail();
                        return ScanGeneric();
                    }
                    return Unary("-", start, 1);
                }
                if (c == '[')
                {
                    var type = ParseAttributeOrType();
                    if (type.Kind == PsKind.Attribute)
                    {
                        Skip(true);
                        var operand = ParseUnary();
                        var attributed = new PsAst(PsKind.Attributed, start);
                        attributed.Add(type);
                        attributed.Add(operand);
                        if (operand == null) Fail();
                        attributed.End = _p;
                        return attributed;
                    }
                    if (Peek() == '.' || (Peek() == ':' && Peek(1) == ':')) return ParsePostfix(TypeLiteral(type), false);
                    int save = _p;
                    Skip(false);
                    if (IsCastOperandStart())
                    {
                        var operand = ParseUnary();
                        var convert = new PsAst(PsKind.Convert, start) { Left = type, Child = operand };
                        convert.Add(type);
                        convert.Add(operand);
                        if (operand == null) Fail();
                        convert.End = operand?.End ?? _p;
                        return convert;
                    }
                    _p = save;
                    return ParsePostfix(TypeLiteral(type), false);
                }
                return ParsePostfix(ParsePrimary(), false);
            }
            finally
            {
                Leave();
            }
        }

        private PsAst Unary(string op, int start, int length)
        {
            _p += length;
            Skip(true);
            var operand = ParseUnary();
            var node = new PsAst(PsKind.Unary, start) { Value = op, Child = operand };
            node.Add(operand);
            if (operand == null) Fail();
            node.End = operand?.End ?? _p;
            return node;
        }

        private static PsAst TypeLiteral(PsAst type) =>
            new PsAst(PsKind.TypeExpression, type.Start) { Value = type.Value, End = type.End };

        private bool IsCastOperandStart()
        {
            char c = Peek(), n = Peek(1);
            if (c == '$' || c == '(' || c == '[' || c == '{' || c == '!' || IsSingleQuote(c) || IsDoubleQuote(c) || char.IsDigit(c)) return true;
            if (c == '@') return n == '(' || n == '{' || IsSingleQuote(n) || IsDoubleQuote(n);
            if (c == '.') return char.IsDigit(n);
            if (IsDash(c))
            {
                if (char.IsDigit(n) || n == '$' || n == '(' || n == '.') return true;
                if (char.IsLetter(n)) return UnaryWords.Contains(LettersAt(_p + 1));
            }
            return false;
        }

        private PsAst ParsePrimary()
        {
            char c = Peek(), n = Peek(1);
            if (c == '$')
            {
                if (n == '(') return ParseSubExpression();
                var variable = TryScanVariable();
                if (variable != null) return variable;
            }
            else if (c == '@')
            {
                if (n == '(') return ParseArrayExpression();
                if (n == '{') return ParseHashtable();
                if ((IsSingleQuote(n) || IsDoubleQuote(n)) && IsHereStringOpen()) return ScanHereString();
                if (IsVarChar(n)) return ScanSplat();
            }
            else if (IsSingleQuote(c)) return ScanSingleQuoted();
            else if (IsDoubleQuote(c)) return ScanDoubleQuoted();
            else if (c == '(') return ParseParen();
            else if (c == '{') return ParseScriptBlockExpression();
            else if (char.IsDigit(c) || (c == '.' && char.IsDigit(n))) return ScanNumber();
            // Nothing an expression can start with. Like PowerShell's recovery, the token is left to start the next
            // statement.
            Fail();
            return null;
        }

        private PsAst ScanNumber()
        {
            int start = _p;
            _p += NumberRun(_p).Length;
            return new PsAst(PsKind.Constant, start) { Value = _s.Substring(start, _p - start), End = _p };
        }

        // Member access (.name, ::name, ?.name), calls (.name(args), .name{block}), indexes ([i], ?[i]) and, in
        // expression mode, postfix ++ and --. Each must touch what it follows.
        private PsAst ParsePostfix(PsAst node, bool commandMode)
        {
            if (node == null) return null;
            while (true)
            {
                char c = Peek(), n = Peek(1);
                int skip;
                bool isStatic = false;
                if (c == '.' && n != '.') skip = 1;
                else if (c == '?' && n == '.') skip = 2;
                else if (c == ':' && n == ':')
                {
                    skip = 2;
                    isStatic = true;
                }
                else if (c == '[' || (c == '?' && n == '['))
                {
                    var index = new PsAst(PsKind.Index, node.Start);
                    index.Add(node);
                    _p += c == '?' ? 2 : 1;
                    Skip(true);
                    index.Add(ParseExpression(false));
                    Skip(true);
                    Expect(']');
                    index.End = _p;
                    node = index;
                    continue;
                }
                else if (!commandMode && ((c == '+' && n == '+') || (IsDash(c) && IsDash(n))))
                {
                    var unary = new PsAst(PsKind.Unary, node.Start) { Value = c == '+' ? "++" : "--", Child = node };
                    unary.Add(node);
                    _p += 2;
                    unary.End = _p;
                    node = unary;
                    continue;
                }
                else return node;
                int save = _p;
                _p += skip;
                var member = ParseMemberName();
                if (member == null)
                {
                    _p = save;
                    return node;
                }
                if (Peek() == '(' || Peek() == '{')
                {
                    var invoke = new PsAst(PsKind.InvokeMember, node.Start) { Static = isStatic, Left = node, Right = member };
                    invoke.Add(node);
                    invoke.Add(member);
                    if (Peek() == '(')
                    {
                        _p++;
                        ParseInvokeArguments(invoke);
                        Expect(')');
                    }
                    else invoke.AddItem(ParseScriptBlockExpression());
                    invoke.End = _p;
                    node = invoke;
                    continue;
                }
                var access = new PsAst(PsKind.Member, node.Start) { Static = isStatic, Left = node, Right = member };
                access.Add(node);
                access.Add(member);
                access.End = _p;
                node = access;
            }
        }

        private PsAst ParseMemberName()
        {
            char c = Peek();
            int start = _p;
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                while (_p < _n && (char.IsLetterOrDigit(_s[_p]) || _s[_p] == '_')) _p++;
                return new PsAst(PsKind.StringConstant, start) { Value = _s.Substring(start, _p - start), End = _p };
            }
            if (IsSingleQuote(c)) return ScanSingleQuoted();
            if (IsDoubleQuote(c)) return ScanDoubleQuoted();
            if (c == '$') return Peek(1) == '(' ? ParseSubExpression() : TryScanVariable();
            if (c == '(') return ParseParen();
            return null;
        }

        private void ParseInvokeArguments(PsAst invoke)
        {
            Skip(true);
            if (Peek() == ')') return;
            while (true)
            {
                Skip(true);
                var argument = ParseExpression(true);
                if (argument == null) return;
                invoke.AddItem(argument);
                Skip(true);
                if (Peek() != ',') return;
                _p++;
            }
        }
    }
}
