# CARD-1545 S1a live-lane manifest, discovery adapter, gate scan and census.
# ASCII-only. Diagnostic inventory only: list-tests of the owned assembly.
# No provider call, no live Claude, no policy edit.

if ($script:AntiphonLiveLaneManifestLoaded) { return }
$script:AntiphonLiveLaneManifestLoaded = $true

$script:LiveLanePreS1BaseSha = '1905f840d6c0f1cbb3bc5bc255ce56d3e23f00f9'
$script:LiveLaneFixtureRel = 'tests/fixtures/live-lanes/full-discovery-before.json'
$script:LiveLaneAllowedSeedPaths = @(
    'tests/live-lanes.json',
    'scripts/lib/live-lane-manifest.ps1',
    'scripts/test-live-lane-manifest.ps1',
    'tests/Antiphon.Tests/Scripts/LiveLaneInventoryTests.cs',
    'tests/Antiphon.Agents.Pty.Tests/LiveClaudeInventoryTests.cs',
    'tests/fixtures/live-lanes/full-discovery-before.json'
)
$script:LiveLaneProtectedPaths = @(
    'tests/test-execution-policy.json',
    'scripts/lib/nightly-common.ps1',
    'scripts/lib/nightly-policy.ps1',
    'scripts/lib/nightly-coverage.ps1'
)

# Dot-source in this script scope. A dot-source inside Initialize-LiveLaneNightly
# would define the readers only until that function returned.
. (Join-Path $PSScriptRoot 'nightly-common.ps1')
. (Join-Path $PSScriptRoot 'nightly-coverage.ps1')
. (Join-Path $PSScriptRoot 'nightly-policy.ps1')
. (Join-Path $PSScriptRoot 'nightly-tests-impl.ps1')
$script:LiveLaneNightlyReady = $true

function ConvertTo-LiveLaneList {
    param($Value)
    if ($null -eq $Value) { return @() }
    return @($Value)
}

function Get-LiveLaneProp {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) {
        foreach ($key in @($Object.Keys)) {
            if ([string]::Equals([string]$key, $Name, [StringComparison]::OrdinalIgnoreCase)) { return $Object[$key] }
        }
    }
    foreach ($p in @($Object.PSObject.Properties)) {
        if ([string]::Equals([string]$p.Name, $Name, [StringComparison]::OrdinalIgnoreCase)) { return $p.Value }
    }
    return $null
}

function Get-LiveLaneRepoRoot {
    param([string]$Start)
    if ([string]::IsNullOrWhiteSpace($Start)) { $Start = (Get-Location).Path }
    $dir = Get-Item -LiteralPath $Start -ErrorAction SilentlyContinue
    if ($dir -and -not $dir.PSIsContainer) { $dir = $dir.Directory }
    while ($null -ne $dir) {
        if (Test-Path -LiteralPath (Join-Path $dir.FullName 'Antiphon.sln')) { return $dir.FullName }
        $dir = $dir.Parent
    }
    throw 'repo-root-missing'
}

function Get-LiveLaneContract {
    return @(
        [pscustomobject]@{ project = 'Antiphon.Tests'; namespace = 'Antiphon.Tests.Agents'; class = 'ClaudeDialogWatchdogCanaryTests'; method = 'C680_LiveNudgeRefusesPermissionAndQuestionDialogs'; arguments = @() },
        [pscustomobject]@{ project = 'Antiphon.Agents.Pty.Tests'; namespace = 'Antiphon.Agents.Pty.Tests'; class = 'ClaudeSubmitContractLiveTests'; method = 'Submitting_via_two_writes_completes_a_turn'; arguments = @('claude') },
        [pscustomobject]@{ project = 'Antiphon.Agents.Pty.Tests'; namespace = 'Antiphon.Agents.Pty.Tests'; class = 'ClaudeRemoteControlAtStartupCanaryTests'; method = 'Control_auto_connects_a_bridge_session_before_the_first_user'; arguments = @() },
        [pscustomobject]@{ project = 'Antiphon.Agents.Pty.Tests'; namespace = 'Antiphon.Agents.Pty.Tests'; class = 'ClaudeRemoteControlAtStartupCanaryTests'; method = 'Settings_file_prevents_auto_connect_and_keeps_startup_records'; arguments = @() },
        [pscustomobject]@{ project = 'Antiphon.Agents.Pty.Tests'; namespace = 'Antiphon.Agents.Pty.Tests'; class = 'ClaudeRemoteControlAtStartupCanaryTests'; method = 'Remote_control_slash_command_still_arms_after_settings_off'; arguments = @() },
        [pscustomobject]@{ project = 'Antiphon.Agents.Pty.Tests'; namespace = 'Antiphon.Agents.Pty.Tests'; class = 'ClaudeRemoteControlMenuCanaryTests'; method = 'C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt'; arguments = @() }
    )
}

function Get-LiveLaneArgumentKey {
    param($Arguments)
    $items = @(ConvertTo-LiveLaneList $Arguments | ForEach-Object { [string]$_ })
    return ($items -join "`n")
}

function Get-LiveLaneIdentityKey {
    param($Row)
    $project = [string](Get-LiveLaneProp $Row 'project')
    $ns = [string](Get-LiveLaneProp $Row 'namespace')
    $class = [string](Get-LiveLaneProp $Row 'class')
    if ([string]::IsNullOrWhiteSpace($class)) { $class = [string](Get-LiveLaneProp $Row 'type') }
    $method = [string](Get-LiveLaneProp $Row 'method')
    $argumentKey = Get-LiveLaneArgumentKey (Get-LiveLaneProp $Row 'arguments')
    return ('{0}|{1}|{2}|{3}|{4}' -f $project, $ns, $class, $method, $argumentKey)
}

function Read-LiveLaneManifestObject {
    param([string]$RepoRoot)
    $path = Join-Path $RepoRoot 'tests/live-lanes.json'
    if (-not (Test-Path -LiteralPath $path)) { throw 'manifest-missing' }
    return ([System.IO.File]::ReadAllText($path) | ConvertFrom-Json)
}

function Read-LiveLaneContextObject {
    $raw = [string]$env:ANTIPHON_LIVE_PORTABLE_CONTEXT
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    $text = $raw
    if ($raw.TrimStart().StartsWith('{') -eq $false) {
        if (-not (Test-Path -LiteralPath $raw)) { return $null }
        $text = [System.IO.File]::ReadAllText($raw)
    }
    return ($text | ConvertFrom-Json)
}

function Initialize-LiveLaneNightly {
    param([string]$RepoRoot)
    if ($script:LiveLaneNightlyReady -and (Get-Command Get-NightlyFileSha256 -ErrorAction SilentlyContinue)) { return }
    if (-not (Get-Command Get-NightlyFileSha256 -ErrorAction SilentlyContinue)) { throw 'nightly-readers-not-loaded' }
    $script:LiveLaneNightlyReady = $true
}

function Test-LiveLaneListOnly {
    param([string]$FilePath, [string[]]$ArgumentList, [string]$BoundAssembly)
    $list = @(ConvertTo-LiveLaneList $ArgumentList)
    $hasList = $false
    $hasRun = $false
    $dll = ''
    for ($i = 0; $i -lt $list.Count; $i++) {
        $a = [string]$list[$i]
        if ($a -eq '--list-tests') { $hasList = $true }
        if ($a -eq '--report-trx' -or $a.StartsWith('--report-trx=')) { $hasRun = $true }
        if ($a -eq 'exec' -and ($i + 1) -lt $list.Count) { $dll = [string]$list[$i + 1] }
    }
    if ([string]::IsNullOrWhiteSpace($dll) -and $FilePath -like '*.dll') { $dll = $FilePath }
    if ([string]::IsNullOrWhiteSpace($dll) -or [string]::IsNullOrWhiteSpace($BoundAssembly)) { return $false }
    try {
        $dll = [System.IO.Path]::GetFullPath($dll)
        $bound = [System.IO.Path]::GetFullPath($BoundAssembly)
    } catch { return $false }
    if (-not $hasList -or $hasRun) { return $false }
    return [string]::Equals($dll, $bound, [StringComparison]::OrdinalIgnoreCase)
}

function Invoke-LiveLaneSeamProcess {
    param([string]$FilePath, [string[]]$ArgumentList, [string]$WorkingDirectory, [int]$TimeoutMilliseconds)
    $admitted = Test-LiveLaneListOnly -FilePath $FilePath -ArgumentList $ArgumentList -BoundAssembly $script:LiveLaneBoundAssembly
    if (-not $admitted) {
        return @{ ExitCode = 2; TimedOut = $false; LogText = 'transport-refused'; ChildrenExited = $true }
    }
    $script:LiveLaneLaunchCount = [int]$script:LiveLaneLaunchCount + 1
    $launch = @(ConvertTo-LiveLaneList $ArgumentList)
    if ($script:LiveLaneSuppressDiag) {
        $filtered = @()
        for ($i = 0; $i -lt $launch.Count; $i++) {
            if ([string]$launch[$i] -eq '--diagnostic-output-directory') { $i++; continue }
            $filtered += [string]$launch[$i]
        }
        $launch = $filtered
    }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    foreach ($a in $launch) { [void]$psi.ArgumentList.Add([string]$a) }
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) { $psi.WorkingDirectory = $WorkingDirectory }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $proc = $null
    try {
        $proc = [System.Diagnostics.Process]::Start($psi)
        $outTask = $proc.StandardOutput.ReadToEndAsync()
        $errTask = $proc.StandardError.ReadToEndAsync()
        $budget = $TimeoutMilliseconds
        if ($budget -le 0) { $budget = 120000 }
        if (-not $proc.WaitForExit($budget)) {
            try { $proc.Kill($true) } catch { try { $proc.Kill() } catch { } }
            $script:LiveLaneObservedExit = 124
            return @{ ExitCode = 124; TimedOut = $true; LogText = 'timeout'; ChildrenExited = $false }
        }
        $proc.WaitForExit()
        $log = ''
        try { $log = [string]$outTask.Result + "`n" + [string]$errTask.Result } catch { $log = '' }
        $code = [int]$proc.ExitCode
        if ($null -ne $script:LiveLaneForceExit) { $code = [int]$script:LiveLaneForceExit }
        $script:LiveLaneObservedExit = $code
        return @{ ExitCode = $code; TimedOut = $false; LogText = $log; ChildrenExited = $true }
    } finally {
        if ($proc) { $proc.Dispose() }
    }
}

function Install-LiveLaneSeam {
    param([string]$BoundAssembly)
    $script:LiveLaneBoundAssembly = [System.IO.Path]::GetFullPath($BoundAssembly)
    $script:LiveLaneLaunchCount = 0
    $script:LiveLaneObservedExit = $null
    $script:LiveLaneForceExit = $null
    $script:LiveLaneSuppressDiag = $false
    if (-not $script:NightlySeams) { $script:NightlySeams = @{} }
    $script:NightlySeams['StartProcess'] = {
        param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        return (Invoke-LiveLaneSeamProcess -FilePath ([string]$FilePath) -ArgumentList @(ConvertTo-LiveLaneList $ArgumentList) -WorkingDirectory ([string]$WorkingDirectory) -TimeoutMilliseconds ([int]$TimeoutMilliseconds))
    }
}

function Get-LiveLaneDisplayMap {
    param([string]$DiagPath)
    $map = @{}
    if (-not (Test-Path -LiteralPath $DiagPath)) { return $map }
    $raw = [System.IO.File]::ReadAllText($DiagPath)
    $rx = [regex]'TestNodeUid \{ Value = (?<uid>[^}]+?) \}, DisplayName = (?<display>.*?), Properties = \['
    foreach ($m in $rx.Matches($raw)) {
        $uid = $m.Groups['uid'].Value.Trim()
        $display = $m.Groups['display'].Value.Trim()
        $window = $raw.Substring($m.Index, [Math]::Min(1200, $raw.Length - $m.Index))
        $sig = ''
        $sigMatch = [regex]::Match($window, 'ParameterTypeFullNames = \[(?<sig>[^\]]*)\]')
        if ($sigMatch.Success) { $sig = $sigMatch.Groups['sig'].Value.Trim() }
        $map[$uid] = @{ Display = $display; Signature = $sig }
    }
    return $map
}

