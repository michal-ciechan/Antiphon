# CARD-0490: build the source-free QEMU boot disk for the PC-28-31 native lane.
#
# The image is generated, never downloaded: every byte comes from the table below
# plus zero padding, so two runs on any machine produce identical bytes. It holds
# no application source, no compiled Antiphon assemblies, no credentials and no
# saved VM memory - only a 512-byte MBR boot sector that proves the guest boots
# and can be driven over the serial chardev the plan's invocation supplies.
#
# Guest contract (see tests/fixtures/card0490-linux/README.md):
#   - emits "CARD0490-BOOT-OK" on COM1 (0x3F8, 115200 8N1) once the MBR runs
#   - echoes every byte received on COM1 back to COM1 (drivability proof)
#   - on 'q' emits "CARD0490-HALT" and powers off through ACPI PM1a_CNT (0x604)
#
# Usage:
#   pwsh -NoProfile -File scripts/build-card0490-bootdisk.ps1 -QemuDir <dir> -Smoke -WriteLock
param(
    [string] $QemuDir = '',
    [string] $OutputPath = '',
    [string] $LockFile = 'tests/fixtures/card0490-linux/assets.lock.json',
    [int] $VirtualSizeMiB = 64,
    [switch] $Smoke,
    [switch] $WriteLock
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not [System.IO.Path]::IsPathRooted($LockFile)) { $LockFile = Join-Path $repoRoot $LockFile }
if ([string]::IsNullOrWhiteSpace($QemuDir)) { $QemuDir = $env:CARD0490_QEMU_DIR }
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot '.antiphon\card0490-assets\card0490-bootdisk.qcow2'
}

function Get-Sha256([string] $path) {
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Resolve-QemuTool([string] $dir, [string] $leaf) {
    if ([string]::IsNullOrWhiteSpace($dir) -or -not (Test-Path -LiteralPath $dir)) {
        throw "QemuDir not supplied or missing. Pass -QemuDir or set CARD0490_QEMU_DIR to the extracted pinned QEMU bundle."
    }
    $hit = Get-ChildItem -LiteralPath $dir -Filter $leaf -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $hit) { throw "$leaf not found under $dir." }
    return $hit.FullName
}

