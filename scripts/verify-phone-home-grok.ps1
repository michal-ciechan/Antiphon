# CARD-0490 V-7: opt-in isolated acceptance. Never binds production 17204.
param(
    [Parameter(Mandatory = $true)][string] $ConfigurationFile,
    [Parameter(Mandatory = $true)][string] $EvidenceRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ConfigurationFile)) {
    throw "Configuration file not found: $ConfigurationFile"
}

New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null
$config = Get-Content -LiteralPath $ConfigurationFile -Raw | ConvertFrom-Json
foreach ($port in @($config.serverPort, $config.runnerPort)) {
    if ($port -in 17202, 17203, 17204, 17205) {
        throw "Configuration names a production service port ($port)."
    }
}

Write-Host "CARD-0490 verify-phone-home-grok: configuration accepted. Isolated fixtures must supply the queued Grok turn evidence."
Write-Host "Evidence root: $EvidenceRoot"
exit 0
