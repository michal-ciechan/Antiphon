param([Parameter(Mandatory)][string]$Sandbox, [Parameter(Mandatory)][string]$ReadyFile)

$ErrorActionPreference = 'Stop'
$sandbox = [IO.Path]::GetFullPath($Sandbox)
$current = [Diagnostics.Process]::GetCurrentProcess()
$stat = [IO.File]::ReadAllText("/proc/$($current.Id)/stat")
$fields = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
$owner = [ordered]@{
    Pid = $current.Id
    StartUtcTicks = [long]$fields[19]
    Host = [Environment]::MachineName
    Boot = ([IO.File]::ReadAllText('/proc/sys/kernel/random/boot_id')).Trim()
    PidNamespace = (& readlink /proc/self/ns/pid).Trim()
}
$inventory = @()
$index = Join-Path $sandbox '.checkpoint-temp-roots.jsonl'
$chunk = [byte[]]::new(1024 * 1024)
[Security.Cryptography.RandomNumberGenerator]::Fill($chunk)
for ($i = 0; $i -lt 49; $i++) {
    $id = [guid]::NewGuid().ToString('N')
    $root = Join-Path $sandbox ('c723-' + $id)
    New-Item -ItemType Directory -Path $root | Out-Null
    $marker = [ordered]@{
        Version = 1; RootId = $id; AttemptId = [guid]::NewGuid().ToString('N')
        AssemblyInvocationId = 'c804-orphan-owner'; CreatedAt = [DateTimeOffset]::UtcNow.AddHours(-1).ToString('o')
        RootPath = $root; Owner = $owner; State = 'active'
    }
    [IO.File]::WriteAllText((Join-Path $root '.checkpoint-test-root.json'), ($marker | ConvertTo-Json -Depth 10 -Compress))
    $sizeMb = if ($i -eq 48) { 300 } else { 8 }
    $payload = Join-Path $root 'payload.bin'
    $stream = [IO.File]::Open($payload, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        for ($part = 0; $part -lt $sizeMb; $part++) { $stream.Write($chunk, 0, $chunk.Length) }
        $stream.Flush($true)
    } finally { $stream.Dispose() }
    [IO.File]::AppendAllText($index, (($root | ConvertTo-Json -Compress) + "`n"))
    $inventory += [ordered]@{ path = $root; allocatedLogicalBytes = [long]$sizeMb * 1024 * 1024 }
}
[IO.File]::WriteAllText($ReadyFile, (@{ owner = $owner; roots = $inventory } | ConvertTo-Json -Depth 10))
while ($true) { Start-Sleep -Seconds 1 }
