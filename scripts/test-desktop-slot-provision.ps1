#requires -Version 5.1
# CARD-0824 S2. Witnesses for scripts/desktop-slot-provision.ps1.
# Runs on Linux pwsh and Windows PowerShell 5.1. Roots are temp directories.
# Free space is a fake provider. The script does not clone and does not call dotnet.
# ASCII-only. No ternary, no ??, no && or ||.
$ErrorActionPreference = 'Stop'
$script:passed = 0
$script:failed = 0
$script:oneGiB = [int64]1024 * 1024 * 1024
$script:defaultBudget = [int64]32 * $script:oneGiB
$script:ample = [int64]64 * $script:oneGiB
$script:cloneUrl = 'https://github.com/michal-ciechan/Antiphon'

function Assert-C0824([bool]$Condition, [string]$Name, [string]$Detail) {
    if ($Condition) {
        $script:passed++
        Write-Host ('PASS C0824S2 ' + $Name)
    } else {
        $script:failed++
        if ([string]::IsNullOrEmpty($Detail)) { Write-Host ('FAIL C0824S2 ' + $Name) }
        else { Write-Host ('FAIL C0824S2 ' + $Name + ' :: ' + $Detail) }
    }
}

function Get-ResultText($Result) {
    if ($null -eq $Result) { return '' }
    if ($null -eq $Result.Lines) { return '' }
    return [string]::Join("`n", @($Result.Lines))
}

function New-BytesProvider([int64]$Bytes) {
    $box = @{ Bytes = $Bytes }
    return { param($Path) return [int64]$box.Bytes }.GetNewClosure()
}

function New-GitRepo([string]$Path) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    & git init -q -- $Path 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw ('git init failed for ' + $Path) }
    & git -C $Path config --local core.longpaths false 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw ('git config false failed for ' + $Path) }
}

function Get-Longpaths([string]$Path) {
    $got = & git -C $Path config --local --get core.longpaths 2>&1
    $code = $LASTEXITCODE
    $text = ($got | Out-String).Trim()
    return @{ Code = $code; Value = $text }
}

$repoScript = Join-Path $PSScriptRoot 'desktop-slot-provision.ps1'
if (-not (Test-Path -LiteralPath $repoScript)) {
    Write-Host 'FAIL C0824S2 provision-script-missing'
    Write-Host 'C0824S2 passed=0 failed=1'
    exit 1
}

. $repoScript

if (-not (Get-Command Invoke-DesktopSlotProvision -ErrorAction SilentlyContinue)) {
    Write-Host 'FAIL C0824S2 provision-function-missing'
    Write-Host 'C0824S2 passed=0 failed=1'
    exit 1
}

$script:tempRoot = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) ('c0824s2-' + [guid]::NewGuid().ToString('n'))))
New-Item -ItemType Directory -Path $script:tempRoot | Out-Null
$script:canary = Join-Path $script:tempRoot 'canary.txt'
Set-Content -LiteralPath $script:canary -Value 'keep' -Encoding Ascii

function Invoke-Case([string]$Name, [scriptblock]$Body) {
    try {
        & $Body
    } catch {
        Assert-C0824 $false ($Name + '.threw') $_.Exception.Message
    }
}

function Assert-Forbidden([string]$Name, [string]$CaseRoot) {
    $existed = Test-Path -LiteralPath $CaseRoot
    $result = Invoke-DesktopSlotProvision -Root $CaseRoot -Execute -DiskBudgetGiB 1 -FreeSpaceProvider (New-BytesProvider $script:ample)
    $text = Get-ResultText $result
    Assert-C0824 ($result.ExitCode -eq 2) ($Name + '.exit') $text
    Assert-C0824 ($text.Contains('refused: forbidden-root')) ($Name + '.message') $text
    Assert-C0824 (-not $text.Contains('created:')) ($Name + '.no-created') $text
    if (-not $existed) {
        Assert-C0824 (-not (Test-Path -LiteralPath $CaseRoot)) ($Name + '.absent') $CaseRoot
    }
}

function Assert-TextReason([string]$Name, [string]$CaseRoot, [string]$Expected) {
    $reason = Get-SlotTextRefusal $CaseRoot
    Assert-C0824 ($reason -eq $Expected) ($Name + '.class') $reason
}

