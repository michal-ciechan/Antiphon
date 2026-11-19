# Operator-only desktop-win phase 1 secret and server map entry.
# Dry-run unless -Execute. -WhatIf stays a dry-run. -Rotate replaces an
# existing SharedSecret. ASCII, Windows PowerShell 5.1.
#
# The secret is generated in memory and written in-process. dotnet user-secrets
# set takes the value as a process argument, and the CLI has no supported stdin
# form for that value, so the command line would expose it. The production
# server reads %APPDATA%\Microsoft\UserSecrets\antiphon-server\secrets.json.
# This helper updates that JSON directly. No child process receives the value.
#
# scripts/lib/windows-phone-home-env.ps1 does not generate
# C:\Antiphon\runner\phone-home.phase1.json. -Execute copies
# scripts/phone-home.phase1.example.json there when the file is absent.
# The example has no secret. The operator can copy it by hand instead.
#
#   pwsh -NoProfile -File scripts/desktop-win-phase1-secrets.ps1
#   pwsh -NoProfile -File scripts/desktop-win-phase1-secrets.ps1 -Execute
param(
    [switch]$Execute,
    [switch]$Rotate,
    [switch]$WhatIf,
    [string]$UserSecretsPath,
    [string]$SecretFile,
    [string]$ProfileHome,
    [string]$Phase1ConfigPath
)
$ErrorActionPreference = 'Stop'

function Refuse([string]$Code, [string]$Message) {
    Write-Host ('REFUSED: ' + $Code)
    Write-Host $Message
    exit 2
}

function Format-Exists([string]$Path) {
    if (Test-Path -LiteralPath $Path) { return 'present' }
    return 'absent'
}

function ConvertTo-Dw3JsonString([string]$Value) {
    if ($null -eq $Value) { $Value = '' }
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    foreach ($ch in $Value.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq '"') {
            [void]$sb.Append('\')
            [void]$sb.Append('"')
        }
        elseif ($ch -eq '\') {
            [void]$sb.Append('\')
            [void]$sb.Append('\')
        }
        elseif ($ch -eq "`n") { [void]$sb.Append('\n') }
        elseif ($ch -eq "`r") { [void]$sb.Append('\r') }
        elseif ($ch -eq "`t") { [void]$sb.Append('\t') }
        elseif ($code -lt 32 -or $code -gt 126) { [void]$sb.AppendFormat('\u{0:x4}', $code) }
        else { [void]$sb.Append($ch) }
    }
    [void]$sb.Append('"')
    return $sb.ToString()
}

function Format-Dw3JsonScalar($Value) {
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [string]) { return (ConvertTo-Dw3JsonString $Value) }
    if ($Value -is [bool]) {
        if ($Value) { return 'true' }
        return 'false'
    }
    $type = $Value.GetType().FullName
    if ($type -eq 'System.Int32' -or $type -eq 'System.Int64' -or $type -eq 'System.Int16' -or $type -eq 'System.Byte') {
        return ([System.Convert]::ToString($Value, 10))
    }
    if ($type -eq 'System.Double' -or $type -eq 'System.Decimal' -or $type -eq 'System.Single') {
        $whole = [Math]::Truncate([double]$Value)
        $delta = [Math]::Abs(([double]$Value) - $whole)
        if ($delta -lt 0.0000001) { return ([System.Convert]::ToString([long]$whole, 10)) }
    }
    return $null
}

function Read-UserSecretMap([string]$Path) {
    $map = New-Object System.Collections.Specialized.OrderedDictionary
    if (-not (Test-Path -LiteralPath $Path)) { return ,$map }
    $raw = [System.IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($raw)) { return ,$map }
    try { $parsed = $raw | ConvertFrom-Json }
    catch { throw 'user-secrets JSON is malformed' }
    if ($null -eq $parsed) { return ,$map }
    if ($parsed -is [System.Array]) { throw 'user-secrets JSON must be an object' }
    foreach ($prop in $parsed.PSObject.Properties) { $map.Add($prop.Name, $prop.Value) }
    return ,$map
}

function Write-UserSecretMap([string]$Path, $Map) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('{')
    [void]$sb.Append("`n")
    $keys = @($Map.Keys)
    for ($i = 0; $i -lt $keys.Length; $i++) {
        $key = [string]$keys[$i]
        $encoded = Format-Dw3JsonScalar $Map[$key]
        if ($null -eq $encoded) { throw 'user-secrets value is not a scalar' }
        $line = '  ' + (ConvertTo-Dw3JsonString $key) + ': ' + $encoded
        if ($i -lt ($keys.Length - 1)) { $line = $line + ',' }
        [void]$sb.Append($line)
        [void]$sb.Append("`n")
    }
    [void]$sb.Append('}')
    [void]$sb.Append("`n")
    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $sb.ToString(), $utf8)
}

