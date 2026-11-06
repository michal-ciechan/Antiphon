# CARD-1733 regression cases; loaded only by the release-gate test harness.
function Test-C1733_NativeMetadata {
    $root = New-C599Root
    $path = Join-Path $root 'missing/child'
    $script:C1733ProviderCalls = 0
    function Test-Path { param($LiteralPath) $script:C1733ProviderCalls++; Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath }
    function Get-Item { param($LiteralPath, [switch]$Force) $script:C1733ProviderCalls++; Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath -Force:$Force }
    function Split-Path { param($Parent) $script:C1733ProviderCalls++; Microsoft.PowerShell.Management\Split-Path -Parent $Parent }
    Assert-C1339ContainedPath $root $path
    Assert-C487 ($script:C1733ProviderCalls -eq 0) 'C1733 native-metadata' ('provider calls=' + $script:C1733ProviderCalls)
}

function Test-C1733_BoundariesAndFreshLinks {
    $root = New-C599Root
    $path = Join-Path $root 'child'
    [void][IO.Directory]::CreateDirectory($path)
    Assert-C1339ContainedPath $root $path
    Assert-C1339ContainedPath $root (Join-Path $path 'absent/leaf')
    $refused = $false
    try { Assert-C1339ContainedPath $root ($root + '-sibling/leaf') } catch { $refused = $_.Exception.Message -match 'fixture-path-outside-root' }
    Assert-C487 $refused 'C1733 sibling-prefix'
    $refused = $false
    try { Assert-C1339ContainedPath $root $root } catch { $refused = $_.Exception.Message -match 'fixture-path-outside-root' }
    Assert-C487 $refused 'C1733 root-boundary'
    # Replace a previously accepted ancestor. Fresh metadata must observe the link.
    [IO.Directory]::Delete($path)
    $target = New-C599Root
    [void][IO.Directory]::CreateSymbolicLink($path, $target)
    $refused = $false
    try { Assert-C1339ContainedPath $root (Join-Path $path 'absent/leaf') } catch { $refused = $_.Exception.Message -match 'fixture-linked-path' }
    Assert-C487 $refused 'C1733 changed-ancestor-link'
    $alias = Join-Path (New-C599Root) 'alias'
    [void][IO.Directory]::CreateSymbolicLink($alias, $root)
    $refused = $false
    try { Assert-C1339ContainedPath (Join-Path $alias 'nested') (Join-Path $alias 'nested/absent') } catch { $refused = $_.Exception.Message -match 'fixture-linked-path' }
    Assert-C487 $refused 'C1733 above-root-link'
    $dangling = Join-Path $root 'dangling'
    [void][IO.File]::CreateSymbolicLink($dangling, (Join-Path $target 'absent'))
    $refused = $false
    try { Assert-C1339ContainedPath $root $dangling } catch { $refused = $_.Exception.Message -match 'fixture-linked-path' }
    Assert-C487 $refused 'C1733 dangling-link'
}

function Test-C1733_SharedSeed {
    $script:C1733SetupGit = 0
    $script:C1733OriginalGit = ${function:Invoke-C1339GitProcess}
    function Invoke-C1339GitProcess {
        param($Root, $WorkingDirectory, $Arguments)
        if ($Arguments[0] -in @('init', 'commit')) { $script:C1733SetupGit++ }
        & $script:C1733OriginalGit @PSBoundParameters
    }
    $a = New-C1339Fixture
    $script:C1733SetupGit = 0
    $b = New-C1339Fixture
    Assert-C487 ($script:C1733SetupGit -eq 0) 'C1733 seed-built-once' ('repeated setup commands=' + $script:C1733SetupGit)
    Add-C1339Tag $a $a.FinalTag $a.Head
    [IO.File]::WriteAllText($a.Gh.StorePath, '{"releases":[]}')
    [IO.File]::WriteAllText($a.FaultPath, '{"push":"before"}')
    $remote = Invoke-C1339LocalGit $b @('show-ref', '--verify', '--hash', ('refs/tags/' + $b.FinalTag)) -Remote -AllowAbsent
    Assert-C487 (-not $remote -and -not [IO.File]::Exists($b.FaultPath) -and $a.Root -cne $b.Root -and $a.CardApi.Url -cne $b.CardApi.Url) 'C1733 private-recipients'
    $valid = Invoke-C1339Publish $b -WhatIf
    Assert-C487 ($valid.ExitCode -eq 0) 'C1733 clone-fresh-authority' $valid.Refusal
}

function Test-C1733_ReservationFinally {
    $fx = New-C1339PromotionFixture
    $valid = Invoke-C1339Promote $fx @{ WhatIf = $true }
    Assert-C487 ($valid.ExitCode -eq 0) 'C1733 first-control' $valid.Refusal
    $snap = Save-C599ASnapshot $fx
    $path = Join-Path $fx.ReleaseRoot 'publications.json'
    $original = [IO.File]::ReadAllBytes($path)
    $threw = $false
    try { Invoke-C1339ReservationVariant $fx $snap { [IO.File]::WriteAllText($path, '{broken'); throw 'mutation-threw' } 'unused' } catch { $threw = $_.Exception.Message -match 'mutation-threw' }
    Assert-C487 ($threw -and [Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -ceq [Convert]::ToBase64String($original)) 'C1733 throwing-mutation-restores'
    $next = Invoke-C1339Promote $fx @{ WhatIf = $true }
    Assert-C487 ($next.ExitCode -eq 0) 'C1733 next-variant-valid' $next.Refusal
}