function Get-LiveLaneParsedArguments {
    param([string]$Method, [string]$Display)
    if ([string]::IsNullOrWhiteSpace($Display)) { return @() }
    $prefix = $Method + '('
    if (-not $Display.StartsWith($prefix, [StringComparison]::Ordinal)) { return @() }
    if (-not $Display.EndsWith(')', [StringComparison]::Ordinal)) { return @() }
    $inner = $Display.Substring($prefix.Length, $Display.Length - $prefix.Length - 1).Trim()
    if ([string]::IsNullOrWhiteSpace($inner)) { return @() }
    $parts = @()
    $current = New-Object System.Text.StringBuilder
    $quote = $null
    foreach ($ch in $inner.ToCharArray()) {
        if ($null -ne $quote) {
            if ($ch -eq $quote) { $quote = $null } else { [void]$current.Append($ch) }
            continue
        }
        if ($ch -eq '"' -or $ch -eq "'") { $quote = $ch; continue }
        if ($ch -eq ',') { $parts += $current.ToString().Trim(); $current = New-Object System.Text.StringBuilder; continue }
        [void]$current.Append($ch)
    }
    $tail = $current.ToString().Trim()
    if (-not [string]::IsNullOrWhiteSpace($tail) -or $parts.Count -gt 0) { $parts += $tail }
    return @($parts | ForEach-Object { $_.Trim().Trim('"').Trim("'") } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Add-LiveLaneDiscoveryOverlay {
    param($Document, [string]$DiagPath, [string]$Project)
    $map = Get-LiveLaneDisplayMap -DiagPath $DiagPath
    $nodes = @()
    foreach ($n in @(ConvertTo-LiveLaneList (Get-LiveLaneProp $Document 'nodes'))) {
        $uid = [string](Get-LiveLaneProp $n 'uid')
        if ([string]::IsNullOrWhiteSpace($uid)) { $uid = [string](Get-LiveLaneProp $n 'Uid') }
        $method = [string](Get-LiveLaneProp $n 'method')
        if ([string]::IsNullOrWhiteSpace($method)) { $method = [string](Get-LiveLaneProp $n 'Method') }
        $display = $method
        $sig = [string](Get-LiveLaneProp $n 'signature')
        if ($map.ContainsKey($uid)) {
            $display = [string]$map[$uid].Display
            if (-not [string]::IsNullOrWhiteSpace([string]$map[$uid].Signature)) { $sig = [string]$map[$uid].Signature }
        }
        $parsedArguments = @(Get-LiveLaneParsedArguments -Method $method -Display $display)
        $ns = [string](Get-LiveLaneProp $n 'namespace')
        if ([string]::IsNullOrWhiteSpace($ns)) { $ns = [string](Get-LiveLaneProp $n 'Namespace') }
        $type = [string](Get-LiveLaneProp $n 'type')
        if ([string]::IsNullOrWhiteSpace($type)) { $type = [string](Get-LiveLaneProp $n 'Type') }
        $cats = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $n 'categories') | ForEach-Object { [string]$_ })
        if ($cats.Count -eq 0) { $cats = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $n 'Categories') | ForEach-Object { [string]$_ }) }
        $nodes += [pscustomobject]@{
            project = $Project
            uid = $uid
            namespace = $ns
            type = $type
            method = $method
            displayName = $display
            arguments = $parsedArguments
            signature = $sig
            categories = $cats
        }
    }
    return $nodes
}

function Invoke-LiveLaneDiscovery {
    param(
        [string]$RepoRoot,
        [string]$Project,
        [string]$AssemblyPath,
        [string]$CaptureDir
    )
    Initialize-LiveLaneNightly -RepoRoot $RepoRoot
    if ([string]::IsNullOrWhiteSpace($AssemblyPath) -or -not (Test-Path -LiteralPath $AssemblyPath)) {
        return @{ Ok = $false; Error = 'assembly-missing'; LaunchCount = 0; Nodes = @(); AssemblyHash = '' }
    }
    $assembly = [System.IO.Path]::GetFullPath($AssemblyPath)
    New-Item -ItemType Directory -Path $CaptureDir -Force | Out-Null
    $diagDir = Join-Path $CaptureDir ($Project + '.diag')
    if (Test-Path -LiteralPath $diagDir) { Remove-Item -LiteralPath $diagDir -Recurse -Force }
    New-Item -ItemType Directory -Path $diagDir -Force | Out-Null
    $outPath = Join-Path $CaptureDir ($Project + '.discovery-doc.json')
    $logPath = Join-Path $CaptureDir ($Project + '.discovery.log')
    Install-LiveLaneSeam -BoundAssembly $assembly
    $dotnet = 'dotnet'
    $cmd = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd -and -not [string]::IsNullOrWhiteSpace([string]$cmd.Source)) { $dotnet = [string]$cmd.Source }
    $started = [Diagnostics.Stopwatch]::StartNew()
    $errorText = ''
    $doc = $null
    try {
        $doc = Invoke-NightlyProduceDiscovery -ExePath $dotnet -OutputPath $outPath -DiagnosticDirectory $diagDir `
            -LogPath $logPath -WorkingDirectory $RepoRoot -TimeoutMilliseconds 110000 `
            -ArgumentPrefix @('exec', $assembly) -AssemblyPath $assembly
    } catch {
        $errorText = $_.Exception.Message
    }
    $started.Stop()
    $exitCode = $script:LiveLaneObservedExit
    $diag = Get-ChildItem -LiteralPath $diagDir -Filter '*.diag' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    $diagPath = ''
    if ($diag) { $diagPath = $diag.FullName }
    $hash = ''
    try { $hash = Get-NightlyFileSha256 -Path $assembly } catch { $hash = '' }
    $docHash = ''
    if ($doc) { $docHash = [string](Get-LiveLaneProp $doc 'assemblyHash') }
    $nodes = @()
    if ($doc -and $diagPath) { $nodes = @(Add-LiveLaneDiscoveryOverlay -Document $doc -DiagPath $diagPath -Project $Project) }
    $accepted = $true
    $reason = ''
    if (-not [string]::IsNullOrWhiteSpace($errorText)) { $accepted = $false; $reason = 'missing-diag' }
    if ($null -eq $exitCode -or [int]$exitCode -ne 0) { $accepted = $false; if (-not $reason) { $reason = 'nonzero-exit' } }
    if ([string]::IsNullOrWhiteSpace($diagPath)) { $accepted = $false; if (-not $reason) { $reason = 'missing-diag' } }
    if ($nodes.Count -eq 0) { $accepted = $false; if (-not $reason) { $reason = 'empty-discovery' } }
    if (-not [string]::IsNullOrWhiteSpace($docHash) -and -not [string]::Equals($docHash, $hash, [StringComparison]::OrdinalIgnoreCase)) {
        $accepted = $false; $reason = 'wrong-assembly-hash'
    }
    $capture = [ordered]@{
        format = 'antiphon-live-lane-discovery-capture-v1'
        project = $Project
        assemblyPath = $assembly
        assemblyHash = $hash
        observedAssemblyHash = $docHash
        launchCount = [int]$script:LiveLaneLaunchCount
        exitCode = $exitCode
        producerAccepted = $accepted
        reason = $reason
        detail = $errorText
        discoveryMs = [int]$started.ElapsedMilliseconds
        nodes = @($nodes)
    }
    $capturePath = Join-Path $CaptureDir ($Project + '.discovery.json')
    ($capture | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $capturePath -Encoding utf8
    return @{
        Ok = $accepted
        Error = $(if ($accepted) { '' } else { $(if ($errorText) { $errorText } else { $reason }) })
        LaunchCount = [int]$script:LiveLaneLaunchCount
        Nodes = @($nodes)
        AssemblyHash = $hash
        ObservedAssemblyHash = $docHash
        AssemblyPath = $assembly
        DiscoveryMs = [int]$started.ElapsedMilliseconds
        ExitCode = $exitCode
        Reason = $reason
    }
}

function Test-LiveLaneSameArguments {
    param($Left, $Right)
    $a = @(ConvertTo-LiveLaneList $Left | ForEach-Object { [string]$_ })
    $b = @(ConvertTo-LiveLaneList $Right | ForEach-Object { [string]$_ })
    if ($a.Count -ne $b.Count) { return $false }
    for ($i = 0; $i -lt $a.Count; $i++) {
        if (-not [string]::Equals($a[$i], $b[$i], [StringComparison]::Ordinal)) { return $false }
    }
    return $true
}

function Get-LiveLaneMatchingNodes {
    param([object[]]$Nodes, $Identity)
    $hits = @()
    foreach ($n in @(ConvertTo-LiveLaneList $Nodes)) {
        $ns = [string](Get-LiveLaneProp $n 'namespace')
        $type = [string](Get-LiveLaneProp $n 'type')
        if ([string]::IsNullOrWhiteSpace($type)) { $type = [string](Get-LiveLaneProp $n 'class') }
        $method = [string](Get-LiveLaneProp $n 'method')
        if ($ns -ne [string]$Identity.namespace) { continue }
        if ($type -ne [string]$Identity.class) { continue }
        if ($method -ne [string]$Identity.method) { continue }
        $proj = [string](Get-LiveLaneProp $n 'project')
        if (-not [string]::IsNullOrWhiteSpace($proj) -and $proj -ne [string]$Identity.project) { continue }
        if (-not (Test-LiveLaneSameArguments (Get-LiveLaneProp $n 'arguments') $Identity.arguments)) { continue }
        $hits += $n
    }
    return @($hits)
}

function Get-LiveLaneCategorySet {
    param($Values)
    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($c in @(ConvertTo-LiveLaneList $Values)) {
        $text = [string]$c
        if (-not [string]::IsNullOrWhiteSpace($text)) { [void]$set.Add($text) }
    }
    return $set
}

function Test-LiveLaneCategoryEqual {
    param($Left, $Right)
    $a = Get-LiveLaneCategorySet $Left
    $b = Get-LiveLaneCategorySet $Right
    if ($a.Count -ne $b.Count) { return $false }
    foreach ($item in $a) { if (-not $b.Contains($item)) { return $false } }
    return $true
}

function Copy-LiveLaneEntries {
    param($Entries)
    $copies = @()
    foreach ($e in @(ConvertTo-LiveLaneList $Entries)) {
        $copies += [pscustomobject]@{
            project = [string](Get-LiveLaneProp $e 'project')
            namespace = [string](Get-LiveLaneProp $e 'namespace')
            class = [string](Get-LiveLaneProp $e 'class')
            method = [string](Get-LiveLaneProp $e 'method')
            arguments = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $e 'arguments') | ForEach-Object { [string]$_ })
            uid = [string](Get-LiveLaneProp $e 'uid')
            categories = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $e 'categories') | ForEach-Object { [string]$_ })
            bound = [bool](Get-LiveLaneProp $e 'bound')
        }
    }
    return @($copies)
}

function Test-LiveLaneRosterAccepted {
    param([object[]]$Entries, [object[]]$Nodes, [string]$Project, [switch]$FullManifest)
    $contract = @(Get-LiveLaneContract)
    if (-not $FullManifest) { $contract = @($contract | Where-Object { $_.project -eq $Project }) }
    $core = @(ConvertTo-LiveLaneList $Entries)
    if (-not $FullManifest) { $core = @($core | Where-Object { [string](Get-LiveLaneProp $_ 'project') -eq $Project }) }
    $reasons = @()
    $allBound = $true
    foreach ($e in $core) {
        $uid = [string](Get-LiveLaneProp $e 'uid')
        $bound = $false
        $boundProp = Get-LiveLaneProp $e 'bound'
        if ($null -ne $boundProp) { $bound = [bool]$boundProp }
        if (-not $bound -or [string]::IsNullOrWhiteSpace($uid)) { $allBound = $false }
    }
    if (-not $allBound) { $reasons += 'manifest-uid-bound' }
    if ($core.Count -ne $contract.Count) { $reasons += 'manifest-count' }
    $seen = @{}
    $exact = $true
    foreach ($want in $contract) {
        $key = Get-LiveLaneIdentityKey $want
        $have = @($core | Where-Object { (Get-LiveLaneIdentityKey $_) -eq $key })
        if ($have.Count -ne 1) { $exact = $false; continue }
        $entry = $have[0]
        $uid = [string](Get-LiveLaneProp $entry 'uid')
        if ($seen.ContainsKey($uid) -and -not [string]::IsNullOrWhiteSpace($uid)) { $reasons += 'duplicate-uid' }
        if (-not [string]::IsNullOrWhiteSpace($uid)) { $seen[$uid] = $true }
        if ([string]$want.project -ne $Project) { continue }
        $local = @(Get-LiveLaneMatchingNodes -Nodes $Nodes -Identity $want)
        if ($local.Count -ne 1) { $reasons += 'local-identity'; continue }
        if ($allBound) {
            $localUid = [string](Get-LiveLaneProp $local[0] 'uid')
            if ($localUid -ne $uid) { $reasons += 'uid-equals-discovery' }
            if (-not (Test-LiveLaneCategoryEqual (Get-LiveLaneProp $entry 'categories') (Get-LiveLaneProp $local[0] 'categories'))) {
                $reasons += 'category-equals-discovery'
            }
        }
    }
    if (-not $exact) { $reasons += 'exact-set' }
    $expansionExtra = $false
    foreach ($want in $contract) {
        if ([string]$want.project -ne $Project) { continue }
        $sameMethod = @($Nodes | Where-Object {
            [string](Get-LiveLaneProp $_ 'namespace') -eq $want.namespace -and
            [string](Get-LiveLaneProp $_ 'type') -eq $want.class -and
            [string](Get-LiveLaneProp $_ 'method') -eq $want.method
        })
        $observed = @($sameMethod | ForEach-Object { Get-LiveLaneArgumentKey (Get-LiveLaneProp $_ 'arguments') } | Sort-Object -Unique)
        $wanted = @($contract | Where-Object { $_.class -eq $want.class -and $_.method -eq $want.method -and $_.project -eq $Project } | ForEach-Object { Get-LiveLaneArgumentKey $_.arguments } | Sort-Object -Unique)
        if (($observed -join '|') -ne ($wanted -join '|')) { $expansionExtra = $true }
    }
    if ($expansionExtra) { $reasons += 'extra-expansion' }
    $unique = @($reasons | Select-Object -Unique)
    return @{ Accepted = ($unique.Count -eq 0); Reasons = @($unique); Count = $core.Count; ContractCount = $contract.Count }
}

