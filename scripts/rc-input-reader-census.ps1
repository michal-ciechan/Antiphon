#requires -Version 7.0
# CARD-1820 S3: mechanical runtime-reader census. Broadening is off, so an unresolved
# mask cannot hide an under-selected pair. Exit 1 names every pair. -EmitRows prints
# census groups for those pairs. The literal scan is a lower bound. A second pass
# binds directory walks (EnumerateFiles, GetFiles, GetDirectories, SourceFiles) to a
# tracked directory of at least two segments. scripts/lib/rc-input-walks.json records
# filtered listings the line scan cannot bind. A whole-repo walk is named there.
# Unowned paths select nothing; this script prints them and does not exit 1 for them.
param([switch]$EmitRows, [switch]$ScanOnly)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'lib/rc-input-manifest.ps1')
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sha=(Invoke-TestSelectionGit $root @('rev-parse','HEAD')).Stdout.Trim()
$dirty=(Invoke-TestSelectionGit $root @('diff','--name-only','HEAD','--','tests','scripts/lib/rc-input-owners.json','scripts/lib/rc-input-noop.json','scripts/lib/rc-input-walks.json','scripts/rc-input-reader-census.ps1')).Stdout
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
$pairSeen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$refs=New-Object System.Collections.Generic.List[object]
foreach ($line in $pairLines) {
    $parts=$line.Split("`t",2)
    $key=$parts[0]+"`n"+$parts[1]
    if ($pairSeen.Add($key)) { $refs.Add([pscustomobject]@{suite=$parts[0];path=$parts[1]}) }
}
$prefixes=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($path in $tracked) {
    $slash=0
    while (($next=$path.IndexOf('/',$slash)) -ge 0) {
        [void]$prefixes.Add($path.Substring(0,$next))
        $slash=$next+1
    }
}
function Test-RcTrackedPrefix([string]$Prefix) { return $prefixes.Contains($Prefix) }
function Get-RcQuotedLiterals([string]$Line) {
    $found=New-Object System.Collections.Generic.List[string]
    foreach ($match in [regex]::Matches($Line,'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"')) {
        $raw=$match.Value
        if ($raw.StartsWith('@"',[StringComparison]::Ordinal)) { $text=$raw.Substring(2,$raw.Length-3).Replace('""','"') }
        else { $text=$raw.Substring(1,$raw.Length-2).Replace('\\','/').Replace('\"','"').Replace('\','/') }
        if ($text) { $found.Add($text) }
    }
    return $found
}
$walkWord=[regex]'EnumerateFiles|GetFiles|GetDirectories|SourceFiles|GetManifestResourceNames|GetManifestResourceStream'
$walkedSuites=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$repoWalkSuites=New-Object System.Collections.Generic.List[string]
$walkPath='scripts/lib/rc-input-walks.json'
if ($trackedSet.Contains($walkPath)) {
    $walkDoc=ConvertFrom-Json -InputObject (Get-TestSelectionBlob $root $sha $walkPath) -AsHashtable
    foreach ($id in @($walkDoc.repoWalkSuites)) { if ($id) { $repoWalkSuites.Add([string]$id); [void]$walkedSuites.Add([string]$id) } }
    foreach ($walk in @($walkDoc.walks)) {
        [void]$walkedSuites.Add([string]$walk.suite)
        foreach ($glob in @($walk.globs)) {
            $pattern=New-RcInputPattern ([string]$glob)
            foreach ($path in $tracked) {
                if (-not $pattern.IsMatch($path)) { continue }
                $key=[string]$walk.suite+"`n"+$path
                if ($pairSeen.Add($key)) { $refs.Add([pscustomobject]@{suite=[string]$walk.suite;path=$path}) }
            }
        }
    }
}
foreach ($id in $repoWalkSuites) {
    foreach ($path in $tracked) {
        $key=$id+"`n"+$path
        if ($pairSeen.Add($key)) { $refs.Add([pscustomobject]@{suite=$id;path=$path}) }
    }
}
$unanalysable=New-Object System.Collections.Generic.List[string]
foreach ($row in $membership) {
    $parts=$row.Split("`t",2)
    $suite=$parts[0]
    $source=$parts[1]
    $full=Join-Path $root ($source.Replace('/',[IO.Path]::DirectorySeparatorChar))
    $lines=@(Get-Content -LiteralPath $full)
    $sawListing=$false
    $sawRepoRoot=$false
    for ($i=0; $i -lt $lines.Count; $i++) {
        $line=$lines[$i]
        if ($line.Contains('ls-files')) { $sawListing=$true }
        if ($line.Contains('WorkingDirectory = repoRoot') -or $line.Contains('WorkingDirectory = repositoryRoot')) { $sawRepoRoot=$true }
        if (-not $walkWord.IsMatch($line)) { continue }
        $segments=New-Object System.Collections.Generic.List[string]
        $slashLiterals=New-Object System.Collections.Generic.List[string]
        foreach ($text in (Get-RcQuotedLiterals $line)) {
            if ($text.Length -lt 1 -or $text.Length -gt 240) { continue }
            if ($text.IndexOf(' ') -ge 0 -or $text.IndexOf('*') -ge 0 -or $text.IndexOf('?') -ge 0 -or $text.IndexOf('$') -ge 0) { continue }
            if ($text[0] -eq '/' -or $text[0] -eq '.') { continue }
            if ($text.Contains('/')) { $slashLiterals.Add($text.Trim('/')); continue }
            if ($text -cmatch '^[A-Za-z0-9_.@+-]+$') { $segments.Add($text) }
        }
        $csOnly=$line.Contains('*.cs')
        $candidates=New-Object System.Collections.Generic.List[string]
        foreach ($literal in $slashLiterals) {
            if (($literal.Split('/').Count) -ge 2 -and $literal.Length -ge 8 -and (Test-RcTrackedPrefix $literal)) { $candidates.Add($literal) }
        }
        $run=New-Object System.Collections.Generic.List[string]
        foreach ($segment in $segments) {
            $trial=if ($run.Count -eq 0) { $segment } else { (($run -join '/')+'/'+$segment) }
            if (Test-RcTrackedPrefix $trial) { $run.Add($segment) }
            else {
                if ($run.Count -ge 2) { $joined=($run -join '/'); if ($joined.Length -ge 8) { $candidates.Add($joined) } }
                $run.Clear()
                if (Test-RcTrackedPrefix $segment) { $run.Add($segment) }
            }
        }
        if ($run.Count -ge 2) { $joined=($run -join '/'); if ($joined.Length -ge 8) { $candidates.Add($joined) } }
        foreach ($candidate in $candidates) {
            $needle=$candidate+'/'
            foreach ($path in $tracked) {
                if (-not $path.StartsWith($needle,[StringComparison]::Ordinal)) { continue }
                if ($csOnly -and -not $path.EndsWith('.cs',[StringComparison]::Ordinal)) { continue }
                $key=$suite+"`n"+$path
                if ($pairSeen.Add($key)) { $refs.Add([pscustomobject]@{suite=$suite;path=$path}) }
            }
        }
    }
    if ($sawListing -and $sawRepoRoot -and -not $walkedSuites.Contains($suite)) {
        $unanalysable.Add($suite+' '+$source)
    }
}
if ($ScanOnly) {
    Write-Output ("paths-checked="+$tracked.Count)
    Write-Output ("suites-checked="+$suites.Count)
    Write-Output ("pairs-checked="+$refs.Count)
    Write-Output ("unanalysable="+$unanalysable.Count)
    if ($repoWalkSuites.Count) { Write-Output ("repo-walk="+($repoWalkSuites -join ',')) } else { Write-Output 'repo-walk=none' }
    foreach ($row in $unanalysable) { Write-Output ("UNANALYSABLE: "+$row) }
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
$noopHits=New-Object System.Collections.Generic.List[object]
foreach ($row in $under) {
    if (Test-RcInputNoopPath ([string]$row.path)) { $noopHits.Add($row) }
}
$unowned=@($inv.unowned)
Write-Output ("paths-checked="+$tracked.Count)
Write-Output ("suites-checked="+$suites.Count)
Write-Output ("pairs-checked="+$refs.Count)
Write-Output ("under-selected="+$under.Count)
Write-Output ("unanalysable="+$unanalysable.Count)
Write-Output ("noop-conflict="+$noopHits.Count)
Write-Output ("unowned-count="+$unowned.Count)
Write-Output ("unresolved-count="+@($inv.unresolved).Count)
foreach ($reason in @($inv.unresolved)) { Write-Output ("unresolved: "+$reason) }
Write-Output ("creditBlockedBy="+[string]$inv.creditBlockedBy)
Write-Output ("needsOrchestratorDecision="+[string]$inv.needsOrchestratorDecision)
if ($repoWalkSuites.Count) { Write-Output ("repo-walk="+($repoWalkSuites -join ',')) } else { Write-Output 'repo-walk=none' }
foreach ($row in $unanalysable) { Write-Output ("UNANALYSABLE: "+$row) }
foreach ($row in $noopHits) { Write-Output ("noop-conflict: "+$row.suite+" "+$row.path) }
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
$unownedDir=Join-Path $root '.antiphon'
if (-not [IO.Directory]::Exists($unownedDir)) { [void][IO.Directory]::CreateDirectory($unownedDir) }
$unownedFile=Join-Path $unownedDir 'rc-unowned-paths.txt'
$unownedLines=New-Object System.Collections.Generic.List[string]
foreach ($row in $unowned) { $unownedLines.Add(([string]$row.hint)+"`t"+[string]$row.path) }
[IO.File]::WriteAllLines($unownedFile, $unownedLines)
Write-Output ("UNOWNED-COUNT "+$unowned.Count)
if ($unowned.Count -le 40) {
    foreach ($row in $unowned) { Write-Output ("UNOWNED-PATH "+[string]$row.hint+" "+[string]$row.path) }
} else {
    Write-Output ("UNOWNED-LIST "+$unownedFile)
}
if ($under.Count -or $unanalysable.Count -or $noopHits.Count) { exit 1 }
exit 0
