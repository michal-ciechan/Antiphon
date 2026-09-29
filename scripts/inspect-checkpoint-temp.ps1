param(
    [int]$MaxEntries = 512,
    [int]$MaxDescendants = 10000,
    [string]$OutputFile
)

$ErrorActionPreference = 'Stop'
if ($MaxEntries -lt 1 -or $MaxEntries -gt 10000 -or $MaxDescendants -lt 1 -or $MaxDescendants -gt 100000) {
    throw 'Inspection bounds are outside the supported range.'
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$result = [ordered]@{
    schemaVersion = 1
    tempRoot = $tempRoot
    inspectedAt = [DateTimeOffset]::UtcNow.ToString('o')
    maxEntries = $MaxEntries
    maxDescendants = $MaxDescendants
    entriesExamined = 0
    roots = @()
    truncated = $false
}
$entries = Get-ChildItem -LiteralPath $tempRoot -Force | Select-Object -First ($MaxEntries + 1)
foreach ($entry in $entries) {
    if ($result.entriesExamined -ge $MaxEntries) { $result.truncated = $true; break }
    $result.entriesExamined++
    if (-not $entry.PSIsContainer -or $entry.Name -cnotmatch '^c723-[0-9a-f]{32}$') { continue }
    $markerPath = Join-Path $entry.FullName '.checkpoint-test-root.json'
    $reason = 'legacy-unmarked'
    $marker = $null
    if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
        try {
            $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
            if ($marker.Version -eq 1 -and $marker.RootId -ceq $entry.Name.Substring(5) -and
                [IO.Path]::GetFullPath([string]$marker.RootPath) -ceq [IO.Path]::GetFullPath($entry.FullName)) {
                $reason = 'marked-identity-unverified'
            } else { $reason = 'marker-invalid' }
        } catch { $reason = 'marker-unreadable' }
    }
    $count = 0
    [long]$logicalBytes = 0
    $sampleTruncated = $false
    try {
        foreach ($child in Get-ChildItem -LiteralPath $entry.FullName -Recurse -Force) {
            if ($count -ge $MaxDescendants) { $sampleTruncated = $true; break }
            $count++
            if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                $reason = 'linked-descendant'
                $sampleTruncated = $true
                break
            }
            if (-not $child.PSIsContainer) { $logicalBytes += $child.Length }
        }
    } catch {
        $reason = 'inventory-incomplete'
        $sampleTruncated = $true
    }
    $result.roots += [ordered]@{
        path = $entry.FullName
        reason = $reason
        sampledDescendants = $count
        sampledLogicalBytes = $logicalBytes
        sampleTruncated = $sampleTruncated
    }
}
$json = $result | ConvertTo-Json -Depth 8
if ($OutputFile) {
    $parent = Split-Path -Parent $OutputFile
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputFile), $json)
}
$json
