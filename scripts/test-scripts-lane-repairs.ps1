param(
    [ValidateSet('opaque', 'owned', 'origin')][string]$Case,
    [string]$HostCleanupDll
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$root = Join-Path ([IO.Path]::GetTempPath()) ('scripts-lane-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
try {
    if ($Case -eq 'origin') {
        . "$PSScriptRoot/lib/nightly-common.ps1"
        & git init --quiet $root
        if ($LASTEXITCODE -ne 0) { throw 'git init failed' }
        # This is a string witness even on Linux: Git quotes and escapes these values.
        foreach ($origin in @('C:\Antiphon\origin.git', 'C:\a space\a"quote\origin.git', '\\server\share\origin.git', 'https://example.invalid/repo')) {
            & git -C $root config --local remote.other.url 'https://wrong.invalid/first'
            & git -C $root config --local remote.origin.url $origin
            if ($LASTEXITCODE -ne 0) { throw 'git config failed' }
            $actual = Get-NightlyOriginUrl $root
            if ($actual -cne $origin) { throw "origin mismatch: expected <$origin>, actual <$actual>" }
        }
        & git -C $root config --local --unset remote.origin.url
        if ((Get-NightlyOriginUrl $root) -cne '') { throw 'missing origin was admitted' }
        'PASS origin: 4 exact strings and missing origin'
        return
    }
    $opaque = Join-Path $root 'bin-private/nested/bin-archive/report.md'
    [IO.Directory]::CreateDirectory((Split-Path $opaque -Parent)) | Out-Null
    [IO.File]::WriteAllText($opaque, 'irreplaceable')
    [IO.Directory]::SetLastWriteTime((Join-Path $root 'bin-private'), [datetime]::Now.AddDays(-10))
    $context = $null
    if ($Case -eq 'owned') {
        [void][Reflection.Assembly]::LoadFrom($HostCleanupDll)
        $context = [Antiphon.Checkpoints.CheckpointCleanupDefaults]::Context
        $owned = Join-Path $root 'bin-owned'
        $ownership = [Antiphon.Checkpoints.SafeWrite]::Under($root)
        $reason = [Antiphon.Checkpoints.SafeWrite]::CreateFreshDirectory($owned, $ownership)
        if ($null -ne $reason) { throw "create: $reason" }
        [IO.File]::WriteAllText((Join-Path $owned 'payload.txt'), 'regenerable')
        $reason = [Antiphon.Checkpoints.OwnedOutputLedger]::Seal($owned, [datetimeoffset]::UtcNow)
        if ($null -ne $reason) { throw "seal: $reason" }
    }
    # The old implementation also calls robocopy. Supply a harmless fixture-only stand-in
    # on Linux; Remove-Item still exercises the destructive old path.
    function robocopy { }
    if ($Case -eq 'owned') {
        & "$PSScriptRoot/cleanup-build-junk.ps1" -RepoRoot $root -SkipIfModifiedWithinMinutes 0 -CleanupContext $context
        if ([IO.Directory]::Exists($owned)) { throw 'owned output was retained' }
    } else {
        & "$PSScriptRoot/cleanup-build-junk.ps1" -RepoRoot $root -SkipIfModifiedWithinMinutes 0
    }
    if (-not [IO.File]::Exists($opaque)) { throw 'unowned bin-* bytes were deleted' }
    if ([IO.File]::ReadAllText($opaque) -cne 'irreplaceable') { throw 'unowned bytes changed' }
    "PASS $Case`: unowned preserved" + $(if ($Case -eq 'owned') { ', owned removed' })
} finally {
    [IO.Directory]::Delete($root, $true)
}
