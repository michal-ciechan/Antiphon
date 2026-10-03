$ErrorActionPreference = 'Stop'
$fixture = Join-Path (git rev-parse --show-toplevel) 'scripts/fixtures/c727-fake-http.ps1'
$env:C727_TEST_STATE = Join-Path $PSScriptRoot 'bench-state.json'
$env:C727_TEST_TRACE = Join-Path $PSScriptRoot 'bench-trace.jsonl'
'{"scenario":"c1008","statuses":{"server2":{"runnerId":"server2","sessions":0,"runnerSessions":0,"queuedTasks":0}}}' | Set-Content -LiteralPath $env:C727_TEST_STATE
$results = @()
foreach ($request in @('status','projects')) {
    $arguments = if ($request -eq 'status') { @{ Method='GET'; RunnerId='server2'; Suffix='/status' } } else { @{ Method='GET'; Path='/api/projects?includeArchived=true' } }
    $expected = $null
    foreach ($mode in @('subprocess','script-scope')) {
        $times = @()
        for ($i=0; $i -lt 10; $i++) {
            $sw = [Diagnostics.Stopwatch]::StartNew()
            if ($mode -eq 'subprocess') { $raw = & pwsh -NoProfile -File $fixture @arguments }
            else { $raw = & $fixture @arguments }
            $sw.Stop()
            if ($LASTEXITCODE -ne 0) { throw 'benchmark exit mismatch' }
            $json = $raw | ConvertFrom-Json | ConvertTo-Json -Compress -Depth 20
            if ($null -eq $expected) { $expected = $json }
            if ($json -cne $expected) { throw 'benchmark output mismatch' }
            $times += $sw.Elapsed.TotalMilliseconds
        }
        $results += [pscustomobject]@{request=$request;mode=$mode;calls=10;milliseconds=$times;meanMilliseconds=($times|Measure-Object -Average).Average;outputIdentical=$true}
    }
}
foreach ($mode in @('subprocess','script-scope')) {
    if ($mode -eq 'subprocess') { $raw = & pwsh -NoProfile -File $fixture -Method GET -Path '/unknown' }
    else { $raw = & $fixture -Method GET -Path '/unknown' }
    if ($LASTEXITCODE -ne 2 -or $null -ne $raw) { throw 'benchmark failure exit mismatch' }
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'bench-http.json')
$results | Select-Object request,mode,calls,meanMilliseconds,outputIdentical | Format-Table
