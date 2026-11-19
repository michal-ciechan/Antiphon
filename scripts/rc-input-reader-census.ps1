#requires -Version 7.0
# CARD-1820 S3: mechanical runtime-reader census. Broadening is off, so an unresolved
# mask cannot hide an under-selected pair. Exit 1 names every pair. -EmitRows prints
# census groups for those pairs. The literal scan is a lower bound; path classes it
# cannot close select every suite in rc-input-owners.json.
param([switch]$EmitRows, [switch]$ScanOnly)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'lib/rc-input-manifest.ps1')
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sha=(Invoke-TestSelectionGit $root @('rev-parse','HEAD')).Stdout.Trim()
$dirty=(Invoke-TestSelectionGit $root @('diff','--name-only','HEAD','--','tests','scripts/lib/rc-input-owners.json')).Stdout
if ($dirty.Trim()) { throw "census-dirty-tree:$($dirty.Trim())" }
$tracked=New-Object System.Collections.Generic.List[string]
foreach ($line in (Invoke-TestSelectionGit $root @('ls-files','-z')).Stdout.Split([char]0)) {
    if ($line) { $tracked.Add($line.Replace('\','/')) }
}
$trackedSet=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($path in $tracked) { [void]$trackedSet.Add($path) }
function Get-CensusSources([string]$Project) {
    $files=New-Object System.Collections.Generic.List[string]
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $directory=$Project -replace '[^/]+$',''
    foreach ($path in $tracked) {
        if ($path.StartsWith($directory,[StringComparison]::Ordinal) -and $path.EndsWith('.cs',[StringComparison]::Ordinal)) {
            if ($seen.Add($path)) { $files.Add($path) }
        }
    }
    $text=Get-TestSelectionBlob $root $sha $Project
    foreach ($match in [regex]::Matches($text,'<Compile\s+Include="([^"]+)"')) {
        $value=$match.Groups[1].Value.Replace('\','/')
        if ($value -match '[$*?]') { continue }
        $target=Join-Path ($directory) $value
        $parts=New-Object System.Collections.Generic.List[string]
        foreach ($part in ($target -replace '\\','/').Split('/')) {
            if ($part -eq '..') { if ($parts.Count) { $parts.RemoveAt($parts.Count-1) } }
            elseif ($part -and $part -ne '.') { $parts.Add($part) }
        }
        $resolved=$parts -join '/'
        if ($resolved.EndsWith('.cs',[StringComparison]::Ordinal) -and $trackedSet.Contains($resolved) -and $seen.Add($resolved)) {
            $files.Add($resolved)
        }
    }
    return ,$files
}
if (-not ('RcReaderCensusScan' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
public static class RcReaderCensusScan {
    static readonly Regex Strings = new Regex("@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"", RegexOptions.Compiled);
    static readonly Regex Gap = new Regex("^[\\s,+()]*$", RegexOptions.Compiled);
    static bool Excluded(string source, string literal, string path) {
        if (source.EndsWith("ServerGitAssemblyTests.cs", StringComparison.Ordinal) && literal == "Antiphon.Server.csproj" && path == "server/Antiphon.Server.csproj") return true;
        if (source.EndsWith("PhaseJournalDiagnostic.cs", StringComparison.Ordinal) && literal == "Antiphon.Tests.csproj" && path.EndsWith("Antiphon.Tests.csproj", StringComparison.Ordinal)) return true;
        if (literal == "combined.pdf") return true;
        if (source.EndsWith("TestClassificationMetadata.cs", StringComparison.Ordinal) && path.EndsWith("slow-tests-allowlist.txt", StringComparison.Ordinal)) return true;
        return false;
    }
    static List<string> Literals(string line) {
        var found = new List<string>();
        var run = new List<string>();
        var last = 0;
        var started = false;
        foreach (Match match in Strings.Matches(line)) {
            string text;
            var raw = match.Value;
            if (raw.StartsWith("@\"", StringComparison.Ordinal)) text = raw.Substring(2, raw.Length - 3).Replace("\"\"", "\"");
            else text = raw.Substring(1, raw.Length - 2).Replace("\\\\", "/").Replace("\\\"", "\"").Replace('\\', '/');
            if (started && !Gap.IsMatch(line.Substring(last, match.Index - last))) Flush(run, found);
            started = true;
            last = match.Index + match.Length;
            if (text.Length == 0 || text == "/") continue;
            run.Add(text);
        }
        Flush(run, found);
        return found;
    }
    static void Flush(List<string> run, List<string> found) {
        if (run.Count == 1) found.Add(run[0]);
        else if (run.Count >= 2) {
            found.Add(string.Join("/", run.ToArray()));
            foreach (var part in run) if (part.IndexOf('/') >= 0) found.Add(part);
        }
        run.Clear();
    }
    public static string[] Pairs(string root, string[] tracked, string[] membership) {
        var trackedSet = new HashSet<string>(tracked, StringComparer.Ordinal);
        var byBase = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var bySuffix = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in tracked) {
            var slash = path.LastIndexOf('/');
            var baseName = slash < 0 ? path : path.Substring(slash + 1);
            List<string> list;
            if (!byBase.TryGetValue(baseName, out list)) { list = new List<string>(); byBase[baseName] = list; }
            list.Add(path);
            var start = 0;
            while (true) {
                var next = path.IndexOf('/', start);
                if (next < 0) break;
                start = next + 1;
                if (start >= path.Length || path.IndexOf('/', start) < 0) break;
                var suffix = path.Substring(start);
                if (!bySuffix.TryGetValue(suffix, out list)) { list = new List<string>(); bySuffix[suffix] = list; }
                list.Add(path);
            }
        }
        var cache = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<string>();
        var bySuite = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var row in membership) {
            var tab = row.IndexOf('\t');
            if (tab <= 0) continue;
            var suite = row.Substring(0, tab);
            var source = row.Substring(tab + 1);
            List<string> members;
            if (!bySuite.TryGetValue(suite, out members)) { members = new List<string>(); bySuite[suite] = members; }
            members.Add(source);
        }
        foreach (var entry in bySuite) {
            foreach (var source in entry.Value) {
                List<string> parsed;
                if (!cache.TryGetValue(source, out parsed)) {
                    parsed = new List<string>();
                    var full = Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar));
                    foreach (var line in File.ReadLines(full)) {
                        if (line.IndexOf('"') < 0) continue;
                        foreach (var literal in Literals(line)) {
                            if (literal.Length < 5 || literal.Length > 240) continue;
                            if (literal.IndexOf(' ') >= 0 || literal.IndexOf('*') >= 0 || literal.IndexOf('?') >= 0 || literal.IndexOf('$') >= 0) continue;
                            if (literal[0] == '/' || literal[0] == '.') continue;
                            parsed.Add(literal);
                        }
                    }
                    cache[source] = parsed;
                }
                foreach (var literal in parsed) {
                    List<string> hits = null;
                    if (trackedSet.Contains(literal)) hits = new List<string> { literal };
                    else if (literal.IndexOf('/') >= 0) {
                        List<string> suff;
                        if (bySuffix.TryGetValue(literal, out suff)) hits = suff;
                    } else if (literal.IndexOf('.') >= 0) {
                        List<string> bases;
                        if (byBase.TryGetValue(literal, out bases) && bases.Count <= 3) hits = bases;
                    }
                    if (hits == null) continue;
                    foreach (var hit in hits) {
                        if (Excluded(source, literal, hit)) continue;
                        var key = entry.Key + "\n" + hit;
                        if (!seen.Add(key)) continue;
                        rows.Add(entry.Key + "\t" + hit);
                    }
                }
            }
        }
        return rows.ToArray();
    }
}
'@ -Language CSharp
}
$suites=@($tracked | Where-Object { $_.StartsWith('tests/',[StringComparison]::Ordinal) -and $_.EndsWith('.csproj',[StringComparison]::Ordinal) })
$membership=New-Object System.Collections.Generic.List[string]
foreach ($suite in $suites) {
    foreach ($source in (Get-CensusSources $suite)) { $membership.Add($suite+"`t"+$source) }
}
$pairLines=[RcReaderCensusScan]::Pairs($root, [string[]]$tracked.ToArray(), $membership.ToArray())
$refs=@($pairLines | ForEach-Object {
    $parts=$_.Split("`t",2)
    [pscustomobject]@{suite=$parts[0];path=$parts[1]}
})
if ($ScanOnly) {
    Write-Output ("paths-checked="+$tracked.Count)
    Write-Output ("suites-checked="+$suites.Count)
    Write-Output ("pairs-checked="+$refs.Count)
    foreach ($ref in ($refs | Sort-Object path,suite)) { Write-Output ("referenced: "+$ref.suite+" "+$ref.path) }
    exit 0
}
$inv=Read-RcInputInventory $root $sha $null -SkipUnresolvedBroadening
$owned=[Collections.Generic.Dictionary[string,System.Collections.Generic.HashSet[string]]]::new([StringComparer]::Ordinal)
foreach ($suite in @($inv.suites)) {
    $set=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($input in @($suite.inputs)) {
        if ($input.present) { [void]$set.Add([string]$input.path) }
    }
    $owned[$suite.id]=$set
}
$under=New-Object System.Collections.Generic.List[object]
foreach ($ref in $refs) {
    $set=$null
    if (-not $owned.TryGetValue($ref.suite,[ref]$set) -or -not $set.Contains($ref.path)) { $under.Add($ref) }
}
$under=@($under | Sort-Object path,suite)
Write-Output ("paths-checked="+$tracked.Count)
Write-Output ("suites-checked="+$suites.Count)
Write-Output ("pairs-checked="+$refs.Count)
Write-Output ("under-selected="+$under.Count)
if ($EmitRows) {
    $i=0
    foreach ($row in $under) {
        $i++
        $name=('scan-{0:d2}' -f $i)
        Write-Output ('{"group":"'+$name+'","paths":["'+$row.path+'"],"suites":["'+$row.suite+'"]}')
    }
} else {
    foreach ($row in $under) { Write-Output ("under-selected: "+$row.suite+" "+$row.path) }
}
if ($under.Count) { exit 1 }
exit 0
