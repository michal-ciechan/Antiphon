#requires -Version 5.1
# CARD-1514: opt-in phase timing sidecars. ASCII-only. Unset phase root is a no-op.
# TRX stays authoritative. This file never edits TRX, LogRoot summary.json, receipts,
# selection, the cost cache, or the release manifest.

# Visibility, not the script flag. A function-scoped dot-source used to set the
# flag and drop Resolve-QualificationTimingRoot when that call returned, and the
# next lock decision then failed with command-not-found.
if ($script:TestPhaseEvidenceLoading) { return }
$script:TestPhaseEvidenceResolver = $null
try { $script:TestPhaseEvidenceResolver = Get-Command Resolve-QualificationTimingRoot -ErrorAction SilentlyContinue } catch { $script:TestPhaseEvidenceResolver = $null }
if ($script:TestPhaseEvidenceResolver) {
    $script:TestPhaseEvidenceLoaded = $true
    return
}
$script:TestPhaseEvidenceLoading = $true

if (-not (Get-Command Write-NightlyAtomicJson -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'nightly-common.ps1')
}

$script:TestPhaseVariableNames = @(
    'ANTIPHON_TEST_PHASE_ROOT',
    'ANTIPHON_TEST_PHASE_RUN_ID',
    'ANTIPHON_TEST_PHASE_GROUP_ID',
    'ANTIPHON_TEST_PHASE_ATTEMPT',
    'ANTIPHON_TEST_PHASE_SUITE'
)

function Remove-TestPhaseVariables {
    param($Environment)
    if ($null -eq $Environment) { return }
    foreach ($name in $script:TestPhaseVariableNames) {
        if ($Environment -is [System.Collections.IDictionary]) {
            [void]$Environment.Remove($name)
        }
    }
}

function Enable-TestPhaseExecutionEnvironment {
    param(
        [hashtable]$Environment,
        [string]$LogRoot,
        [string]$RunId,
        [string]$GroupId,
        [string]$Attempt,
        [string]$Suite,
        [string]$Profile,
        [string]$TimingRoot = ''
    )
    if ($null -eq $Environment) { $Environment = @{} }
    $explicit = [Environment]::GetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT')
    $qualification = ($Profile -eq 'rc') -or ($Profile -eq 'nightly')
    $enable = $qualification -or (-not [string]::IsNullOrWhiteSpace($explicit)) -or (-not [string]::IsNullOrWhiteSpace($TimingRoot))
    if (-not $enable) {
        Remove-TestPhaseVariables -Environment $Environment
        return $Environment
    }
    $root = $TimingRoot
    if ([string]::IsNullOrWhiteSpace($root)) { $root = $explicit }
    if ([string]::IsNullOrWhiteSpace($root)) {
        $root = Join-Path $LogRoot 'timing'
    }
    $Environment['ANTIPHON_TEST_PHASE_ROOT'] = [string]$root
    $Environment['ANTIPHON_TEST_PHASE_RUN_ID'] = [string]$RunId
    $Environment['ANTIPHON_TEST_PHASE_GROUP_ID'] = [string]$GroupId
    $Environment['ANTIPHON_TEST_PHASE_ATTEMPT'] = [string]$Attempt
    $Environment['ANTIPHON_TEST_PHASE_SUITE'] = [string]$Suite
    return $Environment
}

function ConvertTo-TestPhaseCsvCell {
    param($Value)
    $text = ''
    if ($null -ne $Value) { $text = [string]$Value }
    $escaped = $text.Replace('"', '""')
    if ($escaped.Length -gt 0) {
        $lead = $escaped.Substring(0, 1)
        if ($lead -eq '=' -or $lead -eq '+' -or $lead -eq '-' -or $lead -eq '@') {
            $escaped = "'" + $escaped
        }
    }
    return '"' + $escaped + '"'
}

function ConvertTo-TestPhaseMarkdownCell {
    param($Value)
    $text = ''
    if ($null -ne $Value) { $text = [string]$Value }
    $text = $text.Replace('\', '\\')
    $text = $text.Replace('|', '\|')
    $text = $text.Replace("`r", ' ')
    $text = $text.Replace("`n", ' ')
    return $text
}

function ConvertTo-TestPhaseJsonString {
    param([string]$Text)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    foreach ($ch in $Text.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq '"') { [void]$sb.Append('\"') }
        elseif ($ch -eq '\') { [void]$sb.Append('\\') }
        elseif ($ch -eq "`n") { [void]$sb.Append('\n') }
        elseif ($ch -eq "`r") { [void]$sb.Append('\r') }
        elseif ($code -lt 32) { [void]$sb.Append('\u' + $code.ToString('x4')) }
        else { [void]$sb.Append($ch) }
    }
    [void]$sb.Append('"')
    return $sb.ToString()
}

# Hand-built so a one-element array stays an array on Windows PowerShell 5.1.
function ConvertTo-TestPhaseJsonText {
    param($Value, [int]$Depth = 0)
    if ($Depth -gt 24) { return 'null' }
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [bool]) {
        if ($Value) { return 'true' }
        return 'false'
    }
    if ($Value -is [byte] -or $Value -is [int16] -or $Value -is [int32] -or $Value -is [int64] -or $Value -is [decimal] -or $Value -is [double] -or $Value -is [single]) {
        return [System.Convert]::ToString($Value, [System.Globalization.CultureInfo]::InvariantCulture)
    }
    if ($Value -is [string]) { return (ConvertTo-TestPhaseJsonString -Text $Value) }
    if ($Value -is [System.Collections.IDictionary]) {
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($key in @($Value.Keys)) {
            $parts.Add((ConvertTo-TestPhaseJsonString -Text ([string]$key)) + ':' + (ConvertTo-TestPhaseJsonText -Value $Value[$key] -Depth ($Depth + 1)))
        }
        return '{' + ($parts.ToArray() -join ',') + '}'
    }
    $props = $Value.PSObject.Properties
    $hasNote = $false
    if ($null -ne $props) {
        foreach ($prop in $props) {
            if ($prop.MemberType -eq [System.Management.Automation.PSMemberTypes]::NoteProperty) { $hasNote = $true; break }
        }
    }
    if ($hasNote) {
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($prop in $props) {
            if ($prop.MemberType -ne [System.Management.Automation.PSMemberTypes]::NoteProperty) { continue }
            $parts.Add((ConvertTo-TestPhaseJsonString -Text $prop.Name) + ':' + (ConvertTo-TestPhaseJsonText -Value $prop.Value -Depth ($Depth + 1)))
        }
        return '{' + ($parts.ToArray() -join ',') + '}'
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($item in $Value) {
            $parts.Add((ConvertTo-TestPhaseJsonText -Value $item -Depth ($Depth + 1)))
        }
        return '[' + ($parts.ToArray() -join ',') + ']'
    }
    return (ConvertTo-TestPhaseJsonString -Text ([string]$Value))
}

function Write-TestPhaseTextAtomic {
    param([string]$Path, [string]$Text)
    $identity = Get-TestPhasePathIdentity -Path $Path
    if ($identity.Kind -eq 'unsupported' -or $identity.Kind -eq 'unreadable') { return }
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $tmp = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($tmp, $Text, $utf8)
    if ($identity.Kind -eq 'regular') {
        $bak = $Path + '.bak'
        [System.IO.File]::Replace($tmp, $Path, $bak)
        if (Test-Path -LiteralPath $bak) { Remove-Item -LiteralPath $bak -Force -ErrorAction SilentlyContinue }
    } else {
        [System.IO.File]::Move($tmp, $Path)
    }
}

