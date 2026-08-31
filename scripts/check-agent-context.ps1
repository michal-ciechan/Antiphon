# Enforces CARD-0254's universal-agent-context byte budget.
# Raw bytes are authoritative: character and word counts can hide UTF-8 growth.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$targetBytes = 24576
$hardCeilingBytes = 30720
$repoRoot = Split-Path -Parent $PSScriptRoot
$path = Join-Path $repoRoot 'AGENTS.md'

if (-not (Test-Path -LiteralPath $path)) {
    Write-Error "AGENTS.md was not found at $path."
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes($path)
$size = $bytes.Length
$encoding = [System.Text.UTF8Encoding]::new($false, $true)
try {
    $text = $encoding.GetString($bytes)
} catch {
    Write-Error "AGENTS.md is not valid UTF-8: $($_.Exception.Message)"
    exit 1
}

Write-Host "AGENTS.md raw UTF-8 bytes: $size"
Write-Host "Delivery target: <= $targetBytes bytes"
Write-Host "Hard ceiling:    <= $hardCeilingBytes bytes"

$sections = [regex]::Matches($text, '(?m)^## .+$')
if ($sections.Count -eq 0) {
    Write-Error 'AGENTS.md has no level-two sections to report.'
    exit 1
}

Write-Host ''
Write-Host 'Section sizes (raw UTF-8 bytes):'
$sectionRows = @()
for ($index = 0; $index -lt $sections.Count; $index++) {
    $start = $sections[$index].Index
    $end = if ($index + 1 -lt $sections.Count) { $sections[$index + 1].Index } else { $text.Length }
    $name = $sections[$index].Value.Substring(3).Trim()
    $sectionBytes = $encoding.GetByteCount($text.Substring($start, $end - $start))
    $sectionRows += [pscustomobject]@{ Section = $name; Bytes = $sectionBytes }
}
$sectionRows | Format-Table -AutoSize

if ($size -gt $hardCeilingBytes) {
    Write-Error "AGENTS.md is $size raw UTF-8 bytes: above the $hardCeilingBytes-byte hard ceiling by $($size - $hardCeilingBytes) bytes."
    exit 1
}

if ($size -gt $targetBytes) {
    Write-Error "AGENTS.md is $size raw UTF-8 bytes: above the $targetBytes-byte delivery target by $($size - $targetBytes) bytes (still below the $hardCeilingBytes-byte hard ceiling)."
    exit 1
}

Write-Host "PASS: AGENTS.md is within the $targetBytes-byte delivery target." -ForegroundColor Green
exit 0
