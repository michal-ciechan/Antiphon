#requires -Version 5.1
# CARD-0479 group B. Rehearses the literal fenced snippets in docs/mechanical-pc-contract.md.
# Disposable git fixtures only. Canned CHECKPOINT/EXECUTED/FAILED text stands in for dotnet.
# ASCII-only.
param(
    [string]$Case = '',
    [string]$ResultsDirectory = ''
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$here = $PSScriptRoot
$lib = Join-Path $here 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'c487-harness.ps1')

# CP-10 invokes this script with no -ResultsDirectory. Linux login shells often
# leave TEMP unset, and New-C487Root joins that variable.
if ([string]::IsNullOrWhiteSpace($env:TEMP)) {
    if (-not [string]::IsNullOrWhiteSpace($env:TMPDIR)) { $env:TEMP = $env:TMPDIR }
    elseif (-not [string]::IsNullOrWhiteSpace($env:TMP)) { $env:TEMP = $env:TMP }
    else { $env:TEMP = '/tmp' }
}

$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory
$script:RepoRoot = Split-Path -Parent $here
$script:Contract = Join-Path $script:RepoRoot 'docs/mechanical-pc-contract.md'
$script:Pwsh = (Get-Command -Name pwsh -ErrorAction Stop).Source
$script:Utf8 = New-Object System.Text.UTF8Encoding $false
$script:StandardFiles = [ordered]@{
    'src/a.txt' = "alpha BEFORE omega`n"
    'src/b.txt' = "bravo`n"
    'fixtures/config.txt' = "config-v1`n"
    'backend/identity.txt' = "backend-v1`n"
}
$script:Members = @('src/a.txt', 'src/b.txt', 'fixtures/config.txt', 'backend/identity.txt')

function Write-Utf8 {
    param([string]$Path, [string]$Text)
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $Text, $script:Utf8)
}

function New-Work {
    param([string]$Name)
    $dir = Join-Path $ResultsDirectory ($Name + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    return $dir
}

function Get-Full {
    param([string]$Root, [string]$Relative)
    return (Join-Path $Root ($Relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar)))
}

