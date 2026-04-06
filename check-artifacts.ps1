$WorkflowId = '949ca541-4169-4ea2-96a5-0468f69f629b'
$worktree = Join-Path $env:TEMP "antiphon-worktrees\$WorkflowId"

Write-Host "=== Worktree: $worktree ==="
if (Test-Path $worktree) {
    Get-ChildItem $worktree -Recurse -File | Select-Object @{N='Path';E={$_.FullName.Replace($worktree, '.')}} | Format-Table -AutoSize

    # Show git status and log
    Write-Host ""
    Write-Host "=== Git Status ==="
    $gitStatus = & git -C $worktree status 2>&1
    Write-Host $gitStatus

    Write-Host ""
    Write-Host "=== Git Log ==="
    $gitLog = & git -C $worktree log --oneline -20 2>&1
    Write-Host $gitLog

    Write-Host ""
    Write-Host "=== Git Remote ==="
    $gitRemote = & git -C $worktree remote -v 2>&1
    Write-Host $gitRemote

    Write-Host ""
    Write-Host "=== Git Branches ==="
    $gitBranches = & git -C $worktree branch -a 2>&1
    Write-Host $gitBranches
} else {
    Write-Host "Worktree not found at $worktree"
    Write-Host ""
    Write-Host "Searching for worktrees..."
    $tempDir = $env:TEMP
    Get-ChildItem (Join-Path $tempDir "antiphon-worktrees") -ErrorAction SilentlyContinue | Format-Table -AutoSize
}
