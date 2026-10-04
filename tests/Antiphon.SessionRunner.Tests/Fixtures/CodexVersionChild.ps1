param(
    [ValidateSet('success','nonzero','stderr','stdout-flood','stderr-flood','stdin','timeout','tree','parent-exits','leaf','diagnostic','notice','notice-held')]
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
[Console]::Out.WriteLine('codex-cli 0.160.0')
if ($Mode -eq 'stderr') { [Console]::Error.Write('E') }
if ($Mode -eq 'diagnostic') { [Console]::Error.Write('C959-diagnostic-sentinel') }
if ($Mode -eq 'notice' -or $Mode -eq 'notice-held') {
    [Console]::Error.Write('npm notice synthetic update; C:\Users\C1031\private; /home/C1031/private; C1031-token-canary')
}
if ($Mode -eq 'nonzero') { exit 1 }
exit 0