function Get-Sha {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-BytesSha {
    param([byte[]]$Bytes)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function Format-JsonArray {
    param([string[]]$Items)
    if ($null -eq $Items -or $Items.Count -eq 0) { return '[]' }
    $quoted = foreach ($item in $Items) { '"' + $item.Replace('\', '\\').Replace('"', '\"') + '"' }
    return '[' + ($quoted -join ',') + ']'
}

function Invoke-Git {
    param([string]$Repo, [string[]]$GitArgs)
    $output = & git -C $Repo @GitArgs 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw ("git " + ($GitArgs -join ' ') + " -> " + $output.Trim()) }
    return $output.Trim()
}

function New-Snapshot {
    param([hashtable]$Files)
    if ($null -eq $Files) { $Files = $script:StandardFiles }
    $root = Join-Path (New-Work 'repo') 'snapshot'
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    foreach ($rel in $Files.Keys) { Write-Utf8 (Get-Full $root $rel) ([string]$Files[$rel]) }
    Invoke-Git $root @('init', '-q', '-b', 'master') | Out-Null
    Invoke-Git $root @('config', 'user.email', 'c479@example.invalid') | Out-Null
    Invoke-Git $root @('config', 'user.name', 'c479') | Out-Null
    Invoke-Git $root @('config', 'commit.gpgsign', 'false') | Out-Null
    Invoke-Git $root @('config', 'core.autocrlf', 'false') | Out-Null
    Invoke-Git $root @('add', '--', 'src', 'fixtures', 'backend') | Out-Null
    Invoke-Git $root @('commit', '-q', '-m', 'L') | Out-Null
    $head = Invoke-Git $root @('rev-parse', 'HEAD')
    return [pscustomobject]@{ Root = $root; Head = $head }
}

function Get-Seal {
    param([string]$Root, [string[]]$Paths)
    $seal = @{}
    foreach ($rel in $Paths) { $seal[$rel] = Get-Sha (Get-Full $Root $rel) }
    return $seal
}

function Test-Seal {
    param($Seal, [string]$Root)
    foreach ($rel in @($Seal.Keys)) {
        if ((Get-Sha (Get-Full $Root $rel)) -ne $Seal[$rel]) { return $false }
    }
    return $true
}

function Get-ReplacedHash {
    param([string]$Full, [string]$Context, [string]$Replacement)
    $text = [System.IO.File]::ReadAllText($Full)
    $updated = $text.Replace($Context, $Replacement)
    $tmp = Join-Path (New-Work 'mut') 'bytes.txt'
    # Same writer as the apply snippet: UTF-8 with BOM.
    [System.IO.File]::WriteAllText($tmp, $updated)
    return (Get-Sha $tmp)
}

function Get-Snippet {
    param([string]$Label)
    $text = [System.IO.File]::ReadAllText($script:Contract)
    $marker = '# mechanical-pc-contract v1: ' + $Label
    $pattern = '(?s)```powershell\r?\n(' + [regex]::Escape($marker) + '.*?\r?\n)```'
    $match = [regex]::Match($text, $pattern)
    if (-not $match.Success) { throw "missing snippet $Label" }
    return $match.Groups[1].Value
}

function Invoke-Snippet {
    param([string]$Label, [hashtable]$Params)
    $work = New-Work ('snip-' + $Label)
    $scriptPath = Join-Path $work ($Label + '.ps1')
    Write-Utf8 $scriptPath (Get-Snippet $Label)
    $argv = @('-NoProfile', '-NonInteractive', '-File', $scriptPath)
    foreach ($key in @($Params.Keys)) {
        $argv += ('-' + $key)
        $argv += [string]$Params[$key]
    }
    $text = & $script:Pwsh @argv 2>&1 | Out-String
    return [pscustomobject]@{ Exit = $LASTEXITCODE; Text = $text.Trim() }
}

function Test-Accepted {
    param($Result)
    return ($Result.Exit -eq 0 -and $Result.Text.Contains('C479 ACCEPT') -and -not $Result.Text.Contains('C479 REFUSE'))
}

function Test-Refused {
    param($Result, [string]$Reason)
    return ($Result.Exit -eq 2 -and $Result.Text.Contains('C479 REFUSE ' + $Reason))
}

function Write-Index {
    param([string]$Pack, [string]$Snapshot, [string[]]$Members)
    $parts = @()
    foreach ($rel in $Members) {
        $hash = Get-Sha (Get-Full $Snapshot $rel)
        $parts += ('{"path":"' + $rel + '","sha256":"' + $hash + '"}')
    }
    $json = '{"members":[' + ($parts -join ',') + ']}'
    $path = Join-Path $Pack 'index.json'
    Write-Utf8 $path $json
    $digest = Get-BytesSha ([System.IO.File]::ReadAllBytes($path))
    Write-Utf8 (Join-Path $Pack 'index.sha256') $digest
}

function New-Pack {
    param([string]$Name)
    $pack = Join-Path (New-Work $Name) 'pack'
    New-Item -ItemType Directory -Path $pack -Force | Out-Null
    return $pack
}

function Write-Apply {
    param([string]$Pack, [string[]]$Allowed, $Closure, $Edits)
    $allowedJson = Format-JsonArray $Allowed
    $closureParts = @()
    foreach ($item in @($Closure)) {
        $closureParts += ('{"path":"' + $item.Path + '","sha256":"' + $item.Sha + '"}')
    }
    $editParts = @()
    foreach ($edit in @($Edits)) {
        $editParts += ('{"path":"' + $edit.Path + '","preimageSha256":"' + $edit.Preimage + '","mutantSha256":"' + $edit.Mutant + '","context":"' + $edit.Context + '","replacement":"' + $edit.Replacement + '","expectedMatches":' + $edit.Expected + '}')
    }
    $json = '{"allowedFiles":' + $allowedJson + ',"closure":[' + ($closureParts -join ',') + '],"edits":[' + ($editParts -join ',') + ']}'
    Write-Utf8 (Join-Path $Pack 'apply.json') $json
}

function New-Edit {
    param([string]$Path, [string]$Preimage, [string]$Mutant, [string]$Context, [string]$Replacement, [int]$Expected)
    return [pscustomobject]@{
        Path = $Path; Preimage = $Preimage; Mutant = $Mutant
        Context = $Context; Replacement = $Replacement; Expected = $Expected
    }
}

function New-Closure {
    param([string]$Root, [string]$Relative, [string]$Sha)
    if ([string]::IsNullOrWhiteSpace($Sha)) { $Sha = Get-Sha (Get-Full $Root $Relative) }
    return [pscustomobject]@{ Path = $Relative; Sha = $Sha }
}

function Write-Restore {
    param([string]$Pack, $Files)
    $parts = @()
    foreach ($file in @($Files)) {
        $parts += ('{"path":"' + $file.Path + '","preimageSha256":"' + $file.Preimage + '","mutantSha256":"' + $file.Mutant + '"}')
    }
    Write-Utf8 (Join-Path $Pack 'restore.json') ('{"files":[' + ($parts -join ',') + ']}')
}

function New-DriverText {
    param([int]$ExitCode, [int]$Skipped, [string]$BuildSource, [string[]]$Executed, [string[]]$Failed)
    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("CHECKPOINT CP-x commit=abc build=ok filter=/*/*/Example/* executed=$($Executed.Count) passed=0 failed=$($Failed.Count) skipped=$Skipped trx=/tmp/c479.trx slot=granted waited=0s dirty=0 source=abc sourceState=clean buildSource=$BuildSource")
    [void]$lines.Add("CHECKPOINT CP-x EXIT CODE: $ExitCode")
    foreach ($name in $Executed) { [void]$lines.Add("EXECUTED $name") }
    foreach ($name in $Failed) { [void]$lines.Add("FAILED $name") }
    return (($lines -join "`n") + "`n")
}

function Read-Driver {
    param([string]$Text)
    $executed = New-Object System.Collections.Generic.List[string]
    $failed = New-Object System.Collections.Generic.List[string]
    $skipped = 0
    $buildSource = 'verified'
    $exitCode = 0
    foreach ($line in ($Text -split "\r?\n")) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0) { continue }
        if ($trimmed.StartsWith('EXECUTED ')) { [void]$executed.Add($trimmed.Substring(9).Trim()); continue }
        if ($trimmed.StartsWith('FAILED ')) { [void]$failed.Add($trimmed.Substring(7).Trim()); continue }
        if ($trimmed -match 'EXIT CODE:\s*(\d+)') { $exitCode = [int]$Matches[1]; continue }
        if ($trimmed.StartsWith('CHECKPOINT ')) {
            if ($trimmed -match 'skipped=(\d+)') { $skipped = [int]$Matches[1] }
            if ($trimmed -match 'buildSource=([A-Za-z0-9_-]+)') { $buildSource = $Matches[1] }
        }
    }
    return [pscustomobject]@{
        Executed = $executed.ToArray()
        Failed = $failed.ToArray()
        Skipped = $skipped
        BuildSource = $buildSource
        ExitCode = $exitCode
    }
}

function Write-OracleJson {
    param(
        [string]$Pack, [int]$DriverExit, [string]$BuildSource, [string]$MutantHash, [string]$RestoredHash,
        [int]$Skipped, [bool]$Preexisted, [string[]]$Executed, [string[]]$Failed,
        [string[]]$ExpectedExecuted, [string[]]$ExpectedFailed
    )
    $flag = 'false'
    if ($Preexisted) { $flag = 'true' }
    $json = '{"driverExit":' + $DriverExit +
        ',"buildSource":"' + $BuildSource +
        '","mutantOutputHash":"' + $MutantHash +
        '","restoredOutputHash":"' + $RestoredHash +
        '","skipped":' + $Skipped +
        ',"resultsPreexisted":' + $flag +
        ',"executed":' + (Format-JsonArray $Executed) +
        ',"failed":' + (Format-JsonArray $Failed) +
        ',"expectedExecuted":' + (Format-JsonArray $ExpectedExecuted) +
        ',"expectedFailed":' + (Format-JsonArray $ExpectedFailed) + '}'
    Write-Utf8 (Join-Path $Pack 'oracle.json') $json
}

function Invoke-Oracle {
    param(
        [string]$DriverText, [string[]]$ExpectedExecuted, [string[]]$ExpectedFailed,
        [bool]$Preexisted = $false, [string]$BuildSourceOverride = '', [bool]$SameOutput = $false
    )
    $work = New-Work 'oracle'
    $driverPath = Join-Path $work 'driver.txt'
    Write-Utf8 $driverPath $DriverText
    $parsed = Read-Driver ([System.IO.File]::ReadAllText($driverPath))
    $mutant = 'a' * 64
    $restored = 'b' * 64
    if ($SameOutput) { $restored = $mutant }
    $build = $parsed.BuildSource
    if (-not [string]::IsNullOrWhiteSpace($BuildSourceOverride)) { $build = $BuildSourceOverride }
    $pack = Join-Path $work 'pack'
    New-Item -ItemType Directory -Path $pack -Force | Out-Null
    Write-OracleJson $pack $parsed.ExitCode $build $mutant $restored $parsed.Skipped $Preexisted `
        @($parsed.Executed) @($parsed.Failed) $ExpectedExecuted $ExpectedFailed
    return (Invoke-Snippet 'oracle' @{ PackRoot = $pack })
}

function Copy-Backup {
    param([string]$Evidence, [string]$Relative, [string]$SourceFull)
    $dest = Join-Path (Join-Path $Evidence 'backup') ($Relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    $parent = Split-Path -Parent $dest
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [System.IO.File]::Copy($SourceFull, $dest, $true)
}

function New-MutantFile {
    param([string]$Full, [string]$Context, [string]$Replacement)
    $text = [System.IO.File]::ReadAllText($Full)
    $updated = $text.Replace($Context, $Replacement)
    $tmp = Join-Path (New-Work 'stage') 'staged.txt'
    [System.IO.File]::WriteAllText($tmp, $updated)
    [System.IO.File]::Copy($tmp, $Full, $true)
    return (Get-Sha $Full)
}

$script:ExactExecuted = @('Example.Negative', 'Example.CompanionOwner', 'Example.CompanionPrereq')
$script:ExactFailed = @('Example.Negative')

function New-ExactDriver {
    param([int]$ExitCode = 1, [int]$Skipped = 0, [string]$BuildSource = 'verified', [string[]]$Executed, [string[]]$Failed)
    if ($null -eq $Executed) { $Executed = $script:ExactExecuted }
    if ($null -eq $Failed) { $Failed = $script:ExactFailed }
    return (New-DriverText -ExitCode $ExitCode -Skipped $Skipped -BuildSource $BuildSource -Executed $Executed -Failed $Failed)
}

function Test-binding-source {
    $snap = New-Snapshot
    $pack = New-Pack 'preflight-ok'
    Write-Index $pack $snap.Root $script:Members
    $seal = Get-Seal $snap.Root $script:Members
    $ok = Invoke-Snippet 'preflight' @{ SnapshotRoot = $snap.Root; PackRoot = $pack; L = $snap.Head }
    Assert-C487 ((Test-Accepted $ok) -and (Test-Seal $seal $snap.Root)) 'C479 binding-source:head-equals-l-accepts' $ok.Text

    $badHead = Invoke-Snippet 'preflight' @{ SnapshotRoot = $snap.Root; PackRoot = $pack; L = ('0' * 40) }
    Assert-C487 ((Test-Refused $badHead 'source-mismatch') -and (Test-Seal $seal $snap.Root)) 'C479 binding-source:head-mismatch-refuses' $badHead.Text

    $remote = New-Snapshot
    Invoke-Git $remote.Root @('checkout', '-q', '-b', 'remote-tip') | Out-Null
    Invoke-Git $remote.Root @('commit', '-q', '--allow-empty', '-m', 'R') | Out-Null
    $tip = Invoke-Git $remote.Root @('rev-parse', 'HEAD')
    Invoke-Git $remote.Root @('checkout', '-q', 'master') | Out-Null
    Invoke-Git $remote.Root @('update-ref', 'refs/remotes/origin/master', $tip) | Out-Null
    $remotePack = New-Pack 'preflight-remote'
    Write-Index $remotePack $remote.Root $script:Members
    $remoteSeal = Get-Seal $remote.Root $script:Members
    $advanced = Invoke-Snippet 'preflight' @{ SnapshotRoot = $remote.Root; PackRoot = $remotePack; L = $remote.Head }
    $stillL = (Invoke-Git $remote.Root @('rev-parse', 'HEAD')) -eq $remote.Head
    Assert-C487 ((Test-Accepted $advanced) -and $stillL -and (Test-Seal $remoteSeal $remote.Root) -and ($tip -ne $remote.Head)) 'C479 binding-source:remote-advanced-accepts' $advanced.Text

    $missing = New-Snapshot
    $missingPack = New-Pack 'preflight-missing'
    Write-Index $missingPack $missing.Root $script:Members
    Remove-Item -LiteralPath (Join-Path $missingPack 'index.sha256') -Force
    $missingSeal = Get-Seal $missing.Root $script:Members
    $noDigest = Invoke-Snippet 'preflight' @{ SnapshotRoot = $missing.Root; PackRoot = $missingPack; L = $missing.Head }
    Assert-C487 ((Test-Refused $noDigest 'binding-missing') -and (Test-Seal $missingSeal $missing.Root)) 'C479 binding-source:missing-digest-refuses' $noDigest.Text

    $drift = New-Snapshot
    $driftPack = New-Pack 'preflight-member'
    Write-Index $driftPack $drift.Root $script:Members
    Write-Utf8 (Get-Full $drift.Root 'src/a.txt') "member drift`n"
    $after = Get-Sha (Get-Full $drift.Root 'src/a.txt')
    $member = Invoke-Snippet 'preflight' @{ SnapshotRoot = $drift.Root; PackRoot = $driftPack; L = $drift.Head }
    $untouched = (Get-Sha (Get-Full $drift.Root 'src/a.txt')) -eq $after
    Assert-C487 ((Test-Refused $member 'artifact-mismatch') -and $untouched) 'C479 binding-source:member-hash-mismatch-refuses' $member.Text
}

function Test-binding-preimage {
    $crlf = New-Snapshot
    $live = Get-Full $crlf.Root 'src/a.txt'
    $lfHash = Get-Sha $live
    Write-Utf8 $live "alpha BEFORE omega`r`n"
    $crlfHash = Get-Sha $live
    $crlfMutant = Get-ReplacedHash $live 'BEFORE' 'AFTER'
    $crlfPack = New-Pack 'crlf'
    Write-Apply $crlfPack @('src/a.txt') @((New-Closure $crlf.Root 'fixtures/config.txt')) @(
        (New-Edit 'src/a.txt' $lfHash $crlfMutant 'BEFORE' 'AFTER' 1)
    )
    $crlfResult = Invoke-Snippet 'apply' @{ SnapshotRoot = $crlf.Root; PackRoot = $crlfPack }
    Assert-C487 ((Test-Refused $crlfResult 'patch-mismatch') -and ((Get-Sha $live) -eq $crlfHash)) 'C479 binding-preimage:crlf-refuses' $crlfResult.Text

    $candidate = New-Snapshot
    $candidateLive = Get-Full $candidate.Root 'src/a.txt'
    $candidateSeal = Get-Sha $candidateLive
    $cBytes = Join-Path (New-Work 'c-bytes') 'a.txt'
    Write-Utf8 $cBytes "candidate BEFORE omega`n"
    $cHash = Get-Sha $cBytes
    $candidatePack = New-Pack 'candidate'
    Write-Apply $candidatePack @('src/a.txt') @((New-Closure $candidate.Root 'fixtures/config.txt')) @(
        (New-Edit 'src/a.txt' $cHash (Get-ReplacedHash $candidateLive 'BEFORE' 'AFTER') 'BEFORE' 'AFTER' 1)
    )
    $candidateResult = Invoke-Snippet 'apply' @{ SnapshotRoot = $candidate.Root; PackRoot = $candidatePack }
    Assert-C487 ((Test-Refused $candidateResult 'patch-mismatch') -and ((Get-Sha $candidateLive) -eq $candidateSeal)) 'C479 binding-preimage:candidate-at-c-refuses' $candidateResult.Text

    $backend = New-Snapshot
    $backendSeal = Get-Seal $backend.Root @('src/a.txt', 'backend/identity.txt')
    $backendPack = New-Pack 'backend'
    $wrong = '0' * 64
    Write-Apply $backendPack @('src/a.txt') @((New-Closure $backend.Root 'backend/identity.txt' $wrong)) @(
        (New-Edit 'src/a.txt' (Get-Sha (Get-Full $backend.Root 'src/a.txt')) (Get-ReplacedHash (Get-Full $backend.Root 'src/a.txt') 'BEFORE' 'AFTER') 'BEFORE' 'AFTER' 1)
    )
    $backendResult = Invoke-Snippet 'apply' @{ SnapshotRoot = $backend.Root; PackRoot = $backendPack }
    Assert-C487 ((Test-Refused $backendResult 'artifact-mismatch') -and (Test-Seal $backendSeal $backend.Root)) 'C479 binding-preimage:backend-drift-refuses' $backendResult.Text
}

function Test-apply-exact {
    $snap = New-Snapshot
    $full = Get-Full $snap.Root 'src/a.txt'
    $before = Get-Sha $full
    $mutant = Get-ReplacedHash $full 'BEFORE' 'AFTER'
    $others = Get-Seal $snap.Root @('src/b.txt', 'fixtures/config.txt', 'backend/identity.txt')
    $pack = New-Pack 'exact'
    Write-Apply $pack @('src/a.txt') @((New-Closure $snap.Root 'fixtures/config.txt')) @(
        (New-Edit 'src/a.txt' $before $mutant 'BEFORE' 'AFTER' 1)
    )
    $first = Invoke-Snippet 'apply' @{ SnapshotRoot = $snap.Root; PackRoot = $pack }
    $second = Invoke-Snippet 'apply' @{ SnapshotRoot = $snap.Root; PackRoot = $pack }
    $once = (Test-Accepted $first) -and (Test-Refused $second 'patch-mismatch') -and ((Get-Sha $full) -eq $mutant)
    Assert-C487 $once 'C479 apply-exact:applies-once-accepts' ($first.Text + ' / ' + $second.Text)
    Assert-C487 ((Get-Sha $full) -eq $mutant) 'C479 apply-exact:mutant-sha256-matches' (Get-Sha $full)
    $set = (Test-Seal $others $snap.Root) -and ((Get-Sha $full) -ne $before)
    Assert-C487 $set 'C479 apply-exact:changed-file-set-matches' $first.Text
}

function Test-apply-context {
    $dup = New-Snapshot
    $dupFull = Get-Full $dup.Root 'src/a.txt'
    Write-Utf8 $dupFull "BEFORE and BEFORE`n"
    $dupBefore = Get-Sha $dupFull
    $dupMutant = Get-ReplacedHash $dupFull 'BEFORE' 'AFTER'
    $dupPack = New-Pack 'dup'
    Write-Apply $dupPack @('src/a.txt') @((New-Closure $dup.Root 'fixtures/config.txt')) @(
        (New-Edit 'src/a.txt' $dupBefore $dupMutant 'BEFORE' 'AFTER' 1)
    )
    $dupResult = Invoke-Snippet 'apply' @{ SnapshotRoot = $dup.Root; PackRoot = $dupPack }
    Assert-C487 ((Test-Refused $dupResult 'patch-mismatch') -and ((Get-Sha $dupFull) -eq $dupBefore)) 'C479 apply-context:duplicate-refuses-no-write' $dupResult.Text

    $absent = New-Snapshot
    $absentFull = Get-Full $absent.Root 'src/a.txt'
    Write-Utf8 $absentFull "nothing here`n"
    $absentBefore = Get-Sha $absentFull
    $absentPack = New-Pack 'absent'
    Write-Apply $absentPack @('src/a.txt') @((New-Closure $absent.Root 'fixtures/config.txt')) @(
        (New-Edit 'src/a.txt' $absentBefore (Get-ReplacedHash $absentFull 'BEFORE' 'AFTER') 'BEFORE' 'AFTER' 1)
    )
    $absentResult = Invoke-Snippet 'apply' @{ SnapshotRoot = $absent.Root; PackRoot = $absentPack }
    Assert-C487 ((Test-Refused $absentResult 'patch-mismatch') -and ((Get-Sha $absentFull) -eq $absentBefore)) 'C479 apply-context:absent-refuses-no-write' $absentResult.Text
}

function Test-apply-closure {
    $extra = New-Snapshot
    $a = Get-Full $extra.Root 'src/a.txt'
    $b = Get-Full $extra.Root 'src/b.txt'
    $aBefore = Get-Sha $a
    $bBefore = Get-Sha $b
    $extraPack = New-Pack 'extra'
    Write-Apply $extraPack @('src/a.txt') @((New-Closure $extra.Root 'fixtures/config.txt')) @(
        (New-Edit 'src/a.txt' $aBefore (Get-ReplacedHash $a 'BEFORE' 'AFTER') 'BEFORE' 'AFTER' 1),
        (New-Edit 'src/b.txt' $bBefore (Get-ReplacedHash $b 'bravo' 'BRAVO') 'bravo' 'BRAVO' 1)
    )
    $extraResult = Invoke-Snippet 'apply' @{ SnapshotRoot = $extra.Root; PackRoot = $extraPack }
    $extraOk = (Test-Refused $extraResult 'patch-mismatch') -and ((Get-Sha $a) -eq $aBefore) -and ((Get-Sha $b) -eq $bBefore)
    Assert-C487 $extraOk 'C479 apply-closure:extra-file-restores-and-refuses' $extraResult.Text

    $partial = New-Snapshot
    $pa = Get-Full $partial.Root 'src/a.txt'
    $pb = Get-Full $partial.Root 'src/b.txt'
    $paBefore = Get-Sha $pa
    $pbBefore = Get-Sha $pb
    $partialPack = New-Pack 'partial'
    Write-Apply $partialPack @('src/a.txt', 'src/b.txt') @((New-Closure $partial.Root 'fixtures/config.txt')) @(
        (New-Edit 'src/a.txt' $paBefore (Get-ReplacedHash $pa 'BEFORE' 'AFTER') 'BEFORE' 'AFTER' 1),
        (New-Edit 'src/b.txt' ('0' * 64) (Get-ReplacedHash $pb 'bravo' 'BRAVO') 'bravo' 'BRAVO' 1)
    )
    $partialResult = Invoke-Snippet 'apply' @{ SnapshotRoot = $partial.Root; PackRoot = $partialPack }
    $partialOk = (Test-Refused $partialResult 'patch-mismatch') -and ((Get-Sha $pa) -eq $paBefore) -and ((Get-Sha $pb) -eq $pbBefore)
    Assert-C487 $partialOk 'C479 apply-closure:partial-second-preimage-restores-first' $partialResult.Text

    $fixture = New-Snapshot
    $fixtureSeal = Get-Seal $fixture.Root @('src/a.txt', 'fixtures/config.txt')
    $fixturePack = New-Pack 'fixture'
    Write-Apply $fixturePack @('src/a.txt') @((New-Closure $fixture.Root 'fixtures/config.txt' ('1' * 64))) @(
        (New-Edit 'src/a.txt' (Get-Sha (Get-Full $fixture.Root 'src/a.txt')) (Get-ReplacedHash (Get-Full $fixture.Root 'src/a.txt') 'BEFORE' 'AFTER') 'BEFORE' 'AFTER' 1)
    )
    $fixtureResult = Invoke-Snippet 'apply' @{ SnapshotRoot = $fixture.Root; PackRoot = $fixturePack }
    Assert-C487 ((Test-Refused $fixtureResult 'artifact-mismatch') -and (Test-Seal $fixtureSeal $fixture.Root)) 'C479 apply-closure:fixture-hash-refuses-before-write' $fixtureResult.Text
}

function Test-oracle-rows {
    $exact = Invoke-Oracle (New-ExactDriver) $script:ExactExecuted $script:ExactFailed
    Assert-C487 (Test-Accepted $exact) 'C479 oracle-rows:exact-negative-and-companions-accept' $exact.Text

    $extraFailed = @('Example.Negative', 'Example.CompanionOwner')
    $extra = Invoke-Oracle (New-ExactDriver -Failed $extraFailed) $script:ExactExecuted $script:ExactFailed
    Assert-C487 (Test-Refused $extra 'unexpected-red') 'C479 oracle-rows:extra-failure-rejects' $extra.Text

    $zero = Invoke-Oracle (New-ExactDriver -ExitCode 0 -Executed @() -Failed @()) $script:ExactExecuted @()
    Assert-C487 (Test-Refused $zero 'selector-drift') 'C479 oracle-rows:zero-rows-reject' $zero.Text

    $missingExecuted = @('Example.Negative', 'Example.CompanionOwner')
    $missing = Invoke-Oracle (New-ExactDriver -Executed $missingExecuted -Failed $script:ExactFailed) $script:ExactExecuted $script:ExactFailed
    Assert-C487 (Test-Refused $missing 'selector-drift') 'C479 oracle-rows:missing-row-rejects' $missing.Text

    $extraExecuted = @('Example.Negative', 'Example.CompanionOwner', 'Example.CompanionPrereq', 'Example.Extra')
    $extraRow = Invoke-Oracle (New-ExactDriver -Executed $extraExecuted) $script:ExactExecuted $script:ExactFailed
    Assert-C487 (Test-Refused $extraRow 'selector-drift') 'C479 oracle-rows:extra-row-rejects' $extraRow.Text

    $dupExecuted = @('Example.Negative', 'Example.Negative', 'Example.CompanionOwner')
    $dup = Invoke-Oracle (New-ExactDriver -Executed $dupExecuted) @('Example.Negative', 'Example.CompanionOwner') $script:ExactFailed
    Assert-C487 (Test-Refused $dup 'selector-drift') 'C479 oracle-rows:duplicate-row-rejects' $dup.Text

    $skipped = Invoke-Oracle (New-ExactDriver -Skipped 1) $script:ExactExecuted $script:ExactFailed
    Assert-C487 (Test-Refused $skipped 'selector-drift') 'C479 oracle-rows:skipped-rejects' $skipped.Text

    $exit2 = Invoke-Oracle (New-ExactDriver -ExitCode 2) $script:ExactExecuted $script:ExactFailed
    Assert-C487 (Test-Refused $exit2 'prerequisite-missing') 'C479 oracle-rows:driver-exit-2-rejects' $exit2.Text

    $exit3 = Invoke-Oracle (New-ExactDriver -ExitCode 3) $script:ExactExecuted $script:ExactFailed
    Assert-C487 (Test-Refused $exit3 'selector-unresolved') 'C479 oracle-rows:driver-exit-3-rejects' $exit3.Text
}

function Test-oracle-provenance {
    $pre = Invoke-Oracle (New-ExactDriver) $script:ExactExecuted $script:ExactFailed -Preexisted $true
    Assert-C487 (Test-Refused $pre 'stale-output') 'C479 oracle-provenance:preexisting-results-reject' $pre.Text

    $unknown = Invoke-Oracle (New-ExactDriver -BuildSource 'unknown') $script:ExactExecuted $script:ExactFailed
    Assert-C487 (Test-Refused $unknown 'stale-output') 'C479 oracle-provenance:unknown-build-source-rejects' $unknown.Text

    $same = Invoke-Oracle (New-ExactDriver) $script:ExactExecuted $script:ExactFailed -SameOutput $true
    Assert-C487 (Test-Refused $same 'stale-output') 'C479 oracle-provenance:same-output-identity-rejects' $same.Text
}

function Invoke-RestoreCase {
    param([string]$Phase = 'restore', [bool]$Marker = $false, [bool]$RedNote = $false, [string]$LiveText, [string]$SecondLiveText)
    $snap = New-Snapshot
    $evidence = Join-Path (New-Work 'evidence') 'root'
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    $a = Get-Full $snap.Root 'src/a.txt'
    $originalA = Join-Path (New-Work 'pre') 'a.txt'
    [System.IO.File]::Copy($a, $originalA, $true)
    $preimageA = Get-Sha $a
    if ($LiveText) { Write-Utf8 $a $LiveText } else { New-MutantFile $a 'BEFORE' 'AFTER' | Out-Null }
    $mutantA = Get-Sha $a
    Copy-Backup $evidence 'src/a.txt' $originalA
    $files = @([pscustomobject]@{ Path = 'src/a.txt'; Preimage = $preimageA; Mutant = $mutantA })
    if ($SecondLiveText) {
        $b = Get-Full $snap.Root 'src/b.txt'
        $originalB = Join-Path (New-Work 'pre') 'b.txt'
        [System.IO.File]::Copy($b, $originalB, $true)
        $preimageB = Get-Sha $originalB
        Write-Utf8 $b $SecondLiveText
        $mutantB = Get-Sha $b
        Copy-Backup $evidence 'src/b.txt' $originalB
        $files += [pscustomobject]@{ Path = 'src/b.txt'; Preimage = $preimageB; Mutant = $mutantB }
    }
    if ($RedNote) { Write-Utf8 (Join-Path $evidence 'red-phase.txt') 'exit=1' }
    if ($Marker) { Write-Utf8 (Join-Path $evidence 'forced-rebuild.marker') 'rebuilt' }
    $pack = New-Pack 'restore'
    Write-Restore $pack $files
    $result = Invoke-Snippet 'restore' @{ SnapshotRoot = $snap.Root; EvidenceRoot = $evidence; PackRoot = $pack; Phase = $Phase }
    return [pscustomobject]@{ Result = $result; Snapshot = $snap; PreimageA = $preimageA; MutantA = $mutantA }
}

function Test-restore-interrupt {
    $afterApply = Invoke-RestoreCase
    $applyOk = (Test-Accepted $afterApply.Result) -and ((Get-Sha (Get-Full $afterApply.Snapshot.Root 'src/a.txt')) -eq $afterApply.PreimageA)
    Assert-C487 $applyOk 'C479 restore-interrupt:after-apply-restores-preimage' $afterApply.Result.Text

    $afterRed = Invoke-RestoreCase -RedNote $true
    $redOk = (Test-Accepted $afterRed.Result) -and ((Get-Sha (Get-Full $afterRed.Snapshot.Root 'src/a.txt')) -eq $afterRed.PreimageA)
    Assert-C487 $redOk 'C479 restore-interrupt:after-red-restores-preimage' $afterRed.Result.Text

    $noMarker = Invoke-RestoreCase -Phase 'restored'
    $withMarker = Invoke-RestoreCase -Phase 'restored' -Marker $true
    $markerOk = (Test-Refused $noMarker.Result 'stale-output') -and (Test-Accepted $withMarker.Result) -and ((Get-Sha (Get-Full $withMarker.Snapshot.Root 'src/a.txt')) -eq $withMarker.PreimageA)
    Assert-C487 $markerOk 'C479 restore-interrupt:restored-requires-forced-rebuild' ($noMarker.Result.Text + ' / ' + $withMarker.Result.Text)
}

function Test-restore-foreign-edit {
    $snap = New-Snapshot
    $evidence = Join-Path (New-Work 'evidence') 'root'
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    $a = Get-Full $snap.Root 'src/a.txt'
    $b = Get-Full $snap.Root 'src/b.txt'
    $originalA = Join-Path (New-Work 'pre') 'a.txt'
    $originalB = Join-Path (New-Work 'pre') 'b.txt'
    [System.IO.File]::Copy($a, $originalA, $true)
    [System.IO.File]::Copy($b, $originalB, $true)
    $preimageA = Get-Sha $originalA
    $preimageB = Get-Sha $originalB
    $stagedA = Join-Path (New-Work 'mut') 'a.txt'
    [System.IO.File]::WriteAllText($stagedA, ([System.IO.File]::ReadAllText($a)).Replace('BEFORE', 'AFTER'))
    $recordedMutantA = Get-Sha $stagedA
    Write-Utf8 $a "FOREIGN EDIT`n"
    $foreign = Get-Sha $a
    $mutantB = New-MutantFile $b 'bravo' 'BRAVO'
    Copy-Backup $evidence 'src/a.txt' $originalA
    Copy-Backup $evidence 'src/b.txt' $originalB
    $pack = New-Pack 'foreign'
    Write-Restore $pack @(
        [pscustomobject]@{ Path = 'src/a.txt'; Preimage = $preimageA; Mutant = $recordedMutantA },
        [pscustomobject]@{ Path = 'src/b.txt'; Preimage = $preimageB; Mutant = $mutantB }
    )
    $result = Invoke-Snippet 'restore' @{ SnapshotRoot = $snap.Root; EvidenceRoot = $evidence; PackRoot = $pack; Phase = 'restore' }
    $reported = (Test-Refused $result 'restore-mismatch') -and $result.Text.Contains('path=src/a.txt') -and ((Get-Sha $a) -eq $foreign)
    Assert-C487 $reported 'C479 restore-foreign-edit:path-retained-and-reported' $result.Text
    Assert-C487 ((Get-Sha $b) -eq $mutantB) 'C479 restore-foreign-edit:sibling-retained' (Get-Sha $b)
}

function New-Template {
    param([string]$Body)
    $path = Join-Path (New-Work 'template') 'manifest.txt'
    Write-Utf8 $path $Body
    return $path
}

function Invoke-Instantiate {
    param([string]$Template, [string]$Evidence, [hashtable]$Params)
    $all = @{
        SnapshotRoot = $Params.SnapshotRoot
        EvidenceRoot = $Evidence
        TemplatePath = $Template
        TaskId = 'task-1'
        RunId = 'run-a'
        PcId = 'PC1'
        Phase = 'baseline'
        ResultsRelative = 'out'
    }
    foreach ($key in @($Params.Keys)) { $all[$key] = $Params[$key] }
    return (Invoke-Snippet 'instantiate' $all)
}

function Test-instantiate-tokens {
    $snap = New-Snapshot
    $unknownTemplate = New-Template "root={snapshotRoot}`nextra={notAToken}`n"
    $unknownEvidence = Join-Path (New-Work 'evidence') 'root'
    New-Item -ItemType Directory -Path $unknownEvidence -Force | Out-Null
    $unknown = Invoke-Instantiate $unknownTemplate $unknownEvidence @{ SnapshotRoot = $snap.Root }
    $unknownDir = Join-Path $unknownEvidence 'PC1'
    Assert-C487 ((Test-Refused $unknown 'unknown-token') -and -not (Test-Path -LiteralPath $unknownDir)) 'C479 instantiate-tokens:unknown-token-refuses' $unknown.Text

    $missingTemplate = New-Template "task={taskId}`nroot={snapshotRoot}`n"
    $missingEvidence = Join-Path (New-Work 'evidence') 'root'
    New-Item -ItemType Directory -Path $missingEvidence -Force | Out-Null
    $missing = Invoke-Snippet 'instantiate' @{
        SnapshotRoot = $snap.Root; EvidenceRoot = $missingEvidence; TemplatePath = $missingTemplate
        RunId = 'run-a'; PcId = 'PC1'; Phase = 'baseline'; ResultsRelative = 'out'
    }
    Assert-C487 (Test-Refused $missing 'missing-value') 'C479 instantiate-tokens:missing-value-refuses' $missing.Text

    $emptyTemplate = New-Template "root={snapshotRoot}`n"
    $emptyEvidence = Join-Path (New-Work 'evidence') 'root'
    New-Item -ItemType Directory -Path $emptyEvidence -Force | Out-Null
    $empty = Invoke-Snippet 'instantiate' @{
        SnapshotRoot = $snap.Root; EvidenceRoot = $emptyEvidence; TemplatePath = $emptyTemplate
        TaskId = 'task-1'; RunId = 'run-a'; PcId = 'PC1'; Phase = 'baseline'; ResultsRelative = ''
    }
    Assert-C487 ((Test-Refused $empty 'empty-results') -and -not (Test-Path -LiteralPath (Join-Path $emptyEvidence 'PC1'))) 'C479 instantiate-tokens:empty-results-refuses' $empty.Text

    $pathTemplate = New-Template "root={snapshotRoot}`n"
    $pathEvidence = Join-Path (New-Work 'evidence') 'root'
    New-Item -ItemType Directory -Path $pathEvidence -Force | Out-Null
    $escaped = Invoke-Instantiate $pathTemplate $pathEvidence @{ SnapshotRoot = $snap.Root; ResultsRelative = '../out' }
    Assert-C487 ((Test-Refused $escaped 'path-not-contained') -and -not (Test-Path -LiteralPath (Join-Path $pathEvidence 'PC1'))) 'C479 instantiate-tokens:path-not-contained-refuses' $escaped.Text

    $okTemplate = New-Template "root={snapshotRoot}`nevidence={evidenceRoot}`ntask={taskId}`nrun={runId}`npc={pcId}`nphase={phase}`n"
    $okEvidence = Join-Path (New-Work 'evidence') 'root'
    New-Item -ItemType Directory -Path $okEvidence -Force | Out-Null
    $first = Invoke-Instantiate $okTemplate $okEvidence @{ SnapshotRoot = $snap.Root; RunId = 'run-a' }
    $attemptA = Join-Path $okEvidence (Join-Path 'PC1' (Join-Path 'baseline' 'run-a'))
    $manifestA = Join-Path $attemptA 'resolved-manifest.txt'
    $firstOk = (Test-Accepted $first) -and (Test-Path -LiteralPath $manifestA)
    $manifestText = ''
    if ($firstOk) { $manifestText = [System.IO.File]::ReadAllText($manifestA) }
    $resolved = $firstOk -and $manifestText.Contains($snap.Root) -and -not $manifestText.Contains('{snapshotRoot}')
    $manifestHash = ''
    if ($resolved) { $manifestHash = Get-Sha $manifestA }
    $second = Invoke-Instantiate $okTemplate $okEvidence @{ SnapshotRoot = $snap.Root; RunId = 'run-b' }
    $attemptB = Join-Path $okEvidence (Join-Path 'PC1' (Join-Path 'baseline' 'run-b'))
    $secondOk = (Test-Accepted $second) -and (Test-Path -LiteralPath $attemptB) -and ($attemptB -ne $attemptA)
    $third = Invoke-Instantiate $okTemplate $okEvidence @{ SnapshotRoot = $snap.Root; RunId = 'run-a' }
    $kept = $resolved -and ((Get-Sha $manifestA) -eq $manifestHash)
    $existing = $resolved -and $secondOk -and (Test-Refused $third 'existing-attempt') -and $kept
    Assert-C487 $existing 'C479 instantiate-tokens:existing-attempt-refuses' ($first.Text + ' / ' + $second.Text + ' / ' + $third.Text)
}

$script:Cases = @(
    'binding-source', 'binding-preimage', 'apply-exact', 'apply-context', 'apply-closure',
    'oracle-rows', 'oracle-provenance', 'restore-interrupt', 'restore-foreign-edit', 'instantiate-tokens'
)
$selected = $script:Cases
if (-not [string]::IsNullOrWhiteSpace($Case)) {
    if ($script:Cases -notcontains $Case) {
        Write-Host ("unknown case " + $Case)
        exit 2
    }
    $selected = @($Case)
}
foreach ($name in $selected) {
    try {
        & ('Test-' + $name)
    } catch {
        Assert-C487 $false ('C479 ' + $name + ':harness-exception') $_.Exception.Message
    }
}
Write-Host ('C479: {0} passed, {1} failed, {2} rows' -f $script:C487Passed, $script:C487Failed, $script:C487Rows)
$floor = 0
if ([string]::IsNullOrWhiteSpace($Case)) { $floor = 38 }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $floor
