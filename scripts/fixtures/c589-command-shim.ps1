# CARD-0589 offline stand-in for the command scripts/build-slot.ps1 wraps (C589_COMMAND_SHIM).
# Logs 'CMD <argv>' to C589_SLOT_LOG, the file the slot shim logs to, and exits C589_COMMAND_EXIT.
# It runs under pwsh -File, whose binder splits -name:value, so -maxcpucount:3 logs as
# '-maxcpucount 3'; the real command receives the token whole. ASCII-only.
$argv = @($args)
$raw = [Environment]::GetCommandLineArgs()
for ($i = 0; $i -lt $raw.Count; $i++) {
    if ($i -gt 0 -and [string]$raw[$i - 1] -ieq '-File' -and
        [string]::Equals([string]$raw[$i], $PSCommandPath, [StringComparison]::OrdinalIgnoreCase)) {
        $argv = @($raw | Select-Object -Skip ($i + 1))
        break
    }
}
if ($env:C589_SLOT_SCRIPT -like 'deadline_*') {
    Add-Content -LiteralPath $env:C589_SLOT_LOG -Value ('CMD_AT ' + [Diagnostics.Stopwatch]::GetTimestamp()) -Encoding ASCII
}
Add-Content -LiteralPath $env:C589_SLOT_LOG -Value ('CWD ' + (Get-Location).ProviderPath) -Encoding ASCII
Add-Content -LiteralPath $env:C589_SLOT_LOG -Value ('PID ' + $PID) -Encoding ASCII
Add-Content -LiteralPath $env:C589_SLOT_LOG -Value ('ARGV ' + (ConvertTo-Json -InputObject @($argv) -Compress)) -Encoding ASCII
Add-Content -LiteralPath $env:C589_SLOT_LOG -Value ('CMD ' + ((@($args)) -join ' ')) -Encoding ASCII
if ($env:C589_COMMAND_SLEEP_SECONDS) { Start-Sleep -Seconds ([int]$env:C589_COMMAND_SLEEP_SECONDS) }
$code = 0
if ($env:C589_COMMAND_EXIT) { $code = [int]$env:C589_COMMAND_EXIT }
exit $code