function ConvertTo-TestPhaseRelativePath {
    param([string]$LogRoot, [string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($LogRoot)) { return '' }
    $root = [System.IO.Path]::GetFullPath($LogRoot).TrimEnd('\', '/')
    $full = [System.IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { return '' }
    $rel = $full.Substring($root.Length).TrimStart('\', '/')
    return ($rel.Replace('\', '/'))
}

function Get-TestPhaseClassBasisMap {
    $map = @{}
    $path = [string]$script:NightlyClassCostPath
    if ([string]::IsNullOrWhiteSpace($path)) {
        $path = Join-Path $PSScriptRoot 'nightly-class-costs.json'
    }
    if (-not (Test-Path -LiteralPath $path)) { return $map }
    $doc = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
    if ($null -eq $doc.classes) { return $map }
    foreach ($row in @($doc.classes.PSObject.Properties)) {
        $map[[string]$row.Name] = [string]$row.Value.basis
    }
    return $map
}

function Get-TestPhaseTimingDirectory {
    param([string]$RunDirectory)
    if (-not [string]::IsNullOrWhiteSpace([string]$script:QualificationTimingRoot)) {
        return [string]$script:QualificationTimingRoot
    }
    return (Join-Path $RunDirectory 'timing')
}

function Read-TestPhaseTrxAttribute {
    param([string]$Attrs, [string]$Name)
    if ([string]::IsNullOrEmpty($Attrs) -or [string]::IsNullOrEmpty($Name)) { return '' }
    $pattern = '\b' + [regex]::Escape($Name) + '\s*=\s*"([^"]*)"'
    $match = [regex]::Match($Attrs, $pattern)
    if ($match.Success) { return [string]$match.Groups[1].Value }
    $pattern = '\b' + [regex]::Escape($Name) + "\s*=\s*'([^']*)'"
    $match = [regex]::Match($Attrs, $pattern)
    if ($match.Success) { return [string]$match.Groups[1].Value }
    return ''
}

function Get-TestPhaseStdOutText {
    param([string]$Body)
    if ([string]::IsNullOrEmpty($Body)) { return '' }
    $open = $Body.IndexOf('<StdOut', [StringComparison]::Ordinal)
    if ($open -lt 0) { return '' }
    $gt = $Body.IndexOf('>', $open)
    if ($gt -lt 0) { return '' }
    $close = $Body.IndexOf('</StdOut>', $gt, [StringComparison]::Ordinal)
    if ($close -lt 0) { return '' }
    $inner = $Body.Substring($gt + 1, $close - ($gt + 1))
    if ($inner.StartsWith('<![CDATA[', [StringComparison]::Ordinal) -and $inner.EndsWith(']]>', [StringComparison]::Ordinal) -and $inner.Length -ge 12) {
        return $inner.Substring(9, $inner.Length - 12)
    }
    return $inner
}

function Read-TestPhaseTrxRows {
    param(
        [string]$Path,
        $Watch = $null,
        [int]$DeadlineMilliseconds = 200
    )
    $none = [pscustomobject]@{ Rows = @(); Complete = $false; Refused = $true }
    if ([string]::IsNullOrWhiteSpace($Path)) { return $none }
    if ($null -eq $Watch) { $Watch = [Diagnostics.Stopwatch]::StartNew() }
    if ($DeadlineMilliseconds -lt 1) { $DeadlineMilliseconds = 1 }
    $identity = Get-TestPhasePathIdentity -Path $Path
    if ($identity.Kind -ne 'regular') { return $none }
    if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { return $none }
    $opened = Open-TestPhaseReadStream -Path $Path
    if ($opened.Refused -or $null -eq $opened.Stream) { return $none }
    try {
        $read = [Antiphon.TestPhase.TrxTimingScanner]::Read($opened.Stream, $Watch, $DeadlineMilliseconds, 262144, 16384)
        if ($null -eq $read -or -not $read.Complete) { return $none }
        $rows = New-Object System.Collections.Generic.List[object]
        foreach ($row in $read.Rows) {
            $rows.Add([pscustomobject]@{
                TestId = [string]$row.TestId
                Outcome = [string]$row.Outcome
                Name = [string]$row.Name
                Duration = [string]$row.Duration
                StdOut = [string]$row.StdOut
                Path = $Path
            })
        }
        return [pscustomobject]@{ Rows = @($rows.ToArray()); Complete = $true; Refused = $false }
    } catch {
        return $none
    } finally {
        try { $opened.Stream.Dispose() } catch { }
    }
}

function Get-TestPhaseNativeIds {
    param([string]$Text)
    $ids = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrEmpty($Text)) { return @() }
    $marker = 'C1514_NATIVE '
    foreach ($line in ($Text -split "`n")) {
        $trim = $line.Trim().TrimEnd("`r")
        if ($trim.StartsWith('ANTIPHON_TEST_PHASE_ID ', [StringComparison]::Ordinal)) {
            $id = $trim.Substring('ANTIPHON_TEST_PHASE_ID '.Length).Trim()
        } elseif ($trim.StartsWith($marker, [StringComparison]::Ordinal)) {
            # Preserve the original probe's explicit identity witness.
            $rest = $trim.Substring($marker.Length)
            $space = $rest.IndexOf(' ')
            if ($space -lt 1) { continue }
            $id = $rest.Substring($space + 1).Trim()
        } else { continue }
        if (-not [string]::IsNullOrWhiteSpace($id) -and -not $ids.Contains($id)) {
            $ids.Add($id)
        }
    }
    return @($ids.ToArray())
}

function Initialize-TestPhaseLinuxIdentity {
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { return }
    if ('Antiphon.TestPhase.FileIdentity' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace Antiphon.TestPhase {
    [StructLayout(LayoutKind.Sequential)]
    public struct LinuxStat {
        public ulong Dev;
        public ulong Ino;
        public ulong Nlink;
        public uint Mode;
        public uint Uid;
        public uint Gid;
        public int Pad0;
        public ulong Rdev;
        public long Size;
        public ulong A; public ulong B; public ulong C; public ulong D;
        public ulong E; public ulong F; public ulong G; public ulong H;
        public ulong I; public ulong J; public ulong K; public ulong L;
    }
    public static class FileIdentity {
        [DllImport("libc", EntryPoint="lstat", SetLastError=true)]
        public static extern int LStat(string path, out LinuxStat stat);
        [DllImport("libc", EntryPoint="open", SetLastError=true)]
        public static extern int Open(string path, int flags);
        [DllImport("libc", EntryPoint="fstat", SetLastError=true)]
        public static extern int FStat(int fd, out LinuxStat stat);
        [DllImport("libc", EntryPoint="close", SetLastError=true)]
        public static extern int Close(int fd);
    }
}
'@
}

Initialize-TestPhaseLinuxIdentity

function Initialize-TestPhaseStreamReaders {
    if ('Antiphon.TestPhase.TrxTimingScanner' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Antiphon.TestPhase {
    public sealed class TrxTimingRow {
        public string TestId;
        public string Outcome;
        public string Name;
        public string Duration;
        public string StdOut;
    }

    public sealed class TrxTimingRead {
        public bool Complete;
        public List<TrxTimingRow> Rows = new List<TrxTimingRow>();
    }

    public sealed class TrxTimingStats {
        public bool Complete;
        public int Count;
        public double P50;
        public double P95;
        public double Max;
        public string[] SlowNames = new string[0];
        public double[] SlowSeconds = new double[0];
    }

    public sealed class JsonlTimingRead {
        public bool Complete;
        public List<string> Lines = new List<string>();
    }

    public sealed class JsonlHeaderRead {
        public bool Refused;
        public string Line;
    }

    public static class TrxTimingScanner {
        public static TrxTimingRead Read(Stream stream, Stopwatch watch, int deadlineMs, int maxTagBytes, int maxStdoutBytes) {
            var read = new TrxTimingRead();
            var scan = Scan(stream, watch, deadlineMs, maxTagBytes, maxStdoutBytes, true);
            read.Complete = scan.Complete;
            if (!scan.Complete) return read;
            for (int i = 0; i < scan.Rows.Count; i++) {
                var row = scan.Rows[i];
                read.Rows.Add(new TrxTimingRow {
                    TestId = row.TestId,
                    Outcome = row.Outcome,
                    Name = row.Name,
                    Duration = row.Duration,
                    StdOut = row.StdOut
                });
            }
            return read;
        }

        public static TrxTimingStats Summarize(Stream stream, Stopwatch watch, int deadlineMs, int maxTagBytes) {
            var stats = new TrxTimingStats();
            var scan = Scan(stream, watch, deadlineMs, maxTagBytes, 0, false);
            stats.Complete = scan.Complete;
            if (!scan.Complete) return stats;
            var ranked = new List<Ranked>();
            for (int i = 0; i < scan.Rows.Count; i++) {
                if (!scan.Rows[i].HasSeconds) continue;
                ranked.Add(new Ranked { Seconds = scan.Rows[i].Seconds, Name = scan.Rows[i].Name ?? "" });
            }
            stats.Count = ranked.Count;
            if (ranked.Count < 1) return stats;
            ranked.Sort(CompareRank);
            var sorted = new double[ranked.Count];
            for (int i = 0; i < ranked.Count; i++) sorted[i] = ranked[i].Seconds;
            stats.P50 = Rank(sorted, 0.50);
            stats.P95 = Rank(sorted, 0.95);
            stats.Max = Math.Round(sorted[sorted.Length - 1], 3, MidpointRounding.ToEven);
            int take = ranked.Count < 20 ? ranked.Count : 20;
            stats.SlowNames = new string[take];
            stats.SlowSeconds = new double[take];
            for (int i = 0; i < take; i++) {
                var row = ranked[ranked.Count - 1 - i];
                stats.SlowNames[i] = row.Name;
                stats.SlowSeconds[i] = Math.Round(row.Seconds, 3, MidpointRounding.ToEven);
            }
            return stats;
        }

        static int CompareRank(Ranked left, Ranked right) {
            int bySeconds = left.Seconds.CompareTo(right.Seconds);
            if (bySeconds != 0) return bySeconds;
            return string.CompareOrdinal(right.Name, left.Name);
        }

        static double Rank(double[] sorted, double fraction) {
            int n = sorted.Length;
            int rank = (int)Math.Ceiling(fraction * n);
            if (rank < 1) rank = 1;
            if (rank > n) rank = n;
            return Math.Round(sorted[rank - 1], 3, MidpointRounding.ToEven);
        }

        static ScanBag Scan(Stream stream, Stopwatch watch, int deadlineMs, int maxTagBytes, int maxStdoutBytes, bool captureStdout) {
            var bag = new ScanBag();
            var cursor = new Cursor(stream, watch, deadlineMs);
            if (maxTagBytes < 64) maxTagBytes = 64;
            if (maxStdoutBytes < 0) maxStdoutBytes = 0;
            while (true) {
                int b = cursor.NextUntil((byte)'<');
                if (b == -2) { bag.Complete = false; return bag; }
                if (b < 0) break;
                string name = cursor.ReadName();
                if (!IsLocal(name, "UnitTestResult")) continue;
                string attrs;
                bool selfClose;
                if (!cursor.ReadStartTag(maxTagBytes, out attrs, out selfClose)) {
                    bag.Complete = false;
                    return bag;
                }
                string stdout = "";
                if (!selfClose) {
                    if (!cursor.ReadBody(name, maxTagBytes, maxStdoutBytes, captureStdout, out stdout)) {
                        bag.Complete = false;
                        return bag;
                    }
                }
                string duration = Attribute(attrs, "duration");
                double seconds;
                bool hasSeconds = TrySeconds(duration, out seconds);
                bag.Rows.Add(new HeldRow {
                    TestId = captureStdout ? Attribute(attrs, "testId") : "",
                    Outcome = captureStdout ? Attribute(attrs, "outcome") : "",
                    Name = Attribute(attrs, "testName"),
                    Duration = captureStdout ? duration : "",
                    StdOut = stdout ?? "",
                    Seconds = seconds,
                    HasSeconds = hasSeconds
                });
            }
            bag.Complete = true;
            return bag;
        }

        static bool TrySeconds(string text, out double seconds) {
            seconds = 0;
            if (string.IsNullOrEmpty(text)) return false;
            int i = 0;
            int sign = 1;
            if (text[0] == '-') { sign = -1; i++; }
            int first = ReadDigits(text, ref i);
            if (first < 0) return FallbackSeconds(text, out seconds);
            int days = 0;
            int hours = first;
            if (i < text.Length && text[i] == '.') {
                int mark = i;
                i++;
                int afterDot = ReadDigits(text, ref i);
                if (afterDot >= 0 && i < text.Length && text[i] == ':') {
                    days = first;
                    hours = afterDot;
                } else {
                    i = mark;
                    return FallbackSeconds(text, out seconds);
                }
            }
            if (i >= text.Length || text[i] != ':') return FallbackSeconds(text, out seconds);
            i++;
            int minutes = ReadDigits(text, ref i);
            if (minutes < 0 || i >= text.Length || text[i] != ':') return FallbackSeconds(text, out seconds);
            i++;
            int secs = ReadDigits(text, ref i);
            if (secs < 0) return FallbackSeconds(text, out seconds);
            double frac = 0;
            if (i < text.Length && text[i] == '.') {
                i++;
                int digits = 0;
                int value = 0;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9' && digits < 7) {
                    value = value * 10 + (text[i] - '0');
                    digits++;
                    i++;
                }
                if (digits < 1) return FallbackSeconds(text, out seconds);
                double scale = 1;
                for (int d = 0; d < digits; d++) scale *= 10;
                frac = value / scale;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
            }
            if (i != text.Length) return FallbackSeconds(text, out seconds);
            seconds = sign * ((days * 86400.0) + (hours * 3600.0) + (minutes * 60.0) + secs + frac);
            return true;
        }

        static int ReadDigits(string text, ref int i) {
            if (i >= text.Length || text[i] < '0' || text[i] > '9') return -1;
            int value = 0;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9') {
                value = (value * 10) + (text[i] - '0');
                i++;
            }
            return value;
        }

        static bool FallbackSeconds(string text, out double seconds) {
            seconds = 0;
            // TimeSpan.TryParse(string, IFormatProvider, out TimeSpan) is not in .NET Framework.
            try {
                TimeSpan span = TimeSpan.Parse(text, CultureInfo.InvariantCulture);
                seconds = span.TotalSeconds;
                return true;
            } catch (FormatException) {
                return false;
            } catch (OverflowException) {
                return false;
            } catch (ArgumentException) {
                return false;
            }
        }

        static string Attribute(string attrs, string name) {
            if (string.IsNullOrEmpty(attrs) || string.IsNullOrEmpty(name)) return "";
            for (int i = 0; i + name.Length <= attrs.Length; i++) {
                if (i > 0 && IsWord(attrs[i - 1])) continue;
                if (string.CompareOrdinal(attrs, i, name, 0, name.Length) != 0) continue;
                int j = i + name.Length;
                if (j < attrs.Length && IsWord(attrs[j])) continue;
                while (j < attrs.Length && char.IsWhiteSpace(attrs[j])) j++;
                if (j >= attrs.Length || attrs[j] != '=') continue;
                j++;
                while (j < attrs.Length && char.IsWhiteSpace(attrs[j])) j++;
                if (j >= attrs.Length) return "";
                char quote = attrs[j];
                if (quote != '"' && quote != '\'') continue;
                j++;
                int end = attrs.IndexOf(quote, j);
                if (end < 0) return attrs.Substring(j);
                return attrs.Substring(j, end - j);
            }
            return "";
        }

        static bool IsLocal(string name, string local) {
            if (name == local) return true;
            int colon = name.LastIndexOf(':');
            return colon > 0 && name.IndexOf(':') == colon && name.Substring(colon + 1) == local;
        }

        static bool IsWord(char c) {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
        }

        sealed class Ranked {
            public double Seconds;
            public string Name;
        }

        sealed class HeldRow {
            public string TestId;
            public string Outcome;
            public string Name;
            public string Duration;
            public string StdOut;
            public double Seconds;
            public bool HasSeconds;
        }

        sealed class ScanBag {
            public bool Complete;
            public List<HeldRow> Rows = new List<HeldRow>();
        }

        sealed class Cursor {
            readonly Stream _stream;
            readonly Stopwatch _watch;
            readonly int _deadline;
            readonly byte[] _buf = new byte[8192];
            byte[] _tag = new byte[256];
            int _pos;
            int _len;
            int _push = -1;

            public Cursor(Stream stream, Stopwatch watch, int deadline) {
                _stream = stream;
                _watch = watch;
                _deadline = deadline < 1 ? 1 : deadline;
            }

            public int Next() {
                if (_push >= 0) {
                    int pushed = _push;
                    _push = -1;
                    return pushed;
                }
                if (_pos >= _len) {
                    if (_watch.ElapsedMilliseconds >= _deadline) return -2;
                    _len = _stream.Read(_buf, 0, _buf.Length);
                    _pos = 0;
                    if (_len <= 0) return -1;
                }
                return _buf[_pos++];
            }

            public int NextUntil(byte target) {
                if (_push >= 0) {
                    int pushed = _push;
                    _push = -1;
                    if (pushed == target) return pushed;
                }
                while (true) {
                    if (_pos >= _len) {
                        if (_watch.ElapsedMilliseconds >= _deadline) return -2;
                        _len = _stream.Read(_buf, 0, _buf.Length);
                        _pos = 0;
                        if (_len <= 0) return -1;
                    }
                    int found = Array.IndexOf(_buf, target, _pos, _len - _pos);
                    if (found >= 0) {
                        _pos = found + 1;
                        return target;
                    }
                    _pos = _len;
                }
            }

            void Push(int value) { _push = value; }

            public string ReadName() {
                var chars = new StringBuilder();
                while (chars.Length < 200) {
                    int b = Next();
                    if (b < 0) return chars.ToString();
                    if (IsNameByte(b)) chars.Append((char)b);
                    else { Push(b); return chars.ToString(); }
                }
                return chars.ToString();
            }

            static bool IsNameByte(int b) {
                return (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || (b >= '0' && b <= '9')
                    || b == '_' || b == ':' || b == '-' || b == '.';
            }

            public bool ReadStartTag(int maxTagBytes, out string attrs, out bool selfClose) {
                attrs = "";
                selfClose = false;
                int len = 0;
                bool quoted = false;
                byte quote = 0;
                int last = -1;
                while (true) {
                    int b = Next();
                    if (b < 0) return false;
                    if (quoted) {
                        if (!StoreTag(ref len, maxTagBytes, (byte)b)) return false;
                        if (b == quote) quoted = false;
                        continue;
                    }
                    if (b == '"' || b == '\'') {
                        quoted = true;
                        quote = (byte)b;
                        if (!StoreTag(ref len, maxTagBytes, (byte)b)) return false;
                        continue;
                    }
                    if (b == '>') {
                        selfClose = last == '/';
                        int use = len;
                        if (selfClose && use > 0) use--;
                        attrs = Encoding.UTF8.GetString(_tag, 0, use);
                        return true;
                    }
                    if (!StoreTag(ref len, maxTagBytes, (byte)b)) return false;
                    last = b;
                }
            }

            bool StoreTag(ref int len, int maxTagBytes, byte value) {
                if (len >= maxTagBytes) return false;
                if (len >= _tag.Length) {
                    int size = _tag.Length * 2;
                    if (size < len + 1) size = len + 1;
                    if (size > maxTagBytes) size = maxTagBytes;
                    var next = new byte[size];
                    Buffer.BlockCopy(_tag, 0, next, 0, len);
                    _tag = next;
                }
                _tag[len++] = value;
                return true;
            }

            public bool ReadBody(string resultName, int maxTagBytes, int maxStdoutBytes, bool captureStdout, out string stdout) {
                stdout = "";
                int depth = 1;
                var captured = new List<byte>(64);
                bool capturing = false;
                int captureDepth = -1;
                while (depth > 0) {
                    int b;
                    if (capturing && captured.Count < maxStdoutBytes) b = Next();
                    else b = NextUntil((byte)'<');
                    if (b < 0) return false;
                    if (b != '<') {
                        if (capturing && captured.Count < maxStdoutBytes) captured.Add((byte)b);
                        continue;
                    }
                    int mark = Next();
                    if (mark < 0) return false;
                    if (mark == '!') {
                        if (!ReadSpecial(capturing, captured, maxStdoutBytes)) return false;
                        continue;
                    }
                    if (mark == '/') {
                        string endName = ReadName();
                        if (!SkipUntil('>')) return false;
                        depth--;
                        if (capturing && depth < captureDepth) capturing = false;
                        continue;
                    }
                    Push(mark);
                    string startName = ReadName();
                    string ignored;
                    bool selfClose;
                    if (!ReadStartTag(maxTagBytes, out ignored, out selfClose)) return false;
                    if (selfClose) continue;
                    depth++;
                    if (captureStdout && !capturing && IsLocal(startName, "StdOut")) {
                        capturing = true;
                        captureDepth = depth;
                    }
                }
                stdout = DecodeStdout(captured);
                return true;
            }

            bool ReadSpecial(bool capturing, List<byte> captured, int maxStdoutBytes) {
                int next = Next();
                if (next < 0) return false;
                if (next == '-') {
                    int second = Next();
                    if (second < 0) return false;
                    return SkipSequence(new byte[] { (byte)'-', (byte)'>' });
                }
                if (next == '[') {
                    byte[] cdata = Encoding.ASCII.GetBytes("CDATA[");
                    for (int i = 0; i < cdata.Length; i++) {
                        int got = Next();
                        if (got != cdata[i]) return false;
                    }
                    return ReadMarked(new byte[] { (byte)']', (byte)']', (byte)'>' }, capturing, captured, maxStdoutBytes);
                }
                return SkipUntil('>');
            }

            bool ReadMarked(byte[] marker, bool capturing, List<byte> captured, int maxStdoutBytes) {
                int matched = 0;
                while (matched < marker.Length) {
                    int b = Next();
                    if (b < 0) return false;
                    if (b == marker[matched]) {
                        matched++;
                        continue;
                    }
                    if (capturing) {
                        for (int i = 0; i < matched && captured.Count < maxStdoutBytes; i++) captured.Add(marker[i]);
                        if (captured.Count < maxStdoutBytes) captured.Add((byte)b);
                    }
                    matched = 0;
                    if (b == marker[0]) matched = 1;
                }
                return true;
            }

            bool SkipSequence(byte[] marker) {
                int matched = 0;
                while (matched < marker.Length) {
                    int b = Next();
                    if (b < 0) return false;
                    if (b == marker[matched]) matched++;
                    else matched = b == marker[0] ? 1 : 0;
                }
                return true;
            }

            bool SkipUntil(char expected) {
                while (true) {
                    int b = Next();
                    if (b < 0) return false;
                    if (b == expected) return true;
                }
            }

            static string DecodeStdout(List<byte> captured) {
                if (captured.Count < 1) return "";
                string text = Encoding.UTF8.GetString(captured.ToArray());
                if (text.StartsWith("<![CDATA[", StringComparison.Ordinal) && text.EndsWith("]]>", StringComparison.Ordinal) && text.Length >= 12) {
                    return text.Substring(9, text.Length - 12);
                }
                return text;
            }
        }
    }

    public static class JsonlTimingScanner {
        static readonly byte[] Header = Encoding.ASCII.GetBytes("\"recordKind\":\"header\"");
        static readonly byte[] Fixture = Encoding.ASCII.GetBytes("\"recordKind\":\"fixture\"");
        static readonly byte[] Spawn = Encoding.ASCII.GetBytes("\"process-spawn\"");
        static readonly byte[] Wait = Encoding.ASCII.GetBytes("\"process-wait\"");
        static readonly byte[] Setup = Encoding.ASCII.GetBytes("\"fixture-setup\"");
        static readonly byte[] Teardown = Encoding.ASCII.GetBytes("\"fixture-teardown\"");

        public static JsonlTimingRead Read(Stream stream, Stopwatch watch, int deadlineMs, int maxLineBytes) {
            var read = new JsonlTimingRead { Complete = true };
            if (maxLineBytes < 1) maxLineBytes = 1;
            if (deadlineMs < 1) deadlineMs = 1;
            var buf = new byte[8192];
            var line = new byte[maxLineBytes];
            int lineLen = 0;
            while (true) {
                if (watch.ElapsedMilliseconds >= deadlineMs) { read.Complete = false; return read; }
                int count = stream.Read(buf, 0, buf.Length);
                if (count <= 0) break;
                for (int i = 0; i < count; i++) {
                    byte b = buf[i];
                    if (b == (byte)'\n') {
                        Accept(line, lineLen, read.Lines);
                        lineLen = 0;
                        continue;
                    }
                    if (lineLen >= maxLineBytes) {
                        read.Complete = false;
                        return read;
                    }
                    line[lineLen++] = b;
                }
            }
            if (lineLen > 0) Accept(line, lineLen, read.Lines);
            return read;
        }

        static void Accept(byte[] line, int length, List<string> lines) {
            if (length > 0 && line[length - 1] == (byte)'\r') length--;
            if (length < 1) return;
            if (IndexOf(line, length, Header) < 0 && IndexOf(line, length, Fixture) < 0 && IndexOf(line, length, Spawn) < 0
                && IndexOf(line, length, Wait) < 0 && IndexOf(line, length, Setup) < 0 && IndexOf(line, length, Teardown) < 0) {
                return;
            }
            lines.Add(Encoding.UTF8.GetString(line, 0, length));
        }

        static int IndexOf(byte[] line, int length, byte[] needle) {
            int n = needle.Length;
            if (n == 0 || length < n) return -1;
            int last = length - n;
            for (int i = 0; i <= last; i++) {
                int j = 0;
                while (j < n && line[i + j] == needle[j]) j++;
                if (j == n) return i;
            }
            return -1;
        }

        public static JsonlHeaderRead ReadFirstLine(Stream stream, Stopwatch watch, int deadlineMs, int maxLineBytes) {
            var read = new JsonlHeaderRead();
            if (maxLineBytes < 1) maxLineBytes = 1;
            if (deadlineMs < 1) deadlineMs = 1;
            var buf = new byte[8192];
            var line = new byte[maxLineBytes];
            int lineLen = 0;
            while (true) {
                if (watch.ElapsedMilliseconds >= deadlineMs) { read.Refused = true; return read; }
                int count = stream.Read(buf, 0, buf.Length);
                if (count <= 0) break;
                for (int i = 0; i < count; i++) {
                    byte b = buf[i];
                    if (b == (byte)'\n') {
                        if (lineLen > 0 && line[lineLen - 1] == (byte)'\r') lineLen--;
                        read.Line = lineLen < 1 ? "" : Encoding.UTF8.GetString(line, 0, lineLen);
                        return read;
                    }
                    if (lineLen >= maxLineBytes) { read.Refused = true; return read; }
                    line[lineLen++] = b;
                }
            }
            if (lineLen > 0 && line[lineLen - 1] == (byte)'\r') lineLen--;
            read.Line = lineLen < 1 ? "" : Encoding.UTF8.GetString(line, 0, lineLen);
            return read;
        }
    }
}
'@
}

Initialize-TestPhaseStreamReaders

function Get-TestPhasePathIdentity {
    param([string]$Path)
    $missing = [pscustomobject]@{ Kind = 'missing'; Size = [int64]0 }
    if ([string]::IsNullOrWhiteSpace($Path)) { return $missing }
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        if (-not (Test-Path -LiteralPath $Path)) { return $missing }
        try { $attrs = [IO.File]::GetAttributes($Path) } catch {
            return [pscustomobject]@{ Kind = 'unreadable'; Size = [int64]0 }
        }
        $directory = [IO.FileAttributes]::Directory
        $reparse = [IO.FileAttributes]::ReparsePoint
        if (($attrs -band $directory) -eq $directory) { return [pscustomobject]@{ Kind = 'unsupported'; Size = [int64]0 } }
        if (($attrs -band $reparse) -eq $reparse) { return [pscustomobject]@{ Kind = 'unsupported'; Size = [int64]0 } }
        $length = [int64]0
        try { $length = [int64](Get-Item -LiteralPath $Path).Length } catch { $length = [int64]0 }
        return [pscustomobject]@{ Kind = 'regular'; Size = $length }
    }
    Initialize-TestPhaseLinuxIdentity
    if (-not ('Antiphon.TestPhase.FileIdentity' -as [type])) {
        return [pscustomobject]@{ Kind = 'unreadable'; Size = [int64]0 }
    }
    $stat = New-Object Antiphon.TestPhase.LinuxStat
    $code = [Antiphon.TestPhase.FileIdentity]::LStat($Path, [ref]$stat)
    if ($code -ne 0) {
        $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        if ($err -eq 2) { return $missing }
        return [pscustomobject]@{ Kind = 'unreadable'; Size = [int64]0 }
    }
    $kind = ([uint32]$stat.Mode) -band 61440
    if ($kind -eq 32768) { return [pscustomobject]@{ Kind = 'regular'; Size = [int64]$stat.Size } }
    return [pscustomobject]@{ Kind = 'unsupported'; Size = [int64]$stat.Size }
}

function Read-TestPhaseStreamBytes {
    param($Stream, [int]$MaxBytes, [int]$DeadlineMilliseconds, $Watch)
    $buffer = New-Object byte[] 8192
    $ms = New-Object System.IO.MemoryStream
    $complete = $true
    while ($ms.Length -lt $MaxBytes) {
        if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { $complete = $false; break }
        $room = $MaxBytes - [int]$ms.Length
        $want = 8192
        if ($want -gt $room) { $want = $room }
        $count = $Stream.Read($buffer, 0, $want)
        if ($count -le 0) { break }
        $ms.Write($buffer, 0, $count)
    }
    if ($complete -and $ms.Length -ge $MaxBytes) {
        if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) {
            $complete = $false
        } else {
            $extra = $Stream.Read($buffer, 0, 1)
            if ($extra -gt 0) { $complete = $false }
        }
    }
    return [pscustomobject]@{ Bytes = $ms.ToArray(); Complete = [bool]$complete; Refused = $false; Kind = 'regular' }
}

function Open-TestPhaseReadStream {
    param([string]$Path)
    $refused = [pscustomobject]@{ Stream = $null; Refused = $true; Kind = 'unreadable' }
    $stream = $null
    $handle = $null
    $fd = -1
    $owned = $true
    try {
        if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
            $share = [IO.FileShare]([int][IO.FileShare]::ReadWrite -bor [int][IO.FileShare]::Delete)
            $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, $share)
        } else {
            # O_RDONLY | O_NONBLOCK | O_CLOEXEC | O_NOFOLLOW. A FIFO open returns
            # immediately; fstat then refuses anything that is not a regular file.
            Initialize-TestPhaseLinuxIdentity
            $fd = [Antiphon.TestPhase.FileIdentity]::Open($Path, 657408)
            if ($fd -lt 0) { return $refused }
            $stat = New-Object Antiphon.TestPhase.LinuxStat
            $code = [Antiphon.TestPhase.FileIdentity]::FStat($fd, [ref]$stat)
            if ($code -ne 0) { return $refused }
            $kind = ([uint32]$stat.Mode) -band 61440
            if ($kind -ne 32768) {
                return [pscustomobject]@{ Stream = $null; Refused = $true; Kind = 'unsupported' }
            }
            $handle = New-Object Microsoft.Win32.SafeHandles.SafeFileHandle ([IntPtr]$fd, $true)
            $fd = -1
            $stream = New-Object System.IO.FileStream ($handle, [IO.FileAccess]::Read, 8192, $false)
            $handle = $null
        }
        $owned = $false
        return [pscustomobject]@{ Stream = $stream; Refused = $false; Kind = 'regular' }
    } catch {
        return $refused
    } finally {
        if ($owned) {
            if ($null -ne $stream) { try { $stream.Dispose() } catch { } }
            elseif ($null -ne $handle) { try { $handle.Dispose() } catch { } }
            elseif ($fd -ge 0) { try { [void][Antiphon.TestPhase.FileIdentity]::Close($fd) } catch { } }
        }
    }
}

function Read-TestPhaseBoundedBytes {
    param(
        [string]$Path,
        [int]$MaxBytes = 262144,
        [int]$DeadlineMilliseconds = 200,
        $Watch = $null
    )
    $refused = [pscustomobject]@{ Bytes = $null; Complete = $false; Refused = $true; Kind = 'unreadable' }
    if ($MaxBytes -lt 1) { $MaxBytes = 1 }
    if ($DeadlineMilliseconds -lt 1) { $DeadlineMilliseconds = 1 }
    if ($null -eq $Watch) { $Watch = [Diagnostics.Stopwatch]::StartNew() }
    $identity = Get-TestPhasePathIdentity -Path $Path
    if ($identity.Kind -ne 'regular') {
        return [pscustomobject]@{ Bytes = $null; Complete = $false; Refused = $true; Kind = [string]$identity.Kind }
    }
    if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) {
        return [pscustomobject]@{ Bytes = $null; Complete = $false; Refused = $true; Kind = 'budget' }
    }
    $opened = Open-TestPhaseReadStream -Path $Path
    if ($opened.Refused -or $null -eq $opened.Stream) {
        $kind = [string]$opened.Kind
        if ([string]::IsNullOrWhiteSpace($kind)) { $kind = 'unreadable' }
        return [pscustomobject]@{ Bytes = $null; Complete = $false; Refused = $true; Kind = $kind }
    }
    try {
        return (Read-TestPhaseStreamBytes -Stream $opened.Stream -MaxBytes $MaxBytes -DeadlineMilliseconds $DeadlineMilliseconds -Watch $Watch)
    } catch {
        return $refused
    } finally {
        try { $opened.Stream.Dispose() } catch { }
    }
}

function ConvertTo-TestPhaseJsonlRecords {
    param($Read, [int]$MaxLines = 4000, [int]$DeadlineMilliseconds = 200, $Watch = $null)
    $records = New-Object System.Collections.Generic.List[object]
    $torn = 0
    $malformed = 0
    if ($null -eq $Watch) { $Watch = [Diagnostics.Stopwatch]::StartNew() }
    if ($DeadlineMilliseconds -lt 1) { $DeadlineMilliseconds = 1 }
    if ($null -eq $Read -or [bool]$Read.Refused -or $null -eq $Read.Bytes) {
        return [pscustomobject]@{ Records = @(); Torn = 1; Malformed = 0; Complete = $false }
    }
    $complete = [bool]$Read.Complete
    if (-not $complete) { $torn = 1 }
    $text = [Text.Encoding]::UTF8.GetString($Read.Bytes)
    if ((-not $complete) -and (-not $text.EndsWith("`n"))) {
        $cut = $text.LastIndexOf([char]10)
        if ($cut -lt 0) { $text = '' }
        else { $text = $text.Substring(0, $cut + 1) }
    }
    if ($MaxLines -lt 1) { $MaxLines = 1 }
    $parts = $text.Split([string[]]@("`n"), [System.StringSplitOptions]::None)
    $limit = $parts.Length
    if ($limit -gt 0 -and $text.EndsWith("`n")) { $limit = $limit - 1 }
    $lines = 0
    for ($i = 0; $i -lt $limit; $i++) {
        if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { $complete = $false; $torn = 1; break }
        if ($lines -ge $MaxLines) { $complete = $false; $torn = 1; break }
        $line = $parts[$i].TrimEnd("`r")
        $lines++
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parsed = $null
        $ok = $true
        try { $parsed = $line | ConvertFrom-Json } catch { $ok = $false }
        if ($ok -and $null -ne $parsed -and -not [string]::IsNullOrWhiteSpace([string]$parsed.recordKind)) {
            $records.Add($parsed)
            continue
        }
        $lastContent = $true
        for ($j = $i + 1; $j -lt $limit; $j++) {
            if (-not [string]::IsNullOrWhiteSpace($parts[$j].TrimEnd("`r"))) { $lastContent = $false; break }
        }
        if ($lastContent) { $torn++ } else { $malformed++ }
    }
    if ($lines -ge $MaxLines) { $complete = $false; if ($torn -lt 1) { $torn = 1 } }
    return [pscustomobject]@{ Records = @($records.ToArray()); Torn = $torn; Malformed = $malformed; Complete = [bool]$complete }
}

function Read-TestPhaseJsonlFile {
    param([string]$Path)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $read = Read-TestPhaseBoundedBytes -Path $Path -MaxBytes 262144 -DeadlineMilliseconds 200 -Watch $watch
    $parsed = ConvertTo-TestPhaseJsonlRecords -Read $read -MaxLines 4000 -DeadlineMilliseconds 200 -Watch $watch
    return [pscustomobject]@{ Records = @($parsed.Records); Torn = [int]$parsed.Torn; Malformed = [int]$parsed.Malformed; Path = $Path }
}

# Summary-only. The 200 ms / 262144 inline bound stays on Read-TestPhaseJsonlFile.
# An incomplete inline read is parsed again under a separate finite budget.
function Read-TestPhaseJsonlForSummary {
    param([string]$Path)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $read = Read-TestPhaseBoundedBytes -Path $Path -MaxBytes 262144 -DeadlineMilliseconds 200 -Watch $watch
    $parsed = ConvertTo-TestPhaseJsonlRecords -Read $read -MaxLines 4000 -DeadlineMilliseconds 200 -Watch $watch
    if (-not [bool]$parsed.Complete) {
        $again = [Diagnostics.Stopwatch]::StartNew()
        $full = Read-TestPhaseBoundedBytes -Path $Path -MaxBytes 8388608 -DeadlineMilliseconds 5000 -Watch $again
        $parsed = ConvertTo-TestPhaseJsonlRecords -Read $full -MaxLines 20000 -DeadlineMilliseconds 5000 -Watch $again
    }
    return [pscustomobject]@{ Records = @($parsed.Records); Torn = [int]$parsed.Torn; Malformed = [int]$parsed.Malformed; Path = $Path }
}

function Read-TestPhaseJsonlBounded {
    param(
        [string]$Path,
        [int]$MaxBytes = 262144,
        [int]$MaxLines = 4000,
        [int]$DeadlineMilliseconds = 200,
        $Watch = $null
    )
    if ($null -eq $Watch) { $Watch = [Diagnostics.Stopwatch]::StartNew() }
    $read = Read-TestPhaseBoundedBytes -Path $Path -MaxBytes $MaxBytes -DeadlineMilliseconds $DeadlineMilliseconds -Watch $Watch
    $parsed = ConvertTo-TestPhaseJsonlRecords -Read $read -MaxLines $MaxLines -DeadlineMilliseconds $DeadlineMilliseconds -Watch $Watch
    return [pscustomobject]@{ Records = @($parsed.Records); Complete = [bool]$parsed.Complete; Path = $Path }
}

function Write-TestPhasePlannedManifest {
    param(
        [string]$LogRoot,
        [string]$RunId,
        [string]$Attempt,
        [string]$Suite,
        [string]$SourceDigest,
        [string]$AssemblyDigest,
        [string]$PolicyDigest,
        [string]$CostFileDigest,
        [string]$AssemblyId,
        $Groups,
        $DiscoveryNodes,
        $ProfileExclusions
    )
    $entry = [pscustomobject]@{
        Document = [pscustomobject]@{ Nodes = @($DiscoveryNodes) }
        InProgress = @()
        Views = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    }
    $view = Get-NightlyDiscoveryIndexView -Entry $entry -ProfileExclusions $ProfileExclusions -ProfileAware
    $basisMap = Get-TestPhaseClassBasisMap
    $groupRows = @()
    foreach ($group in @($Groups)) {
        $ids = New-Object System.Collections.Generic.List[string]
        $carried = @()
        if ($group.PSObject.Properties.Name -contains 'nodes' -and $null -ne $group.nodes) {
            $carried = @($group.nodes)
        }
        if ($carried.Count -gt 0) {
            foreach ($node in $carried) {
                $uid = [string]$node.Uid
                if (-not [string]::IsNullOrWhiteSpace($uid)) { $ids.Add($uid) }
            }
        } else {
            $classes = @()
            foreach ($c in @($group.classes)) { $classes += [string]$c }
            foreach ($uid in @(Get-NightlyDiscoveryIndexRequiredUids -View $view -RequiredClasses $classes)) {
                $ids.Add([string]$uid)
            }
        }
        $bases = @{}
        foreach ($className in @($group.classes)) {
            $key = [string]$className
            $basis = 'model'
            if ($basisMap.ContainsKey($key)) { $basis = [string]$basisMap[$key] }
            elseif ($null -ne $script:NightlyClassCostTable -and $script:NightlyClassCostTable.ContainsKey($key)) { $basis = 'measured' }
            $bases[$basis] = $true
        }
        $pricing = 'model'
        $basisNames = @($bases.Keys)
        if ($basisNames.Count -eq 1) { $pricing = [string]$basisNames[0] }
        elseif ($basisNames.Count -gt 1) { $pricing = 'mixed' }
        $filter = ''
        if ($group.PSObject.Properties.Name -contains 'exactFilter' -and -not [string]::IsNullOrWhiteSpace([string]$group.exactFilter)) {
            $filter = [string]$group.exactFilter
        } elseif ($group.PSObject.Properties.Name -contains 'groupFilter') {
            $filter = [string]$group.groupFilter
        }
        $seconds = 0
        if ($group.PSObject.Properties.Name -contains 'seconds' -and $null -ne $group.seconds) {
            $seconds = [double]$group.seconds
        }
        $groupRows += [ordered]@{
            chunk = [string]$group.id
            classes = @($group.classes | ForEach-Object { [string]$_ })
            filter = $filter
            plannedTestIds = @($ids.ToArray())
            plannedSeconds = $seconds
            pricingBasis = $pricing
            sharedManifestOrder = $true
        }
    }
    $union = @{}
    $duplicate = $false
    foreach ($row in $groupRows) {
        foreach ($id in @($row.plannedTestIds)) {
            if ($union.ContainsKey([string]$id)) { $duplicate = $true }
            $union[[string]$id] = $true
        }
    }
    $missing = @()
    foreach ($id in @($view.RequiredUids)) {
        if (-not $union.ContainsKey([string]$id)) { $missing += [string]$id }
    }
    $extra = @()
    foreach ($id in @($union.Keys)) {
        $found = $false
        foreach ($req in @($view.RequiredUids)) {
            if ([string]$req -eq [string]$id) { $found = $true }
        }
        if (-not $found) { $extra += [string]$id }
    }
    $manifest = [ordered]@{
        version = 1
        runId = [string]$RunId
        attempt = [string]$Attempt
        suite = [string]$Suite
        sourceDigest = [string]$SourceDigest
        assemblyDigest = [string]$AssemblyDigest
        policyDigest = [string]$PolicyDigest
        costFileDigest = [string]$CostFileDigest
        assembly = [string]$AssemblyId
        requiredPartitionExact = ((-not $duplicate) -and $missing.Count -eq 0 -and $extra.Count -eq 0)
        missingIds = @($missing)
        extraIds = @($extra)
        groups = @($groupRows)
    }
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $LogRoot
    $text = ConvertTo-TestPhaseJsonText -Value $manifest
    $path = Join-Path $dir 'groups-planned.json'
    Write-TestPhaseTextAtomic -Path $path -Text $text
    # Keep the singleton for existing readers. Also retain every allowlisted suite.
    if ($Suite -match '^[a-z0-9][a-z0-9-]{0,40}$') {
        $suiteFile = 'groups-planned-' + $Suite + '.json'
        Write-TestPhaseTextAtomic -Path (Join-Path $dir $suiteFile) -Text $text
        Update-TestPhasePlannedIndex -Directory $dir -Suite $Suite -FileName $suiteFile -RunId $RunId -Attempt $Attempt -AssemblyId $AssemblyId
    }
    return $path
}

function Write-TestPhaseFailureMarker {
    param([string]$RunDirectory, [string]$Code)
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    try {
        if (-not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        $path = Join-Path $dir 'phase-summary-failed'
        [System.IO.File]::WriteAllText($path, $Code)
    } catch {
        # An unwritable timing directory must not change the caller's verdict.
    }
}

function ConvertTo-TestPhaseExecutedChunk {
    param($Item, [string]$Chunk, [string]$RunId, [string]$Attempt, [string]$Suite, [double]$HostWallSeconds, [bool]$TrxMissing, [int]$PassedCount, $Outcomes, [string]$Trx, [string]$TrxDigest, $ExitCode, $CoverageComplete, $TestsPassed)
    $priorOutcomes = New-Object System.Collections.Generic.List[object]
    if ($null -ne $Outcomes) {
        foreach ($row in @($Outcomes)) {
            if ($null -eq $row) { continue }
            $priorOutcomes.Add([ordered]@{ testId = [string]$row.testId; outcome = [string]$row.outcome })
        }
    }
    $chunkId = $Chunk
    $run = $RunId
    $attempt = $Attempt
    $suite = $Suite
    $wall = $HostWallSeconds
    $missing = $TrxMissing
    $passed = $PassedCount
    $trx = $Trx
    $digest = $TrxDigest
    $exitCode = $ExitCode
    $coverage = $CoverageComplete
    $tests = $TestsPassed
    if ($null -ne $Item) {
        if ($Item.PSObject.Properties.Name -contains 'chunk') { $chunkId = [string]$Item.chunk }
        if ($Item.PSObject.Properties.Name -contains 'runId') { $run = [string]$Item.runId }
        if ($Item.PSObject.Properties.Name -contains 'attempt') { $attempt = [string]$Item.attempt }
        if ($Item.PSObject.Properties.Name -contains 'suite') { $suite = [string]$Item.suite }
        if ($Item.PSObject.Properties.Name -contains 'hostWallSeconds' -and $null -ne $Item.hostWallSeconds) { $wall = [double]$Item.hostWallSeconds }
        if ($Item.PSObject.Properties.Name -contains 'trxMissing') { $missing = [bool]$Item.trxMissing }
        if ($Item.PSObject.Properties.Name -contains 'passedCount' -and $null -ne $Item.passedCount) { $passed = [int]$Item.passedCount }
        if ($Item.PSObject.Properties.Name -contains 'trx') { $trx = [string]$Item.trx }
        if ($Item.PSObject.Properties.Name -contains 'trxDigest') { $digest = [string]$Item.trxDigest }
        if ($Item.PSObject.Properties.Name -contains 'exitCode') { $exitCode = $Item.exitCode }
        if ($Item.PSObject.Properties.Name -contains 'coverageComplete') { $coverage = $Item.coverageComplete }
        if ($Item.PSObject.Properties.Name -contains 'testsPassed') { $tests = $Item.testsPassed }
        $priorOutcomes = New-Object System.Collections.Generic.List[object]
        if ($Item.PSObject.Properties.Name -contains 'outcomes' -and $null -ne $Item.outcomes) {
            foreach ($row in @($Item.outcomes)) {
                if ($null -eq $row) { continue }
                $priorOutcomes.Add([ordered]@{ testId = [string]$row.testId; outcome = [string]$row.outcome })
            }
        }
    }
    return [ordered]@{
        chunk = [string]$chunkId
        runId = [string]$run
        attempt = [string]$attempt
        suite = [string]$suite
        hostWallSeconds = $wall
        trxMissing = [bool]$missing
        manufacturedPassed = $false
        passedCount = $passed
        outcomes = @($priorOutcomes.ToArray())
        trx = [string]$trx
        trxDigest = [string]$digest
        exitCode = $exitCode
        coverageComplete = $coverage
        testsPassed = $tests
    }
}

function Write-TestPhaseExecutedManifest {
    param(
        $Verdict,
        [string]$LogRoot,
        [string]$TrxPath,
        [string]$Suite,
        [string]$Chunk,
        [string]$RunId,
        [string]$Attempt,
        [double]$HostWallSeconds = 0
    )
    $rows = @()
    $trxIdentity = Get-TestPhasePathIdentity -Path $TrxPath
    $trxMissing = ($trxIdentity.Kind -ne 'regular')
    if ($trxIdentity.Kind -eq 'regular') {
        $trxRead = Read-TestPhaseTrxRows -Path $TrxPath
        if ($null -ne $trxRead -and $trxRead.Complete -and -not $trxRead.Refused) {
            $rows = @($trxRead.Rows)
        }
    }
    $outcomes = New-Object System.Collections.Generic.List[object]
    $passed = 0
    foreach ($row in $rows) {
        $outcomes.Add([ordered]@{ testId = [string]$row.TestId; outcome = [string]$row.Outcome })
        if ([string]$row.Outcome -eq 'Passed') { $passed++ }
    }
    $digest = ''
    if ($trxIdentity.Kind -eq 'regular') {
        try { $digest = Get-NightlyFileSha256 -Path $TrxPath } catch { $digest = '' }
    }
    Write-TestPhaseOwnerObservation -Verdict $Verdict -LogRoot $LogRoot -Suite $Suite -Chunk $Chunk
    $exitCode = $null
    $coverageComplete = $null
    $testsPassed = $null
    if ($null -ne $Verdict) {
        if ($Verdict.PSObject.Properties.Name -contains 'ExitCode') { $exitCode = $Verdict.ExitCode }
        if ($Verdict.PSObject.Properties.Name -contains 'coverageComplete') { $coverageComplete = [bool]$Verdict.coverageComplete }
        if ($Verdict.PSObject.Properties.Name -contains 'testsPassed') { $testsPassed = [bool]$Verdict.testsPassed }
    }
    $fresh = ConvertTo-TestPhaseExecutedChunk -Chunk $Chunk -RunId $RunId -Attempt $Attempt -Suite $Suite -HostWallSeconds $HostWallSeconds -TrxMissing $trxMissing -PassedCount $passed -Outcomes @($outcomes.ToArray()) -Trx (ConvertTo-TestPhaseRelativePath -LogRoot $LogRoot -Path $TrxPath) -TrxDigest $digest -ExitCode $exitCode -CoverageComplete $coverageComplete -TestsPassed $testsPassed
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $LogRoot
    $path = Join-Path $dir 'groups-executed.json'
    $kept = New-Object System.Collections.Generic.List[object]
    $topRun = [string]$RunId
    $topAttempt = [string]$Attempt
    $topSuite = [string]$Suite
    if ((Get-TestPhasePathIdentity -Path $path).Kind -eq 'regular') {
        $existing = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        if ($existing.PSObject.Properties.Name -contains 'runId' -and -not [string]::IsNullOrWhiteSpace([string]$existing.runId)) { $topRun = [string]$existing.runId }
        if ($existing.PSObject.Properties.Name -contains 'attempt') { $topAttempt = [string]$existing.attempt }
        if ($existing.PSObject.Properties.Name -contains 'suite' -and -not [string]::IsNullOrWhiteSpace([string]$existing.suite)) { $topSuite = [string]$existing.suite }
        $prior = @()
        if ($existing.PSObject.Properties.Name -contains 'chunks' -and $null -ne $existing.chunks) { $prior = @($existing.chunks) }
        elseif ($existing.PSObject.Properties.Name -contains 'chunk') { $prior = @($existing) }
        foreach ($item in $prior) {
            if ($null -eq $item) { continue }
            $same = ([string]$item.chunk -eq [string]$Chunk) -and ([string]$item.suite -eq [string]$Suite)
            if ($same) { continue }
            $kept.Add((ConvertTo-TestPhaseExecutedChunk -Item $item))
        }
    }
    $kept.Add($fresh)
    $manifest = [ordered]@{
        version = 1
        runId = $topRun
        attempt = $topAttempt
        suite = $topSuite
        manufacturedPassed = $false
        chunks = @($kept.ToArray())
    }
    Write-TestPhaseTextAtomic -Path $path -Text (ConvertTo-TestPhaseJsonText -Value $manifest)
}

function Complete-TestPhaseLaunch {
    param(
        $Verdict,
        [string]$LogRoot,
        [string]$TrxPath,
        [string]$Suite,
        [string]$Chunk,
        [string]$RunId,
        [string]$Attempt,
        [double]$HostWallSeconds = 0
    )
    try {
        if ($script:TestPhaseSeams -and $script:TestPhaseSeams.Summary) {
            & $script:TestPhaseSeams.Summary
        }
        Write-TestPhaseExecutedManifest -Verdict $Verdict -LogRoot $LogRoot -TrxPath $TrxPath -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt -HostWallSeconds $HostWallSeconds
        $planned = Join-Path (Get-TestPhaseTimingDirectory -RunDirectory $LogRoot) 'groups-planned.json'
        if (Test-Path -LiteralPath $planned) {
            Invoke-TestPhaseRunSummary -RunDirectory $LogRoot -Top 50
        }
    } catch {
        Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
    }
    return $Verdict
}

function ConvertTo-TestPhaseSummaryRows {
    param($Planned, [string]$RunDirectory)
    $quality = New-Object System.Collections.Generic.List[string]
    $torn = 0
    $malformed = 0
    $shards = @()
    $timing = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    if (Test-Path -LiteralPath $timing) {
        $shards = @(Get-ChildItem -LiteralPath $timing -Filter 'cases-*.jsonl' -File -ErrorAction SilentlyContinue)
        foreach ($chunkDir in @(Get-ChildItem -LiteralPath $timing -Force -Filter 'chunk-*' -ErrorAction SilentlyContinue)) {
            $kind = Test-QualificationTimingDirectory -Path $chunkDir.FullName
            if ($kind -eq 'symlink') {
                if (-not $quality.Contains('chunk-link-refused')) { $quality.Add('chunk-link-refused') }
                continue
            }
            if ($kind -ne 'directory') { continue }
            $shards += @(Get-ChildItem -LiteralPath $chunkDir.FullName -Filter 'cases-*.jsonl' -File -ErrorAction SilentlyContinue)
        }
    }
    $completes = @{}
    $starts = @{}
    $fixtures = @()
    $seenComplete = @{}
    foreach ($shard in $shards) {
        $parsed = Read-TestPhaseJsonlForSummary -Path $shard.FullName
        $torn += [int]$parsed.Torn
        $malformed += [int]$parsed.Malformed
        $header = $null
        foreach ($rec in @($parsed.Records)) {
            if ([string]$rec.recordKind -eq 'header') { $header = $rec; break }
        }
        $stale = $false
        if ($null -ne $header) {
            if (-not [string]::IsNullOrWhiteSpace([string]$Planned.runId) -and [string]$header.runId -ne [string]$Planned.runId) { $stale = $true }
            if (-not [string]::IsNullOrWhiteSpace([string]$Planned.assembly) -and [string]$header.assembly -ne [string]$Planned.assembly) { $stale = $true }
        }
        if ($stale) {
            if (-not $quality.Contains('stale-input-not-joined')) { $quality.Add('stale-input-not-joined') }
            continue
        }
        foreach ($rec in @($parsed.Records)) {
            $kind = [string]$rec.recordKind
            if ($kind -eq 'fixture') { $fixtures += $rec; continue }
            $id = [string]$rec.testId
            if ([string]::IsNullOrWhiteSpace($id)) { continue }
            $evidenceKey = Get-TestPhaseEvidenceKey -Header $header -TestId $id
            if ($kind -eq 'start') { $starts[$evidenceKey] = $rec; continue }
            if ($kind -ne 'complete') { continue }
            if ($seenComplete.ContainsKey($evidenceKey)) {
                $prior = $completes[$evidenceKey]
                if ([string]$prior.Record.outcome -ne [string]$rec.outcome) {
                    $prior.Conflict = $true
                    if (-not $quality.Contains('conflicting-duplicate')) { $quality.Add('conflicting-duplicate') }
                }
            } else {
                $seenComplete[$evidenceKey] = $true
                $completes[$evidenceKey] = [pscustomobject]@{ Record = $rec; Header = $header; Conflict = $false }
            }
        }
    }
    if ($torn -gt 0 -and -not $quality.Contains('torn-tail')) { $quality.Add('torn-tail') }
    if ($malformed -gt 0 -and -not $quality.Contains('malformed-line')) { $quality.Add('malformed-line') }

    $trxGroups = @{}
    $trxSuites = @{}
    $executedPath = Join-Path $timing 'groups-executed.json'
    if ((Get-TestPhasePathIdentity -Path $executedPath).Kind -eq 'regular') {
        $executed = [System.IO.File]::ReadAllText($executedPath) | ConvertFrom-Json
        foreach ($chunk in @($executed.chunks)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$chunk.trx)) {
                $fullTrx = [System.IO.Path]::GetFullPath((Join-Path $RunDirectory ([string]$chunk.trx)))
                $trxGroups[$fullTrx] = [string]$chunk.chunk
                $suiteText = ''
                if ($chunk.PSObject.Properties.Name -contains 'suite') { $suiteText = [string]$chunk.suite }
                $trxSuites[$fullTrx] = $suiteText
            }
        }
    }
    $trxRows = @()
    $trxFiles = @(Get-ChildItem -LiteralPath $RunDirectory -Filter '*.trx' -File -ErrorAction SilentlyContinue)
    foreach ($trx in $trxFiles) {
        if ((Get-TestPhasePathIdentity -Path $trx.FullName).Kind -ne 'regular') { continue }
        $trxRead = Read-TestPhaseTrxRows -Path $trx.FullName
        if ($null -eq $trxRead -or -not $trxRead.Complete -or $trxRead.Refused) { continue }
        foreach ($row in @($trxRead.Rows)) {
            $group = ''
            if ($trxGroups.ContainsKey($trx.FullName)) { $group = [string]$trxGroups[$trx.FullName] }
            $row | Add-Member -NotePropertyName GroupId -NotePropertyValue $group -Force
            $suiteText = ''
            if ($trxSuites.ContainsKey($trx.FullName)) { $suiteText = [string]$trxSuites[$trx.FullName] }
            $row | Add-Member -NotePropertyName Suite -NotePropertyValue $suiteText -Force
            $trxRows += $row
        }
    }
    $nativeMap = @{}
    foreach ($row in $trxRows) {
        $keys = @([string]$row.TestId)
        foreach ($native in @(Get-TestPhaseNativeIds -Text ([string]$row.StdOut))) { $keys += [string]$native }
        foreach ($key in $keys) {
            if ([string]::IsNullOrWhiteSpace($key)) { continue }
            if (-not $nativeMap.ContainsKey($key)) { $nativeMap[$key] = New-Object System.Collections.Generic.List[object] }
            $list = $nativeMap[$key]
            $already = $false
            foreach ($existing in $list) {
                if ([string]$existing.TestId -eq [string]$row.TestId -and [string]$existing.Path -eq [string]$row.Path) { $already = $true }
            }
            if (-not $already) { $list.Add($row) }
        }
    }

    $tests = @()
    $joinedTrx = @{}
    $completeTestIds = @{}
    foreach ($completeKey in @($completes.Keys)) {
        $completeTestIds[[string]$completes[$completeKey].Record.testId] = $true
    }
    foreach ($completeKey in @($completes.Keys)) {
        $bag = $completes[$completeKey]
        $id = [string]$bag.Record.testId
        $headerSuite = ''
        if ($null -ne $bag.Header -and $bag.Header.PSObject.Properties.Name -contains 'suite') {
            $headerSuite = [string]$bag.Header.suite
        }
        $matches = @()
        if ($nativeMap.ContainsKey([string]$id)) { $matches = @($nativeMap[[string]$id].ToArray()) }
        $filtered = New-Object System.Collections.Generic.List[object]
        foreach ($match in $matches) {
            $trxSuite = ''
            if ($match.PSObject.Properties.Name -contains 'Suite') { $trxSuite = [string]$match.Suite }
            $bothSuites = (-not [string]::IsNullOrWhiteSpace($headerSuite)) -and (-not [string]::IsNullOrWhiteSpace($trxSuite))
            if ($bothSuites -and ($headerSuite -ne $trxSuite)) { continue }
            $filtered.Add($match)
        }
        $matches = @($filtered.ToArray())
        $distinct = @{}
        foreach ($match in $matches) { $distinct[[string]$match.TestId + '|' + [string]$match.Path] = $match }
        $matchRows = @($distinct.Values)
        $joined = $matchRows.Count -eq 1 -and -not $bag.Conflict
        $trxOutcome = ''
        $phaseOutcome = ''
        $duration = $null
        if ($bag.Conflict) {
            $phaseOutcome = 'conflict'
            if (-not $quality.Contains('conflicting-duplicate')) { $quality.Add('conflicting-duplicate') }
        } else {
            $phaseOutcome = [string]$bag.Record.outcome
        }
        if ($matchRows.Count -gt 1) {
            $joined = $false
            if (-not $quality.Contains('ambiguous-id')) { $quality.Add('ambiguous-id') }
        } elseif ($matchRows.Count -eq 1) {
            $trxOutcome = [string]$matchRows[0].Outcome
            $duration = [string]$matchRows[0].Duration
            $joinedTrx[[string]$matchRows[0].TestId + '|' + [string]$matchRows[0].Path] = $true
        }
        $outcome = 'incomplete'
        $incomplete = $true
        if ($joined) {
            if ($trxOutcome -eq $phaseOutcome -and -not [string]::IsNullOrWhiteSpace($trxOutcome)) {
                $outcome = $trxOutcome
                $incomplete = $false
            } else {
                $outcome = 'incomplete'
                if (-not $quality.Contains('outcome-disagreement')) { $quality.Add('outcome-disagreement') }
            }
        } else {
            if (-not $quality.Contains('missing-terminal-remains-incomplete')) { $quality.Add('missing-terminal-remains-incomplete') }
        }
        $src = $bag.Record
        $ex = $src.exclusive
        $tests += [ordered]@{
            testId = [string]$id
            suite = [string]$headerSuite
            className = [string]$src.className
            method = [string]$src.method
            duration = $duration
            groupId = [string]$bag.Header.groupId
            outcome = $outcome
            trxOutcome = $trxOutcome
            phaseOutcome = $phaseOutcome
            joined = [bool]$joined
            incomplete = [bool]$incomplete
            countsKnown = $true
            db = $(if ($ex) { [long]$ex.db } else { 0 })
            host = $(if ($ex) { [long]$ex.host } else { 0 })
            process = $(if ($ex) { [long]$ex.process } else { 0 })
            fs = $(if ($ex) { [long]$ex.fs } else { 0 })
            network = $(if ($ex) { [long]$ex.network } else { 0 })
            fixture = $(if ($ex) { [long]$ex.fixture } else { 0 })
            overlapMs = $(if ($src.overlapMs) { [long]$src.overlapMs } else { 0 })
            remainderMs = $(if ($src.remainderMs) { [long]$src.remainderMs } else { 0 })
            droppedPhaseCount = $(if ($src.droppedPhaseCount) { [int]$src.droppedPhaseCount } else { 0 })
        }
    }
    foreach ($startKey in @($starts.Keys)) {
        if ($completes.ContainsKey([string]$startKey)) { continue }
        $startRec = $starts[$startKey]
        $startId = [string]$startRec.testId
        $startSuite = ''
        if (-not $quality.Contains('missing-terminal-remains-incomplete')) { $quality.Add('missing-terminal-remains-incomplete') }
        $tests += [ordered]@{
            testId = [string]$startId
            suite = [string]$startSuite
            className = ''
            method = ''
            duration = $null
            groupId = $null
            outcome = 'incomplete'
            trxOutcome = ''
            phaseOutcome = ''
            joined = $false
            incomplete = $true
            countsKnown = $false
            db = 0; host = 0; process = 0; fs = 0; network = 0; fixture = 0
            overlapMs = 0; remainderMs = 0; droppedPhaseCount = 0
        }
    }
    foreach ($row in $trxRows) {
        $key = [string]$row.TestId + '|' + [string]$row.Path
        if ($joinedTrx.ContainsKey($key)) { continue }
        $nativeHit = $false
        foreach ($native in @(Get-TestPhaseNativeIds -Text ([string]$row.StdOut))) {
            if ($completeTestIds.ContainsKey([string]$native)) { $nativeHit = $true }
        }
        if ($nativeHit) { continue }
        if ($completeTestIds.ContainsKey([string]$row.TestId)) { continue }
        $outcome = [string]$row.Outcome
        $incomplete = $false
        $countsKnown = $false
        if ($outcome -eq 'Passed') {
            $outcome = 'unknown'
            $incomplete = $true
            if (-not $quality.Contains('missing-phase-unknown')) { $quality.Add('missing-phase-unknown') }
        }
        $rowSuite = ''
        if ($row.PSObject.Properties.Name -contains 'Suite') { $rowSuite = [string]$row.Suite }
        $tests += [ordered]@{
            testId = [string]$row.TestId
            suite = [string]$rowSuite
            className = ''
            method = [string]$row.Name
            duration = [string]$row.Duration
            groupId = [string]$row.GroupId
            outcome = [string]$outcome
            trxOutcome = [string]$row.Outcome
            phaseOutcome = ''
            joined = $false
            incomplete = [bool]$incomplete
            countsKnown = [bool]$countsKnown
            db = 0; host = 0; process = 0; fs = 0; network = 0; fixture = 0
            overlapMs = 0; remainderMs = 0; droppedPhaseCount = 0
        }
    }
    $shared = @()
    foreach ($fix in $fixtures) {
        $ex = $fix.exclusive
        $shared += [ordered]@{
            fixtureId = [string]$fix.fixtureId
            db = $(if ($ex) { [long]$ex.db } else { 0 })
            host = $(if ($ex) { [long]$ex.host } else { 0 })
            process = $(if ($ex) { [long]$ex.process } else { 0 })
            fs = $(if ($ex) { [long]$ex.fs } else { 0 })
            network = $(if ($ex) { [long]$ex.network } else { 0 })
            fixture = $(if ($ex) { [long]$ex.fixture } else { 0 })
        }
    }
    $complete = $quality.Count -eq 0
    foreach ($test in $tests) {
        if ([bool]$test.incomplete) { $complete = $false }
    }
    return [pscustomobject]@{
        Quality = @($quality.ToArray())
        Torn = $torn
        Malformed = $malformed
        Tests = @($tests)
        Shared = @($shared)
        Complete = [bool]$complete
    }
}

