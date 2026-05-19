param(
    [string[]]$Ports = @("17281", "17282", "17283"),
    [switch]$StopPortOwners
)

$ErrorActionPreference = "Stop"

$PortNumbers = @(
    $Ports |
        ForEach-Object { $_ -split "," } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { [int]$_.Trim() }
)

if ($PortNumbers.Count -eq 0) {
    throw "At least one port must be provided."
}

function Get-PortOwnerRows {
    param([int[]]$TargetPorts)

    $connections = Get-NetTCPConnection -LocalPort $TargetPorts -State Listen -ErrorAction SilentlyContinue
    if (-not $connections) {
        return @()
    }

    $connections |
        Sort-Object LocalPort, OwningProcess -Unique |
        ForEach-Object {
            $process = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
            [pscustomobject]@{
                Port = $_.LocalPort
                ProcessId = $_.OwningProcess
                ProcessName = if ($process) { $process.ProcessName } else { "<exited>" }
                Path = if ($process) { $process.Path } else { "" }
            }
        }
}

$owners = @(Get-PortOwnerRows -TargetPorts $PortNumbers)
if ($owners.Count -eq 0) {
    Write-Host "Dev ports are clear: $($PortNumbers -join ', ')." -ForegroundColor Green
    exit 0
}

Write-Host "Dev port owner check found listeners:" -ForegroundColor Yellow
$owners | Format-Table Port, ProcessId, ProcessName, Path -AutoSize | Out-String | Write-Host

if (-not $StopPortOwners) {
    Write-Host "Aborting to avoid starting Antiphon against stale or mismatched processes." -ForegroundColor Red
    Write-Host "Stop these processes manually, or rerun with -StopPortOwners to stop the listed owners." -ForegroundColor Red
    exit 1
}

$ownerIds = $owners |
    Where-Object { $_.ProcessName -ne "<exited>" } |
    Select-Object -ExpandProperty ProcessId -Unique

foreach ($processId in $ownerIds) {
    Write-Host "Stopping PID $processId..." -ForegroundColor Yellow
    Stop-Process -Id $processId -Force
}

Start-Sleep -Seconds 1

$remainingOwners = @(Get-PortOwnerRows -TargetPorts $PortNumbers)
if ($remainingOwners.Count -ne 0) {
    Write-Host "Some dev ports are still occupied after stopping owners:" -ForegroundColor Red
    $remainingOwners | Format-Table Port, ProcessId, ProcessName, Path -AutoSize | Out-String | Write-Host
    exit 1
}

Write-Host "Dev ports are clear: $($PortNumbers -join ', ')." -ForegroundColor Green
