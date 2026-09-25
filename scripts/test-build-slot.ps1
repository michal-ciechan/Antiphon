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
    'C589_SLOT_WAIT_SECONDS', 'C589_SLOT_GRACE_SECONDS', 'C589_SLOT_RETRY_MS', 'C589_COMMAND_SHIM', 'C589_COMMAND_EXIT')

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
        [int]$CommandExit = 0
    )
    Set-C589Seams -Fx $Fx -SlotScript $SlotScript -WaitSeconds $WaitSeconds -GraceSeconds $GraceSeconds -RetryMs $RetryMs -CommandExit $CommandExit
    Push-Location -LiteralPath $script:RepoRoot
    try {
        $output = & pwsh -NoProfile -NonInteractive -File $script:Wrapper @WrapperArgs 2>&1
        $code = $LASTEXITCODE
    } finally {
        Pop-Location
        Clear-C589Seams
    }
    $lines = @($output | ForEach-Object { [string]$_ })
    $calls = @()
    if (Test-Path -LiteralPath $Fx.Log) { $calls = @(Get-Content -LiteralPath $Fx.Log) }
    return [pscustomobject]@{ Exit = $code; Lines = $lines; Text = ($lines -join "`n"); Calls = $calls }
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
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $r = Invoke-C589Wrapper -Fx $fx -SlotScript $case.Answer -WaitSeconds '1' -GraceSeconds '0.4' -RetryMs '10' -WrapperArgs @('-Label', 'late-unreachable', '--', 'dotnet', 'build', 'tests/X')
        $clock.Stop()
        Assert-C487 -Cond ($r.Exit -eq 0 -and (Get-C589Line -Result $r -Pattern ('^BUILD SLOT unleased reason=runner_unreachable maxcpucount=4 last=' + $case.Last + '$')) -eq 1 -and (Get-C589Line -Result $r -Pattern '^BUILD SLOT timeout ') -eq 0) `
            -Name ('C589 WrapperUnreachableAtDeadline ' + $case.Answer + ' fails open after grace') -Detail ('exit={0} {1}' -f $r.Exit, $r.Text)
        Assert-C487 -Cond ($clock.Elapsed.TotalSeconds -ge 1.5 -and (Get-C589Order -Result $r) -ceq 'POST,POST,CMD' -and (Get-C589Cmd -Result $r) -ceq 'CMD dotnet build tests/X -maxcpucount 4') `
            -Name ('C589 WrapperUnreachableAtDeadline ' + $case.Answer + ' never runs unleased early') -Detail ('elapsed={0:N3}s calls={1}' -f $clock.Elapsed.TotalSeconds, ($r.Calls -join ' | '))
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

$script:C589ExpectedRows = 7 + 12 + 2 + 3 + 4 + 2 + 5

foreach ($required in @($script:Wrapper, $script:Lib)) {
    if (-not (Test-Path -LiteralPath $required)) { throw ('missing ' + $required) }
}

if ($Case) {
    $fn = Get-Command -Name ('Test-{0}' -f $Case) -ErrorAction SilentlyContinue
    if (-not $fn) { Write-Error ('unknown case {0}' -f $Case); exit 2 }
    & $fn
} else {
    foreach ($fn in (Get-C487CaseFunctions -Prefix 'C589_')) { & $fn }
}
Write-C487Evidence -ResultsDirectory $ResultsDirectory -Case 'build-slot-summary' -Body @{ passed = $script:C487Passed; failed = $script:C487Failed; rows = $script:C487Rows }
Complete-C487Harness -ResultsDirectory $ResultsDirectory -ExpectedRows $(if ($Case) { 0 } else { $script:C589ExpectedRows })