function Write-TestPhaseSummaryFiles {
    param($Planned, $Report, [string]$RunDirectory, [int]$Top)
    $timing = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    if (-not (Test-Path -LiteralPath $timing)) {
        New-Item -ItemType Directory -Path $timing -Force | Out-Null
    }
    $orderedTests = @($Report.Tests | Sort-Object { [string]$_.testId })
    $summary = [ordered]@{
        version = 1
        complete = [bool]$Report.Complete
        quality = @($Report.Quality)
        tornLines = [int]$Report.Torn
        malformedLines = [int]$Report.Malformed
        tests = @($orderedTests)
        sharedFixtures = @($Report.Shared)
    }
    Write-TestPhaseTextAtomic -Path (Join-Path $timing 'summary.json') -Text (ConvertTo-TestPhaseJsonText -Value $summary)

    $headers = @('testId','duration','groupId','outcome','trxOutcome','phaseOutcome','joined','incomplete','db','host','process','fs','network','fixture','overlapMs','remainderMs','droppedPhaseCount')
    $lines = New-Object System.Collections.Generic.List[string]
    $headCells = @()
    foreach ($h in $headers) { $headCells += (ConvertTo-TestPhaseCsvCell -Value $h) }
    $lines.Add(($headCells -join ','))
    foreach ($test in $orderedTests) {
        $cells = @(
            (ConvertTo-TestPhaseCsvCell -Value $test.testId),
            (ConvertTo-TestPhaseCsvCell -Value $test.duration),
            (ConvertTo-TestPhaseCsvCell -Value $test.groupId),
            (ConvertTo-TestPhaseCsvCell -Value $test.outcome),
            (ConvertTo-TestPhaseCsvCell -Value $test.trxOutcome),
            (ConvertTo-TestPhaseCsvCell -Value $test.phaseOutcome),
            (ConvertTo-TestPhaseCsvCell -Value $test.joined),
            (ConvertTo-TestPhaseCsvCell -Value $test.incomplete),
            (ConvertTo-TestPhaseCsvCell -Value $test.db),
            (ConvertTo-TestPhaseCsvCell -Value $test.host),
            (ConvertTo-TestPhaseCsvCell -Value $test.process),
            (ConvertTo-TestPhaseCsvCell -Value $test.fs),
            (ConvertTo-TestPhaseCsvCell -Value $test.network),
            (ConvertTo-TestPhaseCsvCell -Value $test.fixture),
            (ConvertTo-TestPhaseCsvCell -Value $test.overlapMs),
            (ConvertTo-TestPhaseCsvCell -Value $test.remainderMs),
            (ConvertTo-TestPhaseCsvCell -Value $test.droppedPhaseCount)
        )
        $lines.Add(($cells -join ','))
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllLines((Join-Path $timing 'tests.csv'), $lines.ToArray(), $utf8)

    $limit = $Top
    if ($limit -lt 1) { $limit = 1 }
    $ranked = @($orderedTests | Sort-Object { -1 * ([long]$_.db + [long]$_.host + [long]$_.process + [long]$_.fs + [long]$_.network + [long]$_.fixture + [long]$_.overlapMs + [long]$_.remainderMs) })
    $md = New-Object System.Collections.Generic.List[string]
    $md.Add('# Phase timing')
    $md.Add('')
    $md.Add('TRX remains authoritative. Sidecars are private and are not gate inputs.')
    $md.Add('')
    $md.Add('| test | outcome | exclusive+overlap+remainder |')
    $md.Add('| --- | --- | --- |')
    $shown = 0
    foreach ($test in $ranked) {
        if ($shown -ge $limit) { break }
        $shown++
        $total = [long]$test.db + [long]$test.host + [long]$test.process + [long]$test.fs + [long]$test.network + [long]$test.fixture + [long]$test.overlapMs + [long]$test.remainderMs
        $md.Add('| ' + (ConvertTo-TestPhaseMarkdownCell -Value $test.testId) + ' | ' + (ConvertTo-TestPhaseMarkdownCell -Value $test.outcome) + ' | ' + (ConvertTo-TestPhaseMarkdownCell -Value $total) + ' |')
    }
    $md.Add('')
    $md.Add('Shared fixture rows are separate from per-test totals.')
    [System.IO.File]::WriteAllLines((Join-Path $timing 'summary.md'), $md.ToArray(), $utf8)
}

function Invoke-TestPhaseRunSummary {
    param([string]$RunDirectory, [int]$Top = 50)
    $plannedPath = Join-Path (Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory) 'groups-planned.json'
    $docs = @(Read-TestPhasePlannedDocuments -RunDirectory $RunDirectory)
    if ($docs.Count -lt 1) { return }
    try {
        # A historical singleton is one suite. It stays the stale-check document
        # and is not treated as the membership of every other suite.
        $planned = $docs[0]
        if ((Get-TestPhasePathIdentity -Path $plannedPath).Kind -eq 'regular') {
            $planned = [System.IO.File]::ReadAllText($plannedPath) | ConvertFrom-Json
        }
        $report = ConvertTo-TestPhaseSummaryRows -Planned $planned -RunDirectory $RunDirectory
        Write-TestPhaseSummaryFiles -Planned $planned -Report $report -RunDirectory $RunDirectory -Top $Top
    } catch {
        Write-TestPhaseFailureMarker -RunDirectory $RunDirectory -Code 'phase-summary-failed'
    }
}

function Resolve-QualificationTimingRoot {
    param([string]$TimingRoot, [string]$LogRoot, [string]$RunId)
    if (-not [string]::IsNullOrWhiteSpace($TimingRoot)) { return [string]$TimingRoot }
    if ([string]::IsNullOrWhiteSpace($LogRoot)) { return '' }
    if (-not [string]::IsNullOrWhiteSpace($RunId)) { return (Join-Path $LogRoot ('timing-' + $RunId)) }
    return (Join-Path $LogRoot 'timing')
}

function Get-QualificationNearestRank {
    param($Sorted, [double]$Fraction)
    $values = @($Sorted)
    $n = $values.Count
    if ($n -lt 1) { return $null }
    $rank = [int][math]::Ceiling($Fraction * $n)
    if ($rank -lt 1) { $rank = 1 }
    if ($rank -gt $n) { $rank = $n }
    return [math]::Round([double]$values[$rank - 1], 3)
}

function Get-QualificationTrxStats {
    param(
        [string]$TrxPath,
        $Watch = $null,
        [int]$DeadlineMilliseconds = 200
    )
    $incomplete = [pscustomobject]@{ P50 = $null; P95 = $null; Max = $null; Slowest = @(); Count = 0; Complete = $false }
    if ([string]::IsNullOrWhiteSpace($TrxPath)) { return $null }
    if ($null -eq $Watch) { $Watch = [Diagnostics.Stopwatch]::StartNew() }
    if ($DeadlineMilliseconds -lt 1) { $DeadlineMilliseconds = 1 }
    $identity = Get-TestPhasePathIdentity -Path $TrxPath
    if ($identity.Kind -ne 'regular') { return $incomplete }
    if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { return $incomplete }
    $opened = Open-TestPhaseReadStream -Path $TrxPath
    if ($opened.Refused -or $null -eq $opened.Stream) { return $incomplete }
    try {
        $stats = [Antiphon.TestPhase.TrxTimingScanner]::Summarize($opened.Stream, $Watch, $DeadlineMilliseconds, 262144)
        if ($null -eq $stats -or -not $stats.Complete) { return $incomplete }
        $slowRows = New-Object System.Collections.Generic.List[object]
        $names = @($stats.SlowNames)
        $values = @($stats.SlowSeconds)
        for ($i = 0; $i -lt $names.Count; $i++) {
            $seconds = 0.0
            if ($i -lt $values.Count) { $seconds = [double]$values[$i] }
            $slowRows.Add([ordered]@{ name = [string]$names[$i]; seconds = $seconds })
        }
        $p50 = $null
        $p95 = $null
        $max = $null
        if ([int]$stats.Count -gt 0) {
            $p50 = [double]$stats.P50
            $p95 = [double]$stats.P95
            $max = [double]$stats.Max
        }
        return [pscustomobject]@{
            P50 = $p50
            P95 = $p95
            Max = $max
            Slowest = @($slowRows.ToArray())
            Count = [int]$stats.Count
            Complete = $true
        }
    } catch {
        return $incomplete
    } finally {
        try { $opened.Stream.Dispose() } catch { }
    }
}

function Get-QualificationSpanSeconds {
    param($Phase)
    if ($null -eq $Phase) { return $null }
    $start = 0.0
    $end = 0.0
    try { $start = [double]$Phase.startOffsetMs } catch { return $null }
    try { $end = [double]$Phase.endOffsetMs } catch { $end = $start }
    if ($end -lt $start) { $end = $start }
    return (($end - $start) / 1000.0)
}

function ConvertTo-QualificationShardContribution {
    param(
        $Records,
        [string]$FileName,
        [string]$Chunk,
        [string]$RunId,
        [string]$Suite,
        [string]$Attempt
    )
    $empty = [pscustomobject]@{ fixtures = @{}; processSpawn = $null; processWait = $null }
    $header = $null
    foreach ($rec in @($Records)) {
        if ([string]$rec.recordKind -eq 'header') { $header = $rec; break }
    }
    if (-not (Test-QualificationShardOwned -Header $header -FileName $FileName -Chunk $Chunk -RunId $RunId -Suite $Suite -Attempt $Attempt)) {
        return $empty
    }
    $fixtures = @{}
    $processSpawn = $null
    $processWait = $null
    foreach ($rec in @($Records)) {
        $kind = [string]$rec.recordKind
        if ($kind -eq 'fixture') {
            $fid = ''
            if ($rec.PSObject.Properties.Name -contains 'fixtureId') { $fid = [string]$rec.fixtureId }
            if ([string]::IsNullOrWhiteSpace($fid)) { $fid = [guid]::NewGuid().ToString('N') }
            if ($fixtures.ContainsKey($fid)) { continue }
            $bucket = @{ setup = $null; teardown = $null }
            foreach ($phase in @($rec.phases)) {
                $seconds = Get-QualificationSpanSeconds -Phase $phase
                if ($null -eq $seconds) { continue }
                $name = [string]$phase.phase
                if ($name -eq 'fixture-setup') {
                    if ($null -eq $bucket.setup) { $bucket.setup = 0.0 }
                    $bucket.setup = [double]$bucket.setup + [double]$seconds
                } elseif ($name -eq 'fixture-teardown') {
                    if ($null -eq $bucket.teardown) { $bucket.teardown = 0.0 }
                    $bucket.teardown = [double]$bucket.teardown + [double]$seconds
                }
            }
            $fixtures[$fid] = $bucket
        } elseif ($kind -eq 'complete') {
            foreach ($phase in @($rec.phases)) {
                $seconds = Get-QualificationSpanSeconds -Phase $phase
                if ($null -eq $seconds) { continue }
                $name = [string]$phase.phase
                if ($name -eq 'process-spawn') {
                    if ($null -eq $processSpawn) { $processSpawn = 0.0 }
                    $processSpawn = [double]$processSpawn + [double]$seconds
                } elseif ($name -eq 'process-wait') {
                    if ($null -eq $processWait) { $processWait = 0.0 }
                    $processWait = [double]$processWait + [double]$seconds
                }
            }
        }
    }
    return [pscustomobject]@{ fixtures = $fixtures; processSpawn = $processSpawn; processWait = $processWait }
}

function Test-QualificationTimingDirectory {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return 'missing' }
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        if (-not (Test-Path -LiteralPath $Path)) { return 'missing' }
        try { $attrs = [IO.File]::GetAttributes($Path) } catch { return 'unreadable' }
        $reparse = [IO.FileAttributes]::ReparsePoint
        $directory = [IO.FileAttributes]::Directory
        if (($attrs -band $reparse) -eq $reparse) { return 'symlink' }
        if (($attrs -band $directory) -eq $directory) { return 'directory' }
        return 'other'
    }
    Initialize-TestPhaseLinuxIdentity
    if (-not ('Antiphon.TestPhase.FileIdentity' -as [type])) { return 'unreadable' }
    $stat = New-Object Antiphon.TestPhase.LinuxStat
    $code = [Antiphon.TestPhase.FileIdentity]::LStat($Path, [ref]$stat)
    if ($code -ne 0) {
        $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        if ($err -eq 2) { return 'missing' }
        return 'unreadable'
    }
    $kind = ([uint32]$stat.Mode) -band 61440
    if ($kind -eq 16384) { return 'directory' }
    if ($kind -eq 40960) { return 'symlink' }
    if ($kind -eq 4096) { return 'fifo' }
    return 'other'
}

