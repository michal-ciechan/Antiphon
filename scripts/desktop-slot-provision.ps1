#requires -Version 5.1
# CARD-0824 S2 stub. The witness is red until the provision behavior lands.
[CmdletBinding()]
param(
    [string]$Root = 'C:\Antiphon\slots',
    [switch]$Execute,
    [switch]$WhatIf,
    [switch]$RemoveEmpty,
    [int]$DiskBudgetGiB = 32,
    [scriptblock]$FreeSpaceProvider
)

function Invoke-DesktopSlotProvision {
    param(
        [string]$Root = 'C:\Antiphon\slots',
        [switch]$Execute,
        [switch]$WhatIf,
        [switch]$RemoveEmpty,
        [int]$DiskBudgetGiB = 32,
        [scriptblock]$FreeSpaceProvider
    )
    $row = @{
        ExitCode = 0
        Lines = @('mode=execute')
    }
    return $row
}

if ($MyInvocation.InvocationName -ne '.') {
    $result = Invoke-DesktopSlotProvision -Root $Root -Execute:$Execute -WhatIf:$WhatIf -RemoveEmpty:$RemoveEmpty -DiskBudgetGiB $DiskBudgetGiB -FreeSpaceProvider $FreeSpaceProvider
    foreach ($line in @($result.Lines)) { Write-Host $line }
    exit $result.ExitCode
}