function New-Dw3Secret {
    $rng = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
    try {
        $bytes = New-Object byte[] 32
        $rng.GetBytes($bytes)
        $sb = New-Object System.Text.StringBuilder 64
        foreach ($b in $bytes) { [void]$sb.AppendFormat('{0:x2}', $b) }
        [Array]::Clear($bytes, 0, $bytes.Length)
        return $sb.ToString()
    }
    finally { $rng.Dispose() }
}

function Invoke-Dw3Native {
    param([string]$File, [string[]]$ArgumentList, [string]$Secret)
    if (-not [string]::IsNullOrEmpty($Secret)) {
        if ($File.Contains($Secret)) { throw 'refusing to pass the secret on a command line' }
        foreach ($arg in $ArgumentList) {
            if ($null -ne $arg -and $arg.Contains($Secret)) { throw 'refusing to pass the secret on a command line' }
        }
    }
    & $File @ArgumentList
    if ($LASTEXITCODE -ne 0) { throw ($File + ' failed (' + $LASTEXITCODE + ')') }
}

function Get-UnixMode([string]$Path) {
    $mode = & stat -c '%a' -- $Path
    if ($LASTEXITCODE -ne 0) { return $null }
    return ([string]$mode).Trim()
}

function Get-PathOwner([string]$Path) {
    if ($env:OS -eq 'Windows_NT') {
        $acl = Get-Acl -LiteralPath $Path
        $owner = [string]$acl.Owner
        $slash = $owner.LastIndexOf('\')
        if ($slash -ge 0 -and $slash -lt ($owner.Length - 1)) { return $owner.Substring($slash + 1) }
        return $owner
    }
    $name = & stat -c '%U' -- $Path
    if ($LASTEXITCODE -ne 0) { return $null }
    return ([string]$name).Trim()
}

function Find-OwnerAnchor([string]$Dir) {
    $current = $Dir
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) { return $current }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) { return $null }
        $current = $parent
    }
    return $null
}

function Set-OwnerOnlyFile([string]$Path, [string]$Secret) {
    if ($env:OS -eq 'Windows_NT') {
        Invoke-Dw3Native -File 'icacls' -ArgumentList @($Path, '/inheritance:r') -Secret $Secret
        $who = $env:USERNAME
        if (-not [string]::IsNullOrWhiteSpace($env:USERDOMAIN)) { $who = $env:USERDOMAIN + '\' + $env:USERNAME }
        Invoke-Dw3Native -File 'icacls' -ArgumentList @($Path, '/grant:r', ($who + ':(F)')) -Secret $Secret
        $acl = Get-Acl -LiteralPath $Path
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($rule in @($acl.Access)) { [void]$acl.RemoveAccessRule($rule) }
        $rights = [System.Security.AccessControl.FileSystemRights]::FullControl
        $allow = [System.Security.AccessControl.AccessControlType]::Allow
        $access = New-Object System.Security.AccessControl.FileSystemAccessRule($who, $rights, $allow)
        $acl.AddAccessRule($access)
        Set-Acl -LiteralPath $Path -AclObject $acl
        return
    }
    Invoke-Dw3Native -File 'chmod' -ArgumentList @('600', $Path) -Secret $Secret
}

function Restore-SavedAcl([string]$Path, $Saved, [string]$Secret) {
    if ($null -eq $Saved) { return }
    if ($env:OS -eq 'Windows_NT') {
        Set-Acl -LiteralPath $Path -AclObject $Saved
        return
    }
    Invoke-Dw3Native -File 'chmod' -ArgumentList @([string]$Saved, $Path) -Secret $Secret
}

function Get-DesktopWinEntries([string]$HomeDir, [string]$Secret) {
    $map = New-Object System.Collections.Specialized.OrderedDictionary
    $prefix = 'PhoneHomeRunner:Runners:desktop-win:'
    $map.Add($prefix + 'Enabled', 'true')
    $map.Add($prefix + 'AllowDelegatedTasks', 'true')
    $map.Add($prefix + 'DisplayName', 'desktop-win')
    $map.Add($prefix + 'HostWorkspaceRoot', 'C:\src\Antiphon')
    $map.Add($prefix + 'RunnerWorkspace', 'C:/Antiphon/runner/work')
    $map.Add($prefix + 'RunnerRepository', 'C:/Antiphon/runner/work/repos/antiphon')
    $map.Add($prefix + 'MaxCapacity', '1')
    $map.Add($prefix + 'RawExeAllowList:0', 'C:/Program Files/PowerShell/7/pwsh.exe')
    $map.Add($prefix + 'CallbackOrigin', 'https://antiphon.desktop.codeperf.net')
    $map.Add($prefix + 'SharedSecret', $Secret)
    $map.Add($prefix + 'ChildClaudeHome', (Join-PhoneHomeChild $HomeDir '.claude'))
    $map.Add($prefix + 'ChildCodexHome', (Join-PhoneHomeChild $HomeDir '.codex'))
    $map.Add($prefix + 'ChildGrokHome', (Join-PhoneHomeChild $HomeDir '.grok'))
    $map.Add($prefix + 'ClaudeAuthProbeEnabled', 'true')
    $map.Add($prefix + 'CodexAuthProbeEnabled', 'true')
    return ,$map
}

if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_ORCHESTRATOR) -or -not [string]::IsNullOrWhiteSpace($env:CLAUDECODE)) {
    Refuse 'agent-shell' 'ANTIPHON_ORCHESTRATOR or CLAUDECODE is set. This helper is operator-only.'
}

