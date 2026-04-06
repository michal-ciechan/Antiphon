param([string]$WorkflowId = '949ca541-4169-4ea2-96a5-0468f69f629b')

$baseUrl = 'http://localhost:5000'
$pollInterval = 15

Write-Host "Monitoring workflow $WorkflowId ..."

while ($true) {
    try {
        $w = Invoke-RestMethod -Uri "$baseUrl/api/workflows/$WorkflowId" -Method GET
        $status = $w.status
        $stage = if ($w.currentStageName) { $w.currentStageName } else { '(none)' }
        $completed = $w.completedStages
        $total = $w.totalStages
        Write-Host "[$((Get-Date).ToString('HH:mm:ss'))] Status=$status  Stage=$stage  Progress=$completed/$total"

        if ($status -eq 'GateWaiting') {
            Write-Host "  --> Gate waiting! Approving..."
            $approveBody = '{"feedback":"Looks good, proceed."}'
            Invoke-RestMethod -Uri "$baseUrl/api/workflows/$WorkflowId/gates/approve" -Method POST -Body $approveBody -ContentType 'application/json' | Out-Null
            Write-Host "  --> Gate approved."
        }
        elseif ($status -eq 'Completed') {
            Write-Host "  --> Workflow COMPLETED!"
            Write-Host ""
            Write-Host "Checking for artifacts..."

            # Check temp worktree
            $worktree = Join-Path $env:TEMP "antiphon-worktrees\$WorkflowId"
            if (Test-Path $worktree) {
                Write-Host "Worktree: $worktree"
                Get-ChildItem $worktree -Recurse -File | Select-Object FullName | Format-Table -AutoSize
            }
            break
        }
        elseif ($status -eq 'Failed') {
            Write-Host "  --> Workflow FAILED"
            break
        }
        elseif ($status -eq 'Abandoned') {
            Write-Host "  --> Workflow ABANDONED"
            break
        }
    } catch {
        Write-Host "  [Error polling] $($_.Exception.Message)"
    }

    Start-Sleep -Seconds $pollInterval
}