function Get-LiveLaneLocalCoreCount {
    param([object[]]$Nodes, [string]$Project)
    $count = 0
    foreach ($want in @(Get-LiveLaneContract | Where-Object { $_.project -eq $Project })) {
        $count += @(Get-LiveLaneMatchingNodes -Nodes $Nodes -Identity $want).Count
    }
    return $count
}

function Invoke-LiveLaneRosterMatrix {
    param([string]$Project, [object[]]$Nodes, $Manifest, $Context, [string]$AssemblyHash, [string]$AssemblyPath)
    $failures = New-Object System.Collections.Generic.List[string]
    $prefix = $(if ($Project -eq 'Antiphon.Tests') { 'V-1' } else { 'V-2' })
    $full = $Project -eq 'Antiphon.Tests'
    $core = @(Copy-LiveLaneEntries (Get-LiveLaneProp $Manifest 'core'))
    $projectCore = @($core | Where-Object { $_.project -eq $Project })
    $pool = $(if ($full) { @($core) } else { @($projectCore) })
    $eval = Test-LiveLaneRosterAccepted -Entries $pool -Nodes $Nodes -Project $Project -FullManifest:$full
    if (-not $eval.Accepted) {
        foreach ($reason in @($eval.Reasons)) {
            if ($reason -eq 'manifest-uid-bound') { $failures.Add(("C1545 {0}/valid manifest-uid-bound" -f $prefix)) }
            elseif ($reason -eq 'manifest-count' -or $reason -eq 'exact-set') { $failures.Add(("C1545 {0}/valid exact-set" -f $prefix)) }
            elseif ($reason -eq 'uid-equals-discovery') { $failures.Add(("C1545 {0}/valid uid-equals-discovery" -f $prefix)) }
            elseif ($reason -eq 'category-equals-discovery') { $failures.Add(("C1545 {0}/valid category-equals-discovery" -f $prefix)) }
            elseif ($reason -eq 'extra-expansion') { $failures.Add(("C1545 {0}/valid extra-expansion" -f $prefix)) }
            else { $failures.Add(("C1545 {0}/valid {1}" -f $prefix, $reason)) }
        }
    }

    $five = @($pool | Select-Object -SkipLast 1)
    $fiveEval = Test-LiveLaneRosterAccepted -Entries $five -Nodes $Nodes -Project $Project -FullManifest:$full
    $expectedCount = $(if ($full) { 6 } else { 5 })
    if ($fiveEval.Accepted -or $fiveEval.Count -eq $expectedCount) {
        $failures.Add(("C1545 {0}/five manifest.count == {1}" -f $prefix, $expectedCount))
    }

    $dup = @(Copy-LiveLaneEntries $pool)
    if ($dup.Count -gt 0) { $dup += $dup[0] }
    $dupEval = Test-LiveLaneRosterAccepted -Entries $dup -Nodes $Nodes -Project $Project -FullManifest:$full
    if ($dupEval.Accepted) { $failures.Add(("C1545 {0}/duplicate exact-set" -f $prefix)) }

    $renamed = @(Copy-LiveLaneEntries $pool)
    if ($renamed.Count -gt 0) { $renamed[0].method = ($renamed[0].method + '_renamed') }
    $c680 = [pscustomobject]@{ project = 'Antiphon.Tests'; namespace = 'Antiphon.Tests.Agents'; class = 'ClaudeDialogWatchdogCanaryTests'; method = 'C680_LiveNudgeRefusesPermissionAndQuestionDialogs'; arguments = @() }
    $localC680 = 0
    if ($Project -eq 'Antiphon.Tests') { $localC680 = @(Get-LiveLaneMatchingNodes -Nodes $Nodes -Identity $c680).Count }
    else {
        $submit = @(Get-LiveLaneContract | Where-Object { $_.method -eq 'Submitting_via_two_writes_completes_a_turn' })[0]
        $localC680 = @(Get-LiveLaneMatchingNodes -Nodes $Nodes -Identity $submit).Count
    }
    if ($Project -eq 'Antiphon.Tests' -and $localC680 -ne 1) { $failures.Add('C1545 V-1/renamed localC680Matches == 1') }
    if ($Project -ne 'Antiphon.Tests' -and $localC680 -ne 1) { $failures.Add('C1545 V-2/renamed localPtyCoreSet == expectedFive') }
    $renamedEval = Test-LiveLaneRosterAccepted -Entries $renamed -Nodes $Nodes -Project $Project -FullManifest:$full
    if ($renamedEval.Accepted) { $failures.Add(("C1545 {0}/renamed exact-set" -f $prefix)) }

    if ($Project -eq 'Antiphon.Tests') {
        $unexpected = @(Copy-LiveLaneEntries $pool)
        $unexpected += [pscustomobject]@{ project = $Project; namespace = 'Antiphon.Tests.Scripts'; class = 'NotALiveLane'; method = 'Unexpected'; arguments = @(); uid = 'unexpected'; categories = @(); bound = $true }
        $unexpectedEval = Test-LiveLaneRosterAccepted -Entries $unexpected -Nodes $Nodes -Project $Project -FullManifest
        if ($unexpectedEval.Accepted) { $failures.Add('C1545 V-1/unexpected exact-set') }

        $unbound = @(Copy-LiveLaneEntries $pool)
        if ($unbound.Count -gt 0) { $unbound[0].uid = ''; $unbound[0].bound = $false }
        $unboundEval = Test-LiveLaneRosterAccepted -Entries $unbound -Nodes $Nodes -Project $Project -FullManifest
        if ($unboundEval.Reasons -notcontains 'manifest-uid-bound') { $failures.Add('C1545 V-1/unbound-uid manifest-uid-bound') }

        $foreign = @(Copy-LiveLaneEntries $pool)
        if ($foreign.Count -gt 0) { $foreign[0].project = 'Foreign.Tests' }
        $foreignEval = Test-LiveLaneRosterAccepted -Entries $foreign -Nodes $Nodes -Project $Project -FullManifest
        if ($foreignEval.Accepted) { $failures.Add('C1545 V-1/foreign-project exact-set') }
    } else {
        $extraNodes = @($Nodes)
        $submitNode = @($Nodes | Where-Object { [string](Get-LiveLaneProp $_ 'method') -eq 'Submitting_via_two_writes_completes_a_turn' } | Select-Object -First 1)
        if ($submitNode.Count -eq 1) {
            $clone = [pscustomobject]@{
                project = $Project
                uid = ([string](Get-LiveLaneProp $submitNode[0] 'uid') + '-extra')
                namespace = [string](Get-LiveLaneProp $submitNode[0] 'namespace')
                type = [string](Get-LiveLaneProp $submitNode[0] 'type')
                method = [string](Get-LiveLaneProp $submitNode[0] 'method')
                displayName = 'Submitting_via_two_writes_completes_a_turn(other)'
                arguments = @('other')
                signature = [string](Get-LiveLaneProp $submitNode[0] 'signature')
                categories = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $submitNode[0] 'categories'))
            }
            $extraNodes += $clone
        }
        $extraEval = Test-LiveLaneRosterAccepted -Entries $projectCore -Nodes $extraNodes -Project $Project
        if ($extraEval.Reasons -notcontains 'extra-expansion') { $failures.Add('C1545 V-2/extra-argument expandedUidSet has no extra argument') }

        $wrong = @(Copy-LiveLaneEntries $projectCore)
        foreach ($row in $wrong) { $row.project = 'Wrong.Assembly' }
        $wrongEval = Test-LiveLaneRosterAccepted -Entries $wrong -Nodes $Nodes -Project $Project
        $localSet = Get-LiveLaneLocalCoreCount -Nodes $Nodes -Project $Project
        if ($wrongEval.Accepted -or $localSet -ne 5) { $failures.Add('C1545 V-2/wrong-assembly localPtyCoreSet == expectedFive') }

        $hashProbe = Test-LiveLaneDiscoveryAdmission -ExitCode 0 -DiagExists $true -ObservedHash ('0' * 64) -BoundHash $AssemblyHash -NodeCount $Nodes.Count
        if ($hashProbe.Accepted -or $hashProbe.Observed -eq $hashProbe.Bound) { $failures.Add('C1545 V-2/wrong-assembly-hash observedAssemblyHash == boundBuildAssemblyHash') }
        $exitProbe = Test-LiveLaneDiscoveryAdmission -ExitCode 7 -DiagExists $true -ObservedHash $AssemblyHash -BoundHash $AssemblyHash -NodeCount $Nodes.Count
        if ($exitProbe.Accepted) { $failures.Add('C1545 V-2/nonzero-exit producerAccepted == false') }
        $diagProbe = Test-LiveLaneDiscoveryAdmission -ExitCode 0 -DiagExists $false -ObservedHash $AssemblyHash -BoundHash $AssemblyHash -NodeCount 0
        if ($diagProbe.Accepted -or $diagProbe.Reason -ne 'missing-diag') { $failures.Add('C1545 V-2/missing-diag producerAccepted == false') }

        $before = [int]$script:LiveLaneLaunchCount
        $refused = -not (Test-LiveLaneListOnly -FilePath 'dotnet' -ArgumentList @('exec', $AssemblyPath, '--report-trx') -BoundAssembly $AssemblyPath)
        $after = [int]$script:LiveLaneLaunchCount
        if (-not $refused -or $after -ne $before) { $failures.Add('C1545 V-2/run-not-list launchCount == 0') }

        $partial = Test-LiveLaneExportDocument -Export ([pscustomobject]@{
            format = 'antiphon-live-lane-export-v1'; project = $Project; cp = 'CP-25'; sourceSha = 'abc'; runId = 'run'; assemblyHash = $AssemblyHash; terminal = $true; nodes = @()
        }) -ExpectedSource 'abc' -ExpectedRun 'run' -ExpectedHash $AssemblyHash -ExpectedProject $Project -RequireCore $true
        if ($partial.Accepted -or $partial.Reason -ne 'producer-incomplete') { $failures.Add('C1545 V-2/partial-export producerAccepted == false') }
    }

    $exportRequired = $false
    $mode = ''
    if ($Context) {
        $mode = [string](Get-LiveLaneProp $Context 'mode')
        $flag = Get-LiveLaneProp $Context 'exportRequired'
        if ($null -ne $flag) { $exportRequired = [bool]$flag }
    }
    if ($Project -eq 'Antiphon.Tests') {
        $source = 'local-source'
        $run = 'local-run'
        $hash = $(if ($AssemblyHash) { $AssemblyHash } else { 'local-hash' })
        if ($Context -and $exportRequired) {
            $source = [string](Get-LiveLaneProp $Context 'sourceSha')
            $run = [string](Get-LiveLaneProp $Context 'runId')
        }
        $healthyNodes = @($Nodes | Select-Object -First 1)
        $healthy = Test-LiveLaneExportDocument -Export ([pscustomobject]@{
            format = 'antiphon-live-lane-export-v1'; project = 'Antiphon.Agents.Pty.Tests'; cp = 'CP-25'; sourceSha = $source; runId = $run; assemblyHash = $hash; terminal = $true
            nodes = @($healthyNodes)
        }) -ExpectedSource $source -ExpectedRun $run -ExpectedHash $hash -ExpectedProject 'Antiphon.Agents.Pty.Tests' -RequireCore $false
        if (-not $healthy.Accepted) { $failures.Add('C1545 V-1/valid exportAccepted') }
        foreach ($pair in @(
            @{ id = 'missing'; export = $null; reason = 'producer-incomplete' },
            @{ id = 'partial'; export = [pscustomobject]@{ format = 'antiphon-live-lane-export-v1'; project = 'Antiphon.Agents.Pty.Tests'; cp = 'CP-25'; sourceSha = $source; runId = $run; assemblyHash = $hash; terminal = $true; nodes = @() }; reason = 'producer-incomplete' },
            @{ id = 'pending'; export = [pscustomobject]@{ format = 'antiphon-live-lane-export-v1'; project = 'Antiphon.Agents.Pty.Tests'; cp = 'CP-25'; sourceSha = $source; runId = $run; assemblyHash = $hash; terminal = $false; nodes = @($healthyNodes) }; reason = 'producer-incomplete' },
            @{ id = 'wrong-source'; export = [pscustomobject]@{ format = 'antiphon-live-lane-export-v1'; project = 'Antiphon.Agents.Pty.Tests'; cp = 'CP-25'; sourceSha = 'other-source'; runId = $run; assemblyHash = $hash; terminal = $true; nodes = @($healthyNodes) }; reason = 'source-mismatch' },
            @{ id = 'wrong-build'; export = [pscustomobject]@{ format = 'antiphon-live-lane-export-v1'; project = 'Antiphon.Agents.Pty.Tests'; cp = 'CP-25'; sourceSha = $source; runId = $run; assemblyHash = ('f' * 64); terminal = $true; nodes = @($healthyNodes) }; reason = 'build-mismatch' },
            @{ id = 'wrong-run'; export = [pscustomobject]@{ format = 'antiphon-live-lane-export-v1'; project = 'Antiphon.Agents.Pty.Tests'; cp = 'CP-25'; sourceSha = $source; runId = 'other-run'; assemblyHash = $hash; terminal = $true; nodes = @($healthyNodes) }; reason = 'run-mismatch' }
        )) {
            $probe = Test-LiveLaneExportDocument -Export $pair.export -ExpectedSource $source -ExpectedRun $run -ExpectedHash $hash -ExpectedProject 'Antiphon.Agents.Pty.Tests' -RequireCore $false
            if ($probe.Accepted -or $probe.Reason -ne $pair.reason) {
                $failures.Add(("C1545 V-1/{0} exportAccepted == false; reason={1}" -f $pair.id, $pair.reason))
            }
        }
        if (-not $exportRequired) {
            # Baseline and ordinary mode do not consume an absent CP-25 file.
        } else {
            $path = [string](Get-LiveLaneProp $Context 'cp25Export')
            $loaded = Read-LiveLaneExportFile -Path $path
            $real = Test-LiveLaneExportDocument -Export $loaded.Export -ExpectedSource $source -ExpectedRun $run -ExpectedHash ([string](Get-LiveLaneProp $loaded.Export 'assemblyHash')) -ExpectedProject 'Antiphon.Agents.Pty.Tests' -RequireCore $true -AssemblyPath ([string](Get-LiveLaneProp $loaded.Export 'assemblyPath'))
            if (-not $loaded.Found -or -not $real.Accepted) { $failures.Add('C1545 V-1/valid exportAccepted') }
        }
    }

    return @($failures)
}