function Read-QualificationTimingJsonl {
    param(
        [string]$Path,
        $Watch = $null,
        [int]$DeadlineMilliseconds = 200
    )
    $none = [pscustomobject]@{ Records = @(); Complete = $false }
    if ([string]::IsNullOrWhiteSpace($Path)) { return $none }
    if ($null -eq $Watch) { $Watch = [Diagnostics.Stopwatch]::StartNew() }
    if ($DeadlineMilliseconds -lt 1) { $DeadlineMilliseconds = 1 }
    $identity = Get-TestPhasePathIdentity -Path $Path
    if ($identity.Kind -ne 'regular') { return $none }
    if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { return $none }
    $opened = Open-TestPhaseReadStream -Path $Path
    if ($opened.Refused -or $null -eq $opened.Stream) { return $none }
    try {
        $read = [Antiphon.TestPhase.JsonlTimingScanner]::Read($opened.Stream, $Watch, $DeadlineMilliseconds, 262144)
        if ($null -eq $read -or -not $read.Complete) { return $none }
        $records = New-Object System.Collections.Generic.List[object]
        foreach ($line in $read.Lines) {
            if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { return $none }
            $parsed = $null
            try { $parsed = $line | ConvertFrom-Json } catch { $parsed = $null }
            if ($null -ne $parsed) { $records.Add($parsed) }
        }
        return [pscustomobject]@{ Records = @($records.ToArray()); Complete = $true }
    } catch {
        return $none
    } finally {
        try { $opened.Stream.Dispose() } catch { }
    }
}

