# CARD-1733 test-only immutable templates and private mutable case state.
$script:C1339Seeds = @{}
$global:C1733Timing = [ordered]@{
    seeds = 0; clones = 0; seedSeconds = 0.0; cloneSeconds = 0.0
    containmentCalls = 0; containmentSeconds = 0.0
    saves = 0; saveSeconds = 0.0; restores = 0; restoreSeconds = 0.0
    writes = 0; unchanged = 0; teardownSeconds = 0.0
}

function New-C1339Fixture {
    param([switch]$Stamp, [switch]$NoCandidateRemote)
    $ErrorActionPreference = 'Stop'
    # Stamp and long-lived candidates have different immutable authority identities.
    $key = [string][bool]$Stamp
    if (-not $script:C1339Seeds.ContainsKey($key)) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $script:C1339Seeds[$key] = New-C1339FixtureSeed -Stamp:$Stamp
        $global:C1733Timing.seeds++
        $global:C1733Timing.seedSeconds += $watch.Elapsed.TotalSeconds
    }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $seed = $script:C1339Seeds[$key]
    $root = New-C599Root
    # No hardlinks, alternates or shared refs. The seed is never returned to a case.
    foreach ($dir in [IO.Directory]::EnumerateDirectories($seed.Root, '*', [IO.SearchOption]::AllDirectories)) {
        Assert-C1339ContainedPath $seed.Root $dir
        [void][IO.Directory]::CreateDirectory((Join-Path $root ([IO.Path]::GetRelativePath($seed.Root, $dir))))
    }
    $oldJsonRoot = ($seed.Root | ConvertTo-Json -Compress).Trim('"')
    $newJsonRoot = ($root | ConvertTo-Json -Compress).Trim('"')
    foreach ($file in [IO.Directory]::EnumerateFiles($seed.Root, '*', [IO.SearchOption]::AllDirectories)) {
        Assert-C1339ContainedPath $seed.Root $file
        $relative = [IO.Path]::GetRelativePath($seed.Root, $file)
        $destination = Join-Path $root $relative
        [IO.File]::Copy($file, $destination)
        # Git metadata/object bytes are immutable and must remain byte-identical.
        if ($relative.Replace('\', '/') -match '^(checkout/\.git/|remote\.git/)') { continue }
        $text = [IO.File]::ReadAllText($destination)
        $rebound = $text.Replace($oldJsonRoot, $newJsonRoot).Replace($seed.Root, $root)
        if ($rebound -cne $text) { [IO.File]::WriteAllText($destination, $rebound, [Text.UTF8Encoding]::new($false)) }
    }
    # Clone the descriptor too, rebinding its private API route independently of paths.
    $oldRoute = '/fx/' + [IO.Path]::GetFileName($seed.Root)
    $newRoute = '/fx/' + [IO.Path]::GetFileName($root)
    $json = ($seed | ConvertTo-Json -Depth 20 -Compress).Replace($oldJsonRoot, $newJsonRoot).Replace($oldRoute, $newRoute)
    $fx = $json | ConvertFrom-Json
    $script:C599PolicyClones.Add($fx.Checkout)
    $script:C599PolicyClones.Add($fx.Bare)
    if ($NoCandidateRemote) {
        [void](Invoke-C1339LocalGit $fx @('update-ref', '-d', ('refs/heads/' + $fx.CandidateRef)) -Remote)
    }
    $global:C1733Timing.clones++
    $global:C1733Timing.cloneSeconds += $watch.Elapsed.TotalSeconds
    return $fx
}

function Save-C1339Snapshot {
    param($Fx)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $snap = Save-C599ASnapshot $Fx
    Initialize-C1339Snapshot $snap
    $global:C1733Timing.saves++
    $global:C1733Timing.saveSeconds += $watch.Elapsed.TotalSeconds
    return $snap
}

function Initialize-C1339Snapshot {
    param($Snap)
    if ($null -ne $Snap.Entries) { return }
    # Cache immutable expected bytes/parents, never a previous acceptance decision.
    $parents = @{}
    $entries = @(foreach ($path in $Snap.Files.Keys) {
        $parents[[IO.Path]::GetDirectoryName($path)] = $true
        [pscustomobject]@{ Path = $path; Bytes = $Snap.Files[$path]; Encoded = [Convert]::ToBase64String($Snap.Files[$path]) }
    })
    $Snap | Add-Member Entries $entries
    $Snap | Add-Member Parents @($parents.Keys)
}

function Restore-C1339Snapshot {
    param($Snap)
    $ErrorActionPreference = 'Stop'
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $global:C1733Timing.restores++
    try {
        Initialize-C1339Snapshot $Snap
        foreach ($file in [IO.Directory]::EnumerateFiles($Snap.Root, '*', [IO.SearchOption]::AllDirectories)) {
            if (-not $Snap.Files.ContainsKey($file)) { [IO.File]::Delete($file) }
        }
        foreach ($path in $Snap.Absent) { if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) } }
        foreach ($parent in $Snap.Parents) { [void][IO.Directory]::CreateDirectory($parent) }
        foreach ($entry in $Snap.Entries) {
            if ([IO.File]::Exists($entry.Path) -and
                [Convert]::ToBase64String([IO.File]::ReadAllBytes($entry.Path)) -ceq $entry.Encoded) {
                $global:C1733Timing.unchanged++
                continue
            }
            [IO.File]::WriteAllBytes($entry.Path, $entry.Bytes)
            $global:C1733Timing.writes++
        }
    } finally { $global:C1733Timing.restoreSeconds += $watch.Elapsed.TotalSeconds }
}

function Complete-C1733FixtureRun {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try { Stop-C599CardStub; Clear-C599FixtureReadOnly }
    finally {
        $global:C1733Timing.teardownSeconds += $watch.Elapsed.TotalSeconds
        if ($env:C1733_TIMING_ROOT) {
            $timingRoot = $env:C1733_TIMING_ROOT
            if (-not [IO.Path]::IsPathRooted($timingRoot)) { $timingRoot = Join-Path $repoRoot $timingRoot }
            [void][IO.Directory]::CreateDirectory($timingRoot)
            $record = [ordered]@{ case = $Case; phases = $global:C1733Timing }
            $path = Join-Path $timingRoot ($Case + '-' + [guid]::NewGuid().ToString('N') + '.json')
            [IO.File]::WriteAllText($path, ($record | ConvertTo-Json -Depth 8))
        }
    }
}
