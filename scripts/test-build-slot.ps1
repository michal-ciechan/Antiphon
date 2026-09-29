#requires -Version 5.1
# CARD-0589 V-5 harness for scripts/build-slot.ps1 and scripts/lib/build-slot.ps1. Offline: the slot
# shim (scripts/fixtures/c589-slot-shim.ps1) stands in for the runner's /build-slots and the command
# shim (scripts/fixtures/c589-command-shim.ps1) for the wrapped build/test driver. Both append to one
# calls.log, so POST -> CMD -> DELETE order is checked on one timeline. No runner is contacted.
# ASCII-only.
param(
    [string]$Case = '',
    [string]$ResultsDirectory = ''
)
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$lib = Join-Path $here 'lib'
. (Join-Path $lib 'nightly-common.ps1')
. (Join-Path $lib 'c487-harness.ps1')

$ResultsDirectory = New-C487Root -ResultsDirectory $ResultsDirectory
$script:RepoRoot = Split-Path -Parent $here
$script:Wrapper = Join-Path $here 'build-slot.ps1'
$script:Lib = Join-Path $lib 'build-slot.ps1'
$script:SlotShim = Join-Path $here (Join-Path 'fixtures' 'c589-slot-shim.ps1')
$script:CommandShim = Join-Path $here (Join-Path 'fixtures' 'c589-command-shim.ps1')
$script:Lease = 'c5890000-0000-4000-8000-000000000001'
$script:SeamNames = @('ANTIPHON_BUILD_SLOTS_URL', 'C589_SLOT_SHIM', 'C589_SLOT_LOG', 'C589_SLOT_SCRIPT',
    'C589_SLOT_WAIT_SECONDS', 'C589_SLOT_GRACE_SECONDS', 'C589_SLOT_RETRY_MS', 'C589_COMMAND_SHIM', 'C589_COMMAND_EXIT',
    'C589_COMMAND_SLEEP_SECONDS')

