# CARD-1784. Pure planning for the Linux post-land early warning.
# ASCII. No ternary, no ??, no ?. Run mode stays in the entry script.
$script:PostLandLibRoot = $PSScriptRoot
$script:PostLandScriptsRoot = Split-Path -Parent $script:PostLandLibRoot
$script:PostLandProductRoot = Split-Path -Parent $script:PostLandScriptsRoot

Set-StrictMode -Version 2.0

# Tests set POST_LAND_WARNING_CULTURE so a desktop culture is in force. startedAt is read from the ISO text.
$script:PostLandCultureName = [Environment]::GetEnvironmentVariable('POST_LAND_WARNING_CULTURE')
if (-not [string]::IsNullOrWhiteSpace($script:PostLandCultureName)) {
    $script:PostLandCulture = [Globalization.CultureInfo]::GetCultureInfo($script:PostLandCultureName)
    [Threading.Thread]::CurrentThread.CurrentCulture = $script:PostLandCulture
    [Threading.Thread]::CurrentThread.CurrentUICulture = $script:PostLandCulture
}

function New-PostLandStringList {
    $list = New-Object System.Collections.Generic.List[string]
    return ,$list
}

function New-PostLandObjectList {
    $list = New-Object System.Collections.Generic.List[object]
    return ,$list
}

function Add-PostLandStrings($Target, $Value) {
    if ($null -eq $Value) { return }
    foreach ($item in @($Value)) {
        if ($null -eq $item) { continue }
        $text = [string]$item
        if (-not [string]::IsNullOrWhiteSpace($text)) { [void]$Target.Add($text) }
    }
}

function Invoke-PostLandProcess {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [string]$WorkingDirectory,
        [string[]]$Arguments
    )
    $proc = [Diagnostics.Process]::new()
    $proc.StartInfo.FileName = $FileName
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $proc.StartInfo.WorkingDirectory = $WorkingDirectory
    }
    $proc.StartInfo.UseShellExecute = $false
    $proc.StartInfo.RedirectStandardOutput = $true
    $proc.StartInfo.RedirectStandardError = $true
    $utf8 = [Text.UTF8Encoding]::new($false)
    $proc.StartInfo.StandardOutputEncoding = $utf8
    $proc.StartInfo.StandardErrorEncoding = $utf8
    if ($null -ne $Arguments) {
        foreach ($arg in $Arguments) { [void]$proc.StartInfo.ArgumentList.Add([string]$arg) }
    }
    if (-not $proc.Start()) { return [pscustomobject]@{ Exit = 1; Stdout = ''; Stderr = 'process did not start' } }
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    $proc.WaitForExit()
    return [pscustomobject]@{
        Exit = $proc.ExitCode
        Stdout = $outTask.GetAwaiter().GetResult()
        Stderr = $errTask.GetAwaiter().GetResult()
    }
}

function Invoke-PostLandGit {
    param([string]$Repo, [string[]]$Arguments)
    $gitArgs = @('-C', $Repo) + @($Arguments)
    return (Invoke-PostLandProcess -FileName 'git' -Arguments $gitArgs)
}

function Resolve-PostLandRange {
    param([string]$Range)
    if ([string]::IsNullOrWhiteSpace($Range) -or $Range.Contains('...')) {
        return [pscustomobject]@{ Ok = $false; Error = 'refused=bad-range'; Base = ''; Tip = '' }
    }
    $index = $Range.IndexOf('..')
    if ($index -lt 1) {
        return [pscustomobject]@{ Ok = $false; Error = 'refused=bad-range'; Base = ''; Tip = '' }
    }
    $base = $Range.Substring(0, $index)
    $tip = $Range.Substring($index + 2)
    if ([string]::IsNullOrWhiteSpace($base) -or [string]::IsNullOrWhiteSpace($tip) -or $tip.Contains('..')) {
        return [pscustomobject]@{ Ok = $false; Error = 'refused=bad-range'; Base = ''; Tip = '' }
    }
    return [pscustomobject]@{ Ok = $true; Error = ''; Base = $base; Tip = $tip }
}

function Resolve-PostLandSha {
    param([string]$Repo, [string]$Value)
    if ($Value -match '^[0-9a-fA-F]{40}$') { return $Value }
    $ran = Invoke-PostLandGit $Repo @('rev-parse', '--verify', $Value)
    if ($ran.Exit -ne 0) { return '' }
    return $ran.Stdout.Trim()
}

function Test-PostLandFullSha {
    param([string]$Value)
    return $Value -match '^[0-9a-fA-F]{40}$'
}