function Assert-EntryRefusal([string]$Name, [string]$CaseRoot, [string]$Expected, [bool]$AlsoRemove) {
    $result = Invoke-DesktopSlotProvision -Root $CaseRoot -Execute -RemoveEmpty:$AlsoRemove -DiskBudgetGiB 1 -FreeSpaceProvider (New-BytesProvider $script:ample)
    $text = Get-ResultText $result
    Assert-C0824 ($result.ExitCode -eq 2) ($Name + '.exit') $text
    Assert-C0824 ($text.Contains('refused: ' + $Expected)) ($Name + '.message') $text
    Assert-C0824 (-not $text.Contains('created:')) ($Name + '.no-created') $text
    Assert-C0824 (-not $text.Contains('removed:')) ($Name + '.no-removed') $text
}

try {
    Invoke-Case 'forbidden' {
        Assert-Forbidden 'forbidden-src' 'C:\src\Antiphon'
        Assert-Forbidden 'forbidden-src-child' 'C:\src\Antiphon\server'
        Assert-Forbidden 'forbidden-src-slash' 'C:/src/Antiphon/client'
        Assert-Forbidden 'forbidden-releases' 'C:\Antiphon\releases'
        Assert-Forbidden 'forbidden-releases-child' 'C:\Antiphon\releases\checkout-1'
        Assert-Forbidden 'forbidden-releases-slash' 'C:/Antiphon/releases/candidates/rc'
        Assert-Forbidden 'forbidden-worktrees' 'C:\Antiphon\worktrees'
        Assert-Forbidden 'forbidden-worktrees-child' 'C:\Antiphon\worktrees\card-task-x'
        Assert-Forbidden 'forbidden-worktrees-slash' 'C:/Antiphon/worktrees/ws-01'
    }

    Invoke-Case 'releases-parent' {
        $parent = Join-Path $script:tempRoot 'rel-parent'
        $releases = Join-Path $parent 'releases'
        New-Item -ItemType Directory -Path $releases | Out-Null
        $caseRoot = [System.IO.Path]::GetFullPath((Join-Path $releases 'pool'))
        $result = Invoke-DesktopSlotProvision -Root $caseRoot -Execute -DiskBudgetGiB 1 -FreeSpaceProvider (New-BytesProvider $script:ample)
        $created = Test-Path -LiteralPath $caseRoot
        $text = Get-ResultText $result
        Write-Host ('C0824S2 releases-parent exit=' + $result.ExitCode + ' created=' + $created)
        Assert-C0824 ($result.ExitCode -eq 2) 'releases-parent.exit' $text
        Assert-C0824 ($text.Contains('refused: forbidden-root')) 'releases-parent.message' $text
        Assert-C0824 (-not $created) 'releases-parent.created' $caseRoot
        $left = @(Get-ChildItem -LiteralPath $releases -Force)
        Assert-C0824 ($left.Count -eq 0) 'releases-parent.empty' ([string]$left.Count)
    }

    Invoke-Case 'filesystem-root' {
        foreach ($pair in @(
            @{ Name = 'unix'; Root = '/' },
            @{ Name = 'drive'; Root = 'C:\' },
            @{ Name = 'drive-slash'; Root = 'C:/' }
        )) {
            $result = Invoke-DesktopSlotProvision -Root $pair.Root -Execute -DiskBudgetGiB 1 -FreeSpaceProvider (New-BytesProvider $script:ample)
            $text = Get-ResultText $result
            Assert-C0824 ($result.ExitCode -eq 2) ('filesystem-root-' + $pair.Name + '.exit') $text
            Assert-C0824 ($text.Contains('refused: filesystem-root')) ('filesystem-root-' + $pair.Name + '.message') $text
            Assert-C0824 (-not $text.Contains('created:')) ('filesystem-root-' + $pair.Name + '.no-created') $text
        }
    }

    Invoke-Case 'dry-run' {
        $caseRoot = [System.IO.Path]::GetFullPath((Join-Path $script:tempRoot 'dry'))
        $result = Invoke-DesktopSlotProvision -Root $caseRoot -FreeSpaceProvider (New-BytesProvider $script:ample)
        $text = Get-ResultText $result
        $clone = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $caseRoot 'repos') 'antiphon')).Replace('\', '/')
        Assert-C0824 ($result.ExitCode -eq 0) 'dry-run.exit' $text
        Assert-C0824 (-not (Test-Path -LiteralPath $caseRoot)) 'dry-run.absent' $caseRoot
        Assert-C0824 ($text.Contains('mode=dry-run')) 'dry-run.mode' $text
        Assert-C0824 ($text.Contains('next-command: git clone ' + $script:cloneUrl + ' ' + $clone)) 'dry-run.clone-command' $text
        Assert-C0824 ($text.Contains('next-command: git -C ' + $clone + ' config --local core.longpaths true')) 'dry-run.longpaths-command' $text
        Assert-C0824 (-not $text.Contains('mode=execute')) 'dry-run.not-execute' $text
    }

    Invoke-Case 'dry-run-longpaths' {
        $caseRoot = [System.IO.Path]::GetFullPath((Join-Path $script:tempRoot 'dry-git'))
        $clone = Join-Path (Join-Path $caseRoot 'repos') 'antiphon'
        New-GitRepo $clone
        $result = Invoke-DesktopSlotProvision -Root $caseRoot -FreeSpaceProvider (New-BytesProvider $script:ample)
        $text = Get-ResultText $result
        $got = Get-Longpaths $clone
        Assert-C0824 ($result.ExitCode -eq 0) 'dry-run-longpaths.exit' $text
        Assert-C0824 ($got.Value -eq 'false') 'dry-run-longpaths.unchanged' $got.Value
        Assert-C0824 ($text.Contains('mode=dry-run')) 'dry-run-longpaths.mode' $text
        Assert-C0824 (-not $text.Contains('git clone')) 'dry-run-longpaths.no-clone-text' $text
    }

    Invoke-Case 'whatif' {
        $caseRoot = [System.IO.Path]::GetFullPath((Join-Path $script:tempRoot 'whatif'))
        $result = Invoke-DesktopSlotProvision -Root $caseRoot -Execute -WhatIf -FreeSpaceProvider (New-BytesProvider $script:ample)
        $text = Get-ResultText $result
        Assert-C0824 ($result.ExitCode -eq 0) 'whatif.exit' $text
        Assert-C0824 (-not (Test-Path -LiteralPath $caseRoot)) 'whatif.absent' $caseRoot
        Assert-C0824 ($text.Contains('mode=dry-run')) 'whatif.mode' $text
    }

    Invoke-Case 'execute' {
        $caseRoot = [System.IO.Path]::GetFullPath((Join-Path $script:tempRoot 'execute'))
        $clone = Join-Path (Join-Path $caseRoot 'repos') 'antiphon'
        $decoy = Join-Path $caseRoot 'decoy'
        New-GitRepo $clone
        New-GitRepo $decoy
        $result = Invoke-DesktopSlotProvision -Root $caseRoot -Execute -FreeSpaceProvider (New-BytesProvider $script:ample)
        $text = Get-ResultText $result
        Assert-C0824 ($result.ExitCode -eq 0) 'execute.exit' $text
        Assert-C0824 ($text.Contains('mode=execute')) 'execute.mode' $text
        Assert-C0824 ($text.Contains('longpaths=true')) 'execute.longpaths-line' $text
        Assert-C0824 (-not $text.Contains('git clone')) 'execute.no-clone-text' $text
        Assert-C0824 ($text.Contains('budget-bytes=' + $script:defaultBudget)) 'execute.default-budget' $text
        foreach ($name in @('worktrees', 'workspace-slots', 'retained', 'repos')) {
            $dir = Join-Path $caseRoot $name
            Assert-C0824 (Test-Path -LiteralPath $dir) ('execute.dir.' + $name) $dir
        }
        $ledger = Join-Path (Join-Path $caseRoot 'workspace-slots') 'ledger.json'
        Assert-C0824 (-not (Test-Path -LiteralPath $ledger)) 'execute.no-ledger' $ledger
        $slots = @(Get-ChildItem -LiteralPath (Join-Path $caseRoot 'worktrees') -Force)
        Assert-C0824 ($slots.Count -eq 0) 'execute.no-ws' ([string]$slots.Count)
        $got = Get-Longpaths $clone
        Assert-C0824 ($got.Code -eq 0 -and $got.Value -eq 'true') 'execute.longpaths' $got.Value
        $decoyGot = Get-Longpaths $decoy
        Assert-C0824 ($decoyGot.Value -eq 'false') 'execute.decoy-longpaths' $decoyGot.Value
        $second = Invoke-DesktopSlotProvision -Root $caseRoot -Execute -FreeSpaceProvider (New-BytesProvider $script:ample)
        $secondText = Get-ResultText $second
        $again = Get-Longpaths $clone
        Assert-C0824 ($second.ExitCode -eq 0) 'execute.second-exit' $secondText
        Assert-C0824 ($again.Value -eq 'true') 'execute.second-longpaths' $again.Value
        Assert-C0824 ($secondText.Contains('mode=execute')) 'execute.second-mode' $secondText
        $rootKids = @(Get-ChildItem -LiteralPath $caseRoot -Force)
        Assert-C0824 ($rootKids.Count -eq 5) 'execute.idempotent-children' ([string]$rootKids.Count)
    }

    Invoke-Case 'low-free-space' {
        $caseRoot = [System.IO.Path]::GetFullPath((Join-Path $script:tempRoot 'low'))
        $low = $script:defaultBudget - [int64]1
        $result = Invoke-DesktopSlotProvision -Root $caseRoot -Execute -FreeSpaceProvider (New-BytesProvider $low)
        $text = Get-ResultText $result
        Assert-C0824 ($result.ExitCode -eq 2) 'low-free-space.exit' $text
        Assert-C0824 ($text.Contains('refused: low-free-space')) 'low-free-space.message' $text
        Assert-C0824 (-not (Test-Path -LiteralPath $caseRoot)) 'low-free-space.absent' $caseRoot
        $dry = Invoke-DesktopSlotProvision -Root $caseRoot -FreeSpaceProvider (New-BytesProvider $low)
        $dryText = Get-ResultText $dry
        Assert-C0824 ($dry.ExitCode -eq 2) 'low-free-space.dry-exit' $dryText
        Assert-C0824 (-not (Test-Path -LiteralPath $caseRoot)) 'low-free-space.dry-absent' $caseRoot
    }

    Invoke-Case 'budget-boundary' {
        $caseRoot = [System.IO.Path]::GetFullPath((Join-Path $script:tempRoot 'boundary'))
        $result = Invoke-DesktopSlotProvision -Root $caseRoot -Execute -FreeSpaceProvider (New-BytesProvider $script:defaultBudget)
        $text = Get-ResultText $result
        Assert-C0824 ($result.ExitCode -eq 0) 'budget-boundary.equal-exit' $text
        Assert-C0824 (Test-Path -LiteralPath (Join-Path $caseRoot 'worktrees')) 'budget-boundary.equal-created' $caseRoot
    }

    Invoke-Case 'disk-budget-parameter' {
        $lowRoot = [System.IO.Path]::GetFullPath((Join-Path $script:tempRoot 'param-low'))
        $low = Invoke-DesktopSlotProvision -Root $lowRoot -Execute -DiskBudgetGiB 1 -FreeSpaceProvider (New-BytesProvider ([int64]1))
        $lowText = Get-ResultText $low
        Assert-C0824 ($low.ExitCode -eq 2) 'disk-budget-parameter.low-exit' $lowText
        Assert-C0824 (-not (Test-Path -LiteralPath $lowRoot)) 'disk-budget-parameter.low-absent' $lowRoot
        $okRoot = [System.IO.Path]::GetFullPath((Join-Path $script:tempRoot 'param-ok'))
        $ok = Invoke-DesktopSlotProvision -Root $okRoot -Execute -DiskBudgetGiB 1 -FreeSpaceProvider (New-BytesProvider $script:oneGiB)
        $okText = Get-ResultText $ok
        $clone = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $okRoot 'repos') 'antiphon')).Replace('\', '/')
        Assert-C0824 ($ok.ExitCode -eq 0) 'disk-budget-parameter.ok-exit' $okText
        Assert-C0824 (Test-Path -LiteralPath (Join-Path $okRoot 'retained')) 'disk-budget-parameter.ok-created' $okRoot
        Assert-C0824 ($okText.Contains('budget-bytes=' + $script:oneGiB)) 'disk-budget-parameter.budget' $okText
        Assert-C0824 ($okText.Contains('next-command: git clone ' + $script:cloneUrl + ' ' + $clone)) 'disk-budget-parameter.clone-command' $okText
    }

    Invoke-Case 'remove-empty' {
        $caseRoot = [System.IO.Path]::GetFullPath((Join-Path $script:tempRoot 'remove'))
        $made = Invoke-DesktopSlotProvision -Root $caseRoot -Execute -FreeSpaceProvider (New-BytesProvider $script:ample)
        Assert-C0824 ($made.ExitCode -eq 0) 'remove-empty.provision' (Get-ResultText $made)
        $dry = Invoke-DesktopSlotProvision -Root $caseRoot -RemoveEmpty -FreeSpaceProvider (New-BytesProvider $script:ample)
        $dryText = Get-ResultText $dry
        Assert-C0824 ($dry.ExitCode -eq 0) 'remove-empty.dry-exit' $dryText
        Assert-C0824 ($dryText.Contains('mode=dry-run')) 'remove-empty.dry-mode' $dryText
        Assert-C0824 (Test-Path -LiteralPath (Join-Path $caseRoot 'worktrees')) 'remove-empty.dry-keeps' $caseRoot
        $gone = Invoke-DesktopSlotProvision -Root $caseRoot -Execute -RemoveEmpty -FreeSpaceProvider (New-BytesProvider $script:ample)
        $goneText = Get-ResultText $gone
        Assert-C0824 ($gone.ExitCode -eq 0) 'remove-empty.exit' $goneText
        foreach ($name in @('worktrees', 'workspace-slots', 'retained', 'repos')) {
            $dir = Join-Path $caseRoot $name
            Assert-C0824 (-not (Test-Path -LiteralPath $dir)) ('remove-empty.removed.' + $name) $dir
        }
    }

    Invoke-Case 'remove-empty-blocked' {
        $childRoot = [System.IO.Path]::GetFullPath((Join-Path $script:tempRoot 'remove-child'))
        $made = Invoke-DesktopSlotProvision -Root $childRoot -Execute -FreeSpaceProvider (New-BytesProvider $script:ample)
        Assert-C0824 ($made.ExitCode -eq 0) 'remove-empty-child.provision' (Get-ResultText $made)
        $leaf = Join-Path (Join-Path $childRoot 'worktrees') 'leaf.txt'
        Set-Content -LiteralPath $leaf -Value 'stay' -Encoding Ascii
        $blocked = Invoke-DesktopSlotProvision -Root $childRoot -Execute -RemoveEmpty -FreeSpaceProvider (New-BytesProvider $script:ample)
        $blockedText = Get-ResultText $blocked
        Assert-C0824 ($blocked.ExitCode -eq 2) 'remove-empty-child.exit' $blockedText
        Assert-C0824 ($blockedText.Contains('refused: remove-empty-blocked')) 'remove-empty-child.message' $blockedText
        Assert-C0824 (Test-Path -LiteralPath $leaf) 'remove-empty-child.kept' $leaf
        Assert-C0824 (Test-Path -LiteralPath (Join-Path $childRoot 'repos')) 'remove-empty-child.repos' $childRoot

        $ledgerRoot = [System.IO.Path]::GetFullPath((Join-Path $script:tempRoot 'remove-ledger'))
        $madeLedger = Invoke-DesktopSlotProvision -Root $ledgerRoot -Execute -FreeSpaceProvider (New-BytesProvider $script:ample)
        Assert-C0824 ($madeLedger.ExitCode -eq 0) 'remove-empty-ledger.provision' (Get-ResultText $madeLedger)
        $ledger = Join-Path (Join-Path $ledgerRoot 'workspace-slots') 'ledger.json'
        Set-Content -LiteralPath $ledger -Value '{}' -Encoding Ascii
        $ledgerBlocked = Invoke-DesktopSlotProvision -Root $ledgerRoot -Execute -RemoveEmpty -FreeSpaceProvider (New-BytesProvider $script:ample)
        $ledgerText = Get-ResultText $ledgerBlocked
        Assert-C0824 ($ledgerBlocked.ExitCode -eq 2) 'remove-empty-ledger.exit' $ledgerText
        Assert-C0824 (Test-Path -LiteralPath $ledger) 'remove-empty-ledger.kept' $ledger
        Assert-C0824 (Test-Path -LiteralPath (Join-Path $ledgerRoot 'retained')) 'remove-empty-ledger.retained' $ledgerRoot
    }

    Invoke-Case 'forbidden-parent' {
        foreach ($pair in @(
            @{ Name = 'antiphon'; Root = 'C:\Antiphon' },
            @{ Name = 'antiphon-slash'; Root = 'C:\Antiphon\' },
            @{ Name = 'antiphon-fwd'; Root = 'C:/ANTIPHON' },
            @{ Name = 'src'; Root = 'C:\src' },
            @{ Name = 'src-slash'; Root = 'C:\src\' },
            @{ Name = 'src-fwd'; Root = 'C:/src' },
            @{ Name = 'dotdot-worktrees'; Root = 'C:\Antiphon\worktrees\..' },
            @{ Name = 'dotdot-src'; Root = 'C:\src\Antiphon\..' }
        )) {
            $comparable = ConvertTo-SlotPath $pair.Root
            Assert-TextReason ('parent-' + $pair.Name) $pair.Root 'forbidden-root'
            Assert-C0824 (Test-ForbiddenSlotRoot $comparable) ('parent-' + $pair.Name + '.forbidden') $comparable
            Assert-EntryRefusal ('parent-' + $pair.Name + '.execute') $pair.Root 'forbidden-root' $false
            Assert-EntryRefusal ('parent-' + $pair.Name + '.remove') $pair.Root 'forbidden-root' $true
        }
        $slots = 'C:\Antiphon\slots'
        Assert-TextReason 'default-root' $slots ''
        Assert-C0824 (-not (Test-ForbiddenSlotRoot (ConvertTo-SlotPath $slots))) 'default-root.not-forbidden' $slots
    }

    Invoke-Case 'path-alias' {
        Write-Host 'UNOBSERVED C0824S2 windows-only: 8.3 expansion, NTFS junctions, Win32 trailing-dot collapse, powershell.exe 5.1'
        foreach ($pair in @(
            @{ Name = 'tilde'; Root = 'C:\src\ANTIPH~1\pool'; Reason = 'alias-segment'; Entry = $true },
            @{ Name = 'dot'; Root = 'C:\src\Antiphon.\pool'; Reason = 'alias-segment'; Entry = $true },
            @{ Name = 'releases-dot'; Root = 'C:\Antiphon\releases.\pool'; Reason = 'alias-segment'; Entry = $true },
            @{ Name = 'worktrees-dot'; Root = 'C:\Antiphon\worktrees.'; Reason = 'alias-segment'; Entry = $true },
            @{ Name = 'space'; Root = 'C:\src\Antiphon \pool'; Reason = 'alias-segment'; Entry = $true },
            @{ Name = 'unc'; Root = '\\server\share\releases\pool'; Reason = 'unsupported-root'; Entry = $false },
            @{ Name = 'unc-fwd'; Root = '//server/share/releases/pool'; Reason = 'unsupported-root'; Entry = $true },
            @{ Name = 'admin'; Root = '\\localhost\C$\src\Antiphon\pool'; Reason = 'unsupported-root'; Entry = $false },
            @{ Name = 'extended'; Root = '\\?\C:\Antiphon\releases\x'; Reason = 'unsupported-root'; Entry = $false },
            @{ Name = 'device'; Root = '\\.\C:\src\Antiphon'; Reason = 'unsupported-root'; Entry = $false }
        )) {
            Assert-TextReason ('alias-' + $pair.Name) $pair.Root $pair.Reason
            if ($pair.Entry) {
                Assert-EntryRefusal ('alias-' + $pair.Name + '.execute') $pair.Root $pair.Reason $false
                Assert-EntryRefusal ('alias-' + $pair.Name + '.remove') $pair.Root $pair.Reason $true
            }
        }
    }

    Invoke-Case 'reparse-ancestor' {
        $target = Join-Path $script:tempRoot 'reparse-target'
        $link = Join-Path $script:tempRoot 'reparse-link'
        New-Item -ItemType Directory -Path $target | Out-Null
        New-Item -ItemType SymbolicLink -Path $link -Target $target | Out-Null
        $caseRoot = Join-Path $link 'pool'
        $result = Invoke-DesktopSlotProvision -Root $caseRoot -Execute -FreeSpaceProvider (New-BytesProvider $script:ample)
        $text = Get-ResultText $result
        $kids = @(Get-ChildItem -LiteralPath $target -Force)
        Assert-C0824 ($result.ExitCode -eq 2) 'reparse.exit' $text
        Assert-C0824 ($text.Contains('refused: reparse-ancestor')) 'reparse.message' $text
        Assert-C0824 ($kids.Count -eq 0) 'reparse.target-empty' ([string]$kids.Count)
        Assert-C0824 (-not (Test-Path -LiteralPath $caseRoot)) 'reparse.pool-absent' $caseRoot
    }

    Invoke-Case 'clone-ceiling' {
        $outer = [System.IO.Path]::GetFullPath((Join-Path $script:tempRoot 'enclosing'))
        New-GitRepo $outer
        $before = Get-Longpaths $outer
        Assert-C0824 ($before.Value -eq 'false') 'clone-ceiling.before' $before.Value
        $clone = Join-Path (Join-Path $outer 'repos') 'antiphon'
        New-Item -ItemType Directory -Path (Join-Path $clone '.git') -Force | Out-Null
        $result = Invoke-DesktopSlotProvision -Root $outer -Execute -FreeSpaceProvider (New-BytesProvider $script:ample)
        $text = Get-ResultText $result
        $after = Get-Longpaths $outer
        Assert-C0824 ($result.ExitCode -eq 2) 'clone-ceiling.exit' $text
        Assert-C0824 ($text.Contains('refused: clone-unresolved')) 'clone-ceiling.message' $text
        Assert-C0824 ($after.Value -eq 'false') 'clone-ceiling.outer' $after.Value
        Assert-C0824 (-not $text.Contains('longpaths=true')) 'clone-ceiling.no-true' $text

        $outerFile = [System.IO.Path]::GetFullPath((Join-Path $script:tempRoot 'enclosing-gitfile'))
        New-GitRepo $outerFile
        $fileBefore = Get-Longpaths $outerFile
        Assert-C0824 ($fileBefore.Value -eq 'false') 'clone-gitfile.before' $fileBefore.Value
        $fileClone = Join-Path (Join-Path $outerFile 'repos') 'antiphon'
        New-Item -ItemType Directory -Path $fileClone -Force | Out-Null
        $gitFile = Join-Path $fileClone '.git'
        $pointer = 'gitdir: ' + (Join-Path $outerFile '.git')
        Set-Content -LiteralPath $gitFile -Value $pointer -Encoding Ascii
        $fileResult = Invoke-DesktopSlotProvision -Root $outerFile -Execute -FreeSpaceProvider (New-BytesProvider $script:ample)
        $fileText = Get-ResultText $fileResult
        $fileAfter = Get-Longpaths $outerFile
        Assert-C0824 ($fileResult.ExitCode -eq 2) 'clone-gitfile.exit' $fileText
        Assert-C0824 ($fileText.Contains('refused: clone-')) 'clone-gitfile.message' $fileText
        Assert-C0824 ($fileAfter.Value -eq 'false') 'clone-gitfile.outer' $fileAfter.Value
        Assert-C0824 (-not $fileText.Contains('longpaths=true')) 'clone-gitfile.no-true' $fileText
    }

    Invoke-Case 'remove-empty-race' {
        $caseRoot = [System.IO.Path]::GetFullPath((Join-Path $script:tempRoot 'race'))
        $made = Invoke-DesktopSlotProvision -Root $caseRoot -Execute -FreeSpaceProvider (New-BytesProvider $script:ample)
        Assert-C0824 ($made.ExitCode -eq 0) 'remove-empty-race.provision' (Get-ResultText $made)
        $child = Join-Path (Join-Path $caseRoot 'worktrees') 'racer.txt'
        $hook = {
            param($Path)
            $leaf = Join-Path $Path 'racer.txt'
            if ($Path.EndsWith('worktrees') -or $Path.EndsWith('worktrees\')) {
                if (-not (Test-Path -LiteralPath $leaf)) {
                    Set-Content -LiteralPath $leaf -Value 'stay' -Encoding Ascii
                }
            }
        }
        $threw = $false
        $result = $null
        try {
            $result = Invoke-DesktopSlotProvision -Root $caseRoot -Execute -RemoveEmpty -BeforeRemove $hook -FreeSpaceProvider (New-BytesProvider $script:ample)
        } catch {
            $threw = $true
            Assert-C0824 $false 'remove-empty-race.no-throw' $_.Exception.Message
        }
        if (-not $threw) {
            $text = Get-ResultText $result
            Assert-C0824 ($result.ExitCode -eq 2) 'remove-empty-race.exit' $text
            Assert-C0824 ($text.Contains('refused: remove-empty-blocked')) 'remove-empty-race.message' $text
        }
        Assert-C0824 (Test-Path -LiteralPath $child) 'remove-empty-race.child' $child
        Assert-C0824 (Test-Path -LiteralPath (Join-Path $caseRoot 'worktrees')) 'remove-empty-race.dir' $caseRoot
        Assert-C0824 (Test-Path -LiteralPath (Join-Path $caseRoot 'repos')) 'remove-empty-race.repos' $caseRoot
    }

    Invoke-Case 'ps51-ascii' {
        $bytes = [System.IO.File]::ReadAllBytes($repoScript)
        $nonAscii = -1
        for ($i = 0; $i -lt $bytes.Length; $i++) {
            if ($bytes[$i] -gt 127) { $nonAscii = $i; break }
        }
        Assert-C0824 ($nonAscii -eq -1) 'ascii' ([string]$nonAscii)
        $raw = [System.IO.File]::ReadAllText($repoScript)
        $banned = $false
        if ($raw.Contains('&&')) { $banned = $true }
        if ($raw.Contains('||')) { $banned = $true }
        if ($raw.Contains('??')) { $banned = $true }
        Assert-C0824 (-not $banned) 'no-ps7-operators' 'found && or || or ??'
        $tokens = $null
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($repoScript, [ref]$tokens, [ref]$errors)
        Assert-C0824 (@($errors).Count -eq 0) 'parse' ([string]@($errors).Count)
        $script:c0824Banned = $false
        [void]@($ast.FindAll({
            param($node)
            if ($node -is [System.Management.Automation.Language.TernaryExpressionAst]) { $script:c0824Banned = $true }
            if ($node -is [System.Management.Automation.Language.PipelineChainAst]) { $script:c0824Banned = $true }
            if ($node -is [System.Management.Automation.Language.MemberExpressionAst] -and $node.NullConditional) { $script:c0824Banned = $true }
            return $false
        }, $true))
        foreach ($token in @($tokens)) {
            $kind = [string]$token.Kind
            if ($kind -eq 'QuestionQuestion' -or $kind -eq 'QuestionQuestionEquals') { $script:c0824Banned = $true }
        }
        Assert-C0824 (-not $script:c0824Banned) 'ps51-ast' 'ternary or ?? or pipeline chain'
        $script:c0824Dotnet = $false
        $script:c0824Clone = $false
        $script:c0824Global = $false
        [void]@($ast.FindAll({
            param($node)
            if ($node -isnot [System.Management.Automation.Language.CommandAst]) { return $false }
            $commandName = $node.GetCommandName()
            if ([string]::IsNullOrEmpty($commandName)) { return $false }
            if ($commandName -eq 'dotnet') { $script:c0824Dotnet = $true }
            if ($commandName -eq 'git') {
                $extent = $node.Extent.Text
                if ($extent -match '(?i)\bclone\b') { $script:c0824Clone = $true }
                if ($extent -match '(?i)--global') { $script:c0824Global = $true }
            }
            return $false
        }, $true))
        Assert-C0824 (-not $script:c0824Dotnet) 'no-dotnet-command' 'dotnet command in script'
        Assert-C0824 (-not $script:c0824Clone) 'no-git-clone-command' 'git clone command in script'
        Assert-C0824 (-not $script:c0824Global) 'no-git-global' 'git --global in script'
    }

    Invoke-Case 'root-unavailable' {
        $driveRoot = 'C:\'
        if (Test-Path -LiteralPath $driveRoot) {
            Write-Host 'SKIP C0824S2 root-unavailable host-has-drive'
        } else {
            $caseRoot = 'C:\Antiphon\slots'
            $cwd = [System.IO.Path]::GetFullPath((Get-Location).Path)
            $result = Invoke-DesktopSlotProvision -Root $caseRoot -Execute -FreeSpaceProvider (New-BytesProvider $script:ample)
            $text = Get-ResultText $result
            $mangled = Join-Path $cwd 'C:\Antiphon\slots'
            Assert-C0824 ($result.ExitCode -eq 2) 'root-unavailable.exit' $text
            Assert-C0824 ($text.Contains('refused: root-unavailable')) 'root-unavailable.message' $text
            Assert-C0824 (-not (Test-Path -LiteralPath $mangled)) 'root-unavailable.not-created' $mangled
        }
    }

    $canaryText = ''
    if (Test-Path -LiteralPath $script:canary) { $canaryText = (Get-Content -LiteralPath $script:canary -Raw).Trim() }
    Assert-C0824 ($canaryText -eq 'keep') 'canary' $canaryText
} catch {
    Assert-C0824 $false 'unhandled' $_.Exception.Message
}

Write-Host ('C0824S2 passed=' + $script:passed + ' failed=' + $script:failed)
if (Test-Path -LiteralPath $script:tempRoot) {
    Remove-Item -LiteralPath $script:tempRoot -Recurse -Force
}
if ($script:failed -gt 0) { exit 1 }
exit 0