function New-C589Case {
    param([string]$Name)
    $root = Join-Path $ResultsDirectory ($Name + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    return [pscustomobject]@{ Root = $root; Log = (Join-Path $root 'calls.log') }
}

function Set-C589Seams {
    param($Fx, [string]$SlotScript, [string]$WaitSeconds, [string]$GraceSeconds, [string]$RetryMs, [int]$CommandExit)
    # A dead endpoint as well as the shim: nothing here may reach a real runner.
    $env:ANTIPHON_BUILD_SLOTS_URL = 'http://127.0.0.1:1/build-slots'
    $env:C589_SLOT_SHIM = $script:SlotShim
    $env:C589_SLOT_LOG = $Fx.Log
    $env:C589_SLOT_SCRIPT = $SlotScript
    $env:C589_SLOT_WAIT_SECONDS = $WaitSeconds
    $env:C589_SLOT_GRACE_SECONDS = $GraceSeconds
    $env:C589_SLOT_RETRY_MS = $RetryMs
    $env:C589_COMMAND_SHIM = $script:CommandShim
    $env:C589_COMMAND_EXIT = [string]$CommandExit
}

function Clear-C589Seams {
    foreach ($name in $script:SeamNames) { Remove-Item -LiteralPath ('Env:' + $name) -ErrorAction SilentlyContinue }
}

function Invoke-C589Wrapper {
    param(
        $Fx,
        [string[]]$WrapperArgs,
        [string]$SlotScript = 'granted',
        [string]$WaitSeconds = '',
        [string]$GraceSeconds = '',
        [string]$RetryMs = '10',
        [int]$CommandExit = 0,
        [string]$WorkingDirectory = $script:RepoRoot,
        [int]$DeadlineSeconds = 120,
        [switch]$NoWait,
        [switch]$DisableCommandShim
    )
    Set-C589Seams -Fx $Fx -SlotScript $SlotScript -WaitSeconds $WaitSeconds -GraceSeconds $GraceSeconds -RetryMs $RetryMs -CommandExit $CommandExit
    if ($DisableCommandShim) { Remove-Item Env:C589_COMMAND_SHIM -ErrorAction SilentlyContinue }
    try {
        $psi = [Diagnostics.ProcessStartInfo]::new()
        $pwshPath = (Get-Command pwsh).Source
        $psi.FileName = $pwshPath
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.WorkingDirectory = $WorkingDirectory
        $launchArgs = @('-NoProfile', '-NonInteractive', '-File', $script:Wrapper) + @($WrapperArgs)
        if ($NoWait -and $IsLinux) {
            # The TUnit host ignores SIGINT, and its children inherit that disposition.
            # Perl resets it before exec, keeping the wrapper pid and literal argv.
            $psi.FileName = (Get-Command perl).Source
            $launchArgs = @('-e', '$SIG{INT} = "DEFAULT"; exec @ARGV', '--', $pwshPath) + $launchArgs
        }
        foreach ($token in $launchArgs) {
            [void]$psi.ArgumentList.Add([string]$token)
        }
        $proc = [Diagnostics.Process]::Start($psi)
        $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
        $stderrTask = $proc.StandardError.ReadToEndAsync()
    } finally {
        Clear-C589Seams
    }
    if ($NoWait) { return [pscustomobject]@{ Process = $proc; StdoutTask = $stdoutTask; StderrTask = $stderrTask; Fx = $Fx } }
    return Wait-C589Wrapper -Running ([pscustomobject]@{ Process = $proc; StdoutTask = $stdoutTask; StderrTask = $stderrTask; Fx = $Fx }) -DeadlineSeconds $DeadlineSeconds
}

function Wait-C589Wrapper {
    param($Running, [int]$DeadlineSeconds = 120)
    $proc = $Running.Process
    $timedOut = -not $proc.WaitForExit($DeadlineSeconds * 1000)
    if ($timedOut) {
        try { $proc.Kill($true) } catch { }
        [void]$proc.WaitForExit(5000)
    }
    $merged = [string]$Running.StdoutTask.Result
    $errText = [string]$Running.StderrTask.Result
    if ($errText) {
        if ($merged -and -not $merged.EndsWith("`n")) { $merged += "`n" }
        $merged += $errText
    }
    $lines = @()
    if ($merged) {
        $merged = $merged -replace "`r`n", "`n" -replace "`r", "`n"
        if ($merged.EndsWith("`n")) { $merged = $merged.Substring(0, $merged.Length - 1) }
        $lines = @($merged -split "`n")
    }
    $calls = @()
    if (Test-Path -LiteralPath $Running.Fx.Log) { $calls = @(Get-Content -LiteralPath $Running.Fx.Log) }
    $code = $(if ($timedOut) { -1 } else { $proc.ExitCode })
    $proc.Dispose()
    return [pscustomobject]@{ Exit = $code; TimedOut = $timedOut; Lines = $lines; Text = ($lines -join "`n"); Calls = $calls }
}

function Get-C589Order {
    param($Result)
    return @($Result.Calls | ForEach-Object {
        if ($_ -like 'SLOT POST *') { 'POST' } elseif ($_ -like 'SLOT DELETE *') { 'DELETE' } elseif ($_ -like 'CMD *') { 'CMD' }
    }) -join ','
}

function Get-C589Cmd {
    param($Result)
    $cmd = @($Result.Calls | Where-Object { $_ -like 'CMD *' })
    if ($cmd.Count -ne 1) { return ('<{0} commands>' -f $cmd.Count) }
    return [string]$cmd[0]
}

function Get-C589Line {
    param($Result, [string]$Pattern)
    return @($Result.Lines | Where-Object { $_ -cmatch $Pattern }).Count
}

function Get-C589LoggedValue {
    param($Result, [string]$Prefix)
    $lines = @($Result.Calls | Where-Object { $_ -clike ($Prefix + ' *') })
    if ($lines.Count -ne 1) { return ('<{0} {1} lines>' -f $lines.Count, $Prefix) }
    return ([string]$lines[0]).Substring($Prefix.Length + 1)
}

function Test-C800_WrapperPassesWildcardArgvLiterally {
    $fx = New-C589Case -Name 'c800-argv'
    New-Item -ItemType Directory -Path (Join-Path $fx.Root 'a/b') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fx.Root 'a/b/Unit') -Value ''
    Set-Content -LiteralPath (Join-Path $fx.Root 'run.trx') -Value ''
    Set-Content -LiteralPath (Join-Path $fx.Root 'old.trx') -Value ''
    $argv = @('dotnet', 'run', '--project', 'tests/X', '--no-build', '--', '--treenode-filter', '*/*/*[Category=Unit]', '--report-trx-filename', '*.trx')
    $r = Invoke-C589Wrapper -Fx $fx -WorkingDirectory $fx.Root -CommandExit 3 -WrapperArgs (@('-Label', 'c800-argv', '--') + $argv)
    Assert-C487 -Cond ($r.Exit -eq 3) -Name 'C800 WrapperPassesWildcardArgvLiterally propagates exit 3' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    Assert-C487 -Cond ((Get-C589Order -Result $r) -ceq 'POST,CMD,DELETE') -Name 'C800 WrapperPassesWildcardArgvLiterally runs the command between grant and release' -Detail ($r.Calls -join ' | ')
    Assert-C487 -Cond ((Get-C589LoggedValue -Result $r -Prefix 'ARGV') -ceq (ConvertTo-Json -InputObject $argv -Compress)) -Name 'C800 WrapperPassesWildcardArgvLiterally every token arrives literally' -Detail (Get-C589LoggedValue -Result $r -Prefix 'ARGV')
    Assert-C487 -Cond ((Get-C589LoggedValue -Result $r -Prefix 'CWD') -ceq $fx.Root) -Name "C800 WrapperPassesWildcardArgvLiterally the child runs in the caller's directory" -Detail (Get-C589LoggedValue -Result $r -Prefix 'CWD')

    $ip = New-C589Case -Name 'c800-inproc'
    $inproc = Join-Path $ip.Root 'inproc'
    New-Item -ItemType Directory -Path $inproc | Out-Null
    Set-Content -LiteralPath (Join-Path $inproc 'run.trx') -Value ''
    Set-C589Seams -Fx $ip -SlotScript 'granted' -WaitSeconds '' -GraceSeconds '' -RetryMs '10' -CommandExit 0
    Push-Location -LiteralPath $inproc
    try { & $script:Wrapper -Label c800-inproc -- dotnet build tests/X '*.trx' | Out-Null }
    finally { Pop-Location; Clear-C589Seams }
    $ipResult = [pscustomobject]@{ Calls = @(Get-Content -LiteralPath $ip.Log) }
    $ipArgv = @('dotnet', 'build', 'tests/X', '*.trx', '-maxcpucount:3')
    Assert-C487 -Cond ((Get-C589LoggedValue -Result $ipResult -Prefix 'ARGV') -ceq (ConvertTo-Json -InputObject $ipArgv -Compress)) -Name 'C800 WrapperPassesWildcardArgvLiterally an in-process call passes the token literally' -Detail (Get-C589LoggedValue -Result $ipResult -Prefix 'ARGV')
    Assert-C487 -Cond ((Get-C589LoggedValue -Result $ipResult -Prefix 'CWD') -ceq $inproc) -Name 'C800 WrapperPassesWildcardArgvLiterally an in-process call runs the child at the pushed location' -Detail (Get-C589LoggedValue -Result $ipResult -Prefix 'CWD')
}

