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

# A6 stated aggregate. The former per-shard 5000 ms ceiling is now the whole
# summary re-read budget, so 387 shards cannot delay the receipt by about 32 minutes.
# The 200 ms / 262144 inline bound stays on its own watch. The measured cost is recorded.
function Get-TestPhaseSummaryBudgetMilliseconds { return 5000 }

function Read-TestPhaseJsonlForSummary {
    param([string]$Path, $BudgetState = $null)
    $inline = [Diagnostics.Stopwatch]::StartNew()
    $read = Read-TestPhaseBoundedBytes -Path $Path -MaxBytes 262144 -DeadlineMilliseconds 200 -Watch $inline
    $parsed = ConvertTo-TestPhaseJsonlRecords -Read $read -MaxLines 4000 -DeadlineMilliseconds 200 -Watch $inline
    if (-not [bool]$parsed.Complete) {
        $watch = $null
        $deadline = Get-TestPhaseSummaryBudgetMilliseconds
        $maxBytes = 8388608
        if ($null -ne $BudgetState) {
            $watch = $BudgetState.Watch
            if ($null -ne $BudgetState.Milliseconds) { $deadline = [int]$BudgetState.Milliseconds }
            if ($null -ne $BudgetState.BytesRemaining) { $maxBytes = [int]$BudgetState.BytesRemaining }
        }
        if ($null -eq $watch) { $watch = New-Object System.Diagnostics.Stopwatch }
        if ($maxBytes -gt 0 -and $watch.ElapsedMilliseconds -lt $deadline) {
            if (-not $watch.IsRunning) { $watch.Start() }
            try {
                $full = Read-TestPhaseBoundedBytes -Path $Path -MaxBytes $maxBytes -DeadlineMilliseconds $deadline -Watch $watch
                $parsed = ConvertTo-TestPhaseJsonlRecords -Read $full -MaxLines 20000 -DeadlineMilliseconds $deadline -Watch $watch
                if ($null -ne $BudgetState -and $null -ne $full -and $null -ne $full.Bytes) {
                    $left = [int]$BudgetState.BytesRemaining - [int]$full.Bytes.Length
                    if ($left -lt 0) { $left = 0 }
                    $BudgetState.BytesRemaining = $left
                }
            } finally {
                if ($watch.IsRunning) { $watch.Stop() }
            }
            if ($null -ne $BudgetState -and $null -ne $BudgetState.Watch -and $BudgetState.Watch.PSObject.Methods.Name -contains 'OnFullRead') {
                $BudgetState.Watch.OnFullRead()
            }
        }
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
        $filters = New-Object System.Collections.Generic.List[string]
        foreach ($plannedRow in @($groupRows)) {
            if ($null -eq $plannedRow) { continue }
            $filterText = ''
            if ($plannedRow.PSObject.Properties.Name -contains 'filter' -and $null -ne $plannedRow.filter) { $filterText = [string]$plannedRow.filter }
            $filters.Add($filterText)
        }
        Update-TestPhasePlannedIndex -Directory $dir -Suite $Suite -FileName $suiteFile -RunId $RunId -Attempt $Attempt -AssemblyId $AssemblyId -SourceDigest $SourceDigest -PolicyDigest $PolicyDigest -CostFileDigest $CostFileDigest -AssemblyDigest $AssemblyDigest -Filters @($filters.ToArray())
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

function ConvertTo-TestPhaseB64 {
    param([string]$Text)
    return [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$Text))
}

function ConvertFrom-TestPhaseB64 {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return '' }
    return [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$Text))
}

function Get-TestPhaseSha256Text {
    param([string]$Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes([string]$Text)
        $hash = $sha.ComputeHash($bytes)
        $sb = New-Object System.Text.StringBuilder
        foreach ($b in $hash) { [void]$sb.Append($b.ToString('x2')) }
        return $sb.ToString()
    } finally {
        $sha.Dispose()
    }
}

function Get-TestPhaseSha256File {
    param([string]$Path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $hash = $sha.ComputeHash($stream)
        $sb = New-Object System.Text.StringBuilder
        foreach ($b in $hash) { [void]$sb.Append($b.ToString('x2')) }
        return $sb.ToString()
    } finally {
        $stream.Dispose()
        $sha.Dispose()
    }
}

function Get-TestPhaseWriterEnvironment {
    $edition = 'Desktop'
    try { if ($PSVersionTable.PSEdition) { $edition = [string]$PSVersionTable.PSEdition } } catch { }
    $framework = ''
    try { $framework = [string][System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription } catch { $framework = [string][Environment]::Version }
    $culture = ''
    $ui = ''
    try { $culture = [string][System.Threading.Thread]::CurrentThread.CurrentCulture.Name } catch { }
    try { $ui = [string][System.Threading.Thread]::CurrentThread.CurrentUICulture.Name } catch { }
    $tz = [System.TimeZoneInfo]::Local
    $offset = $tz.GetUtcOffset([datetime]::Now)
    $sign = '+'
    if ($offset.Ticks -lt 0) { $sign = '-' }
    $off = $sign + ([math]::Abs($offset.Hours)).ToString('00') + ':' + ([math]::Abs($offset.Minutes)).ToString('00')
    return ($edition + '|' + [string]$PSVersionTable.PSVersion + '|' + $framework + '|' + $culture + '|' + $ui + '|' + [string]$tz.Id + '|' + $off)
}

function Test-TestPhaseSuiteChunkEqual {
    param([string]$LeftSuite, [string]$LeftChunk, [string]$RightSuite, [string]$RightChunk)
    $cmp = [System.StringComparer]::InvariantCultureIgnoreCase
    if (-not $cmp.Equals([string]$LeftSuite, [string]$RightSuite)) { return $false }
    return $cmp.Equals([string]$LeftChunk, [string]$RightChunk)
}

function Test-TestPhaseOrdinalEqual {
    param([string]$Left, [string]$Right)
    return [System.StringComparer]::Ordinal.Equals([string]$Left, [string]$Right)
}

function Test-TestPhaseUnsafeDirectory {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $true }
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return $true }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return $true }
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { return $false }
    Initialize-TestPhaseLinuxIdentity
    if (-not ('Antiphon.TestPhase.FileIdentity' -as [type])) { return $true }
    $stat = New-Object Antiphon.TestPhase.LinuxStat
    $code = [Antiphon.TestPhase.FileIdentity]::LStat($Path, [ref]$stat)
    if ($code -ne 0) { return $true }
    $kind = ([uint32]$stat.Mode) -band 61440
    if ($kind -eq 16384) { return $false }
    return $true
}

function Wait-TestPhaseCommitBarrier {
    param([string]$Stage)
    $barrier = $script:TestPhaseCommitBarrier
    if ($null -eq $barrier) { return }
    $name = ''
    if ($barrier.PSObject.Properties.Name -contains 'Stage') { $name = [string]$barrier.Stage }
    if ($name -ne $Stage) { return }
    $signal = ''
    $release = ''
    if ($barrier.PSObject.Properties.Name -contains 'Signal') { $signal = [string]$barrier.Signal }
    if ($barrier.PSObject.Properties.Name -contains 'Release') { $release = [string]$barrier.Release }
    if (-not [string]::IsNullOrWhiteSpace($signal)) {
        $utf8 = New-Object System.Text.UTF8Encoding $false
        $parent = Split-Path -Parent $signal
        if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        [System.IO.File]::WriteAllText($signal, $Stage, $utf8)
    }
    if ([string]::IsNullOrWhiteSpace($release)) { return }
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not [System.IO.File]::Exists($release)) {
        if ($watch.ElapsedMilliseconds -gt 120000) { throw 'commit-barrier-timeout' }
        Start-Sleep -Milliseconds 20
    }
}

function Test-TestPhaseCommitFault {
    param([string]$Stage)
    $fault = $script:TestPhaseCommitFault
    if ($null -eq $fault) { return $false }
    $name = ''
    if ($fault.PSObject.Properties.Name -contains 'Stage') { $name = [string]$fault.Stage }
    if ($name -ne $Stage) { return $false }
    $remaining = 0
    if ($fault.PSObject.Properties.Name -contains 'Remaining') { $remaining = [int]$fault.Remaining }
    if ($remaining -le 0) { return $false }
    $fault.Remaining = $remaining - 1
    return $true
}

function New-TestPhaseRefusedContext {
    param([string]$Reason)
    return [pscustomobject]@{
        Refused = $true
        Reason = [string]$Reason
        Closed = $true
        Id = [guid]::NewGuid().ToString('n')
        Lock = $null
        TimingRoot = ''
        Generation = ''
        LogRoot = ''
        RunId = ''
        Attempt = ''
        Suite = ''
        Source = ''
        InvocationId = ''
        HasSeed = $false
        HeaderReady = $false
        HeaderRun = ''
        HeaderAttempt = ''
        HeaderSuite = ''
        HeaderAttemptSeen = $false
        Records = (New-Object System.Collections.Generic.List[object])
        SeedRows = (New-Object System.Collections.Generic.List[object])
        SeedPath = ''
        LastCommitted = 0
        NextSequence = 1
        Unresolved = $false
        CorruptionReason = ''
        Gaps = (New-Object System.Collections.Generic.List[string])
        ProjectOnly = $false
        WriterEnvironment = ''
        NeedsRecovery = $false
        RescanAttempted = $false
        PendingGaps = (New-Object System.Collections.Generic.List[object])
    }
}

function New-TestPhaseOpenContext {
    param([string]$TimingRoot, [string]$LogRoot, [string]$RunId, [string]$Attempt, [string]$Suite, [string]$Source, [string]$InvocationId, $Lock)
    $ctx = New-TestPhaseRefusedContext -Reason ''
    $ctx.Refused = $false
    $ctx.Closed = $false
    $ctx.Lock = $Lock
    $ctx.TimingRoot = $TimingRoot
    $ctx.LogRoot = $LogRoot
    $ctx.RunId = $RunId
    $ctx.Attempt = $Attempt
    $ctx.Suite = $Suite
    $ctx.Source = $Source
    $ctx.InvocationId = $InvocationId
    $ctx.WriterEnvironment = Get-TestPhaseWriterEnvironment
    return $ctx
}

function Resolve-TestPhaseExecutedTimingRoot {
    param([string]$LogRoot, [string]$TimingRoot)
    $selected = ''
    if (-not [string]::IsNullOrWhiteSpace($TimingRoot)) { $selected = $TimingRoot }
    elseif (-not [string]::IsNullOrWhiteSpace([string]$script:QualificationTimingRoot)) { $selected = [string]$script:QualificationTimingRoot }
    elseif (-not [string]::IsNullOrWhiteSpace($LogRoot)) { $selected = (Join-Path $LogRoot 'timing') }
    if ([string]::IsNullOrWhiteSpace($selected)) { return '' }
    return [System.IO.Path]::GetFullPath($selected)
}

