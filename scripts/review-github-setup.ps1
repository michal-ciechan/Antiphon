<#
.SYNOPSIS
  Configures this machine's Antiphon server to review GitHub PRs as the antiphon-review GitHub App.

.DESCRIPTION
  Reads the app's ids and PEM private key from the Bitwarden item (default
  "GitHub App - antiphon-review"; Bitwarden must be unlocked, ~/.bw-session or BW_SESSION) and
  writes them, with the plain review settings, into the server's user-secrets store
  (id antiphon-server) through stdin. The key never reaches the command line, the console or a
  file. Existing user-secrets are kept: dotnet user-secrets set merges the keys it is given.

  The server reads user-secrets at start, so restart the AppHost afterwards
  (scripts/restart-apphost.ps1 from the main checkout).

.EXAMPLE
  pwsh -NoProfile -File scripts/review-github-setup.ps1 -DryRun
  pwsh -NoProfile -File scripts/review-github-setup.ps1
#>
param(
    [string]$VaultItem = 'GitHub App - antiphon-review',
    [string]$Repo = 'michal-ciechan/Antiphon',
    [string]$Project = 'Antiphon',
    [string]$PublicBaseUrl = 'https://antiphon.desktop.codeperf.net',
    [string]$ViewerLogin = 'michal-ciechan',
    [string]$SecretsId = 'antiphon-server',
    # Prints the settings that would be written, with the key redacted, and writes nothing.
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not $env:BW_SESSION) {
    $sessionFile = Join-Path $HOME '.bw-session'
    if (-not (Test-Path -LiteralPath $sessionFile)) { throw 'Bitwarden is locked: no BW_SESSION and no ~/.bw-session. Unlock it first (bitwarden skill).' }
    $env:BW_SESSION = (Get-Content -LiteralPath $sessionFile -Raw).Trim()
}

$raw = & bw get item $VaultItem --nointeraction
if ($LASTEXITCODE -ne 0 -or -not $raw) { throw "Bitwarden item '$VaultItem' not found or the vault is locked." }
$item = $raw | ConvertFrom-Json
$raw = $null

function Get-Field([string]$name) {
    $f = $item.fields | Where-Object { $_.name -eq $name } | Select-Object -First 1
    if (-not $f -or -not $f.value) { throw "Bitwarden item '$VaultItem' has no '$name' field." }
    return [string]$f.value
}

$clientId = Get-Field 'Client ID'
$installationId = [long](Get-Field 'Installation ID')
$pem = [string]$item.notes
if ($pem -notmatch 'BEGIN (RSA )?PRIVATE KEY') { throw "Bitwarden item '$VaultItem' notes do not hold a PEM private key." }

$settings = [ordered]@{
    'Review:CodeHosts:Hosts:github:Kind'                = 'GitHub'
    'Review:CodeHosts:Hosts:github:App:AppId'           = $clientId
    'Review:CodeHosts:Hosts:github:App:InstallationId'  = "$installationId"
    'Review:CodeHosts:Hosts:github:App:PrivateKeyConfigKey' = 'Review:Secrets:GitHubAppPrivateKey'
    'Review:Secrets:GitHubAppPrivateKey'                = $pem
    'Review:PublicBaseUrl'                              = $PublicBaseUrl
    'Review:DefaultViewerLogin'                         = $ViewerLogin
    'Review:Agents:Repos:0:Repo'                        = $Repo
    'Review:Agents:Repos:0:Project'                     = $Project
    # Reviewers run on this machine, where the project's checkout fetches refs/pull/N/head.
    'Review:Agents:Runner'                              = 'local'
    # Verify on a Haiku seat (a delegate task). If it cannot answer, verify continues unverified
    # (onFailure: continue), so a large diff no longer fails the run.
    'Review:Completions:Chains:completions-cheap:0:Provider'  = 'haiku-delegate'
    'Review:Completions:Chains:completions-cheap:0:Transport' = 'Seat'
    # No public webhook here: poll the repo for new PRs, pushes and /antiphon commands.
    'Review:Polling:Enabled'                            = 'true'
    'Review:Polling:IntervalSeconds'                    = '120'
    'Review:Polling:Repos:0:Repo'                       = $Repo
    # A one-person repo: the author may decide their own gates.
    'Review:Policy:AuthorMayDecideRepos:0'              = $Repo
}

if ($DryRun) {
    foreach ($k in $settings.Keys) {
        $v = if ($k -eq 'Review:Secrets:GitHubAppPrivateKey') { "<PEM, $($pem.Length) chars>" } else { $settings[$k] }
        Write-Host ("{0} = {1}" -f $k, $v)
    }
    Write-Host 'Dry run: nothing written.'
    return
}

$json = $settings | ConvertTo-Json -Compress
$json | & dotnet user-secrets set --id $SecretsId | Out-Null
$exit = $LASTEXITCODE
$json = $null; $pem = $null; $item = $null
if ($exit -ne 0) { throw "dotnet user-secrets set failed (exit $exit)." }

# Never `user-secrets list` here: it prints values, the key among them.
Write-Host ("Set {0} review settings in user-secrets '{1}': {2}" -f $settings.Count, $SecretsId, ($settings.Keys -join ', '))
Write-Host 'Restart the AppHost from the main checkout to load them.'