$windows = ($env:OS -eq 'Windows_NT')
if (-not $windows) {
    if ([string]::IsNullOrWhiteSpace($UserSecretsPath) -or [string]::IsNullOrWhiteSpace($SecretFile)) {
        Refuse 'not-windows' 'Pass -UserSecretsPath and -SecretFile. The operator runs this helper on Windows.'
    }
}
if ([string]::IsNullOrWhiteSpace($SecretFile)) { $SecretFile = 'C:\Antiphon\runner\secrets\phone-home' }
if ([string]::IsNullOrWhiteSpace($UserSecretsPath)) {
    $appData = $env:APPDATA
    if ([string]::IsNullOrWhiteSpace($appData) -and -not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $appData = Join-Path $env:USERPROFILE 'AppData\Roaming'
    }
    if ([string]::IsNullOrWhiteSpace($appData)) { Refuse 'user-secrets-path-missing' 'APPDATA is not set. Pass -UserSecretsPath.' }
    $UserSecretsPath = Join-Path $appData 'Microsoft\UserSecrets\antiphon-server\secrets.json'
}
if ([string]::IsNullOrWhiteSpace($ProfileHome)) {
    if (-not [string]::IsNullOrWhiteSpace($HOME)) { $ProfileHome = $HOME }
    elseif (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) { $ProfileHome = $env:USERPROFILE }
}
if ([string]::IsNullOrWhiteSpace($Phase1ConfigPath)) {
    $secretsParent = Split-Path -Parent $SecretFile
    $runnerRoot = $null
    if (-not [string]::IsNullOrWhiteSpace($secretsParent)) { $runnerRoot = Split-Path -Parent $secretsParent }
    if ([string]::IsNullOrWhiteSpace($runnerRoot)) { Refuse 'phase1-path' 'Pass -Phase1ConfigPath. The secret path has no runner root.' }
    $Phase1ConfigPath = Join-Path $runnerRoot 'phone-home.phase1.json'
}

$secretsDir = Split-Path -Parent $SecretFile
if ([string]::IsNullOrWhiteSpace($secretsDir)) { Refuse 'secret-path' 'The secret file path has no directory.' }
if ([string]::IsNullOrWhiteSpace($env:USERNAME)) { Refuse 'username-missing' 'USERNAME is not set.' }
$anchor = Find-OwnerAnchor $secretsDir
if ($null -eq $anchor) { Refuse 'secrets-dir-owner' 'The secrets directory does not exist and has no existing parent.' }
$ownerName = Get-PathOwner $anchor
if ([string]::IsNullOrWhiteSpace($ownerName) -or -not $ownerName.Equals($env:USERNAME, [StringComparison]::OrdinalIgnoreCase)) {
    Refuse 'secrets-dir-owner' 'USERNAME does not match the secrets directory owner.'
}

$apply = $false
if ($Execute -and -not $WhatIf) { $apply = $true }
if ($apply -and [string]::IsNullOrWhiteSpace($ProfileHome)) { Refuse 'profile-home-missing' 'Profile home is empty.' }

$lib = Join-Path $PSScriptRoot 'lib/windows-phone-home-env.ps1'
if (-not (Test-Path -LiteralPath $lib)) { Refuse 'phone-home-lib-missing' 'scripts/lib/windows-phone-home-env.ps1 is not beside this script.' }
. $lib

$example = Join-Path $PSScriptRoot 'phone-home.phase1.example.json'
try { $map = Read-UserSecretMap $UserSecretsPath }
catch { Refuse 'user-secrets-malformed' 'The user-secrets file could not be read as a flat JSON object.' }

