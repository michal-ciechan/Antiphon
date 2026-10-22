# TEST STUB (scratch only): records the launch instead of starting a terminal.
param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$WorkDir, [string]$Model)
Add-Content -Path $env:UC_LAUNCH_RECORD -Value ("launch Name=$Name WorkDir=$WorkDir Model=$Model")
Write-Host "stub launcher: would launch '$Name' in $WorkDir"
