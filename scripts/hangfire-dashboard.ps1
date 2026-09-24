# Open the Hangfire dashboard in the default browser, already logged in (CARD-0658).
# ASCII-only: this script must parse under Windows PowerShell 5.1.
#
#   hangfire-dashboard.ps1
#
# The dashboard needs the operator credential; a loopback address is not one. This script reads
# the operator token from the owner-only file the server creates at startup (default
# %LOCALAPPDATA%\Antiphon\operator-token; override with ANTIPHON_OPERATOR_TOKEN_FILE), asks the
# server for a one-time login link (single use, two minutes) and hands it to the browser, which
# receives a 12-hour dashboard cookie scoped to /hangfire. It prints neither the token, nor the
# link, nor its nonce: the link is a credential until it is redeemed, and terminals here are
# captured into transcripts. A refusal is reported by its problem code only.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')

$onWindows = ($PSVersionTable.PSEdition -eq 'Desktop') -or ($IsWindows -eq $true)
if (-not $onWindows) {
    Write-Output 'Opening a browser is only supported on Windows. From a script, send the X-Antiphon-Operator-Token header (read from the operator token file) with curl to /hangfire instead.'
    exit 2
}

# Twin of Add-OperatorToken in scripts/runner-slots.ps1; keep the two in step.
$headers = @{}
function Add-OperatorToken {
    $path = $env:ANTIPHON_OPERATOR_TOKEN_FILE
    if ([string]::IsNullOrWhiteSpace($path)) {
        $path = Join-Path (Join-Path $env:LOCALAPPDATA 'Antiphon') 'operator-token'
    }
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Operator token file not found at $path. The server creates it at startup; run this as the account the server runs under."
    }
    $token = (Get-Content -LiteralPath $path -Raw).Trim()
    if ([string]::IsNullOrEmpty($token)) { throw "Operator token file at $path is empty." }
    $headers['X-Antiphon-Operator-Token'] = $token
}

Add-OperatorToken

try {
    $login = Invoke-RestMethod -Method Post -Uri "$api/api/operator/dashboard-sessions" -Headers $headers
}
catch {
    $code = $null
    if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
        try { $code = ($_.ErrorDetails.Message | ConvertFrom-Json).code } catch { $code = $null }
    }
    if ([string]::IsNullOrWhiteSpace($code)) { $code = 'request_failed' }
    Write-Output "The server refused the dashboard login: $code"
    exit 1
}

if ([string]::IsNullOrWhiteSpace($login.loginPath) -or -not $login.loginPath.StartsWith('/')) {
    Write-Output 'The server returned no usable login link.'
    exit 1
}

try {
    Start-Process -FilePath ($api + $login.loginPath)
}
catch {
    # The error text would contain the link; report a fixed message instead.
    Write-Output 'The default browser could not be started.'
    exit 1
}

Write-Output 'Opened the Hangfire dashboard in the default browser (session lasts 12 hours).'