function Get-PostLandChangedPaths {
    param([string]$Repo, [string]$BaseSha, [string]$TipSha, [string]$FileList)
    $paths = New-PostLandStringList
    if (-not [string]::IsNullOrWhiteSpace($FileList)) {
        if (-not (Test-Path -LiteralPath $FileList)) {
            return [pscustomobject]@{ Ok = $false; Error = 'refused=bad-file-list'; Paths = $paths }
        }
        $text = [IO.File]::ReadAllText($FileList)
        foreach ($line in ($text -split "`r?`n")) {
            $trim = $line.Trim().Replace('\', '/')
            if (-not [string]::IsNullOrWhiteSpace($trim)) { [void]$paths.Add($trim) }
        }
        return [pscustomobject]@{ Ok = $true; Error = ''; Paths = $paths }
    }
    $ran = Invoke-PostLandGit $Repo @('diff', '--name-only', '--diff-filter=ACMRTD', $BaseSha, $TipSha)
    if ($ran.Exit -ne 0) {
        return [pscustomobject]@{ Ok = $false; Error = 'refused=git-diff'; Paths = $paths }
    }
    foreach ($line in ($ran.Stdout -split "`r?`n")) {
        $trim = $line.Trim().Replace('\', '/')
        if (-not [string]::IsNullOrWhiteSpace($trim)) { [void]$paths.Add($trim) }
    }
    return [pscustomobject]@{ Ok = $true; Error = ''; Paths = $paths }
}

function Get-PostLandPinnedDocMarks {
    param([string]$Repo)
    $marks = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $root = Join-Path $Repo 'tests'
    if (-not (Test-Path -LiteralPath $root)) { return $marks }
    $pattern = [regex]::new('docs/[A-Za-z0-9_./-]+\.md|[A-Za-z0-9_.-]+\.md', 'CultureInvariant')
    foreach ($file in [IO.Directory]::EnumerateFiles($root, '*.cs', [IO.SearchOption]::AllDirectories)) {
        $norm = ([string]$file).Replace('\', '/')
        if ($norm.Contains('/bin/') -or $norm.Contains('/obj/')) { continue }
        $text = [IO.File]::ReadAllText($file)
        foreach ($match in $pattern.Matches($text)) {
            $value = ([string]$match.Value).Replace('\', '/').Trim()
            if ([string]::IsNullOrWhiteSpace($value)) { continue }
            [void]$marks.Add($value)
            $name = [IO.Path]::GetFileName($value)
            if (-not [string]::IsNullOrWhiteSpace($name)) { [void]$marks.Add($name) }
        }
    }
    return ,$marks
}

function Test-PostLandDocsOnly {
    param([string]$Repo, $Paths)
    $count = 0
    $docs = New-PostLandStringList
    foreach ($path in @($Paths)) {
        if ($null -eq $path) { continue }
        $norm = ([string]$path).Replace('\', '/').Trim()
        if ($norm.StartsWith('./')) { $norm = $norm.Substring(2) }
        if ([string]::IsNullOrWhiteSpace($norm)) { continue }
        $count++
        $underDocs = $norm.StartsWith('docs/') -and $norm.EndsWith('.md')
        if (-not $underDocs) { return $false }
        [void]$docs.Add($norm)
    }
    if ($count -eq 0) { return $true }
    $pinned = Get-PostLandPinnedDocMarks $Repo
    foreach ($path in $docs) {
        $name = [IO.Path]::GetFileName($path)
        if ($pinned.Contains($path) -or $pinned.Contains($name)) { return $false }
    }
    return $true
}

function Test-PostLandPidAlive {
    param([int]$ProcessId)
    if ($ProcessId -le 0) { return $false }
    $procfs = '/proc/' + $ProcessId
    if (Test-Path -LiteralPath $procfs) { return $true }
    try {
        $proc = Get-Process -Id $ProcessId -ErrorAction Stop
        return $null -ne $proc
    }
    catch {
        return $false
    }
}

function Get-PostLandProcessStartUtc {
    param([int]$ProcessId)
    try {
        $proc = Get-Process -Id $ProcessId -ErrorAction Stop
    }
    catch {
        return $null
    }
    if ($null -eq $proc) { return $null }
    try {
        return $proc.StartTime.ToUniversalTime()
    }
    catch {
        return $null
    }
}

function Get-PostLandStartedAtToken {
    param([string]$Raw)
    if ([string]::IsNullOrWhiteSpace($Raw)) { return $null }
    $marker = '"startedAt":"'
    $at = $Raw.IndexOf($marker)
    if ($at -lt 0) { return $null }
    $start = $at + $marker.Length
    $end = $Raw.IndexOf('"', $start)
    if ($end -lt 0) { return $null }
    return $Raw.Substring($start, $end - $start)
}

function ConvertTo-PostLandUtc {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $parsed = [datetime]::MinValue
    $ok = [datetime]::TryParse(
        $Text,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$parsed)
    if (-not $ok) { return $null }
    if ($parsed.Kind -eq [DateTimeKind]::Unspecified) {
        return [datetime]::SpecifyKind($parsed, [DateTimeKind]::Utc)
    }
    return $parsed.ToUniversalTime()
}

function Test-PostLandPidReused {
    param([int]$ProcessId, [string]$StartedAtText)
    $lockStart = ConvertTo-PostLandUtc $StartedAtText
    if ($null -eq $lockStart) { return $false }
    $procStart = Get-PostLandProcessStartUtc $ProcessId
    if ($null -eq $procStart) { return $false }
    $limit = $lockStart.AddSeconds(2)
    if ($procStart -gt $limit) { return $true }
    return $false
}

function Get-PostLandLockKind {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return 'absent' }
    $raw = ''
    try {
        $raw = [IO.File]::ReadAllText($Path)
        $doc = $raw | ConvertFrom-Json
    }
    catch {
        return 'stale'
    }
    if ($null -eq $doc.PSObject.Properties['pid']) { return 'stale' }
    $processId = 0
    try { $processId = [int]$doc.pid } catch { return 'stale' }
    if ($processId -eq $PID) { return 'ours' }
    if (Test-PostLandPidAlive $processId) {
        if (Test-PostLandPidReused $processId (Get-PostLandStartedAtToken $raw)) { return 'stale' }
        return 'busy'
    }
    if ($null -eq $doc.PSObject.Properties['startedAt']) { return 'busy' }
    if ([string]::IsNullOrWhiteSpace([string]$doc.startedAt)) { return 'busy' }
    return 'stale'
}

function Test-PostLandLockBusy {
    param([string]$Path)
    $kind = Get-PostLandLockKind $Path
    if ($kind -eq 'busy') { return $true }
    return $false
}

function Enter-PostLandLock {
    param([string]$Path, [string]$RunId)
    $parentDir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parentDir)) {
        $null = [IO.Directory]::CreateDirectory($parentDir)
    }
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        $now = [datetime]::UtcNow.ToString('o')
        $body = '{"pid":' + [string]$PID + ',"startedAt":"' + $now + '","runId":"' + $RunId + '"}' + "`n"
        $temp = $Path + '.' + [string]$PID + '.' + [string]$attempt + '.tmp'
        [IO.File]::WriteAllText($temp, $body)
        try {
            [IO.File]::Move($temp, $Path)
            return $true
        }
        catch {
            if (Test-Path -LiteralPath $temp) {
                try { Remove-Item -LiteralPath $temp -Force } catch { }
            }
            $kind = 'absent'
            if (Test-Path -LiteralPath $Path) { $kind = Get-PostLandLockKind $Path }
            if ($kind -eq 'ours') { return $true }
            if ($kind -eq 'busy') { return $false }
            if ($kind -eq 'stale') {
                $delayText = [Environment]::GetEnvironmentVariable('POST_LAND_WARNING_RECLAIM_DELAY_MS')
                if (-not [string]::IsNullOrWhiteSpace($delayText)) {
                    $delayMs = 0
                    try { $delayMs = [int]$delayText } catch { $delayMs = 0 }
                    if ($delayMs -gt 0) { Start-Sleep -Milliseconds $delayMs }
                }
                $claim = $Path + '.' + [string]$PID + '.' + [string]$attempt + '.reclaim'
                $moved = $false
                try {
                    [IO.File]::Move($Path, $claim)
                    $moved = $true
                }
                catch {
                    $moved = $false
                }
                if (-not $moved) { continue }
                $claimedKind = Get-PostLandLockKind $claim
                if ($claimedKind -ne 'stale') {
                    try { [IO.File]::Move($claim, $Path) } catch { }
                    return $false
                }
                try { Remove-Item -LiteralPath $claim -Force } catch { }
                continue
            }
            return $false
        }
    }
    return $false
}

function Exit-PostLandLock {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    if ((Get-PostLandLockKind $Path) -ne 'ours') { return }
    try { Remove-Item -LiteralPath $Path -Force } catch { }
}

function Write-PostLandRunMarker {
    param([string]$Path, [string]$RunId, [string]$State)
    $parentDir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parentDir)) {
        $null = [IO.Directory]::CreateDirectory($parentDir)
    }
    $body = '{"runId":"' + $RunId + '","state":"' + $State + '"}' + "`n"
    [IO.File]::WriteAllText($Path, $body)
}

