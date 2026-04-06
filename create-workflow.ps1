$body = @{
    templateId = 'b0000000-0000-0000-0000-000000000003'
    projectId = '6f2d4495-2a2e-433a-8880-220b11d0090d'
    featureName = 'mav-ref-documentation'
    initialContext = @'
Document the mav-ref project thoroughly using the following steps:

STEP 1 - Clone the repository:
Use the git tool: git clone https://git.mavensecurities.com/Maven/mav-ref.git .
(This clones into the current working directory)

STEP 2 - Explore the codebase:
Use glob and grep tools to discover all files, understand the structure, find entry points, configuration, and key source files.

STEP 3 - Analyze from PARTY MODE - 3 simultaneous perspectives:
  Perspective A (New Developer): What do I need to get started? How do I run this? What are the key files?
  Perspective B (Architect): What are the design patterns, dependencies, data flows, and architectural decisions?
  Perspective C (Integration Engineer): How do other services integrate with this? What APIs/events does it expose?

STEP 4 - Write documentation files:
Use the file_write tool to create DOCUMENTATION.md and INTEGRATION_GUIDE.md in the worktree root.

STEP 5 - Create a feature branch and commit:
Use git tool:
  git config user.email "antiphon@mavensecurities.com"
  git config user.name "Antiphon Agent"
  git checkout -b feature/antiphon-documentation
  git add DOCUMENTATION.md INTEGRATION_GUIDE.md
  git commit -m "docs: Add DOCUMENTATION.md and INTEGRATION_GUIDE.md via Antiphon"
  git push origin feature/antiphon-documentation

You MUST use the tools (git, glob, grep, file_write) to complete these steps. Do not just describe what you would do - actually DO it using the tools.
'@
    stageModelOverrides = @{
        'analyze-codebase' = 'gpt-4o'
        'finalize-documentation' = 'gpt-4o'
    }
} | ConvertTo-Json -Depth 5

Write-Host "Sending body:"
Write-Host $body

try {
    $response = Invoke-RestMethod -Uri 'http://localhost:5000/api/workflows' -Method POST -Body $body -ContentType 'application/json'
    Write-Host "SUCCESS - Created workflow: $($response.id)"
    $response | ConvertTo-Json
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Write-Host "Error $statusCode : $($_.Exception.Message)"
    Write-Host "Details: $($_.ErrorDetails.Message)"

    $stream = $_.Exception.Response.GetResponseStream()
    $reader = New-Object System.IO.StreamReader($stream)
    $responseBody = $reader.ReadToEnd()
    Write-Host "Body: $responseBody"
}