# --- 512-byte master boot record -------------------------------------------
# Hand-assembled 16-bit real mode, org 0x7C00. Each row asserts its own offset,
# so a mis-sized row fails the build instead of producing a silently wrong image.
function New-BootSector {
    # A List<byte> is used rather than an index variable so the row helpers can
    # append from their own scope; Count is the authoritative current offset.
    $code = [System.Collections.Generic.List[byte]]::new()
    $emitBytes = {
        param([int] $at, [byte[]] $bytes)
        if ($at -ne $code.Count) {
            throw ("boot sector offset drift: expected 0x{0:X2}, row declares 0x{1:X2}" -f $code.Count, $at)
        }
        $code.AddRange($bytes)
    }
    $emit = {
        param([int] $at, [string] $hex)
        $bytes = @($hex -split '\s+' | Where-Object { $_ } | ForEach-Object { [Convert]::ToByte($_, 16) })
        & $emitBytes $at ([byte[]]$bytes)
    }

    # entry: flat segments, stack just below the load address
    & $emit 0x00 'FA 31 C0 8E D8 8E C0 8E D0 BC 00 7C FB'  # cli; xor ax,ax; ds=es=ss=0; sp=0x7C00; sti
    # COM1 (0x3F8) to 115200 8N1, FIFO on, DTR/RTS asserted
    & $emit 0x0D 'BA F9 03'; & $emit 0x10 '30 C0'; & $emit 0x12 'EE'   # IER   = 0x00
    & $emit 0x13 'BA FB 03'; & $emit 0x16 'B0 80'; & $emit 0x18 'EE'   # LCR   = DLAB
    & $emit 0x19 'BA F8 03'; & $emit 0x1C 'B0 01'; & $emit 0x1E 'EE'   # DLL   = 1
    & $emit 0x1F 'BA F9 03'; & $emit 0x22 '30 C0'; & $emit 0x24 'EE'   # DLM   = 0
    & $emit 0x25 'BA FB 03'; & $emit 0x28 'B0 03'; & $emit 0x2A 'EE'   # LCR   = 8N1
    & $emit 0x2B 'BA FA 03'; & $emit 0x2E 'B0 C7'; & $emit 0x30 'EE'   # FCR   = 0xC7
    & $emit 0x31 'BA FC 03'; & $emit 0x34 'B0 03'; & $emit 0x36 'EE'   # MCR   = DTR|RTS
    # banner
    & $emit 0x37 'BE 87 7C'                                            # mov si, banner
    & $emit 0x3A 'AC'; & $emit 0x3B '84 C0'; & $emit 0x3D '74 05'      # lodsb; test al,al; jz echo
    & $emit 0x3F 'E8 25 00'; & $emit 0x42 'EB F6'                      # call putc; jmp banner loop
    # echo loop: every driven byte comes straight back out; 'q' ends the run
    & $emit 0x44 'E8 31 00'; & $emit 0x47 '3C 71'; & $emit 0x49 '74 05' # call getc; cmp al,'q'; je halt
    & $emit 0x4B 'E8 19 00'; & $emit 0x4E 'EB F4'                      # call putc; jmp echo
    # halt: farewell banner then ACPI S5
    & $emit 0x50 'BE 9A 7C'
    & $emit 0x53 'AC'; & $emit 0x54 '84 C0'; & $emit 0x56 '74 05'
    & $emit 0x58 'E8 0C 00'; & $emit 0x5B 'EB F6'
    & $emit 0x5D 'BA 04 06'; & $emit 0x60 'B8 00 20'; & $emit 0x63 'EF' # out 0x604, 0x2000
    & $emit 0x64 'F4'; & $emit 0x65 'EB FD'                             # hlt; jmp $
    # putc: spin on THRE, then write AL
    & $emit 0x67 '52'; & $emit 0x68 '50'
    & $emit 0x69 'BA FD 03'; & $emit 0x6C 'EC'; & $emit 0x6D 'A8 20'; & $emit 0x6F '74 F8'
    & $emit 0x71 '58'; & $emit 0x72 'BA F8 03'; & $emit 0x75 'EE'; & $emit 0x76 '5A'; & $emit 0x77 'C3'
    # getc: spin on DR, then read AL
    & $emit 0x78 '52'
    & $emit 0x79 'BA FD 03'; & $emit 0x7C 'EC'; & $emit 0x7D 'A8 01'; & $emit 0x7F '74 F8'
    & $emit 0x81 'BA F8 03'; & $emit 0x84 'EC'; & $emit 0x85 '5A'; & $emit 0x86 'C3'

    $crlfNul = [byte[]] @(13, 10, 0)
    & $emitBytes 0x87 ([Text.Encoding]::ASCII.GetBytes('CARD0490-BOOT-OK') + $crlfNul)
    & $emitBytes 0x9A ([byte[]] @(13, 10) + [Text.Encoding]::ASCII.GetBytes('CARD0490-HALT') + $crlfNul)
    if ($code.Count -ne 0xAC) { throw ("boot sector text ended at 0x{0:X2}, expected 0xAC" -f $code.Count) }

    $sector = New-Object byte[] 512
    $code.CopyTo($sector, 0)
    $sector[510] = 0x55
    $sector[511] = 0xAA
    return , $sector
}

$qemuImg = Resolve-QemuTool $QemuDir 'qemu-img.exe'
$lock = Get-Content -LiteralPath $LockFile -Raw | ConvertFrom-Json

# The disk is only trustworthy if the tool that wrote it is the pinned one.
$imgSha = Get-Sha256 $qemuImg
if ($lock.'qemu-img'.sha256 -ne 'pending-operator-pin' -and $imgSha -ne $lock.'qemu-img'.sha256) {
    throw "qemu-img.exe sha256 $imgSha does not match the pin $($lock.'qemu-img'.sha256) in $LockFile."
}

$outDir = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$rawPath = [System.IO.Path]::ChangeExtension($OutputPath, '.raw')
Remove-Item -LiteralPath $rawPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $OutputPath -Force -ErrorAction SilentlyContinue

$sector = New-BootSector
$stream = [IO.File]::Create($rawPath)
try {
    $stream.Write($sector, 0, $sector.Length)
    $stream.SetLength([long]$VirtualSizeMiB * 1MB)
}
finally { $stream.Dispose() }
Write-Host ("raw image {0} bytes sha256={1}" -f (Get-Item $rawPath).Length, (Get-Sha256 $rawPath))

& $qemuImg convert -f raw -O qcow2 -o compat=1.1 $rawPath $OutputPath | Out-Host
if ($LASTEXITCODE -ne 0) { throw "qemu-img convert failed (exit $LASTEXITCODE)." }
Remove-Item -LiteralPath $rawPath -Force -ErrorAction SilentlyContinue

