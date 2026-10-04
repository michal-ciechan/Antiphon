param(
    [ValidateSet('success','nonzero','stderr','stdout-flood','stderr-flood','stdin','timeout','tree','parent-exits','leaf','diagnostic','notice','notice-held',
        'stderr-4096','stderr-4097','stdout-notices','outside-cap','fragment','lf-cap','lf-cap-overflow','crlf-cap','crlf-split','eof-cap','eof-stderr-overflow','both-floods',
        'nonzero-notice','nonzero-stdout','nonzero-stderr','nonzero-both','stderr-banner','malformed-stdout','empty')]
    [string]$Mode = 'success',
    [Parameter(Mandatory=$true)][string]$ReceiptRoot
)
$ErrorActionPreference = 'Stop'
if (-not [IO.Directory]::Exists($ReceiptRoot)) { throw 'missing owned receipt root' }
$ownedProcess = [Diagnostics.Process]::GetCurrentProcess()
$kernelStartTicks = $null
if ($IsLinux) {
    $stat = [IO.File]::ReadAllText("/proc/$PID/stat")
    $kernelStartTicks = ($stat.Substring($stat.LastIndexOf(')') + 2) -split ' ')[19]
}
$receipt = @{
    pid = $PID
    kernelStartTicks = $kernelStartTicks
    startedUtc = $ownedProcess.StartTime.ToUniversalTime().ToString('O')
    mode = $Mode
} | ConvertTo-Json -Compress
[IO.File]::WriteAllText((Join-Path $ReceiptRoot ($Mode + '.json')), $receipt)
if ($Mode -eq 'notice-held') {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not [IO.File]::Exists((Join-Path $ReceiptRoot 'release'))) {
        if ([DateTime]::UtcNow -ge $deadline) { throw 'owned notice was not released' }
        [Threading.Thread]::Sleep(10)
    }
}
if ($Mode -eq 'leaf' -or $Mode -eq 'timeout') {
    [Threading.Thread]::Sleep(20000)
    exit 0
}
if ($Mode -eq 'tree' -or $Mode -eq 'parent-exits') {
    $shellName = if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' }
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME $shellName))
    $start.UseShellExecute = $false
    foreach ($item in @('-NoLogo','-NoProfile','-NonInteractive','-File',
                         $PSCommandPath,'-Mode','leaf','-ReceiptRoot',$ReceiptRoot)) {
        $start.ArgumentList.Add($item)
    }
    $leaf = [Diagnostics.Process]::Start($start)
    try {
        $readyPath = Join-Path $ReceiptRoot 'leaf.json'
        $readyLimit = [DateTime]::UtcNow.AddSeconds(5)
        while (-not [IO.File]::Exists($readyPath) -and [DateTime]::UtcNow -lt $readyLimit) {
            [Threading.Thread]::Sleep(10)
        }
        if (-not [IO.File]::Exists($readyPath)) { throw 'leaf did not become ready' }
        [IO.File]::WriteAllText((Join-Path $ReceiptRoot 'ready'), 'ready')
        [Console]::Out.WriteLine('codex-cli 0.160.0')
        if ($Mode -ne 'parent-exits') { $leaf.WaitForExit() }
    }
    finally {
        if ($Mode -ne 'parent-exits') {
            if (-not $leaf.HasExited) { $leaf.Kill($true) }
            $leaf.WaitForExit()
        }
        $leaf.Dispose()
    }
    exit 0
}
if ($Mode -eq 'stdout-flood') { [Console]::Out.Write(('A' * 4097)); exit 0 }
if ($Mode -eq 'stderr-flood') { [Console]::Error.Write(('E' * 4097)); exit 0 }
if ($Mode -eq 'stdin') {
    $inputBody = [Console]::In.ReadToEnd()
    if ($inputBody.Length -ne 0) { throw 'probe stdin carried data' }
    [IO.File]::WriteAllText((Join-Path $ReceiptRoot 'stdin-eof'), 'eof')
}
$banner = 'codex-cli 0.160.0'
switch ($Mode) {
    'stderr-4096' { [Console]::Out.WriteLine($banner); [Console]::Error.Write(('E' * 4096)); exit 0 }
    'stderr-4097' { [Console]::Out.WriteLine($banner); [Console]::Error.Write(('E' * 4097)); exit 0 }
    'stdout-notices' { [Console]::Out.Write($banner + "`n" + ('N' * 4097)); [Console]::Error.Write('notice'); exit 0 }
    'outside-cap' { [Console]::Out.Write(('N' * 4096) + "`n" + $banner + "`n"); exit 0 }
    'fragment' { [Console]::Out.Write(('N' * (4096 - $banner.Length - 1)) + "`n" + $banner + "-beta`n"); exit 0 }
    'lf-cap' { [Console]::Out.Write(('N' * (4096 - $banner.Length - 2)) + "`n" + $banner + "`n"); exit 0 }
    'lf-cap-overflow' { [Console]::Out.Write(('N' * (4096 - $banner.Length - 2)) + "`n" + $banner + "`nN"); exit 0 }
    'crlf-cap' { [Console]::Out.Write(('N' * (4096 - $banner.Length - 3)) + "`n" + $banner + "`r`n"); exit 0 }
    'crlf-split' { [Console]::Out.Write(('N' * (4096 - $banner.Length - 2)) + "`n" + $banner + "`r`n"); exit 0 }
    'eof-cap' { [Console]::Out.Write(('N' * (4096 - $banner.Length - 1)) + "`n" + $banner); exit 0 }
    'eof-stderr-overflow' { [Console]::Out.Write($banner); [Console]::Error.Write(('E' * 4097)); exit 0 }
    'both-floods' { [Console]::Out.Write($banner + "`n" + ('N' * 1048576)); [Console]::Error.Write(('E' * 1048576)); exit 0 }
    'nonzero-notice' { [Console]::Out.WriteLine($banner); [Console]::Error.Write('notice'); exit 1 }
    'nonzero-stdout' { [Console]::Out.Write($banner + "`n" + ('N' * 4097)); [Console]::Error.Write('notice'); exit 1 }
    'nonzero-stderr' { [Console]::Out.WriteLine($banner); [Console]::Error.Write(('E' * 4097)); exit 1 }
    'nonzero-both' { [Console]::Out.Write($banner + "`n" + ('N' * 4097)); [Console]::Error.Write(('E' * 4097)); exit 1 }
    'stderr-banner' { [Console]::Error.WriteLine($banner); exit 0 }
    'malformed-stdout' { [Console]::Out.WriteLine('codex-cli banana'); [Console]::Error.WriteLine($banner); exit 0 }
    'empty' { exit 0 }
}
[Console]::Out.WriteLine('codex-cli 0.160.0')
if ($Mode -eq 'stderr') { [Console]::Error.Write('E') }
if ($Mode -eq 'diagnostic') { [Console]::Error.Write('C959-diagnostic-sentinel') }
if ($Mode -eq 'notice' -or $Mode -eq 'notice-held') {
    [Console]::Error.Write('npm notice synthetic update; C:\Users\C1031\private; /home/C1031/private; C1031-token-canary')
}
if ($Mode -eq 'nonzero') { exit 1 }
exit 0