function Test-PostLandHourly {
    param([string]$Path, [int]$Minutes)
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $raw = ''
    try {
        $raw = [IO.File]::ReadAllText($Path)
    }
    catch {
        return $false
    }
    $started = ConvertTo-PostLandUtc (Get-PostLandStartedAtToken $raw)
    if ($null -eq $started) { return $false }
    $age = ([datetime]::UtcNow - $started).TotalMinutes
    if ($age -ge 0 -and $age -lt $Minutes) { return $true }
    return $false
}

function Test-PostLandCompleted {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    try {
        $doc = [IO.File]::ReadAllText($Path) | ConvertFrom-Json
    }
    catch {
        return $false
    }
    if ($null -eq $doc.PSObject.Properties['state']) { return $false }
    return ([string]$doc.state) -eq 'completed'
}

function Read-PostLandManifest {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try {
        $doc = [IO.File]::ReadAllText($Path) | ConvertFrom-Json
    }
    catch {
        return $null
    }
    if ($null -eq $doc.PSObject.Properties['schema']) { return $null }
    if ([int]$doc.schema -ne 1) { return $null }
    return $doc
}

function Get-PostLandClasses {
    param([string]$Repo)
    $found = New-PostLandObjectList
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $root = Join-Path $Repo 'tests/Antiphon.Tests'
    if (-not (Test-Path -LiteralPath $root)) { return $found }
    $classLine = [regex]::new('^public\s+(?:(?:sealed|static|partial|abstract)\s+)*class\s+([A-Za-z_][A-Za-z0-9_]*)\b', 'CultureInvariant')
    $fileNs = [regex]::new('^namespace\s+([A-Za-z0-9_.]+)\s*;', 'CultureInvariant')
    $blockNs = [regex]::new('^namespace\s+([A-Za-z0-9_.]+)\s*\{', 'CultureInvariant')
    $rawRun = [regex]::new('"{3,}', 'CultureInvariant')
    $files = [IO.Directory]::EnumerateFiles($root, '*.cs', [IO.SearchOption]::AllDirectories)
    foreach ($file in $files) {
        $relative = ([string]$file).Replace('\', '/')
        if ($relative.Contains('/bin/') -or $relative.Contains('/obj/')) { continue }
        $lines = [IO.File]::ReadAllLines($file)
        # A line inside or on the edge of a raw string literal is fixture text, not a declaration.
        $inRaw = New-Object 'bool[]' $lines.Length
        $open = $false
        for ($r = 0; $r -lt $lines.Length; $r++) {
            $runs = $rawRun.Matches($lines[$r]).Count
            if ($open -or $runs -gt 0) { $inRaw[$r] = $true }
            if (($runs % 2) -eq 1) { $open = -not $open }
        }
        $ns = ''
        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($inRaw[$i]) { continue }
            $trim = $lines[$i].Trim()
            $fileMatch = $fileNs.Match($trim)
            if ($fileMatch.Success) { $ns = $fileMatch.Groups[1].Value; continue }
            $blockMatch = $blockNs.Match($trim)
            if ($blockMatch.Success) { $ns = $blockMatch.Groups[1].Value; continue }
            $match = $classLine.Match($trim)
            if (-not $match.Success) { continue }
            $unit = $false
            $integration = $false
            for ($j = $i - 1; $j -ge 0; $j--) {
                if ($inRaw[$j]) { break }
                $earlier = $lines[$j].Trim()
                if ($earlier.Length -eq 0 -or $earlier.StartsWith('//')) { continue }
                if ($earlier.StartsWith('[')) {
                    if ($earlier.Contains('Category("Unit")')) { $unit = $true }
                    if ($earlier.Contains('Category("Integration")')) { $integration = $true }
                    continue
                }
                break
            }
            $hasTest = $false
            for ($k = $i + 1; $k -lt $lines.Length; $k++) {
                if ($inRaw[$k]) { continue }
                $body = $lines[$k].Trim()
                if ($classLine.IsMatch($body)) { break }
                if ($body.Contains('[Test]') -or $body.Contains('[Test,') -or $body.Contains('[TestAttribute')) {
                    $hasTest = $true
                    break
                }
            }
            if (-not $hasTest) { continue }
            $name = $match.Groups[1].Value
            $key = $ns + "`n" + $name
            if (-not $seen.Add($key)) { continue }
            [void]$found.Add([pscustomobject]@{
                Namespace = $ns
                Name = $name
                Unit = ($unit -and -not $integration)
                Integration = ($integration -and -not $unit)
            })
        }
    }
    return ,$found
}

function New-PostLandClassFilter {
    param([string[]]$Names)
    $parts = New-PostLandStringList
    foreach ($name in @($Names)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$name)) { [void]$parts.Add('(' + $name + '*)') }
    }
    if ($parts.Count -eq 0) { return '' }
    return '/*/*/' + ($parts -join '|') + '/*'
}

function New-PostLandShard {
    param(
        [string]$Id,
        [string]$Kind,
        [string]$Filter,
        [string]$Namespace,
        [string]$Letter,
        $ClassItems
    )
    $classes = New-PostLandObjectList
    if ($null -ne $ClassItems) {
    foreach ($item in $ClassItems) {
        if ($null -eq $item) { continue }
        [void]$classes.Add([ordered]@{
            name = [string]$item.Name
            namespace = [string]$item.Namespace
        })
    }
    }
    return [pscustomobject][ordered]@{
        id = $Id
        kind = $Kind
        filter = $Filter
        namespace = $Namespace
        letter = $Letter
        classes = $classes
        trx = $null
        executed = $null
        passed = $null
        failed = $null
        skipped = $null
        exit = $null
        slow = $false
    }
}

function Sort-PostLandNames {
    param($Names)
    $list = New-PostLandStringList
    Add-PostLandStrings $list $Names
    $array = $list.ToArray()
    [Array]::Sort($array, [StringComparer]::Ordinal)
    $sorted = New-PostLandStringList
    foreach ($name in $array) { [void]$sorted.Add($name) }
    return ,$sorted
}

