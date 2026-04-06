$WorkflowId = '949ca541-4169-4ea2-96a5-0468f69f629b'
$w = Invoke-RestMethod -Uri "http://localhost:5000/api/workflows/$WorkflowId" -Method GET
$w | ConvertTo-Json -Depth 5
