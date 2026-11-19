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
# The user-secrets file is parsed by the reader in this script, not by
# ConvertFrom-Json. Windows PowerShell 5.1 has no -DateKind, and pwsh 7
# ConvertFrom-Json turns a date-shaped string into DateTime. One reader keeps
# strings, numbers, nested objects and arrays on both hosts, so a date-shaped
# string round-trips as that string.
#
# Nothing that can fail is done after a secret byte is on disk. The helper
# validates and serialises the new JSON in memory, writes each half to an
# owner-only sibling in the same directory, then swaps them. A failure deletes
# the siblings and restores the half that already moved, or leaves both old.
# It prints the half names only. There is no .bak.
#
# scripts/lib/windows-phone-home-env.ps1 does not generate
# C:\Antiphon\runner\phone-home.phase1.json. -Execute copies
# scripts/phone-home.phase1.example.json there when the file is absent.
# The example has no secret. The operator can copy it by hand instead.
#
# The owner check uses the first existing ancestor of the secrets directory.
# A directory created from an elevated shell can be owned by
# BUILTIN\Administrators, and the helper then refuses secrets-dir-owner.
# Run it from a shell whose USERNAME owns that directory.
#
# DW3_FAIL_STEP and DW3_MODE_AT_WRITE are test seams. The fail step aborts
# and never prints a value. The mode probe records only a mode line.
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

function Skip-Dw3Ws {
    while ($script:jp -lt $script:js.Length) {
        $c = $script:js[$script:jp]
        if ($c -eq ' ' -or $c -eq "`t" -or $c -eq "`r" -or $c -eq "`n") { $script:jp++ }
        else { break }
    }
}

