<#
.SYNOPSIS
    Send Antiphon's server-composed away digest now.
#>
param(
    [string]$Channel,
    [datetime]$Since,
    [string]$BaseUrl = 'http://localhost:17202'
)

$ErrorActionPreference = 'Stop'
$body = @{}
if ($Channel) {
    $channelId = [Guid]::Empty
    if (-not [Guid]::TryParse($Channel, [ref]$channelId)) {
        $matches = @(Invoke-RestMethod -Uri ($BaseUrl.TrimEnd('/') + '/api/channels') -Method Get |
            Where-Object { $_.title -and $_.title -ieq $Channel })
        if ($matches.Count -ne 1) {
            Write-Error "Channel '$Channel' was not found uniquely by title. Pass its GUID instead."
            exit 1
        }
        $channelId = [Guid]$matches[0].id
    }
    $body.channelId = $channelId.ToString('D')
}
if ($PSBoundParameters.ContainsKey('Since')) { $body.since = $Since.ToUniversalTime().ToString('O') }
try {
    $result = Invoke-RestMethod -Uri ($BaseUrl.TrimEnd('/') + '/api/digest/send') -Method Post -ContentType 'application/json' -Body ($body | ConvertTo-Json)
    $result | ConvertTo-Json -Depth 5
    if (@($result | Where-Object { $_.reason -eq 'send_failed' }).Count -gt 0) { exit 1 }
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
