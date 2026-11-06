# CARD-1731: exercise the actual snapshot/variant helpers, not a substitute gate.
function Test-C1731_SnapshotIsolation {
    $fx = New-C599PublishFx
    $binary = Join-Path $fx.ReleaseRoot 'nested/binary.dat'
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($binary))
    [IO.File]::WriteAllBytes($binary, [byte[]]@(0, 255, 1, 128))
    $snap = Save-C599ASnapshot $fx
    $control = Get-C599AGate $fx
    Assert-C487 $control.Ok 'C1731 first variant starts from a valid control'
    $extra = Join-Path $fx.ReleaseRoot 'unexpected.dat'
    $absent = $fx.Git.Trace
    $cleanupCalled = $false
    $failed = Invoke-C599AVariant $fx $snap 'throw after mutation' -Expect @('unreachable') -Mutate {
        [IO.File]::WriteAllBytes($binary, [byte[]]@(128, 1, 255, 0))
        [IO.File]::WriteAllText($extra, 'unexpected')
        [IO.File]::WriteAllText($absent, 'recipient trace')
        [IO.File]::Delete($fx.Gh.StorePath)
        throw 'C1731 injected mutation failure'
    } -Cleanup { $script:C1731CleanupCalled = $true }
    Assert-C487 (-not $failed.Pass -and $failed.Detail.Contains('C1731 injected mutation failure') -and $script:C1731CleanupCalled) 'C1731 throwing mutation fails and runs cleanup'
    $same = $true
    foreach ($p in $snap.Files.Keys) {
        if (-not [IO.File]::Exists($p) -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($p)) -cne [Convert]::ToBase64String($snap.Files[$p])) { $same = $false }
    }
    Assert-C487 ($same -and -not [IO.File]::Exists($extra) -and -not [IO.File]::Exists($absent)) 'C1731 finally restores every byte and absence after failure'
    $next = Get-C599AGate $fx
    Assert-C487 $next.Ok 'C1731 next variant sees a valid isolated control'
    [IO.File]::Delete($binary)
    [IO.Directory]::Delete([IO.Path]::GetDirectoryName($binary))
    Restore-C599ASnapshot $snap
    Assert-C487 ([Convert]::ToBase64String([IO.File]::ReadAllBytes($binary)) -ceq 'AP8BgA==') 'C1731 missing parent and binary file are recreated'
}

function Test-C1731_SnapshotWork {
    $fx = New-C599PublishFx
    $snap = Save-C599ASnapshot $fx
    # Count real helper invocations while retaining the original implementation.
    $restoreBody = (Get-Command Restore-C599ASnapshot).ScriptBlock
    $script:C1731Restores = 0
    function Restore-C599ASnapshot {
        param($Snap)
        $script:C1731Restores++
        & $restoreBody -Snap $Snap
    }
    $failed = Invoke-C599AVariant $fx $snap 'restore count' -Expect @('unreachable') -Mutate { throw 'C1731 count witness' }
    Assert-C487 (-not $failed.Pass -and $script:C1731Restores -eq 1) 'C1731 one mandatory restore per failed variant' ('restores=' + $script:C1731Restores)
    $path = Join-Path $fx.ReleaseRoot 'unchanged.dat'
    [IO.File]::WriteAllBytes($path, [byte[]]@(0, 255, 1, 128))
    $snap = Save-C599ASnapshot $fx
    $stamp = [datetime]'2001-02-03T04:05:06Z'
    [IO.File]::SetLastWriteTimeUtc($path, $stamp)
    Restore-C599ASnapshot $snap
    Assert-C487 ([IO.File]::GetLastWriteTimeUtc($path) -eq $stamp) 'C1731 unchanged snapshot files are not rewritten'
    [IO.File]::WriteAllBytes($path, [byte[]]@(128, 1, 255, 0))
    [IO.File]::SetLastWriteTimeUtc($path, $stamp)
    Restore-C599ASnapshot $snap
    Assert-C487 ([Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -ceq 'AP8BgA==') 'C1731 same length and timestamp cannot hide changed bytes'
}
