#requires -Version 5.1
# Syntax is portable; the local Test-Json schema engine requires PowerShell 7.
function Test-QualificationGraphSchema {
    param([string]$Json, [string]$SchemaPath)
    return Test-Json -Json $Json -SchemaFile $SchemaPath -ErrorAction SilentlyContinue
}