function Read-Dw3JsonString {
    if ($script:jp -ge $script:js.Length -or $script:js[$script:jp] -ne '"') { throw 'user-secrets JSON is malformed' }
    $script:jp++
    $sb = New-Object System.Text.StringBuilder
    while ($script:jp -lt $script:js.Length) {
        $c = $script:js[$script:jp]
        $script:jp++
        if ($c -eq '"') { return @{ T = 'string'; V = $sb.ToString() } }
        if ($c -eq '\') {
            if ($script:jp -ge $script:js.Length) { throw 'user-secrets JSON is malformed' }
            $e = $script:js[$script:jp]
            $script:jp++
            if ($e -eq '"' -or $e -eq '\' -or $e -eq '/') { [void]$sb.Append($e) }
            elseif ($e -eq 'b') { [void]$sb.Append([char]8) }
            elseif ($e -eq 'f') { [void]$sb.Append([char]12) }
            elseif ($e -eq 'n') { [void]$sb.Append("`n") }
            elseif ($e -eq 'r') { [void]$sb.Append("`r") }
            elseif ($e -eq 't') { [void]$sb.Append("`t") }
            elseif ($e -eq 'u') {
                if (($script:jp + 4) -gt $script:js.Length) { throw 'user-secrets JSON is malformed' }
                $hex = $script:js.Substring($script:jp, 4)
                $script:jp += 4
                $code = 0
                $styles = [System.Globalization.NumberStyles]::HexNumber
                $culture = [System.Globalization.CultureInfo]::InvariantCulture
                $ok = [int]::TryParse($hex, $styles, $culture, [ref]$code)
                if (-not $ok) { throw 'user-secrets JSON is malformed' }
                [void]$sb.Append([char]$code)
            }
            else { throw 'user-secrets JSON is malformed' }
        }
        else {
            if ([int]$c -lt 32) { throw 'user-secrets JSON is malformed' }
            [void]$sb.Append($c)
        }
    }
    throw 'user-secrets JSON is malformed'
}

function Read-Dw3JsonLiteral([string]$Word, [string]$Kind, $Value) {
    $len = $Word.Length
    if (($script:jp + $len) -gt $script:js.Length) { throw 'user-secrets JSON is malformed' }
    $slice = $script:js.Substring($script:jp, $len)
    if ($slice -cne $Word) { throw 'user-secrets JSON is malformed' }
    $script:jp += $len
    return @{ T = $Kind; V = $Value }
}

function Read-Dw3JsonNumber {
    $start = $script:jp
    $c = $script:js[$script:jp]
    if ($c -eq '-') {
        $script:jp++
        if ($script:jp -ge $script:js.Length) { throw 'user-secrets JSON is malformed' }
        $c = $script:js[$script:jp]
    }
    if ($c -lt '0' -or $c -gt '9') { throw 'user-secrets JSON is malformed' }
    if ($c -eq '0') { $script:jp++ }
    else {
        while ($script:jp -lt $script:js.Length) {
            $c = $script:js[$script:jp]
            if ($c -lt '0' -or $c -gt '9') { break }
            $script:jp++
        }
    }
    if ($script:jp -lt $script:js.Length -and $script:js[$script:jp] -eq '.') {
        $script:jp++
        $digits = 0
        while ($script:jp -lt $script:js.Length) {
            $c = $script:js[$script:jp]
            if ($c -lt '0' -or $c -gt '9') { break }
            $digits++
            $script:jp++
        }
        if ($digits -lt 1) { throw 'user-secrets JSON is malformed' }
    }
    if ($script:jp -lt $script:js.Length) {
        $exp = $script:js[$script:jp]
        if ($exp -eq 'e' -or $exp -eq 'E') {
            $script:jp++
            if ($script:jp -lt $script:js.Length) {
                $sign = $script:js[$script:jp]
                if ($sign -eq '+' -or $sign -eq '-') { $script:jp++ }
            }
            $digits = 0
            while ($script:jp -lt $script:js.Length) {
                $c = $script:js[$script:jp]
                if ($c -lt '0' -or $c -gt '9') { break }
                $digits++
                $script:jp++
            }
            if ($digits -lt 1) { throw 'user-secrets JSON is malformed' }
        }
    }
    if ($script:jp -le $start) { throw 'user-secrets JSON is malformed' }
    $raw = $script:js.Substring($start, ($script:jp - $start))
    return @{ T = 'number'; V = $raw }
}

function Read-Dw3JsonAny([int]$Depth) {
    if ($Depth -gt 32) { throw 'user-secrets JSON is malformed' }
    Skip-Dw3Ws
    if ($script:jp -ge $script:js.Length) { throw 'user-secrets JSON is malformed' }
    $c = $script:js[$script:jp]
    if ($c -eq '{') { return (Read-Dw3JsonObject ($Depth + 1)) }
    if ($c -eq '[') { return (Read-Dw3JsonArray ($Depth + 1)) }
    if ($c -eq '"') { return (Read-Dw3JsonString) }
    if ($c -eq 't') { return (Read-Dw3JsonLiteral 'true' 'bool' $true) }
    if ($c -eq 'f') { return (Read-Dw3JsonLiteral 'false' 'bool' $false) }
    if ($c -eq 'n') { return (Read-Dw3JsonLiteral 'null' 'null' $null) }
    if ($c -eq '-' -or ($c -ge '0' -and $c -le '9')) { return (Read-Dw3JsonNumber) }
    throw 'user-secrets JSON is malformed'
}

function Read-Dw3JsonObject([int]$Depth) {
    if ($script:jp -ge $script:js.Length -or $script:js[$script:jp] -ne '{') { throw 'user-secrets JSON is malformed' }
    $script:jp++
    $map = New-Object System.Collections.Specialized.OrderedDictionary
    Skip-Dw3Ws
    if ($script:jp -lt $script:js.Length -and $script:js[$script:jp] -eq '}') {
        $script:jp++
        return @{ T = 'object'; V = $map }
    }
    while ($true) {
        Skip-Dw3Ws
        if ($script:jp -ge $script:js.Length -or $script:js[$script:jp] -ne '"') { throw 'user-secrets JSON is malformed' }
        $keyNode = Read-Dw3JsonString
        $key = [string]$keyNode.V
        if ($map.Contains($key)) { throw 'user-secrets JSON is malformed' }
        Skip-Dw3Ws
        if ($script:jp -ge $script:js.Length -or $script:js[$script:jp] -ne ':') { throw 'user-secrets JSON is malformed' }
        $script:jp++
        $value = Read-Dw3JsonAny $Depth
        $map.Add($key, $value)
        Skip-Dw3Ws
        if ($script:jp -ge $script:js.Length) { throw 'user-secrets JSON is malformed' }
        $c = $script:js[$script:jp]
        if ($c -eq ',') {
            $script:jp++
            continue
        }
        if ($c -eq '}') {
            $script:jp++
            break
        }
        throw 'user-secrets JSON is malformed'
    }
    return @{ T = 'object'; V = $map }
}

function Read-Dw3JsonArray([int]$Depth) {
    if ($script:jp -ge $script:js.Length -or $script:js[$script:jp] -ne '[') { throw 'user-secrets JSON is malformed' }
    $script:jp++
    $items = New-Object System.Collections.ArrayList
    Skip-Dw3Ws
    if ($script:jp -lt $script:js.Length -and $script:js[$script:jp] -eq ']') {
        $script:jp++
        return @{ T = 'array'; V = $items }
    }
    while ($true) {
        $value = Read-Dw3JsonAny $Depth
        [void]$items.Add($value)
        Skip-Dw3Ws
        if ($script:jp -ge $script:js.Length) { throw 'user-secrets JSON is malformed' }
        $c = $script:js[$script:jp]
        if ($c -eq ',') {
            $script:jp++
            continue
        }
        if ($c -eq ']') {
            $script:jp++
            break
        }
        throw 'user-secrets JSON is malformed'
    }
    return @{ T = 'array'; V = $items }
}

function Read-Dw3JsonDocument([string]$Text) {
    $script:js = $Text
    $script:jp = 0
    Skip-Dw3Ws
    if ($script:jp -ge $script:js.Length) { throw 'user-secrets JSON is malformed' }
    $value = Read-Dw3JsonAny 0
    Skip-Dw3Ws
    if ($script:jp -ne $script:js.Length) { throw 'user-secrets JSON is malformed' }
    return $value
}

function ConvertTo-Dw3JsonText($Node, [int]$Depth) {
    if ($null -eq $Node) { throw 'user-secrets JSON is malformed' }
    if ($Depth -gt 32) { throw 'user-secrets JSON is malformed' }
    $kind = [string]$Node.T
    if ($kind -eq 'null') { return 'null' }
    if ($kind -eq 'bool') {
        if ($Node.V) { return 'true' }
        return 'false'
    }
    if ($kind -eq 'number') { return [string]$Node.V }
    if ($kind -eq 'string') { return (ConvertTo-Dw3JsonString ([string]$Node.V)) }
    if ($kind -eq 'array') {
        $items = @($Node.V)
        $sb = New-Object System.Text.StringBuilder
        [void]$sb.Append('[')
        if ($Node.V.Count -eq 0) {
            [void]$sb.Append(']')
            return $sb.ToString()
        }
        [void]$sb.Append("`n")
        $pad = '  ' * ($Depth + 1)
        $pad0 = '  ' * $Depth
        for ($i = 0; $i -lt $items.Length; $i++) {
            [void]$sb.Append($pad)
            [void]$sb.Append((ConvertTo-Dw3JsonText $items[$i] ($Depth + 1)))
            if ($i -lt ($items.Length - 1)) { [void]$sb.Append(',') }
            [void]$sb.Append("`n")
        }
        [void]$sb.Append($pad0)
        [void]$sb.Append(']')
        return $sb.ToString()
    }
    if ($kind -eq 'object') {
        $keys = @($Node.V.Keys)
        $sb = New-Object System.Text.StringBuilder
        [void]$sb.Append('{')
        [void]$sb.Append("`n")
        $pad = '  ' * ($Depth + 1)
        $pad0 = '  ' * $Depth
        for ($i = 0; $i -lt $keys.Length; $i++) {
            $key = [string]$keys[$i]
            [void]$sb.Append($pad)
            [void]$sb.Append((ConvertTo-Dw3JsonString $key))
            [void]$sb.Append(': ')
            [void]$sb.Append((ConvertTo-Dw3JsonText $Node.V[$key] ($Depth + 1)))
            if ($i -lt ($keys.Length - 1)) { [void]$sb.Append(',') }
            [void]$sb.Append("`n")
        }
        [void]$sb.Append($pad0)
        [void]$sb.Append('}')
        return $sb.ToString()
    }
    throw 'user-secrets JSON is malformed'
}

function Read-UserSecretNode([string]$Path) {
    $empty = New-Object System.Collections.Specialized.OrderedDictionary
    $node = @{ T = 'object'; V = $empty }
    if (-not (Test-Path -LiteralPath $Path)) { return $node }
    $raw = [System.IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($raw)) { return $node }
    $parsed = Read-Dw3JsonDocument $raw
    if ([string]$parsed.T -ne 'object') { throw 'user-secrets JSON must be an object' }
    return $parsed
}

function Get-Dw3StringValue($Node, [string]$Key) {
    if ($null -eq $Node) { return $null }
    if ([string]$Node.T -ne 'object') { return $null }
    if (-not $Node.V.Contains($Key)) { return $null }
    $child = $Node.V[$Key]
    if ($null -eq $child) { return $null }
    if ([string]$child.T -ne 'string') { return $null }
    return [string]$child.V
}

function Set-Dw3StringValue($Node, [string]$Key, [string]$Value) {
    $entry = @{ T = 'string'; V = $Value }
    if ($Node.V.Contains($Key)) { $Node.V[$Key] = $entry }
    else { $Node.V.Add($Key, $entry) }
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

function Get-Dw3WindowsUser {
    $who = $env:USERNAME
    if (-not [string]::IsNullOrWhiteSpace($env:USERDOMAIN)) { $who = $env:USERDOMAIN + '\' + $env:USERNAME }
    return $who
}

function Set-OwnerOnlyFile([string]$Path, [string]$Secret) {
    if ($env:OS -eq 'Windows_NT') {
        Invoke-Dw3Native -File 'icacls' -ArgumentList @($Path, '/inheritance:r') -Secret $Secret
        $who = Get-Dw3WindowsUser
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

function Test-Dw3OwnerOnly([string]$Path) {
    if ($env:OS -eq 'Windows_NT') {
        $acl = Get-Acl -LiteralPath $Path
        if (-not $acl.AreAccessRulesProtected) { return $false }
        $rules = @($acl.Access)
        if ($rules.Count -ne 1) { return $false }
        $rule = $rules[0]
        if ($rule.IsInherited) { return $false }
        $who = Get-Dw3WindowsUser
        $id = [string]$rule.IdentityReference
        if (-not $id.Equals($who, [StringComparison]::OrdinalIgnoreCase)) { return $false }
        $rights = [int]$rule.FileSystemRights
        $full = [int][System.Security.AccessControl.FileSystemRights]::FullControl
        if (($rights -band $full) -ne $full) { return $false }
        return $true
    }
    $mode = Get-UnixMode $Path
    if ($null -eq $mode) { return $false }
    return ($mode -eq '600')
}

function Restore-SavedAcl([string]$Path, $Saved, [string]$Secret) {
    if ($null -eq $Saved) { return }
    if ($env:OS -eq 'Windows_NT') {
        Set-Acl -LiteralPath $Path -AclObject $Saved
        return
    }
    Invoke-Dw3Native -File 'chmod' -ArgumentList @([string]$Saved, $Path) -Secret $Secret
}

function Remove-Dw3Temp([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    if (-not $Path.EndsWith('.dw3part')) { return }
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    }
}

function Try-New-Dw3UnixModeEmpty([string]$Path) {
    $modeType = [Type]::GetType('System.IO.UnixFileMode, System.Private.CoreLib')
    $optionsType = [Type]::GetType('System.IO.FileStreamOptions, System.Private.CoreLib')
    if ($null -eq $modeType -or $null -eq $optionsType) { return $false }
    $prop = $optionsType.GetProperty('UnixCreateMode')
    if ($null -eq $prop) { return $false }
    $options = [Activator]::CreateInstance($optionsType)
    $options.Mode = [System.IO.FileMode]::CreateNew
    $options.Access = [System.IO.FileAccess]::Write
    $options.Share = [System.IO.FileShare]::None
    $userRead = [Enum]::Parse($modeType, 'UserRead')
    $userWrite = [Enum]::Parse($modeType, 'UserWrite')
    $combined = [Enum]::ToObject($modeType, ([int]$userRead -bor [int]$userWrite))
    $prop.SetValue($options, $combined)
    $stream = New-Object System.IO.FileStream($Path, $options)
    $stream.Dispose()
    return $true
}

function New-Dw3OwnerOnlyEmpty([string]$Path, [string]$Secret, [string]$Step) {
    if (-not [string]::IsNullOrWhiteSpace($Step)) { $script:dw3Step = $Step }
    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    $created = $false
    try {
        $made = $false
        if ($env:OS -ne 'Windows_NT') {
            try { $made = Try-New-Dw3UnixModeEmpty $Path }
            catch {
                $made = $false
                Remove-Dw3Temp $Path
            }
        }
        if (-not $made) {
            Remove-Dw3Temp $Path
            $stream = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            $stream.Dispose()
        }
        $created = $true
        Set-OwnerOnlyFile -Path $Path -Secret $Secret
        if (-not (Test-Dw3OwnerOnly $Path)) { throw 'owner-only permission was not applied' }
    }
    catch {
        if ($created -or (Test-Path -LiteralPath $Path)) { Remove-Dw3Temp $Path }
        throw
    }
}

function Write-Dw3ModeProbe([string]$Path) {
    $probe = $env:DW3_MODE_AT_WRITE
    if ([string]::IsNullOrWhiteSpace($probe)) { return }
    if ($probe -eq $Path) { return }
    $mode = 'unknown'
    if ($env:OS -eq 'Windows_NT') {
        if (Test-Dw3OwnerOnly $Path) { $mode = 'owner-only' }
        else { $mode = 'inherited' }
    }
    else {
        $got = Get-UnixMode $Path
        if (-not [string]::IsNullOrWhiteSpace($got)) { $mode = $got }
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::AppendAllText($probe, ('mode=' + $mode + "`n"), $utf8)
}

function Write-Dw3BytesFlush([string]$Path, [byte[]]$Bytes) {
    $stream = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        if ($null -ne $Bytes -and $Bytes.Length -gt 0) { $stream.Write($Bytes, 0, $Bytes.Length) }
        $stream.Flush($true)
    }
    finally { $stream.Dispose() }
}

function Write-Dw3TextFlush([string]$Path, [string]$Text) {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    if ($null -eq $Text) { $Text = '' }
    Write-Dw3BytesFlush -Path $Path -Bytes ($utf8.GetBytes($Text))
}

function New-Dw3StagedText([string]$Directory, [string]$Text, [string]$Secret, [string]$AclStep, [string]$WriteStep, [string]$FailStep) {
    if ([string]::IsNullOrWhiteSpace($Directory)) { throw 'stage directory is missing' }
    if (-not (Test-Path -LiteralPath $Directory)) {
        New-Item -ItemType Directory -Path $Directory | Out-Null
    }
    $temp = Join-Path $Directory ([guid]::NewGuid().ToString('n') + '.dw3part')
    [void]$script:dw3Temps.Add($temp)
    try {
        New-Dw3OwnerOnlyEmpty -Path $temp -Secret $Secret -Step $AclStep
        $script:dw3Step = $WriteStep
        if (-not [string]::IsNullOrWhiteSpace($FailStep) -and $env:DW3_FAIL_STEP -eq $FailStep) {
            throw 'simulated write failure'
        }
        Write-Dw3ModeProbe $temp
        Write-Dw3TextFlush -Path $temp -Text $Text
        return $temp
    }
    catch {
        Remove-Dw3Temp $temp
        throw
    }
}

function Move-Dw3TempOnto([string]$Temp, [string]$Target) {
    $existed = Test-Path -LiteralPath $Target
    if ($env:OS -eq 'Windows_NT') {
        if ($existed) {
            $method = [System.IO.File].GetMethod('Replace', [Type[]]@([string], [string], [string]))
            if ($null -eq $method) { throw 'user-secrets replace is unavailable' }
            $replaceArgs = New-Object object[] 3
            $replaceArgs[0] = $Temp
            $replaceArgs[1] = $Target
            $replaceArgs[2] = $null
            [void]$method.Invoke($null, $replaceArgs)
        }
        else { [System.IO.File]::Move($Temp, $Target) }
        return
    }
    if ($existed) { [System.IO.File]::Move($Temp, $Target, $true) }
    else { [System.IO.File]::Move($Temp, $Target) }
}

function Publish-Dw3Staged([string]$Temp, [string]$Target, [string]$Secret, [string]$Half, [bool]$OwnerOnly, $SavedAcl) {
    if ($OwnerOnly -and ($env:OS -eq 'Windows_NT') -and (Test-Path -LiteralPath $Target)) {
        Set-OwnerOnlyFile -Path $Target -Secret $Secret
        if (-not (Test-Dw3OwnerOnly $Target)) { throw 'owner-only permission was not applied' }
    }
    Move-Dw3TempOnto -Temp $Temp -Target $Target
    $script:dw3Changed[$Half] = $true
    if ($OwnerOnly) {
        Set-OwnerOnlyFile -Path $Target -Secret $Secret
        if (-not (Test-Dw3OwnerOnly $Target)) { throw 'owner-only permission was not applied' }
    }
    elseif ($null -ne $SavedAcl) {
        Restore-SavedAcl -Path $Target -Saved $SavedAcl -Secret $Secret
    }
    else {
        Set-OwnerOnlyFile -Path $Target -Secret $Secret
        if (-not (Test-Dw3OwnerOnly $Target)) { throw 'owner-only permission was not applied' }
    }
}

function Restore-Dw3Target([string]$Target, [byte[]]$OldBytes, [bool]$Existed, [string]$Secret, $SavedAcl, [bool]$OwnerOnly) {
    $kept = $script:dw3Step
    if (-not $Existed) {
        if (Test-Path -LiteralPath $Target) { Remove-Item -LiteralPath $Target -Force -ErrorAction SilentlyContinue }
        return
    }
    $dir = Split-Path -Parent $Target
    $temp = Join-Path $dir ([guid]::NewGuid().ToString('n') + '.dw3part')
    [void]$script:dw3Temps.Add($temp)
    try {
        New-Dw3OwnerOnlyEmpty -Path $temp -Secret $Secret -Step 'restore'
        if ($null -eq $OldBytes) { $OldBytes = New-Object byte[] 0 }
        Write-Dw3BytesFlush -Path $temp -Bytes $OldBytes
        Move-Dw3TempOnto -Temp $temp -Target $Target
        if ($OwnerOnly) { Set-OwnerOnlyFile -Path $Target -Secret $Secret }
        elseif ($null -ne $SavedAcl) { Restore-SavedAcl -Path $Target -Saved $SavedAcl -Secret $Secret }
        $script:dw3Step = $kept
    }
    catch {
        Remove-Dw3Temp $temp
        $script:dw3Step = 'restore'
        throw
    }
}

function Write-Dw3Changed {
    $any = $false
    if ($script:dw3Changed['secret-file']) {
        Write-Host 'changed: secret-file'
        $any = $true
    }
    if ($script:dw3Changed['user-secrets']) {
        Write-Host 'changed: user-secrets'
        $any = $true
    }
    if (-not $any) { Write-Host 'changed: none' }
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
    Refuse 'secrets-dir-owner' 'USERNAME does not match the secrets directory owner. A directory created from an elevated shell can be owned by BUILTIN\Administrators. Run this helper from a shell whose USERNAME owns the secrets directory.'
}

$apply = $false
if ($Execute -and -not $WhatIf) { $apply = $true }
if ($apply -and [string]::IsNullOrWhiteSpace($ProfileHome)) { Refuse 'profile-home-missing' 'Profile home is empty.' }

$lib = Join-Path $PSScriptRoot 'lib/windows-phone-home-env.ps1'
if (-not (Test-Path -LiteralPath $lib)) { Refuse 'phone-home-lib-missing' 'scripts/lib/windows-phone-home-env.ps1 is not beside this script.' }
. $lib

$example = Join-Path $PSScriptRoot 'phone-home.phase1.example.json'
try { $map = Read-UserSecretNode $UserSecretsPath }
catch { Refuse 'user-secrets-malformed' 'The user-secrets file could not be read as a JSON object.' }
try { $null = ConvertTo-Dw3JsonText $map 0 }
catch { Refuse 'user-secrets-malformed' 'The user-secrets file could not be read as a JSON object.' }

$secretKey = 'PhoneHomeRunner:Runners:desktop-win:SharedSecret'
$existingSecret = Get-Dw3StringValue $map $secretKey
$hasSecret = $false
if (-not [string]::IsNullOrWhiteSpace($existingSecret)) { $hasSecret = $true }
if ($apply -and $hasSecret -and -not $Rotate) {
    Refuse 'shared-secret-present' 'PhoneHomeRunner:Runners:desktop-win:SharedSecret is already set. Re-run with -Rotate to replace it. Nothing was changed.'
}
if ($apply -and (Test-Path -LiteralPath $SecretFile) -and -not $Rotate) {
    Refuse 'secret-file-present' 'The secret file already exists. Re-run with -Rotate to replace it. Nothing was changed.'
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

$secret = $null
$oldJsonBytes = $null
$oldJsonExists = $false
$oldJsonSaved = $null
$oldSecretBytes = $null
$oldSecretExists = $false
if (Test-Path -LiteralPath $UserSecretsPath) {
    $oldJsonExists = $true
    $oldJsonBytes = [System.IO.File]::ReadAllBytes($UserSecretsPath)
    if ($windows) { $oldJsonSaved = Get-Acl -LiteralPath $UserSecretsPath }
    else { $oldJsonSaved = Get-UnixMode $UserSecretsPath }
}
if (Test-Path -LiteralPath $SecretFile) {
    $oldSecretExists = $true
    $oldSecretBytes = [System.IO.File]::ReadAllBytes($SecretFile)
}
$script:dw3Step = 'prepare'
$script:dw3Changed = @{ 'secret-file' = $false; 'user-secrets' = $false }
$script:dw3Temps = New-Object System.Collections.Generic.List[string]
$phaseWrote = $false

try {
    $script:dw3Step = 'generate'
    $secret = New-Dw3Secret
    $real = Get-DesktopWinEntries -HomeDir $ProfileHome -Secret $secret
    foreach ($key in @($real.Keys)) { Set-Dw3StringValue $map $key ([string]$real[$key]) }
    $script:dw3Step = 'serialize'
    $jsonText = (ConvertTo-Dw3JsonText $map 0) + "`n"
    $secretText = $secret + "`n"

    $script:dw3Step = 'phase1-copy'
    if (-not (Test-Path -LiteralPath $Phase1ConfigPath)) {
        $phaseParent = Split-Path -Parent $Phase1ConfigPath
        if (-not [string]::IsNullOrWhiteSpace($phaseParent) -and -not (Test-Path -LiteralPath $phaseParent)) {
            New-Item -ItemType Directory -Path $phaseParent | Out-Null
        }
        [System.IO.File]::Copy($example, $Phase1ConfigPath, $false)
        $phaseWrote = $true
    }

    $storeDir = Split-Path -Parent $UserSecretsPath
    if ([string]::IsNullOrWhiteSpace($storeDir)) { throw 'user-secrets directory is missing' }
    $script:dw3Step = 'secret-acl'
    $secretTemp = New-Dw3StagedText -Directory $secretsDir -Text $secretText -Secret $secret -AclStep 'secret-acl' -WriteStep 'secret-write' -FailStep ''
    $script:dw3Step = 'user-secrets-acl'
    $jsonTemp = New-Dw3StagedText -Directory $storeDir -Text $jsonText -Secret $secret -AclStep 'user-secrets-acl' -WriteStep 'user-secrets-write' -FailStep 'user-secrets-write'

    $script:dw3Step = 'user-secrets-commit'
    Publish-Dw3Staged -Temp $jsonTemp -Target $UserSecretsPath -Secret $secret -Half 'user-secrets' -OwnerOnly $false -SavedAcl $oldJsonSaved
    $script:dw3Step = 'secret-file-commit'
    if ($env:DW3_FAIL_STEP -eq 'secret-file-commit') { throw 'simulated secret-file commit failure' }
    Publish-Dw3Staged -Temp $secretTemp -Target $SecretFile -Secret $secret -Half 'secret-file' -OwnerOnly $true -SavedAcl $null

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
    foreach ($temp in @($script:dw3Temps)) { Remove-Dw3Temp $temp }
    $script:dw3Temps.Clear()
    $secretChanged = [bool]$script:dw3Changed['secret-file']
    $jsonChanged = [bool]$script:dw3Changed['user-secrets']
    $changedCount = 0
    if ($secretChanged) { $changedCount++ }
    if ($jsonChanged) { $changedCount++ }
    if ($changedCount -eq 1) {
        try {
            if ($jsonChanged) {
                Restore-Dw3Target -Target $UserSecretsPath -OldBytes $oldJsonBytes -Existed $oldJsonExists -Secret $secret -SavedAcl $oldJsonSaved -OwnerOnly $false
                $script:dw3Changed['user-secrets'] = $false
            }
            if ($secretChanged) {
                Restore-Dw3Target -Target $SecretFile -OldBytes $oldSecretBytes -Existed $oldSecretExists -Secret $secret -SavedAcl $null -OwnerOnly $true
                $script:dw3Changed['secret-file'] = $false
            }
        }
        catch { $script:dw3Step = 'restore' }
    }
    Write-Host ('step=' + $script:dw3Step)
    Write-Dw3Changed
    Refuse 'failed' 'The helper stopped before finishing. No secret was written to the console.'
}
exit 0