function Test-C800_WrapperStartsUnitFilterWithinDeadline {
    $fx = New-C589Case -Name 'c800-unit'
    $filter = '/*/*/*/*[Category=Unit]'
    $argv = @('-Label', 'c800-unit', '--', 'dotnet', 'run', '--project', 'tests/Antiphon.Tests', '--no-build', '--property:OutputPath=bin-c800/', '--', '--treenode-filter', $filter, '--report-trx', '--report-trx-filename', 'run.trx', '--results-directory', $fx.Root)
    $r = Invoke-C589Wrapper -Fx $fx -DeadlineSeconds 30 -WrapperArgs $argv
    Assert-C487 -Cond (-not $r.TimedOut -and $r.Exit -eq 0) -Name 'C800 WrapperStartsUnitFilterWithinDeadline exits within 30 s' -Detail ('exit={0} timedOut={1} calls={2}' -f $r.Exit, $r.TimedOut, ($r.Calls -join ' | '))
    $logged = Get-C589LoggedValue -Result $r -Prefix 'ARGV'
    $literal = $false
    try { $literal = @($logged | ConvertFrom-Json) -ccontains $filter } catch { }
    Assert-C487 -Cond $literal -Name 'C800 WrapperStartsUnitFilterWithinDeadline passes the Unit filter literally' -Detail $logged
    Assert-C487 -Cond ((Get-C589Order -Result $r) -ceq 'POST,CMD,DELETE') -Name 'C800 WrapperStartsUnitFilterWithinDeadline releases the lease' -Detail ($r.Calls -join ' | ')
}