function Test-LiveLaneDiscoveryAdmission {
    param([int]$ExitCode, [bool]$DiagExists, [string]$ObservedHash, [string]$BoundHash, [int]$NodeCount)
    $reason = ''
    $accepted = $true
    if ($ExitCode -ne 0) { $accepted = $false; $reason = 'nonzero-exit' }
    if (-not $DiagExists) { $accepted = $false; if (-not $reason) { $reason = 'missing-diag' } }
    if ($NodeCount -le 0) { $accepted = $false; if (-not $reason) { $reason = 'missing-diag' } }
    if (-not [string]::Equals([string]$ObservedHash, [string]$BoundHash, [StringComparison]::OrdinalIgnoreCase)) {
        $accepted = $false; $reason = 'wrong-assembly-hash'
    }
    return @{ Accepted = $accepted; Reason = $reason; Observed = [string]$ObservedHash; Bound = [string]$BoundHash }
}

function Test-LiveLaneExportDocument {
    param($Export, [string]$ExpectedSource, [string]$ExpectedRun, [string]$ExpectedHash, [string]$ExpectedProject, [bool]$RequireCore, [string]$AssemblyPath = '')
    if ($null -eq $Export) { return @{ Accepted = $false; Reason = 'producer-incomplete' } }
    $terminal = Get-LiveLaneProp $Export 'terminal'
    $nodes = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $Export 'nodes'))
    if ($null -eq $terminal -or -not [bool]$terminal) { return @{ Accepted = $false; Reason = 'producer-incomplete' } }
    if ($nodes.Count -eq 0) { return @{ Accepted = $false; Reason = 'producer-incomplete' } }
    $project = [string](Get-LiveLaneProp $Export 'project')
    if ($project -ne $ExpectedProject) { return @{ Accepted = $false; Reason = 'source-mismatch' } }
    $source = [string](Get-LiveLaneProp $Export 'sourceSha')
    if ($source -ne $ExpectedSource) { return @{ Accepted = $false; Reason = 'source-mismatch' } }
    $run = [string](Get-LiveLaneProp $Export 'runId')
    if ($run -ne $ExpectedRun) { return @{ Accepted = $false; Reason = 'run-mismatch' } }
    $hash = [string](Get-LiveLaneProp $Export 'assemblyHash')
    if (-not [string]::Equals($hash, $ExpectedHash, [StringComparison]::OrdinalIgnoreCase)) { return @{ Accepted = $false; Reason = 'build-mismatch' } }
    if (-not [string]::IsNullOrWhiteSpace($AssemblyPath)) {
        if (-not (Test-Path -LiteralPath $AssemblyPath)) { return @{ Accepted = $false; Reason = 'build-mismatch' } }
        Initialize-LiveLaneNightly -RepoRoot (Get-LiveLaneRepoRoot $AssemblyPath)
        $fileHash = Get-NightlyFileSha256 -Path $AssemblyPath
        if (-not [string]::Equals($fileHash, $hash, [StringComparison]::OrdinalIgnoreCase)) { return @{ Accepted = $false; Reason = 'build-mismatch' } }
    }
    if ($RequireCore) {
        $pty = @(Get-LiveLaneContract | Where-Object { $_.project -eq 'Antiphon.Agents.Pty.Tests' })
        foreach ($want in $pty) {
            if (@(Get-LiveLaneMatchingNodes -Nodes $nodes -Identity $want).Count -ne 1) { return @{ Accepted = $false; Reason = 'producer-incomplete' } }
        }
    }
    return @{ Accepted = $true; Reason = '' }
}

function Read-LiveLaneExportFile {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) { return @{ Found = $false; Export = $null } }
    return @{ Found = $true; Export = ([System.IO.File]::ReadAllText($Path) | ConvertFrom-Json) }
}