function Format-PostLandSummary {
    param(
        [string]$Tip,
        [string]$Base,
        $NewReds,
        $UnknownShards,
        [int]$OwedCount,
        [int]$BuildExit = -1
    )
    $baseEnd = $Base
    $tipEnd = $Tip
    if ($Base.Length -ge 12) { $baseEnd = $Base.Substring(0, 12) }
    if ($Tip.Length -ge 12) { $tipEnd = $Tip.Substring(0, 12) }
    $batch = $baseEnd + '..' + $tipEnd
    $owed = 'owed-windows=' + $OwedCount
    $names = Sort-PostLandNames $NewReds
    $listed = ''
    if ($names.Count -gt 0) { $listed = ' ' + ($names -join ',') }
    $unknown = New-PostLandStringList
    Add-PostLandStrings $unknown $UnknownShards
    if ($BuildExit -gt 0) {
        return 'post-land tip=' + $Tip + ' batch=' + $batch + ' new-reds=unknown build=' + $BuildExit + ' ' + $owed
    }
    if ($unknown.Count -gt 0) {
        return 'post-land tip=' + $Tip + ' batch=' + $batch + ' new-reds=unknown no-trx=' + ($unknown -join ',') + $listed + ' ' + $owed
    }
    return 'post-land tip=' + $Tip + ' batch=' + $batch + ' new-reds=' + $names.Count + $listed + ' ' + $owed
}

function New-PostLandPayload {
    param(
        [string]$State,
        [string]$Reason,
        [string]$BaseSha,
        [string]$TipSha,
        $Shards,
        $Owed,
        $NewReds,
        $Failures,
        $Previous,
        [string]$Summary,
        [string]$ManifestPath,
        [bool]$Ok = $true
    )
    if ($null -eq $Shards) { $Shards = New-PostLandObjectList }
    if ($null -eq $Owed) { $Owed = New-PostLandObjectList }
    if ($null -eq $NewReds) { $NewReds = New-PostLandStringList }
    if ($null -eq $Failures) { $Failures = New-PostLandStringList }
    return [pscustomobject][ordered]@{
        ok = $Ok
        state = $State
        reason = $Reason
        tipSha = $TipSha
        baseSha = $BaseSha
        batch = ($BaseSha + '..' + $TipSha)
        lane = 1
        serial = $true
        slot = $false
        shards = $Shards
        owedWindows = $Owed
        newReds = $NewReds
        failures = $Failures
        previousTipSha = $Previous
        summaryLine = $Summary
        manifest = $ManifestPath
        buildExit = $null
        slowShards = (New-PostLandStringList)
        startedAt = $null
        finishedAt = $null
    }
}

function Invoke-PostLandSelector {
    param(
        [string]$Repo,
        [string]$BaseSha,
        [string]$TipSha,
        [string]$FileList,
        [string]$Table
    )
    $selector = Join-Path $script:PostLandScriptsRoot 'impacted-classes.ps1'
    $arguments = @('-NoProfile', '-NonInteractive', '-File', $selector, '-Json', '-Repo', $Repo)
    if (-not [string]::IsNullOrWhiteSpace($FileList)) {
        $arguments += @('-FileList', $FileList)
    }
    else {
        $arguments += @('-Range', ($BaseSha + '..' + $TipSha))
    }
    if (-not [string]::IsNullOrWhiteSpace($Table)) {
        $arguments += @('-Table', $Table)
    }
    $ran = Invoke-PostLandProcess -FileName 'pwsh' -WorkingDirectory $Repo -Arguments $arguments
    if ($ran.Exit -ne 0) {
        $message = $ran.Stderr.Trim()
        if ([string]::IsNullOrWhiteSpace($message)) { $message = 'selector failed' }
        return [pscustomobject]@{ Ok = $false; Error = $message; Classes = (New-PostLandStringList); Windows = (New-PostLandObjectList) }
    }
    $body = $ran.Stdout.Trim()
    $start = $body.IndexOf('{')
    $end = $body.LastIndexOf('}')
    if ($start -lt 0 -or $end -le $start) {
        return [pscustomobject]@{ Ok = $false; Error = 'selector returned no JSON'; Classes = (New-PostLandStringList); Windows = (New-PostLandObjectList) }
    }
    try {
        $doc = $body.Substring($start, $end - $start + 1) | ConvertFrom-Json
    }
    catch {
        return [pscustomobject]@{ Ok = $false; Error = 'selector JSON was not readable'; Classes = (New-PostLandStringList); Windows = (New-PostLandObjectList) }
    }
    $classes = New-PostLandStringList
    if ($null -ne $doc.PSObject.Properties['classes']) { Add-PostLandStrings $classes $doc.classes }
    $windows = New-PostLandObjectList
    if ($null -ne $doc.PSObject.Properties['windowsFilters'] -and $null -ne $doc.windowsFilters) {
        foreach ($row in @($doc.windowsFilters)) {
            if ($null -ne $row) { [void]$windows.Add($row) }
        }
    }
    return [pscustomobject]@{ Ok = $true; Error = ''; Classes = $classes; Windows = $windows }
}

function Get-PostLandWindowNames {
    param($Windows)
    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $pattern = [regex]::new('\(([A-Za-z_][A-Za-z0-9_]*)\*\)', 'CultureInvariant')
    if ($null -ne $Windows) {
    foreach ($row in $Windows) {
        if ($null -eq $row) { continue }
        $filter = ''
        if ($null -ne $row.PSObject.Properties['treenodeFilter']) { $filter = [string]$row.treenodeFilter }
        foreach ($match in $pattern.Matches($filter)) { [void]$names.Add($match.Groups[1].Value) }
    }
    }
    return ,$names
}

function New-PostLandOwedWindows {
    param($Windows)
    $owed = New-PostLandObjectList
    if ($null -ne $Windows) {
    foreach ($row in $Windows) {
        if ($null -eq $row) { continue }
        $filter = ''
        if ($null -ne $row.PSObject.Properties['treenodeFilter']) { $filter = [string]$row.treenodeFilter }
        if ([string]::IsNullOrWhiteSpace($filter)) { continue }
        [void]$owed.Add([ordered]@{
            filter = $filter
            passed = $false
        })
    }
    }
    return ,$owed
}

function Add-PostLandUnitRun {
    param($Shards, $Manifest, $Checkpoints, $Mode, $Key, $Namespace, $Items)
    if ($null -eq $Items -or $Items.Count -eq 0) { return }
    if ($Mode -eq 'cp') {
        $filter = ([string]$Manifest.checkpointsFilter).Replace('<Letter>', $Key)
        [void]$Shards.Add((New-PostLandShard -Id ('unit-cp-' + $Key) -Kind 'unit' -Filter $filter -Namespace $Checkpoints -Letter $Key -ClassItems $Items))
        return
    }
    $filter = ([string]$Manifest.nonCheckpointsFilter).Replace('<namespace>', $Namespace)
    [void]$Shards.Add((New-PostLandShard -Id ('unit-ns-' + $Namespace) -Kind 'unit' -Filter $filter -Namespace $Namespace -Letter $null -ClassItems $Items))
}

