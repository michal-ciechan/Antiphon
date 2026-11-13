# CARD-1885: read only private compiler identities; never select a process by name for cleanup.
function Get-C1885Compilers([string]$Pipe) {
    $observations = @()
    foreach ($directory in [IO.Directory]::EnumerateDirectories('/proc')) {
        $number = 0
        if (-not [int]::TryParse([IO.Path]::GetFileName($directory), [ref]$number)) { continue }
        try {
            $argv = [IO.File]::ReadAllText("$directory/cmdline").Split([char]0)
            if ($argv -cnotcontains "-pipename:$Pipe") { continue }
            $stat = [IO.File]::ReadAllText("$directory/stat")
            $fields = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
            $status = [IO.File]::ReadAllText("$directory/status")
            $cpus = [regex]::Match($status, '(?m)^Cpus_allowed_list:\s*(.+)$').Groups[1].Value.Trim()
            $rss = [regex]::Match($status, '(?m)^VmRSS:\s*(\d+)').Groups[1].Value
            $threadCpus = @()
            foreach ($thread in [IO.Directory]::EnumerateDirectories("$directory/task")) {
                try { $threadCpus += [regex]::Match([IO.File]::ReadAllText("$thread/status"), '(?m)^Cpus_allowed_list:\s*(.+)$').Groups[1].Value.Trim() } catch [IO.IOException] { }
            }
            $again = [IO.File]::ReadAllText("$directory/stat")
            $againFields = $again.Substring($again.LastIndexOf(')') + 2).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
            if ($fields[19] -cne $againFields[19]) { throw 'compiler PID was reused during observation' }
            $observations += @{ Pid = $number; Start = $fields[19]; Ticks = [long]$fields[11] + [long]$fields[12];
                RssBytes = $(if ($rss) { [long]$rss * 1024 } else { 0 }); Cpus = $cpus; Threads = $threadCpus; Argv = $argv }
        } catch [IO.IOException] { } catch [UnauthorizedAccessException] { }
    }
    return $observations
}
function Stop-C1885PrivateCompiler([string]$Dotnet, [string]$Dll, [string]$Pipe, [string]$Directory) {
    # Shutdown is addressed to the exact pipe owned by this fixture, never all SDK servers.
    $stop = [Diagnostics.ProcessStartInfo]::new($Dotnet)
    $stop.UseShellExecute = $false
    $stop.WorkingDirectory = $Directory
    foreach ($arg in @('exec', $Dll, '-shutdown', "-pipename:$Pipe")) { $stop.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($stop)
    try {
        if (-not $process.WaitForExit(15000)) { throw 'private-pipe shutdown deadline' }
        if ($process.ExitCode -ne 0) { throw "private-pipe shutdown failed: $($process.ExitCode)" }
    } finally {
        if (-not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(1000) }
        $process.Dispose()
    }
}
function Get-C1885Set([string]$List) {
    $set = [Collections.Generic.HashSet[int]]::new()
    foreach ($part in $List.Split(',')) {
        $range = $part.Split('-'); $last = [int]$range[0]
        if ($range.Count -eq 2) { $last = [int]$range[1] }
        for ($cpu = [int]$range[0]; $cpu -le $last; $cpu++) { [void]$set.Add($cpu) }
    }
    return ,$set
}