function Write-LiveLaneExportFile {
    param([string]$Path, [string]$Project, [string]$Cp, [string]$SourceSha, [string]$RunId, [string]$AssemblyHash, [string]$AssemblyPath, [object[]]$Nodes)
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $doc = [ordered]@{
        format = 'antiphon-live-lane-export-v1'
        project = $Project
        cp = $Cp
        sourceSha = $SourceSha
        runId = $RunId
        assemblyHash = $AssemblyHash
        assemblyPath = $AssemblyPath
        terminal = $true
        nodes = @($Nodes)
    }
    ($doc | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $Path -Encoding utf8
}

function Remove-LiveLaneComments {
    param([string]$Text)
    $sb = New-Object System.Text.StringBuilder
    $i = 0
    $mode = 'code'
    while ($i -lt $Text.Length) {
        $ch = $Text[$i]
        $next = ''
        if (($i + 1) -lt $Text.Length) { $next = $Text.Substring($i, 2) }
        if ($mode -eq 'code') {
            if ($next -eq '//') { $mode = 'line'; $i += 2; continue }
            if ($next -eq '/*') { $mode = 'block'; $i += 2; continue }
            if ($ch -eq '"') { $mode = 'string'; [void]$sb.Append($ch); $i++; continue }
            if ($ch -eq "'") { $mode = 'char'; [void]$sb.Append($ch); $i++; continue }
            [void]$sb.Append($ch); $i++; continue
        }
        if ($mode -eq 'line') {
            if ($ch -eq "`n") { $mode = 'code'; [void]$sb.Append($ch) }
            $i++; continue
        }
        if ($mode -eq 'block') {
            if ($next -eq '*/') { $mode = 'code'; $i += 2; [void]$sb.Append(' '); continue }
            if ($ch -eq "`n") { [void]$sb.Append($ch) }
            $i++; continue
        }
        if ($mode -eq 'string' -or $mode -eq 'char') {
            $end = $(if ($mode -eq 'string') { '"' } else { "'" })
            [void]$sb.Append($ch)
            if ($ch -eq '\') {
                if (($i + 1) -lt $Text.Length) { [void]$sb.Append($Text[$i + 1]); $i += 2; continue }
            }
            if ($ch -eq $end) { $mode = 'code' }
            $i++; continue
        }
    }
    return $sb.ToString()
}

function Get-LiveLaneBraceSpan {
    param([string]$Text, [int]$OpenAt)
    $depth = 0
    $i = $OpenAt
    while ($i -lt $Text.Length) {
        $ch = $Text[$i]
        if ($ch -eq '{') { $depth++ }
        elseif ($ch -eq '}') {
            $depth--
            if ($depth -eq 0) { return $Text.Substring($OpenAt, $i - $OpenAt + 1) }
        }
        $i++
    }
    return $null
}

function Get-LiveLaneQuotedArgs {
    param([string]$Inner)
    if ([string]::IsNullOrWhiteSpace($Inner)) { return @() }
    $matches = [regex]::Matches($Inner, '"([^"]*)"')
    if ($matches.Count -eq 0) { return @($Inner.Trim()) }
    return @($matches | ForEach-Object { $_.Groups[1].Value })
}

function Invoke-LiveLaneGateScan {
    param([string]$RepoRoot, [string[]]$ExtraFiles = @())
    $roots = @(
        (Join-Path $RepoRoot 'tests/Antiphon.Tests'),
        (Join-Path $RepoRoot 'tests/Antiphon.Agents.Pty.Tests')
    )
    $files = @()
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $files += @(Get-ChildItem -LiteralPath $root -Filter '*.cs' -Recurse -File | Where-Object {
            $parts = $_.FullName.Substring($root.Length).Split([char[]]@('\', '/'))
            $skip = $false
            foreach ($part in $parts) {
                if ($part -eq 'bin' -or $part -eq 'obj' -or $part -like 'bin-*') { $skip = $true }
            }
            -not $skip
        })
    }
    foreach ($extra in @(ConvertTo-LiveLaneList $ExtraFiles)) {
        if ($extra -and (Test-Path -LiteralPath $extra)) { $files += Get-Item -LiteralPath $extra }
    }
    $gates = @()
    $unresolved = 0
    foreach ($file in $files) {
        $path = [string]$file.FullName
        $project = ''
        if ($path -match 'tests[/\\]Antiphon\.Tests[/\\]' -or $path -match 'Antiphon\.Tests') {
            if ($path -match 'Antiphon\.Agents\.Pty\.Tests') { $project = 'Antiphon.Agents.Pty.Tests' } else { $project = 'Antiphon.Tests' }
        }
        if ($path -match 'Antiphon\.Agents\.Pty\.Tests') { $project = 'Antiphon.Agents.Pty.Tests' }
        $text = Remove-LiveLaneComments ([System.IO.File]::ReadAllText($path))
        $ns = ''
        $nsMatch = [regex]::Match($text, 'namespace\s+([A-Za-z0-9_\.]+)\s*[;{]')
        if ($nsMatch.Success) { $ns = $nsMatch.Groups[1].Value }
        $classRx = [regex]'(?<attrs>(?:\[[^\]]*\]\s*)*)(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+|file\s+)*class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)'
        $classMatches = @($classRx.Matches($text))
        for ($c = 0; $c -lt $classMatches.Count; $c++) {
            $cm = $classMatches[$c]
            $className = $cm.Groups['name'].Value
            $classAttrs = $cm.Groups['attrs'].Value
            $classExplicit = $classAttrs -match '\[Explicit(\(|\])'
            $braceAt = $text.IndexOf('{', $cm.Index + $cm.Length)
            if ($braceAt -lt 0) { if ($classExplicit) { $unresolved++ }; continue }
            $span = Get-LiveLaneBraceSpan -Text $text -OpenAt $braceAt
            if ($null -eq $span) { $unresolved++; continue }
            $methodRx = [regex]'(?<attrs>(?:\[[^\]]*\]\s*)+)(?:public|internal)\s+(?:sealed\s+|static\s+|async\s+|override\s+|virtual\s+|new\s+|partial\s+|[\w\.]+\s+)*?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\('
            $methods = @($methodRx.Matches($span))
            $tests = @($methods | Where-Object { $_.Groups['attrs'].Value -match '\[Test(\(|\])' })
            if ($classExplicit -and $tests.Count -eq 0) { $unresolved++ }
            foreach ($mm in $tests) {
                $attrs = $mm.Groups['attrs'].Value
                $method = $mm.Groups['name'].Value
                if ($method -eq 'SkipIfNotEligible') { continue }
                $open = $span.IndexOf('{', $mm.Index + $mm.Length)
                $body = ''
                if ($open -ge 0) {
                    $bodySpan = Get-LiveLaneBraceSpan -Text $span -OpenAt $open
                    if ($null -eq $bodySpan) { $unresolved++; continue }
                    $body = $bodySpan
                }
                $shapes = @()
                if ($classExplicit -or $attrs -match '\[Explicit(\(|\])') { $shapes += 'Explicit' }
                if ($body -match 'ANTIPHON_HEADED_TESTS') { $shapes += 'ANTIPHON_HEADED_TESTS' }
                if ($body -match 'SkipIfNotEligible' -and $method -ne 'SkipIfNotEligible') { $shapes += 'SkipIfNotEligible' }
                if ($body -match 'RealCliStub') { $shapes += 'RealCliStub' }
                $unknown = $attrs -match '\[GateShape\('
                if ($unknown) { $unresolved++ ; continue }
                if ($shapes.Count -eq 0) { continue }
                $argGroups = @()
                $argMatches = [regex]::Matches($attrs, '\[Arguments\(([^\]]*)\)\]')
                if ($argMatches.Count -eq 0) { $argGroups = @(@()) }
                else {
                    foreach ($am in $argMatches) { $argGroups += ,@(Get-LiveLaneQuotedArgs $am.Groups[1].Value) }
                }
                foreach ($argumentGroup in $argGroups) {
                    $gates += [pscustomobject]@{
                        project = $project
                        namespace = $ns
                        class = $className
                        method = $method
                        arguments = @($argumentGroup)
                        shapes = @($shapes)
                    }
                }
            }
        }
    }
    return @{ Gates = @($gates); Unresolved = $unresolved }
}

function Get-LiveLaneRcExclusionIndex {
    param([string]$RepoRoot)
    Initialize-LiveLaneNightly -RepoRoot $RepoRoot
    $policy = Read-NightlyExecutionPolicy -RepoRoot $RepoRoot
    $platform = 'linux'
    if (-not $IsLinux) { $platform = 'windows' }
    $ids = @()
    foreach ($id in @(ConvertTo-LiveLaneList $policy.Object.defaultSuites)) { $ids += [string]$id }
    foreach ($id in @(ConvertTo-LiveLaneList $policy.Object.profiles.rc.requiredSuites)) { $ids += [string]$id }
    $ids = @($ids | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    $rows = @()
    foreach ($id in $ids) {
        $rows += @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile 'rc' -SuiteId $id -Platform $platform)
    }
    return @($rows)
}

function Get-LiveLaneGateDisposition {
    param($Gate, $Exclusions)
    $contract = @(Get-LiveLaneContract)
    $key = Get-LiveLaneIdentityKey $Gate
    foreach ($want in $contract) {
        if ((Get-LiveLaneIdentityKey $want) -eq $key) {
            return @{ Disposition = 'core'; Owner = 'CARD-1545'; Reason = 'live-claude core roster' }
        }
    }
    foreach ($row in @(ConvertTo-LiveLaneList $Exclusions)) {
        if ([string]$row.Class -eq [string](Get-LiveLaneProp $Gate 'class')) {
            return @{ Disposition = 'policy-excluded'; Owner = [string]$row.Owner; Reason = [string]$row.Reason }
        }
    }
    return @{ Disposition = 'diagnostic'; Owner = 'CARD-1545'; Reason = 'live-claude diagnostic gate' }
}

function Invoke-LiveLaneGateMatrix {
    param([string]$RepoRoot, $Manifest)
    $failures = New-Object System.Collections.Generic.List[string]
    $scan = Invoke-LiveLaneGateScan -RepoRoot $RepoRoot
    $catalog = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $Manifest 'gates'))
    $exclusions = @(Get-LiveLaneRcExclusionIndex -RepoRoot $RepoRoot)
    $index = @{}
    $ambiguous = 0
    $invalid = 0
    foreach ($row in $catalog) {
        $key = Get-LiveLaneIdentityKey $row
        if ($index.ContainsKey($key)) { $ambiguous++ }
        $index[$key] = $row
        $owner = [string](Get-LiveLaneProp $row 'owner')
        $reason = [string](Get-LiveLaneProp $row 'reason')
        $disp = [string](Get-LiveLaneProp $row 'disposition')
        if ([string]::IsNullOrWhiteSpace($owner) -or [string]::IsNullOrWhiteSpace($reason) -or [string]::IsNullOrWhiteSpace($disp)) { $invalid++ }
    }
    $unmapped = 0
    foreach ($gate in @($scan.Gates)) {
        $key = Get-LiveLaneIdentityKey $gate
        if (-not $index.ContainsKey($key)) { $unmapped++ }
    }
    if ($unmapped -ne 0) { $failures.Add('C1545 V-3/valid unmappedGateCount == 0') }
    if ($scan.Unresolved -ne 0) { $failures.Add('C1545 V-3/valid unresolvedGateCount == 0') }
    if ($ambiguous -ne 0) { $failures.Add('C1545 V-3/valid ambiguousDispositionCount == 0') }
    if ($invalid -ne 0) { $failures.Add('C1545 V-3/valid invalidDispositionCount == 0') }
    foreach ($gate in @($scan.Gates)) {
        $key = Get-LiveLaneIdentityKey $gate
        if (-not $index.ContainsKey($key)) { continue }
        $expected = Get-LiveLaneGateDisposition -Gate $gate -Exclusions $exclusions
        $row = $index[$key]
        if ([string](Get-LiveLaneProp $row 'disposition') -ne $expected.Disposition) { $failures.Add('C1545 V-3/valid disposition'); break }
        if ([string](Get-LiveLaneProp $row 'owner') -ne $expected.Owner) { $failures.Add('C1545 V-3/valid owner'); break }
        if ([string](Get-LiveLaneProp $row 'reason') -ne $expected.Reason) { $failures.Add('C1545 V-3/valid reason'); break }
    }

    $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('antiphon-live-lane-gate-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    try {
        $newFile = Join-Path $scratch 'NewGate.cs'
        [System.IO.File]::WriteAllText($newFile, "namespace Scratch;`n[Explicit]`npublic class NewGate {`n  [Test]`n  public void Injected() { }`n}`n")
        $newScan = Invoke-LiveLaneGateScan -RepoRoot $RepoRoot -ExtraFiles @($newFile)
        $newUnmapped = 0
        foreach ($gate in @($newScan.Gates)) {
            if (-not $index.ContainsKey((Get-LiveLaneIdentityKey $gate))) { $newUnmapped++ }
        }
        if ($newUnmapped -eq 0) { $failures.Add('C1545 V-3/new-gate unmappedGateCount == 0') }

        $ordinary = Join-Path $scratch 'Ordinary.cs'
        [System.IO.File]::WriteAllText($ordinary, "namespace Scratch;`npublic class OrdinaryControl {`n  [Test]`n  public void Plain() { int x = 1; }`n}`n")
        $ordinaryScan = Invoke-LiveLaneGateScan -RepoRoot $RepoRoot -ExtraFiles @($ordinary)
        $ordinaryUnmapped = 0
        foreach ($gate in @($ordinaryScan.Gates)) {
            if (-not $index.ContainsKey((Get-LiveLaneIdentityKey $gate))) { $ordinaryUnmapped++ }
        }
        if ($ordinaryUnmapped -ne $unmapped -or $ordinaryScan.Unresolved -ne $scan.Unresolved) { $failures.Add('C1545 V-3/ordinary-control inventoryAccepted') }

        if ($catalog.Count -gt 0) {
            $missingOwner = @(Copy-LiveLaneGateRows $catalog)
            $missingOwner[0].owner = ''
            if ((Measure-LiveLaneGateInvalid $missingOwner) -ne 1) { $failures.Add('C1545 V-3/missing-owner invalidDispositionCount == 1') }
            $missingReason = @(Copy-LiveLaneGateRows $catalog)
            $missingReason[0].reason = ''
            if ((Measure-LiveLaneGateInvalid $missingReason) -ne 1) { $failures.Add('C1545 V-3/missing-reason invalidDispositionCount == 1') }
            $dupRows = @(Copy-LiveLaneGateRows $catalog)
            $dupRows += $dupRows[0]
            if ((Measure-LiveLaneGateAmbiguous $dupRows) -lt 1) { $failures.Add('C1545 V-3/duplicate-disposition ambiguousDispositionCount == 1') }
        } else {
            $failures.Add('C1545 V-3/missing-owner invalidDispositionCount == 1')
            $failures.Add('C1545 V-3/missing-reason invalidDispositionCount == 1')
            $failures.Add('C1545 V-3/duplicate-disposition ambiguousDispositionCount == 1')
        }

        $unknown = Join-Path $scratch 'Unknown.cs'
        [System.IO.File]::WriteAllText($unknown, "namespace Scratch;`npublic class Mystery {`n  [Test]`n  [GateShape(`"unresolved`")]`n  public void Odd() { }`n}`n")
        $unknownScan = Invoke-LiveLaneGateScan -RepoRoot $RepoRoot -ExtraFiles @($unknown)
        $accepted = ($unknownScan.Unresolved -eq 0)
        if ($unknownScan.Unresolved -ne ($scan.Unresolved + 1) -or $accepted) { $failures.Add('C1545 V-3/unknown-shape unresolvedGateCount == 1') }
    } finally {
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    }
    return @($failures)
}

function Copy-LiveLaneGateRows {
    param($Rows)
    $copies = @()
    foreach ($row in @(ConvertTo-LiveLaneList $Rows)) {
        $copies += [pscustomobject]@{
            project = [string](Get-LiveLaneProp $row 'project')
            namespace = [string](Get-LiveLaneProp $row 'namespace')
            class = [string](Get-LiveLaneProp $row 'class')
            method = [string](Get-LiveLaneProp $row 'method')
            arguments = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $row 'arguments'))
            disposition = [string](Get-LiveLaneProp $row 'disposition')
            owner = [string](Get-LiveLaneProp $row 'owner')
            reason = [string](Get-LiveLaneProp $row 'reason')
        }
    }
    return @($copies)
}

function Measure-LiveLaneGateInvalid {
    param($Rows)
    $count = 0
    foreach ($row in @(ConvertTo-LiveLaneList $Rows)) {
        if ([string]::IsNullOrWhiteSpace([string](Get-LiveLaneProp $row 'owner')) -or [string]::IsNullOrWhiteSpace([string](Get-LiveLaneProp $row 'reason')) -or [string]::IsNullOrWhiteSpace([string](Get-LiveLaneProp $row 'disposition'))) { $count++ }
    }
    return $count
}

function Measure-LiveLaneGateAmbiguous {
    param($Rows)
    $seen = @{}
    $count = 0
    foreach ($row in @(ConvertTo-LiveLaneList $Rows)) {
        $key = Get-LiveLaneIdentityKey $row
        if ($seen.ContainsKey($key)) { $count++ }
        $seen[$key] = $true
    }
    return $count
}

function Write-LiveLaneGateCatalog {
    param([string]$RepoRoot)
    $scan = Invoke-LiveLaneGateScan -RepoRoot $RepoRoot
    $exclusions = @(Get-LiveLaneRcExclusionIndex -RepoRoot $RepoRoot)
    $rows = @()
    foreach ($gate in @($scan.Gates | Sort-Object project, namespace, class, method, @{ Expression = { (Get-LiveLaneArgumentKey (Get-LiveLaneProp $_ 'arguments')) } })) {
        $disp = Get-LiveLaneGateDisposition -Gate $gate -Exclusions $exclusions
        $rows += [ordered]@{
            project = [string]$gate.project
            namespace = [string]$gate.namespace
            class = [string]$gate.class
            method = [string]$gate.method
            arguments = @($gate.arguments)
            shapes = @($gate.shapes)
            disposition = $disp.Disposition
            owner = $disp.Owner
            reason = $disp.Reason
        }
    }
    $manifest = Read-LiveLaneManifestObject -RepoRoot $RepoRoot
    $core = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $manifest 'core'))
    $additive = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $manifest 'additiveContract'))
    $doc = [ordered]@{ format = 'antiphon-live-lanes-v1'; core = $core; gates = @($rows); additiveContract = $additive }
    $path = Join-Path $RepoRoot 'tests/live-lanes.json'
    ($doc | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $path -Encoding utf8
    return @{ Count = $rows.Count; Unresolved = $scan.Unresolved; Path = $path }
}

