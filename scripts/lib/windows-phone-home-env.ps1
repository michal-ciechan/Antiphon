# CARD-1744 S4. Pure phone-home env and config rules for the Windows runner.
# No file IO and no process lookup. ASCII, Windows PowerShell 5.1.
# Dot-source only. Callers read the config file, test the secret path, and
# resolve pwsh plus the agent executables.

function ConvertTo-DriveAbsolute([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return '' }
    if ($Path -notmatch '^[A-Za-z]:[\\/]') {
        return [System.IO.Path]::GetFullPath($Path)
    }
    $slash = $Path.Replace('/', '\')
    $drive = $slash.Substring(0, 2)
    $tail = $slash.Substring(2)
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($segment in ($tail -split '\\')) {
        if ($segment -eq '' -or $segment -eq '.') { continue }
        if ($segment -eq '..') {
            if ($parts.Count -gt 0) { $parts.RemoveAt($parts.Count - 1) }
            continue
        }
        $parts.Add($segment)
    }
    if ($parts.Count -eq 0) { return $drive + '\' }
    return $drive + '\' + [string]::Join('\', $parts.ToArray())
}

function ConvertTo-PhoneHomeSlash([string]$Path) {
    if ($null -eq $Path) { return '' }
    return $Path.Replace('\', '/').TrimEnd('/')
}

function Join-PhoneHomeChild([string]$Left, [string]$Right) {
    $rightNorm = $Right
    if ($null -eq $rightNorm) { $rightNorm = '' }
    $rightNorm = $rightNorm.TrimStart('\', '/')
    if ([string]::IsNullOrWhiteSpace($Left)) { return $rightNorm }
    $leftTrim = $Left.TrimEnd('\', '/')
    if ($leftTrim -match '^[A-Za-z]:' -or $leftTrim.Contains('\')) {
        return $leftTrim + '\' + $rightNorm.Replace('/', '\')
    }
    return $leftTrim + '/' + $rightNorm.Replace('\', '/')
}

function New-WindowsPhoneHomeRunnerEnv {
    param(
        [string]$RunnerId,
        [string]$Root,
        [int]$Port,
        [int]$Capacity,
        [string]$ServerOrigin,
        [string]$ClaudeHome,
        [string]$CodexHome,
        [string]$GrokHome,
        [string]$PwshPath,
        $Executables
    )
    $absolute = ConvertTo-DriveAbsolute $Root
    $rootSlash = ConvertTo-PhoneHomeSlash $absolute
    $work = $rootSlash + '/work'
    $state = $rootSlash + '/state'
    $secret = Join-PhoneHomeChild $Root 'secrets\phone-home'
    $envs = [ordered]@{
        'ASPNETCORE_URLS'                      = 'http://localhost:' + [string]$Port
        'PhoneHome__Enabled'                   = 'true'
        'PhoneHome__RunnerId'                  = $RunnerId
        'PhoneHome__ServerOrigin'              = $ServerOrigin
        'PhoneHome__SecretPath'                = $secret
        'PhoneHome__StoreIdPath'               = $state + '/runner-store-id'
        'PhoneHome__CapacityStatePath'         = $state + '/runner-capacity'
        'PhoneHome__AllowedCwd'                = $work
        'PhoneHome__RunnerRepository'          = $work + '/repos/antiphon'
        'PhoneHome__Capacity'                  = [string]$Capacity
        'PhoneHome__ClaudeHome'                = $ClaudeHome
        'PhoneHome__CodexHome'                 = $CodexHome
        'PhoneHome__GrokHome'                  = $GrokHome
        'PhoneHome__RawExeAllowList__0'        = $PwshPath
        'SessionRunner__SessionLogPath'        = $state + '/session-runner'
        'SessionRunner__PtyHostDir'            = $state + '/pty-hosts'
        'SessionRunner__Herdr__Enabled'        = 'false'
        'SessionRunner__HostStats__Volumes__0' = $work
        'Serilog__LogPath'                     = $state + '/logs'
    }
    if ($null -ne $Executables) {
        foreach ($name in @($Executables.Keys)) {
            $envs['PhoneHome__Executables__' + [string]$name] = [string]$Executables[$name]
        }
    }
    return ,$envs
}

function New-PhoneHomeConfigResult([bool]$Ok, [string]$Reason, [string]$RunnerId, [string]$ServerOrigin, [int]$Capacity, [string]$Root, [int]$Port) {
    $row = @{
        ok           = $Ok
        reason       = $Reason
        runnerId     = $RunnerId
        serverOrigin = $ServerOrigin
        capacity     = $Capacity
        root         = $Root
        port         = $Port
    }
    return ,$row
}

function Test-JsonIntegral($Value) {
    if ($null -eq $Value) { return $false }
    $name = $Value.GetType().FullName
    return $name -eq 'System.Byte' -or $name -eq 'System.Int16' -or $name -eq 'System.Int32' -or $name -eq 'System.Int64' -or $name -eq 'System.UInt16' -or $name -eq 'System.UInt32' -or $name -eq 'System.UInt64'
}

function Get-PhoneHomeRootRefusal([string]$Root) {
    if ([string]::IsNullOrWhiteSpace($Root)) { return 'root-not-drive-rooted' }
    $text = $Root.Trim()
    if ($text.StartsWith('\\?\') -or $text.StartsWith('\\.\') -or $text.StartsWith('//?/') -or $text.StartsWith('//./')) {
        return 'root-device'
    }
    if ($text.StartsWith('\\') -or $text.StartsWith('//')) { return 'root-unc' }
    if ($text -notmatch '^[A-Za-z]:[\\/]') { return 'root-not-drive-rooted' }
    return $null
}

function ConvertFrom-WindowsPhoneHomeConfig {
    param(
        [string]$Text,
        [int]$RequirePort = 0
    )
    if ($null -eq $Text) {
        return ,(New-PhoneHomeConfigResult $false 'config-malformed' $null $null 0 $null 0)
    }
    foreach ($ch in $Text.ToCharArray()) {
        if ([int]$ch -gt 127) {
            return ,(New-PhoneHomeConfigResult $false 'config-malformed' $null $null 0 $null 0)
        }
    }
    $parsed = $null
    try {
        $parsed = $Text | ConvertFrom-Json
    } catch {
        return ,(New-PhoneHomeConfigResult $false 'config-malformed' $null $null 0 $null 0)
    }
    if ($null -eq $parsed -or $parsed -isnot [System.Management.Automation.PSCustomObject]) {
        return ,(New-PhoneHomeConfigResult $false 'config-malformed' $null $null 0 $null 0)
    }
    $allowed = @('runnerId', 'serverOrigin', 'capacity', 'root', 'port')
    $present = @{}
    foreach ($property in $parsed.PSObject.Properties) {
        $known = $false
        foreach ($name in $allowed) {
            if ($property.Name -ceq $name) { $known = $true }
        }
        if (-not $known) {
            return ,(New-PhoneHomeConfigResult $false ('unknown-key:' + $property.Name) $null $null 0 $null 0)
        }
        $present[$property.Name] = $property.Value
    }
    foreach ($name in @('runnerId', 'serverOrigin', 'capacity')) {
        if (-not $present.ContainsKey($name)) {
            return ,(New-PhoneHomeConfigResult $false ('missing-key:' + $name) $null $null 0 $null 0)
        }
    }

    $runnerId = [string]$present['runnerId']
    if ($runnerId.Equals('local', [StringComparison]::OrdinalIgnoreCase) -or $runnerId.Equals('desktop', [StringComparison]::OrdinalIgnoreCase)) {
        return ,(New-PhoneHomeConfigResult $false 'runner-id-reserved' $null $null 0 $null 0)
    }
    if ($runnerId -cnotmatch '^[a-z0-9][a-z0-9-]{0,63}$') {
        return ,(New-PhoneHomeConfigResult $false 'runner-id-invalid' $null $null 0 $null 0)
    }

    $originText = $present['serverOrigin']
    if ($originText -isnot [string]) {
        return ,(New-PhoneHomeConfigResult $false 'origin-invalid' $null $null 0 $null 0)
    }
    $uri = $null
    if (-not [Uri]::TryCreate([string]$originText, [UriKind]::Absolute, [ref]$uri)) {
        return ,(New-PhoneHomeConfigResult $false 'origin-invalid' $null $null 0 $null 0)
    }
    if ($uri.Scheme -cne 'https') {
        return ,(New-PhoneHomeConfigResult $false 'origin-not-https' $null $null 0 $null 0)
    }
    if (-not [string]::IsNullOrEmpty($uri.UserInfo)) {
        return ,(New-PhoneHomeConfigResult $false 'origin-userinfo' $null $null 0 $null 0)
    }
    if (-not [string]::IsNullOrEmpty($uri.Query)) {
        return ,(New-PhoneHomeConfigResult $false 'origin-query' $null $null 0 $null 0)
    }
    if (-not [string]::IsNullOrEmpty($uri.Fragment)) {
        return ,(New-PhoneHomeConfigResult $false 'origin-fragment' $null $null 0 $null 0)
    }
    if ($uri.AbsolutePath -ne '/' -and $uri.AbsolutePath -ne '') {
        return ,(New-PhoneHomeConfigResult $false 'origin-path' $null $null 0 $null 0)
    }

    if (-not (Test-JsonIntegral $present['capacity'])) {
        return ,(New-PhoneHomeConfigResult $false 'capacity-invalid' $null $null 0 $null 0)
    }
    $capacity = [int64]$present['capacity']
    if ($capacity -ne 1 -and $capacity -ne 2) {
        return ,(New-PhoneHomeConfigResult $false 'capacity-out-of-range' $null $null 0 $null 0)
    }

    $root = 'C:\Antiphon\runner'
    if ($present.ContainsKey('root')) {
        if ($present['root'] -isnot [string]) {
            return ,(New-PhoneHomeConfigResult $false 'root-not-drive-rooted' $null $null 0 $null 0)
        }
        $rootRefusal = Get-PhoneHomeRootRefusal ([string]$present['root'])
        if ($null -ne $rootRefusal) {
            return ,(New-PhoneHomeConfigResult $false $rootRefusal $null $null 0 $null 0)
        }
        $root = [string]$present['root']
    }

    $port = 17204
    if ($present.ContainsKey('port')) {
        if (-not (Test-JsonIntegral $present['port'])) {
            return ,(New-PhoneHomeConfigResult $false 'port-invalid' $null $null 0 $null 0)
        }
        $portValue = [int64]$present['port']
        if ($portValue -ne 17204 -and $portValue -ne 17214) {
            return ,(New-PhoneHomeConfigResult $false 'port-invalid' $null $null 0 $null 0)
        }
        $port = [int]$portValue
    }
    if ($RequirePort -ne 0 -and $port -ne $RequirePort) {
        return ,(New-PhoneHomeConfigResult $false 'port-not-broker' $null $null 0 $null 0)
    }

    return ,(New-PhoneHomeConfigResult $true $null $runnerId ([string]$originText) ([int]$capacity) $root $port)
}

function New-AutostartPlanResult([string]$Mode, [string]$Reason, $Env, [string]$ExeArgs) {
    if ($null -eq $Env) { $Env = @{} }
    $row = @{
        mode    = $Mode
        reason  = $Reason
        env     = $Env
        exeArgs = $ExeArgs
    }
    return ,$row
}

function Resolve-SessionRunnerAutostartPlan {
    param(
        $ConfigText,
        [bool]$ExplicitConfigPath,
        [bool]$SecretPresent,
        [string]$HomeDir,
        [string]$PwshPath,
        $Executables,
        [string]$RepoRoot
    )
    if ($null -eq $ConfigText) {
        return ,(New-AutostartPlanResult 'direct' $null @{} '--urls http://localhost:17204')
    }
    $require = 0
    if (-not $ExplicitConfigPath) { $require = 17204 }
    $parsed = ConvertFrom-WindowsPhoneHomeConfig -Text ([string]$ConfigText) -RequirePort $require
    if (-not $parsed.ok) {
        return ,(New-AutostartPlanResult 'refused' ([string]$parsed.reason) @{} $null)
    }
    if (-not $SecretPresent) {
        return ,(New-AutostartPlanResult 'refused' 'secret-missing' @{} $null)
    }
    if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
        return ,(New-AutostartPlanResult 'refused' 'repo-root-missing' @{} $null)
    }
    $built = New-WindowsPhoneHomeRunnerEnv `
        -RunnerId ([string]$parsed.runnerId) `
        -Root ([string]$parsed.root) `
        -Port ([int]$parsed.port) `
        -Capacity ([int]$parsed.capacity) `
        -ServerOrigin ([string]$parsed.serverOrigin) `
        -ClaudeHome (Join-PhoneHomeChild $HomeDir '.claude') `
        -CodexHome (Join-PhoneHomeChild $HomeDir '.codex') `
        -GrokHome (Join-PhoneHomeChild $HomeDir '.grok') `
        -PwshPath $PwshPath `
        -Executables $Executables
    $exeArgs = '--urls http://localhost:' + [string]$parsed.port
    return ,(New-AutostartPlanResult 'phone-home' $null $built $exeArgs)
}

function Get-WindowsPhoneHomeLaunchRefusal {
    param(
        [bool]$ConfigPresent,
        [string]$ConfigPath
    )
    if (-not $ConfigPresent) { return $null }
    if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        $ConfigPath = 'C:\Antiphon\runner\phone-home.json'
    }
    return ('REFUSED: phone-home-config-present' + [Environment]::NewLine + 'If the Linux server is serving, this refusal is correct: do NOT delete the file. Only during ROLLBACK, after the Linux server is stopped and pg_stat_activity is clear: delete ' + $ConfigPath + ', run restart-session-runner.ps1 -Hard, then retry.')
}