function Build-PostLandShards {
    param($Manifest, $Classes, $SelectorClasses, $WindowNames)
    $shards = New-PostLandObjectList
    $checkpoints = [string]$Manifest.checkpointsNamespace
    # One shard per namespace, and one per Checkpoints initial letter.
    # File-walk order repeats a letter and would duplicate the receipt row.
    $byKey = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    if ($null -ne $Classes) {
        foreach ($class in $Classes) {
            if ($null -eq $class -or -not $class.Unit) { continue }
            $ns = [string]$class.Namespace
            $name = [string]$class.Name
            $mode = 'ns'
            $key = 'ns:' + $ns
            $letter = ''
            if ($ns -eq $checkpoints -and $name.Length -gt 0) {
                $mode = 'cp'
                $letter = $name.Substring(0, 1)
                $key = 'cp:' + $letter
            }
            if (-not $byKey.ContainsKey($key)) {
                $items = New-Object System.Collections.Generic.List[object]
                $byKey[$key] = [pscustomobject]@{ Mode = $mode; Namespace = $ns; Letter = $letter; Items = $items }
            }
            [void]$byKey[$key].Items.Add($class)
        }
    }
    $sortedKeys = New-PostLandStringList
    foreach ($key in $byKey.Keys) { [void]$sortedKeys.Add($key) }
    $keyArray = $sortedKeys.ToArray()
    [Array]::Sort($keyArray, [StringComparer]::Ordinal)
    foreach ($key in $keyArray) {
        $bucket = $byKey[$key]
        if ($bucket.Mode -eq 'cp') {
            Add-PostLandUnitRun $shards $Manifest $checkpoints 'cp' $bucket.Letter $checkpoints $bucket.Items
        }
        else {
            Add-PostLandUnitRun $shards $Manifest $checkpoints 'ns' $bucket.Namespace $bucket.Namespace $bucket.Items
        }
    }
    $selected = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    if ($null -ne $SelectorClasses) {
        foreach ($name in $SelectorClasses) {
            if (-not [string]::IsNullOrWhiteSpace([string]$name)) { [void]$selected.Add([string]$name) }
        }
    }
    $integration = New-PostLandObjectList
    if ($null -ne $Classes) {
    foreach ($class in $Classes) {
        if ($null -eq $class -or -not $class.Integration) { continue }
        if (-not $selected.Contains([string]$class.Name)) { continue }
        if ($WindowNames.Contains([string]$class.Name)) { continue }
        [void]$integration.Add($class)
    }
    }
    $ordered = @($integration | Sort-Object Name, Namespace)
    $max = 8
    if ($null -ne $Manifest.PSObject.Properties['maxIntegrationClassesPerShard']) {
        $max = [int]$Manifest.maxIntegrationClassesPerShard
    }
    if ($max -lt 1) { $max = 8 }
    $shardNumber = 0
    $orderedCount = 0
    if ($null -ne $ordered) { $orderedCount = @($ordered).Count }
    for ($index = 0; $index -lt $orderedCount; $index += $max) {
        $chunk = New-PostLandObjectList
        $names = New-PostLandStringList
        $end = [Math]::Min($index + $max, $orderedCount)
        for ($i = $index; $i -lt $end; $i++) {
            [void]$chunk.Add($ordered[$i])
            [void]$names.Add([string]$ordered[$i].Name)
        }
        $shardNumber++
        $filter = New-PostLandClassFilter $names.ToArray()
        [void]$shards.Add((New-PostLandShard -Id ('integration-' + $shardNumber) -Kind 'integration' -Filter $filter -Namespace $null -Letter $null -ClassItems $chunk))
    }
    return ,$shards
}

function Read-PostLandTrx {
    param([string]$Path)
    $failures = New-PostLandStringList
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $executed = 0
    $passed = 0
    $failed = 0
    $skipped = 0
    [xml]$doc = [IO.File]::ReadAllText($Path)
    $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $tests = $doc.SelectNodes('//t:UnitTest', $ns)
    if ($null -eq $tests -or $tests.Count -eq 0) { $tests = $doc.SelectNodes('//UnitTest') }
    $byId = @{}
    foreach ($unit in @($tests)) {
        if ($null -eq $unit) { continue }
        $method = $unit.SelectSingleNode('./t:TestMethod', $ns)
        if ($null -eq $method) { $method = $unit.SelectSingleNode('./TestMethod') }
        if ($null -eq $method) { continue }
        $byId[[string]$unit.id] = [pscustomobject]@{ Class = [string]$method.className; Method = [string]$method.name }
    }
    $results = $doc.SelectNodes('//t:UnitTestResult', $ns)
    if ($null -eq $results -or $results.Count -eq 0) { $results = $doc.SelectNodes('//UnitTestResult') }
    foreach ($result in @($results)) {
        if ($null -eq $result) { continue }
        $executed++
        $outcome = [string]$result.outcome
        if ($outcome -eq 'Passed') { $passed++ }
        elseif ($outcome -eq 'Failed' -or $outcome -eq 'Error' -or $outcome -eq 'Timeout' -or $outcome -eq 'Aborted') {
            $failed++
            $id = [string]$result.testId
            if ($byId.ContainsKey($id)) {
                $info = $byId[$id]
                $className = [string]$info.Class
                $short = $className
                $dot = $className.LastIndexOf('.')
                if ($dot -ge 0 -and $dot -lt ($className.Length - 1)) { $short = $className.Substring($dot + 1) }
                $label = $short + '.' + [string]$info.Method
                if ($seen.Add($label)) { [void]$failures.Add($label) }
            }
        }
        elseif ($outcome -eq 'NotExecuted' -or $outcome -eq 'Skipped') { $skipped++ }
    }
    return [pscustomobject]@{ Failures = $failures; Executed = $executed; Passed = $passed; Failed = $failed; Skipped = $skipped }
}

function Get-PostLandReceiptFailures {
    param($Doc)
    $list = New-PostLandStringList
    if ($null -ne $Doc.PSObject.Properties['failures']) { Add-PostLandStrings $list $Doc.failures }
    elseif ($null -ne $Doc.PSObject.Properties['newReds']) { Add-PostLandStrings $list $Doc.newReds }
    return ,$list
}

function Test-PostLandAncestor {
    param([string]$Repo, [string]$Ancestor, [string]$Tip)
    if (-not (Test-Path -LiteralPath (Join-Path $Repo '.git'))) { return $false }
    $ran = Invoke-PostLandGit $Repo @('merge-base', '--is-ancestor', $Ancestor, $Tip)
    return $ran.Exit -eq 0
}