function Test-C800_WrapperKeepsScriptCommandsInProcess {
    $fx = New-C589Case -Name 'c800-script'
    Set-Content -LiteralPath (Join-Path $fx.Root 'run.trx') -Value ''
    $r = Invoke-C589Wrapper -Fx $fx -WorkingDirectory $fx.Root -CommandExit 6 -DisableCommandShim -WrapperArgs @('-NoSlot', '--', $script:CommandShim, '--treenode-filter', '*.trx')
    Assert-C487 -Cond ($r.Exit -eq 6) -Name 'C800 WrapperKeepsScriptCommandsInProcess propagates exit 6 from a script command' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    Assert-C487 -Cond ((Get-C589LoggedValue -Result $r -Prefix 'ARGV') -ceq '["--treenode-filter","*.trx"]') -Name 'C800 WrapperKeepsScriptCommandsInProcess a script command receives its tokens unchanged' -Detail (Get-C589LoggedValue -Result $r -Prefix 'ARGV')
}

function Test-C800_WrapperInterruptKillsChildAndReleasesLease {
    if (-not $IsLinux) { Write-Host 'SKIP C800 WrapperInterruptKillsChildAndReleasesLease (Linux only)'; return }
    $fx = New-C589Case -Name 'c800-interrupt'
    $env:C589_COMMAND_SLEEP_SECONDS = '20'
    $running = $null
    try {
        $running = Invoke-C589Wrapper -Fx $fx -NoWait -WrapperArgs @('-Label', 'c800-interrupt', '--', 'dotnet', 'build', 'tests/X')
        $childPid = 0
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while ($clock.Elapsed.TotalSeconds -lt 10) {
            if (Test-Path -LiteralPath $fx.Log) {
                $pidLines = @(Get-Content -LiteralPath $fx.Log | Where-Object { $_ -cmatch '^PID [0-9]+$' })
                if ($pidLines.Count -gt 0) { $childPid = [int](($pidLines[0] -split ' ')[1]); break }
            }
            Start-Sleep -Milliseconds 100
        }
        if ($childPid -gt 0) { & /bin/sh -c 'kill -INT "$1"' _ $running.Process.Id }
        $quickExit = $running.Process.WaitForExit(5000)
        $gone = $false
        if ($childPid -gt 0) {
            $clock.Restart()
            while ($clock.Elapsed.TotalSeconds -lt 5) {
                if (-not (Test-Path -LiteralPath ('/proc/' + $childPid))) { $gone = $true; break }
                Start-Sleep -Milliseconds 100
            }
        }
        $calls = @()
        if (Test-Path -LiteralPath $fx.Log) { $calls = @(Get-Content -LiteralPath $fx.Log) }
        $result = [pscustomobject]@{ Calls = $calls }
        Assert-C487 -Cond $quickExit -Name 'C800 WrapperInterruptKillsChildAndReleasesLease the wrapper exits within 5 s of SIGINT' -Detail ('pid={0} child={1}' -f $running.Process.Id, $childPid)
        Assert-C487 -Cond $gone -Name 'C800 WrapperInterruptKillsChildAndReleasesLease the child is gone within 5 s' -Detail ('child={0} exists={1}' -f $childPid, (Test-Path -LiteralPath ('/proc/' + $childPid)))
        Assert-C487 -Cond ((Get-C589Order -Result $result) -ceq 'POST,CMD,DELETE') -Name 'C800 WrapperInterruptKillsChildAndReleasesLease the lease is released after the command' -Detail ($calls -join ' | ')
    } finally {
        if ($running) {
            if (-not $running.Process.HasExited) { try { $running.Process.Kill($true) } catch { } }
            [void]$running.Process.WaitForExit(5000)
            $running.Process.Dispose()
        }
        Remove-Item Env:C589_COMMAND_SLEEP_SECONDS -ErrorAction SilentlyContinue
    }
}

