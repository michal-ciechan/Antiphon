<#
.SYNOPSIS
  Runs the review engine's live GitHub check (tests/Antiphon.Tests/Review/ReviewLiveGitHubTests.cs)
  against a real scratch PR, signed in as the antiphon-review GitHub App.

.DESCRIPTION
  Reads the app's client id, installation id and PEM key from the Bitwarden item (Bitwarden must be
  unlocked: BW_SESSION or ~/.bw-session), puts them in this process's environment only, and runs the
  test class through the build slot. The key is never printed or written to a file.

  The PR must be a scratch PR labelled antiphon:live-check: the check posts a scripted finding, a
  thread reply and commit statuses on it. Label it antiphon:skip too, so a live instance's poller
  leaves it alone.

.EXAMPLE
  pwsh -NoProfile -File scripts/review-live-github.ps1 -Pr 12
#>
param(
    [Parameter(Mandatory = $true)][int]$Pr,
    [string]$Repo = 'michal-ciechan/Antiphon',
    [string]$VaultItem = 'GitHub App - antiphon-review',
    [string]$RequiredLabel = 'antiphon:live-check',
    # Reuse the last build of tests/Antiphon.Tests instead of building first.
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$labels = & gh pr view $Pr --repo $Repo --json labels --jq '.labels[].name'
if ($LASTEXITCODE -ne 0) { throw "Could not read PR #$Pr on $Repo with gh." }
if (-not ($labels -contains $RequiredLabel)) { throw "PR #$Pr is not labelled $RequiredLabel; the live check only posts on a scratch PR." }

if (-not $env:BW_SESSION) {
    $sessionFile = Join-Path $HOME '.bw-session'
    if (-not (Test-Path -LiteralPath $sessionFile)) { throw 'Bitwarden is locked: no BW_SESSION and no ~/.bw-session.' }
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

$env:ANTIPHON_LIVE_GITHUB_APP_ID = Get-Field 'Client ID'
$env:ANTIPHON_LIVE_GITHUB_INSTALLATION_ID = Get-Field 'Installation ID'
$env:ANTIPHON_LIVE_GITHUB_APP_KEY = [string]$item.notes
$env:ANTIPHON_LIVE_GITHUB_REPO = $Repo
$env:ANTIPHON_LIVE_GITHUB_PR = "$Pr"
$item = $null

try {
    $run = @('run', '--project', 'tests/Antiphon.Tests')
    if ($NoBuild) { $run += '--no-build' }
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build-slot.ps1') -Label review-live-github -- `
        dotnet @run -- --treenode-filter '/*/*/ReviewLiveGitHubTests/*'
    exit $LASTEXITCODE
}
finally {
    Remove-Item Env:ANTIPHON_LIVE_GITHUB_APP_KEY -ErrorAction SilentlyContinue
}
