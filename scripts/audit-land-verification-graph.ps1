# CARD-1385: hash the dotnet-build definition files at HEAD and compare them
# with server/Application/Services/LandVerificationBuildGraph.manifest.txt.
# CARD-1390: the trailing NUL check is ordinal. -Check and -Update refuse an
# EmbeddedResource, Content, None, or Compile include that can select
# docs/**/*.md or the root README.md. Every ls-tree row is validated, not
# only definition rows. ASCII-only: must parse under Windows PowerShell 5.1.
#
# Usage:
#   pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 [-Check]
#   pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Update
#
# -Check is the default. It prints digest=<HEX> count=<N> and exits 0 only
# when both manifest lines match. -Update rewrites the manifest from HEAD.
# Exit 1 is a mismatch, a docs-input refusal, or bad usage. Exit 2 is a git failure.
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$Update
)

$ErrorActionPreference = 'Stop'

function Exit-Audit([int]$Code, [string]$Message) {
    if (-not [string]::IsNullOrEmpty($Message)) {
        [Console]::Error.WriteLine($Message)
    }
    exit $Code
}

if ($Check.IsPresent -and $Update.IsPresent) {
    Exit-Audit 1 'Specify -Check or -Update, not both.'
}
if (-not $Update.IsPresent) {
    $Check = $true
}

if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    Exit-Audit 1 'script root is unknown'
}
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ManifestPath = [System.IO.Path]::Combine($RepoRoot, 'server', 'Application', 'Services', 'LandVerificationBuildGraph.manifest.txt')

