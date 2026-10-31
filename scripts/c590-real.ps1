# CARD-0590 live server2 bridge. Dot-source from verify-docker-stack.ps1.
# Executing this file with -Cp runs one plan checkpoint. ASCII only.
param(
    [string]$Cp = '',
    [switch]$RefreshClaudeToken
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'c1008-host-proof.ps1')

$script:C590LiveCases = @(
    # CARD-0604 S4 host-lane cases. They run on server2's own shell against the HOST daemon and are
    # the only cases that change standing state there. Their entries in verify-docker-stack.ps1's
    # switch are the STUB boundaries; this roster is what routes a real run to the remote instead.
    'deploy-parent',
    'deploy-temp-runner',
    'retire-temp-runner', 'retire-temp-containers',
    'runner-cache-inventory',
    'runner-cache-fixture',
    'runner-cache-seed',
    'runner-cache-reset',
    'verify-runner-caches',
    'verify-runner-caches-retired',
    'runner-cache-prune-preview',
    'runner-cache-prune',
    'nested-residue',
    'persistent-restart',
    'server2-independent-handoff',
    'git-credential-smoke',
    'baseline-build-failure',
    'server-image-payload',
    'runner-image-payload',
    'testing-runner-payload',
    # CARD-0604 S9/CP-15. The host-lane containment measurement (V-28 on cgroup v1).
    # Without this entry the case falls through to verify-docker-stack.ps1's default arm and
    # reports 'RealCasePending' -- a red row for a routing reason, not a measured one.
    'custody-containment',
    'parent-native-and-command-session',
    'child-server-image-payload',
    'child-runner-image-payload',
    'test-image-context-and-tools',
    'receipt-runner-image-payload',
    'fixture-image-payload',
    'deployment-state',
    'client-lint',
    'client-tests',
    'messaging-tests',
    'dotnet-filter',
    'session-result-export-and-child-cleanup',
    'session-denied-socket',
    'interrupted-export-cleanup',
    'stock-idle',
    'stock-busy',
    'insert-refused',
    'insert-committed-idle',
    'insert-committed-busy',
    'attempt-committed',
    'body-before-enter',
    'recipient-before-ingestion',
    'transcript-save-fails-release',
    'transcript-save-fails-restart',
    'receipt-before-verdict',
    'response-before-client',
    'receipt-before-manifest',
    'changed-generation',
    'failure-summary',
    'runtime-context-engine',
    'test-context-engine',
    'throwaway-all'
)

function Test-C590LiveCase {
    param([Parameter(Mandatory = $true)][string]$Case)
    return $script:C590LiveCases -contains $Case
}

function Get-C590RepoRoot {
    return (Split-Path -Parent $PSScriptRoot)
}

function Get-C590StateDir {
    $dir = Join-Path (Get-C590RepoRoot) '.antiphon\c590-server2'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return $dir
}

function Get-C590RunId {
    param($Manifest)
    $names = @($Manifest.PSObject.Properties.Name)
    if ($names -contains 'runId' -and $Manifest.runId) {
        return ([string]$Manifest.runId).ToLowerInvariant()
    }
    $path = Join-Path (Get-C590StateDir) 'run-id.txt'
    if (Test-Path -LiteralPath $path) {
        return (Get-Content -LiteralPath $path -Raw).Trim()
    }
    $id = ([guid]::NewGuid().ToString('N')).Substring(0, 12)
    Set-Content -LiteralPath $path -Value $id -Encoding ascii -NoNewline
    return $id
}

function Get-C590Sha {
    param($Manifest)
    $names = @($Manifest.PSObject.Properties.Name)
    if ($names -contains 'sourceSha' -and $Manifest.sourceSha -and [string]$Manifest.sourceSha -match '^[0-9a-f]{40}$') {
        return [string]$Manifest.sourceSha
    }
    return (& git -C (Get-C590RepoRoot) rev-parse HEAD).Trim()
}

function Invoke-C590Ssh {
    param([Parameter(Mandatory = $true)][string]$Remote, [string]$ReceiptPath = '')
    $safeLines = [System.Collections.Generic.List[string]]::new()
    # Only diagnosis tokens and fixed fixture progress survive. Never retain raw
    # stderr, inspect output, package metadata or the remainder of a diagnosis.
    & ssh -o BatchMode=yes -o ConnectTimeout=30 -o ServerAliveInterval=30 -o ServerAliveCountMax=240 mc@server2 $Remote 2>&1 |
        ForEach-Object {
            if ($ReceiptPath -and $safeLines.Count -lt 256) {
                $line = [string]$_
                if ($line -cmatch '^(?:DIAGNOSIS=)?((?:Cache|Fixture|Npm|Nuget)[A-Z][A-Za-z0-9]{0,95})(?:\s|$)') {
                    $safeLines.Add($Matches[1])
                } elseif ($line -cmatch '^PASS (?:F-[1-9]|PC-[0-9]{2})$') {
                    $safeLines.Add($line)
                }
            }
        }
    $transportExit = [int]$LASTEXITCODE
    if ($ReceiptPath) {
        $safeLines.Add('transport-exit=' + $transportExit)
        [System.IO.File]::WriteAllLines($ReceiptPath, $safeLines)
    }
    return $transportExit
}

# CARD-0628 D-1 / CARD-0737. The vault read is opt-in. A normal deploy-parent does not call bw
# and does not replace the server2 token file. -RefreshClaudeToken, manifest refreshClaudeToken,
# or ANTIPHON_REFRESH_CLAUDE_TOKEN=1 streams vault item antiphon/server2/claude-oauth-token
# (login.password) through the relay session (BW_SESSION, else ~/.bw-session) over SSH STDIN
# into the server2 file at 0600. Never argv, never echoed, never written to a desktop file.
# A locked vault or a missing item skips the refresh and leaves the previous file unchanged.
# The host lane warns when that file is missing or empty. Returns whether a token was delivered.
$script:C628ClaudeTokenItem = 'antiphon/server2/claude-oauth-token'
$script:C628ClaudeTokenRemote = '/home/mc/antiphon-server2/secrets/claude_oauth_token'
$script:C628RefreshSkipped = 'C628 ClaudeOAuthTokenRefreshSkipped: refresh skipped, previous file unchanged'

function Test-C628ClaudeTokenRefreshRequested {
    param($Manifest)
    if ($script:C628RefreshClaudeToken) { return $true }
    if ($env:ANTIPHON_REFRESH_CLAUDE_TOKEN -match '^(?i:1|true)$') { return $true }
    if ($null -eq $Manifest) { return $false }
    $names = @($Manifest.PSObject.Properties.Name)
    if ($names -contains 'refreshClaudeToken' -and [string]$Manifest.refreshClaudeToken -match '^(?i:1|true)$') {
        return $true
    }
    return $false
}

function Invoke-C628ClaudeTokenOnDeploy {
    param(
        $Manifest,
        [switch]$Refresh
    )
    if ($Refresh) { $script:C628RefreshClaudeToken = $true }
    if (-not (Test-C628ClaudeTokenRefreshRequested -Manifest $Manifest)) {
        Write-Host 'Claude token: keeping the existing server2 file (no vault refresh requested)'
        return $false
    }
    return Send-C628ClaudeOAuthToken
}

