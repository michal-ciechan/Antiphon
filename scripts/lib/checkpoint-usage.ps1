. (Join-Path $PSScriptRoot 'checkpoint-file-reader.ps1')
# CARD-0804 usage acceptance: native allocated-byte sampling and the roster/TRX/event gates.
# Dot-sourced by scripts/verify-checkpoint-temp-usage.ps1 and by CheckpointTempUsageTests, which
# drive these functions with supplied inputs. ASCII only.

function Get-Allocated([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return [long]0 }
    if ($IsLinux) {
        for ($attempt = 0; $attempt -lt 5; $attempt++) {
            $line = & du -s -B1 -- $path 2>&1
            if ($LASTEXITCODE -eq 0) { return [long]([string]$line).Split([char]9)[0] }
            if (-not (Test-Path -LiteralPath $path)) { return [long]0 }
            # A test may remove a descendant after du discovers it. Only accept a
            # later complete sample; persistent errors and every other errno stay red.
            if ($attempt -lt 4 -and ([string]($line -join ' ')) -match 'cannot access .+: No such file or directory') {
                Start-Sleep -Milliseconds 20
                continue
            }
            throw "Allocated-byte sample failed for $path : $line"
        }
    }
    if ($IsWindows) { return Get-WindowsAllocated $path }
    throw 'Native allocated-byte sampling is not implemented for this OS.'
}

# Windows allocated bytes: per file, GetCompressedFileSizeW (compressed/sparse aware) rounded up to the
# volume cluster size. Reparse points, the sampled root included, are counted as zero and never followed,
# so a link cannot inflate or hide a root. Only a path that vanished while sampling is skipped; any other
# listing or size error fails the sample instead of reading as zero usage.
if ($IsWindows) {
    if (-not ('C804.NativeAlloc' -as [type])) {
        Add-Type -Namespace C804 -Name NativeAlloc -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
private static extern uint GetCompressedFileSizeW(string name, out uint high);
[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
public static extern bool GetDiskFreeSpaceW(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);
public static long CompressedSize(string name, out int error)
{
    uint high;
    uint low = GetCompressedFileSizeW(name, out high);
    error = low == uint.MaxValue ? System.Runtime.InteropServices.Marshal.GetLastWin32Error() : 0;
    return error == 0 ? ((long)high << 32) + low : -1;
}
'@
    }
    $script:clusterBytes = @{}
}

function Get-ClusterBytes([string]$path) {
    $root = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($path))
    if (-not $script:clusterBytes.ContainsKey($root)) {
        [uint32]$spc = 0; [uint32]$bps = 0; [uint32]$free = 0; [uint32]$total = 0
        if (-not [C804.NativeAlloc]::GetDiskFreeSpaceW($root, [ref]$spc, [ref]$bps, [ref]$free, [ref]$total)) {
            throw "Cluster size probe failed for $root"
        }
        $script:clusterBytes[$root] = [long]$spc * [long]$bps
    }
    return $script:clusterBytes[$root]
}

function Get-WindowsFileAllocated([string]$file, [long]$cluster) {
    [int]$err = 0
    $size = [C804.NativeAlloc]::CompressedSize('\\?\' + $file, [ref]$err)
    # ERROR_FILE_NOT_FOUND / ERROR_PATH_NOT_FOUND: removed while sampling.
    if ($err -eq 2 -or $err -eq 3) { return [long]0 }
    if ($err -ne 0) { throw "Allocated-byte sample failed for $file : Win32 error $err" }
    return [long]([Math]::Ceiling($size / $cluster) * $cluster)
}

function Get-WindowsAllocated([string]$path) {
    try { $attributes = [IO.File]::GetAttributes($path) }
    catch [IO.FileNotFoundException] { return [long]0 }
    catch [IO.DirectoryNotFoundException] { return [long]0 }
    if ($attributes -band [IO.FileAttributes]::ReparsePoint) { return [long]0 }
    $cluster = Get-ClusterBytes $path
    if (-not ($attributes -band [IO.FileAttributes]::Directory)) {
        return Get-WindowsFileAllocated ([IO.Path]::GetFullPath($path)) $cluster
    }
    [long]$total = 0
    $stack = [Collections.Generic.Stack[string]]::new()
    $stack.Push([IO.Path]::GetFullPath($path))
    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        try { $entries = [IO.DirectoryInfo]::new($dir).GetFileSystemInfos() }
        catch [IO.DirectoryNotFoundException] { continue }
        catch { throw "Allocated-byte sample failed for $dir : $($_.Exception.Message)" }
        foreach ($entry in $entries) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
            if ($entry.Attributes -band [IO.FileAttributes]::Directory) { $stack.Push($entry.FullName); continue }
            $total += Get-WindowsFileAllocated $entry.FullName $cluster
        }
    }
    return $total
}