function Get-PostLandCommitDate {
    param([string]$Repo, [string]$Sha)
    $ran = Invoke-PostLandGit $Repo @('log', '-1', '--format=%cI', $Sha)
    if ($ran.Exit -ne 0) { return '' }
    return $ran.Stdout.Trim()
}

function Get-PostLandPrevious {
    param([string]$Repo, [string]$ReceiptRoot, [string]$Tip)
    $bestSha = $null
    $bestDate = ''
    $bestFailures = New-PostLandStringList
    if (-not (Test-Path -LiteralPath $ReceiptRoot)) {
        return [pscustomobject]@{ Tip = $null; Failures = $bestFailures }
    }
    foreach ($file in [IO.Directory]::EnumerateFiles($ReceiptRoot, '*.json')) {
        $name = [IO.Path]::GetFileNameWithoutExtension($file)
        if ($name -notmatch '^[0-9a-fA-F]{40}$') { continue }
        if ($name -eq $Tip) { continue }
        try { $doc = [IO.File]::ReadAllText($file) | ConvertFrom-Json } catch { continue }
        if ($null -eq $doc.PSObject.Properties['state']) { continue }
        if (([string]$doc.state) -ne 'completed') { continue }
        if (-not (Test-PostLandAncestor $Repo $name $Tip)) { continue }
        $date = Get-PostLandCommitDate $Repo $name
        if ($null -eq $bestSha -or $date -gt $bestDate) {
            $bestSha = $name
            $bestDate = $date
            $bestFailures = Get-PostLandReceiptFailures $doc
        }
    }
    return [pscustomobject]@{ Tip = $bestSha; Failures = $bestFailures }
}

function Complete-PostLandOutcome {
    param($Payload, [string]$Repo, [string]$ReceiptRoot)
    $tipDir = Join-Path $ReceiptRoot ([string]$Payload.tipSha)
    $expect = Test-Path -LiteralPath $tipDir -PathType Container
    $allFailures = New-PostLandStringList
    $unknown = New-PostLandStringList
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $ancestor = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $previous = $null
    if ($expect) {
        $prev = Get-PostLandPrevious $Repo $ReceiptRoot ([string]$Payload.tipSha)
        $previous = $prev.Tip
        if ($null -ne $prev.Failures) {
            foreach ($name in $prev.Failures) {
                if (-not [string]::IsNullOrWhiteSpace([string]$name)) { [void]$ancestor.Add([string]$name) }
            }
        }
        foreach ($shard in $Payload.shards) {
            if ($null -eq $shard) { continue }
            $trxPath = Join-Path $tipDir ([string]$shard.id + '.trx')
            $shard.trx = $trxPath
            if (-not (Test-Path -LiteralPath $trxPath)) {
                [void]$unknown.Add([string]$shard.id)
                continue
            }
            $parsed = Read-PostLandTrx $trxPath
            $shard.executed = $parsed.Executed
            $shard.passed = $parsed.Passed
            $shard.failed = $parsed.Failed
            $shard.skipped = $parsed.Skipped
            if ($parsed.Executed -eq 0) {
                [void]$unknown.Add([string]$shard.id)
                continue
            }
            if ($null -ne $parsed.Failures) {
                foreach ($name in $parsed.Failures) {
                    $label = [string]$name
                    if ([string]::IsNullOrWhiteSpace($label)) { continue }
                    if ($seen.Contains($label)) { continue }
                    [void]$seen.Add($label)
                    [void]$allFailures.Add($label)
                }
            }
        }
    }
    $newReds = New-PostLandStringList
    foreach ($name in $allFailures) {
        if ($ancestor.Contains([string]$name)) { continue }
        [void]$newReds.Add([string]$name)
    }
    $sortedNew = Sort-PostLandNames $newReds
    $sortedAll = Sort-PostLandNames $allFailures
    $owedCount = 0
    if ($null -ne $Payload.owedWindows) { $owedCount = $Payload.owedWindows.Count }
    $Payload.newReds = $sortedNew
    $Payload.failures = $sortedAll
    $Payload.previousTipSha = $previous
    $Payload.summaryLine = Format-PostLandSummary $Payload.tipSha $Payload.baseSha $sortedNew $unknown $owedCount
    $exit = 0
    if ($unknown.Count -gt 0 -or $sortedNew.Count -gt 0) { $exit = 1 }
    if ($Payload.state -ne 'run') { $exit = 0 }
    return $exit
}

