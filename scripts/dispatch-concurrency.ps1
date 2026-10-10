# Read and replace durable dispatch-concurrency policy without composing HTTP by hand.
#
# ASCII-only on purpose: agent/ops scripts must parse under Windows PowerShell 5.1, which reads a
# no-BOM .ps1 as CP1252 and mangles non-ASCII characters.
#
# Verbs:
#   dispatch-concurrency.ps1 get [-Project <guid>]
#   dispatch-concurrency.ps1 history [-Project <guid>] [-BeforeRevision n] [-Limit n]
#   dispatch-concurrency.ps1 set [-Project <guid>] (-SettingsFile path | -Settings '{...}')
#       -Reason text | -ReasonFile path [-Provenance Human|Auto]
#       [-ExpectedRevision n] [-ExpectedGlobalRevision n]
#   dispatch-concurrency.ps1 clear [-Project <guid>] (-Reason text | -ReasonFile path)
#       [-Provenance Human|Auto] [-ExpectedRevision n] [-ExpectedGlobalRevision n]
#
# set and clear GET the current revisions, then send one PUT. An explicit revision is sent as
# given and is never replaced with the GET value. A 409 is shown and not retried. Clear sends
# an empty overrides object, not a copy of the current policy.
#
# The task token is a header only. It is never written to stdout or stderr.
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('get', 'history', 'set', 'clear')]
    [string]$Verb = 'get',

    [string]$Project,

    [string]$SettingsFile,

    [string]$Settings,

    [string]$Reason,

    [string]$ReasonFile,

    [ValidateSet('Human', 'Auto')]
    [string]$Provenance = 'Human',

    [string]$ExpectedRevision,

    [string]$ExpectedGlobalRevision,

    [string]$BeforeRevision,

    [string]$Limit
)

$ErrorActionPreference = 'Stop'

function Fail-Local {
    param([string]$Message)
    Write-Error $Message
    exit 1
}

function Test-GuidText {
    param([string]$Value)
    $parsed = [guid]::Empty
    return [guid]::TryParse($Value, [ref]$parsed)
}

function Read-Utf8File {
    param([string]$Path, [string]$Label)
    if ([string]::IsNullOrWhiteSpace($Path)) { Fail-Local "$Label is required." }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Fail-Local "$Label was not found: $Path" }
    return [System.IO.File]::ReadAllText($Path, [System.Text.UTF8Encoding]::new($false))
}

$hasSettings = -not [string]::IsNullOrWhiteSpace($Settings)
$hasSettingsFile = -not [string]::IsNullOrWhiteSpace($SettingsFile)
$hasReason = -not [string]::IsNullOrWhiteSpace($Reason)
$hasReasonFile = -not [string]::IsNullOrWhiteSpace($ReasonFile)
$hasProject = -not [string]::IsNullOrWhiteSpace($Project)

if ($hasProject -and -not (Test-GuidText $Project)) {
    Fail-Local 'project must be a GUID.'
}
if ($hasSettings -and $hasSettingsFile) {
    Fail-Local '-Settings and -SettingsFile are mutually exclusive.'
}
if ($hasReason -and $hasReasonFile) {
    Fail-Local '-Reason and -ReasonFile are mutually exclusive.'
}

$isWrite = $Verb -eq 'set' -or $Verb -eq 'clear'
if (-not $isWrite -and ($hasSettings -or $hasSettingsFile -or $hasReason -or $hasReasonFile -or $ExpectedRevision -or $ExpectedGlobalRevision)) {
    Fail-Local "$Verb does not accept settings, reason, or revision arguments."
}
if ($Verb -eq 'set' -and -not ($hasSettings -xor $hasSettingsFile)) {
    Fail-Local 'set needs -Settings or -SettingsFile, and not both.'
}
if ($Verb -eq 'clear' -and ($hasSettings -or $hasSettingsFile)) {
    Fail-Local 'clear does not take a settings document. It sends an empty overrides object.'
}
if ($isWrite -and -not ($hasReason -xor $hasReasonFile)) {
    Fail-Local "$Verb needs -Reason or -ReasonFile, and not both."
}
if ($Verb -ne 'history' -and ($BeforeRevision -or $Limit)) {
    Fail-Local '-BeforeRevision and -Limit belong to history.'
}