function Test-C589_WrapperRunsUnderLease {
    $fx = New-C589Case -Name 'wrapper-lease'
    $r = Invoke-C589Wrapper -Fx $fx -CommandExit 7 -WrapperArgs @('-Label', 'mutation-shard-1', '--', 'dotnet', 'build', 'tests/X', '--nologo')
    Assert-C487 -Cond ($r.Exit -eq 7) -Name 'C589 WrapperRunsUnderLease propagates the command exit code' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    Assert-C487 -Cond ((Get-C589Order -Result $r) -ceq 'POST,CMD,DELETE' -and @($r.Calls | Where-Object { $_ -ceq ('SLOT DELETE ' + $script:Lease) }).Count -eq 1) `
        -Name 'C589 WrapperRunsUnderLease runs the command between grant and release' -Detail ($r.Calls -join ' | ')
    Assert-C487 -Cond ((Get-C589Cmd -Result $r) -ceq 'CMD dotnet build tests/X --nologo -maxcpucount 3') `
        -Name 'C589 WrapperRunsUnderLease applies the grant -maxcpucount to dotnet build' -Detail (Get-C589Cmd -Result $r)
    Assert-C487 -Cond ((Get-C589Line -Result $r -Pattern ('^BUILD SLOT granted lease=' + $script:Lease + ' waited=\d+s maxcpucount=3$')) -eq 1 -and (Get-C589Line -Result $r -Pattern ('^BUILD SLOT released lease=' + $script:Lease + ' held=\d+s$')) -eq 1) `
        -Name 'C589 WrapperRunsUnderLease prints the granted and released lines' -Detail $r.Text
    Assert-C487 -Cond (@($r.Calls | Where-Object { $_ -clike 'SLOT POST label=mutation-shard-1 pid=*' }).Count -eq 1) `
        -Name 'C589 WrapperRunsUnderLease asks under its -Label' -Detail ($r.Calls -join ' | ')

    $ns = New-C589Case -Name 'wrapper-noslot'
    $n = Invoke-C589Wrapper -Fx $ns -WrapperArgs @('-Label', 'operator', '-NoSlot', '--', 'dotnet', 'build', 'tests/X')
    Assert-C487 -Cond ($n.Exit -eq 0 -and (Get-C589Order -Result $n) -ceq 'CMD' -and (Get-C589Cmd -Result $n) -ceq 'CMD dotnet build tests/X' -and (Get-C589Line -Result $n -Pattern '^BUILD SLOT skipped by -NoSlot$') -eq 1) `
        -Name 'C589 WrapperRunsUnderLease -NoSlot runs the command unchanged without asking' -Detail ('exit={0} calls={1} {2}' -f $n.Exit, ($n.Calls -join ' | '), $n.Text)

    # Dot-invoked from a PowerShell session the wrapper reads its own arguments; the exit code still arrives.
    $ip = New-C589Case -Name 'wrapper-inprocess'
    Set-C589Seams -Fx $ip -SlotScript 'granted' -WaitSeconds '' -GraceSeconds '' -RetryMs '10' -CommandExit 5
    try {
        & $script:Wrapper -Label 'in-process' -- dotnet test tests/X | Out-Null
        $ipExit = $LASTEXITCODE
    } finally { Clear-C589Seams }
    $ipCalls = @(Get-Content -LiteralPath $ip.Log)
    Assert-C487 -Cond ($ipExit -eq 5 -and ($ipCalls -join ',') -cmatch '^SLOT POST label=in-process .*,CMD dotnet test tests/X -maxcpucount 3,SLOT DELETE ') `
        -Name 'C589 WrapperRunsUnderLease an in-process call leases, runs and returns the exit code' -Detail ('exit={0} calls={1}' -f $ipExit, ($ipCalls -join ' | '))
}

