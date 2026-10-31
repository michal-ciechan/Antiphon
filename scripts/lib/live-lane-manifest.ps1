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
    $item = Get-Item -LiteralPath $Path
    $key = $item.FullName + '|' + $item.Length + '|' + $item.LastWriteTimeUtc.Ticks
    if ($null -eq $script:LiveLaneExportCache) { $script:LiveLaneExportCache = @{} }
    if ($script:LiveLaneExportCache.ContainsKey($key)) { return $script:LiveLaneExportCache[$key] }
    $loaded = @{ Found = $true; Export = ([System.IO.File]::ReadAllText($Path) | ConvertFrom-Json) }
    $script:LiveLaneExportCache[$key] = $loaded
    return $loaded
}

function Copy-LiveLaneExportShell {
    param($Export)
    $terminal = Get-LiveLaneProp $Export 'terminal'
    $flag = $false
    if ($null -ne $terminal) { $flag = [bool]$terminal }
    return [pscustomobject]@{
        format = [string](Get-LiveLaneProp $Export 'format')
        project = [string](Get-LiveLaneProp $Export 'project')
        cp = [string](Get-LiveLaneProp $Export 'cp')
        sourceSha = [string](Get-LiveLaneProp $Export 'sourceSha')
        runId = [string](Get-LiveLaneProp $Export 'runId')
        assemblyHash = [string](Get-LiveLaneProp $Export 'assemblyHash')
        assemblyPath = [string](Get-LiveLaneProp $Export 'assemblyPath')
        terminal = $flag
        nodes = (Get-LiveLaneProp $Export 'nodes')
    }
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

function Initialize-LiveLaneSourceStripper {
    if ($script:LiveLaneSourceReady) { return }
    $code = @'
using System;
using System.Text;
public static class LiveLaneSource
{
    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        Code(text, sb, 0, text.Length, false);
        return sb.ToString();
    }

    static int Code(string text, StringBuilder sb, int i, int n, bool hole)
    {
        int depth = hole ? 1 : 0;
        while (i < n)
        {
            char c = text[i];
            if (i + 1 < n && c == '/' && text[i + 1] == '/')
            {
                while (i < n && text[i] != '\n') i++;
                continue;
            }
            if (i + 1 < n && c == '/' && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(text[i] == '*' && text[i + 1] == '/'))
                {
                    if (text[i] == '\n') sb.Append('\n');
                    i++;
                }
                if (i + 1 < n) i += 2;
                sb.Append(' ');
                continue;
            }
            if (IsVerbatimInterp(text, i))
            {
                i = Interpolated(text, sb, i + 3, n, true);
                continue;
            }
            if (i + 1 < n && c == '@' && text[i + 1] == '"')
            {
                i = Verbatim(text, sb, i + 2, n);
                continue;
            }
            if (c == '$' && i + 1 < n && QuoteRun(text, i + 1) >= 3)
            {
                i = Raw(text, sb, i + 1, n, QuoteRun(text, i + 1));
                continue;
            }
            int run = QuoteRun(text, i);
            if (run >= 3)
            {
                i = Raw(text, sb, i, n, run);
                continue;
            }
            if (i + 1 < n && c == '$' && text[i + 1] == '"')
            {
                i = Interpolated(text, sb, i + 2, n, false);
                continue;
            }
            if (c == '"')
            {
                i = Normal(text, sb, i + 1, n);
                continue;
            }
            if (c == '\'')
            {
                i = CharLit(text, sb, i, n);
                continue;
            }
            if (hole && c == '{') { depth++; sb.Append(c); i++; continue; }
            if (hole && c == '}')
            {
                depth--;
                if (depth == 0) return i + 1;
                sb.Append(c);
                i++;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return i;
    }

    static bool IsVerbatimInterp(string text, int i)
    {
        if (i + 2 >= text.Length) return false;
        return (text[i] == '$' && text[i + 1] == '@' && text[i + 2] == '"')
            || (text[i] == '@' && text[i + 1] == '$' && text[i + 2] == '"');
    }

    static int QuoteRun(string text, int i)
    {
        int count = 0;
        while (i + count < text.Length && text[i + count] == '"') count++;
        return count;
    }

    static void Lit(StringBuilder sb, char c)
    {
        sb.Append(c == '{' || c == '}' ? ' ' : c);
    }

    static int Normal(string text, StringBuilder sb, int i, int n)
    {
        while (i < n)
        {
            char c = text[i];
            if (c == '\\')
            {
                Lit(sb, c);
                if (i + 1 < n) Lit(sb, text[i + 1]);
                i += 2;
                continue;
            }
            if (c == '"') return i + 1;
            Lit(sb, c);
            i++;
        }
        return i;
    }

    static int Verbatim(string text, StringBuilder sb, int i, int n)
    {
        while (i < n)
        {
            if (text[i] == '"')
            {
                if (i + 1 < n && text[i + 1] == '"') { Lit(sb, '"'); i += 2; continue; }
                return i + 1;
            }
            Lit(sb, text[i]);
            i++;
        }
        return i;
    }

    static int Raw(string text, StringBuilder sb, int quoteAt, int n, int quotes)
    {
        int i = quoteAt + quotes;
        int end = n;
        int k = i;
        while (k < n && text[k] != '\n')
        {
            if (text[k] == '"')
            {
                int run = QuoteRun(text, k);
                if (run >= quotes) { end = k + run; break; }
                k += Math.Max(run, 1);
                continue;
            }
            k++;
        }
        if (end == n)
        {
            i = k;
            while (i < n)
            {
                if (text[i] == '\n')
                {
                    int j = i + 1;
                    while (j < n && (text[j] == ' ' || text[j] == '\t')) j++;
                    if (QuoteRun(text, j) >= quotes)
                    {
                        end = j;
                        while (end < n && text[end] == '"') end++;
                        break;
                    }
                }
                i++;
            }
        }
        for (int t = quoteAt; t < end; t++) Lit(sb, text[t]);
        return end;
    }

    static int Interpolated(string text, StringBuilder sb, int i, int n, bool verbatim)
    {
        while (i < n)
        {
            char c = text[i];
            if (!verbatim && c == '\\')
            {
                Lit(sb, c);
                if (i + 1 < n) Lit(sb, text[i + 1]);
                i += 2;
                continue;
            }
            if (verbatim && c == '"' && i + 1 < n && text[i + 1] == '"') { Lit(sb, '"'); i += 2; continue; }
            if (c == '"') return i + 1;
            if (c == '{' && i + 1 < n && text[i + 1] == '{') { Lit(sb, ' '); Lit(sb, ' '); i += 2; continue; }
            if (c == '}' && i + 1 < n && text[i + 1] == '}') { Lit(sb, ' '); Lit(sb, ' '); i += 2; continue; }
            if (c == '{')
            {
                sb.Append('{');
                i = Code(text, sb, i + 1, n, true);
                sb.Append('}');
                continue;
            }
            Lit(sb, c);
            i++;
        }
        return i;
    }

    static int CharLit(string text, StringBuilder sb, int i, int n)
    {
        i++;
        if (i < n && text[i] == '\\')
        {
            i++;
            if (i < n) i++;
        }
        else if (i < n)
        {
            if (text[i] == '{' || text[i] == '}') sb.Append(' ');
            i++;
        }
        if (i < n && text[i] == '\'') i++;
        return i;
    }
}
'@
    Add-Type -TypeDefinition $code -Language CSharp
    $script:LiveLaneSourceReady = $true
}

function Remove-LiveLaneComments {
    param([string]$Text)
    Initialize-LiveLaneSourceStripper
    return [LiveLaneSource]::Strip([string]$Text)
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
    $reasons = New-Object System.Collections.Generic.List[string]
    foreach ($file in $files) {
        $path = [string]$file.FullName
        $project = ''
        if ($path -match 'tests[/\\]Antiphon\.Tests[/\\]' -or $path -match 'Antiphon\.Tests') {
            if ($path -match 'Antiphon\.Agents\.Pty\.Tests') { $project = 'Antiphon.Agents.Pty.Tests' } else { $project = 'Antiphon.Tests' }
        }
        if ($path -match 'Antiphon\.Agents\.Pty\.Tests') { $project = 'Antiphon.Agents.Pty.Tests' }
        $raw = [System.IO.File]::ReadAllText($path)
        if ($raw -notmatch 'Explicit|ANTIPHON_HEADED_TESTS|SkipIfNotEligible|RealCliStub|GateShape') { continue }
        $text = Remove-LiveLaneComments $raw
        $ns = ''
        $nsMatch = [regex]::Match($text, 'namespace\s+([A-Za-z0-9_\.]+)\s*[;{]')
        if ($nsMatch.Success) { $ns = $nsMatch.Groups[1].Value }
        $classRx = [regex]'(?<attrs>(?:\[[^\]]*\]\s*)*)(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+|file\s+)*class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)'
        $classMatches = @($classRx.Matches($text))
        for ($c = 0; $c -lt $classMatches.Count; $c++) {
            $cm = $classMatches[$c]
            $className = $cm.Groups['name'].Value
            $classAttrs = $cm.Groups['attrs'].Value
            $classExplicit = $classAttrs -match '\[Explicit(\(|\]|,)'
            $braceAt = $text.IndexOf('{', $cm.Index + $cm.Length)
            if ($braceAt -lt 0) { if ($classExplicit) { $unresolved++; $reasons.Add($path + "`t" + $className + "`tno-brace") }; continue }
            $span = Get-LiveLaneBraceSpan -Text $text -OpenAt $braceAt
            if ($null -eq $span) { $unresolved++; $reasons.Add($path + "`t" + $className + "`tclass-span"); continue }
            $local = $span
            foreach ($other in $classMatches) {
                if ($other.Index -le $cm.Index) { continue }
                $otherBrace = $text.IndexOf('{', $other.Index + $other.Length)
                if ($otherBrace -lt $braceAt -or $otherBrace -ge ($braceAt + $span.Length)) { continue }
                $otherSpan = Get-LiveLaneBraceSpan -Text $text -OpenAt $otherBrace
                if ($null -eq $otherSpan) { continue }
                $rel = $otherBrace - $braceAt
                if ($rel -ge 0 -and ($rel + $otherSpan.Length) -le $local.Length) {
                    $local = $local.Remove($rel, $otherSpan.Length).Insert($rel, (' ' * $otherSpan.Length))
                }
            }
            $methodRx = [regex]'(?<attrs>(?:\[[^\]]*\]\s*)+)(?:public|internal)\s+(?:sealed\s+|static\s+|async\s+|override\s+|virtual\s+|new\s+|partial\s+|[\w\.]+\s+)*?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\('
            $methods = @($methodRx.Matches($local))
            $tests = @($methods | Where-Object { $_.Groups['attrs'].Value -match '\[Test(\(|\]|,)' })
            if ($classExplicit -and $tests.Count -eq 0) { $unresolved++; $reasons.Add($path + "`t" + $className + "`tno-tests") }
            foreach ($mm in $tests) {
                $attrs = $mm.Groups['attrs'].Value
                $method = $mm.Groups['name'].Value
                if ($method -eq 'SkipIfNotEligible') { continue }
                $sigEnd = $mm.Index + $mm.Length
                $open = $local.IndexOf('{', $sigEnd)
                $arrow = $local.IndexOf('=>', $sigEnd)
                $body = ''
                if ($arrow -ge 0 -and ($open -lt 0 -or $arrow -lt $open)) {
                    $semi = $local.IndexOf(';', $arrow)
                    if ($semi -lt 0) { $semi = $local.Length }
                    $body = $local.Substring($arrow, $semi - $arrow)
                } elseif ($open -ge 0) {
                    $bodySpan = Get-LiveLaneBraceSpan -Text $local -OpenAt $open
                    if ($null -eq $bodySpan) { $unresolved++; $reasons.Add($path + "`t" + $className + "`t" + $method + "`tbody-span"); continue }
                    $body = $bodySpan
                }
                $shapes = @()
                if ($classExplicit -or $attrs -match '\[Explicit(\(|\]|,)') { $shapes += 'Explicit' }
                if ($body -match 'ANTIPHON_HEADED_TESTS') { $shapes += 'ANTIPHON_HEADED_TESTS' }
                if ($body -match 'SkipIfNotEligible' -and $method -ne 'SkipIfNotEligible') { $shapes += 'SkipIfNotEligible' }
                if ($body -match 'RealCliStub') { $shapes += 'RealCliStub' }
                $unknown = $attrs -match '\[GateShape\('
                if ($unknown) { $unresolved++; $reasons.Add($path + "`t" + $className + "`t" + $method + "`tgate-shape"); continue }
                if ($shapes.Count -eq 0) { continue }
                $argGroups = New-Object System.Collections.Generic.List[object]
                $argMatches = [regex]::Matches($attrs, 'Arguments\(([^)]*)\)')
                if ($argMatches.Count -eq 0) { $argGroups.Add(@()) }
                else {
                    foreach ($am in $argMatches) { $argGroups.Add(@(Get-LiveLaneQuotedArgs $am.Groups[1].Value)) }
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

function Initialize-LiveLaneCensusEngine {
    if ($script:LiveLaneCensusEngineReady) { return }
    $code = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public static class LiveLaneCensusEngine
{
    sealed class Node
    {
        public string Project;
        public string Uid;
        public string Ns;
        public string Type;
        public string Method;
        public string ArgKey;
        public string[] Cats;
    }

    sealed class ExRow
    {
        public string Class;
        public string Reason;
        public string Owner;
        public string[] Methods;
    }

    sealed class Id
    {
        public string Project;
        public string Ns;
        public string Class;
        public string Method;
        public string ArgKey;
    }

    static readonly Dictionary<string, Node[]> Files = new Dictionary<string, Node[]>(StringComparer.Ordinal);
    static readonly Dictionary<string, string> Results = new Dictionary<string, string>(StringComparer.Ordinal);

    public static string Compare(string beforeFile, string after1, string after2, string beforeEx, string afterEx, string additive, string contract, string patch)
    {
        string key = (beforeFile ?? "") + "\n" + (after1 ?? "") + "\n" + (after2 ?? "") + "\n"
            + Stamp(beforeFile) + "\n" + Stamp(after1) + "\n" + Stamp(after2) + "\n"
            + (beforeEx ?? "") + "\n" + (afterEx ?? "") + "\n" + (additive ?? "") + "\n" + (contract ?? "") + "\n" + (patch ?? "");
        string cached;
        if (Results.TryGetValue(key, out cached)) return cached;
        Node[] before = Load(beforeFile);
        Node[] after = Concat(Load(after1), Load(after2));
        after = Apply(after, patch ?? "", ParseIds(contract));
        var diag = Run(before, after, ParseEx(beforeEx), ParseEx(afterEx), ParseIds(additive), ParseIds(contract));
        string text = string.Join("\n", diag);
        Results[key] = text;
        return text;
    }

    static string Stamp(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "missing";
        var info = new FileInfo(path);
        return info.Length.ToString() + ":" + info.LastWriteTimeUtc.Ticks.ToString();
    }

    static Node[] Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new Node[0];
        string stamp = path + "\n" + Stamp(path);
        Node[] hit;
        if (Files.TryGetValue(stamp, out hit)) return hit;
        var list = new List<Node>();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(path)))
        {
            JsonElement nodes;
            if (!doc.RootElement.TryGetProperty("nodes", out nodes) || nodes.ValueKind != JsonValueKind.Array) 
            {
                Files[stamp] = new Node[0];
                return Files[stamp];
            }
            foreach (var el in nodes.EnumerateArray()) list.Add(ReadNode(el));
        }
        var arr = list.ToArray();
        Files[stamp] = arr;
        return arr;
    }

    static Node ReadNode(JsonElement el)
    {
        var n = new Node();
        n.Project = Str(el, "project");
        n.Uid = Str(el, "uid");
        n.Ns = Str(el, "namespace");
        n.Type = Str(el, "type");
        if (string.IsNullOrEmpty(n.Type)) n.Type = Str(el, "class");
        n.Method = Str(el, "method");
        n.ArgKey = ArgKey(el);
        n.Cats = CatList(el);
        return n;
    }

    static string Str(JsonElement el, string name)
    {
        JsonElement value;
        if (!el.TryGetProperty(name, out value) || value.ValueKind == JsonValueKind.Null) return "";
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
        return value.ToString();
    }

    static string ArgKey(JsonElement el)
    {
        JsonElement value;
        if (!el.TryGetProperty("arguments", out value) || value.ValueKind == JsonValueKind.Null) return "";
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
        if (value.ValueKind != JsonValueKind.Array) return "";
        var parts = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            string text = item.ValueKind == JsonValueKind.String ? (item.GetString() ?? "") : item.ToString();
            parts.Add(text);
        }
        return string.Join("\n", parts);
    }

    static string[] CatList(JsonElement el)
    {
        JsonElement value;
        if (!el.TryGetProperty("categories", out value)) return new string[0];
        if (value.ValueKind == JsonValueKind.String) return new string[] { value.GetString() ?? "" };
        if (value.ValueKind != JsonValueKind.Array) return new string[0];
        var parts = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            string text = item.ValueKind == JsonValueKind.String ? (item.GetString() ?? "") : item.ToString();
            if (text.Length > 0) parts.Add(text);
        }
        return parts.ToArray();
    }

    static Node[] Concat(Node[] left, Node[] right)
    {
        var all = new Node[left.Length + right.Length];
        Array.Copy(left, 0, all, 0, left.Length);
        Array.Copy(right, 0, all, left.Length, right.Length);
        return all;
    }

    static Id[] ParseIds(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Id[0];
        var list = new List<Id>();
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new Id[0];
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var id = new Id();
                id.Project = Str(el, "project");
                id.Ns = Str(el, "namespace");
                id.Class = Str(el, "class");
                if (string.IsNullOrEmpty(id.Class)) id.Class = Str(el, "type");
                id.Method = Str(el, "method");
                id.ArgKey = ArgKey(el);
                list.Add(id);
            }
        }
        return list.ToArray();
    }

    static ExRow[] ParseEx(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ExRow[0];
        var list = new List<ExRow>();
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new ExRow[0];
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var row = new ExRow();
                row.Class = Str(el, "class");
                row.Reason = Str(el, "reason");
                row.Owner = Str(el, "owner");
                JsonElement methods;
                if (el.TryGetProperty("methods", out methods) && methods.ValueKind == JsonValueKind.Array)
                {
                    var parts = new List<string>();
                    foreach (var item in methods.EnumerateArray())
                    {
                        string text = item.ValueKind == JsonValueKind.String ? (item.GetString() ?? "") : item.ToString();
                        if (text.Length > 0) parts.Add(text);
                    }
                    row.Methods = parts.ToArray();
                }
                else row.Methods = new string[0];
                list.Add(row);
            }
        }
        return list.ToArray();
    }

    static Node Clone(Node n)
    {
        var c = new Node();
        c.Project = n.Project;
        c.Uid = n.Uid;
        c.Ns = n.Ns;
        c.Type = n.Type;
        c.Method = n.Method;
        c.ArgKey = n.ArgKey;
        c.Cats = (string[])n.Cats.Clone();
        return c;
    }

    static bool HasCat(string[] cats, string name)
    {
        for (int i = 0; i < cats.Length; i++)
        {
            if (string.Equals(cats[i], name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    static string[] WithoutCat(string[] cats, string name)
    {
        var list = new List<string>();
        for (int i = 0; i < cats.Length; i++)
        {
            if (!string.Equals(cats[i], name, StringComparison.OrdinalIgnoreCase)) list.Add(cats[i]);
        }
        return list.ToArray();
    }

    static string[] WithCat(string[] cats, string name)
    {
        var list = new List<string>();
        list.AddRange(cats);
        list.Add(name);
        return list.ToArray();
    }

    static bool IsContract(Node n, Id[] contract)
    {
        for (int i = 0; i < contract.Length; i++)
        {
            var id = contract[i];
            if (!string.Equals(id.Project, n.Project, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(id.Ns, n.Ns, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(id.Class, n.Type, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(id.Method, n.Method, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(id.ArgKey, n.ArgKey, StringComparison.OrdinalIgnoreCase)) continue;
            return true;
        }
        return false;
    }

    static Node[] Apply(Node[] after, string patch, Id[] contract)
    {
        if (string.IsNullOrEmpty(patch)) return after;
        if (patch == "drop-first")
        {
            if (after.Length == 0) return after;
            var dropped = new Node[after.Length - 1];
            Array.Copy(after, 1, dropped, 0, dropped.Length);
            return dropped;
        }
        var copy = (Node[])after.Clone();
        if (patch == "remove-integration")
        {
            for (int i = 0; i < copy.Length; i++)
            {
                if (!HasCat(copy[i].Cats, "Integration")) continue;
                var n = Clone(copy[i]);
                n.Cats = WithoutCat(n.Cats, "Integration");
                copy[i] = n;
                break;
            }
            return copy;
        }
        if (patch == "tag-seventh")
        {
            for (int i = 0; i < copy.Length; i++)
            {
                if (IsContract(copy[i], contract)) continue;
                if (HasCat(copy[i].Cats, "LiveClaude")) continue;
                var n = Clone(copy[i]);
                n.Cats = WithCat(n.Cats, "LiveClaude");
                copy[i] = n;
                break;
            }
            return copy;
        }
        if (patch == "add-optin")
        {
            for (int i = 0; i < copy.Length; i++)
            {
                if (HasCat(copy[i].Cats, "OptIn") || HasCat(copy[i].Cats, "Explicit")) continue;
                if (IsContract(copy[i], contract)) continue;
                var n = Clone(copy[i]);
                n.Cats = WithCat(n.Cats, "OptIn");
                copy[i] = n;
                break;
            }
            return copy;
        }
        if (patch == "extra-expansion")
        {
            if (copy.Length == 0) return copy;
            var n = Clone(copy[0]);
            n.Uid = (n.Uid ?? "") + "-extra";
            n.ArgKey = "extra-arg";
            var grown = new Node[copy.Length + 1];
            Array.Copy(copy, grown, copy.Length);
            grown[copy.Length] = n;
            return grown;
        }
        if (patch == "unknown-node")
        {
            var n = new Node();
            n.Project = "Antiphon.Tests";
            n.Uid = "undeclared-live-lane-uid";
            n.Ns = "Antiphon.Tests.Scripts";
            n.Type = "NotInContract";
            n.Method = "UnknownMethod";
            n.ArgKey = "";
            n.Cats = new string[] { "Unit" };
            var grown = new Node[copy.Length + 1];
            Array.Copy(copy, grown, copy.Length);
            grown[copy.Length] = n;
            return grown;
        }
        return copy;
    }

    static List<string> Run(Node[] before, Node[] after, ExRow[] beforeEx, ExRow[] afterEx, Id[] additive, Id[] contract)
    {
        var diag = new List<string>();
        var beforeMap = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in before) beforeMap[n.Uid ?? ""] = n;
        var afterMap = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in after) afterMap[n.Uid ?? ""] = n;
        foreach (var pair in beforeMap)
        {
            Node other;
            if (!afterMap.TryGetValue(pair.Key, out other))
            {
                Add(diag, "old-membership-changed");
                continue;
            }
            bool removed;
            bool addedOther;
            bool addedLive;
            CategoryDelta(pair.Value.Cats, other.Cats, out removed, out addedOther, out addedLive);
            if (removed || addedOther) Add(diag, "category-delta");
            else if (addedLive && !IsContract(pair.Value, contract)) Add(diag, "category-delta");
            if (!string.Equals(pair.Value.ArgKey ?? "", other.ArgKey ?? "", StringComparison.OrdinalIgnoreCase)) Add(diag, "extra-expansion");
        }
        foreach (var pair in afterMap)
        {
            if (beforeMap.ContainsKey(pair.Key)) continue;
            var n = pair.Value;
            bool declared = false;
            for (int i = 0; i < additive.Length; i++)
            {
                if (string.Equals(additive[i].Class, n.Type, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(additive[i].Method, n.Method, StringComparison.OrdinalIgnoreCase))
                {
                    declared = true;
                    break;
                }
            }
            if (!declared) Add(diag, "undeclared-new-uid");
            else if (HasCat(n.Cats, "LiveClaude")) Add(diag, "category-delta");
        }
        if (ExpansionDiffers(before, after)) Add(diag, "extra-expansion");
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in after)
        {
            for (int i = 0; i < additive.Length; i++)
            {
                if (string.Equals(additive[i].Class, n.Type, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(additive[i].Method, n.Method, StringComparison.OrdinalIgnoreCase))
                {
                    allowed.Add(n.Uid ?? "");
                    break;
                }
            }
        }
        var beforeDefault = Disposition(before, null, false);
        var afterDefault = Disposition(after, null, false);
        var beforeRc = Disposition(before, beforeEx, true);
        var afterRc = Disposition(after, afterEx, true);
        if (!SetEqual(beforeDefault.Required, Filter(afterDefault.Required, allowed)) ||
            !SetEqual(beforeDefault.Excluded, Filter(afterDefault.Excluded, allowed)))
        {
            Add(diag, "nightly-disposition-changed");
        }
        if (!SetEqual(beforeRc.Required, Filter(afterRc.Required, allowed)) ||
            !SetEqual(beforeRc.Excluded, Filter(afterRc.Excluded, allowed)))
        {
            string c680 = "";
            foreach (var n in before)
            {
                if (string.Equals(n.Method, "C680_LiveNudgeRefusesPermissionAndQuestionDialogs", StringComparison.OrdinalIgnoreCase))
                {
                    c680 = n.Uid ?? "";
                    break;
                }
            }
            if (c680.Length > 0 && beforeRc.Required.Contains(c680) && afterRc.Excluded.Contains(c680)) Add(diag, "C680-required-to-excluded");
        }
        return diag;
    }

    static void CategoryDelta(string[] before, string[] after, out bool removed, out bool addedOther, out bool addedLive)
    {
        removed = false;
        addedOther = false;
        addedLive = false;
        var b = new HashSet<string>(before ?? new string[0], StringComparer.Ordinal);
        var a = new HashSet<string>(after ?? new string[0], StringComparer.Ordinal);
        foreach (var item in b) if (!a.Contains(item)) removed = true;
        foreach (var item in a)
        {
            if (b.Contains(item)) continue;
            if (string.Equals(item, "LiveClaude", StringComparison.Ordinal)) addedLive = true;
            else addedOther = true;
        }
    }

    static bool ExpansionDiffers(Node[] before, Node[] after)
    {
        var left = Expand(before);
        var right = Expand(after);
        foreach (var pair in right)
        {
            HashSet<string> other;
            if (!left.TryGetValue(pair.Key, out other)) continue;
            if (!pair.Value.SetEquals(other)) return true;
        }
        return false;
    }

    static Dictionary<string, HashSet<string>> Expand(Node[] nodes)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
        {
            string key = (n.Type ?? "") + "|" + (n.Method ?? "");
            HashSet<string> set;
            if (!map.TryGetValue(key, out set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[key] = set;
            }
            set.Add(n.ArgKey ?? "");
        }
        return map;
    }

    sealed class Sets
    {
        public HashSet<string> Required;
        public HashSet<string> Excluded;
    }

    static Sets Disposition(Node[] nodes, ExRow[] exclusions, bool profile)
    {
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = new Dictionary<string, List<ExRow>>(StringComparer.OrdinalIgnoreCase);
        if (profile && exclusions != null)
        {
            foreach (var row in exclusions)
            {
                if (string.IsNullOrWhiteSpace(row.Class)) continue;
                List<ExRow> list;
                if (!index.TryGetValue(row.Class, out list))
                {
                    list = new List<ExRow>();
                    index[row.Class] = list;
                }
                list.Add(row);
            }
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
        {
            string uid = n.Uid ?? "";
            if (!seen.Add(uid)) continue;
            bool hit = profile ? ProfileHit(n, index) : HasCat(n.Cats, "OptIn") || HasCat(n.Cats, "Explicit");
            if (hit) excluded.Add(uid);
            else required.Add(uid);
        }
        return new Sets { Required = required, Excluded = excluded };
    }

    static bool ProfileHit(Node n, Dictionary<string, List<ExRow>> index)
    {
        string className = (n.Ns ?? "") + "." + (n.Type ?? "");
        int open = className.IndexOf('(');
        if (open > 0 && className.EndsWith(")", StringComparison.Ordinal)) className = className.Substring(0, open);
        int dot = className.LastIndexOf('.');
        string simple = (dot >= 0 && dot < className.Length - 1) ? className.Substring(dot + 1) : className;
        if (Hit(index, simple, n.Method)) return true;
        if (!string.Equals(simple, className, StringComparison.OrdinalIgnoreCase) && Hit(index, className, n.Method)) return true;
        return false;
    }

    static bool Hit(Dictionary<string, List<ExRow>> index, string key, string method)
    {
        List<ExRow> rows;
        if (!index.TryGetValue(key, out rows)) return false;
        foreach (var row in rows)
        {
            if (row.Methods == null || row.Methods.Length == 0) return true;
            for (int i = 0; i < row.Methods.Length; i++)
            {
                if (string.Equals(row.Methods[i], method, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    static HashSet<string> Filter(HashSet<string> set, HashSet<string> allowed)
    {
        var copy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in set)
        {
            if (!allowed.Contains(id)) copy.Add(id);
        }
        return copy;
    }

    static bool SetEqual(HashSet<string> left, HashSet<string> right) { return left.SetEquals(right); }

    static void Add(List<string> diag, string name)
    {
        if (!diag.Contains(name)) diag.Add(name);
    }
}
'@
    Add-Type -TypeDefinition $code -Language CSharp
    $script:LiveLaneCensusEngineReady = $true
}

function ConvertTo-LiveLaneJsonToken {
    param([string]$Text)
    if ($null -eq $Text) { $Text = '' }
    return '"' + ($Text.Replace('\', '\\').Replace('"', '\"')) + '"'
}

function ConvertTo-LiveLaneJsonStringArray {
    param($Values)
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($value in @(ConvertTo-LiveLaneList $Values)) {
        $parts.Add((ConvertTo-LiveLaneJsonToken ([string]$value)))
    }
    return '[' + ($parts -join ',') + ']'
}

function ConvertTo-LiveLaneExclusionJson {
    param($Rows)
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($row in @(ConvertTo-LiveLaneList $Rows)) {
        $methods = New-Object System.Collections.Generic.List[string]
        foreach ($m in @(ConvertTo-LiveLaneList $row.Methods)) {
            $text = [string]$m
            if (-not [string]::IsNullOrWhiteSpace($text)) { $methods.Add($text) }
        }
        $items.Add('{"class":' + (ConvertTo-LiveLaneJsonToken ([string]$row.Class)) + ',"methods":' + (ConvertTo-LiveLaneJsonStringArray $methods) + ',"reason":' + (ConvertTo-LiveLaneJsonToken ([string]$row.Reason)) + ',"owner":' + (ConvertTo-LiveLaneJsonToken ([string]$row.Owner)) + '}')
    }
    return '[' + ($items -join ',') + ']'
}

function ConvertTo-LiveLaneIdentityJson {
    param($Rows)
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($row in @(ConvertTo-LiveLaneList $Rows)) {
        $class = [string](Get-LiveLaneProp $row 'class')
        if ([string]::IsNullOrWhiteSpace($class)) { $class = [string](Get-LiveLaneProp $row 'type') }
        $args = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $row 'arguments') | ForEach-Object { [string]$_ })
        $items.Add('{"project":' + (ConvertTo-LiveLaneJsonToken ([string](Get-LiveLaneProp $row 'project'))) + ',"namespace":' + (ConvertTo-LiveLaneJsonToken ([string](Get-LiveLaneProp $row 'namespace'))) + ',"class":' + (ConvertTo-LiveLaneJsonToken $class) + ',"method":' + (ConvertTo-LiveLaneJsonToken ([string](Get-LiveLaneProp $row 'method'))) + ',"arguments":' + (ConvertTo-LiveLaneJsonStringArray $args) + '}')
    }
    return '[' + ($items -join ',') + ']'
}

function Get-LiveLaneCachedExclusions {
    param([string]$PolicyPath, [string]$Platform)
    if ([string]::IsNullOrWhiteSpace($PolicyPath) -or -not (Test-Path -LiteralPath $PolicyPath)) { throw 'policy-missing' }
    $item = Get-Item -LiteralPath $PolicyPath
    $key = $item.FullName + '|' + $item.Length + '|' + $item.LastWriteTimeUtc.Ticks + '|' + $Platform
    if ($null -eq $script:LiveLaneExclusionCache) { $script:LiveLaneExclusionCache = @{} }
    if ($script:LiveLaneExclusionCache.ContainsKey($key)) { return @($script:LiveLaneExclusionCache[$key]) }
    $obj = [System.IO.File]::ReadAllText($PolicyPath) | ConvertFrom-Json
    $rows = @(Get-LiveLaneExclusionsFromObject $obj $Platform)
    $script:LiveLaneExclusionCache[$key] = $rows
    return $rows
}

function Get-LiveLaneCensusIdentityPayload {
    param([string]$RepoRoot)
    $path = Join-Path $RepoRoot 'tests/live-lanes.json'
    $item = Get-Item -LiteralPath $path
    $key = $item.FullName + '|' + $item.Length + '|' + $item.LastWriteTimeUtc.Ticks
    if ($script:LiveLaneIdentityPayloadKey -eq $key -and $null -ne $script:LiveLaneIdentityPayload) { return $script:LiveLaneIdentityPayload }
    $manifest = Read-LiveLaneManifestObject $RepoRoot
    $payload = @{
        Additive = (ConvertTo-LiveLaneIdentityJson (Get-LiveLaneProp $manifest 'additiveContract'))
        Contract = (ConvertTo-LiveLaneIdentityJson (Get-LiveLaneContract))
    }
    $script:LiveLaneIdentityPayloadKey = $key
    $script:LiveLaneIdentityPayload = $payload
    return $payload
}

function Get-LiveLaneCensusSeedMaterial {
    param([string]$RepoRoot, [string]$Seed, [string]$FixtureRel, [string]$Pre)
    $blob = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', ($Seed + ':' + $FixtureRel))).Text
    if ([string]::IsNullOrWhiteSpace($blob)) { return $null }
    $protectedKey = ''
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        $protectedKey += '|' + (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', ($Seed + ':' + $rel))).Text
    }
    $key = $blob + '|' + $FixtureRel + '|' + $Pre + $protectedKey
    if ($null -eq $script:LiveLaneCensusSeed) { $script:LiveLaneCensusSeed = @{} }
    if ($script:LiveLaneCensusSeed.ContainsKey($key)) { return $script:LiveLaneCensusSeed[$key] }
    $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('antiphon-live-lane-' + $PID)
    $before = Join-Path $scratch ('before-' + $blob.Substring(0, 12))
    New-Item -ItemType Directory -Path $before -Force | Out-Null
    $fixtureCopy = Join-Path $before $FixtureRel
    $ok = $true
    try { Copy-LiveLaneGitBlob -RepoRoot $RepoRoot -Spec ($Seed + ':' + $FixtureRel) -Dest $fixtureCopy } catch { $ok = $false }
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        try { Copy-LiveLaneGitBlob -RepoRoot $RepoRoot -Spec ($Seed + ':' + $rel) -Dest (Join-Path $before $rel) } catch { $ok = $false }
    }
    if (-not $ok -or -not (Test-Path -LiteralPath $fixtureCopy)) { return $null }
    $fixture = [System.IO.File]::ReadAllText($fixtureCopy) | ConvertFrom-Json
    $parent = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', ($Seed + '^'))).Text
    $recorded = [string](Get-LiveLaneProp (Get-LiveLaneProp $fixture 'producing') 'sourceSha')
    $names = @((Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('log', '--name-only', '--pretty=format:', ($Pre + '..' + $Seed))).Text -split "`n")
    $touched = @($names | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    $equal = @{}
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        $seedBlob = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', ($Seed + ':' + $rel))).Text
        $preBlob = (Get-LiveLaneGit -RepoRoot $RepoRoot -GitArgs @('rev-parse', ($Pre + ':' + $rel))).Text
        $equal[$rel] = ($seedBlob -eq $preBlob)
    }
    $fileHash = @{}
    foreach ($rel in @($script:LiveLaneProtectedPaths)) {
        $fileHash[$rel] = (Get-FileHash -LiteralPath (Join-Path $before $rel) -Algorithm SHA256).Hash
    }
    Initialize-LiveLaneNightly -RepoRoot $RepoRoot
    $beforePolicy = [System.IO.File]::ReadAllText((Join-Path $before 'tests/test-execution-policy.json')) | ConvertFrom-Json
    $platform = $(if ($IsLinux) { 'linux' } else { 'windows' })
    $beforeEx = @(Get-LiveLaneExclusionsFromObject $beforePolicy $platform)
    $entry = @{
        Key = $key
        Blob = $blob
        Fixture = $fixture
        FixtureCopy = $fixtureCopy
        BeforeDir = $before
        Parent = $parent
        Recorded = $recorded
        Touched = @($touched)
        ProtectedEqual = $equal
        FileHash = $fileHash
        BeforeEx = $beforeEx
        BeforeExJson = (ConvertTo-LiveLaneExclusionJson $beforeEx)
    }
    $script:LiveLaneCensusSeed[$key] = $entry
    return $entry
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
    $material = $null
    if (-not [string]::IsNullOrWhiteSpace($blob)) {
        $material = Get-LiveLaneCensusSeedMaterial -RepoRoot $repo -Seed $seed -FixtureRel $fixtureRel -Pre $pre
    }
    if ($null -eq $material) {
        $diagnostics.Add('seed-object-missing')
        return @{ Accepted = $false; Diagnostics = @($diagnostics | Select-Object -Unique) }
    }
    $before = $material.BeforeDir
    $fixture = $material.Fixture
    $parent = (Get-LiveLaneGit -RepoRoot $repo -GitArgs @('rev-parse', ($seed + '^'))).Text
    $recorded = $material.Recorded
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
        $drift = $false
        if ($Mutant -and $Mutant.ContainsKey('driftPath') -and $Mutant.driftPath -eq $rel) { $drift = $true }
        $equal = $true
        if ($material.ProtectedEqual.ContainsKey($rel)) { $equal = [bool]$material.ProtectedEqual[$rel] }
        if ($drift -or -not $equal) { $diagnostics.Add('baseline-drift') }
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
            $mutateExport = $Mutant -and ($Mutant.ContainsKey('exportSource') -or $Mutant.ContainsKey('exportRun') -or $Mutant.ContainsKey('exportHash') -or $Mutant.ContainsKey('terminal'))
            if ($mutateExport) { $export = Copy-LiveLaneExportShell $export }
            if ($Mutant -and $Mutant.ContainsKey('exportSource')) { $export | Add-Member -NotePropertyName sourceSha -NotePropertyValue $Mutant.exportSource -Force }
            if ($Mutant -and $Mutant.ContainsKey('exportRun')) { $export | Add-Member -NotePropertyName runId -NotePropertyValue $Mutant.exportRun -Force }
            if ($Mutant -and $Mutant.ContainsKey('exportHash')) { $export | Add-Member -NotePropertyName assemblyHash -NotePropertyValue $Mutant.exportHash -Force }
            if ($Mutant -and $Mutant.ContainsKey('terminal')) { $export | Add-Member -NotePropertyName terminal -NotePropertyValue ([bool]$Mutant.terminal) -Force }
            $check = Test-LiveLaneExportDocument -Export $export -ExpectedSource $source -ExpectedRun $run -ExpectedHash $expectedHash -ExpectedProject ([string](Get-LiveLaneProp $export 'project')) -RequireCore $false -AssemblyPath ([string](Get-LiveLaneProp $export 'assemblyPath'))
            if (-not $check.Accepted -and $diagnostics -notcontains $check.Reason) { $diagnostics.Add($check.Reason) }
        }
    }
    $useNodeEngine = -not ($Mutant -and $Mutant.ContainsKey('afterNodes'))
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
        $afterFile = Join-Path $afterRoot $rel
        $h1 = ''
        if ($material.FileHash.ContainsKey($rel)) { $h1 = [string]$material.FileHash[$rel] }
        if ([string]::IsNullOrWhiteSpace($h1) -or -not (Test-Path -LiteralPath $afterFile)) { $diagnostics.Add('reader-input-changed'); continue }
        $h2 = (Get-FileHash -LiteralPath $afterFile -Algorithm SHA256).Hash
        if ($h1 -ne $h2) { $diagnostics.Add('reader-input-changed') }
    }
    Initialize-LiveLaneNightly -RepoRoot $repo
    $afterPolicyPath = Join-Path $afterRoot 'tests/test-execution-policy.json'
    if ($Mutant -and $Mutant.ContainsKey('policyPath')) { $afterPolicyPath = [string]$Mutant.policyPath }
    $platform = $(if ($IsLinux) { 'linux' } else { 'windows' })
    $beforeEx = @($material.BeforeEx)
    $afterEx = @(Get-LiveLaneCachedExclusions -PolicyPath $afterPolicyPath -Platform $platform)
    if ($useNodeEngine) {
        $payload = Get-LiveLaneCensusIdentityPayload -RepoRoot $repo
        $patch = ''
        if ($Mutant -and $Mutant.ContainsKey('patch')) { $patch = [string]$Mutant.patch }
        Initialize-LiveLaneCensusEngine
        $beforeFile = [string]$material.FixtureCopy
        $after1 = ''
        $after2 = ''
        if ($cp25.Found) { $after1 = $cp25Path }
        if ($cp1.Found) { $after2 = $cp1Path }
        $text = [LiveLaneCensusEngine]::Compare($beforeFile, $after1, $after2, [string]$material.BeforeExJson, (ConvertTo-LiveLaneExclusionJson $afterEx), [string]$payload.Additive, [string]$payload.Contract, $patch)
        foreach ($line in @(([string]$text) -split "`n")) {
            if (-not [string]::IsNullOrWhiteSpace($line)) { $diagnostics.Add($line) }
        }
        $reasonChanged = $false
        $ownerChanged = $false
        $beforeBy = @{}
        foreach ($row in @($beforeEx)) { $beforeBy[[string]$row.Class] = $row }
        foreach ($row in @($afterEx)) {
            $cls = [string]$row.Class
            if (-not $beforeBy.ContainsKey($cls)) { continue }
            if ([string]$beforeBy[$cls].Reason -ne [string]$row.Reason) { $reasonChanged = $true }
            if ([string]$beforeBy[$cls].Owner -ne [string]$row.Owner) { $ownerChanged = $true }
        }
        if ($reasonChanged) { $diagnostics.Add('disposition-reason-changed') }
        if ($ownerChanged) { $diagnostics.Add('disposition-owner-changed') }
    } else {
        $afterNodes = @()
        if ($cp25.Found) { $afterNodes += @(ConvertTo-LiveLaneList (Get-LiveLaneProp $cp25.Export 'nodes')) }
        if ($cp1.Found) { $afterNodes += @(ConvertTo-LiveLaneList (Get-LiveLaneProp $cp1.Export 'nodes')) }
        if ($Mutant -and $Mutant.ContainsKey('afterNodes')) { $afterNodes = @($Mutant.afterNodes) }
        $beforeNodes = @(ConvertTo-LiveLaneList (Get-LiveLaneProp $fixture 'nodes'))
        $additive = @(ConvertTo-LiveLaneList (Get-LiveLaneProp (Read-LiveLaneManifestObject $repo) 'additiveContract'))
        $member = Compare-LiveLaneMembership -BeforeNodes $beforeNodes -AfterNodes $afterNodes -BeforeEx $beforeEx -AfterEx $afterEx -Additive $additive
        foreach ($item in @($member)) { $diagnostics.Add($item) }
    }
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

    $dropCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ patch = 'drop-first'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $dropCase @('old-membership-changed'))) { $missing.Add('remove-old-uid') }

    $catCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ patch = 'remove-integration'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $catCase @('category-delta'))) { $missing.Add('remove-Integration') }

    $tagCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ patch = 'tag-seventh'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $tagCase @('category-delta'))) { $missing.Add('tag-seventh') }

    $extraCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ patch = 'extra-expansion'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $extraCase @('extra-expansion'))) { $missing.Add('extra-expansion') }

    $unknownCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ patch = 'unknown-node'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $unknownCase @('undeclared-new-uid'))) { $missing.Add('unknown-new-uid') }

    $nightCase = Invoke-LiveLaneCensus -Context $context -Mutant @{ patch = 'add-optin'; fastHash = $true }
    if (-not (Test-LiveLaneCensusHas $nightCase @('nightly-disposition-changed'))) { $missing.Add('default-disposition') }

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
