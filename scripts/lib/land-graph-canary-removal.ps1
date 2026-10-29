# Guarded removal of one recorded land-graph canary directory.
# The audit script dot-sources this file. It is not a script parameter and it never exits:
# a successful audit exit is only the production script after both passes approve.
# ASCII-only and Windows PowerShell 5.1 compatible.
$ErrorActionPreference = 'Stop'

function Remove-RecordedCanary([string]$RecordPath, [string]$RepositoryRoot) {
    if ([string]::IsNullOrWhiteSpace($RecordPath)) { return }
    if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { return }
    # GetDirectoryName("repo/") is the repository, so a trailing separator would
    # treat a canary-named directory inside the repo as a sibling of it.
    if ($RepositoryRoot.EndsWith('/') -or $RepositoryRoot.EndsWith('\')) { return } # guard:repo-separator
    if (-not [System.IO.Directory]::Exists($RepositoryRoot)) { return } # guard:repo-directory
    if (-not [System.IO.File]::Exists($RecordPath)) { return }
    $recordedRaw = [System.IO.File]::ReadAllText($RecordPath)
    if ($recordedRaw.IndexOf("`n") -ge 0 -or $recordedRaw.IndexOf("`r") -ge 0) { return }
    $recorded = $recordedRaw.Trim()
    if ([string]::IsNullOrWhiteSpace($recorded)) { return }
    $hasTrailingSeparator = $recorded.EndsWith('/') -or $recorded.EndsWith('\')
    $stripped = $recorded.TrimEnd('/', '\')
    if ([string]::IsNullOrWhiteSpace($stripped)) { return }
    $strippedFull = [System.IO.Path]::GetFullPath($stripped)
    $name = [System.IO.Path]::GetFileName($strippedFull)
    if ($name -cnotmatch '\Aantiphon-landgraph-canary-[0-9a-f]{32}\z') { return } # guard:name
    $parent = [System.IO.Path]::GetDirectoryName($strippedFull)
    $expectedParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetDirectoryName($RepositoryRoot))
    if (-not [string]::Equals($parent, $expectedParent, [System.StringComparison]::Ordinal)) { return } # guard:parent
    $roundTrip = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($parent, $name))
    if ($hasTrailingSeparator -or -not [string]::Equals($strippedFull, $roundTrip, [System.StringComparison]::Ordinal)) { return } # guard:rebuilt
    if ([string]::Equals($strippedFull, [System.IO.Path]::GetFullPath($RepositoryRoot), [System.StringComparison]::Ordinal)) { return } # guard:repo-root
    if (-not [System.IO.Directory]::Exists($strippedFull)) { return }
    $link = ([System.IO.File]::GetAttributes($strippedFull) -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
    try {
        if ($link) { [System.IO.Directory]::Delete($strippedFull, $false) }
        else { [System.IO.Directory]::Delete($strippedFull, $true) }
    } catch {
        [Console]::Error.WriteLine('canary delete failed: ' + $_.Exception.Message)
    }
}