function Update-LiveLaneManifestBindings {
    param([string]$RepoRoot, [string]$CaptureDir)
    $manifest = Read-LiveLaneManifestObject -RepoRoot $RepoRoot
    $byProject = @{}
    foreach ($project in @('Antiphon.Tests', 'Antiphon.Agents.Pty.Tests')) {
        $path = Join-Path $CaptureDir ($project + '.discovery.json')
        if (-not (Test-Path -LiteralPath $path)) { throw ('capture-missing ' + $project) }
        $doc = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        $byProject[$project] = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $doc 'nodes'))
    }
    $core = @()
    foreach ($entry in @(ConvertTo-LiveLaneList (Get-LiveLaneProp $manifest 'core'))) {
        $project = [string](Get-LiveLaneProp $entry 'project')
        $identity = [pscustomobject]@{
            project = $project
            namespace = [string](Get-LiveLaneProp $entry 'namespace')
            class = [string](Get-LiveLaneProp $entry 'class')
            method = [string](Get-LiveLaneProp $entry 'method')
            arguments = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $entry 'arguments') | ForEach-Object { [string]$_ })
        }
        $hits = @(Get-LiveLaneMatchingNodes -Nodes $byProject[$project] -Identity $identity)
        if ($hits.Count -ne 1) { throw ('bind-miss ' + $identity.method + ' count=' + $hits.Count) }
        $core += [ordered]@{
            project = $identity.project
            namespace = $identity.namespace
            class = $identity.class
            method = $identity.method
            arguments = @($identity.arguments)
            uid = [string](Get-LiveLaneProp $hits[0] 'uid')
            categories = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $hits[0] 'categories') | ForEach-Object { [string]$_ })
            bound = $true
        }
    }
    $doc = [ordered]@{
        format = 'antiphon-live-lanes-v1'
        core = @($core)
        gates = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $manifest 'gates'))
        additiveContract = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $manifest 'additiveContract'))
    }
    ($doc | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath (Join-Path $RepoRoot 'tests/live-lanes.json') -Encoding utf8
}

function Write-LiveLaneDiscoveryFixture {
    param([string]$RepoRoot, [string]$CaptureDir, [string]$SourceSha)
    $projects = @()
    $nodes = @()
    foreach ($pair in @(
        @{ project = 'Antiphon.Tests'; cp = 'CP-27' },
        @{ project = 'Antiphon.Agents.Pty.Tests'; cp = 'CP-28' }
    )) {
        $path = Join-Path $CaptureDir ($pair.project + '.discovery.json')
        $doc = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        $projects += [ordered]@{ project = $pair.project; cp = $pair.cp; assemblyHash = [string](Get-LiveLaneProp $doc 'assemblyHash') }
        foreach ($n in @(ConvertTo-LiveLaneList (Get-LiveLaneProp $doc 'nodes'))) { $nodes += $n }
    }
    $fixture = [ordered]@{
        format = 'antiphon-live-lane-discovery-fixture-v1'
        producing = [ordered]@{ sourceSha = $SourceSha; projects = @($projects) }
        nodes = @($nodes)
    }
    $dest = Join-Path $RepoRoot $script:LiveLaneFixtureRel
    $parent = Split-Path -Parent $dest
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    ($fixture | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $dest -Encoding utf8
    return $dest
}

function Get-LiveLaneGit {
    param([string]$RepoRoot, [string[]]$GitArgs)
    $out = & git -C $RepoRoot @GitArgs 2>$null
    return @{ Exit = $LASTEXITCODE; Text = (($out | Out-String).Trim()) }
}

function Copy-LiveLaneGitBlob {
    param([string]$RepoRoot, [string]$Spec, [string]$Dest)
    $parent = Split-Path -Parent $Dest
    if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    [void]$psi.ArgumentList.Add('-C')
    [void]$psi.ArgumentList.Add($RepoRoot)
    [void]$psi.ArgumentList.Add('cat-file')
    [void]$psi.ArgumentList.Add('blob')
    [void]$psi.ArgumentList.Add($Spec)
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $proc = [System.Diagnostics.Process]::Start($psi)
    $fs = [System.IO.File]::Create($Dest)
    try { $proc.StandardOutput.BaseStream.CopyTo($fs) } finally { $fs.Dispose() }
    $err = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    $code = $proc.ExitCode
    $proc.Dispose()
    if ($code -ne 0) { throw ('seed-object-missing ' + $Spec) }
}

function Get-LiveLanePolicyHashProcess {
    param([string]$RepoRoot, [string]$PolicyRoot, [string]$PolicyPath)
    $script = Join-Path $RepoRoot 'scripts/test-live-lane-manifest.ps1'
    $pwshArgs = @('-NoProfile', '-File', $script, '-Case', 'EmitPolicyHash', '-PolicyRoot', $PolicyRoot)
    if (-not [string]::IsNullOrWhiteSpace($PolicyPath)) { $pwshArgs += @('-PolicyPath', $PolicyPath) }
    $out = & pwsh @pwshArgs
    if ($LASTEXITCODE -ne 0) { throw 'policy-hash-process' }
    return ([string]@($out)[-1]).Trim()
}

function Get-LiveLaneNodeUid {
    param($Node)
    return [string](Get-LiveLaneProp $Node 'uid')
}

function Test-LiveLaneContractIdentity {
    param($Node)
    $identity = [pscustomobject]@{
        project = [string](Get-LiveLaneProp $Node 'project')
        namespace = [string](Get-LiveLaneProp $Node 'namespace')
        class = [string](Get-LiveLaneProp $Node 'type')
        method = [string](Get-LiveLaneProp $Node 'method')
        arguments = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $Node 'arguments'))
    }
    if ([string]::IsNullOrWhiteSpace($identity.class)) { $identity.class = [string](Get-LiveLaneProp $Node 'class') }
    foreach ($want in @(Get-LiveLaneContract)) {
        if ((Get-LiveLaneIdentityKey $want) -eq (Get-LiveLaneIdentityKey $identity)) { return $true }
    }
    return $false
}

function Get-LiveLaneCategoryDelta {
    param($Before, $After)
    $b = Get-LiveLaneCategorySet $Before
    $a = Get-LiveLaneCategorySet $After
    $added = @()
    $removed = @()
    foreach ($item in $a) { if (-not $b.Contains($item)) { $added += $item } }
    foreach ($item in $b) { if (-not $a.Contains($item)) { $removed += $item } }
    return @{ Added = @($added); Removed = @($removed) }
}

