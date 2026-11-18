# Offline production-function witnesses. No SSH, Docker or live origin is used.
param([ValidateSet('window','deadline','timings','guards','config','receipt')][string]$Case)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'c590-real.ps1')
function Assert-True([bool]$Value,[string]$Message) { if(-not $Value){throw $Message} }
$root=Join-Path ([IO.Path]::GetTempPath()) ('c1989-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($root)
$origin='https://fixture.invalid/repo.git';$repo='repos/fixture'
$operation='c100800000000000000000000000000000001'
$proof=Join-Path $root 'proof'
$advertisement=('a'*40)+"`trefs/heads/main`n"
$bundle=Join-Path $root 'proof.bundle'
[IO.File]::WriteAllText($bundle,'fixture bundle bytes')
$hash=(Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash.ToLowerInvariant()
$header=@('c1008-host-proof 2',('operation '+$operation),('origin '+(Get-C1008Sha256 $origin)),('repo '+$repo),
    'created 1700000000',('nonce '+('b'*32)),('inventory '+('c'*64)),('bundle '+$hash),
    ('advertisement '+(Get-C1008Sha256 $advertisement)),'count 1','completed 1700000001')
[IO.File]::WriteAllText($proof,($header -join "`n")+"`n"+$advertisement)
$script:delayHash=0;$script:delayGit=0;$script:withdraw=$false;$script:gitCalls=0
function Get-FileHash {
    param($LiteralPath,$Algorithm)
    if($script:delayHash){Start-Sleep -Milliseconds $script:delayHash}
    Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $LiteralPath -Algorithm $Algorithm
}
function Invoke-C1609HostGit {
    param($Repo,$WorkingDirectory,$GitArgs,$TimeoutMs)
    $script:gitCalls++;$script:gitBudget=$TimeoutMs
    if($script:delayGit){Start-Sleep -Milliseconds $script:delayGit}
    if($script:withdraw){return (('d'*40)+"`trefs/heads/main`n")}
    return $advertisement
}
function Invoke-Witness([int]$Budget) {
    $script:timing=@{}
    Assert-C1609LiveOriginProof -OriginUrl $origin -RepoRelative $repo -OperationId $operation -ProofFile $proof `
        -ApprovedOrigins @($origin) -NetworkBudgetMs $Budget -Timing $script:timing
}
try {
    switch($Case){
        window {
            $script:delayGit=5500
            Invoke-Witness 8000
            Assert-True ($script:gitCalls -eq 1) 'slow control must read live refs once'
            Assert-True ($script:timing.elapsedMs -ge 5500) 'slow control must be measured'
            Assert-True ($script:timing.outcome -ceq 'passed') 'slow control inside 8 seconds must pass'
        }
        deadline {
            $script:delayHash=1200
            $refused=$false
            try{Invoke-Witness 1000}catch{$refused=$true}
            Assert-True $refused 'over-bound hashing must refuse'
            Assert-True ($script:gitCalls -eq 0) 'no live read after exhausted hash budget'
            Assert-True ($script:timing.reason -ceq 'control-deadline') 'deadline must retain fixed reason'
            Assert-True ($script:timing.step -ceq 'bundle-hash') 'deadline must name bundle-hash'
            Assert-True ($script:timing.elapsedMs -ge 1200) 'deadline must retain elapsed time'
        }
        timings {
            $script:delayHash=80;$script:delayGit=120
            Invoke-Witness 1000
            $names=@($script:timing.steps|ForEach-Object {$_.step})
            foreach($name in @('proof-read','bundle-hash','expected-parse','ls-remote','live-parse','compare','cleanup')){
                Assert-True ($names -contains $name) ('missing timing '+$name)
            }
            Assert-True (($script:timing.steps|Where-Object step -eq 'bundle-hash').elapsedMs -ge 80) 'hash duration is real'
            Assert-True (($script:timing.steps|Where-Object step -eq 'ls-remote').elapsedMs -ge 120) 'git duration is real'
            Assert-True ($script:gitBudget -lt 1000) 'git gets remaining shared budget'
        }
        guards {
            $script:withdraw=$true;$refused=$false
            try{Invoke-Witness 8000}catch{$refused=$true}
            Assert-True $refused 'withdrawn ref must still refuse'
            Assert-True ($script:C1609LiveProofReason -ceq 'withdrawn-ref') 'withdrawn ref retains diagnosis'
            $script:withdraw=$false;$script:gitCalls=0
            [IO.File]::AppendAllText($bundle,'tampered')
            $refused=$false;try{Invoke-Witness 8000}catch{$refused=$true}
            Assert-True $refused 'tampered bundle must still refuse'
            Assert-True ($script:gitCalls -eq 0) 'bad proof must not reach live read'
            Assert-True ($script:C1609LiveProofReason -ceq 'proof') 'bad proof retains diagnosis'
        }
        config {
            [Environment]::SetEnvironmentVariable('ANTIPHON_RETIRE_CONTROL_WINDOW_MS',$null)
            Assert-True ($null -eq [Environment]::GetEnvironmentVariable('ANTIPHON_RETIRE_CONTROL_WINDOW_MS')) 'key must be absent'
            Assert-True ((Get-C1609ControlWindowMs) -eq 8000) 'absent default must be 8000'
            $env:ANTIPHON_RETIRE_CONTROL_WINDOW_MS='';Assert-True ((Get-C1609ControlWindowMs) -eq 8000) 'empty default'
            $env:ANTIPHON_RETIRE_CONTROL_WINDOW_MS='7000';Assert-True ((Get-C1609ControlWindowMs) -eq 7000) 'configured window'
            foreach($value in @('0','-1','999','8001','15000','abc','1;echo bad')){
                $env:ANTIPHON_RETIRE_CONTROL_WINDOW_MS=$value;$refused=$false
                try{Get-C1609ControlWindowMs|Out-Null}catch{$refused=$true}
                Assert-True $refused ('invalid window must refuse: '+$value)
            }
        }
        receipt {
            # Linux-only transport fixture; the production coordinator, assertion,
            # receipt writer and failure sanitation execute unchanged.
            $ssh=Join-Path $root 'ssh'
            [IO.File]::WriteAllText($ssh,@'
#!/bin/sh
printf 'C1609_PROOF_REQUEST aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb initial none cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\n'
IFS= read -r response || exit 2
set -- $response
printf 'C1609_CONTROL_REQUEST dddddddddddddddddddddddddddddddd ready %s %s\n' "$7" "$9"
IFS= read -r response || exit 2
case "$response" in C1609_CONTROL_RESPONSE*) exit 0;; *) exit 3;; esac
'@)
            & chmod 0700 $ssh
            if($LASTEXITCODE -ne 0){throw 'fixture chmod failed'}
            $oldPath=$env:PATH;$env:PATH=$root+[IO.Path]::PathSeparator+$env:PATH
            $env:ANTIPHON_RETIRE_CONTROL_WINDOW_MS='1000'
            $project='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
            function Get-C1008RetireProofExports {
                param($EvidenceRoot,$Run,$RecycleOperation,$InventoryRemote,$ProjectId,[switch]$Coordinated,
                    $NetworkBudgetMs,$PreviousProofDirectory,$RequiredObjectDigest,[switch]$RequireObjectCoverage,$InventoryPhaseNonce)
                $leaf='proof-set';$directory=Join-Path $EvidenceRoot $leaf
                [void][IO.Directory]::CreateDirectory($directory)
                $name=Get-C1008Sha256 ($repo+[char]10+$origin)
                [IO.File]::Copy($proof,(Join-Path $directory $name),$true)
                [IO.File]::Copy($bundle,(Join-Path $directory ($name+'.bundle')),$true)
                $inventory=Join-Path $EvidenceRoot ('inventory-'+$InventoryPhaseNonce)
                [void][IO.Directory]::CreateDirectory($inventory)
                [IO.File]::WriteAllText((Join-Path $inventory 'private-origins.txt'),
                    ('C1008_PRIVATE_ORIGIN repo='+$repo+' origin='+$origin+' originHash='+(Get-C1008Sha256 $origin)+' proofName='+$name+"`n"))
                [IO.File]::WriteAllText((Join-Path $inventory 'trusted-origins.json'),
                    (@{schema=1;projectId=$ProjectId;origins=@($origin)}|ConvertTo-Json -Compress))
                return @("export C1008_HOST_PROOF='/fixture/proof-set'","export C1008_OPERATION_STARTED='1700000000'",
                    ("export C1609_PROOF_NONCE='"+('e'*32)+"'"),("export C1609_PROOF_INVENTORY='"+('c'*64)+"'"))
            }
            try {
                foreach($slow in @($false,$true)){
                    $evidence=Join-Path $root ('evidence-'+$slow)
                    [void][IO.Directory]::CreateDirectory($evidence)
                    $script:delayGit=if($slow){1200}else{0}
                    $receipt=Join-Path $evidence 'remote-output.txt'
                    $code=Invoke-C1609RetirementCoordinator -Remote 'fixture' -EvidenceRoot $evidence -Run 'fixture' `
                        -Operation $operation -ProjectId $project -ReceiptPath $receipt
                    Assert-True ($code -eq $(if($slow){2}else{0})) 'coordinator must preserve success/refusal exit'
                    $record=Get-Content -Raw (Join-Path $evidence 'coordinator-controls.jsonl')|ConvertFrom-Json
                    Assert-True ($record.limitMs -eq 1000) 'receipt binds configured limit'
                    Assert-True ($record.origins.Count -eq 1) 'receipt retains each origin'
                    Assert-True ($record.origins[0].steps.Count -ge 5) 'receipt retains individual control steps'
                    if($slow){
                        Assert-True ($record.outcome -ceq 'refused' -and $record.step -ceq 'ls-remote') 'receipt names failing step'
                        Assert-True ($record.elapsedMs -ge 1200) 'receipt retains failed elapsed time'
                        $message=[IO.File]::ReadAllText($receipt)
                        Assert-True ($message.Contains('failure-reason=control-deadline') -and $message.Contains('control-step=ls-remote')) 'failure message names deadline and step'
                    }else{Assert-True ($record.outcome -ceq 'passed') 'success receipt'}
                    Assert-True (-not ([IO.File]::ReadAllText((Join-Path $evidence 'coordinator-controls.jsonl'))).Contains($origin)) 'receipt must not contain origin URL'
                }
            } finally {
                $env:PATH=$oldPath
                [IO.File]::Delete($ssh)
                # Fixed fixture leaves, never an arbitrary recursive cleanup.
                foreach($name in @('evidence-False','evidence-True')){
                    $dir=Join-Path $root $name
                    foreach($leaf in @('coordinator-exchanges.jsonl','coordinator-controls.jsonl','remote-output.txt')){[IO.File]::Delete((Join-Path $dir $leaf))}
                    $exchange=Join-Path $dir ('exchange-'+('a'*32))
                    $inv=Join-Path $exchange ('inventory-'+('b'*32))
                    foreach($leaf in @('private-origins.txt','trusted-origins.json')){[IO.File]::Delete((Join-Path $inv $leaf))}
                    $pd=Join-Path $exchange 'proof-set';$pn=Get-C1008Sha256 ($repo+[char]10+$origin)
                    [IO.File]::Delete((Join-Path $pd $pn));[IO.File]::Delete((Join-Path $pd ($pn+'.bundle')))
                    foreach($path in @($inv,$pd,$exchange,$dir)){if([IO.Directory]::Exists($path)){[IO.Directory]::Delete($path)}}
                }
            }
        }
    }
    Write-Output ('PASS C1989 '+$Case)
} finally {
    # Delete only the three files and directory this invocation created.
    [IO.File]::Delete($proof);[IO.File]::Delete($bundle);[IO.Directory]::Delete($root)
}