function Send-C628ClaudeOAuthToken {
    $session = $env:BW_SESSION
    if (-not $session) {
        $pickup = Join-Path $HOME '.bw-session'
        if (Test-Path -LiteralPath $pickup) { $session = (Get-Content -Raw -LiteralPath $pickup).Trim() }
    }
    if (-not $session -or -not (Get-Command bw -ErrorAction SilentlyContinue)) {
        Write-Warning $script:C628RefreshSkipped
        return $false
    }
    $previous = $env:BW_SESSION
    $token = ''
    try {
        $env:BW_SESSION = $session
        $token = (& bw get password $script:C628ClaudeTokenItem --nointeraction 2>$null | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or -not $token) {
            $token = ''
            Write-Warning $script:C628RefreshSkipped
            return $false
        }
        $target = $script:C628ClaudeTokenRemote
        $remote = "umask 077 && mkdir -p /home/mc/antiphon-server2/secrets && cat > '$target.tmp' && chmod 0600 '$target.tmp' && mv -f '$target.tmp' '$target'"
        $token | & ssh -o BatchMode=yes -o ConnectTimeout=30 mc@server2 $remote
        if ($LASTEXITCODE -ne 0) {
            Write-Warning 'C628 ClaudeOAuthTokenNotDelivered: the SSH write failed; the previous token file, if any, is unchanged'
            return $false
        }
        return $true
    }
    finally {
        $token = ''
        $env:BW_SESSION = $previous
    }
}

function New-C590ManifestFile {
    param(
        [string]$EvidenceRoot,
        [string]$RunId,
        [string]$Sha,
        [string]$Filter = '',
        [string]$Project = '',
        [bool]$NoBuild = $false,
        [int]$MinExecuted = 1,
        [string]$Checkpoint = '',
        [bool]$Broker = $false
    )
    $dir = Split-Path -Parent $EvidenceRoot
    New-Item -ItemType Directory -Force -Path $dir, $EvidenceRoot | Out-Null
    $obj = [ordered]@{
        evidenceRoot = $EvidenceRoot
        sourceSha = $Sha
        runId = $RunId
        filter = $Filter
        project = $Project
        noBuild = $NoBuild
        minExecuted = $MinExecuted
        checkpoint = $Checkpoint
        broker = $Broker
    }
    $path = Join-Path $dir ("manifest-" + $Checkpoint + ".json")
    if (-not $Checkpoint) { $path = Join-Path $dir 'manifest.json' }
    ($obj | ConvertTo-Json -Compress) | Set-Content -LiteralPath $path -Encoding ascii
    return $path
}

function Get-C590PlanRow {
    param([Parameter(Mandatory = $true)][string]$Checkpoint)
    $plan = Join-Path (Get-C590RepoRoot) 'docs\superpowers\plans\2026-09-21-card-0590-self-contained-docker-stack-plan.md'
    $hits = @(Select-String -LiteralPath $plan -Pattern ("^\| " + [regex]::Escape($Checkpoint) + " \|") | ForEach-Object { $_.Line })
    if ($hits.Count -eq 0) { throw "plan row missing: $Checkpoint" }
    $line = $hits[$hits.Count - 1]
    $parts = [regex]::Split($line, '(?<!\\)\|')
    if ($parts.Count -lt 9) { throw "plan row unparsed: $Checkpoint" }
    $filter = $parts[5].Trim().Trim('`').Replace('\|', '|')
    $expect = $parts[7].Trim()
    $minMinutes = 0
    [void][int]::TryParse($parts[8].Trim(), [ref]$minMinutes)
    $minExecuted = 1
    $match = [regex]::Match($expect, '>=\s*(\d+)')
    if (-not $match.Success) { $match = [regex]::Match($expect, '(\d+)\s+executed') }
    if (-not $match.Success) { $match = [regex]::Match($expect, '(\d+)\s+case') }
    if ($match.Success) { $minExecuted = [int]$match.Groups[1].Value }
    return [pscustomobject]@{
        Checkpoint = $Checkpoint
        Group = $parts[4].Trim()
        Filter = $filter
        Expect = $expect
        MinMinutes = $minMinutes
        MinExecuted = $minExecuted
    }
}

function Assert-C994RawManifest {
    param([string]$Json)
    try {
        $document = [System.Text.Json.JsonDocument]::Parse($Json)
        function Assert-UniqueJsonObject($Element) {
            if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
                $names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($property in $Element.EnumerateObject()) {
                    if (-not $names.Add($property.Name)) { throw 'TempContainerContextInvalid' }
                    Assert-UniqueJsonObject $property.Value
                }
            } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
                foreach ($item in $Element.EnumerateArray()) { Assert-UniqueJsonObject $item }
            }
        }
        Assert-UniqueJsonObject $document.RootElement
        if ($document.RootElement.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
            foreach ($property in $document.RootElement.EnumerateObject()) {
                if ($property.Name -ceq 'tempContainerCleanup') {
                    # Validate the raw string before ConvertFrom-Json turns an unzoned
                    # timestamp into DateTime and normalization invents a UTC offset.
                    $stamp = $property.Value.GetProperty('retiredAt')
                    if ($stamp.ValueKind -ne [System.Text.Json.JsonValueKind]::String) { throw 'TempContainerContextInvalid' }
                    [void](ConvertTo-C994RetiredInstant $stamp.GetString())
                }
            }
        }
    } catch { throw 'TempContainerContextInvalid' }
    finally { if ($null -ne $document) { $document.Dispose() } }
}

function ConvertTo-C994RetiredInstant {
    param($Value)
    $text = if ($Value -is [datetime]) { $Value.ToUniversalTime().ToString('o') }
        elseif ($Value -is [datetimeoffset]) { $Value.ToUniversalTime().ToString('o') }
        elseif ($Value -is [string]) { $Value } else { throw 'TempContainerContextInvalid' }
    if ($text -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,9})?(Z|[+-]\d{2}:\d{2})$') { throw 'TempContainerContextInvalid' }
    $instant = [datetimeoffset]::MinValue
    if (-not [datetimeoffset]::TryParse($text, [cultureinfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::None, [ref]$instant)) { throw 'TempContainerContextInvalid' }
    return $instant.ToUniversalTime().ToString('o')
}

function Assert-C994BridgeContext {
    param($Context, [string]$Case)
    if ($Case -cne 'retire-temp-containers' -or $null -eq $Context -or $Context -is [array]) { throw 'TempContainerContextInvalid' }
    $expected = @('version','operationId','project','projectId','dryRun','retiredAt')
    $keys = @($Context.PSObject.Properties.Name)
    if ($keys.Count -ne $expected.Count -or @($keys | Where-Object { $_ -cnotin $expected }).Count -ne 0 -or
        ($Context.version -isnot [int] -and $Context.version -isnot [long]) -or $Context.version -ne 1 -or
        $Context.operationId -isnot [string] -or $Context.operationId -cnotmatch '^c994[0-9a-f]{32}$' -or
        $Context.project -isnot [string] -or $Context.project -cne 'antiphon-runner-temp' -or
        $Context.projectId -isnot [string] -or $Context.projectId -cnotmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' -or
        $Context.dryRun -isnot [bool]) { throw 'TempContainerContextInvalid' }
    [void](ConvertTo-C994RetiredInstant $Context.retiredAt)
}

