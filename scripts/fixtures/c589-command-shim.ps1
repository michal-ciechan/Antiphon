# CARD-0589 offline stand-in for the command scripts/build-slot.ps1 wraps (C589_COMMAND_SHIM).
# Logs 'CMD <argv>' to C589_SLOT_LOG, the file the slot shim logs to, and exits C589_COMMAND_EXIT.
# It runs under pwsh -File, whose binder splits -name:value, so -maxcpucount:3 logs as
# '-maxcpucount 3'; the real command receives the token whole. ASCII-only.
$argv = @($args)
if ($env:C589_SLOT_SCRIPT -like 'deadline_*') {
    Add-Content -LiteralPath $env:C589_SLOT_LOG -Value ('CMD_AT ' + [Diagnostics.Stopwatch]::GetTimestamp()) -Encoding ASCII
}
Add-Content -LiteralPath $env:C589_SLOT_LOG -Value ('CMD ' + (($argv) -join ' ')) -Encoding ASCII
$code = 0
if ($env:C589_COMMAND_EXIT) { $code = [int]$env:C589_COMMAND_EXIT }
exit $code
