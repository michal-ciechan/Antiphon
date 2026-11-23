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