function Assert-C1008BridgeContext {
    param($Context, [string]$Case)
    if ($null -eq $Context -or $Case -notin @('deploy-parent','retire-temp-runner')) { throw 'RecycleContextInvalid' }
    $keys = @($Context.PSObject.Properties.Name)
    $expected = @('version','project','operationId','dryRun','resume','projectId')
    if ($keys -ccontains 'mode') {
        if ($Case -cne 'retire-temp-runner' -or $Context.mode -cne 'manual' -or
            $Context.attestationId -isnot [string] -or $Context.attestationId -cnotmatch '^[0-9a-f]{64}$') { throw 'RecycleContextInvalid' }
        $expected += @('mode','attestationId')
    }
    if ($keys -ccontains 'expectedProofDigest') {
        if ($Case -cne 'retire-temp-runner' -or $Context.dryRun -ne $false -or
            $Context.expectedProofDigest -isnot [string] -or $Context.expectedProofDigest -cnotmatch '^[0-9a-f]{64}$') { throw 'RecycleContextInvalid' }
        $expected += 'expectedProofDigest'
    }
    if ($Context.mode -ceq 'manual' -and -not $Context.dryRun -and $keys -cnotcontains 'expectedProofDigest') { throw 'RecycleContextInvalid' }
    if ($keys -ccontains 'cleanupOperationId') {
        if ($Case -cne 'retire-temp-runner' -or $Context.resume -ne $false -or
            $Context.cleanupOperationId -isnot [string] -or $Context.cleanupOperationId -cnotmatch '^c994[0-9a-f]{32}$') { throw 'RecycleContextInvalid' }
        $expected += 'cleanupOperationId'
    }
    if ($keys.Count -ne $expected.Count -or @($keys | Where-Object { $_ -cnotin $expected }).Count) { throw 'RecycleContextInvalid' }
    $project = if ($Case -eq 'deploy-parent') { 'antiphon-runner' } else { 'antiphon-runner-temp' }
    if (($Context.version -isnot [int] -and $Context.version -isnot [long]) -or $Context.version -ne 1 -or
        [string]$Context.project -cne $project -or [string]$Context.operationId -cnotmatch '^c1008[0-9a-f]{32}$' -or
        $Context.dryRun -isnot [bool] -or $Context.resume -isnot [bool] -or
        [string]$Context.projectId -cnotmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' -or
        ($Context.dryRun -and $Context.resume)) { throw 'RecycleContextInvalid' }
}