function ConvertTo-QualificationGroupToken {
    param([string]$Chunk)
    if ([string]::IsNullOrEmpty($Chunk)) { return '' }
    $bytes = [Text.Encoding]::UTF8.GetBytes([string]$Chunk)
    $sb = New-Object System.Text.StringBuilder ($bytes.Length * 2)
    foreach ($b in $bytes) { [void]$sb.Append($b.ToString('x2')) }
    return $sb.ToString()
}

# Length-prefixed chunk, run id, attempt, and suite. LF separators. SHA-256
# lowercase hex. The same text is hashed by TestTimingSession.IdentityToken.
function ConvertTo-QualificationIdentityText {
    param([string]$Chunk, [string]$RunId, [string]$Attempt, [string]$Suite)
    if ($null -eq $Chunk) { $Chunk = '' }
    if ($null -eq $RunId) { $RunId = '' }
    if ($null -eq $Attempt) { $Attempt = '' }
    if ($null -eq $Suite) { $Suite = '' }
    return ([string]$Chunk.Length + ':' + [string]$Chunk + "`n" + [string]$RunId.Length + ':' + [string]$RunId + "`n" + [string]$Attempt.Length + ':' + [string]$Attempt + "`n" + [string]$Suite.Length + ':' + [string]$Suite)
}