$secretKey = 'PhoneHomeRunner:Runners:desktop-win:SharedSecret'
$hasSecret = $false
if ($map.Contains($secretKey) -and -not [string]::IsNullOrWhiteSpace([string]$map[$secretKey])) { $hasSecret = $true }
if ($apply -and $hasSecret -and -not $Rotate) {
    Refuse 'shared-secret-present' 'PhoneHomeRunner:Runners:desktop-win:SharedSecret is already set. Re-run with -Rotate to replace it. Nothing was changed.'
}

$entries = Get-DesktopWinEntries -HomeDir $ProfileHome -Secret ''
if (-not $apply) {
    Write-Host 'mode: dry-run'
    Write-Host ('secret-file: ' + $SecretFile + ' ' + (Format-Exists $SecretFile))
    Write-Host ('user-secrets: ' + $UserSecretsPath + ' ' + (Format-Exists $UserSecretsPath))
    Write-Host ('secrets-dir: ' + $secretsDir + ' ' + (Format-Exists $secretsDir))
    Write-Host ('phase1-config: ' + $Phase1ConfigPath + ' ' + (Format-Exists $Phase1ConfigPath))
    Write-Host ('phase1-example: ' + $example + ' ' + (Format-Exists $example))
    if ($hasSecret) { Write-Host 'shared-secret-key: present' } else { Write-Host 'shared-secret-key: absent' }
    Write-Host 'standing-agent: unset'
    Write-Host 'keys:'
    foreach ($key in @($entries.Keys)) { Write-Host ('  ' + $key) }
    if ($Rotate) { Write-Host 'rotate: requested' }
    Write-Host 'changes: none'
    exit 0
}

if (-not (Test-Path -LiteralPath $example)) { Refuse 'phase1-example-missing' 'scripts/phone-home.phase1.example.json is not beside this script.' }

try {
    if (-not (Test-Path -LiteralPath $secretsDir)) { New-Item -ItemType Directory -Path $secretsDir | Out-Null }
    $storeDir = Split-Path -Parent $UserSecretsPath
    if (-not [string]::IsNullOrWhiteSpace($storeDir) -and -not (Test-Path -LiteralPath $storeDir)) {
        New-Item -ItemType Directory -Path $storeDir | Out-Null
    }
    $phaseWrote = $false
    if (-not (Test-Path -LiteralPath $Phase1ConfigPath)) {
        $phaseParent = Split-Path -Parent $Phase1ConfigPath
        if (-not [string]::IsNullOrWhiteSpace($phaseParent) -and -not (Test-Path -LiteralPath $phaseParent)) {
            New-Item -ItemType Directory -Path $phaseParent | Out-Null
        }
        [System.IO.File]::Copy($example, $Phase1ConfigPath, $false)
        $phaseWrote = $true
    }

    $secret = New-Dw3Secret
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($SecretFile, ($secret + "`n"), $utf8)
    Set-OwnerOnlyFile -Path $SecretFile -Secret $secret

    $savedAcl = $null
    if (Test-Path -LiteralPath $UserSecretsPath) {
        if ($windows) { $savedAcl = Get-Acl -LiteralPath $UserSecretsPath }
        else { $savedAcl = Get-UnixMode $UserSecretsPath }
    }
    $real = Get-DesktopWinEntries -HomeDir $ProfileHome -Secret $secret
    foreach ($key in @($real.Keys)) {
        if ($map.Contains($key)) { $map[$key] = [string]$real[$key] }
        else { $map.Add($key, [string]$real[$key]) }
    }
    Write-UserSecretMap -Path $UserSecretsPath -Map $map
    Restore-SavedAcl -Path $UserSecretsPath -Saved $savedAcl -Secret $secret

    Write-Host 'mode: execute'
    Write-Host 'secret-file: wrote'
    Write-Host 'user-secrets: updated'
    if ($phaseWrote) { Write-Host 'phase1-config: wrote' } else { Write-Host 'phase1-config: present' }
    Write-Host ('keys-set: ' + $real.Count)
    Write-Host 'standing-agent: unset'
    Write-Host 'NEXT 1: restart AppHost from C:\src\Antiphon'
    Write-Host 'pwsh -NoProfile -File C:\src\Antiphon\scripts\restart-apphost.ps1'
    Write-Host 'NEXT 2: start the phase 1 runner from the side clone in a normal terminal'
    Write-Host ('pwsh -NoProfile -File C:\Antiphon\runner\app\scripts\autostart-session-runner.ps1 -PhoneHomeConfigPath ' + $Phase1ConfigPath)
    Write-Host 'NEXT 3: after the restart, verify'
    Write-Host 'GET http://localhost:17202/api/session-runners/desktop-win/status'
}
catch {
    Refuse 'failed' 'The helper stopped before finishing. No secret was written to the console.'
}
exit 0
