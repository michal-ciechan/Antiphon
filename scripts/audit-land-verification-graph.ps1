# CARD-1385: hash the dotnet-build definition files at HEAD and compare them
# with server/Application/Services/LandVerificationBuildGraph.manifest.txt.
# ASCII-only: must parse under Windows PowerShell 5.1.
#
# Usage:
#   pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 [-Check]
#   pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Update
#
# -Check is the default. It prints digest=<HEX> count=<N> and exits 0 only
# when both manifest lines match. -Update rewrites the manifest from HEAD.
# Exit 1 is a mismatch or bad usage. Exit 2 is a git failure.
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

function Get-HeadTreeBytes([string]$Root) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $psi.Arguments = 'ls-tree -r -z HEAD'
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

$bytes = Get-HeadTreeBytes $RepoRoot
if ($null -eq $bytes) {
    Exit-Audit 2 'git ls-tree failed'
}

$utf8 = New-Object System.Text.UTF8Encoding $false
$text = $utf8.GetString($bytes)
$trailing = [string]([char]0)
if ($text.Length -eq 0 -or -not $text.EndsWith($trailing)) {
    Exit-Audit 1 'ls-tree output is empty or missing its trailing NUL'
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
    if (-not $seen.Add($path)) { Exit-Audit 1 'duplicate path' }
    if (-not (Test-DefinitionPath $path)) { continue }
    $mode = $meta[0]
    $kind = $meta[1]
    $oid = $meta[2]
    $regular = ($mode -eq '100644' -or $mode -eq '100755') -and $kind -eq 'blob' -and (Test-FullObjectId $oid)
    if (-not $regular) { Exit-Audit 1 'definition path is not a regular blob' }
    [void]$selected.Add($row)
}

$arr = $selected.ToArray()
[System.Array]::Sort($arr, [System.StringComparer]::Ordinal)
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
$manifestCount = [int]$manifestCountText
if ($manifestDigest -cne $hex -or $manifestCount -ne $count) {
    Exit-Audit 1 'manifest does not match the selected graph'
}
exit 0