$overridesRaw = $null
if ($Verb -eq 'set') {
    $overridesRaw = if ($hasSettingsFile) { Read-Utf8File $SettingsFile 'settings file' } else { $Settings }
    try { $null = $overridesRaw | ConvertFrom-Json -ErrorAction Stop }
    catch { Fail-Local 'settings are not valid JSON.' }
}
elseif ($Verb -eq 'clear') {
    $overridesRaw = '{}'
}

$reasonText = $null
if ($isWrite) {
    $reasonText = if ($hasReasonFile) { Read-Utf8File $ReasonFile 'reason file' } else { $Reason }
}

$api = $env:ANTIPHON_API
if ([string]::IsNullOrWhiteSpace($api)) { $api = 'http://localhost:17202' }
$api = $api.TrimEnd('/')

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($env:ANTIPHON_TASK_TOKEN)) {
    $headers['X-Antiphon-Task-Token'] = $env:ANTIPHON_TASK_TOKEN
}

function Get-ScopePath {
    if ($hasProject) { return "/api/projects/$Project/dispatch-concurrency" }
    return '/api/dispatch-concurrency'
}

function Invoke-Api {
    param([string]$Method, [string]$Path, [string]$Body)
    $uri = "$api$Path"
    try {
        if ($null -ne $Body) {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($Body)
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -Body $bytes -ContentType 'application/json; charset=utf-8'
        }
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
    }
    catch {
        $raw = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($raw)) { $raw = $_.Exception.Message }
        Write-Error ("Antiphon {0} {1} failed: {2}" -f $Method, $Path, $raw)
        exit 1
    }
}

function Write-Json {
    param($Value)
    Write-Output ($Value | ConvertTo-Json -Depth 30 -Compress)
}

function ConvertTo-AuditBody {
    param([long]$Revision, [Nullable[long]]$GlobalRevision, [string]$Overrides, [string]$ReasonValue, [string]$ProvenanceValue)
    $reasonJson = ConvertTo-Json -InputObject $ReasonValue -Compress
    $provenanceJson = ConvertTo-Json -InputObject $ProvenanceValue -Compress
    $wrapped = '{"expectedRevision":' + $Revision + ',"overrides":' + $Overrides.Trim() + ',"reason":' + $reasonJson + ',"provenance":' + $provenanceJson + '}'
    if ($null -eq $GlobalRevision) { return $wrapped }
    return '{"expectedRevision":' + $Revision + ',"expectedGlobalRevision":' + $GlobalRevision + ',"overrides":' + $Overrides.Trim() + ',"reason":' + $reasonJson + ',"provenance":' + $provenanceJson + '}'
}

$scopePath = Get-ScopePath
if ($Verb -eq 'get') {
    Write-Json (Invoke-Api 'GET' $scopePath $null)
    exit 0
}

if ($Verb -eq 'history') {
    $query = @()
    if (-not [string]::IsNullOrWhiteSpace($Limit)) { $query += ('limit=' + [uri]::EscapeDataString($Limit)) }
    if (-not [string]::IsNullOrWhiteSpace($BeforeRevision)) { $query += ('beforeRevision=' + [uri]::EscapeDataString($BeforeRevision)) }
    $path = "$scopePath/revisions"
    if ($query.Count -gt 0) { $path = $path + '?' + ($query -join '&') }
    Write-Json (Invoke-Api 'GET' $path $null)
    exit 0
}

$current = Invoke-Api 'GET' $scopePath $null
$revision = $current.revision
if (-not [string]::IsNullOrWhiteSpace($ExpectedRevision)) {
    $explicit = 0L
    if (-not [long]::TryParse($ExpectedRevision, [ref]$explicit)) { Fail-Local 'expectedRevision must be an integer.' }
    $revision = $explicit
}
$globalRevision = $null
if ($hasProject) {
    $globalRevision = [long]$current.globalRevision
    if (-not [string]::IsNullOrWhiteSpace($ExpectedGlobalRevision)) {
        $explicitGlobal = 0L
        if (-not [long]::TryParse($ExpectedGlobalRevision, [ref]$explicitGlobal)) { Fail-Local 'expectedGlobalRevision must be an integer.' }
        $globalRevision = $explicitGlobal
    }
}
elseif (-not [string]::IsNullOrWhiteSpace($ExpectedGlobalRevision)) {
    Fail-Local 'expectedGlobalRevision is only valid with -Project.'
}

$body = ConvertTo-AuditBody -Revision $revision -GlobalRevision $globalRevision -Overrides $overridesRaw -ReasonValue $reasonText -ProvenanceValue $Provenance
Write-Json (Invoke-Api 'PUT' $scopePath $body)
exit 0