function Get-Snapshot([string]$temp) {
    [long]$bytes = 0
    $roots = @()
    foreach ($entry in Get-ChildItem -LiteralPath $temp -Directory -Force) {
        if ($entry.Name -cnotmatch '^c723-[0-9a-f]{32}$') { continue }
        $size = Get-Allocated $entry.FullName
        $roots += [ordered]@{ path = $entry.FullName; allocatedBytes = $size }
        $bytes += $size
    }
    return [ordered]@{ roots = $roots; count = $roots.Count; allocatedBytes = $bytes }
}

# Compiled non-explicit Antiphon.Tests.Checkpoints cases, including expanded argument rows.
# selected is scanned from tests/Antiphon.Tests/Checkpoints and does not read the roster
# passed to Test-UsageEvidence, so an incomplete roster still fails admission. The scan
# matches CheckpointRoster.CompiledCases: non-abstract classes in that exact namespace,
# instance [Test] methods that are not [Explicit], one row per [Arguments] attribute, or
# one row when a method has none. Comments and string literals are not cases. Each OS
# skips the other OS's native cases; the skip set is matched by name against the roster.
function Get-CheckpointSourceCensusCount {
    $cached = Get-Variable -Name checkpointSourceCensusCount -Scope Script -ErrorAction SilentlyContinue
    if ($null -ne $cached) { return [int]$cached.Value }
    if (-not ('C1342.CheckpointSourceCensus' -as [type])) {
        Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;

namespace C1342
{
public static class CheckpointSourceCensus
{
    public static int Count(string directory)
    {
        var total = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            total += CountFile(File.ReadAllText(file));
        return total;
    }

    public static List<string> Names(string directory)
    {
        var names = new List<string>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            names.AddRange(Cases(File.ReadAllText(file)));
        return names;
    }

    static int CountFile(string text) => Cases(text).Count;

    static List<string> Cases(string text)
    {
        var parser = new Parser(Tokenize(text));
        parser.Parse();
        return parser.Names;
    }

    sealed class Parser
    {
        readonly List<string> _tokens;
        int _p;
        string _ns = "";
        readonly Stack<ClassFrame> _classes = new Stack<ClassFrame>();
        public readonly List<string> Names = new List<string>();

        public Parser(List<string> tokens) { _tokens = tokens; }

        static readonly HashSet<string> Modifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "public","private","protected","internal","static","sealed","abstract","override","virtual",
            "async","extern","unsafe","partial","new","required","readonly","volatile","file","ref","event","fixed"
        };

        string Tok(int k = 0)
        {
            var j = _p + k;
            return j >= 0 && j < _tokens.Count ? _tokens[j] : "";
        }

        string Take()
        {
            var t = Tok();
            if (_p < _tokens.Count) _p++;
            return t;
        }

        static bool Ident(string t) => t.Length > 0 && (char.IsLetter(t[0]) || t[0] == '_');

        public void Parse()
        {
            while (_p < _tokens.Count)
            {
                var before = _p;
                Declaration();
                if (_p == before) _p++;
            }
        }

        void Declaration()
        {
            var attrs = Attributes();
            var mods = ModifiersOf();
            var kw = Tok();
            if (kw == "namespace") { Namespace(); return; }
            if (kw == "class" || kw == "struct" || kw == "interface" || kw == "enum" || kw == "record")
            {
                TypeDecl(attrs, mods);
                return;
            }
            if (kw == "using" || kw == "extern" || kw == "delegate") { SkipSemicolon(); return; }
            if (kw == "global" && Tok(1) == "using") { SkipSemicolon(); return; }
            if (kw == "}") return;
            if (_classes.Count > 0) { Member(attrs, mods); return; }
            if (kw == ";") { Take(); return; }
            Take();
        }

        List<string> Attributes()
        {
            var names = new List<string>();
            while (Tok() == "[")
                names.AddRange(AttributeList());
            return names;
        }

        List<string> AttributeList()
        {
            var names = new List<string>();
            if (Tok() != "[") return names;
            Take();
            while (Tok() != "]" && Tok() != "")
            {
                var target = Tok();
                if (Ident(target) && Tok(1) == ":" && Tok(2) != ":" &&
                    (target == "assembly" || target == "module" || target == "return" || target == "param" ||
                     target == "field" || target == "method" || target == "type" || target == "property" || target == "event"))
                {
                    Take();
                    Take();
                }
                var name = Dotted();
                if (Tok() == "(") SkipParens();
                if (name.Length > 0) names.Add(name);
                if (Tok() == ",") { Take(); continue; }
                break;
            }
            if (Tok() == "]") Take();
            return names;
        }

        string Dotted()
        {
            if (!Ident(Tok())) return "";
            var name = Take();
            while (true)
            {
                if (Tok() == "." && Ident(Tok(1))) { Take(); name = Take(); continue; }
                if (Tok() == ":" && Tok(1) == ":" && Ident(Tok(2))) { Take(); Take(); name = Take(); continue; }
                break;
            }
            return name;
        }

        List<string> ModifiersOf()
        {
            var mods = new List<string>();
            while (Modifiers.Contains(Tok())) mods.Add(Take());
            return mods;
        }

        void Namespace()
        {
            Take();
            var name = QualifiedName();
            if (Tok() == ";") { Take(); _ns = name; return; }
            if (Tok() == "{")
            {
                Take();
                var previous = _ns;
                _ns = previous.Length == 0 ? name : previous + "." + name;
                while (Tok() != "" && Tok() != "}") Declaration();
                if (Tok() == "}") Take();
                _ns = previous;
            }
        }

        string QualifiedName()
        {
            if (!Ident(Tok())) return "";
            var name = Take();
            while (Tok() == "." && Ident(Tok(1)))
            {
                Take();
                name = name + "." + Take();
            }
            return name;
        }

        void TypeDecl(List<string> attrs, List<string> mods)
        {
            var kind = Take();
            if (kind == "record" && (Tok() == "class" || Tok() == "struct"))
            {
                var next = Take();
                kind = next == "struct" ? "struct" : "class";
            }
            else if (kind == "record") kind = "class";
            var typeName = Dotted();
            if (Tok() == "<") SkipAngles();
            if (Tok() == "(") SkipParens();
            while (Tok() != "{" && Tok() != ";" && Tok() != "" && Tok() != "}")
            {
                if (Tok() == "(") { SkipParens(); continue; }
                if (Tok() == "<") { SkipAngles(); continue; }
                if (Tok() == "[") { SkipBrackets(); continue; }
                Take();
            }
            var countable = kind == "class" && _ns == "Antiphon.Tests.Checkpoints";
            var explicitType = Has(attrs, "Explicit") || Has(attrs, "ExplicitAttribute");
            var abstractType = mods.Contains("abstract");
            var staticType = mods.Contains("static");
            var full = _classes.Count == 0 ? _ns + "." + typeName : _classes.Peek().Full + "+" + typeName;
            if (Tok() == ";") { Take(); return; }
            if (Tok() != "{") return;
            Take();
            _classes.Push(new ClassFrame(full, countable, explicitType, abstractType, staticType));
            while (_p < _tokens.Count)
            {
                if (Tok() == "}") { Take(); break; }
                var before = _p;
                Declaration();
                if (_p == before) _p++;
            }
            if (_classes.Count > 0) _classes.Pop();
        }

        void Member(List<string> attrs, List<string> mods)
        {
            var angle = 0;
            var paren = 0;
            var bracket = 0;
            var lastIdent = "";
            var method = false;
            var sawParen = false;
            while (Tok() != "")
            {
                var t = Tok();
                if (t == "<" && paren == 0) { angle++; Take(); continue; }
                if (t == ">" && angle > 0 && paren == 0) { angle--; Take(); continue; }
                if (t == "[" && angle == 0 && paren == 0) { bracket++; Take(); continue; }
                if (t == "]" && bracket > 0 && paren == 0) { bracket--; Take(); continue; }
                if (t == "(" && angle == 0 && bracket == 0) { paren++; Take(); continue; }
                if (t == ")" && paren > 0)
                {
                    paren--;
                    Take();
                    if (paren == 0 && angle == 0 && bracket == 0) sawParen = true;
                    continue;
                }
                if (angle == 0 && paren == 0 && bracket == 0 && (t == "{" || t == "=" || t == ";" || t == "}"))
                    break;
                if (angle == 0 && paren == 0 && bracket == 0 && Ident(t))
                {
                    if (sawParen) { lastIdent = t; method = true; sawParen = false; }
                    else lastIdent = t;
                }
                Take();
            }
            if (sawParen) method = true;
            var cls = _classes.Peek();
            var isTest = Has(attrs, "Test") || Has(attrs, "TestAttribute");
            var explicitMember = cls.Explicit || Has(attrs, "Explicit") || Has(attrs, "ExplicitAttribute");
            var arguments = 0;
            foreach (var attr in attrs)
                if (attr == "Arguments" || attr == "ArgumentsAttribute") arguments++;
            var staticMember = cls.Static || mods.Contains("static");
            if (method && isTest && !explicitMember && !staticMember && !cls.Abstract && cls.Countable && lastIdent.Length > 0)
            {
                var rows = Math.Max(1, arguments);
                for (var n = 0; n < rows; n++) Names.Add(cls.Full + "." + lastIdent);
            }
            var stop = Tok();
            if (stop == "{") { SkipBraces(); return; }
            if (stop == "=")
            {
                Take();
                if (Tok() == ">") Take();
                SkipExpression();
                return;
            }
            if (stop == ";") Take();
        }

        static bool Has(List<string> attrs, string name)
        {
            foreach (var attr in attrs)
                if (attr == name) return true;
            return false;
        }

        void SkipParens() { SkipPair("(", ")"); }
        void SkipAngles() { SkipPair("<", ">"); }
        void SkipBrackets() { SkipPair("[", "]"); }
        void SkipBraces() { SkipPair("{", "}"); }

        void SkipPair(string open, string close)
        {
            if (Tok() != open) return;
            var depth = 0;
            do
            {
                var t = Take();
                if (t == open) depth++;
                else if (t == close) depth--;
            }
            while (depth > 0 && Tok() != "");
        }

        void SkipExpression()
        {
            var paren = 0; var brace = 0; var bracket = 0; var angle = 0;
            while (Tok() != "")
            {
                var t = Tok();
                if (t == "(") paren++;
                else if (t == ")") { if (paren > 0) paren--; }
                else if (t == "{") brace++;
                else if (t == "}")
                {
                    if (brace > 0) brace--;
                    else if (paren == 0 && bracket == 0) return;
                }
                else if (t == "[") bracket++;
                else if (t == "]") { if (bracket > 0) bracket--; }
                else if (t == "<" && paren == 0 && brace == 0) angle++;
                else if (t == ">" && angle > 0) angle--;
                else if (t == ";" && paren == 0 && brace == 0 && bracket == 0 && angle == 0) { Take(); return; }
                Take();
            }
        }

        void SkipSemicolon()
        {
            while (Tok() != "" && Tok() != ";")
            {
                if (Tok() == "(") { SkipParens(); continue; }
                if (Tok() == "{") { SkipBraces(); continue; }
                Take();
            }
            if (Tok() == ";") Take();
        }
    }

    sealed class ClassFrame
    {
        public readonly string Full;
        public readonly bool Countable;
        public readonly bool Explicit;
        public readonly bool Abstract;
        public readonly bool Static;
        public ClassFrame(string full, bool countable, bool explicitType, bool abstractType, bool staticType)
        {
            Full = full; Countable = countable; Explicit = explicitType; Abstract = abstractType; Static = staticType;
        }
    }

    static List<string> Tokenize(string text)
    {
        var scan = new Scan(text);
        scan.Read();
        return scan.Tokens;
    }

    sealed class Scan
    {
        readonly string _s;
        readonly int _n;
        int _i;
        bool _lineStart = true;
        public readonly List<string> Tokens = new List<string>();

        public Scan(string text)
        {
            _s = text ?? "";
            _n = _s.Length;
            if (_n > 0 && _s[0] == '\uFEFF') _i = 1;
        }

        char At(int k)
        {
            var j = _i + k;
            return j >= 0 && j < _n ? _s[j] : '\0';
        }

        public void Read()
        {
            while (_i < _n)
            {
                var c = _s[_i];
                if (c == '\n') { _lineStart = true; _i++; continue; }
                if (char.IsWhiteSpace(c)) { _i++; continue; }
                if (_lineStart && c == '#')
                {
                    while (_i < _n && _s[_i] != '\n') _i++;
                    continue;
                }
                _lineStart = false;
                if (c == '/' && At(1) == '/')
                {
                    while (_i < _n && _s[_i] != '\n') _i++;
                    continue;
                }
                if (c == '/' && At(1) == '*')
                {
                    _i += 2;
                    while (_i < _n && !(_s[_i] == '*' && At(1) == '/')) _i++;
                    if (_i < _n) _i += 2;
                    continue;
                }
                if (TryString()) continue;
                if (c == '\'')
                {
                    _i++;
                    if (At(0) == '\\') _i += 2;
                    else if (_i < _n) _i++;
                    if (At(0) == '\'') _i++;
                    continue;
                }
                if (char.IsLetter(c) || c == '_' || (c == '@' && char.IsLetter(At(1))))
                {
                    var start = _i;
                    if (c == '@') _i++;
                    while (_i < _n && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
                    var ident = _s.Substring(start, _i - start);
                    if (ident.Length > 0 && ident[0] == '@') ident = ident.Substring(1);
                    Tokens.Add(ident);
                    continue;
                }
                Tokens.Add(c.ToString());
                _i++;
            }
        }

        bool TryString()
        {
            if (At(0) == '@' && At(1) == '$' && At(2) == '"') { _i += 3; Quoted(true, true); return true; }
            if (At(0) == '$' && At(1) == '@' && At(2) == '"') { _i += 3; Quoted(true, true); return true; }
            if (At(0) == '@' && At(1) == '"') { _i += 2; Quoted(true, false); return true; }
            if (At(0) == '$')
            {
                var d = 0;
                while (At(d) == '$') d++;
                if (At(d) != '"') return false;
                var q = 0;
                while (At(d + q) == '"') q++;
                if (q >= 3) { _i += d + q; Raw(q, d); return true; }
                if (q == 1) { _i += d + 1; Quoted(false, true); return true; }
                return false;
            }
            if (At(0) != '"') return false;
            var quotes = 0;
            while (At(quotes) == '"') quotes++;
            if (quotes >= 3) { _i += quotes; Raw(quotes, 0); return true; }
            if (quotes == 2) { _i += 2; return true; }
            _i++;
            Quoted(false, false);
            return true;
        }

        void Quoted(bool verbatim, bool interpolated)
        {
            while (_i < _n)
            {
                var c = _s[_i];
                if (interpolated && c == '{')
                {
                    if (At(1) == '{') { _i += 2; continue; }
                    _i++;
                    Hole(1);
                    continue;
                }
                if (interpolated && c == '}' && At(1) == '}') { _i += 2; continue; }
                if (!verbatim && c == '\\') { _i = Math.Min(_n, _i + 2); continue; }
                if (c == '"')
                {
                    if (verbatim && At(1) == '"') { _i += 2; continue; }
                    _i++;
                    return;
                }
                _i++;
            }
        }

        void Raw(int quotes, int dollars)
        {
            while (_i < _n)
            {
                if (dollars > 0 && At(0) == '{')
                {
                    var run = 0;
                    while (At(run) == '{') run++;
                    if (run >= dollars) { _i += dollars; Hole(dollars); continue; }
                }
                if (At(0) == '"')
                {
                    var run = 0;
                    while (At(run) == '"') run++;
                    if (run >= quotes) { _i += quotes; return; }
                }
                _i++;
            }
        }

        void Hole(int closer)
        {
            var brace = 0;
            var paren = 0;
            var bracket = 0;
            while (_i < _n)
            {
                if (TryString()) continue;
                var c = _s[_i];
                if (c == '/' && At(1) == '/') { while (_i < _n && _s[_i] != '\n') _i++; continue; }
                if (c == '/' && At(1) == '*')
                {
                    _i += 2;
                    while (_i < _n && !(_s[_i] == '*' && At(1) == '/')) _i++;
                    if (_i < _n) _i += 2;
                    continue;
                }
                if (c == '\'')
                {
                    _i++;
                    if (At(0) == '\\') _i += 2;
                    else if (_i < _n) _i++;
                    if (At(0) == '\'') _i++;
                    continue;
                }
                if (c == '{') { brace++; _i++; continue; }
                if (c == '(') { paren++; _i++; continue; }
                if (c == '[') { bracket++; _i++; continue; }
                if (c == ')') { if (paren > 0) paren--; _i++; continue; }
                if (c == ']') { if (bracket > 0) bracket--; _i++; continue; }
                if (c == '}')
                {
                    if (brace > 0) { brace--; _i++; continue; }
                    for (var k = 0; k < closer; k++)
                    {
                        if (At(0) != '}') return;
                        _i++;
                    }
                    return;
                }
                _i++;
            }
        }
    }
}
}
'@
    }
    $root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $dir = Join-Path $root 'tests/Antiphon.Tests/Checkpoints'
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) {
        throw "Checkpoint source census directory missing: $dir"
    }
    $script:checkpointSourceCensusCount = [C1342.CheckpointSourceCensus]::Count($dir)
    return [int]$script:checkpointSourceCensusCount
}

