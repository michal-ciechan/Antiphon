$ErrorActionPreference = "Continue"
$ev = Split-Path -Parent $MyInvocation.MyCommand.Path
$grok = Join-Path $env:USERPROFILE ".grok\bin\grok.exe"
& $grok agent --help 2>&1 | Out-String | Set-Content -LiteralPath (Join-Path $ev "80-grok-agent-help.txt") -Encoding utf8
Write-Output "WROTE 80-grok-agent-help.txt"

$tmp = Join-Path $env:TEMP "card0575-boards.json"
curl.exe -sS -m 15 --resolve "antiphon.desktop.codeperf.net:443:127.0.0.1" -o $tmp "https://antiphon.desktop.codeperf.net/api/boards"
$raw = Get-Content -LiteralPath $tmp -Raw
$n = [Math]::Min(180, $raw.Length)
$snip = $raw.Substring(0, $n)
Set-Content -LiteralPath (Join-Path $ev "81-caddy-api-boards-snip.txt") -Value ("CADDY_API_BOARDS bytes=" + $raw.Length + "`n" + $snip) -Encoding utf8
Write-Output "WROTE 81-caddy-api-boards-snip.txt"

$h1 = curl.exe -sS -m 10 -D - -o NUL "http://127.0.0.1:17203/health" 2>&1 | Out-String
$h2 = curl.exe -sS -m 10 -D - -o NUL "http://127.0.0.1:17203/api/version" 2>&1 | Out-String
$h3 = curl.exe -sS -m 10 -H "Host: antiphon.desktop.codeperf.net" -D - -o NUL "http://127.0.0.1:17203/api/version" 2>&1 | Out-String
$h4 = curl.exe -sS -m 10 -H "Host: antiphon.desktop.codeperf.net" -D - -o NUL "http://127.0.0.1:17203/health" 2>&1 | Out-String
Set-Content -LiteralPath (Join-Path $ev "82-17203-proxy.txt") -Value ("=== 17203/health ===`n$h1`n=== 17203/api/version ===`n$h2`n=== 17203/api/version Host antiphon ===`n$h3`n=== 17203/health Host antiphon ===`n$h4") -Encoding utf8
Write-Output "WROTE 82-17203-proxy.txt FOLLOWUP_DONE"