function ConvertTo-QualificationChunkIdentity {
    param([string]$Chunk, [string]$RunId, [string]$Attempt, [string]$Suite)
    $text = ConvertTo-QualificationIdentityText -Chunk $Chunk -RunId $RunId -Attempt $Attempt -Suite $Suite
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($text)
        $hash = $sha.ComputeHash($bytes)
    } finally {
        $sha.Dispose()
    }
    $sb = New-Object System.Text.StringBuilder ($hash.Length * 2)
    foreach ($b in $hash) { [void]$sb.Append($b.ToString('x2')) }
    return $sb.ToString()
}

function Get-QualificationChunkDirectoryName {
    param([string]$Chunk, [string]$RunId, [string]$Attempt, [string]$Suite)
    return ('chunk-' + (ConvertTo-QualificationChunkIdentity -Chunk $Chunk -RunId $RunId -Attempt $Attempt -Suite $Suite))
}

function ConvertFrom-QualificationOwnerName {
    param([string]$Name)
    if ($Name -notmatch '^own-([0-9a-f]+)-(\d+)-(.+)\.txt$') { return $null }
    return [pscustomobject]@{
        Token = [string]$Matches[1]
        Journal = ('cases-' + [string]$Matches[2] + '-' + [string]$Matches[3] + '.jsonl')
    }
}

# Same budget as TestTimingSession.OwnerHashPrefixLength: the longest prefix
# whose owner path is still 240 characters under the real candidate root.
function Get-QualificationOwnerPrefixLength {
    $proc = '2147483647'
    $nonce = '0' * 32
    $windowsRoot = 'C:\Antiphon\releases\candidates\rc-2026-10-27-' + ('0' * 12) + '\logs\timing-' + ('0' * 32)
    $bare = $windowsRoot + '\chunk-' + ('0' * 64) + '\own--' + $proc + '-' + $nonce + '.txt'
    $room = 240 - $bare.Length
    if ($room -lt 1) { return 1 }
    if ($room -gt 64) { return 64 }
    return $room
}

function Test-QualificationOwnerToken {
    param([string]$Token, [string]$Identity)
    if ([string]::IsNullOrEmpty($Token) -or [string]::IsNullOrEmpty($Identity)) { return $false }
    if ($Token -ceq $Identity) { return $true }
    $prefix = Get-QualificationOwnerPrefixLength
    if ($Token.Length -ne $prefix) { return $false }
    if ($Identity.Length -lt $prefix) { return $false }
    if ($Identity.Substring(0, $prefix) -ceq $Token) { return $true }
    return $false
}

function New-QualificationEmptyContribution {
    return [pscustomobject]@{ fixtures = @{}; processSpawn = $null; processWait = $null }
}

function Test-QualificationShardOwned {
    param($Header, [string]$FileName, [string]$Chunk, [string]$RunId, [string]$Suite, [string]$Attempt)
    if ($null -eq $Header) { return $false }
    if ([string]$Header.recordKind -ne 'header') { return $false }
    if ([string]$Header.groupId -ne [string]$Chunk) { return $false }
    if ([string]$Header.runId -ne [string]$RunId) { return $false }
    if ([string]$Header.suite -ne [string]$Suite) { return $false }
    if ([string]$Header.attempt -ne [string]$Attempt) { return $false }
    $filePid = ''
    $fileNonce = ''
    if ($FileName -match '^cases-(\d+)-([^.]+)\.jsonl$') {
        $filePid = [string]$Matches[1]
        $fileNonce = [string]$Matches[2]
    } else {
        return $false
    }
    $headerPid = ''
    if ($Header.PSObject.Properties.Name -contains 'pid' -and $null -ne $Header.pid -and -not [string]::IsNullOrWhiteSpace([string]$Header.pid)) {
        $headerPid = [string]$Header.pid
    }
    $headerNonce = ''
    if ($Header.PSObject.Properties.Name -contains 'nonce' -and $null -ne $Header.nonce -and -not [string]::IsNullOrWhiteSpace([string]$Header.nonce)) {
        $headerNonce = [string]$Header.nonce
    }
    if ($headerPid -ne '' -and $headerPid -ne $filePid) { return $false }
    if ($headerNonce -ne '' -and $headerNonce -ne $fileNonce) { return $false }
    return $true
}

function Read-QualificationShardHeader {
    param(
        [string]$Path,
        $Watch = $null,
        [int]$DeadlineMilliseconds = 200
    )
    $refused = [pscustomobject]@{ Header = $null; Refused = $true }
    $absent = [pscustomobject]@{ Header = $null; Refused = $false }
    if ([string]::IsNullOrWhiteSpace($Path)) { return $refused }
    if ($null -eq $Watch) { $Watch = [Diagnostics.Stopwatch]::StartNew() }
    if ($DeadlineMilliseconds -lt 1) { $DeadlineMilliseconds = 1 }
    if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { return $refused }
    $opened = Open-TestPhaseReadStream -Path $Path
    if ($opened.Refused -or $null -eq $opened.Stream) { return $refused }
    try {
        $read = [Antiphon.TestPhase.JsonlTimingScanner]::ReadFirstLine($opened.Stream, $Watch, $DeadlineMilliseconds, 262144)
        if ($null -eq $read -or $read.Refused) { return $refused }
        $text = [string]$read.Line
        if ([string]::IsNullOrWhiteSpace($text)) { return $absent }
        if ($text.IndexOf('"recordKind":"header"', [StringComparison]::Ordinal) -lt 0) { return $absent }
        $parsed = $null
        try { $parsed = $text | ConvertFrom-Json } catch { $parsed = $null }
        if ($null -eq $parsed -or [string]$parsed.recordKind -ne 'header') { return $absent }
        return [pscustomobject]@{ Header = $parsed; Refused = $false }
    } catch {
        return $refused
    } finally {
        try { $opened.Stream.Dispose() } catch { }
    }
}

function Get-QualificationPhaseSums {
    param(
        [string]$TimingRoot,
        [string]$Chunk,
        [string]$RunId = '',
        [string]$Suite = '',
        [string]$Attempt = '',
        $Watch = $null,
        [int]$DeadlineMilliseconds = 500
    )
    $empty = [pscustomobject]@{
        fixtureSetupSeconds = $null
        fixtureTeardownSeconds = $null
        processSpawnSeconds = $null
        processWaitSeconds = $null
        refused = $false
    }
    if ([string]::IsNullOrWhiteSpace($TimingRoot)) { return $empty }
    $listing = Test-QualificationTimingDirectory -Path $TimingRoot
    if ($listing -eq 'missing') { return $empty }
    if ($listing -ne 'directory') {
        $empty.refused = $true
        return $empty
    }
    if (-not $script:QualificationPhaseShardCache) { $script:QualificationPhaseShardCache = @{} }
    $ownedWatch = $false
    if ($null -eq $Watch) {
        $Watch = [Diagnostics.Stopwatch]::StartNew()
        $ownedWatch = $true
    }
    if ($DeadlineMilliseconds -lt 1) { $DeadlineMilliseconds = 1 }
    $seenFixtures = @{}
    $fixtureSetup = $null
    $fixtureTeardown = $null
    $processSpawn = $null
    $processWait = $null
    $refused = $false
    # The directory name is the hash of this chunk, run, attempt, and suite.
    # Listing it does not enumerate another chunk or an earlier attempt.
    # A sidecar for a different identity does not hide a journal. A journal
    # with no matching sidecar is attributed from its first record.
    $folderName = Get-QualificationChunkDirectoryName -Chunk $Chunk -RunId $RunId -Attempt $Attempt -Suite $Suite
    $folder = Join-Path $TimingRoot $folderName
    $folderKind = Test-QualificationTimingDirectory -Path $folder
    if ($folderKind -eq 'missing') { return $empty }
    if ($folderKind -ne 'directory') {
        $empty.refused = $true
        return $empty
    }
    $entries = @(Get-ChildItem -LiteralPath $folder -Force -ErrorAction SilentlyContinue)
    $shards = @($entries | Where-Object { $_.Name -like 'cases-*.jsonl' })
    $ourToken = ConvertTo-QualificationChunkIdentity -Chunk $Chunk -RunId $RunId -Attempt $Attempt -Suite $Suite
    $ownedNames = @{}
    foreach ($entry in $entries) {
        if ([string]$entry.Name -notlike 'own-*.txt') { continue }
        $owner = ConvertFrom-QualificationOwnerName -Name $entry.Name
        if ($null -eq $owner) { continue }
        if (Test-QualificationOwnerToken -Token $owner.Token -Identity $ourToken) {
            $ownedNames[$owner.Journal] = $true
        }
    }
    foreach ($shard in $shards) {
        if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { $refused = $true; break }
        $name = [string]$shard.Name
        $shardIdentity = Get-TestPhasePathIdentity -Path $shard.FullName
        if ($shardIdentity.Kind -ne 'regular') { $refused = $true; continue }
        $identity = ([string]$RunId + '|' + [string]$Suite + '|' + [string]$Attempt + '|' + [string]$Chunk)
        $stamp = 0L
        try { $stamp = [int64]$shard.LastWriteTimeUtc.Ticks } catch { $stamp = 0L }
        $key = ([string]$shard.FullName + '|' + [string]$shardIdentity.Size + '|' + [string]$stamp + '|' + $identity)
        $contribution = $null
        if ($script:QualificationPhaseShardCache.ContainsKey($key)) {
            $contribution = $script:QualificationPhaseShardCache[$key]
        } else {
            $left = $DeadlineMilliseconds - [int]$Watch.ElapsedMilliseconds
            if ($left -lt 1) { $refused = $true; break }
            $slice = $left
            if ($ownedWatch -and $slice -gt 200) { $slice = 200 }
            $owned = $ownedNames.ContainsKey($name)
            if (-not $owned) {
                if ($ownedWatch) {
                    $headerRead = Read-QualificationShardHeader -Path $shard.FullName -DeadlineMilliseconds $slice
                } else {
                    $headerRead = Read-QualificationShardHeader -Path $shard.FullName -DeadlineMilliseconds $DeadlineMilliseconds -Watch $Watch
                }
                if ($headerRead.Refused) { $refused = $true; continue }
                $owned = Test-QualificationShardOwned -Header $headerRead.Header -FileName $name -Chunk $Chunk -RunId $RunId -Suite $Suite -Attempt $Attempt
            }
            if (-not $owned) {
                $contribution = New-QualificationEmptyContribution
                $script:QualificationPhaseShardCache[$key] = $contribution
            } else {
                if ($ownedWatch) {
                    $parsed = Read-QualificationTimingJsonl -Path $shard.FullName -DeadlineMilliseconds $slice
                } else {
                    $parsed = Read-QualificationTimingJsonl -Path $shard.FullName -DeadlineMilliseconds $DeadlineMilliseconds -Watch $Watch
                }
                if (-not $parsed.Complete) { $refused = $true }
                $contribution = ConvertTo-QualificationShardContribution -Records $parsed.Records -FileName $shard.Name -Chunk $Chunk -RunId $RunId -Suite $Suite -Attempt $Attempt
                if ($parsed.Complete) { $script:QualificationPhaseShardCache[$key] = $contribution }
            }
        }
        if ($null -eq $contribution) { continue }
        foreach ($fid in @($contribution.fixtures.Keys)) {
            if ($seenFixtures.ContainsKey([string]$fid)) { continue }
            $seenFixtures[[string]$fid] = $true
            $bucket = $contribution.fixtures[$fid]
            if ($null -ne $bucket.setup) {
                if ($null -eq $fixtureSetup) { $fixtureSetup = 0.0 }
                $fixtureSetup = [double]$fixtureSetup + [double]$bucket.setup
            }
            if ($null -ne $bucket.teardown) {
                if ($null -eq $fixtureTeardown) { $fixtureTeardown = 0.0 }
                $fixtureTeardown = [double]$fixtureTeardown + [double]$bucket.teardown
            }
        }
        if ($null -ne $contribution.processSpawn) {
            if ($null -eq $processSpawn) { $processSpawn = 0.0 }
            $processSpawn = [double]$processSpawn + [double]$contribution.processSpawn
        }
        if ($null -ne $contribution.processWait) {
            if ($null -eq $processWait) { $processWait = 0.0 }
            $processWait = [double]$processWait + [double]$contribution.processWait
        }
    }
    if ($null -ne $fixtureSetup) { $empty.fixtureSetupSeconds = [math]::Round([double]$fixtureSetup, 3) }
    if ($null -ne $fixtureTeardown) { $empty.fixtureTeardownSeconds = [math]::Round([double]$fixtureTeardown, 3) }
    if ($null -ne $processSpawn) { $empty.processSpawnSeconds = [math]::Round([double]$processSpawn, 3) }
    if ($null -ne $processWait) { $empty.processWaitSeconds = [math]::Round([double]$processWait, 3) }
    $empty.refused = [bool]$refused
    return $empty
}

function ConvertTo-QualificationStampText {
    param($Value)
    if ($null -eq $Value) { return '' }
    if ($Value -is [datetime]) { return ([datetime]$Value).ToUniversalTime().ToString('o') }
    return [string]$Value
}

function ConvertFrom-QualificationJToken {
    param($Token)
    if ($null -eq $Token) { return $null }
    if ($Token -is [Newtonsoft.Json.Linq.JObject]) {
        $map = [ordered]@{}
        foreach ($prop in $Token.Properties()) {
            $map[[string]$prop.Name] = ConvertFrom-QualificationJToken -Token $prop.Value
        }
        return [pscustomobject]$map
    }
    if ($Token -is [Newtonsoft.Json.Linq.JArray]) {
        $items = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Token) { $items.Add((ConvertFrom-QualificationJToken -Token $item)) }
        return ,@($items.ToArray())
    }
    if ($Token -is [Newtonsoft.Json.Linq.JValue]) { return $Token.Value }
    return $null
}