function Get-PostLandDecision {
    param(
        [string]$Repo,
        [string]$ReceiptRoot,
        [string]$BaseSha,
        [string]$TipSha,
        [string]$FileList,
        [string]$Table,
        [bool]$Force
    )
    $manifestPath = Join-Path $script:PostLandProductRoot 'tests/post-land-unit-shards.json'
    $emptyShards = New-PostLandObjectList
    $changed = Get-PostLandChangedPaths $Repo $BaseSha $TipSha $FileList
    if (-not $changed.Ok) {
        $summary = Format-PostLandSummary $TipSha $BaseSha @() @() 0
        $payload = New-PostLandPayload -State 'refused' -Reason $changed.Error -BaseSha $BaseSha -TipSha $TipSha -Shards $emptyShards -Summary $summary -ManifestPath $manifestPath -Ok $false
        return [pscustomobject]@{ Payload = $payload; Exit = 2 }
    }
    if (Test-PostLandDocsOnly $Repo $changed.Paths) {
        $summary = Format-PostLandSummary $TipSha $BaseSha @() @() 0
        $payload = New-PostLandPayload -State 'skipped-docs' -Reason 'docs-only' -BaseSha $BaseSha -TipSha $TipSha -Shards $emptyShards -Summary $summary -ManifestPath $manifestPath
        return [pscustomobject]@{ Payload = $payload; Exit = 0 }
    }
    $manifest = Read-PostLandManifest $manifestPath
    if ($null -eq $manifest) {
        $summary = Format-PostLandSummary $TipSha $BaseSha @() @() 0
        $payload = New-PostLandPayload -State 'refused' -Reason 'refused=bad-manifest' -BaseSha $BaseSha -TipSha $TipSha -Shards $emptyShards -Summary $summary -ManifestPath $manifestPath -Ok $false
        return [pscustomobject]@{ Payload = $payload; Exit = 2 }
    }
    if (Test-PostLandLockBusy (Join-Path $ReceiptRoot 'lock.json')) {
        $summary = Format-PostLandSummary $TipSha $BaseSha @() @() 0
        $payload = New-PostLandPayload -State 'skipped-busy' -Reason 'lock-held' -BaseSha $BaseSha -TipSha $TipSha -Shards $emptyShards -Summary $summary -ManifestPath $manifestPath
        return [pscustomobject]@{ Payload = $payload; Exit = 0 }
    }
    $minutes = [int]$manifest.coalesceMinutes
    if (-not $Force -and (Test-PostLandHourly (Join-Path $ReceiptRoot 'last-start.json') $minutes)) {
        $summary = Format-PostLandSummary $TipSha $BaseSha @() @() 0
        $payload = New-PostLandPayload -State 'skipped-hourly' -Reason 'within-60-minutes' -BaseSha $BaseSha -TipSha $TipSha -Shards $emptyShards -Summary $summary -ManifestPath $manifestPath
        return [pscustomobject]@{ Payload = $payload; Exit = 0 }
    }
    if (-not $Force -and (Test-PostLandCompleted (Join-Path $ReceiptRoot ($TipSha + '.json')))) {
        $summary = Format-PostLandSummary $TipSha $BaseSha @() @() 0
        $payload = New-PostLandPayload -State 'skipped-completed' -Reason 'already-completed' -BaseSha $BaseSha -TipSha $TipSha -Shards $emptyShards -Summary $summary -ManifestPath $manifestPath
        return [pscustomobject]@{ Payload = $payload; Exit = 0 }
    }
    $delayText = [Environment]::GetEnvironmentVariable('POST_LAND_WARNING_PLAN_DELAY_MS')
    if (-not [string]::IsNullOrWhiteSpace($delayText)) {
        $delayMs = 0
        try { $delayMs = [int]$delayText } catch { $delayMs = 0 }
        if ($delayMs -gt 0) { Start-Sleep -Milliseconds $delayMs }
    }
    $selector = Invoke-PostLandSelector $Repo $BaseSha $TipSha $FileList $Table
    if (-not $selector.Ok) {
        [Console]::Error.WriteLine($selector.Error)
        $summary = Format-PostLandSummary $TipSha $BaseSha @() @() 0
        $payload = New-PostLandPayload -State 'refused' -Reason 'refused=selector' -BaseSha $BaseSha -TipSha $TipSha -Shards $emptyShards -Summary $summary -ManifestPath $manifestPath -Ok $false
        return [pscustomobject]@{ Payload = $payload; Exit = 2 }
    }
    $classes = Get-PostLandClasses $Repo
    $windowNames = Get-PostLandWindowNames $selector.Windows
    $owed = New-PostLandOwedWindows $selector.Windows
    $shards = Build-PostLandShards $manifest $classes $selector.Classes $windowNames
    $owedCount = 0
    if ($null -ne $owed) { $owedCount = $owed.Count }
    $summary = Format-PostLandSummary $TipSha $BaseSha @() @() $owedCount
    $payload = New-PostLandPayload -State 'run' -Reason 'planned' -BaseSha $BaseSha -TipSha $TipSha -Shards $shards -Owed $owed -Summary $summary -ManifestPath $manifestPath
    $exit = Complete-PostLandOutcome $payload $Repo $ReceiptRoot
    return [pscustomobject]@{ Payload = $payload; Exit = $exit }
}

function Write-PostLandJson {
    param($Payload)
    $json = $Payload | ConvertTo-Json -Depth 12
    [Console]::Out.WriteLine($json)
}

function Write-PostLandReceipt {
    param([string]$ReceiptRoot, $Payload)
    $null = [IO.Directory]::CreateDirectory($ReceiptRoot)
    $path = Join-Path $ReceiptRoot ([string]$Payload.tipSha + '.json')
    if (Test-Path -LiteralPath $path) {
        try {
            $existing = [IO.File]::ReadAllText($path) | ConvertFrom-Json
            if ($null -ne $existing.PSObject.Properties['state'] -and ([string]$existing.state) -eq 'completed' -and ([string]$Payload.state) -ne 'completed') {
                return
            }
        }
        catch { }
    }
    $json = $Payload | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($path, $json + "`n")
}

# POST_LAND_WARNING_SLOT_SCRIPT, when set, replaces scripts/build-slot.ps1.
# The lane still invokes that path as pwsh -File <path> -Label post-land-early-warning -- <command>.
# Command stdout and stderr go to LogPath (or the host when LogPath is empty) and the function returns only the exit code.
# POST_LAND_WARNING_RECLAIM_DELAY_MS sleeps after a stale classification and before the reclaim rename.
function Invoke-PostLandSlot {
    param([string[]]$Command, [string]$LogPath)
    $shim = [Environment]::GetEnvironmentVariable('POST_LAND_WARNING_SLOT_SHIM')
    if (-not [string]::IsNullOrWhiteSpace($shim)) {
        $parent = Split-Path -Parent $shim
        if (-not [string]::IsNullOrWhiteSpace($parent)) { $null = [IO.Directory]::CreateDirectory($parent) }
        $joined = @($Command) -join ' '
        [IO.File]::AppendAllText($shim, $joined + "`n")
        $delayText = [Environment]::GetEnvironmentVariable('POST_LAND_WARNING_SLOT_DELAY_MS')
        if (-not [string]::IsNullOrWhiteSpace($delayText)) {
            $delayMs = 0
            try { $delayMs = [int]$delayText } catch { $delayMs = 0 }
            if ($delayMs -gt 0) { Start-Sleep -Milliseconds $delayMs }
        }
        $kind = 'build'
        $fileName = ''
        $results = ''
        $args = @($Command)
        for ($i = 0; $i -lt $args.Count; $i++) {
            if ($args[$i] -eq '--no-build') { $kind = 'shard' }
            if ($args[$i] -eq '--report-trx-filename' -and ($i + 1) -lt $args.Count) { $fileName = [string]$args[$i + 1] }
            if ($args[$i] -eq '--results-directory' -and ($i + 1) -lt $args.Count) { $results = [string]$args[$i + 1] }
        }
        $trxSource = [Environment]::GetEnvironmentVariable('POST_LAND_WARNING_SLOT_TRX')
        if ($kind -eq 'shard' -and -not [string]::IsNullOrWhiteSpace($trxSource) -and -not [string]::IsNullOrWhiteSpace($fileName) -and -not [string]::IsNullOrWhiteSpace($results) -and (Test-Path -LiteralPath $trxSource)) {
            $null = [IO.Directory]::CreateDirectory($results)
            $copied = Join-Path $results $fileName
            [IO.File]::Copy($trxSource, $copied, $true)
            [IO.File]::SetLastWriteTimeUtc($copied, [datetime]::UtcNow)
        }
        $exitName = 'POST_LAND_WARNING_SLOT_BUILD_EXIT'
        if ($kind -eq 'shard') { $exitName = 'POST_LAND_WARNING_SLOT_SHARD_EXIT' }
        $exitText = [Environment]::GetEnvironmentVariable($exitName)
        if ([string]::IsNullOrWhiteSpace($exitText)) { return 0 }
        try { return [int]$exitText } catch { return 1 }
    }
    $saved = $null
    $hasNative = Test-Path variable:PSNativeCommandUseErrorActionPreference
    if ($hasNative) {
        $saved = $PSNativeCommandUseErrorActionPreference
        $PSNativeCommandUseErrorActionPreference = $false
    }
    $slot = Join-Path $script:PostLandScriptsRoot 'build-slot.ps1'
    $slotOverride = [Environment]::GetEnvironmentVariable('POST_LAND_WARNING_SLOT_SCRIPT')
    if (-not [string]::IsNullOrWhiteSpace($slotOverride)) { $slot = $slotOverride }
    $code = 1
    try {
        if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
            $logParent = Split-Path -Parent $LogPath
            if (-not [string]::IsNullOrWhiteSpace($logParent)) {
                $null = [IO.Directory]::CreateDirectory($logParent)
            }
            & pwsh -NoProfile -NonInteractive -File $slot -Label 'post-land-early-warning' -- @Command *>> $LogPath
        }
        else {
            & pwsh -NoProfile -NonInteractive -File $slot -Label 'post-land-early-warning' -- @Command | Out-Host
        }
        if ($null -ne $LASTEXITCODE) { $code = [int]$LASTEXITCODE }
    }
    finally {
        if ($hasNative) { $PSNativeCommandUseErrorActionPreference = $saved }
    }
    return $code
}

