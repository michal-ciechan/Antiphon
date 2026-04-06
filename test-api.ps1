try {
    $r = Invoke-RestMethod -Uri 'http://localhost:5000/api/workflows' -Method GET
    Write-Host "GET workflows OK: $($r.Count) workflows"
} catch {
    Write-Host "GET failed: $($_.Exception.Message)"
    Write-Host $_.ErrorDetails.Message
}
