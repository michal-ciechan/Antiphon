# CARD-1989 checkpoint command adapter: native outputs required by the task brief.
param([switch]$Build,[Parameter(Mandatory=$true)][string]$Class,[string]$Method='*')
$ErrorActionPreference='Stop'
$env:ANTIPHON_LINUX_COMPILER_POLICY='0'
$repo=Split-Path -Parent $PSScriptRoot
Set-Location $repo
$sha=(& git rev-parse HEAD).Trim()
if((& git status --porcelain --untracked-files=no)){throw 'source must be committed and clean'}
$root=Join-Path $repo '.antiphon/c1989-native'
[void][IO.Directory]::CreateDirectory($root)
$stamp=Join-Path $root 'build.json'
$dll=Join-Path $repo 'tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.dll'
function Wait-Load {
    if([IO.File]::Exists('/proc/loadavg')){
        do {
            $load=[IO.File]::ReadAllText('/proc/loadavg').Trim();Write-Output ('LOAD '+$load)
            $busy=[double]::Parse($load.Split(' ')[0],[Globalization.CultureInfo]::InvariantCulture) -ge 16
            if($busy){Start-Sleep -Seconds 30}
        }while($busy)
    }
}
if($Build){
    Wait-Load
    $cap=$env:ANTIPHON_BUILD_SLOT_MAXCPUCOUNT
    if(-not $cap){throw 'checkpoint build slot required'}
    & dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo ('-maxcpucount:'+$cap)
    if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
    @{source=$sha;assembly=(Get-FileHash $dll).Hash}|ConvertTo-Json|Set-Content $stamp
}
$binding=Get-Content $stamp -Raw|ConvertFrom-Json
if($binding.source -cne $sha -or $binding.assembly -cne (Get-FileHash $dll).Hash){throw 'native build provenance mismatch'}
$results=Join-Path $root ([guid]::NewGuid().ToString('N'))
Wait-Load
$filter='/*/*/'+$Class+'/'+$Method
# Pass the slash/star selector literally. PowerShell's Unix native wildcard
# expansion can traverse the filesystem before it ever starts dotnet.
$start=[Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute=$false
foreach($argument in @('run','--project','tests/Antiphon.Tests','--no-build','-p:UseAppHost=false','--',
    '--timeout','570s','--treenode-filter',$filter,'--report-trx','--report-trx-filename','run.trx','--results-directory',$results)){
    [void]$start.ArgumentList.Add($argument)
}
$process=[Diagnostics.Process]::Start($start)
try{$process.WaitForExit();$code=$process.ExitCode}finally{$process.Dispose()}
$trx=Join-Path $results 'run.trx'
if(-not [IO.File]::Exists($trx)){throw 'fresh TRX missing'}
[xml]$xml=Get-Content $trx -Raw
$counts=$xml.TestRun.ResultSummary.Counters
Write-Output ('C1989-TRX source='+$sha+' assembly='+$binding.assembly+' filter='+$filter+' executed='+$counts.executed+' passed='+$counts.passed+' failed='+$counts.failed+' trx='+$trx)
if([int]$counts.executed -le 0){throw 'zero tests'}
if($code -ne 0){exit $code}
if([int]$counts.failed -ne 0 -or [int]$counts.notExecuted -ne 0){throw 'failed or skipped tests'}