function Invoke-PostLandLane {
    param(
        [string]$Repo,
        [string]$ReceiptRoot,
        $Payload,
        $Manifest
    )
    $now = [datetime]::UtcNow.ToString('o')
    $Payload.startedAt = $now
    $null = [IO.Directory]::CreateDirectory($ReceiptRoot)
    $tipDir = Join-Path $ReceiptRoot ([string]$Payload.tipSha)
    $null = [IO.Directory]::CreateDirectory($tipDir)
    [IO.File]::WriteAllText((Join-Path $ReceiptRoot 'last-start.json'), '{"startedAt":"' + $now + '"}' + "`n")
    $budget = 30.0
    if ($null -ne $Manifest -and $null -ne $Manifest.PSObject.Properties['shardBudgetMinutes']) {
        $budget = [double]$Manifest.shardBudgetMinutes
    }
    $budgetText = [Environment]::GetEnvironmentVariable('POST_LAND_WARNING_SHARD_BUDGET_MINUTES')
    if (-not [string]::IsNullOrWhiteSpace($budgetText)) {
        $parsedBudget = 0.0
        $parsed = [double]::TryParse($budgetText, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$parsedBudget)
        if ($parsed) { $budget = $parsedBudget }
    }
    $slowIds = New-PostLandStringList
    $buildExit = 1
    Push-Location $Repo
    try {
        $build = @('dotnet', 'build', 'tests/Antiphon.Tests', '--property:UseAppHost=false', '--nologo')
        $slotLog = Join-Path $tipDir 'slot.log'
        $buildExit = Invoke-PostLandSlot -Command $build -LogPath $slotLog
        $Payload.buildExit = $buildExit
        if ($buildExit -eq 0 -and $null -ne $Payload.shards) {
            $launched = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
            foreach ($shard in $Payload.shards) {
                if ($null -eq $shard) { continue }
                if (-not $launched.Add([string]$shard.filter)) { continue }
                $phase = Join-Path $tipDir ('phase/' + [string]$shard.id)
                $null = [IO.Directory]::CreateDirectory($phase)
                $env:ANTIPHON_TEST_PHASE_ROOT = $phase
                $trxPath = Join-Path $tipDir ([string]$shard.id + '.trx')
                if (Test-Path -LiteralPath $trxPath) { Remove-Item -LiteralPath $trxPath -Force }
                $run = @(
                    'dotnet', 'run', '--project', 'tests/Antiphon.Tests', '--no-build', '--property:UseAppHost=false',
                    '--', '--treenode-filter', [string]$shard.filter, '--report-trx', '--report-trx-filename', ([string]$shard.id + '.trx'),
                    '--results-directory', $tipDir
                )
                $started = [datetime]::UtcNow
                $watch = [Diagnostics.Stopwatch]::StartNew()
                $shardExit = 1
                try { $shardExit = Invoke-PostLandSlot -Command $run -LogPath $slotLog } catch { [Console]::Error.WriteLine($_.Exception.Message) }
                $watch.Stop()
                $shard.exit = $shardExit
                $fresh = $false
                if (Test-Path -LiteralPath $trxPath) {
                    $written = [IO.File]::GetLastWriteTimeUtc($trxPath)
                    if ($written -ge $started.AddSeconds(-2)) { $fresh = $true }
                }
                if (-not $fresh -and (Test-Path -LiteralPath $trxPath)) {
                    Remove-Item -LiteralPath $trxPath -Force
                }
                $shard.slow = $false
                if ($watch.Elapsed.TotalMinutes -gt $budget) {
                    $shard.slow = $true
                    [void]$slowIds.Add([string]$shard.id)
                    [Console]::Error.WriteLine('R1 debug shard=' + [string]$shard.id)
                }
            }
        }
    }
    finally {
        Pop-Location
    }
    $Payload.slowShards = $slowIds
    $Payload.slot = $true
    $Payload.finishedAt = [datetime]::UtcNow.ToString('o')
    if ($buildExit -ne 0) {
        $owedCount = 0
        if ($null -ne $Payload.owedWindows) { $owedCount = $Payload.owedWindows.Count }
        $emptyNames = New-PostLandStringList
        $Payload.summaryLine = Format-PostLandSummary -Tip ([string]$Payload.tipSha) -Base ([string]$Payload.baseSha) -NewReds $emptyNames -UnknownShards $emptyNames -OwedCount $owedCount -BuildExit $buildExit
        $Payload.state = 'incomplete'
        Write-PostLandReceipt $ReceiptRoot $Payload
        [Console]::Out.WriteLine([string]$Payload.summaryLine)
        return 1
    }
    $null = Complete-PostLandOutcome $Payload $Repo $ReceiptRoot
    if (([string]$Payload.summaryLine).Contains('new-reds=unknown')) {
        $Payload.state = 'incomplete'
    }
    else {
        $Payload.state = 'completed'
    }
    Write-PostLandReceipt $ReceiptRoot $Payload
    [Console]::Out.WriteLine([string]$Payload.summaryLine)
    $exit = 0
    if ($null -ne $Payload.newReds -and @($Payload.newReds).Count -gt 0) { $exit = 1 }
    if (([string]$Payload.summaryLine).Contains('new-reds=unknown')) { $exit = 1 }
    return $exit
}