function Test-C589_WrapperMaxCpuCountRules {
    . $script:Lib
    $cases = @(
        @{ In = @('dotnet', 'build', 'x'); Out = 'dotnet build x -maxcpucount:3'; Row = 'dotnet build gets the count' },
        @{ In = @('dotnet', 'test', 'x', '--', '--treenode-filter', '/*/*/A/*'); Out = 'dotnet test x -maxcpucount:3 -- --treenode-filter /*/*/A/*'; Row = 'dotnet test gets it before --' },
        @{ In = @('/usr/share/dotnet/dotnet', 'publish', 'x'); Out = '/usr/share/dotnet/dotnet publish x -maxcpucount:3'; Row = 'a dotnet path gets the count' },
        @{ In = @('C:\Program Files\dotnet\dotnet.exe', 'build'); Out = 'C:\Program Files\dotnet\dotnet.exe build -maxcpucount:3'; Row = 'dotnet.exe gets the count' },
        @{ In = @('dotnet', 'run', '--project', 'tests/X'); Out = 'dotnet run --project tests/X'; Row = 'dotnet run is left alone because it forwards the switch to the program' },
        @{ In = @('dotnet', 'run', '--project', 'tests/X', '--no-build', '--', '--x'); Out = 'dotnet run --project tests/X --no-build -- --x'; Row = 'dotnet run --no-build is left alone' },
        @{ In = @('dotnet', 'build', '-m:2', 'x'); Out = 'dotnet build -m:2 x'; Row = 'an explicit -m:2 wins' },
        @{ In = @('dotnet', 'build', 'x', '-maxCpuCount'); Out = 'dotnet build x -maxCpuCount'; Row = 'an explicit bare -maxcpucount wins' },
        @{ In = @('dotnet', 'build', '/m', 'x'); Out = 'dotnet build /m x'; Row = 'an explicit /m wins' },
        @{ In = @('dotnet', 'test', 'x', '--', '-m:9'); Out = 'dotnet test x -maxcpucount:3 -- -m:9'; Row = 'a -m after -- belongs to the program' },
        @{ In = @('pwsh', '-File', 'x.ps1'); Out = 'pwsh -File x.ps1'; Row = 'a non-dotnet command is left alone' }
    )
    foreach ($case in $cases) {
        $out = @(Add-AntiphonMaxCpuCount -Command $case.In -MaxCpuCount 3) -join ' '
        Assert-C487 -Cond ($out -ceq $case.Out) -Name ('C589 WrapperMaxCpuCountRules {0}' -f $case.Row) -Detail $out
    }

    $fx = New-C589Case -Name 'wrapper-run'
    $r = Invoke-C589Wrapper -Fx $fx -WrapperArgs @('-Label', 'run', '--', 'dotnet', 'run', '--project', 'tests/X', '--', '--treenode-filter', '/*/*/A/*')
    Assert-C487 -Cond ($r.Exit -eq 0 -and (Get-C589Cmd -Result $r) -ceq 'CMD dotnet run --project tests/X -- --treenode-filter /*/*/A/*' -and (Get-C589Line -Result $r -Pattern '^BUILD SLOT note: dotnet run ') -eq 1) `
        -Name 'C589 WrapperMaxCpuCountRules a wrapped dotnet run is leased, unchanged and says why' -Detail ('exit={0} cmd={1} {2}' -f $r.Exit, (Get-C589Cmd -Result $r), $r.Text)
}

function Test-C589_WrapperReleasesOnFailure {
    $fx = New-C589Case -Name 'wrapper-fails'
    $r = Invoke-C589Wrapper -Fx $fx -CommandExit 1 -WrapperArgs @('-Label', 'red', '--', 'dotnet', 'build', 'tests/X')
    Assert-C487 -Cond ($r.Exit -eq 1) -Name 'C589 WrapperReleasesOnFailure exit code 1' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    Assert-C487 -Cond ((Get-C589Order -Result $r) -ceq 'POST,CMD,DELETE' -and (Get-C589Line -Result $r -Pattern '^BUILD SLOT released lease=') -eq 1) `
        -Name 'C589 WrapperReleasesOnFailure releases the lease after a failing command' -Detail ($r.Calls -join ' | ')
}

function Test-C589_WrapperTimeout {
    $fx = New-C589Case -Name 'wrapper-timeout'
    $r = Invoke-C589Wrapper -Fx $fx -SlotScript 'busy' -WaitSeconds '1' -RetryMs '50' -WrapperArgs @('-Label', 'late', '--', 'dotnet', 'build', 'tests/X')
    Assert-C487 -Cond ($r.Exit -eq 4) -Name 'C589 WrapperTimeout exit code 4' -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
    Assert-C487 -Cond (@($r.Calls | Where-Object { $_ -like 'CMD *' -or $_ -like 'SLOT DELETE *' }).Count -eq 0 -and @($r.Calls | Where-Object { $_ -like 'SLOT POST *' }).Count -ge 2) `
        -Name 'C589 WrapperTimeout runs nothing and releases nothing' -Detail ($r.Calls -join ' | ')
    Assert-C487 -Cond ((Get-C589Line -Result $r -Pattern '^BUILD SLOT waiting label=late position=1 occupied=2/2 elapsed=0m$') -ge 1 -and (Get-C589Line -Result $r -Pattern '^BUILD SLOT timeout after 1s position=1$') -eq 1) `
        -Name 'C589 WrapperTimeout prints the waiting and timeout lines' -Detail $r.Text
}

