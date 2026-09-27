# Shared token reader for runner operator scripts. Dot-source only; never print the value.
function Get-RunnerOperatorToken {
    $path = $env:ANTIPHON_OPERATOR_TOKEN_FILE
    if ([string]::IsNullOrWhiteSpace($path)) {
        $path = Join-Path (Join-Path $env:LOCALAPPDATA 'Antiphon') 'operator-token'
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Operator token file not found at $path. The server creates it at startup; run this as the account the server runs under."
    }
    $token = (Get-Content -LiteralPath $path -Raw).Trim()
    if ([string]::IsNullOrEmpty($token)) { throw "Operator token file at $path is empty." }
    return $token
}