function Invoke-LiveLaneCensus {
    param($Context, [hashtable]$Mutant = $null)
    $diagnostics = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Context) { return @{ Accepted = $false; Diagnostics = @('context-required') } }
    $repo = [string](Get-LiveLaneProp $Context 'candidateRoot')
    if ([string]::IsNullOrWhiteSpace($repo)) { $repo = Get-LiveLaneRepoRoot (Get-Location).Path }
    $seed = [string](Get-LiveLaneProp $Context 'censusBaseSha')
    if ($Mutant -and $Mutant.ContainsKey('seed')) { $seed = [string]$Mutant.seed }
    if ([string]::IsNullOrWhiteSpace($seed)) { $diagnostics.Add('seed-object-missing') ; return @{ Accepted = $false; Diagnostics = @($diagnostics) } }
    $head = (Get-LiveLaneGit -RepoRoot $repo -GitArgs @('rev-parse', 'HEAD')).Text
    $ancestor = Get-LiveLaneGit -RepoRoot $repo -GitArgs @('merge-base', '--is-ancestor', $seed, $head)
    if ($ancestor.Exit -ne 0) { $diagnostics.Add('seed-not-ancestor') }
    $pre = [string](Get-LiveLaneProp $Context 'preS1BaseSha')
    if ($pre -ne $script:LiveLanePreS1BaseSha) { $diagnostics.Add('baseline-drift') }
    $fixtureRel = [string](Get-LiveLaneProp $Context 'fixturePath')
    if ([string]::IsNullOrWhiteSpace($fixtureRel)) { $fixtureRel = $script:LiveLaneFixtureRel }
    $required = @($script:LiveLaneProtectedPaths) + @($fixtureRel)
    if ($Mutant -and $Mutant.ContainsKey('missingPath')) { $required += [string]$Mutant.missingPath }
    foreach ($rel in $required) {
        $exists = Get-LiveLaneGit -RepoRoot $repo -GitArgs @('cat-file', '-e', ($seed + ':' + $rel))
        if ($exists.Exit -ne 0) { $diagnostics.Add('seed-object-missing') }
    }
    $blob = (Get-LiveLaneGit -RepoRoot $repo -GitArgs @('rev-parse', ($seed + ':' + $fixtureRel))).Text
    $expectedBlob = [string](Get-LiveLaneProp $Context 'fixtureBlob')
    if ($Mutant -and $Mutant.ContainsKey('fixtureBlob')) { $expectedBlob = [string]$Mutant.fixtureBlob }
    if ([string]::IsNullOrWhiteSpace($blob) -or $blob -ne $expectedBlob) { $diagnostics.Add('fixture-binding') }
    $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('antiphon-live-lane-' + $PID)
    $before = Join-Path $scratch ('before-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $before -Force | Out-Null
    $fixtureCopy = Join-Path $before $fixtureRel
    try { Copy-LiveLaneGitBlob -RepoRoot $repo -Spec ($seed + ':' + $fixtureRel) -Dest $fixtureCopy } catch { $diagnostics.Add('seed-object-missing') }
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        try { Copy-LiveLaneGitBlob -RepoRoot $repo -Spec ($seed + ':' + $rel) -Dest (Join-Path $before $rel) } catch { $diagnostics.Add('seed-object-missing') }
    }
    if (-not (Test-Path -LiteralPath $fixtureCopy)) { return @{ Accepted = $false; Diagnostics = @($diagnostics | Select-Object -Unique) } }
    $fixture = [System.IO.File]::ReadAllText($fixtureCopy) | ConvertFrom-Json
    $parent = (Get-LiveLaneGit -RepoRoot $repo -GitArgs @('rev-parse', ($seed + '^'))).Text
    $recorded = [string](Get-LiveLaneProp (Get-LiveLaneProp $fixture 'producing') 'sourceSha')
    if ($recorded -ne $parent -or $recorded -eq $seed) { $diagnostics.Add('producer-provenance') }
    $provenance = Get-LiveLaneProp $Context 'producerProvenance'
    if ($Mutant -and $Mutant.ContainsKey('provenance')) { $provenance = $Mutant.provenance }
    foreach ($project in @(ConvertTo-LiveLaneList (Get-LiveLaneProp (Get-LiveLaneProp $fixture 'producing') 'projects'))) {
        $name = [string](Get-LiveLaneProp $project 'project')
        $wantHash = [string](Get-LiveLaneProp $project 'assemblyHash')
        $got = $null
        if ($provenance) { $got = Get-LiveLaneProp $provenance $name }
        $gotHash = [string](Get-LiveLaneProp $got 'assemblyHash')
        $gotSource = [string](Get-LiveLaneProp $got 'sourceSha')
        if ($gotHash -ne $wantHash -or $gotSource -ne $recorded) { $diagnostics.Add('producer-provenance') }
    }
    $names = @((Get-LiveLaneGit -RepoRoot $repo -GitArgs @('log', '--name-only', '--pretty=format:', ($pre + '..' + $seed))).Text -split "`n")
    $touched = @($names | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    if ($Mutant -and $Mutant.ContainsKey('extraPath')) { $touched += [string]$Mutant.extraPath }
    foreach ($name in $touched) {
        if ($script:LiveLaneAllowedSeedPaths -notcontains $name) { $diagnostics.Add('seed-range') }
    }
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        $seedBlob = (Get-LiveLaneGit -RepoRoot $repo -GitArgs @('rev-parse', ($seed + ':' + $rel))).Text
        $preBlob = (Get-LiveLaneGit -RepoRoot $repo -GitArgs @('rev-parse', ($pre + ':' + $rel))).Text
        $drift = $false
        if ($Mutant -and $Mutant.ContainsKey('driftPath') -and $Mutant.driftPath -eq $rel) { $drift = $true }
        if ($drift -or $seedBlob -ne $preBlob) { $diagnostics.Add('baseline-drift') }
    }
    $cp25Path = [string](Get-LiveLaneProp $Context 'cp25Export')
    $cp1Path = [string](Get-LiveLaneProp $Context 'cp1Export')
    if ($Mutant -and $Mutant.ContainsKey('cp25')) { $cp25Path = [string]$Mutant.cp25 }
    if ($Mutant -and $Mutant.ContainsKey('cp1')) { $cp1Path = [string]$Mutant.cp1 }
    $cp25 = Read-LiveLaneExportFile -Path $cp25Path
    $cp1 = Read-LiveLaneExportFile -Path $cp1Path
    $source = [string](Get-LiveLaneProp $Context 'sourceSha')
    $run = [string](Get-LiveLaneProp $Context 'runId')
    if (-not $cp25.Found -or -not $cp1.Found) { $diagnostics.Add('producer-incomplete') }
    else {
        foreach ($loaded in @($cp25, $cp1)) {
            $export = $loaded.Export
            $expectedHash = [string](Get-LiveLaneProp $export 'assemblyHash')
            if ($Mutant -and $Mutant.ContainsKey('exportSource')) { $export | Add-Member -NotePropertyName sourceSha -NotePropertyValue $Mutant.exportSource -Force }
            if ($Mutant -and $Mutant.ContainsKey('exportRun')) { $export | Add-Member -NotePropertyName runId -NotePropertyValue $Mutant.exportRun -Force }
            if ($Mutant -and $Mutant.ContainsKey('exportHash')) { $export | Add-Member -NotePropertyName assemblyHash -NotePropertyValue $Mutant.exportHash -Force }
            if ($Mutant -and $Mutant.ContainsKey('terminal')) { $export | Add-Member -NotePropertyName terminal -NotePropertyValue ([bool]$Mutant.terminal) -Force }
            $check = Test-LiveLaneExportDocument -Export $export -ExpectedSource $source -ExpectedRun $run -ExpectedHash $expectedHash -ExpectedProject ([string](Get-LiveLaneProp $export 'project')) -RequireCore $false -AssemblyPath ([string](Get-LiveLaneProp $export 'assemblyPath'))
            if (-not $check.Accepted -and $diagnostics -notcontains $check.Reason) { $diagnostics.Add($check.Reason) }
        }
    }
    $afterNodes = @()
    if ($cp25.Found) { $afterNodes += @(ConvertTo-LiveLaneList (Get-LiveLaneProp $cp25.Export 'nodes')) }
    if ($cp1.Found) { $afterNodes += @(ConvertTo-LiveLaneList (Get-LiveLaneProp $cp1.Export 'nodes')) }
    if ($Mutant -and $Mutant.ContainsKey('afterNodes')) { $afterNodes = @($Mutant.afterNodes) }
    $beforeNodes = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $fixture 'nodes'))
    $beforeMap = @{}
    foreach ($n in $beforeNodes) { $beforeMap[(Get-LiveLaneNodeUid $n)] = $n }
    $afterMap = @{}
    foreach ($n in $afterNodes) {
        $uid = Get-LiveLaneNodeUid $n
        if (-not [string]::IsNullOrWhiteSpace($uid)) { $afterMap[$uid] = $n }
    }
    $additive = @(ConvertTo-LiveLaneList (Get-LiveLaneProp (Read-LiveLaneManifestObject $repo) 'additiveContract'))
    foreach ($uid in @($beforeMap.Keys)) {
        if (-not $afterMap.ContainsKey($uid)) { $diagnostics.Add('old-membership-changed'); continue }
        $delta = Get-LiveLaneCategoryDelta (Get-LiveLaneProp $beforeMap[$uid] 'categories') (Get-LiveLaneProp $afterMap[$uid] 'categories')
        $allowed = @()
        if (Test-LiveLaneContractIdentity $beforeMap[$uid]) { $allowed = @('LiveClaude') }
        foreach ($added in @($delta.Added)) { if ($allowed -notcontains $added) { $diagnostics.Add('category-delta') } }
        foreach ($removed in @($delta.Removed)) { $diagnostics.Add('category-delta') }
        $beforeArgs = Get-LiveLaneArgumentKey (Get-LiveLaneProp $beforeMap[$uid] 'arguments')
        $afterArgs = Get-LiveLaneArgumentKey (Get-LiveLaneProp $afterMap[$uid] 'arguments')
        if ($beforeArgs -ne $afterArgs) { $diagnostics.Add('extra-expansion') }
    }
    foreach ($uid in @($afterMap.Keys)) {
        if ($beforeMap.ContainsKey($uid)) { continue }
        $node = $afterMap[$uid]
        $class = [string](Get-LiveLaneProp $node 'type')
        if ([string]::IsNullOrWhiteSpace($class)) { $class = [string](Get-LiveLaneProp $node 'class') }
        $method = [string](Get-LiveLaneProp $node 'method')
        $declared = $false
        foreach ($row in $additive) {
            if ([string](Get-LiveLaneProp $row 'class') -eq $class -and [string](Get-LiveLaneProp $row 'method') -eq $method) { $declared = $true }
        }
        if (-not $declared) { $diagnostics.Add('undeclared-new-uid') }
        else {
            $cats = Get-LiveLaneCategorySet (Get-LiveLaneProp $node 'categories')
            if ($cats.Contains('LiveClaude')) { $diagnostics.Add('category-delta') }
        }
    }
    $beforeExp = @{}
    foreach ($n in @($beforeNodes)) {
        $key = ([string](Get-LiveLaneProp $n 'type') + '|' + [string](Get-LiveLaneProp $n 'method'))
        if (-not $beforeExp.ContainsKey($key)) { $beforeExp[$key] = @() }
        $beforeExp[$key] += (Get-LiveLaneArgumentKey (Get-LiveLaneProp $n 'arguments'))
    }
    $afterExp = @{}
    foreach ($n in @($afterNodes)) {
        $key = ([string](Get-LiveLaneProp $n 'type') + '|' + [string](Get-LiveLaneProp $n 'method'))
        if ([string]::IsNullOrWhiteSpace((Get-LiveLaneProp $n 'type'))) { $key = ([string](Get-LiveLaneProp $n 'class') + '|' + [string](Get-LiveLaneProp $n 'method')) }
        if (-not $afterExp.ContainsKey($key)) { $afterExp[$key] = @() }
        $afterExp[$key] += (Get-LiveLaneArgumentKey (Get-LiveLaneProp $n 'arguments'))
    }
    foreach ($key in @($afterExp.Keys)) {
        if (-not $beforeExp.ContainsKey($key)) { continue }
        $left = @($beforeExp[$key] | Sort-Object -Unique) -join '|'
        $right = @($afterExp[$key] | Sort-Object -Unique) -join '|'
        if ($left -ne $right) { $diagnostics.Add('extra-expansion') }
    }
    $afterRoot = $repo
    if ($Mutant -and $Mutant.ContainsKey('afterRoot')) { $afterRoot = [string]$Mutant.afterRoot }
    try {
        $fast = $Mutant -and $Mutant.ContainsKey('fastHash') -and $Mutant.fastHash
        if ($fast) {
            Initialize-LiveLaneNightly -RepoRoot $repo
            $beforeObj = [System.IO.File]::ReadAllText((Join-Path $before 'tests/test-execution-policy.json')) | ConvertFrom-Json
            $afterPolicyFile = Join-Path $afterRoot 'tests/test-execution-policy.json'
            if ($Mutant.ContainsKey('policyPath') -and -not [string]::IsNullOrWhiteSpace([string]$Mutant.policyPath)) { $afterPolicyFile = [string]$Mutant.policyPath }
            $afterObj = [System.IO.File]::ReadAllText($afterPolicyFile) | ConvertFrom-Json
            $beforeHash = Get-NightlyPolicyHash -Object $beforeObj
            $afterHash = Get-NightlyPolicyHash -Object $afterObj
        } else {
            $beforeHash = Get-LiveLanePolicyHashProcess -RepoRoot $repo -PolicyRoot $before
            $afterPolicyArg = ''
            if ($Mutant -and $Mutant.ContainsKey('policyPath')) { $afterPolicyArg = [string]$Mutant.policyPath }
            $afterHash = Get-LiveLanePolicyHashProcess -RepoRoot $repo -PolicyRoot $afterRoot -PolicyPath $afterPolicyArg
        }
        if ($beforeHash -ne $afterHash) { $diagnostics.Add('policy-hash-changed') }
    } catch { $diagnostics.Add('policy-hash-changed') }
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        $seedFile = Join-Path $before $rel
        $afterFile = Join-Path $afterRoot $rel
        if (-not (Test-Path -LiteralPath $seedFile) -or -not (Test-Path -LiteralPath $afterFile)) { $diagnostics.Add('reader-input-changed'); continue }
        $h1 = (Get-FileHash -LiteralPath $seedFile -Algorithm SHA256).Hash
        $h2 = (Get-FileHash -LiteralPath $afterFile -Algorithm SHA256).Hash
        if ($h1 -ne $h2) { $diagnostics.Add('reader-input-changed') }
    }
    Initialize-LiveLaneNightly -RepoRoot $repo
    $beforePolicy = ([System.IO.File]::ReadAllText((Join-Path $before 'tests/test-execution-policy.json')) | ConvertFrom-Json)
    $afterPolicyPath = Join-Path $afterRoot 'tests/test-execution-policy.json'
    if ($Mutant -and $Mutant.ContainsKey('policyPath')) { $afterPolicyPath = [string]$Mutant.policyPath }
    $afterPolicy = ([System.IO.File]::ReadAllText($afterPolicyPath) | ConvertFrom-Json)
    $platform = $(if ($IsLinux) { 'linux' } else { 'windows' })
    $beforeEx = @(Get-LiveLaneExclusionsFromObject $beforePolicy $platform)
    $afterEx = @(Get-LiveLaneExclusionsFromObject $afterPolicy $platform)
    $member = Compare-LiveLaneMembership -BeforeNodes $beforeNodes -AfterNodes $afterNodes -BeforeEx $beforeEx -AfterEx $afterEx -Additive $additive
    foreach ($item in @($member)) { $diagnostics.Add($item) }
    $unique = @($diagnostics | Select-Object -Unique)
    return @{ Accepted = ($unique.Count -eq 0); Diagnostics = $unique }
}

function Get-LiveLaneExclusionsFromObject {
    param($PolicyObject, [string]$Platform)
    $ids = @()
    foreach ($id in @(ConvertTo-LiveLaneList $PolicyObject.defaultSuites)) { $ids += [string]$id }
    foreach ($id in @(ConvertTo-LiveLaneList $PolicyObject.profiles.rc.requiredSuites)) { $ids += [string]$id }
    $rows = @()
    foreach ($id in @($ids | Sort-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($id)) { continue }
        $rows += @(Get-NightlyProfileExclusions -PolicyObject $PolicyObject -Profile 'rc' -SuiteId $id -Platform $Platform)
    }
    return @($rows)
}

function Compare-LiveLaneMembership {
    param($BeforeNodes, $AfterNodes, $BeforeEx, $AfterEx, $Additive)
    $diagnostics = @()
    $beforeDefault = Get-LiveLaneDispositionSets -Nodes $BeforeNodes -Exclusions $null -ProfileAware $false
    $afterDefault = Get-LiveLaneDispositionSets -Nodes $AfterNodes -Exclusions $null -ProfileAware $false
    $beforeRc = Get-LiveLaneDispositionSets -Nodes $BeforeNodes -Exclusions $BeforeEx -ProfileAware $true
    $afterRc = Get-LiveLaneDispositionSets -Nodes $AfterNodes -Exclusions $AfterEx -ProfileAware $true
    $allowed = @{}
    foreach ($n in @(ConvertTo-LiveLaneList $AfterNodes)) {
        $class = [string](Get-LiveLaneProp $n 'type')
        if ([string]::IsNullOrWhiteSpace($class)) { $class = [string](Get-LiveLaneProp $n 'class') }
        $method = [string](Get-LiveLaneProp $n 'method')
        foreach ($row in @(ConvertTo-LiveLaneList $Additive)) {
            if ([string](Get-LiveLaneProp $row 'class') -eq $class -and [string](Get-LiveLaneProp $row 'method') -eq $method) {
                $allowed[(Get-LiveLaneNodeUid $n)] = $true
            }
        }
    }
    if (-not (Test-LiveLaneSetEqual $beforeDefault.Required $afterDefault.Required $allowed) -or -not (Test-LiveLaneSetEqual $beforeDefault.Excluded $afterDefault.Excluded $allowed)) {
        $diagnostics += 'nightly-disposition-changed'
    }
    if (-not (Test-LiveLaneSetEqual $beforeRc.Required $afterRc.Required $allowed) -or -not (Test-LiveLaneSetEqual $beforeRc.Excluded $afterRc.Excluded $allowed)) {
        $c680 = @($BeforeNodes | Where-Object { [string](Get-LiveLaneProp $_ 'method') -eq 'C680_LiveNudgeRefusesPermissionAndQuestionDialogs' } | Select-Object -First 1)
        if ($c680.Count -eq 1) {
            $uid = Get-LiveLaneNodeUid $c680[0]
            $wasRequired = @($beforeRc.Required) -contains $uid
            $nowExcluded = @($afterRc.Excluded) -contains $uid
            if ($wasRequired -and $nowExcluded) { $diagnostics += 'C680-required-to-excluded' }
        }
    }
    $reasonChanged = $false
    $ownerChanged = $false
    $beforeBy = @{}
    foreach ($row in @(ConvertTo-LiveLaneList $BeforeEx)) { $beforeBy[[string]$row.Class] = $row }
    foreach ($row in @(ConvertTo-LiveLaneList $AfterEx)) {
        $cls = [string]$row.Class
        if (-not $beforeBy.ContainsKey($cls)) { continue }
        if ([string]$beforeBy[$cls].Reason -ne [string]$row.Reason) { $reasonChanged = $true }
        if ([string]$beforeBy[$cls].Owner -ne [string]$row.Owner) { $ownerChanged = $true }
    }
    if ($reasonChanged) { $diagnostics += 'disposition-reason-changed' }
    if ($ownerChanged) { $diagnostics += 'disposition-owner-changed' }
    return @($diagnostics)
}