$bootSha = Get-Sha256 $OutputPath
Write-Host ("bootDisk {0} bytes sha256={1} path={2}" -f (Get-Item $OutputPath).Length, $bootSha, $OutputPath)

if ($Smoke) {
    $qemuSystem = Resolve-QemuTool $QemuDir 'qemu-system-x86_64.exe'
    $systemSha = Get-Sha256 $qemuSystem
    if ($lock.qemu.sha256 -ne 'pending-operator-pin' -and $systemSha -ne $lock.qemu.sha256) {
        throw "qemu-system-x86_64.exe sha256 $systemSha does not match the pin $($lock.qemu.sha256) in $LockFile."
    }
    $share = Join-Path (Split-Path -Parent $qemuSystem) 'share'
    $overlay = Join-Path $outDir 'card0490-bootdisk-smoke.qcow2'
    Remove-Item -LiteralPath $overlay -Force -ErrorAction SilentlyContinue
    & $qemuImg create -f qcow2 -F qcow2 -b $OutputPath $overlay | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "qemu-img create overlay failed (exit $LASTEXITCODE)." }

    $rootJson = [ordered]@{
        driver      = 'qcow2'
        'node-name' = 'pcroot'
        file        = [ordered]@{ driver = 'file'; filename = $overlay }
    } | ConvertTo-Json -Depth 4 -Compress

    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $qemuSystem
    $smokeArgs = @(
        '-no-user-config', '-nodefaults',
        '-machine', 'q35', '-accel', 'tcg,thread=multi', '-cpu', 'max', '-smp', '2', '-m', '4096',
        '-display', 'none', '-monitor', 'none', '-nic', 'none', '-no-reboot',
        '-chardev', 'stdio,id=pcio,signal=off', '-serial', 'chardev:pcio',
        '-blockdev', $rootJson,
        '-device', 'virtio-blk-pci,drive=pcroot,bootindex=1')
    if (Test-Path -LiteralPath $share) { $smokeArgs = @('-L', $share) + $smokeArgs }
    foreach ($a in $smokeArgs) { $psi.ArgumentList.Add($a) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $driven = 'CARD0490-DRIVEN'
    $proc = [Diagnostics.Process]::Start($psi)
    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    try {
        Start-Sleep -Seconds 8
        $proc.StandardInput.Write($driven)
        $proc.StandardInput.Flush()
        Start-Sleep -Seconds 3
        $proc.StandardInput.Write('q')
        $proc.StandardInput.Flush()
        if (-not $proc.WaitForExit(60000)) {
            $proc.Kill($true)
            [void]$proc.WaitForExit(10000)
            throw "Guest did not power off within 60s."
        }
    }
    finally {
        if (-not $proc.HasExited) { try { $proc.Kill($true) } catch { } }
    }
    $serial = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    Write-Host "--- guest serial ---"
    Write-Host $serial
    if ($stderr) { Write-Host "--- qemu stderr ---"; Write-Host $stderr }
    if ($serial -notmatch 'CARD0490-BOOT-OK') { throw "Guest never announced CARD0490-BOOT-OK." }
    if ($serial -notmatch [regex]::Escape($driven)) { throw "Guest did not echo driven input '$driven'." }
    if ($serial -notmatch 'CARD0490-HALT') { throw "Guest did not acknowledge the halt byte." }
    if ($proc.ExitCode -ne 0) { throw "QEMU exited $($proc.ExitCode) after guest poweroff." }
    Remove-Item -LiteralPath $overlay -Force -ErrorAction SilentlyContinue
    Write-Host "Smoke passed: boot banner, driven echo and ACPI poweroff all observed."
}

if ($WriteLock) {
    # Same shape as the qemu/qemu-img entries: version, hash, provenance, file name.
    # "source" is the generator rather than a URL because the disk is built, not fetched.
    $lock.bootDisk = [pscustomobject][ordered]@{
        version        = '1'
        sha256         = $bootSha
        source         = 'scripts/build-card0490-bootdisk.ps1'
        file           = [System.IO.Path]::GetFileName($OutputPath)
        format         = 'qcow2'
        virtualSizeMiB = $VirtualSizeMiB
    }
    $lock | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $LockFile -Encoding utf8
    Write-Host "Wrote bootDisk pin to $LockFile"
}

exit 0