function Get-TestPhaseJournalRoot {
    param([string]$TimingRoot)
    return (Join-Path $TimingRoot 'groups-executed.journal')
}

function Read-TestPhaseJsonFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try { return ([System.IO.File]::ReadAllText($Path) | ConvertFrom-Json) } catch { return $null }
}

function Test-TestPhaseRecordDigest {
    param([string]$Text)
    $needle = '"digest":"'
    $start = $Text.IndexOf($needle)
    if ($start -lt 0) { return $false }
    $hexStart = $start + $needle.Length
    $end = $Text.IndexOf('"', $hexStart)
    if ($end -lt 0) { return $false }
    $hex = $Text.Substring($hexStart, $end - $hexStart).ToLowerInvariant()
    $probe = $Text.Remove($start, ($end + 1) - $start).Insert($start, '"digest":""')
    $actual = Get-TestPhaseSha256Text -Text $probe
    return ($actual -eq $hex)
}

function Write-TestPhaseDirectory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Copy-TestPhaseBytes {
    param([string]$Source, [string]$Destination)
    Write-TestPhaseDirectory -Path (Split-Path -Parent $Destination)
    [System.IO.File]::Copy($Source, $Destination, $true)
}

function Save-TestPhaseMalformedLegacy {
    param([string]$TimingRoot, [string]$Generation)
    $flat = Join-Path $TimingRoot 'groups-executed.json'
    if (-not (Test-Path -LiteralPath $flat)) { return }
    $dest = Join-Path (Join-Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $TimingRoot) 'predecessors') $Generation) 'groups-executed.json'
    if (Test-Path -LiteralPath $dest) { return }
    Copy-TestPhaseBytes -Source $flat -Destination $dest
}

function Import-TestPhaseSeedRows {
    param($Parsed)
    $rows = New-Object System.Collections.Generic.List[object]
    $prior = @()
    if ($null -ne $Parsed -and $Parsed.PSObject.Properties.Name -contains 'chunks' -and $null -ne $Parsed.chunks) { $prior = @($Parsed.chunks) }
    elseif ($null -ne $Parsed -and $Parsed.PSObject.Properties.Name -contains 'chunk') { $prior = @($Parsed) }
    foreach ($item in $prior) {
        if ($null -eq $item) { continue }
        $settled = ConvertTo-TestPhaseExecutedChunk -Item $item
        $rows.Add([pscustomobject]@{
            SettledJson = (ConvertTo-TestPhaseJsonText -Value $settled)
            SettledSuite = [string]$settled.suite
            SettledChunk = [string]$settled.chunk
        })
    }
    $run = ''
    $attempt = ''
    $suite = ''
    $attemptSeen = $false
    if ($null -ne $Parsed -and $Parsed.PSObject.Properties.Name -contains 'runId') { $run = [string]$Parsed.runId }
    if ($null -ne $Parsed -and $Parsed.PSObject.Properties.Name -contains 'attempt') {
        $attempt = [string]$Parsed.attempt
        $attemptSeen = $true
    }
    if ($null -ne $Parsed -and $Parsed.PSObject.Properties.Name -contains 'suite') { $suite = [string]$Parsed.suite }
    return [pscustomobject]@{ Rows = $rows; Run = $run; Attempt = $attempt; Suite = $suite; AttemptSeen = $attemptSeen }
}

function Set-TestPhaseSeedHeader {
    param($Context, $Cache)
    $Context.HeaderReady = $true
    $Context.HeaderRun = [string]$Cache.Run
    $Context.HeaderAttempt = [string]$Cache.Attempt
    $Context.HeaderSuite = [string]$Cache.Suite
    $Context.HeaderAttemptSeen = [bool]$Cache.AttemptSeen
    $Context.SeedRows = $Cache.Rows
    $Context.HasSeed = $true
}

function Write-TestPhaseSeedCache {
    param($Context)
    if (-not $Context.HasSeed) { return }
    $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation
    Write-TestPhaseDirectory -Path $genDir
    $lines = New-Object System.Collections.Generic.List[string]
    $flag = '0'
    if ($Context.HeaderAttemptSeen) { $flag = '1' }
    $lines.Add('H' + "`t" + (ConvertTo-TestPhaseB64 ([string]$Context.HeaderRun)) + "`t" + (ConvertTo-TestPhaseB64 ([string]$Context.HeaderAttempt)) + "`t" + (ConvertTo-TestPhaseB64 ([string]$Context.HeaderSuite)) + "`t" + $flag)
    foreach ($row in $Context.SeedRows) {
        $lines.Add((ConvertTo-TestPhaseB64 ([string]$row.SettledSuite)) + "`t" + (ConvertTo-TestPhaseB64 ([string]$row.SettledChunk)) + "`t" + [string]$row.SettledJson)
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllLines((Join-Path $genDir 'seed-cache.txt'), $lines.ToArray(), $utf8)
}

function Read-TestPhaseSeedCache {
    param($Context, [string]$Generation)
    $path = Join-Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Generation) 'seed-cache.txt'
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    $lines = @([System.IO.File]::ReadAllLines($path))
    if ($lines.Count -lt 1) { return $false }
    $header = [string]$lines[0]
    if (-not $header.StartsWith('H' + "`t")) { return $false }
    $parts = $header.Split([char]9)
    if ($parts.Length -lt 5) { return $false }
    $rows = New-Object System.Collections.Generic.List[object]
    for ($i = 1; $i -lt $lines.Count; $i++) {
        $line = [string]$lines[$i]
        if ([string]::IsNullOrEmpty($line)) { continue }
        $first = $line.IndexOf([char]9)
        if ($first -lt 0) { return $false }
        $second = $line.IndexOf([char]9, $first + 1)
        if ($second -lt 0) { return $false }
        $rows.Add([pscustomobject]@{
            SettledJson = $line.Substring($second + 1)
            SettledSuite = (ConvertFrom-TestPhaseB64 $line.Substring(0, $first))
            SettledChunk = (ConvertFrom-TestPhaseB64 $line.Substring($first + 1, $second - $first - 1))
        })
    }
    $Context.HeaderReady = $true
    $Context.HeaderRun = (ConvertFrom-TestPhaseB64 ([string]$parts[1]))
    $Context.HeaderAttempt = (ConvertFrom-TestPhaseB64 ([string]$parts[2]))
    $Context.HeaderSuite = (ConvertFrom-TestPhaseB64 ([string]$parts[3]))
    $Context.HeaderAttemptSeen = ([string]$parts[4] -eq '1')
    $Context.SeedRows = $rows
    $Context.HasSeed = $true
    $Context.SeedPath = Join-Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Generation) 'seed.json'
    return $true
}