function Test-C589_WrapperUnreachableAtDeadline {
    foreach ($case in @(@{ Answer = 'deadline_unreachable,unreachable'; Last = 'no answer' }, @{ Answer = 'deadline_notfound,notfound'; Last = 'http 404' })) {
        $fx = New-C589Case -Name ('wrapper-deadline-' + $case.Answer)
        $r = Invoke-C589Wrapper -Fx $fx -SlotScript $case.Answer -WaitSeconds '1' -GraceSeconds '0.4' -RetryMs '10' -WrapperArgs @('-Label', 'late-unreachable', '--', 'dotnet', 'build', 'tests/X')
        Assert-C487 -Cond ($r.Exit -eq 0 -and (Get-C589Line -Result $r -Pattern ('^BUILD SLOT unleased reason=runner_unreachable maxcpucount=4 last=' + $case.Last + '$')) -eq 1 -and (Get-C589Line -Result $r -Pattern '^BUILD SLOT timeout ') -eq 0) `
            -Name ('C589 WrapperUnreachableAtDeadline ' + $case.Answer + ' fails open after grace') -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
        $first = @($r.Calls | Where-Object { $_ -like 'FIRST_FAILURE_AT *' })
        $command = @($r.Calls | Where-Object { $_ -like 'CMD_AT *' })
        $graceElapsed = -1.0
        if ($first.Count -eq 1 -and $command.Count -eq 1) {
            $graceElapsed = ([long](($command[0] -split ' ')[1]) - [long](($first[0] -split ' ')[1])) / [double][Diagnostics.Stopwatch]::Frequency
        }
        Assert-C487 -Cond ($graceElapsed -ge 0.39 -and (Get-C589Order -Result $r) -cmatch '^POST,(POST,)+CMD$' -and (Get-C589Cmd -Result $r) -ceq 'CMD dotnet build tests/X -maxcpucount 4') `
            -Name ('C589 WrapperUnreachableAtDeadline ' + $case.Answer + ' never runs unleased early') -Detail ('grace={0:N3}s calls={1}' -f $graceElapsed, ($r.Calls -join ' | '))
    }
}

function Test-C589_WrapperUnreachable {
    $fx = New-C589Case -Name 'wrapper-unreachable'
    $r = Invoke-C589Wrapper -Fx $fx -SlotScript 'unreachable' -GraceSeconds '0' -WrapperArgs @('-Label', 'offline', '--', 'dotnet', 'build', 'tests/X')
    Assert-C487 -Cond ((Get-C589Line -Result $r -Pattern '^BUILD SLOT unleased reason=runner_unreachable maxcpucount=4 last=no answer') -eq 1) `
        -Name 'C589 WrapperUnreachable prints the unleased line' -Detail $r.Text
    Assert-C487 -Cond ($r.Exit -eq 0 -and (Get-C589Order -Result $r) -ceq 'POST,CMD' -and (Get-C589Cmd -Result $r) -ceq 'CMD dotnet build tests/X -maxcpucount 4') `
        -Name 'C589 WrapperUnreachable runs with the fallback -maxcpucount 4 and releases nothing' -Detail ('exit={0} calls={1}' -f $r.Exit, ($r.Calls -join ' | '))

    # A runner that answers busy and then goes away is still unleased after the grace, not a timeout.
    $gone = New-C589Case -Name 'wrapper-gone'
    $g = Invoke-C589Wrapper -Fx $gone -SlotScript 'busy,notfound' -GraceSeconds '0' -WrapperArgs @('-Label', 'gone', '--', 'dotnet', 'build', 'tests/X')
    Assert-C487 -Cond ($g.Exit -eq 0 -and (Get-C589Line -Result $g -Pattern '^BUILD SLOT unleased reason=runner_unreachable maxcpucount=4 last=http 404$') -eq 1 -and (Get-C589Order -Result $g) -ceq 'POST,POST,CMD') `
        -Name 'C589 WrapperUnreachable a runner that stops answering mid-wait falls back unleased' -Detail ('exit={0} calls={1} {2}' -f $g.Exit, ($g.Calls -join ' | '), $g.Text)

    $un = New-C589Case -Name 'wrapper-unlimited'
    $u = Invoke-C589Wrapper -Fx $un -SlotScript 'unlimited' -WrapperArgs @('-Label', 'disabled', '--', 'dotnet', 'build', 'tests/X')
    Assert-C487 -Cond ($u.Exit -eq 0 -and (Get-C589Line -Result $u -Pattern '^BUILD SLOT unlimited maxcpucount=5$') -eq 1 -and (Get-C589Order -Result $u) -ceq 'POST,CMD' -and (Get-C589Cmd -Result $u) -ceq 'CMD dotnet build tests/X -maxcpucount 5') `
        -Name 'C589 WrapperUnreachable a disabled broker answers unlimited with its cpu count and holds nothing' -Detail ('exit={0} calls={1} {2}' -f $u.Exit, ($u.Calls -join ' | '), $u.Text)
}

