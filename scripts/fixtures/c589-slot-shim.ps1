# CARD-0589 offline stand-in for the session runner's /build-slots routes. ASCII-only.
# Called in-process by scripts/lib/build-slot.ps1
# when C589_SLOT_SHIM is set. C589_SLOT_SCRIPT is a comma-separated answer per POST (the last one
# repeats): granted | unlimited | busy | memory_floor | notfound | unreachable. Every call is
# appended to C589_SLOT_LOG, the same file the dotnet shim logs to, so call order is observable.
# Every answer is what the real routes send (BuildSlotRoutes): 200 grant, 409 problem, 404 old runner.
param([string]$Method, [string]$Uri, [string]$BodyJson)
$log = $env:C589_SLOT_LOG
if ($Method -eq 'DELETE') {
    Add-Content -LiteralPath $log -Value ('SLOT DELETE ' + $Uri.Substring($Uri.LastIndexOf('/') + 1)) -Encoding ASCII
    return @{ Status = 204; Body = '' }
}
$label = ''
$holder = ''
try { $req = $BodyJson | ConvertFrom-Json; $label = [string]$req.label; $holder = [string]$req.pid } catch { }
$index = 0
if (Test-Path -LiteralPath $log) { $index = @(Get-Content -LiteralPath $log | Where-Object { $_ -like 'SLOT POST *' }).Count }
Add-Content -LiteralPath $log -Value ('SLOT POST label=' + $label + ' pid=' + $holder + ' uri=' + $Uri) -Encoding ASCII
$answers = @(([string]$env:C589_SLOT_SCRIPT).Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$answer = 'granted'
if ($answers.Count -gt 0) { $answer = $answers[[Math]::Min($index, $answers.Count - 1)] }
switch ($answer) {
    'granted' { return @{ Status = 200; Body = '{"leaseId":"c5890000-0000-4000-8000-000000000001","maxCpuCount":3,"occupied":1,"budget":2,"expiresAtUtc":"2026-09-25T08:00:00Z","unlimited":false}' } }
    'unlimited' { return @{ Status = 200; Body = '{"leaseId":null,"maxCpuCount":5,"occupied":0,"budget":2,"expiresAtUtc":null,"unlimited":true}' } }
    'busy' { return @{ Status = 409; Body = '{"type":"build_slot_busy","title":"build_slot_busy","status":409,"occupied":2,"budget":2,"queuePosition":1,"retryAfterMs":10}' } }
    'memory_floor' { return @{ Status = 409; Body = '{"type":"build_slot_memory_floor","title":"build_slot_memory_floor","status":409,"availableMb":1000,"floorMb":6144,"queuePosition":1,"retryAfterMs":10}' } }
    'notfound' { return @{ Status = 404; Body = '' } }
    default { return @{ Status = 0; Body = ''; Error = 'connection refused (shim)' } }
}