function New-TestPhaseEnvelopeText {
    param(
        [string]$Kind,
        [int]$Sequence,
        [string]$PublicationId,
        $Context,
        [string]$Reason,
        [string]$Suite,
        [string]$Chunk,
        [string]$SettledSuite,
        [string]$SettledChunk,
        [string]$HeaderRun,
        [string]$HeaderAttempt,
        [string]$HeaderSuite,
        [string]$FreshJson,
        [string]$SettledJson
    )
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('{')
    [void]$sb.Append('"version":1')
    [void]$sb.Append(',"kind":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text $Kind))
    [void]$sb.Append(',"generation":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text ([string]$Context.Generation)))
    [void]$sb.Append(',"sequence":')
    [void]$sb.Append([string]$Sequence)
    [void]$sb.Append(',"publicationId":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text $PublicationId))
    [void]$sb.Append(',"root":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text ([string]$Context.TimingRoot)))
    [void]$sb.Append(',"freshSuiteKey":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text (ConvertTo-TestPhaseB64 $Suite)))
    [void]$sb.Append(',"settledSuiteKey":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text (ConvertTo-TestPhaseB64 $SettledSuite)))
    [void]$sb.Append(',"freshChunkKey":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text (ConvertTo-TestPhaseB64 $Chunk)))
    [void]$sb.Append(',"settledChunkKey":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text (ConvertTo-TestPhaseB64 $SettledChunk)))
    [void]$sb.Append(',"freshRun":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text (ConvertTo-TestPhaseB64 $HeaderRun)))
    [void]$sb.Append(',"freshAttempt":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text (ConvertTo-TestPhaseB64 $HeaderAttempt)))
    [void]$sb.Append(',"freshHeaderSuite":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text (ConvertTo-TestPhaseB64 $HeaderSuite)))
    [void]$sb.Append(',"digest":""')
    [void]$sb.Append(',"rowJson":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text $FreshJson))
    [void]$sb.Append(',"settledRowJson":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text $SettledJson))
    [void]$sb.Append(',"writerEnvironment":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text ([string]$Context.WriterEnvironment)))
    [void]$sb.Append(',"reason":')
    [void]$sb.Append((ConvertTo-TestPhaseJsonString -Text $Reason))
    [void]$sb.Append('}')
    $text = $sb.ToString()
    $hash = Get-TestPhaseSha256Text -Text $text
    $needle = '"digest":""'
    $at = $text.IndexOf($needle)
    if ($at -lt 0) { throw 'digest-placeholder-missing' }
    return $text.Remove($at, $needle.Length).Insert($at, '"digest":"' + $hash + '"')
}

function ConvertTo-TestPhaseRecordObject {
    param($Envelope)
    $freshJson = ''
    $settledJson = ''
    if ($Envelope.PSObject.Properties.Name -contains 'rowJson' -and $null -ne $Envelope.rowJson) { $freshJson = [string]$Envelope.rowJson }
    if ($Envelope.PSObject.Properties.Name -contains 'settledRowJson' -and $null -ne $Envelope.settledRowJson) { $settledJson = [string]$Envelope.settledRowJson }
    return [pscustomobject]@{
        Kind = [string]$Envelope.kind
        Sequence = [int]$Envelope.sequence
        FreshJson = $freshJson
        SettledJson = $settledJson
        FreshSuite = (ConvertFrom-TestPhaseB64 ([string]$Envelope.freshSuiteKey))
        FreshChunk = (ConvertFrom-TestPhaseB64 ([string]$Envelope.freshChunkKey))
        SettledSuite = (ConvertFrom-TestPhaseB64 ([string]$Envelope.settledSuiteKey))
        SettledChunk = (ConvertFrom-TestPhaseB64 ([string]$Envelope.settledChunkKey))
        HeaderRun = (ConvertFrom-TestPhaseB64 ([string]$Envelope.freshRun))
        HeaderAttempt = (ConvertFrom-TestPhaseB64 ([string]$Envelope.freshAttempt))
        HeaderSuite = (ConvertFrom-TestPhaseB64 ([string]$Envelope.freshHeaderSuite))
        PublicationId = [string]$Envelope.publicationId
    }
}

function Read-TestPhaseGenerationPrefix {
    param([string]$GenerationDir)
    $loaded = New-Object System.Collections.Generic.List[object]
    $expected = 1
    $corrupt = $false
    $reason = ''
    while ($true) {
        $name = $expected.ToString('00000000') + '.json'
        $path = Join-Path $GenerationDir $name
        if (-not (Test-Path -LiteralPath $path)) { break }
        $text = ''
        try { $text = [System.IO.File]::ReadAllText($path) } catch { $corrupt = $true; $reason = 'unreadable-record'; break }
        if ([string]::IsNullOrWhiteSpace($text) -or -not (Test-TestPhaseRecordDigest -Text $text)) {
            $corrupt = $true
            $reason = 'bad-digest'
            break
        }
        $parsed = $null
        try { $parsed = $text | ConvertFrom-Json } catch { $corrupt = $true; $reason = 'malformed-record'; break }
        if ($null -eq $parsed -or [int]$parsed.sequence -ne $expected) { $corrupt = $true; $reason = 'sequence-mismatch'; break }
        $loaded.Add((ConvertTo-TestPhaseRecordObject -Envelope $parsed))
        $expected++
    }
    $last = 0
    if ($loaded.Count -gt 0) { $last = [int]$loaded[$loaded.Count - 1].Sequence }
    return [pscustomobject]@{
        Records = $loaded
        NextSequence = $expected
        Corrupt = [bool]$corrupt
        Reason = $reason
        LastSequence = $last
    }
}

function Write-TestPhaseGenerationMetadata {
    param($Context, [string]$ParentGeneration, [string]$SeedDigest)
    $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation
    Write-TestPhaseDirectory -Path $genDir
    $meta = [ordered]@{
        version = 1
        generation = [string]$Context.Generation
        runId = [string]$Context.RunId
        attempt = [string]$Context.Attempt
        suite = [string]$Context.Suite
        source = [string]$Context.Source
        invocationId = [string]$Context.InvocationId
        writerEnvironment = [string]$Context.WriterEnvironment
        seedDigest = [string]$SeedDigest
        parentGeneration = [string]$ParentGeneration
        unresolved = [bool]$Context.Unresolved
        corruptionReason = [string]$Context.CorruptionReason
    }
    $path = Join-Path $genDir 'metadata.json'
    Write-TestPhaseTextAtomic -Path $path -Text (ConvertTo-TestPhaseJsonText -Value $meta)
    $digest = Get-TestPhaseSha256File -Path $path
    $pointer = [ordered]@{
        version = 1
        timingRoot = [string]$Context.TimingRoot
        generation = [string]$Context.Generation
        metadataDigest = [string]$digest
    }
    $current = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) 'current.json'
    Write-TestPhaseTextAtomic -Path $current -Text (ConvertTo-TestPhaseJsonText -Value $pointer)
}

function Initialize-TestPhaseEmptyGeneration {
    param($Context, [string]$ParentGeneration)
    $Context.Generation = [guid]::NewGuid().ToString('n')
    $Context.HasSeed = $false
    $Context.SeedPath = ''
    $Context.SeedRows = New-Object System.Collections.Generic.List[object]
    $Context.Records = New-Object System.Collections.Generic.List[object]
    $Context.LastCommitted = 0
    $Context.NextSequence = 1
    $Context.HeaderReady = $false
    Write-TestPhaseGenerationMetadata -Context $Context -ParentGeneration $ParentGeneration -SeedDigest ''
}

function Initialize-TestPhaseSeedGeneration {
    param($Context, [string]$LegacyPath, [string]$ParentGeneration, [bool]$Malformed)
    $Context.Generation = [guid]::NewGuid().ToString('n')
    $Context.Records = New-Object System.Collections.Generic.List[object]
    $Context.LastCommitted = 0
    $Context.NextSequence = 1
    $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation
    Write-TestPhaseDirectory -Path $genDir
    if ($Malformed) {
        $Context.Unresolved = $true
        if ([string]::IsNullOrWhiteSpace($Context.CorruptionReason)) { $Context.CorruptionReason = 'malformed-legacy' }
        Save-TestPhaseMalformedLegacy -TimingRoot $Context.TimingRoot -Generation $Context.Generation
        $Context.HasSeed = $false
        $Context.HeaderReady = $false
        Write-TestPhaseGenerationMetadata -Context $Context -ParentGeneration $ParentGeneration -SeedDigest ''
        return
    }
    $seedPath = Join-Path $genDir 'seed.json'
    Copy-TestPhaseBytes -Source $LegacyPath -Destination $seedPath
    $Context.SeedPath = $seedPath
    $Context.HasSeed = $true
    $parsed = Read-TestPhaseJsonFile -Path $LegacyPath
    $cache = Import-TestPhaseSeedRows -Parsed $parsed
    Set-TestPhaseSeedHeader -Context $Context -Cache $cache
    Write-TestPhaseSeedCache -Context $Context
    $digest = Get-TestPhaseSha256File -Path $seedPath
    Write-TestPhaseGenerationMetadata -Context $Context -ParentGeneration $ParentGeneration -SeedDigest $digest
}

function Get-TestPhaseReduction {
    param($Context)
    $chunkRecords = New-Object System.Collections.Generic.List[object]
    foreach ($rec in $Context.Records) {
        if ($null -eq $rec) { continue }
        if ([string]$rec.Kind -eq 'chunk') { $chunkRecords.Add($rec) }
    }
    $list = New-Object System.Collections.ArrayList
    if ($Context.HasSeed) {
        foreach ($row in $Context.SeedRows) {
            [void]$list.Add([pscustomobject]@{
                Json = [string]$row.SettledJson
                Fresh = [string]$row.SettledJson
                Suite = [string]$row.SettledSuite
                Chunk = [string]$row.SettledChunk
            })
        }
    }
    $last = $null
    foreach ($rec in $chunkRecords) {
        for ($i = $list.Count - 1; $i -ge 0; $i--) {
            if (Test-TestPhaseSuiteChunkEqual -LeftSuite $list[$i].Suite -LeftChunk $list[$i].Chunk -RightSuite $rec.FreshSuite -RightChunk $rec.FreshChunk) {
                $list.RemoveAt($i)
            }
        }
        [void]$list.Add([pscustomobject]@{
            Json = [string]$rec.SettledJson
            Fresh = [string]$rec.FreshJson
            Suite = [string]$rec.SettledSuite
            Chunk = [string]$rec.SettledChunk
        })
        $last = $rec
    }
    $headerRun = ''
    $headerAttempt = ''
    $headerSuite = ''
    if ($null -ne $last) {
        $headerRun = [string]$last.HeaderRun
        $headerAttempt = [string]$last.HeaderAttempt
        $headerSuite = [string]$last.HeaderSuite
    } elseif ($Context.HeaderReady) {
        $headerRun = [string]$Context.HeaderRun
        $headerAttempt = [string]$Context.HeaderAttempt
        $headerSuite = [string]$Context.HeaderSuite
    }
    return [pscustomobject]@{
        Entries = $list
        ChunkCount = $chunkRecords.Count
        HeaderRun = $headerRun
        HeaderAttempt = $headerAttempt
        HeaderSuite = $headerSuite
    }
}

function Get-TestPhaseProjectedDocument {
    param($Context)
    $reduced = Get-TestPhaseReduction -Context $Context
    if ($reduced.ChunkCount -eq 0) {
        if ($Context.HasSeed -and -not [string]::IsNullOrWhiteSpace([string]$Context.SeedPath) -and (Test-Path -LiteralPath $Context.SeedPath)) {
            return [System.IO.File]::ReadAllText([string]$Context.SeedPath)
        }
        return $null
    }
    $parts = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $reduced.Entries.Count; $i++) {
        if ($i -eq ($reduced.Entries.Count - 1)) { $parts.Add([string]$reduced.Entries[$i].Fresh) }
        else { $parts.Add([string]$reduced.Entries[$i].Json) }
    }
    $run = ConvertTo-TestPhaseJsonString -Text ([string]$reduced.HeaderRun)
    $attempt = ConvertTo-TestPhaseJsonString -Text ([string]$reduced.HeaderAttempt)
    $suite = ConvertTo-TestPhaseJsonString -Text ([string]$reduced.HeaderSuite)
    return '{"version":1,"runId":' + $run + ',"attempt":' + $attempt + ',"suite":' + $suite + ',"manufacturedPassed":false,"chunks":[' + ($parts.ToArray() -join ',') + ']}'
}

function Import-TestPhaseGapMemory {
    param($Context, $Records)
    foreach ($rec in $Records) {
        if ($null -eq $rec) { continue }
        if ([string]$rec.Kind -eq 'gap') {
            if (-not $Context.Gaps.Contains('publication-gap')) { [void]$Context.Gaps.Add('publication-gap') }
        }
    }
}

function Import-TestPhaseExistingGeneration {
    param($Context, [string]$Generation, [bool]$ProjectOnly, [bool]$Recover)
    $journal = Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot
    $genDir = Join-Path $journal $Generation
    $meta = Read-TestPhaseJsonFile -Path (Join-Path $genDir 'metadata.json')
    if ($null -eq $meta) {
        $Context.Unresolved = $true
        $Context.CorruptionReason = 'metadata-unreadable'
        Initialize-TestPhaseEmptyGeneration -Context $Context -ParentGeneration $Generation
        return
    }
    $prefix = Read-TestPhaseGenerationPrefix -GenerationDir $genDir
    $same = (Test-TestPhaseOrdinalEqual ([string]$meta.runId) ([string]$Context.RunId)) -and (Test-TestPhaseOrdinalEqual ([string]$meta.attempt) ([string]$Context.Attempt)) -and (Test-TestPhaseOrdinalEqual ([string]$meta.source) ([string]$Context.Source)) -and (Test-TestPhaseOrdinalEqual ([string]$meta.writerEnvironment) ([string]$Context.WriterEnvironment))
    $sealed = Test-Path -LiteralPath (Join-Path $genDir 'seal.json')
    $resumeSealed = [bool]$Recover -and $sealed -and -not $prefix.Corrupt -and $same
    if ($ProjectOnly) {
        $Context.Generation = $Generation
        $Context.Records = $prefix.Records
        $Context.LastCommitted = $prefix.LastSequence
        $Context.NextSequence = $prefix.NextSequence
        $Context.Unresolved = [bool]$meta.unresolved -or $prefix.Corrupt
        $Context.CorruptionReason = [string]$meta.corruptionReason
        if ($prefix.Corrupt -and [string]::IsNullOrWhiteSpace($Context.CorruptionReason)) { $Context.CorruptionReason = $prefix.Reason }
        [void](Read-TestPhaseSeedCache -Context $Context -Generation $Generation)
        Import-TestPhaseGapMemory -Context $Context -Records $prefix.Records
        return
    }
    if (($prefix.Corrupt -or -not $same -or $sealed) -and -not $resumeSealed) {
        $snapshot = New-TestPhaseRefusedContext -Reason ''
        $snapshot.Refused = $false
        $snapshot.TimingRoot = $Context.TimingRoot
        $snapshot.Generation = $Generation
        $snapshot.Records = $prefix.Records
        $snapshot.WriterEnvironment = [string]$Context.WriterEnvironment
        [void](Read-TestPhaseSeedCache -Context $snapshot -Generation $Generation)
        $parentText = Get-TestPhaseProjectedDocument -Context $snapshot
        $reduced = Get-TestPhaseReduction -Context $snapshot
        $Context.Unresolved = [bool]$meta.unresolved -or $prefix.Corrupt
        if ($prefix.Corrupt) { $Context.CorruptionReason = $prefix.Reason }
        elseif ([bool]$meta.unresolved) { $Context.CorruptionReason = [string]$meta.corruptionReason }
        else { $Context.CorruptionReason = '' }
        if ([string]::IsNullOrWhiteSpace($parentText)) {
            Initialize-TestPhaseEmptyGeneration -Context $Context -ParentGeneration $Generation
            return
        }
        $Context.Generation = [guid]::NewGuid().ToString('n')
        $genNew = Join-Path $journal $Context.Generation
        Write-TestPhaseDirectory -Path $genNew
        $newSeed = Join-Path $genNew 'seed.json'
        $utf8 = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText($newSeed, $parentText, $utf8)
        $Context.SeedPath = $newSeed
        $Context.HasSeed = $true
        $Context.Records = New-Object System.Collections.Generic.List[object]
        $Context.LastCommitted = 0
        $Context.NextSequence = 1
        $rows = New-Object System.Collections.Generic.List[object]
        for ($i = 0; $i -lt $reduced.Entries.Count; $i++) {
            $emitted = [string]$reduced.Entries[$i].Json
            if ($i -eq ($reduced.Entries.Count - 1)) { $emitted = [string]$reduced.Entries[$i].Fresh }
            $rows.Add([pscustomobject]@{
                SettledJson = $emitted
                SettledSuite = [string]$reduced.Entries[$i].Suite
                SettledChunk = [string]$reduced.Entries[$i].Chunk
            })
        }
        $Context.SeedRows = $rows
        $Context.HeaderReady = $true
        $Context.HeaderRun = [string]$reduced.HeaderRun
        $Context.HeaderAttempt = [string]$reduced.HeaderAttempt
        $Context.HeaderSuite = [string]$reduced.HeaderSuite
        $Context.HeaderAttemptSeen = $true
        Write-TestPhaseSeedCache -Context $Context
        $header = [pscustomobject]@{ Run = [string]$Context.HeaderRun; Attempt = [string]$Context.HeaderAttempt; Suite = [string]$Context.HeaderSuite }
        Update-TestPhaseContextHeader -Context $Context -Header $header
        Write-TestPhaseGenerationMetadata -Context $Context -ParentGeneration $Generation -SeedDigest (Get-TestPhaseSha256File -Path $newSeed)
        return
    }
    $Context.Generation = $Generation
    $Context.Records = $prefix.Records
    $Context.LastCommitted = $prefix.LastSequence
    $Context.NextSequence = $prefix.NextSequence
    $Context.Unresolved = [bool]$meta.unresolved
    $Context.CorruptionReason = [string]$meta.corruptionReason
    [void](Read-TestPhaseSeedCache -Context $Context -Generation $Generation)
    Import-TestPhaseGapMemory -Context $Context -Records $prefix.Records
    if ($prefix.Records.Count -gt 0) {
        $last = $null
        foreach ($rec in $prefix.Records) {
            if ($rec.Kind -eq 'chunk') { $last = $rec }
        }
        if ($null -ne $last) {
            $mini = [ordered]@{ runId = [string]$last.HeaderRun; attempt = [string]$last.HeaderAttempt; suite = [string]$last.HeaderSuite }
            $settled = (ConvertTo-TestPhaseJsonText -Value $mini) | ConvertFrom-Json
            $Context.HeaderReady = $true
            $Context.HeaderRun = [string]$settled.runId
            $Context.HeaderAttempt = [string]$settled.attempt
            $Context.HeaderSuite = [string]$settled.suite
            $Context.HeaderAttemptSeen = $true
        }
    }
}

function Open-TestPhaseExecutedContext {
    param(
        [string]$LogRoot,
        [string]$TimingRoot,
        [string]$RunId,
        [string]$Attempt,
        [string]$Suite,
        [string]$Source,
        [string]$InvocationId,
        [switch]$ProjectOnly,
        [switch]$Recover
    )
    if ([string]::IsNullOrWhiteSpace($Source)) { $Source = 'unknown' }
    $timing = Resolve-TestPhaseExecutedTimingRoot -LogRoot $LogRoot -TimingRoot $TimingRoot
    if ([string]::IsNullOrWhiteSpace($timing)) { return (New-TestPhaseRefusedContext -Reason 'timing-root-missing') }
    $probe = $timing
    if (-not [string]::IsNullOrWhiteSpace($TimingRoot)) { $probe = $TimingRoot }
    elseif (-not [string]::IsNullOrWhiteSpace([string]$script:QualificationTimingRoot)) { $probe = [string]$script:QualificationTimingRoot }
    elseif (-not [string]::IsNullOrWhiteSpace($LogRoot)) { $probe = (Join-Path $LogRoot 'timing') }
    if (Test-TestPhaseUnsafeDirectory -Path $probe) { return (New-TestPhaseRefusedContext -Reason 'unsafe-root') }
    if (Test-Path -LiteralPath $timing -PathType Leaf) { return (New-TestPhaseRefusedContext -Reason 'timing-root-is-file') }
    Write-TestPhaseDirectory -Path $timing
    $journal = Get-TestPhaseJournalRoot -TimingRoot $timing
    Write-TestPhaseDirectory -Path $journal
    $lockPath = Join-Path $journal 'writer.lock'
    $lock = $null
    try {
        $lock = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    } catch {
        return (New-TestPhaseRefusedContext -Reason 'writer-lock-held')
    }
    $ctx = New-TestPhaseOpenContext -TimingRoot $timing -LogRoot $LogRoot -RunId $RunId -Attempt $Attempt -Suite $Suite -Source $Source -InvocationId $InvocationId -Lock $lock
    $ctx.ProjectOnly = [bool]$ProjectOnly
    $currentPath = Join-Path $journal 'current.json'
    $current = Read-TestPhaseJsonFile -Path $currentPath
    if ($null -eq $current) {
        $flat = Join-Path $timing 'groups-executed.json'
        if ((Get-TestPhasePathIdentity -Path $flat).Kind -eq 'regular') {
            $parsed = Read-TestPhaseJsonFile -Path $flat
            $valid = $false
            if ($null -ne $parsed -and $parsed.PSObject.Properties.Name -contains 'version' -and [int]$parsed.version -eq 1) {
                if (($parsed.PSObject.Properties.Name -contains 'chunks') -or ($parsed.PSObject.Properties.Name -contains 'chunk')) { $valid = $true }
            }
            if ($valid) { Initialize-TestPhaseSeedGeneration -Context $ctx -LegacyPath $flat -ParentGeneration '' -Malformed $false }
            else { Initialize-TestPhaseSeedGeneration -Context $ctx -LegacyPath $flat -ParentGeneration '' -Malformed $true }
        } else {
            Initialize-TestPhaseEmptyGeneration -Context $ctx -ParentGeneration ''
        }
    } else {
        $bound = (Test-TestPhaseOrdinalEqual ([string]$current.timingRoot) ([string]$timing)) -and (-not [string]::IsNullOrWhiteSpace([string]$current.generation))
        if (-not $bound) {
            $ctx.Unresolved = $true
            $ctx.CorruptionReason = 'pointer-binding'
            Initialize-TestPhaseEmptyGeneration -Context $ctx -ParentGeneration ([string]$current.generation)
        } else {
            Import-TestPhaseExistingGeneration -Context $ctx -Generation ([string]$current.generation) -ProjectOnly:([bool]$ProjectOnly) -Recover:([bool]$Recover)
        }
    }
    if (-not $ProjectOnly) { $script:TestPhaseExecutedContext = $ctx }
    return $ctx
}

function Close-TestPhaseExecutedContext {
    param($Context)
    if ($null -eq $Context) { return }
    if ($null -ne $Context.Lock) {
        try { $Context.Lock.Dispose() } catch { }
        $Context.Lock = $null
    }
    $Context.Closed = $true
    if ($null -ne $script:TestPhaseExecutedContext -and [string]$script:TestPhaseExecutedContext.Id -eq [string]$Context.Id) {
        $script:TestPhaseExecutedContext = $null
    }
}

function Get-TestPhaseNextHeader {
    param($Context, [string]$RunId, [string]$Attempt, [string]$Suite)
    $run = [string]$RunId
    $attempt = [string]$Attempt
    $suite = [string]$Suite
    if ($Context.HeaderReady) {
        if (-not [string]::IsNullOrWhiteSpace([string]$Context.HeaderRun)) { $run = [string]$Context.HeaderRun }
        if ($Context.HeaderAttemptSeen) { $attempt = [string]$Context.HeaderAttempt }
        if (-not [string]::IsNullOrWhiteSpace([string]$Context.HeaderSuite)) { $suite = [string]$Context.HeaderSuite }
    }
    return [pscustomobject]@{ Run = $run; Attempt = $attempt; Suite = $suite }
}

function Update-TestPhaseContextHeader {
    param($Context, $Header)
    $mini = [ordered]@{ runId = [string]$Header.Run; attempt = [string]$Header.Attempt; suite = [string]$Header.Suite }
    $settled = (ConvertTo-TestPhaseJsonText -Value $mini) | ConvertFrom-Json
    $Context.HeaderReady = $true
    $Context.HeaderRun = [string]$settled.runId
    $Context.HeaderAttempt = [string]$settled.attempt
    $Context.HeaderSuite = [string]$settled.suite
    $Context.HeaderAttemptSeen = $true
}

function Write-TestPhaseCommitFile {
    param([string]$FinalPath, [string]$Text)
    $dir = Split-Path -Parent $FinalPath
    Write-TestPhaseDirectory -Path $dir
    Wait-TestPhaseCommitBarrier -Stage 'BeforeTempComplete'
    if (Test-TestPhaseCommitFault -Stage 'CreateTemp') { throw 'commit-fault-temp' }
    $tmp = Join-Path $dir ('.' + [guid]::NewGuid().ToString('n') + '.tmp')
    $stream = $null
    try {
        $stream = [System.IO.File]::Open($tmp, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        $bytes = (New-Object System.Text.UTF8Encoding $false).GetBytes($Text)
        $stream.Write($bytes, 0, $bytes.Length)
        if (Test-TestPhaseCommitFault -Stage 'Flush') { throw 'commit-fault-flush' }
        $stream.Flush($true)
        $stream.Dispose()
        $stream = $null
        Wait-TestPhaseCommitBarrier -Stage 'AfterFlushBeforeRename'
        if (Test-TestPhaseCommitFault -Stage 'Rename') { throw 'commit-fault-rename' }
        if ([System.IO.File]::Exists($FinalPath)) { throw 'commit-exists' }
        [System.IO.File]::Move($tmp, $FinalPath)
        $tmp = ''
        Wait-TestPhaseCommitBarrier -Stage 'AfterRenameBeforeAck'
        if (Test-TestPhaseCommitFault -Stage 'Acknowledge') { throw 'commit-fault-ack' }
        $readBack = [System.IO.File]::ReadAllText($FinalPath)
        if ($readBack -ne $Text) { throw 'commit-unreadable' }
    } catch {
        if ($null -ne $stream) { try { $stream.Dispose() } catch { } }
        if (-not [string]::IsNullOrWhiteSpace($tmp) -and [System.IO.File]::Exists($tmp)) {
            try { [System.IO.File]::Delete($tmp) } catch { }
        }
        throw
    }
}

function Add-TestPhaseJournalEnvelope {
    param($Context, [string]$Text, [string]$PublicationId, [int]$Sequence)
    $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation
    $final = Join-Path $genDir ($Sequence.ToString('00000000') + '.json')
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $attempt = 0
    $lastError = ''
    while ($attempt -lt 2) {
        if ($attempt -gt 0 -and $watch.Elapsed.TotalSeconds -gt 2) { break }
        $attempt++
        try {
            if ([System.IO.File]::Exists($final)) {
                $existing = [System.IO.File]::ReadAllText($final)
                if ((Test-TestPhaseRecordDigest -Text $existing) -and $existing.Contains($PublicationId)) { return $true }
                throw 'commit-conflict'
            }
            Write-TestPhaseCommitFile -FinalPath $final -Text $Text
            return $true
        } catch {
            $lastError = [string]$_.Exception.Message
        }
    }
    $Context.Reason = $lastError
    return $false
}

function Invoke-TestPhasePrefixRescan {
    param($Context)
    if (-not $Context.NeedsRecovery) { return }
    if ($Context.RescanAttempted) { return }
    $Context.RescanAttempted = $true
    $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation
    $prefix = Read-TestPhaseGenerationPrefix -GenerationDir $genDir
    $Context.Records = $prefix.Records
    $Context.LastCommitted = $prefix.LastSequence
    $Context.NextSequence = $prefix.NextSequence
    Import-TestPhaseGapMemory -Context $Context -Records $prefix.Records
    if ($prefix.Corrupt) {
        $Context.Unresolved = $true
        if ([string]::IsNullOrWhiteSpace([string]$Context.CorruptionReason)) { $Context.CorruptionReason = [string]$prefix.Reason }
    }
}

function Add-TestPhaseGapRecord {
    param(
        $Context,
        [string]$Reason,
        [string]$Suite = '',
        [string]$Chunk = '',
        [string]$RunId = '',
        [string]$Attempt = '',
        [string]$HeaderRun = '',
        [string]$HeaderAttempt = '',
        [string]$HeaderSuite = '',
        [string]$PublicationId = ''
    )
    $Context.NeedsRecovery = $true
    if (-not $Context.Gaps.Contains('publication-gap')) { [void]$Context.Gaps.Add('publication-gap') }
    if ([string]::IsNullOrWhiteSpace($HeaderRun)) { $HeaderRun = [string]$RunId }
    if ([string]::IsNullOrWhiteSpace($HeaderAttempt)) { $HeaderAttempt = [string]$Attempt }
    if ([string]::IsNullOrWhiteSpace($HeaderSuite)) { $HeaderSuite = [string]$Suite }
    if ([string]::IsNullOrWhiteSpace($PublicationId)) { $PublicationId = [guid]::NewGuid().ToString('n') }
    $sequence = [int]$Context.NextSequence
    $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation
    $final = Join-Path $genDir ($sequence.ToString('00000000') + '.json')
    $envelope = New-TestPhaseEnvelopeText -Kind 'gap' -Sequence $sequence -PublicationId $PublicationId -Context $Context -Reason $Reason -Suite $Suite -Chunk $Chunk -SettledSuite $Suite -SettledChunk $Chunk -HeaderRun $HeaderRun -HeaderAttempt $HeaderAttempt -HeaderSuite $HeaderSuite -FreshJson '' -SettledJson ''
    $ok = Add-TestPhaseJournalEnvelope -Context $Context -Text $envelope -PublicationId $PublicationId -Sequence $sequence
    if (-not $ok) {
        $already = $false
        if ($null -eq $Context.PendingGaps) { $Context.PendingGaps = New-Object System.Collections.Generic.List[object] }
        foreach ($pending in $Context.PendingGaps) {
            if ([string]$pending.PublicationId -eq $PublicationId) { $already = $true; break }
        }
        if (-not $already) {
            [void]$Context.PendingGaps.Add([pscustomobject]@{
                Reason = [string]$Reason
                Suite = [string]$Suite
                Chunk = [string]$Chunk
                RunId = [string]$RunId
                Attempt = [string]$Attempt
                HeaderRun = [string]$HeaderRun
                HeaderAttempt = [string]$HeaderAttempt
                HeaderSuite = [string]$HeaderSuite
                PublicationId = [string]$PublicationId
            })
        }
        return $false
    }
    $Context.Records.Add([pscustomobject]@{
        Kind = 'gap'
        Sequence = $sequence
        FreshJson = ''
        SettledJson = ''
        FreshSuite = [string]$Suite
        FreshChunk = [string]$Chunk
        SettledSuite = [string]$Suite
        SettledChunk = [string]$Chunk
        HeaderRun = [string]$HeaderRun
        HeaderAttempt = [string]$HeaderAttempt
        HeaderSuite = [string]$HeaderSuite
        PublicationId = [string]$PublicationId
    })
    $Context.LastCommitted = $sequence
    $Context.NextSequence = $sequence + 1
    return $true
}

function Submit-TestPhasePendingGaps {
    param($Context)
    if ($null -eq $Context.PendingGaps) { return $true }
    while ($Context.PendingGaps.Count -gt 0) {
        $pending = $Context.PendingGaps[0]
        $ok = Add-TestPhaseGapRecord -Context $Context -Reason ([string]$pending.Reason) -Suite ([string]$pending.Suite) -Chunk ([string]$pending.Chunk) -RunId ([string]$pending.RunId) -Attempt ([string]$pending.Attempt) -HeaderRun ([string]$pending.HeaderRun) -HeaderAttempt ([string]$pending.HeaderAttempt) -HeaderSuite ([string]$pending.HeaderSuite) -PublicationId ([string]$pending.PublicationId)
        if (-not $ok) { return $false }
        if ($Context.PendingGaps.Count -gt 0 -and [string]$Context.PendingGaps[0].PublicationId -eq [string]$pending.PublicationId) {
            $Context.PendingGaps.RemoveAt(0)
        }
    }
    return $true
}

function Add-TestPhaseExecutedChunk {
    param($Context, $Fresh, [string]$Suite, [string]$Chunk, [string]$RunId, [string]$Attempt)
    if ($Context.Closed -or $Context.Refused) { throw 'context-closed' }
    Invoke-TestPhasePrefixRescan -Context $Context
    $current = Read-TestPhaseJsonFile -Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) 'current.json')
    if ($null -eq $current -or -not (Test-TestPhaseOrdinalEqual ([string]$current.generation) ([string]$Context.Generation))) { throw 'stale-context' }
    if ($null -ne $Context.PendingGaps -and $Context.PendingGaps.Count -gt 0) {
        $drained = Submit-TestPhasePendingGaps -Context $Context
        if (-not $drained) {
            Write-TestPhaseFailureMarker -RunDirectory $Context.LogRoot -Code 'phase-summary-failed'
            return $false
        }
    }
    $header = Get-TestPhaseNextHeader -Context $Context -RunId $RunId -Attempt $Attempt -Suite $Suite
    $freshJson = ConvertTo-TestPhaseJsonText -Value $Fresh
    $parsed = $freshJson | ConvertFrom-Json
    $settled = ConvertTo-TestPhaseExecutedChunk -Item $parsed
    $settledJson = ConvertTo-TestPhaseJsonText -Value $settled
    $publicationId = [guid]::NewGuid().ToString('n')
    $sequence = [int]$Context.NextSequence
    $envelope = New-TestPhaseEnvelopeText -Kind 'chunk' -Sequence $sequence -PublicationId $publicationId -Context $Context -Reason '' -Suite $Suite -Chunk $Chunk -SettledSuite ([string]$settled.suite) -SettledChunk ([string]$settled.chunk) -HeaderRun ([string]$header.Run) -HeaderAttempt ([string]$header.Attempt) -HeaderSuite ([string]$header.Suite) -FreshJson $freshJson -SettledJson $settledJson
    $ok = Add-TestPhaseJournalEnvelope -Context $Context -Text $envelope -PublicationId $publicationId -Sequence $sequence
    if (-not $ok) {
        Write-TestPhaseFailureMarker -RunDirectory $Context.LogRoot -Code 'phase-summary-failed'
        Add-TestPhaseGapRecord -Context $Context -Reason ([string]$Context.Reason) -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt -HeaderRun ([string]$header.Run) -HeaderAttempt ([string]$header.Attempt) -HeaderSuite ([string]$header.Suite) | Out-Null
        return $false
    }
    Wait-TestPhaseCommitBarrier -Stage 'AfterAckBeforeProjection'
    $Context.Records.Add([pscustomobject]@{
        Kind = 'chunk'
        Sequence = $sequence
        FreshJson = $freshJson
        SettledJson = $settledJson
        FreshSuite = [string]$Suite
        FreshChunk = [string]$Chunk
        SettledSuite = [string]$settled.suite
        SettledChunk = [string]$settled.chunk
        HeaderRun = [string]$header.Run
        HeaderAttempt = [string]$header.Attempt
        HeaderSuite = [string]$header.Suite
        PublicationId = $publicationId
    })
    $Context.LastCommitted = $sequence
    $Context.NextSequence = $sequence + 1
    $Context.NeedsRecovery = $false
    $Context.RescanAttempted = $false
    Update-TestPhaseContextHeader -Context $Context -Header $header
    return $true
}

function Get-TestPhasePublicationStateText {
    param($Context, $ProjectionText)
    $quality = New-Object System.Collections.Generic.List[string]
    if ($Context.Unresolved) {
        if (-not $quality.Contains('publication-corruption')) { $quality.Add('publication-corruption') }
    }
    $pending = 0
    if ($null -ne $Context.PendingGaps) { $pending = $Context.PendingGaps.Count }
    if ($Context.Gaps.Count -gt 0 -or $pending -gt 0) {
        if (-not $quality.Contains('publication-gap')) { $quality.Add('publication-gap') }
    }
    $complete = ($quality.Count -eq 0)
    $digest = ''
    if ($null -ne $ProjectionText) { $digest = Get-TestPhaseSha256Text -Text $ProjectionText }
    $state = [ordered]@{
        version = 1
        generation = [string]$Context.Generation
        sequence = [int]$Context.LastCommitted
        root = [string]$Context.TimingRoot
        publicationComplete = [bool]$complete
        unresolved = [bool]$Context.Unresolved
        reason = [string]$Context.CorruptionReason
        projectionDigest = [string]$digest
        quality = @($quality.ToArray())
    }
    return (ConvertTo-TestPhaseJsonText -Value $state)
}

function Write-TestPhasePublicationState {
    param($Context, [string]$ProjectionText)
    $text = Get-TestPhasePublicationStateText -Context $Context -ProjectionText $ProjectionText
    $path = Join-Path $Context.TimingRoot 'groups-executed.state.json'
    Write-TestPhaseTextAtomic -Path $path -Text $text
    return (Get-TestPhaseSha256File -Path $path)
}

function Complete-TestPhaseExecutedProjection {
    param($Context, [string]$LogRoot, [string]$TimingRoot, [string]$Terminal)
    $owned = $false
    if ($null -eq $Context) {
        $Context = Open-TestPhaseExecutedContext -LogRoot $LogRoot -TimingRoot $TimingRoot -RunId '' -Attempt '' -Suite '' -Source 'unknown' -ProjectOnly
        $owned = $true
    }
    if ($null -eq $Context -or $Context.Refused) { return $Context }
    try {
        if ($null -ne $Context.PendingGaps -and $Context.PendingGaps.Count -gt 0) {
            Submit-TestPhasePendingGaps -Context $Context | Out-Null
        }
        $text = Get-TestPhaseProjectedDocument -Context $Context
        $terminalSet = -not [string]::IsNullOrWhiteSpace($Terminal)
        if ($null -eq $text -and -not $Context.Unresolved -and $Context.Gaps.Count -eq 0 -and -not $terminalSet) { return $Context }
        if ($null -ne $text) {
            $flat = Join-Path $Context.TimingRoot 'groups-executed.json'
            Write-TestPhaseTextAtomic -Path $flat -Text $text
        }
        $stateDigest = Write-TestPhasePublicationState -Context $Context -ProjectionText $text
        if ($terminalSet) {
            $projectionDigest = ''
            if ($null -ne $text) { $projectionDigest = Get-TestPhaseSha256Text -Text $text }
            $seal = [ordered]@{
                version = 1
                generation = [string]$Context.Generation
                sequence = [int]$Context.LastCommitted
                terminal = [string]$Terminal
                projectionDigest = [string]$projectionDigest
                stateDigest = [string]$stateDigest
            }
            $sealPath = Join-Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation) 'seal.json'
            Write-TestPhaseTextAtomic -Path $sealPath -Text (ConvertTo-TestPhaseJsonText -Value $seal)
        }
        return $Context
    } finally {
        if ($owned) { Close-TestPhaseExecutedContext -Context $Context }
    }
}

function Read-TestPhaseExecutedPrefix {
    param([string]$LogRoot, [string]$TimingRoot)
    $timing = Resolve-TestPhaseExecutedTimingRoot -LogRoot $LogRoot -TimingRoot $TimingRoot
    $empty = [pscustomobject]@{
        Generation = ''
        LastSequence = 0
        RecordKind = 'none'
        Quality = @()
        Complete = $false
        PublicationComplete = $false
        Chunks = @()
        Refused = $false
        Reason = ''
        Unresolved = $false
        Terminal = ''
    }
    if ([string]::IsNullOrWhiteSpace($timing)) { $empty.Refused = $true; $empty.Reason = 'timing-root-missing'; return $empty }
    $current = Read-TestPhaseJsonFile -Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) 'current.json')
    if ($null -eq $current) { return $empty }
    $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) ([string]$current.generation)
    $prefix = Read-TestPhaseGenerationPrefix -GenerationDir $genDir
    $kind = 'none'
    if ($prefix.Records.Count -gt 0) { $kind = [string]$prefix.Records[$prefix.Records.Count - 1].Kind }
    $ids = New-Object System.Collections.Generic.List[string]
    foreach ($rec in $prefix.Records) {
        if ($rec.Kind -eq 'chunk') { $ids.Add([string]$rec.FreshChunk) }
    }
    $quality = New-Object System.Collections.Generic.List[string]
    if ($prefix.Corrupt) { $quality.Add('publication-corruption') }
    $terminal = ''
    $seal = Read-TestPhaseJsonFile -Path (Join-Path $genDir 'seal.json')
    if ($null -ne $seal -and $seal.PSObject.Properties.Name -contains 'terminal') { $terminal = [string]$seal.terminal }
    return [pscustomobject]@{
        Generation = [string]$current.generation
        LastSequence = [int]$prefix.LastSequence
        RecordKind = $kind
        Quality = @($quality.ToArray())
        Complete = (-not $prefix.Corrupt -and $prefix.Records.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($terminal))
        PublicationComplete = (-not $prefix.Corrupt)
        Chunks = @($ids.ToArray())
        Refused = $false
        Reason = [string]$prefix.Reason
        Unresolved = [bool]$prefix.Corrupt
        Terminal = $terminal
    }
}

function Invoke-TestPhaseExecutedRecovery {
    param([string]$LogRoot, [string]$TimingRoot, [string]$RunId, [string]$Attempt, [string]$Suite, [string]$Source)
    $timing = Resolve-TestPhaseExecutedTimingRoot -LogRoot $LogRoot -TimingRoot $TimingRoot
    $run = [string]$RunId
    $att = [string]$Attempt
    $src = [string]$Source
    if (-not [string]::IsNullOrWhiteSpace($timing)) {
        $current = Read-TestPhaseJsonFile -Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) 'current.json')
        if ($null -ne $current -and -not [string]::IsNullOrWhiteSpace([string]$current.generation)) {
            $metaPath = Join-Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) ([string]$current.generation)) 'metadata.json'
            $meta = Read-TestPhaseJsonFile -Path $metaPath
            if ($null -ne $meta) {
                if ([string]::IsNullOrWhiteSpace($run)) { $run = [string]$meta.runId }
                if ([string]::IsNullOrWhiteSpace($att)) { $att = [string]$meta.attempt }
                if ([string]::IsNullOrWhiteSpace($src)) { $src = [string]$meta.source }
            }
        }
    }
    if ([string]::IsNullOrWhiteSpace($src)) { $src = 'unknown' }
    $ctx = Open-TestPhaseExecutedContext -LogRoot $LogRoot -TimingRoot $TimingRoot -RunId $run -Attempt $att -Suite $Suite -Source $src -Recover
    if ($null -eq $ctx -or $ctx.Refused) { return $ctx }
    try {
        $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $ctx.TimingRoot) $ctx.Generation
        $seal = Read-TestPhaseJsonFile -Path (Join-Path $genDir 'seal.json')
        if ($null -ne $seal -and -not $ctx.Unresolved) {
            $preview = Get-TestPhaseProjectedDocument -Context $ctx
            $projectionDigest = ''
            if ($null -ne $preview) { $projectionDigest = Get-TestPhaseSha256Text -Text $preview }
            $stateText = Get-TestPhasePublicationStateText -Context $ctx -ProjectionText $preview
            $stateDigest = Get-TestPhaseSha256Text -Text $stateText
            $projectionMatch = Test-TestPhaseOrdinalEqual $projectionDigest ([string]$seal.projectionDigest)
            $stateMatch = Test-TestPhaseOrdinalEqual $stateDigest ([string]$seal.stateDigest)
            if (-not $projectionMatch -or -not $stateMatch) {
                $ctx.Reason = 'seal-digest-mismatch'
                return $ctx
            }
        }
        Complete-TestPhaseExecutedProjection -Context $ctx | Out-Null
        return $ctx
    } finally {
        Close-TestPhaseExecutedContext -Context $ctx
    }
}

function Get-TestPhasePublicationQuality {
    param([string]$RunDirectory, $KnownPrefix)
    $tokens = New-Object System.Collections.Generic.List[string]
    try {
        $timing = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
        $flat = Join-Path $timing 'groups-executed.json'
        $currentPath = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) 'current.json'
        $hasFlat = (Get-TestPhasePathIdentity -Path $flat).Kind -eq 'regular'
        $hasJournal = (Get-TestPhasePathIdentity -Path $currentPath).Kind -eq 'regular'
        if (-not $hasFlat -and -not $hasJournal) { return @() }
        $statePath = Join-Path $timing 'groups-executed.state.json'
        if ((Get-TestPhasePathIdentity -Path $statePath).Kind -ne 'regular') {
            $tokens.Add('publication-state-missing')
            return @($tokens.ToArray())
        }
        $state = Read-TestPhaseJsonFile -Path $statePath
        if ($null -eq $state) {
            $tokens.Add('publication-state-mismatch')
            return @($tokens.ToArray())
        }
        $mismatch = $false
        if ($hasJournal) {
            $current = Read-TestPhaseJsonFile -Path $currentPath
            if ($null -eq $current -or -not (Test-TestPhaseOrdinalEqual ([string]$current.generation) ([string]$state.generation))) { $mismatch = $true }
            else {
                $prefix = $KnownPrefix
                if ($null -eq $prefix) {
                    $prefix = Read-TestPhaseGenerationPrefix -GenerationDir (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) ([string]$current.generation))
                }
                if ([int]$state.sequence -ne [int]$prefix.LastSequence) { $mismatch = $true }
                if ([bool]$prefix.Corrupt -and -not $tokens.Contains('publication-corruption')) { $tokens.Add('publication-corruption') }
            }
        }
        $rootFull = ''
        try { $rootFull = [System.IO.Path]::GetFullPath($timing) } catch { $rootFull = $timing }
        if (-not (Test-TestPhaseOrdinalEqual ([string]$state.root) ([string]$rootFull))) { $mismatch = $true }
        if ($hasFlat) {
            $digest = Get-TestPhaseSha256File -Path $flat
            if (-not (Test-TestPhaseOrdinalEqual ([string]$state.projectionDigest) ([string]$digest))) { $mismatch = $true }
        }
        if ($mismatch) { $tokens.Add('publication-state-mismatch') }
        if ($state.PSObject.Properties.Name -contains 'quality' -and $null -ne $state.quality) {
            foreach ($token in @($state.quality)) {
                $text = [string]$token
                if ([string]::IsNullOrWhiteSpace($text)) { continue }
                if (-not $tokens.Contains($text)) { $tokens.Add($text) }
            }
        }
    } catch {
        if (-not $tokens.Contains('publication-state-mismatch')) { $tokens.Add('publication-state-mismatch') }
    }
    return @($tokens.ToArray())
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
    Write-TestPhaseOwnerObservation -Verdict $Verdict -LogRoot $LogRoot -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt
    $exitCode = $null
    $coverageComplete = $null
    $testsPassed = $null
    if ($null -ne $Verdict) {
        if ($Verdict.PSObject.Properties.Name -contains 'ExitCode') { $exitCode = $Verdict.ExitCode }
        if ($Verdict.PSObject.Properties.Name -contains 'coverageComplete') { $coverageComplete = [bool]$Verdict.coverageComplete }
        if ($Verdict.PSObject.Properties.Name -contains 'testsPassed') { $testsPassed = [bool]$Verdict.testsPassed }
    }
    $fresh = ConvertTo-TestPhaseExecutedChunk -Chunk $Chunk -RunId $RunId -Attempt $Attempt -Suite $Suite -HostWallSeconds $HostWallSeconds -TrxMissing $trxMissing -PassedCount $passed -Outcomes @($outcomes.ToArray()) -Trx (ConvertTo-TestPhaseRelativePath -LogRoot $LogRoot -Path $TrxPath) -TrxDigest $digest -ExitCode $exitCode -CoverageComplete $coverageComplete -TestsPassed $testsPassed
    $persistent = $false
    $bound = $null
    if ($null -ne $script:TestPhaseExecutedContext) {
        $existingCtx = $script:TestPhaseExecutedContext
        if (-not $existingCtx.Closed -and -not $existingCtx.Refused) {
            $bound = $existingCtx
            $persistent = $true
        }
    }
    $savedContext = $script:TestPhaseExecutedContext
    $savedTiming = $script:QualificationTimingRoot
    $opened = $null
    try {
        if (-not $persistent) {
            $opened = Open-TestPhaseExecutedContext -LogRoot $LogRoot -RunId $RunId -Attempt $Attempt -Suite $Suite -Source 'unknown'
            if ($null -eq $opened -or $opened.Refused) {
                Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
                return
            }
            $bound = $opened
        }
        $committed = $false
        try {
            $committed = Add-TestPhaseExecutedChunk -Context $bound -Fresh $fresh -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt
        } catch {
            Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
            $committed = $false
        }
        if (-not $persistent) {
            $project = $committed -or $bound.HasSeed -or $bound.Unresolved -or ($bound.Gaps.Count -gt 0) -or ($bound.Records.Count -gt 0)
            if ($project) {
                Complete-TestPhaseExecutedProjection -Context $bound | Out-Null
            }
        }
    } finally {
        if ($null -ne $opened) {
            Close-TestPhaseExecutedContext -Context $opened
            $script:TestPhaseExecutedContext = $savedContext
            $script:QualificationTimingRoot = $savedTiming
        }
    }
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
        $live = $script:TestPhaseExecutedContext
        if ($null -ne $live -and -not $live.Closed -and -not $live.Refused) {
            Complete-TestPhaseExecutedProjection -Context $live | Out-Null
        }
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
    $watch = New-Object System.Diagnostics.Stopwatch
    if ($null -ne $script:TestPhaseSummaryReReadWatch) { $watch = $script:TestPhaseSummaryReReadWatch }
    $budget = [pscustomobject]@{
        Watch = $watch
        Milliseconds = [int](Get-TestPhaseSummaryBudgetMilliseconds)
        BytesRemaining = 8388608
    }
    $plannedDocs = @(Read-TestPhasePlannedDocuments -RunDirectory $RunDirectory)
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
    $shards = @($shards | Sort-Object { [string]$_.Name })
    $completes = @{}
    $starts = @{}
    $fixtures = @()
    $seenComplete = @{}
    foreach ($shard in $shards) {
        $parsed = Read-TestPhaseJsonlForSummary -Path $shard.FullName -BudgetState $budget
        $torn += [int]$parsed.Torn
        $malformed += [int]$parsed.Malformed
        $header = $null
        foreach ($rec in @($parsed.Records)) {
            if ([string]$rec.recordKind -eq 'header') { $header = $rec; break }
        }
        $planForShard = $Planned
        $headerSuite = ''
        if ($null -ne $header -and $header.PSObject.Properties.Name -contains 'suite') { $headerSuite = [string]$header.suite }
        if (-not [string]::IsNullOrWhiteSpace($headerSuite)) {
            foreach ($plannedDoc in @($plannedDocs)) {
                if ($null -eq $plannedDoc) { continue }
                $docSuite = ''
                if ($plannedDoc.PSObject.Properties.Name -contains 'suite') { $docSuite = [string]$plannedDoc.suite }
                if ($docSuite -eq $headerSuite) { $planForShard = $plannedDoc; break }
            }
        }
        $stale = $false
        if ($null -ne $header -and $null -ne $planForShard) {
            if (-not [string]::IsNullOrWhiteSpace([string]$planForShard.runId) -and [string]$header.runId -ne [string]$planForShard.runId) { $stale = $true }
            if (-not [string]::IsNullOrWhiteSpace([string]$planForShard.assembly) -and [string]$header.assembly -ne [string]$planForShard.assembly) { $stale = $true }
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
    $journalPrefix = $null
    $currentPath = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) 'current.json'
    $hasJournal = (Get-TestPhasePathIdentity -Path $currentPath).Kind -eq 'regular'
    if ($hasJournal) {
        $current = Read-TestPhaseJsonFile -Path $currentPath
        if ($null -ne $current -and -not [string]::IsNullOrWhiteSpace([string]$current.generation)) {
            $journalPrefix = Read-TestPhaseGenerationPrefix -GenerationDir (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) ([string]$current.generation))
            # Use the validated seed and retained records, including predecessor rows.
            # This read-only context never acquires writer ownership or projects files.
            $snapshot = New-TestPhaseRefusedContext -Reason ''
            $snapshot.Refused = $false
            $snapshot.TimingRoot = $timing
            $snapshot.Generation = [string]$current.generation
            $snapshot.Records = $journalPrefix.Records
            [void](Read-TestPhaseSeedCache -Context $snapshot -Generation ([string]$current.generation))
            $reduced = Get-TestPhaseReduction -Context $snapshot
            for ($entryIndex = 0; $entryIndex -lt $reduced.Entries.Count; $entryIndex++) {
                $entry = $reduced.Entries[$entryIndex]
                $rowJson = [string]$entry.Json
                if ($entryIndex -eq ($reduced.Entries.Count - 1)) { $rowJson = [string]$entry.Fresh }
                if ([string]::IsNullOrWhiteSpace($rowJson)) { continue }
                $chunkObj = $null
                try { $chunkObj = ($rowJson | ConvertFrom-Json) } catch { $chunkObj = $null }
                if ($null -eq $chunkObj) { continue }
                $trxRel = ''
                if ($chunkObj.PSObject.Properties.Name -contains 'trx') { $trxRel = [string]$chunkObj.trx }
                if ([string]::IsNullOrWhiteSpace($trxRel)) { continue }
                $fullTrx = [System.IO.Path]::GetFullPath((Join-Path $RunDirectory $trxRel))
                $trxGroups[$fullTrx] = [string]$chunkObj.chunk
                $suiteText = [string]$chunkObj.suite
                if ([string]::IsNullOrWhiteSpace($suiteText) -and $chunkObj.PSObject.Properties.Name -contains 'suite') { $suiteText = [string]$chunkObj.suite }
                $trxSuites[$fullTrx] = $suiteText
            }
        }
    }
    $executedPath = Join-Path $timing 'groups-executed.json'
    if ($null -eq $journalPrefix -and (Get-TestPhasePathIdentity -Path $executedPath).Kind -eq 'regular') {
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
    foreach ($token in @(Get-TestPhasePublicationQuality -RunDirectory $RunDirectory -KnownPrefix $journalPrefix)) {
        if (-not $quality.Contains([string]$token)) { $quality.Add([string]$token) }
    }
    $complete = $quality.Count -eq 0
    foreach ($test in $tests) {
        if ([bool]$test.incomplete) { $complete = $false }
    }
    $spent = 0
    if ($null -ne $budget.Watch) { $spent = [int]$budget.Watch.ElapsedMilliseconds }
    return [pscustomobject]@{
        Quality = @($quality.ToArray())
        Torn = $torn
        Malformed = $malformed
        Tests = @($tests)
        Shared = @($shared)
        Complete = [bool]$complete
        SummaryAggregationBudgetMilliseconds = [int]$budget.Milliseconds
        SummaryAggregationMilliseconds = [int]$spent
    }
}

function Write-TestPhaseSummaryFiles {
    param($Planned, $Report, [string]$RunDirectory, [int]$Top)
    $timing = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    if (-not (Test-Path -LiteralPath $timing)) {
        New-Item -ItemType Directory -Path $timing -Force | Out-Null
    }
    $orderedTests = @($Report.Tests | Sort-Object { [string]$_.testId })
    $aggregationBudget = Get-TestPhaseSummaryBudgetMilliseconds
    $aggregationSpent = 0
    if ($null -ne $Report -and $Report.PSObject.Properties.Name -contains 'SummaryAggregationBudgetMilliseconds' -and $null -ne $Report.SummaryAggregationBudgetMilliseconds) {
        $aggregationBudget = [int]$Report.SummaryAggregationBudgetMilliseconds
    }
    if ($null -ne $Report -and $Report.PSObject.Properties.Name -contains 'SummaryAggregationMilliseconds' -and $null -ne $Report.SummaryAggregationMilliseconds) {
        $aggregationSpent = [int]$Report.SummaryAggregationMilliseconds
    }
    $summary = [ordered]@{
        version = 1
        complete = [bool]$Report.Complete
        quality = @($Report.Quality)
        tornLines = [int]$Report.Torn
        malformedLines = [int]$Report.Malformed
        summaryAggregationBudgetMilliseconds = [int]$aggregationBudget
        summaryAggregationMilliseconds = [int]$aggregationSpent
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

function ConvertTo-QualificationUtcTicks {
    param($Value)
    if ($null -eq $Value) { return $null }
    try {
        if ($Value -is [datetimeoffset]) { return [int64]([datetimeoffset]$Value).UtcTicks }
        if ($Value -is [datetime]) {
            $stamp = [datetime]$Value
            if ($stamp.Kind -eq [DateTimeKind]::Local) { return [int64]$stamp.ToUniversalTime().Ticks }
            if ($stamp.Kind -eq [DateTimeKind]::Unspecified) {
                return [int64]([datetime]::SpecifyKind($stamp, [DateTimeKind]::Utc)).Ticks
            }
            return [int64]$stamp.Ticks
        }
        $parsed = [datetimeoffset]::Parse([string]$Value, [cultureinfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
        return [int64]$parsed.UtcTicks
    } catch {
        return $null
    }
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
    param($Verdict, [string]$LogRoot, [string]$Suite, [string]$Chunk, [string]$RunId = '', [string]$Attempt = '')
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
        $seenPid = @{}
        for ($seenIndex = 0; $seenIndex -lt $rows.Count; $seenIndex++) {
            $seenPid[[string]$rows[$seenIndex].pid] = $true
        }
        foreach ($key in @($missed.Keys)) {
            if ($seenPid.ContainsKey([string]$key)) { continue }
            $missedPid = 0
            $pidOk = $true
            try { $missedPid = [int]$key } catch { $pidOk = $false }
            if (-not $pidOk) { continue }
            $rows.Add([ordered]@{ pid = $missedPid; image = 'unknown'; startedAtUtc = ''; exitedAtUtc = '' })
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
        $startedAt = ''
        $startedValue = Get-TestPhaseNoteValue -Object $Verdict -Names @('rootStartedAtUtc', 'RootStartedAtUtc')
        if ($null -ne $startedValue) { $startedAt = [string]$startedValue }
        $launch = 0.0
        $launchValue = Get-TestPhaseNoteValue -Object $Verdict -Names @('launchMilliseconds', 'LaunchMilliseconds')
        if ($null -ne $launchValue) { $launch = [double]$launchValue }
        $cleanup = 0.0
        $cleanupValue = Get-TestPhaseNoteValue -Object $Verdict -Names @('cleanupMilliseconds', 'CleanupMilliseconds')
        if ($null -ne $cleanupValue) { $cleanup = [double]$cleanupValue }
        $children = $false
        if (Test-TestPhaseNotePresent -Object $Verdict -Names @('childrenExited', 'ChildrenExited')) {
            $childrenValue = Get-TestPhaseNoteValue -Object $Verdict -Names @('childrenExited', 'ChildrenExited')
            if ($null -ne $childrenValue) { $children = [bool]$childrenValue }
        }
        $drained = $false
        if (Test-TestPhaseNotePresent -Object $Verdict -Names @('outputDrained', 'OutputDrained')) {
            $drainedValue = Get-TestPhaseNoteValue -Object $Verdict -Names @('outputDrained', 'OutputDrained')
            if ($null -ne $drainedValue) { $drained = [bool]$drainedValue }
        }
        $remaining = 0
        $remainingValue = Get-TestPhaseNoteValue -Object $Verdict -Names @('remainingProcessCount', 'RemainingProcessCount')
        if ($null -ne $remainingValue) { $remaining = [int]$remainingValue }
        $missedIds = New-Object System.Collections.Generic.List[int]
        foreach ($key in @($missed.Keys)) {
            $listed = 0
            $listedOk = $true
            try { $listed = [int]$key } catch { $listedOk = $false }
            if ($listedOk) { $missedIds.Add($listed) }
        }
        $runKey = 'run'
        if ($RunId -match '^[A-Za-z0-9][A-Za-z0-9._-]{0,80}$') { $runKey = $RunId }
        $attemptKey = '0'
        if ($Attempt -match '^[A-Za-z0-9][A-Za-z0-9._-]{0,40}$') { $attemptKey = $Attempt }
        $doc = [ordered]@{
            version = 1
            suite = [string]$Suite
            chunk = [string]$Chunk
            runId = [string]$runKey
            attempt = [string]$attemptKey
            rootStartedAtUtc = $startedAt
            rootExitedAtUtc = $exitedAt
            jobEmpty = [bool]$job
            launchMilliseconds = $launch
            cleanupMilliseconds = $cleanup
            drainMilliseconds = $drain
            executionMilliseconds = $execution
            childrenExited = [bool]$children
            outputDrained = [bool]$drained
            remainingProcessCount = [int]$remaining
            missedDescendantPids = @($missedIds.ToArray())
            descendants = @($rows.ToArray())
        }
        $dir = Get-TestPhaseTimingDirectory -RunDirectory $LogRoot
        $path = Join-Path $dir ('owner-' + $Suite + '-' + $Chunk + '-' + $runKey + '-' + $attemptKey + '.json')
        if ((Get-TestPhasePathIdentity -Path $path).Kind -eq 'regular') { return }
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
        [string]$AssemblyId,
        [string]$SourceDigest = '',
        [string]$PolicyDigest = '',
        [string]$CostFileDigest = '',
        [string]$AssemblyDigest = '',
        $Filters = $null
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
                $keptFilters = New-Object System.Collections.Generic.List[string]
                if ($row.PSObject.Properties.Name -contains 'filters' -and $null -ne $row.filters) {
                    foreach ($item in @($row.filters)) { $keptFilters.Add([string]$item) }
                }
                $keptSource = ''
                $keptPolicy = ''
                $keptCost = ''
                $keptAssemblyDigest = ''
                if ($row.PSObject.Properties.Name -contains 'sourceDigest' -and $null -ne $row.sourceDigest) { $keptSource = [string]$row.sourceDigest }
                if ($row.PSObject.Properties.Name -contains 'policyDigest' -and $null -ne $row.policyDigest) { $keptPolicy = [string]$row.policyDigest }
                if ($row.PSObject.Properties.Name -contains 'costFileDigest' -and $null -ne $row.costFileDigest) { $keptCost = [string]$row.costFileDigest }
                if ($row.PSObject.Properties.Name -contains 'assemblyDigest' -and $null -ne $row.assemblyDigest) { $keptAssemblyDigest = [string]$row.assemblyDigest }
                $kept.Add([ordered]@{
                    suite = [string]$row.suite
                    file = [string]$row.file
                    runId = [string]$row.runId
                    attempt = [string]$row.attempt
                    assembly = [string]$row.assembly
                    sourceDigest = $keptSource
                    policyDigest = $keptPolicy
                    costFileDigest = $keptCost
                    assemblyDigest = $keptAssemblyDigest
                    filters = @($keptFilters.ToArray())
                })
            }
        }
    }
    $freshFilters = New-Object System.Collections.Generic.List[string]
    if ($null -ne $Filters) {
        foreach ($item in @($Filters)) { $freshFilters.Add([string]$item) }
    }
    $kept.Add([ordered]@{
        suite = [string]$Suite
        file = [string]$FileName
        runId = [string]$RunId
        attempt = [string]$Attempt
        assembly = [string]$AssemblyId
        sourceDigest = [string]$SourceDigest
        policyDigest = [string]$PolicyDigest
        costFileDigest = [string]$CostFileDigest
        assemblyDigest = [string]$AssemblyDigest
        filters = @($freshFilters.ToArray())
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
                    startedAt = (ConvertTo-QualificationStampText -Value $row.startedAt)
                    endedAt = (ConvertTo-QualificationStampText -Value $row.endedAt)
                    seconds = $phaseSeconds
                })
            }
        }
    }
    $phaseSeconds = 0.0
    if ($null -ne $Seconds) { $phaseSeconds = [double]$Seconds }
    $kept.Add([ordered]@{
        name = [string]$Name
        startedAt = (ConvertTo-QualificationStampText -Value $StartedAt)
        endedAt = (ConvertTo-QualificationStampText -Value $EndedAt)
        seconds = $phaseSeconds
    })
    $doc = [ordered]@{ version = 1; phases = @($kept.ToArray()) }
    Write-TestPhaseTextAtomic -Path $path -Text (ConvertTo-TestPhaseJsonText -Value $doc)
}

function Get-QualificationSetupAccounting {
    param([string]$LogRoot)
    $setup = 0.0
    $sum = 0.0
    $overlapped = $false
    $countedOnce = $false
    $firstStartUtcTicks = $null
    $recordedFirstStart = $false
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
                $phaseName = [string]$row.name
                if ($phaseName -eq 'build' -or $phaseName -eq 'npm-build' -or $phaseName -eq 'dotnet-build') {
                    if ($null -ne $row.seconds) { $sum += [double]$row.seconds }
                }
                $startTicks = ConvertTo-QualificationUtcTicks -Value $row.startedAt
                $endTicks = ConvertTo-QualificationUtcTicks -Value $row.endedAt
                if (-not $recordedFirstStart) {
                    $recordedFirstStart = $true
                    $firstStartUtcTicks = $startTicks
                }
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
    return [pscustomobject]@{
        countedOnce = [bool]$countedOnce
        setupSeconds = [double]$setup
        perChunkBuildSum = [double]$sum
        overlapped = [bool]$overlapped
        firstStartUtcTicks = $firstStartUtcTicks
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
