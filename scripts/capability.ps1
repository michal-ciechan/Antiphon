# Operator issue / rotate / revoke / list for a named Delegation Capability (CARD-0398).
#
# ASCII-only on purpose: agent/ops scripts must parse under Windows PowerShell 5.1, which reads a
# no-BOM .ps1 as CP1252 and mangles non-ASCII characters.
#
# The CLI is the only writer of the DPAPI store. The server returns the raw token once on
# issue/rotate; this script writes $store\<name>.dpapi and prints name + path + roots - never the
# token. ChatGPT loads it with delegate.ps1 -Capability <name> (the name, never the secret).
#
# Verbs:
#   capability.ps1 issue  -Name <n> -Roots <dir>[,dir...] [-BoardId g] [-ProjectId g]
#   capability.ps1 rotate -Name <n> | -Id <guid>
#   capability.ps1 revoke -Name <n> | -Id <guid>
#   capability.ps1 list
#
# Store default: %LOCALAPPDATA%\Antiphon\capabilities. Override with ANTIPHON_CAPABILITY_STORE.
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('issue', 'rotate', 'revoke', 'list')]
    [string]$Verb,

    [string]$Name,

    [string[]]$Roots,

    [string]$BoardId,

    [string]$ProjectId,

    [string]$Id
)

$ErrorActionPreference = 'Stop'

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')

$store = $env:ANTIPHON_CAPABILITY_STORE
if ([string]::IsNullOrWhiteSpace($store)) {
    $store = Join-Path $env:LOCALAPPDATA 'Antiphon\capabilities'
}

function Invoke-AntiphonRaw {
    param([string]$Method, [string]$Path, $Body)
    $uri = "$api$Path"
    try {
        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Depth 6 -Compress
            return Invoke-WebRequest -Method $Method -Uri $uri -Body $json -ContentType 'application/json' -UseBasicParsing
        }
        return Invoke-WebRequest -Method $Method -Uri $uri -UseBasicParsing
    }
    catch {
        $detail = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($detail)) { $detail = $_.Exception.Message }
        Write-Error "Antiphon $Method $Path failed: $detail"
        exit 1
    }
}

function Get-CapabilityStorePath {
    param([string]$CapabilityName)
    return (Join-Path $store ($CapabilityName + '.dpapi'))
}

function Write-CapabilityBlob {
    param([string]$CapabilityName, [string]$Token)
    if ([string]::IsNullOrWhiteSpace($Token)) {
        Write-Error "issue/rotate response did not include a token for '$CapabilityName'."
        exit 1
    }
    New-Item -ItemType Directory -Force -Path $store | Out-Null
    $path = Get-CapabilityStorePath -CapabilityName $CapabilityName
    Add-Type -AssemblyName System.Security | Out-Null
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Token)
    $protected = [System.Security.Cryptography.ProtectedData]::Protect(
        $bytes,
        $null,
        [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
    [System.IO.File]::WriteAllBytes($path, $protected)
    return $path
}

function Resolve-CapabilityId {
    param([string]$CapabilityName, [string]$CapabilityId)
    if (-not [string]::IsNullOrWhiteSpace($CapabilityId)) {
        return $CapabilityId
    }
    if ([string]::IsNullOrWhiteSpace($CapabilityName)) {
        Write-Error 'Pass -Name or -Id.'
        exit 1
    }
    $resp = Invoke-AntiphonRaw -Method GET -Path '/api/delegation-capabilities'
    $rows = @($resp.Content | ConvertFrom-Json)
    $hits = @($rows | Where-Object { $_.name -eq $CapabilityName -and -not $_.revokedAt })
    if ($hits.Count -eq 0) {
        $hits = @($rows | Where-Object { $_.name -eq $CapabilityName })
    }
    if ($hits.Count -eq 0) {
        Write-Error "No capability named '$CapabilityName'."
        exit 1
    }
    if ($hits.Count -gt 1) {
        Write-Error "Capability name '$CapabilityName' is ambiguous; pass -Id."
        exit 1
    }
    return [string]$hits[0].id
}

switch ($Verb) {
    'list' {
        $resp = Invoke-AntiphonRaw -Method GET -Path '/api/delegation-capabilities'
        $rows = @($resp.Content | ConvertFrom-Json)
        if ($rows.Count -eq 0) {
            Write-Output 'No delegation capabilities.'
            return
        }
        foreach ($row in $rows) {
            $state = 'active'
            if ($row.revokedAt) { $state = 'revoked' }
            elseif ($row.rotatedAt) { $state = 'rotated' }
            $rootList = @($row.roots) -join ', '
            Write-Output ("{0}  {1}  {2}  roots: {3}" -f $row.name, $row.id, $state, $rootList)
        }
    }
    'issue' {
        if ([string]::IsNullOrWhiteSpace($Name)) {
            Write-Error 'issue requires -Name.'
            exit 1
        }
        if ($null -eq $Roots -or $Roots.Count -eq 0) {
            Write-Error 'issue requires -Roots.'
            exit 1
        }
        $body = @{
            name  = $Name
            roots = @($Roots)
        }
        if (-not [string]::IsNullOrWhiteSpace($BoardId)) { $body['boardId'] = $BoardId }
        if (-not [string]::IsNullOrWhiteSpace($ProjectId)) { $body['projectId'] = $ProjectId }
        $resp = Invoke-AntiphonRaw -Method POST -Path '/api/delegation-capabilities' -Body $body
        $obj = $resp.Content | ConvertFrom-Json
        $token = [string]$obj.token
        $path = Write-CapabilityBlob -CapabilityName $obj.name -Token $token
        $rootList = @($obj.roots) -join ', '
        Write-Output ("Issued capability '{0}' stored at {1}" -f $obj.name, $path)
        Write-Output ("Roots: {0}" -f $rootList)
    }
    'rotate' {
        $capabilityId = Resolve-CapabilityId -CapabilityName $Name -CapabilityId $Id
        $resp = Invoke-AntiphonRaw -Method POST -Path "/api/delegation-capabilities/$capabilityId/rotate"
        $obj = $resp.Content | ConvertFrom-Json
        $token = [string]$obj.token
        $path = Write-CapabilityBlob -CapabilityName $obj.name -Token $token
        Write-Output ("Rotated capability '{0}' stored at {1}" -f $obj.name, $path)
    }
    'revoke' {
        $capabilityId = Resolve-CapabilityId -CapabilityName $Name -CapabilityId $Id
        $resp = Invoke-AntiphonRaw -Method POST -Path "/api/delegation-capabilities/$capabilityId/revoke"
        $obj = $resp.Content | ConvertFrom-Json
        Write-Output ("Revoked capability '{0}'" -f $obj.name)
    }
}
