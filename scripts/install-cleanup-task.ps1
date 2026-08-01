<#
.SYNOPSIS
    Register (or remove with -Uninstall) the per-user Scheduled Task
    "Antiphon Build Junk Cleanup", which runs scripts/cleanup-build-junk.ps1
    weekly (Mon 09:00) and at every logon. No admin rights required.
#>
param([switch]$Uninstall)

$taskName = 'Antiphon Build Junk Cleanup'
$script = Join-Path $PSScriptRoot 'cleanup-build-junk.ps1'

if ($Uninstall) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Removed task '$taskName' (if it existed)."
    return
}

$pwsh = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
if (-not $pwsh) { $pwsh = 'powershell.exe' }

$action = New-ScheduledTaskAction -Execute $pwsh `
    -Argument "-NonInteractive -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`""
$triggers = @(
    New-ScheduledTaskTrigger -Weekly -DaysOfWeek Monday -At 09:00
    New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
)
# Interactive + Limited principal: registers without admin rights (same shape as install-autostart.ps1).
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 2)

if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $triggers `
    -Principal $principal -Settings $settings `
    -Description 'Deletes regenerable alternate build outputs (bin-verify, bin-ptyhost, bin-profile*) so they cannot accumulate and slow builds.' | Out-Null
Write-Host "Registered task '$taskName' (weekly Mon 09:00 + at logon) running $script"
