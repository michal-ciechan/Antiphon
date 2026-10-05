#requires -Version 7.0
# CARD-0817. Operator-only: read the vault through its relay session, never through argv.
[CmdletBinding()]
param(
    [ValidatePattern('^(?:[A-Za-z0-9_][A-Za-z0-9_.-]*@)?[A-Za-z0-9][A-Za-z0-9.-]*$')]
    [string]$Server = 'mc@server2'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$vaultItem = '97819ca3-710f-43f8-99ce-b4da013e32c1'
$skipped = 'GitHub token refresh skipped, previous file unchanged: unlock the vault relay and retry.'
$previous = $env:BW_SESSION
$session = $previous
$token = ''
$stage = 'vault'

try {
    if (-not $session) {
        $pickup = Join-Path $HOME '.bw-session'
        if (Test-Path -LiteralPath $pickup) { $session = (Get-Content -Raw -LiteralPath $pickup).Trim() }
    }
    if (-not $session -or -not (Get-Command bw -ErrorAction SilentlyContinue)
        -or -not (Get-Command ssh -ErrorAction SilentlyContinue)) {
        Write-Warning $skipped
        exit 2
    }
    $env:BW_SESSION = $session
    $token = (& bw get password $vaultItem --nointeraction 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $token -or $token -match '\s') {
        Write-Warning $skipped
        exit 2
    }

    # A unique sibling avoids collisions between refreshes. Nothing changes the old inode
    # until the complete, non-empty replacement has its final owner and mode.
    $upload = @'
set -eu
umask 077
d=/home/mc/antiphon-server2/secrets/github-token
test -d "$d" && test ! -L "$d"
tmp=$(mktemp "$d/token.tmp.XXXXXXXX")
trap 'rm -f -- "$tmp"' EXIT HUP INT TERM
cat > "$tmp" && test -s "$tmp" && chown 1654:1654 "$tmp" && chmod 0400 "$tmp" && mv -f "$tmp" "$d/token"
'@
    $remote = "sudo -n sh -c '" + $upload.Replace("'", "'\''") + "'"
    $stage = 'upload'
    $token | & ssh -o BatchMode=yes -o ConnectTimeout=30 $Server $remote 1>$null 2>$null
    $token = ''
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'GitHub token refresh was not confirmed over SSH. Write errors before atomic replacement leave the previous file unchanged; a lost connection may require a metadata check.'
        exit 1
    }

    $verify = @'
set -eu
d=/home/mc/antiphon-server2/secrets/github-token
stat -c '%u:%g %a' "$d/token"
if test -s "$d/token"; then printf 'present=true\n'; else printf 'present=false\n'; fi
'@
    $remote = "sudo -n sh -c '" + $verify.Replace("'", "'\''") + "'"
    $stage = 'verify'
    $metadata = @(& ssh -o BatchMode=yes -o ConnectTimeout=30 $Server $remote 2>$null)
    if ($LASTEXITCODE -ne 0 -or $metadata.Count -ne 2
        -or $metadata[0] -cne '1654:1654 400' -or $metadata[1] -cne 'present=true') {
        Write-Warning 'GitHub token was delivered, but owner/mode/presence verification failed. No file contents were printed.'
        exit 1
    }
    # Only these two validated metadata lines can reach stdout.
    Write-Output $metadata
    exit 0
}
catch {
    # Never render an exception or external-tool output: either could carry a credential.
    if ($stage -eq 'vault') { Write-Warning $skipped; exit 2 }
    Write-Warning 'GitHub token refresh was not confirmed; check SSH and the directory metadata before retrying.'
    exit 1
}
finally {
    $token = ''
    $env:BW_SESSION = $previous
    $session = ''
    $previous = ''
}