function Get-NamespaceCensus {
    [ordered]@{
        selected = Get-CheckpointSourceCensusCount
        windowsSkips = @(
            'Antiphon.Tests.Checkpoints.CheckpointRecoveryLinuxTests.*'
            'Antiphon.Tests.Checkpoints.DetachedLauncherTests.executor_survives_its_starter'
            'Antiphon.Tests.Checkpoints.CheckpointBoundedReaderTests.*'
            'Antiphon.Tests.Checkpoints.SafeDeleteProcessBoundaryTests.*'
            'Antiphon.Tests.Checkpoints.CheckpointFixtureProcessTests.*'
            'Antiphon.Tests.Checkpoints.CheckpointConfinementLedgerTests.unchanged_ledger_entries_are_not_hashed_when_the_slot_is_granted'
        )
        linuxSkips = @(
            'Antiphon.Tests.Checkpoints.EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open'
            'Antiphon.Tests.Checkpoints.CheckpointRecoveryWindowsTests.*'
            'Antiphon.Tests.Checkpoints.TimeoutTests.windows_quick_row_finishes_beside_a_slow_row'
            'Antiphon.Tests.Checkpoints.TimeoutTests.windows_row_arguments_round_trip_intact'
            'Antiphon.Tests.Checkpoints.TimeoutTests.windows_chatty_row_drains_interleaved_stdout_and_stderr'
            'Antiphon.Tests.Checkpoints.TimeoutTests.windows_row_timeout_kills_the_start_b_grandchild'
            'Antiphon.Tests.Checkpoints.CheckpointOutputConfinementTests.windows_confined_build_leaves_use_app_host_unset'
        )
    }
}

