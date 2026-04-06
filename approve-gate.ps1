$WorkflowId = '949ca541-4169-4ea2-96a5-0468f69f629b'
try {
    $r = Invoke-RestMethod -Uri "http://localhost:5000/api/workflows/$WorkflowId/gates/approve" -Method POST -Body '{"feedback":"Party mode analysis looks comprehensive. Proceed to finalize."}' -ContentType 'application/json'
    Write-Host "Gate approved successfully"
} catch {
    Write-Host "Error: $($_.Exception.Message)"
    Write-Host $_.ErrorDetails.Message
}