function Test-DefinitionPath([string]$Path) {
    $name = [System.IO.Path]::GetFileName($Path)
    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($extension.EndsWith('proj', [System.StringComparison]::Ordinal)) { return $true }
    switch ($extension) {
        '.props' { return $true }
        '.targets' { return $true }
        '.sln' { return $true }
        '.slnx' { return $true }
        '.slnf' { return $true }
        '.rsp' { return $true }
    }
    if ($name.StartsWith('Directory.Build.', [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    switch ($name.ToLowerInvariant()) {
        'global.json' { return $true }
        'nuget.config' { return $true }
        'packages.lock.json' { return $true }
        'packages.config' { return $true }
        'directory.packages.props' { return $true }
    }
    return $false
}

function Test-ProjectXml([string]$Path) {
    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($extension.EndsWith('proj', [System.StringComparison]::Ordinal)) { return $true }
    if ($extension -eq '.props' -or $extension -eq '.targets') { return $true }
    return $false
}

function Test-FullObjectId([string]$Value) {
    if ($Value.Length -ne 40 -and $Value.Length -ne 64) { return $false }
    foreach ($c in $Value.ToCharArray()) {
        $ok = ($c -ge '0' -and $c -le '9') -or ($c -ge 'a' -and $c -le 'f')
        if (-not $ok) { return $false }
    }
    $zeros = '0' * $Value.Length
    if ($Value -ceq $zeros) { return $false }
    return $true
}

function Test-SafePath([string]$Path) {
    if ([string]::IsNullOrEmpty($Path)) { return $false }
    if ($Path.IndexOf('\') -ge 0) { return $false }
    if ($Path.IndexOf([char]0xFFFD) -ge 0) { return $false }
    foreach ($c in $Path.ToCharArray()) {
        if ([char]::IsControl($c)) { return $false }
    }
    foreach ($component in $Path.Split('/')) {
        if ($component -eq '' -or $component -eq '.' -or $component -eq '..') { return $false }
    }
    return $true
}

function ConvertTo-RegexReplacement([string]$Value) {
    return $Value.Replace('$', '$$')
}

function Test-UnresolvedIncludeIsForbidden([string]$Include) {
    $text = $Include.Replace('\', '/')
    if (($text.IndexOf('docs') -ge 0) -and ($text.IndexOf('.md') -ge 0)) { return $true }
    if ($text.IndexOf('README') -ge 0) { return $true }
    return $false
}

function ConvertTo-RepoRelative([string]$Absolute, [string]$Root) {
    $full = $Absolute.Replace('\', '/')
    $prefix = $Root.Replace('\', '/').TrimEnd('/')
    $head = $prefix + '/'
    if ($full.StartsWith($head, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($head.Length)
    }
    return $null
}

function New-SegmentList([string]$Pattern) {
    $list = New-Object 'System.Collections.Generic.List[string]'
    foreach ($part in $Pattern.Split('/')) {
        if ($part.Length -gt 0) { [void]$list.Add($part) }
    }
    return ,$list
}

function Test-SegmentMatch([string]$Glob, [string]$Text) {
    $body = [regex]::Escape($Glob).Replace('\*', '[^/]*').Replace('\?', '[^/]')
    return [regex]::IsMatch($Text, '^' + $body + '$')
}

function Test-SegmentCanBeMarkdownFile([string]$Segment) {
    if (Test-SegmentMatch $Segment 'a.md') { return $true }
    if ($Segment.IndexOf('*') -lt 0 -and $Segment.IndexOf('?') -lt 0) {
        return $Segment.EndsWith('.md', [System.StringComparison]::Ordinal)
    }
    $concrete = $Segment.Replace('*', 'a').Replace('?', 'a')
    return $concrete.EndsWith('.md', [System.StringComparison]::Ordinal)
}

function Test-SegmentCanBeDirectory([string]$Segment) {
    if ($Segment.IndexOf('*') -lt 0 -and $Segment.IndexOf('?') -lt 0) {
        return $Segment.Length -gt 0
    }
    return (Test-SegmentMatch $Segment 'dir') -or (Test-SegmentMatch $Segment 'a')
}

function Test-GlobWalk($Pattern, [int]$Index, $Target, [int]$TargetIndex) {
    while ($true) {
        if ($Index -ge $Pattern.Count) { return ($TargetIndex -ge $Target.Count) }
        $seg = [string]$Pattern[$Index]
        if ($seg -eq '**') {
            if (Test-GlobWalk $Pattern ($Index + 1) $Target $TargetIndex) { return $true }
            if ($TargetIndex -ge $Target.Count) { return $false }
            $TargetIndex = $TargetIndex + 1
            continue
        }
        if ($TargetIndex -ge $Target.Count) { return $false }
        if (-not (Test-SegmentMatch $seg ([string]$Target[$TargetIndex]))) { return $false }
        $Index = $Index + 1
        $TargetIndex = $TargetIndex + 1
    }
}

function Test-PatternMatchesReadme([string]$Pattern) {
    $target = New-Object 'System.Collections.Generic.List[string]'
    [void]$target.Add('README.md')
    $segs = New-SegmentList $Pattern
    return Test-GlobWalk $segs 0 $target 0
}

function Test-AlignDocs($Pattern, [int]$Index, [int]$Phase, [int]$Depth) {
    if ($Depth -gt 24) { return $false }
    if ($Index -ge $Pattern.Count) { return ($Phase -eq 3) }
    if ($Phase -eq 3) { return $false }
    $seg = [string]$Pattern[$Index]
    if ($seg -eq '**') {
        if (Test-AlignDocs $Pattern ($Index + 1) $Phase ($Depth + 1)) { return $true }
        if ($Depth -gt 16) { return $false }
        if ($Phase -eq 0) {
            if (Test-AlignDocs $Pattern $Index 2 ($Depth + 1)) { return $true }
        }
        elseif ($Phase -eq 2) {
            if (Test-AlignDocs $Pattern $Index 2 ($Depth + 1)) { return $true }
            if (Test-AlignDocs $Pattern ($Index + 1) 3 ($Depth + 1)) { return $true }
        }
        return $false
    }
    if ($Phase -eq 0) {
        if (Test-SegmentMatch $seg 'docs') {
            return Test-AlignDocs $Pattern ($Index + 1) 2 ($Depth + 1)
        }
        return $false
    }
    if ($Phase -eq 2) {
        if ((Test-SegmentCanBeMarkdownFile $seg) -and (Test-AlignDocs $Pattern ($Index + 1) 3 ($Depth + 1))) {
            return $true
        }
        if (Test-SegmentCanBeDirectory $seg) {
            return Test-AlignDocs $Pattern ($Index + 1) 2 ($Depth + 1)
        }
        return $false
    }
    return $false
}

function Test-PatternSelectsDocsMarkdown([string]$Pattern) {
    $segs = New-SegmentList $Pattern
    return Test-AlignDocs $segs 0 0 0
}

function Normalize-RepoPattern([string]$Combined) {
    $stack = New-Object 'System.Collections.Generic.List[string]'
    foreach ($part in $Combined.Replace('\', '/').Split('/')) {
        if ($part -eq '' -or $part -eq '.') { continue }
        if ($part -eq '..') {
            if ($stack.Count -eq 0) { return 'outside' }
            $top = [string]$stack[$stack.Count - 1]
            if ($top.IndexOf('*') -ge 0 -or $top.IndexOf('?') -ge 0) { return 'escape' }
            $stack.RemoveAt($stack.Count - 1)
            continue
        }
        [void]$stack.Add($part)
    }
    if ($stack.Count -eq 0) { return 'outside' }
    return ($stack -join '/')
}

function Resolve-IncludePattern([string]$Root, [string]$DefinitionPath, [string]$Include) {
    $include = $Include.Replace('\', '/')
    $base = [System.IO.Path]::GetDirectoryName($DefinitionPath.Replace('\', '/'))
    if ([string]::IsNullOrEmpty($base)) {
        $base = ''
        $projectDir = '.'
        $fileDir = './'
    }
    else {
        $base = $base.Replace('\', '/')
        $projectDir = $base
        $fileDir = $base + '/'
    }
    $anchored = $include -match '(?i)\$\(\s*MSBuild(ThisFile|Project)Directory\s*\)'
    $expanded = [regex]::Replace($include, '(?i)\$\(\s*MSBuildThisFileDirectory\s*\)', (ConvertTo-RegexReplacement $fileDir))
    $expanded = [regex]::Replace($expanded, '(?i)\$\(\s*MSBuildProjectDirectory\s*\)', (ConvertTo-RegexReplacement $projectDir))
    if ($expanded.IndexOf('$(') -ge 0) {
        if (Test-UnresolvedIncludeIsForbidden $expanded) { return 'forbidden-property' }
        return 'unresolved'
    }
    if ($expanded -match '^[A-Za-z]:/' -or $expanded.StartsWith('/')) {
        $relative = ConvertTo-RepoRelative $expanded $Root
        if ($null -eq $relative) { return 'outside' }
        $expanded = $relative
    }
    elseif ($anchored) {
        if ($expanded.StartsWith('./')) { $expanded = $expanded.Substring(2) }
    }
    elseif ($base.Length -gt 0) {
        $expanded = $base + '/' + $expanded
    }
    return Normalize-RepoPattern $expanded
}

function Format-ApprovalRefusal([string]$Path, [string]$Item, [string]$Include) {
    return 'definition file includes docs markdown or the root README.md; -Update cannot approve it: ' + $Path + ' ' + $Item + ' Include=' + $Include
}

function Get-GitBytes([string]$Root, [string]$Arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $psi.Arguments = $Arguments
    $psi.WorkingDirectory = $Root
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    try {
        $started = $proc.Start()
    }
    catch {
        return $null
    }
    if (-not $started) { return $null }
    $errTask = $proc.StandardError.ReadToEndAsync()
    $ms = New-Object System.IO.MemoryStream
    try {
        $proc.StandardOutput.BaseStream.CopyTo($ms)
        $proc.WaitForExit()
        $null = $errTask.Result
    }
    catch {
        try { $proc.Kill() } catch { }
        return $null
    }
    if ($proc.ExitCode -ne 0) { return $null }
    return ,$ms.ToArray()
}

function Assert-NoDocumentationInput([string]$Root, [string[]]$Rows) {
    $names = @('EmbeddedResource', 'Content', 'None', 'Compile')
    $utf = New-Object System.Text.UTF8Encoding $false
    foreach ($row in $Rows) {
        $tab = $row.IndexOf([char]9)
        $meta = $row.Substring(0, $tab).Split(' ')
        $path = $row.Substring($tab + 1)
        if (-not (Test-ProjectXml $path)) { continue }
        $bytes = Get-GitBytes $Root ('cat-file blob ' + $meta[2])
        if ($null -eq $bytes) { Exit-Audit 2 'git cat-file failed' }
        $content = $utf.GetString($bytes)
        $doc = New-Object System.Xml.XmlDocument
        $doc.XmlResolver = $null
        try {
            $doc.LoadXml($content)
        }
        catch {
            Exit-Audit 1 ('definition file is not readable project XML and cannot be approved: ' + $path)
        }
        foreach ($node in $doc.GetElementsByTagName('*')) {
            $local = $node.LocalName
            $known = $false
            foreach ($name in $names) {
                if ($local -eq $name) { $known = $true }
            }
            if (-not $known) { continue }
            $include = $node.GetAttribute('Include')
            if ([string]::IsNullOrWhiteSpace($include)) { continue }
            foreach ($segment in $include.Split(';')) {
                $segment = $segment.Trim()
                if ($segment.Length -eq 0) { continue }
                $resolved = Resolve-IncludePattern $Root $path $segment
                $refuse = $false
                if ($resolved -eq 'forbidden-property' -or $resolved -eq 'escape') { $refuse = $true }
                elseif ($resolved -ne 'outside' -and $resolved -ne 'unresolved') {
                    if ((Test-PatternMatchesReadme $resolved) -or (Test-PatternSelectsDocsMarkdown $resolved)) {
                        $refuse = $true
                    }
                }
                if ($refuse) {
                    Exit-Audit 1 (Format-ApprovalRefusal $path $local $segment)
                }
            }
        }
    }
}

$bytes = Get-GitBytes $RepoRoot 'ls-tree -r -z HEAD'
if ($null -eq $bytes) {
    Exit-Audit 2 'git ls-tree failed'
}

$utf8 = New-Object System.Text.UTF8Encoding $false
$text = $utf8.GetString($bytes)
$trailing = [string]([char]0)
if ($text.Length -eq 0 -or -not $text.EndsWith($trailing, [System.StringComparison]::Ordinal)) {
    Exit-Audit 1 'ls-tree output is empty or missing its trailing NUL'
}
if ($text.IndexOf([char]0xFFFD) -ge 0) {
    Exit-Audit 1 'ls-tree output is not valid UTF-8'
}

$parts = $text.Split([char]0)
$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
$selected = New-Object 'System.Collections.Generic.List[string]'
$last = $parts.Length - 1
for ($i = 0; $i -lt $last; $i++) {
    $row = $parts[$i]
    $tab = $row.IndexOf([char]9)
    if ($tab -lt 0) { Exit-Audit 1 'ls-tree row has no tab' }
    $meta = $row.Substring(0, $tab).Split(' ')
    $path = $row.Substring($tab + 1)
    if ($meta.Length -ne 3) { Exit-Audit 1 'ls-tree metadata is not mode type object' }
    if (-not (Test-SafePath $path)) { Exit-Audit 1 'ls-tree path is not safe' }
    if (-not $seen.Add($path)) { Exit-Audit 1 'duplicate path' }
    $mode = $meta[0]
    $kind = $meta[1]
    $oid = $meta[2]
    $blob = ($kind -eq 'blob') -and ($mode -eq '100644' -or $mode -eq '100755' -or $mode -eq '120000')
    $gitlink = ($kind -eq 'commit') -and ($mode -eq '160000')
    if ((-not $blob) -and (-not $gitlink)) { Exit-Audit 1 'ls-tree row is not a blob or commit' }
    if (-not (Test-FullObjectId $oid)) { Exit-Audit 1 'ls-tree object id is not a full id' }
    if (-not (Test-DefinitionPath $path)) { continue }
    $regular = ($mode -eq '100644' -or $mode -eq '100755') -and ($kind -eq 'blob')
    if (-not $regular) { Exit-Audit 1 'definition path is not a regular blob' }
    [void]$selected.Add($row)
}

$arr = $selected.ToArray()
[System.Array]::Sort($arr, [System.StringComparer]::Ordinal)
Assert-NoDocumentationInput $RepoRoot $arr
$payload = [string]::Join($trailing, $arr) + $trailing
$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($payload))
}
finally {
    $sha.Dispose()
}
$hex = ([System.BitConverter]::ToString($hash)).Replace('-', '')
$count = $arr.Length
Write-Output ("digest=" + $hex + " count=" + $count)

if ($Update.IsPresent) {
    $body = $hex + "`n" + $count.ToString([System.Globalization.CultureInfo]::InvariantCulture) + "`n"
    $manifestDir = [System.IO.Path]::GetDirectoryName($ManifestPath)
    if (-not [System.IO.Directory]::Exists($manifestDir)) {
        [void][System.IO.Directory]::CreateDirectory($manifestDir)
    }
    [System.IO.File]::WriteAllText($ManifestPath, $body, [System.Text.Encoding]::ASCII)
    exit 0
}

if (-not [System.IO.File]::Exists($ManifestPath)) {
    Exit-Audit 1 'manifest missing'
}
$lines = [System.IO.File]::ReadAllLines($ManifestPath)
if ($lines.Length -ne 2) { Exit-Audit 1 'manifest must be two lines' }
$manifestDigest = $lines[0]
$manifestCountText = $lines[1]
if ($manifestDigest -cnotmatch '^[0-9A-F]{64}$') {
    Exit-Audit 1 'manifest digest is not 64 uppercase hex characters'
}
if ($manifestCountText -notmatch '^[0-9]+$') {
    Exit-Audit 1 'manifest count is not a decimal integer'
}
$manifestCount = [int]::Parse($manifestCountText, [System.Globalization.CultureInfo]::InvariantCulture)
if ($manifestDigest -cne $hex -or $manifestCount -ne $count) {
    Exit-Audit 1 'manifest does not match the selected graph'
}
exit 0