function Get-LiveLaneDispositionSets {
    param($Nodes, $Exclusions, [bool]$ProfileAware)
    $required = @()
    $excluded = @()
    foreach ($n in @(ConvertTo-LiveLaneList $Nodes)) {
        $node = [pscustomobject]@{
            Uid = Get-LiveLaneNodeUid $n
            ClassName = ([string](Get-LiveLaneProp $n 'namespace') + '.' + [string](Get-LiveLaneProp $n 'type'))
            Type = [string](Get-LiveLaneProp $n 'type')
            Method = [string](Get-LiveLaneProp $n 'method')
            Categories = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $n 'categories'))
            Excluded = $false
        }
        $isExcluded = $false
        if ($ProfileAware) { $isExcluded = Test-NightlyProfileNodeExcluded -Node $node -Exclusions $Exclusions }
        else { $isExcluded = Test-NightlyDiscoveryExcluded -Node $node }
        if ($isExcluded) { $excluded += $node.Uid } else { $required += $node.Uid }
    }
    return @{ Required = @($required); Excluded = @($excluded) }
}

function Test-LiveLaneSetEqual {
    param($Left, $Right, $AllowedNew)
    $a = @($Left | Sort-Object -Unique)
    $b = @($Right | Where-Object { -not $AllowedNew.ContainsKey($_) } | Sort-Object -Unique)
    return (($a -join '|') -eq ($b -join '|'))
}

function New-LiveLaneDanglingSeed {
    param([string]$RepoRoot)
    $tree = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', 'HEAD^{tree}')).Text
    $sha = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('commit-tree', $tree, '-m', 'c1545 dangling non-ancestor')).Text
    return $sha
}

function New-LiveLanePolicyMutant {
    param([string]$RepoRoot, [scriptblock]$Edit)
    $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('antiphon-live-lane-policy-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $scratch 'tests') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $scratch 'scripts/lib') -Force | Out-Null
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        Copy-Item -LiteralPath (Join-Path $RepoRoot $rel) -Destination (Join-Path $scratch $rel) -Force
    }
    $policyPath = Join-Path $scratch 'tests/test-execution-policy.json'
    $obj = [System.IO.File]::ReadAllText($policyPath) | ConvertFrom-Json
    & $Edit $obj
    Initialize-LiveLaneNightly -RepoRoot $RepoRoot
    $hash = Get-NightlyPolicyHash -Object $obj
    $obj.policyHash = $hash
    ($obj | ConvertTo-Json -Depth 30) | Set-Content -LiteralPath $policyPath -Encoding utf8
    return @{ Root = $scratch; PolicyPath = $policyPath }
}

function Copy-LiveLaneNodeRows {
    param($Nodes)
    $out = @()
    foreach ($n in @(ConvertTo-LiveLaneList $Nodes)) {
        $out += [pscustomobject]@{
            project = [string](Get-LiveLaneProp $n 'project')
            uid = [string](Get-LiveLaneProp $n 'uid')
            namespace = [string](Get-LiveLaneProp $n 'namespace')
            type = [string](Get-LiveLaneProp $n 'type')
            class = [string](Get-LiveLaneProp $n 'class')
            method = [string](Get-LiveLaneProp $n 'method')
            arguments = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $n 'arguments') | ForEach-Object { [string]$_ })
            categories = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $n 'categories') | ForEach-Object { [string]$_ })
        }
    }
    return @($out)
}

function Test-LiveLaneCensusHas {
    param($Result, [string[]]$Need)
    if ($Result.Accepted) { return $false }
    $have = @($Result.Diagnostics)
    foreach ($item in @($Need)) {
        if ($have -notcontains $item) { return $false }
    }
    return $true
}

function Invoke-LiveLaneFullCensus {
    param([string]$RepoRoot)
    $context = Read-LiveLaneContextObject
    if ($null -eq $context) {
        Write-Output 'context-required'
        return 2
    }
    $real = Invoke-LiveLaneCensus -Context $context
    if (-not $real.Accepted) {
        foreach ($item in @($real.Diagnostics)) { Write-Output $item }
        return 2
    }
    $missing = New-Object System.Collections.Generic.List[string]
    $absent = Invoke-LiveLaneCensus -Context $null
    if (-not (Test-LiveLaneCensusHas $absent @('context-required'))) { $missing.Add('context-absent') }

    $cp25 = Read-LiveLaneExportFile -Path ([string](Get-LiveLaneProp $context 'cp25Export'))
    $cp1 = Read-LiveLaneExportFile -Path ([string](Get-LiveLaneProp $context 'cp1Export'))
    $nodes = @()
    if ($cp25.Found) { $nodes += @(ConvertTo-LiveLaneList (Get-LiveLaneProp $cp25.Export 'nodes')) }
    if ($cp1.Found) { $nodes += @(ConvertTo-LiveLaneList (Get-LiveLaneProp $cp1.Export 'nodes')) }
    $base = @(Copy-LiveLaneNodeRows $nodes)

    $dangling = New-LiveLaneDanglingSeed -RepoRoot $RepoRoot
    $seedCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ seed = $dangling; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $seedCase @('seed-not-ancestor'))) { $missing.Add('seed-nonancestor') }

    $objectCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ missingPath = 'tests/fixtures/live-lanes/does-not-exist'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $objectCase @('seed-object-missing'))) { $missing.Add('object-missing') }

    $blobCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ fixtureBlob = ('a' * 40); fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $blobCase @('fixture-binding'))) { $missing.Add('fixture-blob') }

    $badProv = [pscustomobject]@{
        'Antiphon.Tests' = [pscustomobject]@{ sourceSha = '0' * 40; assemblyHash = '0' * 64 }
        'Antiphon.Agents.Pty.Tests' = [pscustomobject]@{ sourceSha = '0' * 40; assemblyHash = '0' * 64 }
    }
    $provCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ provenance = $badProv; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $provCase @('producer-provenance'))) { $missing.Add('producer-provenance') }

    $driftCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ driftPath = 'tests/test-execution-policy.json'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $driftCase @('baseline-drift'))) { $missing.Add('prebase-drift') }

    $rangeCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ extraPath = 'docs/not-a-live-lane.md'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $rangeCase @('seed-range'))) { $missing.Add('seed-unlisted-edit') }

    $sourceCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ exportSource = ('b' * 40); fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $sourceCase @('source-mismatch'))) { $missing.Add('candidate-source') }
    $buildCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ exportHash = ('c' * 64); fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $buildCase @('build-mismatch'))) { $missing.Add('candidate-build') }
    $runCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ exportRun = 'other-run'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $runCase @('run-mismatch'))) { $missing.Add('candidate-run') }
    $pendingCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ terminal = $false; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $pendingCase @('producer-incomplete'))) { $missing.Add('pending-producer') }

    $dropped = @($base | Select-Object -Skip 1)
    $dropCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ afterNodes = $dropped; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $dropCase @('old-membership-changed'))) { $missing.Add('remove-old-uid') }

    $integration = @(Copy-LiveLaneNodeRows $base)
    $cleared = $false
    foreach ($row in $integration) {
        $cats = @($row.categories)
        if ($cats -contains 'Integration') {
            $row.categories = @($cats | Where-Object { $_ -ne 'Integration' })
            $cleared = $true
            break
        }
    }
    if (-not $cleared) { $missing.Add('remove-Integration') }
    else {
        $catCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ afterNodes = $integration; fastHash = $true }
        if (-not (Test-LiveLaneCensusHas $catCase @('category-delta'))) { $missing.Add('remove-Integration') }
    }

    $seventh = @(Copy-LiveLaneNodeRows $base)
    $tagged = $false
    foreach ($row in $seventh) {
        if (Test-LiveLaneContractIdentity $row) { continue }
        $cats = @($row.categories)
        if ($cats -notcontains 'LiveClaude') {
            $row.categories = @($cats + @('LiveClaude'))
            $tagged = $true
            break
        }
    }
    if (-not $tagged) { $missing.Add('tag-seventh') }
    else {
        $tagCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ afterNodes = $seventh; fastHash = $true }
        if (-not (Test-LiveLaneCensusHas $tagCase @('category-delta'))) { $missing.Add('tag-seventh') }
    }

    $extra = @(Copy-LiveLaneNodeRows $base)
    if ($extra.Count -gt 0) {
        $clone = $extra[0]
        $extra += [pscustomobject]@{
            project = $clone.project; uid = ($clone.uid + '-extra'); namespace = $clone.namespace; type = $clone.type
            class = $clone.class; method = $clone.method; arguments = @('extra-arg'); categories = @($clone.categories)
        }
    }
    $extraCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ afterNodes = $extra; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $extraCase @('extra-expansion'))) { $missing.Add('extra-expansion') }

    $unknown = @(Copy-LiveLaneNodeRows $base)
    $unknown += [pscustomobject]@{
        project = 'Antiphon.Tests'; uid = 'undeclared-live-lane-uid'; namespace = 'Antiphon.Tests.Scripts'; type = 'NotInContract'
        class = 'NotInContract'; method = 'UnknownMethod'; arguments = @(); categories = @('Unit')
    }
    $unknownCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ afterNodes = $unknown; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $unknownCase @('undeclared-new-uid'))) { $missing.Add('unknown-new-uid') }

    $nightly = @(Copy-LiveLaneNodeRows $base)
    $opted = $false
    foreach ($row in $nightly) {
        $cats = @($row.categories)
        if ($cats -contains 'OptIn' -or $cats -contains 'Explicit') { continue }
        if (Test-LiveLaneContractIdentity $row) { continue }
        $row.categories = @($cats + @('OptIn'))
        $opted = $true
        break
    }
    if (-not $opted) { $missing.Add('default-disposition') }
    else {
        $nightCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ afterNodes = $nightly; fastHash = $true }
        if (-not (Test-LiveLaneCensusHas $nightCase @('nightly-disposition-changed'))) { $missing.Add('default-disposition') }
    }

    $readerRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('antiphon-live-lane-reader-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $readerRoot 'scripts/lib') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $readerRoot 'tests') -Force | Out-Null
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        Copy-Item -LiteralPath (Join-Path $RepoRoot $rel) -Destination (Join-Path $readerRoot $rel) -Force
    }
    Add-Content -LiteralPath (Join-Path $readerRoot 'scripts/lib/nightly-coverage.ps1') -Value "`n# c1545 scratch`n" -Encoding ascii
    $readerCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ afterRoot = $readerRoot; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $readerCase @('reader-input-changed'))) { $missing.Add('reader-edit') }

    $c680 = New-LiveLanePolicyMutant -RepoRoot $RepoRoot -Edit {
        param($obj)
        $list = New-Object System.Collections.Generic.List[object]
        foreach ($e in @(ConvertTo-LiveLaneList $obj.profiles.rc.exclusions)) { $list.Add($e) }
        $list.Add([pscustomobject]@{ suite = 'antiphon'; class = 'ClaudeDialogWatchdogCanaryTests'; reason = 'card-1545 scratch exclusion'; owner = 'CARD-1545' })
        $obj.profiles.rc.exclusions = $list.ToArray()
    }
    $c680Case = Invoke-LiveLaneCensus -Context $context -Mutant @{ afterRoot = $c680.Root; policyPath = $c680.PolicyPath; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $c680Case @('policy-hash-changed', 'C680-required-to-excluded'))) { $missing.Add('c680-exclusion') }

    $reason = New-LiveLanePolicyMutant -RepoRoot $RepoRoot -Edit {
        param($obj)
        $obj.profiles.rc.exclusions[0].reason = ([string]$obj.profiles.rc.exclusions[0].reason + ' scratch')
    }
    $reasonCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ afterRoot = $reason.Root; policyPath = $reason.PolicyPath; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $reasonCase @('disposition-reason-changed'))) { $missing.Add('reason-edit') }

    $owner = New-LiveLanePolicyMutant -RepoRoot $RepoRoot -Edit {
        param($obj)
        $obj.profiles.rc.exclusions[0].owner = 'CARD-1545-SCRATCH'
    }
    $ownerCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ afterRoot = $owner.Root; policyPath = $owner.PolicyPath; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $ownerCase @('disposition-owner-changed'))) { $missing.Add('owner-edit') }

    if ($missing.Count -gt 0) {
        foreach ($name in @($missing)) { Write-Output ('expected-refusal-missing ' + $name) }
        return 1
    }
    Write-Output 'accepted'
    return 0
}
