# Offline production-function witnesses. No SSH, Docker or live origin is used.
param([ValidateSet('window','deadline','timings','guards','config')][string]$Case)
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
$realHash=${function:Get-FileHash}
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
    }
    Write-Output ('PASS C1989 '+$Case)
} finally {
    # Delete only the three files and directory this invocation created.
    [IO.File]::Delete($proof);[IO.File]::Delete($bundle);[IO.Directory]::Delete($root)
}