function Test-DeclaredOsSkip([string]$Name, $Census) {
    $patterns = if ($IsWindows) { $Census.windowsSkips } else { $Census.linuxSkips }
    foreach ($pattern in $patterns) { if ($Name -clike $pattern) { return $true } }
    return $false
}

# The selection roster, TRX and root event stream of one pass, checked against each other.
function Test-UsageEvidence([string]$Phase, [string]$RosterPath, [string]$TrxPath, [string]$EventsPath) {
    $errors = [collections.generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $RosterPath)) { $errors.Add('selection roster missing') }
    if (-not (Test-Path -LiteralPath $TrxPath)) { $errors.Add('TRX missing') }
    if (-not (Test-Path -LiteralPath $EventsPath)) { $errors.Add('event stream missing') }

    $selected = @()
    if (Test-Path -LiteralPath $RosterPath) {
        try { $selected = @(Read-CheckpointText $RosterPath | ConvertFrom-Json) }
        catch { $errors.Add('selection roster malformed') }
    }
    $results = @()
    $definitions = @{}
    if (Test-Path -LiteralPath $TrxPath) {
        try {
            [xml]$doc = Read-CheckpointText $TrxPath
            $manager = [Xml.XmlNamespaceManager]::new($doc.NameTable)
            $manager.AddNamespace('t', $doc.DocumentElement.NamespaceURI)
            foreach ($unit in $doc.SelectNodes('//t:UnitTest', $manager)) {
                $method = $unit.SelectSingleNode('t:TestMethod', $manager)
                if ($null -ne $method) { $definitions[[string]$unit.id] = [string]$method.className + '.' + [string]$method.name }
            }
            $results = @($doc.SelectNodes('//t:UnitTestResult', $manager) | ForEach-Object {
                [ordered]@{ id = [string]$_.testId; outcome = [string]$_.outcome; name = $definitions[[string]$_.testId] }
            })
        } catch { $errors.Add('TRX malformed') }
    }
    $executed = @($results | Where-Object { $_.outcome -ne 'NotExecuted' })
    $skipped = @($results | Where-Object { $_.outcome -eq 'NotExecuted' })
    $failed = @($results | Where-Object { $_.outcome -in @('Failed', 'Error', 'Timeout', 'Aborted') })
    if ($results.Count -ne $selected.Count) { $errors.Add("roster/TRX count mismatch selected=$($selected.Count) terminal=$($results.Count)") }
    if (@($results.id | Select-Object -Unique).Count -ne $results.Count) { $errors.Add('duplicate TRX test ID') }
    $expectedNames = @($selected | ForEach-Object { [string]$_.className + '.' + [string]$_.method } | Sort-Object)
    $actualNames = @($results | ForEach-Object { [string]$_.name } | Sort-Object)
    if (($expectedNames -join "`n") -cne ($actualNames -join "`n")) { $errors.Add('selected class/method multiset differs from TRX') }
    if ($failed.Count -ne 0) { $errors.Add("failed TRX tests=$($failed.Count)") }
    $census = Get-NamespaceCensus
    $checkpointSelected = @($selected | Where-Object { [string]$_.className -like 'Antiphon.Tests.Checkpoints.*' })
    # The OS-skip set is derived from the real roster by declared name, never a count.
    $declaredSkips = @($checkpointSelected | ForEach-Object { [string]$_.className + '.' + [string]$_.method } |
        Where-Object { Test-DeclaredOsSkip $_ $census } | Sort-Object)
    if ($Phase -eq 'Namespace') {
        if ($selected.Count -ne $census.selected -or $checkpointSelected.Count -ne $selected.Count) {
            $errors.Add("namespace census selected=$($selected.Count) expected=$($census.selected) executed=$($executed.Count) skipped=$($skipped.Count)")
        }
        $skippedNames = @($skipped | ForEach-Object { [string]$_.name } | Sort-Object)
        if (($skippedNames -join "`n") -cne ($declaredSkips -join "`n")) {
            $errors.Add("namespace skips differ from declared OS skips declared=$($declaredSkips.Count) skipped=$($skipped.Count)")
        }
    } else {
        if ($executed.Count -lt 1000) { $errors.Add("full-suite executed=$($executed.Count) below 1000") }
        if ($checkpointSelected.Count -ne $census.selected) {
            $errors.Add("full roster checkpoint cases=$($checkpointSelected.Count) expected=$($census.selected)")
        }
        $checkpointFloor = $census.selected - $declaredSkips.Count
        if (@($executed | Where-Object { $_.name -like 'Antiphon.Tests.Checkpoints.*' }).Count -lt $checkpointFloor) {
            $errors.Add('full suite omitted checkpoint executions')
        }
    }

    $created = @{}
    $deleted = @{}
    [long]$copiedBytes = 0
    $eventPeak = 0
    $active = @{}
    $activeBytes = @{}
    [long]$eventPeakBytes = 0
    if (Test-Path -LiteralPath $EventsPath) {
        $raw = (Read-CheckpointText $EventsPath)
        if ($raw.Length -eq 0 -or -not $raw.EndsWith("`n")) { $errors.Add('event tail incomplete') }
        foreach ($line in $raw.Split("`n")) {
            if (-not $line) { continue }
            try { $event = $line | ConvertFrom-Json }
            catch { $errors.Add('event line malformed'); continue }
            switch ([string]$event.kind) {
                'root-create' {
                    $created[[string]$event.path] = 1
                    $active[[string]$event.path] = 1
                    $activeBytes[[string]$event.path] = [long]0
                }
                'root-delete' {
                    $deleted[[string]$event.path] = 1
                    [void]$active.Remove([string]$event.path)
                    [void]$activeBytes.Remove([string]$event.path)
                }
                'copy' {
                    $copiedBytes += [long]$event.bytes
                    foreach ($root in @($activeBytes.Keys)) {
                        if ([string]$event.path -eq $root -or [string]$event.path -like ($root + [IO.Path]::DirectorySeparatorChar + '*')) {
                            $activeBytes[$root] = [long]$activeBytes[$root] + [long]$event.bytes
                            break
                        }
                    }
                }
                default { $errors.Add('unknown event kind') }
            }
            if ($active.Count -gt $eventPeak) { $eventPeak = $active.Count }
            [long]$liveCopyBytes = 0
            foreach ($size in $activeBytes.Values) { $liveCopyBytes += [long]$size }
            if ($liveCopyBytes -gt $eventPeakBytes) { $eventPeakBytes = $liveCopyBytes }
        }
    }
    if ($created.Count -eq 0) { $errors.Add('no owned roots were observed') }
    if ($created.Count -ne $deleted.Count -or $active.Count -ne 0) { $errors.Add('created/deleted root IDs do not reconcile') }

    $rosterHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes(($expectedNames -join "`n")))).ToLowerInvariant()
    return [ordered]@{
        errors = @($errors); selected = $selected.Count; executed = $executed.Count
        failed = $failed.Count; skipped = $skipped.Count; rosterHash = $rosterHash
        createdRoots = $created.Count; deletedRoots = $deleted.Count; copiedLogicalBytes = $copiedBytes
        eventHighWaterRoots = $eventPeak; eventHighWaterCopiedBytes = $eventPeakBytes
    }
}

# Pass 2 must repeat pass 1's accepted roster exactly; the delta is reported, not gated.
function Compare-UsagePass([string]$PriorPath, [string]$RosterHash, [long]$PeakBytes, [long]$FinalBytes, [int]$CreatedRoots) {
    $errors = [collections.generic.List[string]]::new()
    $delta = $null
    if (-not (Test-Path -LiteralPath $PriorPath)) { $errors.Add('pass-1 accepted report missing') }
    else {
        $prior = Read-CheckpointText $PriorPath | ConvertFrom-Json
        if ($prior.rosterHash -cne $RosterHash) { $errors.Add('pass rosters differ') }
        $delta = [ordered]@{ peakAllocatedBytes = $PeakBytes - [long]$prior.peakAllocatedBytes;
            finalAllocatedBytes = $FinalBytes - [long]$prior.finalAllocatedBytes;
            createdRoots = $CreatedRoots - [int]$prior.createdRoots }
    }
    return [ordered]@{ errors = @($errors); delta = $delta }
}
