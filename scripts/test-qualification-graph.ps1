#requires -Version 5.1
param([ValidateSet('SchemaAndIdentity')][string]$Case, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/lib/c487-harness.ps1"
. "$PSScriptRoot/lib/qualification-graph.ps1"
$schema = Join-Path $PSScriptRoot '../docs/schemas/qualification-run-plan-v1.schema.json'
$fixtures = Join-Path $PSScriptRoot 'fixtures/qualification-graph/s1'
$json = [IO.File]::ReadAllText((Join-Path $fixtures 'execute.valid.json'))
$valid = Test-QualificationGraphSchema $json $schema
$graph = $json | ConvertFrom-Json
$graph.nodes[0].PSObject.Properties.Remove('platformOfRecord')
$accepted = Test-QualificationGraphSchema ($graph | ConvertTo-Json -Depth 30) $schema
Assert-C487 ($valid -and -not $accepted) 'QGRAPH SchemaAndIdentity/missing-platform' 'Direct schema accepted a node without platformOfRecord'
Write-Host "C487: $script:C487Passed passed, $script:C487Failed failed, $script:C487Rows rows"
Write-Host "C487 HARNESS EXIT CODE: $([int]($script:C487Failed -gt 0))"
exit ([int]($script:C487Failed -gt 0))