# CARD-1607. Inventory is a separate remote case. Its private-origin list comes
# back as a file. The desktop ls-remote runs here, and only ref lines cross back.
function Get-C1609ProofSetDigest([string]$Directory) {
    $lines=[Collections.Generic.List[string]]::new()
    foreach($file in [IO.Directory]::EnumerateFileSystemEntries($Directory)){
        $name=[IO.Path]::GetFileName($file)
        if($name -ceq 'host-proof.log'){continue}
        if($name -cnotmatch '^[0-9a-f]{64}(\.bundle)?$' -or
            ([IO.FileInfo]::new($file).Attributes -band ([IO.FileAttributes]::ReparsePoint -bor [IO.FileAttributes]::Directory))){throw 'proof-custody'}
        $lines.Add($name+' '+(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant())
    }
    if(-not $lines.Count){throw 'proof-empty'}
    $array=$lines.ToArray();[Array]::Sort($array,[StringComparer]::Ordinal)
    return Get-C1008Sha256 (($array -join [char]10)+[char]10)
}

function Invoke-C1609RetirementCoordinator {
    param([string]$Remote,[string]$EvidenceRoot,[string]$Run,[string]$Operation,[string]$ProjectId,[string]$ReceiptPath)
    $psi=[Diagnostics.ProcessStartInfo]::new('ssh');$psi.UseShellExecute=$false
    $psi.RedirectStandardInput=$true;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
    foreach($arg in @('-o','BatchMode=yes','-o','ConnectTimeout=30','-o','ServerAliveInterval=30','-o','ServerAliveCountMax=240','mc@server2',$Remote)){[void]$psi.ArgumentList.Add($arg)}
    $proc=[Diagnostics.Process]::new();$proc.StartInfo=$psi;$started=$false;$code=2
    $watch=[Diagnostics.Stopwatch]::StartNew();$networkMs=0L;$previousDirectory='';$previousDigest='none'
    $objectDigest=Get-C1008Sha256 ''; $nonces=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $lineBuffer='';$exitedAt=$null;$phaseBinding=''
    try {
        [void]$proc.Start();$started=$true;$proc.StandardInput.NewLine="`n"
        $streams=@(@{reader=$proc.StandardOutput;buffer=[char[]]::new(4096);task=$null;done=$false;count=0;protocol=$true},
            @{reader=$proc.StandardError;buffer=[char[]]::new(4096);task=$null;done=$false;count=0;protocol=$false})
        foreach($stream in $streams){$stream.task=$stream.reader.ReadAsync($stream.buffer,0,$stream.buffer.Length)}
        while(@($streams|Where-Object{-not $_.done}).Count){
            # Content has its own 10800s cap, network work shares 600s, and each
            # confirmation is bounded. This outer cap cannot shorten a valid scan.
            if($watch.Elapsed.TotalSeconds -gt 26000){throw 'deadline'}
            if($proc.HasExited){if($null -eq $exitedAt){$exitedAt=$watch.ElapsedMilliseconds};if($watch.ElapsedMilliseconds-$exitedAt -gt 2000){throw 'drain'}}
            foreach($stream in $streams){
                if($stream.done -or -not $stream.task.IsCompleted){continue}
                $n=$stream.task.GetAwaiter().GetResult();if($n -eq 0){$stream.done=$true;continue}
                $stream.count+=$n;if($stream.count -gt 16777216){throw 'output-bound'}
                if($stream.protocol){
                    $lineBuffer += [string]::new($stream.buffer,0,$n)
                    while(($newline=$lineBuffer.IndexOf([char]10)) -ge 0){
                        $line=$lineBuffer.Substring(0,$newline);$lineBuffer=$lineBuffer.Substring($newline+1)
                        if($line.Length -gt 8192){throw 'line-bound'}
                        $response=''
                        if($line -cmatch '^C1609_PROOF_REQUEST ([0-9a-f]{32}) ([0-9a-f]{32}) (initial|renew) (none|[0-9a-f]{64}) ([0-9a-f]{64})$'){
                            $exchange=$Matches[1];$phase=$Matches[2];$kind=$Matches[3];$prior=$Matches[4];$requiredDigest=$Matches[5]
                            if(-not $phaseBinding){$phaseBinding=$phase};if($phaseBinding -cne $phase){throw 'phase-binding'}
                            if(-not $nonces.Add($exchange) -or $prior -cne $previousDigest -or
                                ($kind -ceq 'initial' -and $previousDirectory) -or ($kind -ceq 'renew' -and -not $previousDirectory)){throw 'exchange-binding'}
                            $remaining=600000-[int]$networkMs;if($remaining -le 0){throw 'network-deadline'}
                            $destination=Join-Path $EvidenceRoot ('exchange-'+$exchange);[void][IO.Directory]::CreateDirectory($destination)
                            $network=[Diagnostics.Stopwatch]::StartNew()
                            $exports=@(Get-C1008RetireProofExports -EvidenceRoot $destination -Run $Run -RecycleOperation $Operation `
                                -InventoryRemote 'true' -ProjectId $ProjectId -Coordinated -NetworkBudgetMs $remaining `
                                -PreviousProofDirectory $previousDirectory -RequiredObjectDigest $requiredDigest -RequireObjectCoverage:($kind -ceq 'renew') -InventoryPhaseNonce $phase)
                            $networkMs += $network.ElapsedMilliseconds;if($networkMs -gt 600000 -or $exports.Count -ne 4){throw 'network-deadline'}
                            $values=@{}
                            foreach($export in $exports){if($export -cnotmatch "^export (C1008_HOST_PROOF|C1008_OPERATION_STARTED|C1609_PROOF_NONCE|C1609_PROOF_INVENTORY)='([A-Za-z0-9._/-]+)'$"){throw 'proof-response'};$values[$Matches[1]]=$Matches[2]}
                            if($values.Count -ne 4){throw 'proof-response'}
                            $name=[IO.Path]::GetFileName($values.C1008_HOST_PROOF)
                            $previousDirectory=Join-Path $destination $name
                            $previousDigest=Get-C1609ProofSetDigest $previousDirectory;$objectDigest=$requiredDigest
                            $response='C1609_PROOF_RESPONSE '+$exchange+' '+$name+' '+$values.C1008_OPERATION_STARTED+' '+$values.C1609_PROOF_NONCE+' '+$values.C1609_PROOF_INVENTORY+' '+$previousDigest+' '+$prior+' '+$requiredDigest
                            [IO.File]::AppendAllText((Join-Path $EvidenceRoot 'coordinator-exchanges.jsonl'),
                                ((@{nonce=$exchange;kind=$kind;objectsDigest=$objectDigest;proofDigest=$previousDigest;networkMs=$network.ElapsedMilliseconds}|ConvertTo-Json -Compress)+"`n"))
                        } elseif($line -cmatch '^C1609_CONTROL_REQUEST ([0-9a-f]{32}) (ready|apply|audit|antiphon-runner-temp_(runner-state|dind-data|runner-tmp|work)) (none|[0-9a-f]{64}) ([0-9a-f]{64})$'){
                            $exchange=$Matches[1];$purpose=$Matches[2];$proof=$Matches[4];$objects=$Matches[5]
                            if(-not $nonces.Add($exchange) -or $proof -cne $previousDigest -or $objects -cne $objectDigest){throw 'control-binding'}
                            $response='C1609_CONTROL_RESPONSE '+$exchange+' '+$purpose+' '+$proof+' '+$objects
                        } elseif($line.StartsWith('C1609_PROOF_REQUEST') -or $line.StartsWith('C1609_CONTROL_REQUEST')){throw 'protocol'}
                        if($response){
                            if($proc.HasExited){throw 'controller-eof'}
                            $write=$proc.StandardInput.WriteLineAsync($response);if(-not $write.Wait(10000)){throw 'response-deadline'};$write.GetAwaiter().GetResult();$proc.StandardInput.Flush()
                        }
                    }
                    if($lineBuffer.Length -gt 8192){throw 'line-bound'}
                }
                $stream.task=$stream.reader.ReadAsync($stream.buffer,0,$stream.buffer.Length)
            }
            [Threading.Thread]::Sleep(10)
        }
        if(-not $proc.WaitForExit(2000)){throw 'exit'};$code=$proc.ExitCode
    } catch { $code=2 }
    finally {
        if($started){try{$proc.StandardInput.Close()}catch{};if(-not $proc.HasExited){$proc.Kill($true);[void]$proc.WaitForExit(2000)}}
        $proc.Dispose()
        [IO.File]::WriteAllText($ReceiptPath,('transport-exit='+$code+"`n"))
    }
    return $code
}

function Get-C1008RetireProofExports {
    param(
        [Parameter(Mandatory = $true)][string]$EvidenceRoot,
        [Parameter(Mandatory = $true)][string]$Run,
        [Parameter(Mandatory = $true)][string]$RecycleOperation,
        [Parameter(Mandatory = $true)][string]$InventoryRemote,
        [string]$ProjectId = '', [switch]$Coordinated, [int]$NetworkBudgetMs = 600000,
        [string]$PreviousProofDirectory = '', [string]$RequiredObjectDigest = '', [switch]$RequireObjectCoverage,
        [string]$InventoryPhaseNonce = ''
    )
    $networkWatch=[Diagnostics.Stopwatch]::StartNew()
    function Invoke-C1609ProofSsh([string]$Command,[string]$Receipt='') {
        if(-not $Coordinated){return Invoke-C590Ssh $Command -ReceiptPath $Receipt}
        try {
            $remaining=[Math]::Min(30000,$NetworkBudgetMs-[int]$networkWatch.ElapsedMilliseconds)
            $r=Invoke-C1609BoundedProcess -FileName ssh -Arguments @('-o','BatchMode=yes','-o','ConnectTimeout=15','mc@server2',$Command) -TimeoutMs $remaining
            return $r.ExitCode
        } catch {return 2}
    }
    function Invoke-C1609ProofScp([string[]]$Arguments) {
        if(-not $Coordinated){& scp @Arguments;return $LASTEXITCODE}
        try {
            $remaining=[Math]::Min(300000,$NetworkBudgetMs-[int]$networkWatch.ElapsedMilliseconds)
            $r=Invoke-C1609BoundedProcess -FileName scp -Arguments $Arguments -TimeoutMs $remaining
            return $r.ExitCode
        } catch {return 2}
    }
    if ($Run -cnotmatch '^[a-z0-9]+$' -or $RecycleOperation -cnotmatch '^c1008[0-9a-f]{32}$') {
        Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleGitAuditUnknown' -ExitCode 2
    }
    $code = Invoke-C1609ProofSsh $InventoryRemote (Join-Path $EvidenceRoot 'inventory-remote-output.txt')
    $inventoryParent = $EvidenceRoot
    $inventoryName='retire-temp-inventory';$inventorySource="mc@server2:/work/test-evidence/$Run/retire-temp-inventory"
    if($Coordinated){
        if($InventoryPhaseNonce -cnotmatch '^[0-9a-f]{32}$'){throw 'phase-binding'}
        $inventoryName='inventory-'+$InventoryPhaseNonce
        $inventorySource="mc@server2:/home/mc/antiphon-server2/retirement/$RecycleOperation/$inventoryName"
    }
    $copyCode=Invoke-C1609ProofScp @('-o','BatchMode=yes','-r',$inventorySource,$inventoryParent)
    if ($copyCode -ne 0) {
        Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleReceiptUnavailable' -ExitCode 2
    }
    $inventoryDir = Join-Path $EvidenceRoot $inventoryName
    $resultPath = Join-Path $inventoryDir 'c590-result.json'
    if (-not (Test-Path -LiteralPath $resultPath)) {
        Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleGitAuditUnknown' -ExitCode 2
    }
    $result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
    if ($code -ne 0 -or -not [bool]$result.accepted) {
        $diagnosis = [string]$result.diagnosis
        if (-not (Test-C1008SafeAuditDiagnosis -Diagnosis $diagnosis)) { $diagnosis = 'RecycleGitAuditUnknown' }
        Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis $diagnosis -ExitCode 2
    }
    $listPath = Join-Path $inventoryDir 'private-origins.txt'
    if (-not (Test-Path -LiteralPath $listPath)) {
        Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleGitAuditUnknown' -ExitCode 2
    }
    try {
        $rows = @(Read-C1008PrivateOriginLines -Text ([System.IO.File]::ReadAllText($listPath)))
    }
    catch {
        Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleGitAuditUnknown' -ExitCode 2
    }
    if ($rows.Count -eq 0) { return @() }
    $requiredByOrigin=@{}
    try {
        $allowPath = Join-Path $inventoryDir 'trusted-origins.json'
        $allowText = [IO.File]::ReadAllText($allowPath)
        Assert-C994RawManifest $allowText
        $allow = $allowText | ConvertFrom-Json
        if ($allow.schema -ne 1 -or $allow.projectId -cne $ProjectId -or $ProjectId -cnotmatch '^[0-9a-f-]{36}$' -or $allow.origins -isnot [array] -or
            (($allow.PSObject.Properties.Name | Sort-Object) -join ',') -cne 'origins,projectId,schema') { throw 'registry' }
        $requestLines = [string[]]@($rows | ForEach-Object { 'C1008_PRIVATE_ORIGIN repo='+$_.Repo+' origin='+$_.Origin+' originHash='+$_.OriginHash+' proofName='+$_.ProofName })
        [Array]::Sort($requestLines,[StringComparer]::Ordinal)
        $requestDigest = Get-C1008Sha256 (($requestLines -join [char]10)+[char]10)
        foreach($row in $rows){if(-not $requiredByOrigin.ContainsKey($row.OriginHash)){$requiredByOrigin[$row.OriginHash]=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)}}
        if($Coordinated){
            $requiredFile=Join-Path $inventoryDir 'published-objects.txt'
            if(([IO.FileInfo]::new($requiredFile)).Length -gt 16777216){throw 'required-size'}
            $requiredText=[IO.File]::ReadAllText($requiredFile)
            if((Get-C1008Sha256 $requiredText) -cne $RequiredObjectDigest){throw 'required-digest'}
            $repos=@{};foreach($row in $rows){$repos[$row.Repo]=$row.OriginHash}
            $covered=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach($line in $requiredText.Split([char]10)){
                if($line -ceq ''){continue}
                if($line -cnotmatch '^C1609_PROVED_OBJECT repo=([A-Za-z0-9._/-]+) object=([0-9a-f]{40}) type=(commit|tag|tree|blob)$'){throw 'required-shape'}
                $repo=$Matches[1];$oid=$Matches[2];$type=$Matches[3]
                if(-not $repos.ContainsKey($repo) -or -not $seen.Add($repo+':'+$oid)){throw 'required-coverage'}
                [void]$covered.Add($repo);$group=$requiredByOrigin[$repos[$repo]]
                if($group.ContainsKey($oid) -and $group[$oid] -cne $type){throw 'required-type'};$group[$oid]=$type
            }
            if($RequireObjectCoverage -and $covered.Count -ne $rows.Count){throw 'required-coverage'}
        }
    } catch { Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleGitAuditUnknown' -ExitCode 2 }
    $phaseNonce = [guid]::NewGuid().ToString('N')
    $started = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $startedText = $started.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $remoteName = 'c1008-proof-' + $RecycleOperation + '-' + $startedText + '-' + $phaseNonce
    $remoteDir = '/home/mc/antiphon-c590/' + $remoteName
    if ($remoteName -cnotmatch '^c1008-proof-c1008[0-9a-f]{32}-[0-9]+-[0-9a-f]{32}$' -or
        $remoteDir -cnotmatch '^/home/mc/antiphon-c590/c1008-proof-c1008[0-9a-f]{32}-[0-9]+-[0-9a-f]{32}$') {
        Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleGitAuditUnknown' -ExitCode 2
    }
    $localDir = Join-Path $EvidenceRoot $remoteName
    if (Test-Path -LiteralPath $localDir) {
        Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleGitAuditUnknown' -ExitCode 2
    }
    [void](New-Item -ItemType Directory -Path $localDir)
    $mkdirCode = Invoke-C1609ProofSsh ('mkdir ' + $remoteDir + ' && chmod 755 ' + $remoteDir)
    if ($mkdirCode -ne 0) {
        Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleGitAuditUnknown' -ExitCode 2
    }
    $logPath = Join-Path $localDir 'host-proof.log'
    $phaseWatch = [Diagnostics.Stopwatch]::StartNew()
    $graphs = @{}
    foreach ($row in $rows) {
        try {
            if ($graphs.ContainsKey($row.OriginHash)) {
                Copy-C1609HostOriginProof -Directory $localDir -SourceName $graphs[$row.OriginHash] -Repo $row.Repo -Origin $row.Origin
            } else {
                $remaining = $NetworkBudgetMs - [int]$phaseWatch.ElapsedMilliseconds
                if($Coordinated){$remaining=[Math]::Min($remaining,$NetworkBudgetMs-[int]$networkWatch.ElapsedMilliseconds)}
                $required=@(foreach($pair in $requiredByOrigin[$row.OriginHash].GetEnumerator()){[pscustomobject]@{objectId=$pair.Key;type=$pair.Value}})
                $previousFile=if($PreviousProofDirectory){Join-Path $PreviousProofDirectory $row.ProofName}else{''}
                Write-C1008HostOriginProof -OriginUrl $row.Origin -RepoRelative $row.Repo -OperationId $RecycleOperation -ProofDirectory $localDir -CreatedUnix $started -LogPath $logPath -ProofVersion 2 -PhaseNonce $phaseNonce -InventoryDigest $requestDigest -ApprovedOrigins $allow.origins -NetworkBudgetMs $remaining -RequiredObjects $required -PreviousProofFile $previousFile
                $graphs[$row.OriginHash] = $row.ProofName
            }
        }
        catch {
            $msg = [string]$_.Exception.Message
            if ($msg -cnotmatch '^RecycleGitAuditUnknown audit check=host-ls-remote status=[0-9]+ repo=[A-Za-z0-9._/-]+$') {
                $msg = 'RecycleGitAuditUnknown'
            }
            Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis $msg -ExitCode 2
        }
        if ($row.ProofName -cnotmatch '^[0-9a-f]{64}$') {
            Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleGitAuditUnknown' -ExitCode 2
        }
        $proofFile = Join-Path $localDir ([string]$row.ProofName)
        if (-not (Test-Path -LiteralPath $proofFile)) {
            Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleGitAuditUnknown' -ExitCode 2
        }
        $copyCode=Invoke-C1609ProofScp @('-o','BatchMode=yes',$proofFile,('mc@server2:' + $remoteDir + '/' + $row.ProofName))
        if ($copyCode -ne 0) {
            Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleReceiptUnavailable' -ExitCode 2
        }
        $bundleFile = Join-Path $localDir ([string]$row.ProofName + '.bundle')
        if (Test-Path -LiteralPath $bundleFile) {
            $copyCode=Invoke-C1609ProofScp @('-o','BatchMode=yes',$bundleFile,('mc@server2:' + $remoteDir + '/' + $row.ProofName + '.bundle'))
            if ($copyCode -ne 0) {
                Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleReceiptUnavailable' -ExitCode 2
            }
            $bundleMode = Invoke-C1609ProofSsh ('chmod 644 ' + $remoteDir + '/' + $row.ProofName + '.bundle')
            if ($bundleMode -ne 0) {
                Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleGitAuditUnknown' -ExitCode 2
            }
        }
        $chmodCode = Invoke-C1609ProofSsh ('chmod 644 ' + $remoteDir + '/' + $row.ProofName)
        if ($chmodCode -ne 0) {
            Write-C590Result -EvidenceRoot $EvidenceRoot -Accepted $false -Diagnosis 'RecycleGitAuditUnknown' -ExitCode 2
        }
    }
    return @(
        ("export C1008_HOST_PROOF='" + $remoteDir + "'"),
        ("export C1008_OPERATION_STARTED='" + $startedText + "'"),
        ("export C1609_PROOF_NONCE='" + $phaseNonce + "'"),
        ("export C1609_PROOF_INVENTORY='" + $requestDigest + "'")
    )
}

function Invoke-C590LiveCase {
    param(
        [Parameter(Mandatory = $true)][string]$Case,
        [Parameter(Mandatory = $true)]$Manifest, [string]$RawManifest = ''
    )
    $root = [string]$Manifest.evidenceRoot
    if (-not $root) { throw 'evidenceRoot is required' }
    $cleanup = $null
    $cleanupExports = @()
    $cleanupOperation = ''
    if ($Case -ceq 'retire-temp-containers' -or $Manifest.PSObject.Properties.Name -ccontains 'tempContainerCleanup') {
        Assert-C994RawManifest -Json $RawManifest
        Assert-C994BridgeContext -Context $Manifest.tempContainerCleanup -Case $Case
        $cleanup = $Manifest.tempContainerCleanup
        $cleanupDry = if ($cleanup.dryRun) { '1' } else { '0' }
        $cleanupStamp = ConvertTo-C994RetiredInstant $cleanup.retiredAt
        $cleanupExports = @("export C994_VERSION='1'", "export C994_OPERATION='$($cleanup.operationId)'",
            "export C994_PROJECT='$($cleanup.project)'", "export C994_PROJECT_ID='$($cleanup.projectId)'",
            "export C994_DRY_RUN='$cleanupDry'", "export C994_RETIRED_AT='$cleanupStamp'")
    }
    $recycleContext = ''
    $recycleOperation = ''
    $recycleProjectId = ''
    $recycleDryRun = '0'
    $recycleResume = '0'
    $manualExports = @()
    if ($Manifest.PSObject.Properties.Name -contains 'recycle') {
        Assert-C1008BridgeContext -Context $Manifest.recycle -Case $Case
        if ($Manifest.recycle.PSObject.Properties.Name -ccontains 'cleanupOperationId') { $cleanupOperation = $Manifest.recycle.cleanupOperationId }
        $recycleContext = 'default'
        $recycleOperation = [string]$Manifest.recycle.operationId
        $recycleProjectId = [string]$Manifest.recycle.projectId
        if ($Manifest.recycle.dryRun) { $recycleDryRun = '1' }
        if ($Manifest.recycle.resume) { $recycleResume = '1' }
        if ($Manifest.recycle.mode -ceq 'manual') {
            . (Join-Path $PSScriptRoot 'lib/runner-operator-token.ps1')
            try { $null = Get-RunnerOperatorToken } catch { throw 'ManualRetirementAuthority' }
            $manualExports = @("export C1008_MODE='manual'",
                "export C1008_MANUAL_AUTHORITY_FILE='/home/mc/antiphon-server2/manual-authority/$recycleOperation.json'",
                "export C1008_ATTESTATION_FILE='/home/mc/antiphon-server2/manual-authority/$recycleOperation.$($Manifest.recycle.attestationId).json'")
        }
        if ($Manifest.recycle.PSObject.Properties.Name -ccontains 'expectedProofDigest') {
            $manualExports += "export C1008_EXPECTED_PROOF_DIGEST='$($Manifest.recycle.expectedProofDigest)'"
        }
    }
    if ($Case -in @('runner-cache-inventory', 'runner-cache-fixture', 'runner-cache-seed', 'runner-cache-reset',
            'verify-runner-caches', 'verify-runner-caches-retired', 'runner-cache-prune-preview', 'runner-cache-prune')) {
        $cacheNames = @($Manifest.PSObject.Properties.Name)
        if ($cacheNames -notcontains 'sourceSha' -or [string]$Manifest.sourceSha -cnotmatch '^[0-9a-f]{40}$' -or
            $cacheNames -notcontains 'runId' -or [string]$Manifest.runId -cnotmatch '^[a-z0-9]{1,64}$') {
            throw 'CacheManifestInvalid'
        }
        $cacheRoot = [System.IO.Path]::GetFullPath((Join-Path (Get-C590RepoRoot) '.antiphon'))
        $candidate = [System.IO.Path]::GetFullPath($root)
        if (-not $candidate.StartsWith($cacheRoot + [System.IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) { throw 'CacheEvidencePathInvalid' }
        if ($cacheNames -contains 'runnerId' -and [string]$Manifest.runnerId -notin @('server2', 'server2-temp')) {
            throw 'CacheRunnerInvalid'
        }
        if ($Case -eq 'verify-runner-caches' -and $cacheNames -notcontains 'runnerId') {
            throw 'CacheRunnerInvalid'
        }
        if ($Case -eq 'runner-cache-prune' -and
            ($cacheNames -notcontains 'previewRunId' -or [string]$Manifest.previewRunId -cnotmatch '^c849[0-9a-f]{16}0$')) {
            throw 'CachePreviewInvalid'
        }
    }
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $run = Get-C590RunId -Manifest $Manifest
    $sha = Get-C590Sha -Manifest $Manifest
    $names = @($Manifest.PSObject.Properties.Name)
    $project = ''
    $noBuild = '0'
    $minExecuted = '1'
    $checkpoint = $Case
    $broker = '0'
    if ($names -contains 'project' -and $Manifest.project) { $project = [string]$Manifest.project }
    if ($names -contains 'noBuild' -and [bool]$Manifest.noBuild) { $noBuild = '1' }
    if ($names -contains 'minExecuted' -and $Manifest.minExecuted) { $minExecuted = [string]$Manifest.minExecuted }
    if ($names -contains 'checkpoint' -and $Manifest.checkpoint) { $checkpoint = [string]$Manifest.checkpoint }
    if ($names -contains 'broker' -and [bool]$Manifest.broker) { $broker = '1' }
    # CARD-0604 D-13. The branch the runner's checkout tracks (master once this cut has landed) and
    # the production origin the host lane probes for runner status. Both are manifest-supplied so a
    # pre-land run can point the runner at the task branch without editing this script.
    $c604Branch = 'master'
    $c604Origin = 'https://antiphon.desktop.codeperf.net'
    if ($names -contains 'c604Branch' -and $Manifest.c604Branch) { $c604Branch = [string]$Manifest.c604Branch }
    if ($names -contains 'c604Origin' -and $Manifest.c604Origin) { $c604Origin = [string]$Manifest.c604Origin }
    if ($c604Branch -notmatch '^[A-Za-z0-9._/-]{1,200}$') { throw 'c604 branch rejected' }
    if ($c604Origin -notmatch '^https?://[A-Za-z0-9._:-]{1,200}$') { throw 'c604 origin rejected' }
    if ($project -notmatch '^[A-Za-z0-9./_-]*$') { throw 'project path rejected' }
    if ($checkpoint -notmatch '^[A-Za-z0-9_-]+$') { throw 'checkpoint name rejected' }
    if ($run -notmatch '^[a-z0-9]+$') { throw 'run id rejected' }
    if ($sha -notmatch '^[0-9a-f]{40}$') { throw 'source sha rejected' }

    $tokenCopied = $false
    $tempRetiredAt = ''
    $runnerId = ''
    $previewRunId = ''
    $savedDonor = ''
    $coldSeed = '0'
    if ($names -contains 'coldSeed') {
        if ($Manifest.coldSeed -isnot [bool] -or $Case -ne 'runner-cache-seed') {
            throw 'CacheColdModeInvalid'
        }
        if ($Manifest.coldSeed) { $coldSeed = '1' }
    }
    if ($names -contains 'savedDonor' -and $Manifest.savedDonor) {
        if ($Case -ne 'runner-cache-seed') { throw 'CacheSavedDonorCaseInvalid' }
        $savedDonor = [string]$Manifest.savedDonor
        if ($savedDonor -cnotmatch '^/[A-Za-z0-9._/-]{1,500}$' -or $savedDonor.Contains('..') -or
            $savedDonor.Contains('//')) { throw 'CacheSavedDonorPathInvalid' }
    }
    if ($coldSeed -eq '1' -and $savedDonor) { throw 'CacheDonorSourceConflict' }
    if ($names -contains 'previewRunId') { $previewRunId = [string]$Manifest.previewRunId }
    if ($previewRunId -and $previewRunId -cnotmatch '^c849[0-9a-f]{16}0$') { throw 'CachePreviewInvalid' }
    $expectAccepting = '0'
    if ($names -contains 'expectAccepting' -and $Manifest.expectAccepting -eq $true) { $expectAccepting = '1' }
    if ($names -contains 'runnerId' -and $Manifest.runnerId) { $runnerId = [string]$Manifest.runnerId }
    if ($Case -eq 'verify-runner-caches' -and $runnerId -notin @('server2', 'server2-temp')) {
        throw 'runnerId rejected'
    }
    if ($runnerId -and $runnerId -notin @('server2', 'server2-temp')) { throw 'runnerId rejected' }
    if ($Case -eq 'retire-temp-runner') {
        if ($names -contains 'tempRetiredAt' -and $Manifest.tempRetiredAt) {
            # CARD-0780. ConvertFrom-Json returns ISO-8601 text as [datetime]; [string] gives a
            # culture date that the guard rejects. Restore the runner's ISO-8601 UTC form.
            $rawRetiredAt = $Manifest.tempRetiredAt
            $tempRetiredAt = if ($rawRetiredAt -is [datetime]) {
                $rawRetiredAt.ToUniversalTime().ToString('o')
            } else { [string]$rawRetiredAt }
        }
        if ($tempRetiredAt -and $tempRetiredAt -notmatch '^[0-9TZ:+.-]{10,40}$') { throw 'tempRetiredAt rejected' }
    }
    try {
        if ($Case -eq 'deploy-parent' -and $recycleDryRun -ne '1') {
            [void](Invoke-C628ClaudeTokenOnDeploy -Manifest $Manifest)
        }

        if ($Case -eq 'git-credential-smoke') {
            $token = (& gh auth token | Out-String).Trim()
            if (-not $token -or $token.Length -lt 10) {
                Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'GitHubTokenUnavailable' -ExitCode 2
            }
            $localToken = Join-Path (Get-C590StateDir) 'gh-token'
            [System.IO.File]::WriteAllText($localToken, $token)
            $token = ''
            [void](Invoke-C590Ssh "mkdir -p /home/mc/antiphon-c590/secrets && chmod 700 /home/mc/antiphon-c590/secrets")
            & scp -o BatchMode=yes $localToken mc@server2:/home/mc/antiphon-c590/secrets/gh-token
            Remove-Item -LiteralPath $localToken -Force
            [void](Invoke-C590Ssh "chmod 600 /home/mc/antiphon-c590/secrets/gh-token")
            $tokenCopied = $true
        }

        if ($names -contains 'filter' -and $Manifest.filter) {
            $filterPath = Join-Path (Get-C590StateDir) 'filter.txt'
            [System.IO.File]::WriteAllText($filterPath, [string]$Manifest.filter)
            & scp -o BatchMode=yes $filterPath ("mc@server2:/tmp/c590-filter-" + $run + ".txt")
            Remove-Item -LiteralPath $filterPath -Force
        }

        $remoteScript = '/home/mc/antiphon-c590/c590-remote.sh'
        if ($Case -cin @('retire-temp-containers', 'retire-temp-runner', 'retire-temp-inventory', 'deploy-parent', 'deploy-temp-runner')) {
            # One private immutable bundle per invocation. No concurrent request
            # can overwrite a running retirement's script or its current helpers.
            $stage = '/home/mc/antiphon-c590/retirement-tools/' + [guid]::NewGuid().ToString('N')
            $prepare = @'
set -eu
umask 077
parent=/home/mc/antiphon-c590/retirement-tools
[ ! -L "$parent" ]
mkdir -p -- "$parent"
[ "$(readlink -e -- "$parent")" = "$parent" ]
[ "$(stat -c %u -- "$parent")" = "$(id -u)" ]
mode="$(stat -c %a -- "$parent")"
(( (8#$mode & 022) == 0 ))
mkdir -- '{0}'
'@ -f $stage
            $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($prepare))
            if ((Invoke-C590Ssh "printf '%s' '$encoded' | base64 -d | bash") -ne 0) {
                Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'RecycleReceiptUnavailable' -ExitCode 2
            }
            $hashRows = @()
            foreach ($name in @('c590-remote.sh', 'c1008-retirement.sh', 'c1008-content-audit.sh', 'c1008-volume-metadata.sh')) {
                $local = Join-Path $PSScriptRoot $name
                $hashRows += (Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name
                & scp -o BatchMode=yes -o ConnectTimeout=15 $local ('mc@server2:' + $stage + '/' + $name)
                if ($LASTEXITCODE -ne 0) {
                    Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'RecycleReceiptUnavailable' -ExitCode 2
                }
            }
            $seal = "set -eu`ncd '$stage'`nprintf '%s\n' '" + ($hashRows -join "`n") + "' > tooling.sha256`n" +
                "sha256sum -c tooling.sha256 >/dev/null`nchmod 0400 -- ./*.sh tooling.sha256`n"
            $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($seal))
            if ((Invoke-C590Ssh "printf '%s' '$encoded' | base64 -d | bash") -ne 0) {
                Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'RecycleReceiptUnavailable' -ExitCode 2
            }
            $remoteScript = $stage + '/c590-remote.sh'
        }
        else {
            [void](Invoke-C590Ssh "mkdir -p /home/mc/antiphon-c590")
            & scp -o BatchMode=yes (Join-Path $PSScriptRoot 'c590-remote.sh') mc@server2:/home/mc/antiphon-c590/c590-remote.sh
        }
        $remoteEntry = "bash '$remoteScript'"
        # CARD-1316. The shell resolver accepts the same positive integer. Reject a
        # bad desktop override before it is quoted into the remote command.
        $auditTimeoutExport = @()
        if (-not [string]::IsNullOrEmpty($env:C1008_AUDIT_TIMEOUT_SECONDS)) {
            if ($env:C1008_AUDIT_TIMEOUT_SECONDS -notmatch '^[1-9][0-9]{0,6}$') { throw 'AuditTimeoutInvalid' }
            $auditTimeoutExport = @("export C1008_AUDIT_TIMEOUT_SECONDS='$($env:C1008_AUDIT_TIMEOUT_SECONDS)'")
        }
        if ($Case -eq 'runner-cache-seed') {
            & scp -o BatchMode=yes (Join-Path $PSScriptRoot 'c849-import-saved-donor.ps1') mc@server2:/home/mc/antiphon-c590/c849-import-saved-donor.ps1
        }
        $bridgeExports = @($cleanupExports) + @($manualExports) + @(
            "export C590_CASE='$Case'"
            "export C590_SHA='$sha'"
            "export C590_RUN='$run'"
            "export C590_PROJECT='$project'"
            "export C590_NOBUILD='$noBuild'"
            "export C590_MIN='$minExecuted'"
            "export C590_CP='$checkpoint'"
            "export C590_BROKER='$broker'"
            "export C590_FILTER_FILE='/tmp/c590-filter-$run.txt'"
            # CARD-0604: the branch the runner's own checkout tracks (master after land), and the
            # production origin the host lane probes for runner status. Never a secret: the
            # phone-home shared secret is generated on server2 and never crosses this bridge.
            "export C604_BRANCH='$c604Branch'"
            "export C604_SERVER_ORIGIN='$c604Origin'"
            "export C590_TEMP_RETIRED_AT='$tempRetiredAt'"
            "export C590_RUNNER_ID='$runnerId'"
            "export C590_PREVIEW_RUN='$previewRunId'"
            "export C590_SAVED_DONOR='$savedDonor'"
            "export C590_COLD_SEED='$coldSeed'"
            "export C590_EXPECT_ACCEPTING='$expectAccepting'"
            "export C1008_CONTEXT='$recycleContext'"
            "export C1008_OPERATION='$recycleOperation'"
            "export C1008_PROJECT_ID='$recycleProjectId'"
            "export C1008_DRY_RUN='$recycleDryRun'"
            "export C1008_RESUME='$recycleResume'"
            "export C1008_CLEANUP_OPERATION='$cleanupOperation'"
        ) + $auditTimeoutExport
        if ($Case -ceq 'retire-temp-runner' -and $recycleContext -ceq 'default') {
            $remote = ($bridgeExports + @("export C1609_EXCHANGE='1'","export C1609_EXCHANGE_PROOF_ROOT='/home/mc/antiphon-c590'",$remoteEntry)) -join '; '
            $code = Invoke-C1609RetirementCoordinator -Remote $remote -EvidenceRoot $root -Run $run -Operation $recycleOperation -ProjectId $recycleProjectId -ReceiptPath (Join-Path $root 'remote-output.txt')
        } else {
            $remote = ($bridgeExports + @($remoteEntry)) -join '; '
            $code = Invoke-C590Ssh $remote -ReceiptPath (Join-Path $root 'remote-output.txt')
        }
        $destParent = $root
        & scp -o BatchMode=yes -r ("mc@server2:/work/test-evidence/" + $run + "/" + $Case) $destParent | Out-Null
        if ($LASTEXITCODE -ne 0 -and ($recycleContext -or $null -ne $cleanup)) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'RecycleReceiptUnavailable' -ExitCode 2
        }
        $resultPath = Join-Path (Join-Path $root $Case) 'c590-result.json'
        if (-not (Test-Path -LiteralPath $resultPath)) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis ("RemoteResultMissing exit=" + $code) -ExitCode 2
        }
        $result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
        if ($code -ne 0 -and [bool]$result.accepted) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'RemoteTransportFailed' -ExitCode 2
        }
        Write-C590Result -EvidenceRoot $root -Accepted ([bool]$result.accepted) -Diagnosis ([string]$result.diagnosis) -ExitCode ([int]$result.exit)
    }
    finally {
        if ($tokenCopied) {
            [void](Invoke-C590Ssh "rm -f /home/mc/antiphon-c590/secrets/gh-token /home/mc/antiphon-c590/askpass")
        }
    }
}

function Invoke-C590PlanCheckpoint {
    param([Parameter(Mandatory = $true)][string]$Checkpoint)
    $row = Get-C590PlanRow -Checkpoint $Checkpoint
    $state = Get-C590StateDir
    $run = Get-C590RunId -Manifest ([pscustomobject]@{})
    $sha = Get-C590Sha -Manifest ([pscustomobject]@{})
    $evidence = Join-Path $state ("evidence-" + $Checkpoint)
    $case = ''
    $project = ''
    $noBuild = $false
    $broker = $false
    if ($row.Filter -match '-Case\s+([A-Za-z0-9-]+)') {
        $case = $Matches[1]
    }
    elseif ($row.Filter -match 'npm --prefix client run lint') {
        $case = 'client-lint'
    }
    elseif ($row.Filter -match 'test-client\.ps1') {
        $case = 'client-tests'
    }
    elseif ($row.Filter -match '/\*') {
        $case = 'dotnet-filter'
        if ($Checkpoint -eq 'CP-19') {
            $project = 'tests/Antiphon.Messaging.Tests'
            $broker = $true
        }
        else {
            $project = 'tests/Antiphon.Tests'
            $noBuild = $Checkpoint -ne 'CP-20'
        }
    }
    else {
        throw "no live dispatcher for $Checkpoint filter"
    }
    $manifestPath = New-C590ManifestFile -EvidenceRoot $evidence -RunId $run -Sha $sha -Filter $row.Filter -Project $project -NoBuild $noBuild -MinExecuted $row.MinExecuted -Checkpoint $Checkpoint -Broker $broker
    $verify = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'verify-docker-stack.ps1'), '-Case', $case, '-Manifest', $manifestPath)
    if ($RefreshClaudeToken) { $verify += '-RefreshClaudeToken' }
    & pwsh @verify
    exit $LASTEXITCODE
}

if ($MyInvocation.InvocationName -ne '.') {
    if (-not $Cp) { throw 'Cp is required when executing c590-real.ps1' }
    Invoke-C590PlanCheckpoint -Checkpoint $Cp
}