function Test-C589_WrapperAsciiOnly {
    foreach ($path in @($script:Wrapper, $script:Lib, (Join-Path $here 'test-build-slot.ps1'), $script:SlotShim, $script:CommandShim)) {
        $bytes = @([IO.File]::ReadAllBytes($path) | Where-Object { $_ -gt 127 })
        Assert-C487 -Cond ($bytes.Count -eq 0) -Name ('C589 WrapperAsciiOnly {0} is ASCII-only' -f (Split-Path -Leaf $path)) -Detail ([string]$bytes.Count)
    }
}

function Test-C589_WrapperRenews {
    $fx = New-C589Case -Name 'wrapper-renew'
    $env:C589_COMMAND_SLEEP_SECONDS = '3'
    try { $r = Invoke-C589Wrapper -Fx $fx -SlotScript 'renew_granted' -WrapperArgs @('-Label', 'renew', '--', 'dotnet', 'build', 'tests/X') }
    finally { Remove-Item Env:C589_COMMAND_SLEEP_SECONDS -ErrorAction SilentlyContinue }
    $renew = @($r.Calls | Where-Object { $_ -like 'SLOT RENEW *' })
    $deleteIndex = [Array]::FindIndex([string[]]$r.Calls, [Predicate[string]] { param($line) $line -like 'SLOT DELETE *' })
    Assert-C487 -Cond ($r.Exit -eq 0 -and $renew.Count -ge 2 -and $deleteIndex -gt 0 -and @($r.Calls[0..($deleteIndex - 1)] | Where-Object { $_ -like 'SLOT RENEW *' }).Count -ge 2) `
        -Name 'C589 WrapperRenews renews twice before release' -Detail ($r.Calls -join ' | ')

    $pidFx = New-C589Case -Name 'wrapper-pid'
    $env:C589_COMMAND_SLEEP_SECONDS = '3'
    try { $pidResult = Invoke-C589Wrapper -Fx $pidFx -SlotScript 'granted' -WrapperArgs @('-Label', 'pid', '--', 'dotnet', 'build', 'tests/X') }
    finally { Remove-Item Env:C589_COMMAND_SLEEP_SECONDS -ErrorAction SilentlyContinue }
    Assert-C487 -Cond ($pidResult.Exit -eq 0 -and @($pidResult.Calls | Where-Object { $_ -like 'SLOT RENEW *' }).Count -eq 0) `
        -Name 'C589 WrapperRenews pid grant has no renewals' -Detail ($pidResult.Calls -join ' | ')
}

$script:C589ExpectedRows = 35 + 11 + $(if ($IsLinux) { 3 } else { 0 })

foreach ($required in @($script:Wrapper, $script:Lib)) {
    if (-not (Test-Path -LiteralPath $required)) { throw ('missing ' + $required) }
}

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'C589_')) { & $fn }
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'C800_')) { & $fn }
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'build-slot-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { $script:C589ExpectedRows })
