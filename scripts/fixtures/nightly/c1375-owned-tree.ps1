#requires -Version 7.0
# Native owned fixture. No self-exit deadline: the supervisor must terminate it.
param(
    [Parameter(Mandatory)][ValidateSet('root', 'child', 'grandchild')][string]$Mode,
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][string]$Nonce
)
$ErrorActionPreference = 'Stop'
if (-not $IsLinux) { throw 'CARD-1375 requires Linux.' }
$child = $null
if ($Mode -ne 'grandchild') {
    $next = if ($Mode -eq 'root') { 'child' } else { 'grandchild' }
    $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME 'pwsh'))
    $info.UseShellExecute = $false
    foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', $PSCommandPath,
        '-Mode', $next, '-Directory', $Directory, '-Nonce', $Nonce)) {
        $info.ArgumentList.Add($argument)
    }
    # Start the next generation before publishing our record: its PowerShell
    # startup can overlap our initialization within the same readiness bound.
    # Inherit both redirected pipes and retain the child for normal OS reaping.
    $child = [Diagnostics.Process]::Start($info)
}
$self = [Diagnostics.Process]::GetCurrentProcess()
$stat = [IO.File]::ReadAllText("/proc/$PID/stat")
$fields = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ')
$identity = [ordered]@{
    role = $Mode; nonce = $Nonce; pid = $PID; parentPid = [int]$fields[1]
    start = $fields[19]; startTimeUtcTicks = $self.StartTime.ToUniversalTime().Ticks
}
$path = Join-Path $Directory "$Mode.json"
[IO.File]::WriteAllText("$path.tmp", ($identity | ConvertTo-Json -Compress))
[IO.File]::Move("$path.tmp", $path)
[Console]::Out.WriteLine("C1375 $Mode stdout $Nonce")
[Console]::Error.WriteLine("C1375 $Mode stderr $Nonce")
if ($null -ne $child) { $child.WaitForExit() }
while ($true) { [Threading.Thread]::Sleep(1000) }