function Test-QualificationSidecarDense {
    param([string]$Text, [int]$Limit, $Watch, [int]$DeadlineMilliseconds)
    if ([string]::IsNullOrEmpty($Text)) { return $false }
    $commas = 0
    $colons = 0
    $commaAt = 0
    $colonAt = 0
    while ($commaAt -ge 0 -and $commas -le $Limit) {
        if ($null -ne $Watch -and $Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { return $true }
        $commaAt = $Text.IndexOf(',', $commaAt, [StringComparison]::Ordinal)
        if ($commaAt -lt 0) { break }
        $commas++
        $commaAt++
    }
    while ($colonAt -ge 0 -and $colons -le $Limit) {
        if ($null -ne $Watch -and $Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { return $true }
        $colonAt = $Text.IndexOf(':', $colonAt, [StringComparison]::Ordinal)
        if ($colonAt -lt 0) { break }
        $colons++
        $colonAt++
    }
    return ($commas -gt $Limit -or $colons -gt $Limit)
}

function Read-QualificationSidecar {
    param(
        [string]$Path,
        $Watch = $null,
        [int]$DeadlineMilliseconds = 200
    )
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    if ($null -eq $Watch) { $Watch = [Diagnostics.Stopwatch]::StartNew() }
    if ($DeadlineMilliseconds -lt 1) { $DeadlineMilliseconds = 1 }
    if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { return $null }
    $identity = Get-TestPhasePathIdentity -Path $Path
    if ($identity.Kind -ne 'regular' -or [int64]$identity.Size -gt 262144) { return $null }
    $read = Read-TestPhaseBoundedBytes -Path $Path -MaxBytes 262144 -DeadlineMilliseconds $DeadlineMilliseconds -Watch $Watch
    if ($null -eq $read -or $read.Refused -or -not $read.Complete -or $null -eq $read.Bytes) { return $null }
    if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { return $null }
    $text = [Text.Encoding]::UTF8.GetString($read.Bytes)
    if (Test-QualificationSidecarDense -Text $text -Limit 4000 -Watch $Watch -DeadlineMilliseconds $DeadlineMilliseconds) { return $null }
    if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { return $null }
    if (-not ('Newtonsoft.Json.JsonConvert' -as [type])) {
        $dll = Join-Path $PSHome 'Newtonsoft.Json.dll'
        if (Test-Path -LiteralPath $dll) { Add-Type -Path $dll }
    }
    if ('Newtonsoft.Json.JsonConvert' -as [type]) {
        $settings = New-Object Newtonsoft.Json.JsonSerializerSettings
        $settings.DateParseHandling = [Newtonsoft.Json.DateParseHandling]::None
        $token = [Newtonsoft.Json.JsonConvert]::DeserializeObject($text, $settings)
        return (ConvertFrom-QualificationJToken -Token $token)
    }
    try { return ($text | ConvertFrom-Json) } catch { return $null }
}

function Get-TestPhaseEvidenceKey {
    param($Header, [string]$TestId)
    $suite = ''
    if ($null -ne $Header -and $Header.PSObject.Properties.Name -contains 'suite') {
        $suite = [string]$Header.suite
    }
    if (-not [string]::IsNullOrWhiteSpace($suite)) { return ($suite + '|' + [string]$TestId) }
    return [string]$TestId
}

function Get-TestPhaseNoteValue {
    param($Object, [string[]]$Names)
    if ($null -eq $Object) { return $null }
    $props = @($Object.PSObject.Properties.Name)
    foreach ($name in @($Names)) {
        if ($props -contains $name) { return $Object.$name }
    }
    return $null
}

function Test-TestPhaseNotePresent {
    param($Object, [string[]]$Names)
    if ($null -eq $Object) { return $false }
    $props = @($Object.PSObject.Properties.Name)
    foreach ($name in @($Names)) {
        if ($props -contains $name) { return $true }
    }
    return $false
}

function ConvertTo-TestPhaseOwnerImage {
    param($Image)
    $text = ''
    if ($null -ne $Image) { $text = [string]$Image }
    if ([string]::IsNullOrWhiteSpace($text)) { return 'unknown' }
    return $text
}

function Write-TestPhaseOwnerObservation {
    param($Verdict, [string]$LogRoot, [string]$Suite, [string]$Chunk)
    try {
        if ($null -eq $Verdict) { return }
        $names = @(
            'rootExitedAtUtc', 'RootExitedAtUtc', 'jobEmpty', 'JobEmpty', 'JobEmptyObserved',
            'descendants', 'Descendants', 'missedDescendantPids', 'MissedDescendantPids',
            'drainMilliseconds', 'DrainMilliseconds', 'executionMilliseconds', 'ExecutionMilliseconds',
            'descendantIdentities', 'DescendantIdentities'
        )
        if (-not (Test-TestPhaseNotePresent -Object $Verdict -Names $names)) { return }
        if ($Suite -notmatch '^[a-z0-9][a-z0-9-]{0,40}$') { return }
        if ($Chunk -notmatch '^[a-z0-9][a-z0-9._-]{0,80}$') { return }
        $missed = @{}
        $missedRaw = Get-TestPhaseNoteValue -Object $Verdict -Names @('missedDescendantPids', 'MissedDescendantPids')
        if ($null -ne $missedRaw) {
            foreach ($childPid in @($missedRaw)) {
                if ($null -eq $childPid -or [string]::IsNullOrWhiteSpace([string]$childPid)) { continue }
                $missed[[string]([int]$childPid)] = $true
            }
        }
        $rows = New-Object System.Collections.Generic.List[object]
        $descRaw = Get-TestPhaseNoteValue -Object $Verdict -Names @('descendants', 'Descendants')
        if ($null -ne $descRaw) {
            foreach ($row in @($descRaw)) {
                if ($null -eq $row) { continue }
                $pidValue = Get-TestPhaseNoteValue -Object $row -Names @('pid', 'Pid')
                $childPid = 0
                if ($null -ne $pidValue -and -not [string]::IsNullOrWhiteSpace([string]$pidValue)) { $childPid = [int]$pidValue }
                $image = ConvertTo-TestPhaseOwnerImage (Get-TestPhaseNoteValue -Object $row -Names @('image', 'Image', 'ProcessName'))
                if ($missed.ContainsKey([string]$childPid)) { $image = 'unknown' }
                $started = Get-TestPhaseNoteValue -Object $row -Names @('startedAtUtc', 'StartedAtUtc')
                $exited = Get-TestPhaseNoteValue -Object $row -Names @('exitedAtUtc', 'ExitedAtUtc')
                $startedText = ''
                $exitedText = ''
                if ($null -ne $started) { $startedText = [string]$started }
                if ($null -ne $exited) { $exitedText = [string]$exited }
                $rows.Add([ordered]@{ pid = $childPid; image = [string]$image; startedAtUtc = $startedText; exitedAtUtc = $exitedText })
            }
        }
        if ($rows.Count -eq 0) {
            $identRaw = Get-TestPhaseNoteValue -Object $Verdict -Names @('descendantIdentities', 'DescendantIdentities')
            $imageRaw = Get-TestPhaseNoteValue -Object $Verdict -Names @('descendantImages', 'DescendantImages')
            $exitRaw = Get-TestPhaseNoteValue -Object $Verdict -Names @('descendantExitedAtUtc', 'DescendantExitedAtUtc')
            $idents = @()
            $images = @()
            $exitStamps = @()
            if ($null -ne $identRaw) { $idents = @($identRaw) }
            if ($null -ne $imageRaw) { $images = @($imageRaw) }
            if ($null -ne $exitRaw) { $exitStamps = @($exitRaw) }
            for ($i = 0; $i -lt $idents.Count; $i++) {
                $ident = [string]$idents[$i]
                $colon = $ident.IndexOf(':')
                $childPid = 0
                $startedText = ''
                if ($colon -gt 0) {
                    $childPid = [int]$ident.Substring(0, $colon)
                    $startedText = $ident.Substring($colon + 1)
                }
                $image = 'unknown'
                if ($i -lt $images.Count) { $image = ConvertTo-TestPhaseOwnerImage $images[$i] }
                if ($missed.ContainsKey([string]$childPid)) { $image = 'unknown' }
                $exitedText = ''
                if ($i -lt $exitStamps.Count -and $null -ne $exitStamps[$i]) { $exitedText = [string]$exitStamps[$i] }
                $rows.Add([ordered]@{ pid = $childPid; image = [string]$image; startedAtUtc = $startedText; exitedAtUtc = $exitedText })
            }
        }
        $job = $false
        if (Test-TestPhaseNotePresent -Object $Verdict -Names @('jobEmpty', 'JobEmpty', 'JobEmptyObserved')) {
            $jobValue = Get-TestPhaseNoteValue -Object $Verdict -Names @('jobEmpty', 'JobEmpty', 'JobEmptyObserved')
            if ($null -ne $jobValue) { $job = [bool]$jobValue }
        }
        $drain = 0.0
        $drainValue = Get-TestPhaseNoteValue -Object $Verdict -Names @('drainMilliseconds', 'DrainMilliseconds')
        if ($null -ne $drainValue) { $drain = [double]$drainValue }
        $execution = 0.0
        $executionValue = Get-TestPhaseNoteValue -Object $Verdict -Names @('executionMilliseconds', 'ExecutionMilliseconds')
        if ($null -ne $executionValue) { $execution = [double]$executionValue }
        $exitedAt = ''
        $exitedValue = Get-TestPhaseNoteValue -Object $Verdict -Names @('rootExitedAtUtc', 'RootExitedAtUtc')
        if ($null -ne $exitedValue) { $exitedAt = [string]$exitedValue }
        $doc = [ordered]@{
            version = 1
            suite = [string]$Suite
            chunk = [string]$Chunk
            rootExitedAtUtc = $exitedAt
            jobEmpty = [bool]$job
            drainMilliseconds = $drain
            executionMilliseconds = $execution
            descendants = @($rows.ToArray())
        }
        $dir = Get-TestPhaseTimingDirectory -RunDirectory $LogRoot
        $path = Join-Path $dir ('owner-' + $Suite + '-' + $Chunk + '.json')
        Write-TestPhaseTextAtomic -Path $path -Text (ConvertTo-TestPhaseJsonText -Value $doc)
    } catch {
        # Owner stamps are diagnostic. A write failure must not change the verdict.
    }
}

function Update-TestPhasePlannedIndex {
    param(
        [string]$Directory,
        [string]$Suite,
        [string]$FileName,
        [string]$RunId,
        [string]$Attempt,
        [string]$AssemblyId
    )
    if ($Suite -notmatch '^[a-z0-9][a-z0-9-]{0,40}$') { return }
    if ($FileName -notmatch '^groups-planned-[a-z0-9][a-z0-9-]{0,40}\.json$') { return }
    $path = Join-Path $Directory 'groups-planned-index.json'
    $kept = New-Object System.Collections.Generic.List[object]
    if ((Get-TestPhasePathIdentity -Path $path).Kind -eq 'regular') {
        $existing = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        if ($existing.PSObject.Properties.Name -contains 'suites' -and $null -ne $existing.suites) {
            foreach ($row in @($existing.suites)) {
                if ($null -eq $row) { continue }
                if ([string]$row.suite -eq $Suite) { continue }
                $kept.Add([ordered]@{
                    suite = [string]$row.suite
                    file = [string]$row.file
                    runId = [string]$row.runId
                    attempt = [string]$row.attempt
                    assembly = [string]$row.assembly
                })
            }
        }
    }
    $kept.Add([ordered]@{
        suite = [string]$Suite
        file = [string]$FileName
        runId = [string]$RunId
        attempt = [string]$Attempt
        assembly = [string]$AssemblyId
    })
    $doc = [ordered]@{
        version = 1
        runId = [string]$RunId
        attempt = [string]$Attempt
        suites = @($kept.ToArray())
    }
    Write-TestPhaseTextAtomic -Path $path -Text (ConvertTo-TestPhaseJsonText -Value $doc)
}

function Read-TestPhasePlannedDocuments {
    param([string]$RunDirectory)
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    $docs = New-Object System.Collections.Generic.List[object]
    $indexPath = Join-Path $dir 'groups-planned-index.json'
    if ((Get-TestPhasePathIdentity -Path $indexPath).Kind -eq 'regular') {
        $index = [System.IO.File]::ReadAllText($indexPath) | ConvertFrom-Json
        if ($index.PSObject.Properties.Name -contains 'suites' -and $null -ne $index.suites) {
            foreach ($row in @($index.suites)) {
                if ($null -eq $row) { continue }
                $file = ''
                if ($row.PSObject.Properties.Name -contains 'file') { $file = [string]$row.file }
                if ($file -notmatch '^groups-planned-[a-z0-9][a-z0-9-]{0,40}\.json$') { continue }
                $docPath = Join-Path $dir $file
                if ((Get-TestPhasePathIdentity -Path $docPath).Kind -ne 'regular') { continue }
                $docs.Add(([System.IO.File]::ReadAllText($docPath) | ConvertFrom-Json))
            }
        }
    }
    if ($docs.Count -eq 0) {
        $singleton = Join-Path $dir 'groups-planned.json'
        if ((Get-TestPhasePathIdentity -Path $singleton).Kind -eq 'regular') {
            $docs.Add(([System.IO.File]::ReadAllText($singleton) | ConvertFrom-Json))
        }
    }
    return @($docs.ToArray())
}

function Write-QualificationRunPhase {
    param(
        [string]$LogRoot,
        [string]$Name,
        [string]$StartedAt,
        [string]$EndedAt,
        $Seconds
    )
    if ([string]::IsNullOrWhiteSpace($LogRoot) -or [string]::IsNullOrWhiteSpace($Name)) { return }
    if ($Name -notmatch '^[a-z0-9][a-z0-9-]{0,40}$') { return }
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $LogRoot
    $path = Join-Path $dir 'setup-phases.json'
    $kept = New-Object System.Collections.Generic.List[object]
    if ((Get-TestPhasePathIdentity -Path $path).Kind -eq 'regular') {
        $existing = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        if ($existing.PSObject.Properties.Name -contains 'phases' -and $null -ne $existing.phases) {
            foreach ($row in @($existing.phases)) {
                if ($null -eq $row) { continue }
                if ([string]$row.name -eq $Name) { continue }
                $phaseSeconds = 0.0
                if ($null -ne $row.seconds) { $phaseSeconds = [double]$row.seconds }
                $kept.Add([ordered]@{
                    name = [string]$row.name
                    startedAt = [string]$row.startedAt
                    endedAt = [string]$row.endedAt
                    seconds = $phaseSeconds
                })
            }
        }
    }
    $phaseSeconds = 0.0
    if ($null -ne $Seconds) { $phaseSeconds = [double]$Seconds }
    $kept.Add([ordered]@{
        name = [string]$Name
        startedAt = [string]$StartedAt
        endedAt = [string]$EndedAt
        seconds = $phaseSeconds
    })
    $doc = [ordered]@{ version = 1; phases = @($kept.ToArray()) }
    Write-TestPhaseTextAtomic -Path $path -Text (ConvertTo-TestPhaseJsonText -Value $doc)
}

function Get-QualificationSetupAccounting {
    param([string]$LogRoot)
    $setup = 0.0
    $overlapped = $false
    $countedOnce = $false
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $LogRoot
    $path = Join-Path $dir 'setup-phases.json'
    $intervals = New-Object System.Collections.Generic.List[object]
    if ((Get-TestPhasePathIdentity -Path $path).Kind -eq 'regular') {
        $existing = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        $countedOnce = $true
        if ($existing.PSObject.Properties.Name -contains 'phases' -and $null -ne $existing.phases) {
            foreach ($row in @($existing.phases)) {
                if ($null -eq $row) { continue }
                if ($null -ne $row.seconds) { $setup += [double]$row.seconds }
                $startTicks = $null
                $endTicks = $null
                try { $startTicks = [datetimeoffset]::Parse([string]$row.startedAt).UtcTicks } catch { $startTicks = $null }
                try { $endTicks = [datetimeoffset]::Parse([string]$row.endedAt).UtcTicks } catch { $endTicks = $null }
                if ($null -ne $startTicks -and $null -ne $endTicks) {
                    $intervals.Add([pscustomobject]@{ StartTicks = [int64]$startTicks; EndTicks = [int64]$endTicks })
                }
            }
        }
    }
    $ordered = @($intervals.ToArray() | Sort-Object StartTicks)
    for ($i = 1; $i -lt $ordered.Count; $i++) {
        if ([int64]$ordered[$i].StartTicks -lt [int64]$ordered[$i - 1].EndTicks) { $overlapped = $true }
    }
    $sum = 0.0
    if (-not [string]::IsNullOrWhiteSpace($LogRoot) -and (Test-Path -LiteralPath $LogRoot)) {
        foreach ($file in @(Get-ChildItem -LiteralPath $LogRoot -Filter '*.timing.json' -File -ErrorAction SilentlyContinue)) {
            try {
                $row = [System.IO.File]::ReadAllText($file.FullName) | ConvertFrom-Json
                if ($row.PSObject.Properties.Name -contains 'buildSeconds' -and $null -ne $row.buildSeconds) {
                    $sum += [double]$row.buildSeconds
                }
            } catch { }
        }
    }
    return [pscustomobject]@{
        countedOnce = [bool]$countedOnce
        setupSeconds = [double]$setup
        perChunkBuildSum = [double]$sum
        overlapped = [bool]$overlapped
    }
}

function Initialize-TestPhaseReadWarmup {
    # Compile the bounded reader before a measured call. A fresh process
    # otherwise spends the read budget on the first invocation.
    if ($script:TestPhaseReadWarmed) { return }
    $script:TestPhaseReadWarmed = $true
    $dir = $null
    try {
        $dir = Join-Path ([IO.Path]::GetTempPath()) ('antiphon-timing-warm-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $path = Join-Path $dir 'cases-0-warm.jsonl'
        $bytes = New-Object byte[] 262145
        [IO.File]::WriteAllBytes($path, $bytes)
        $null = Read-TestPhaseJsonlBounded -Path $path -MaxBytes 262144 -MaxLines 4000 -DeadlineMilliseconds 200
        $null = Read-QualificationTimingJsonl -Path $path -DeadlineMilliseconds 200
        $warmDir = Join-Path $dir (Get-QualificationChunkDirectoryName -Chunk 'warm' -RunId 'warm' -Attempt '0' -Suite 'warm')
        New-Item -ItemType Directory -Path $warmDir -Force | Out-Null
        $good = Join-Path $warmDir 'cases-1-warm.jsonl'
        [System.IO.File]::WriteAllText($good, '{"recordKind":"header","pid":1,"nonce":"warm","runId":"warm","groupId":"warm","attempt":"0","suite":"warm"}' + "`n")
        $null = Read-QualificationTimingJsonl -Path $good -DeadlineMilliseconds 200
        $trx = Join-Path $dir 'warm.trx'
        [System.IO.File]::WriteAllText($trx, '<TestRun><Results><UnitTestResult testName="warm" duration="00:00:01" outcome="Passed" /></Results></TestRun>')
        $null = Get-QualificationTrxStats -TrxPath $trx
        $null = Read-TestPhaseTrxRows -Path $trx
        $null = Get-QualificationPhaseSums -TimingRoot $dir -Chunk 'warm' -RunId 'warm' -Suite 'warm' -Attempt '0'
        $side = Join-Path $dir 'side.json'
        [System.IO.File]::WriteAllText($side, '{}')
        $null = Read-QualificationSidecar -Path $side
        $denseWatch = [Diagnostics.Stopwatch]::StartNew()
        $null = Test-QualificationSidecarDense -Text ('{"v":[' + ('0,' * 4001) + '0]}') -Limit 4000 -Watch $denseWatch -DeadlineMilliseconds 200
        $null = ConvertTo-TestPhaseJsonText -Value ([ordered]@{ why = ''; testCount = 0; slowest = @() })
        $null = Update-QualificationChunkTiming -LogRoot $dir -TimingRoot $dir -Suite 'warm' -Chunk 'c' -TrxPath $trx -LastPhase 'complete' -RunId 'warm' -Attempt '0' -Terminal
    } catch {
    } finally {
        if (-not [string]::IsNullOrWhiteSpace($dir) -and (Test-Path -LiteralPath $dir)) {
            Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Update-QualificationChunkTiming {
    param(
        [string]$LogRoot,
        [string]$TimingRoot,
        [string]$Suite,
        [string]$Chunk,
        [string]$TrxPath,
        [string]$LastPhase,
        [string]$Why,
        $QueueWaitSeconds,
        $BuildSeconds,
        $DiscoverySeconds,
        $RunWallSeconds,
        [string]$StartedAt,
        [string]$CompletedAt,
        [string]$RunId = '',
        [string]$Attempt = '',
        [switch]$Terminal
    )
    try {
        if ([string]::IsNullOrWhiteSpace($LogRoot) -or [string]::IsNullOrWhiteSpace($Suite) -or [string]::IsNullOrWhiteSpace($Chunk)) { return }
        if (-not $script:QualificationChunkMemory) { $script:QualificationChunkMemory = @{} }
        $key = ([string]$LogRoot + '|' + [string]$Suite + '|' + [string]$Chunk)
        $memory = $null
        if ($script:QualificationChunkMemory.ContainsKey($key)) { $memory = $script:QualificationChunkMemory[$key] }
        $bound = $PSBoundParameters
        $path = Join-Path $LogRoot ('{0}-{1}.timing.json' -f $Suite, $Chunk)
        $timingRootText = ''
        if ($bound.ContainsKey('TimingRoot') -and -not [string]::IsNullOrWhiteSpace([string]$bound['TimingRoot'])) {
            $timingRootText = [string]$bound['TimingRoot']
        } elseif ($null -ne $memory -and $memory.ContainsKey('timingRoot') -and -not [string]::IsNullOrWhiteSpace([string]$memory['timingRoot'])) {
            $timingRootText = [string]$memory['timingRoot']
        } elseif ($bound.ContainsKey('TimingRoot')) {
            $timingRootText = [string]$bound['TimingRoot']
        }
        $trx = ''
        if ($bound.ContainsKey('TrxPath')) { $trx = [string]$bound['TrxPath'] }
        elseif ($null -ne $memory -and $memory.ContainsKey('trx')) { $trx = [string]$memory['trx'] }
        $whyText = ''
        if ($bound.ContainsKey('Why')) { $whyText = [string]$bound['Why'] }
        elseif ($null -ne $memory -and $memory.ContainsKey('why')) { $whyText = [string]$memory['why'] }
        $phaseText = ''
        if ($bound.ContainsKey('LastPhase')) { $phaseText = [string]$bound['LastPhase'] }
        elseif ($null -ne $memory -and $memory.ContainsKey('lastPhase')) { $phaseText = [string]$memory['lastPhase'] }
        $startedText = ''
        if ($bound.ContainsKey('StartedAt')) { $startedText = ConvertTo-QualificationStampText $bound['StartedAt'] }
        elseif ($null -ne $memory -and $memory.ContainsKey('startedAt')) { $startedText = [string]$memory['startedAt'] }
        $completedText = ''
        if ($bound.ContainsKey('CompletedAt')) { $completedText = ConvertTo-QualificationStampText $bound['CompletedAt'] }
        elseif ($null -ne $memory -and $memory.ContainsKey('completedAt')) { $completedText = [string]$memory['completedAt'] }
        $queue = $null
        if ($bound.ContainsKey('QueueWaitSeconds')) { $queue = $QueueWaitSeconds }
        elseif ($null -ne $memory -and $memory.ContainsKey('queueWaitSeconds')) { $queue = $memory['queueWaitSeconds'] }
        $build = $null
        if ($bound.ContainsKey('BuildSeconds')) { $build = $BuildSeconds }
        elseif ($null -ne $memory -and $memory.ContainsKey('buildSeconds')) { $build = $memory['buildSeconds'] }
        $discovery = $null
        if ($bound.ContainsKey('DiscoverySeconds')) { $discovery = $DiscoverySeconds }
        elseif ($null -ne $memory -and $memory.ContainsKey('discoverySeconds')) { $discovery = $memory['discoverySeconds'] }
        $runWall = $null
        if ($bound.ContainsKey('RunWallSeconds')) { $runWall = $RunWallSeconds }
        elseif ($null -ne $memory -and $memory.ContainsKey('runWallSeconds')) { $runWall = $memory['runWallSeconds'] }
        $trxMissing = $true
        $p50 = $null
        $p95 = $null
        $max = $null
        $slow = @()
        $count = 0
        $phaseSetup = $null
        $phaseTear = $null
        $phaseSpawn = $null
        $phaseWait = $null
        if ($Terminal) {
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $refused = $false
            $sideIdentity = Get-TestPhasePathIdentity -Path $path
            $priorSide = $null
            if ($sideIdentity.Kind -eq 'missing') {
                $priorSide = $null
            } elseif ($sideIdentity.Kind -ne 'regular' -or [int64]$sideIdentity.Size -gt 262144) {
                $refused = $true
            } else {
                $priorSide = Read-QualificationSidecar -Path $path -Watch $watch -DeadlineMilliseconds 200
                if ($null -eq $priorSide) { $refused = $true }
            }
            if ($null -ne $priorSide -and -not $refused) {
                if ([string]::IsNullOrWhiteSpace($startedText) -and $priorSide.PSObject.Properties.Name -contains 'startedAt') {
                    $startedText = ConvertTo-QualificationStampText $priorSide.startedAt
                }
                if (-not $bound.ContainsKey('QueueWaitSeconds') -and $null -eq $queue -and $priorSide.PSObject.Properties.Name -contains 'queueWaitSeconds') { $queue = $priorSide.queueWaitSeconds }
                if (-not $bound.ContainsKey('BuildSeconds') -and $null -eq $build -and $priorSide.PSObject.Properties.Name -contains 'buildSeconds') { $build = $priorSide.buildSeconds }
                if (-not $bound.ContainsKey('DiscoverySeconds') -and $null -eq $discovery -and $priorSide.PSObject.Properties.Name -contains 'discoverySeconds') { $discovery = $priorSide.discoverySeconds }
                if (-not $bound.ContainsKey('RunWallSeconds') -and $null -eq $runWall -and $priorSide.PSObject.Properties.Name -contains 'runWallSeconds') { $runWall = $priorSide.runWallSeconds }
                if (-not $bound.ContainsKey('TrxPath') -and [string]::IsNullOrWhiteSpace($trx) -and $priorSide.PSObject.Properties.Name -contains 'trx') { $trx = [string]$priorSide.trx }
                if (-not $bound.ContainsKey('Why') -and [string]::IsNullOrWhiteSpace($whyText) -and $priorSide.PSObject.Properties.Name -contains 'why') { $whyText = [string]$priorSide.why }
                if (-not $bound.ContainsKey('CompletedAt') -and [string]::IsNullOrWhiteSpace($completedText) -and $priorSide.PSObject.Properties.Name -contains 'completedAt') {
                    $completedText = ConvertTo-QualificationStampText $priorSide.completedAt
                }
            }
            $stats = $null
            if (-not [string]::IsNullOrWhiteSpace($trx)) {
                $trxIdentity = Get-TestPhasePathIdentity -Path $trx
                if ($trxIdentity.Kind -eq 'missing') {
                    $trxMissing = $true
                } elseif ($trxIdentity.Kind -ne 'regular') {
                    $refused = $true
                } elseif ($watch.ElapsedMilliseconds -ge 200) {
                    $trxMissing = $false
                    $refused = $true
                } else {
                    $trxMissing = $false
                    $stats = Get-QualificationTrxStats -TrxPath $trx -Watch $watch -DeadlineMilliseconds 200
                    if ($null -eq $stats -or -not [bool]$stats.Complete) { $refused = $true }
                }
            }
            if (-not [string]::IsNullOrWhiteSpace($timingRootText)) {
                if ($watch.ElapsedMilliseconds -ge 200) {
                    $refused = $true
                } else {
                    $sums = Get-QualificationPhaseSums -TimingRoot $timingRootText -Chunk $Chunk -RunId $RunId -Suite $Suite -Attempt $Attempt -Watch $watch -DeadlineMilliseconds 200
                    if ($null -ne $sums -and [bool]$sums.refused) { $refused = $true }
                    elseif ($null -ne $sums) {
                        $phaseSetup = $sums.fixtureSetupSeconds
                        $phaseTear = $sums.fixtureTeardownSeconds
                        $phaseSpawn = $sums.processSpawnSeconds
                        $phaseWait = $sums.processWaitSeconds
                    }
                }
            }
            if ($watch.ElapsedMilliseconds -ge 200) { $refused = $true }
            if ($refused) {
                $phaseSetup = $null
                $phaseTear = $null
                $phaseSpawn = $null
                $phaseWait = $null
                $stats = $null
                if ([string]::IsNullOrWhiteSpace($whyText)) { $whyText = 'timing-read-refused' }
            }
            if ($null -ne $stats) {
                $p50 = $stats.P50
                $p95 = $stats.P95
                $max = $stats.Max
                $slow = @($stats.Slowest)
                $count = [int]$stats.Count
            }
        }
        $record = [ordered]@{
            suite = [string]$Suite
            chunk = [string]$Chunk
            timingRoot = [string]$timingRootText
            trx = $trx
            trxMissing = [bool]$trxMissing
            why = $whyText
            lastPhase = $phaseText
            lastPhaseUtc = (Get-NightlyUtcNow).ToString('o')
            startedAt = $startedText
            completedAt = $completedText
            queueWaitSeconds = $queue
            buildSeconds = $build
            buildSlotWaitSeconds = $null
            buildSlot = 'not-used'
            discoverySeconds = $discovery
            runWallSeconds = $runWall
            distillationSeconds = $null
            fixtureSetupSeconds = $phaseSetup
            fixtureTeardownSeconds = $phaseTear
            processSpawnSeconds = $phaseSpawn
            processWaitSeconds = $phaseWait
            testCount = $count
            p50Seconds = $p50
            p95Seconds = $p95
            maxSeconds = $max
            slowest = @($slow)
        }
        $script:QualificationChunkMemory[$key] = @{
            timingRoot = [string]$timingRootText
            trx = [string]$trx
            why = [string]$whyText
            lastPhase = [string]$phaseText
            startedAt = [string]$startedText
            completedAt = [string]$completedText
            queueWaitSeconds = $queue
            buildSeconds = $build
            discoverySeconds = $discovery
            runWallSeconds = $runWall
        }
        $text = ConvertTo-TestPhaseJsonText -Value $record
        Write-TestPhaseTextAtomic -Path $path -Text $text
        if (-not [string]::IsNullOrWhiteSpace($timingRootText)) {
            $copy = Join-Path $timingRootText ('{0}-{1}.timing.json' -f $Suite, $Chunk)
            if (([IO.Path]::GetFullPath($copy)) -ne ([IO.Path]::GetFullPath($path))) {
                Write-TestPhaseTextAtomic -Path $copy -Text $text
            }
        }
    } catch {
        # A timing sidecar must not change the suite verdict.
    }
}

Initialize-TestPhaseReadWarmup

function Invoke-QualificationPostRunAggregation {
    param(
        [int]$ChunkExitCode,
        [string]$ChunkResult,
        [string]$LogRoot,
        [string]$TimingRoot,
        [string]$Suite,
        [string]$Chunk,
        [string]$TrxPath,
        [string]$LastPhase,
        [string]$Why,
        $QueueWaitSeconds,
        $BuildSeconds,
        $DiscoverySeconds,
        $RunWallSeconds,
        [string]$StartedAt,
        [string]$CompletedAt,
        [string]$RunId = '',
        [string]$Attempt = ''
    )
    $splat = @{
        LogRoot = $LogRoot
        TimingRoot = $TimingRoot
        Suite = $Suite
        Chunk = $Chunk
        TrxPath = $TrxPath
        LastPhase = $LastPhase
        RunId = $RunId
        Attempt = $Attempt
        Terminal = $true
    }
    if ($PSBoundParameters.ContainsKey('Why')) { $splat.Why = $Why }
    if ($PSBoundParameters.ContainsKey('QueueWaitSeconds')) { $splat.QueueWaitSeconds = $QueueWaitSeconds }
    if ($PSBoundParameters.ContainsKey('BuildSeconds')) { $splat.BuildSeconds = $BuildSeconds }
    if ($PSBoundParameters.ContainsKey('DiscoverySeconds')) { $splat.DiscoverySeconds = $DiscoverySeconds }
    if ($PSBoundParameters.ContainsKey('RunWallSeconds')) { $splat.RunWallSeconds = $RunWallSeconds }
    if ($PSBoundParameters.ContainsKey('StartedAt')) { $splat.StartedAt = $StartedAt }
    if ($PSBoundParameters.ContainsKey('CompletedAt')) { $splat.CompletedAt = $CompletedAt }
    try { Update-QualificationChunkTiming @splat } catch { }
    return [pscustomobject]@{
        ChunkExitCode = $ChunkExitCode
        ChunkResult = $ChunkResult
    }
}

function Get-QualificationTimingReport {
    param([string]$LogRoot, [string]$TimingRoot)
    $chunks = @()
    if (-not [string]::IsNullOrWhiteSpace($LogRoot) -and (Test-Path -LiteralPath $LogRoot)) {
        foreach ($file in @(Get-ChildItem -LiteralPath $LogRoot -Filter '*.timing.json' -File -ErrorAction SilentlyContinue)) {
            try {
                $row = Read-QualificationSidecar -Path $file.FullName
                if ($null -ne $row) { $chunks += $row }
            } catch { }
        }
    }
    $bySuite = @{}
    foreach ($row in $chunks) {
        $suite = [string]$row.suite
        if ([string]::IsNullOrWhiteSpace($suite)) { continue }
        if (-not $bySuite.ContainsKey($suite)) {
            $bySuite[$suite] = [ordered]@{ suite = $suite; startedAt = ''; completedAt = ''; lastPhase = ''; lastPhaseUtc = '' }
        }
        $bag = $bySuite[$suite]
        $start = [string]$row.startedAt
        if (-not [string]::IsNullOrWhiteSpace($start) -and ([string]::IsNullOrWhiteSpace([string]$bag.startedAt) -or $start -lt [string]$bag.startedAt)) {
            $bag.startedAt = $start
        }
        $end = [string]$row.completedAt
        if (-not [string]::IsNullOrWhiteSpace($end) -and $end -gt [string]$bag.completedAt) { $bag.completedAt = $end }
        $utc = [string]$row.lastPhaseUtc
        if (-not [string]::IsNullOrWhiteSpace($utc) -and $utc -gt [string]$bag.lastPhaseUtc) {
            $bag.lastPhaseUtc = $utc
            $bag.lastPhase = [string]$row.lastPhase
        }
    }
    $timeline = @()
    foreach ($key in @($bySuite.Keys | Sort-Object)) { $timeline += $bySuite[$key] }
    return [pscustomobject]@{
        TimingRoot = [string]$TimingRoot
        Chunks = @($chunks)
        Timeline = @($timeline)
    }
}

$script:TestPhaseEvidenceLoading = $false
$script:TestPhaseEvidenceLoaded = $true
