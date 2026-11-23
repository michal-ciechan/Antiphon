# ASCII-only. Registered X-Antiphon-Caller labels (CARD-2004).
# Match is trim then lowercase, the same rule as AgentTaskGetPollMetrics.RegisteredLabels.
# An unregistered value is refused. It is not copied into the header or the error.
# 'card.ps1' and 'status-snapshot.ps1' are sent by the card and status-snapshot commands (Antiphon.Nightly.AntiphonCaller),
# which kept the labels of the scripts they replaced.

$script:AntiphonRegisteredCallers = @(
    'board-watch',
    'card.ps1',
    'checkpoint-task.ps1',
    'delegate.ps1',
    'status-snapshot.ps1',
    'useagenttask'
)

function Add-AntiphonCallerHeader {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Label
    )
    $text = ''
    if ($null -ne $Label) { $text = $Label.Trim().ToLowerInvariant() }
    $ok = $false
    if ($text.Length -gt 0 -and $text.Length -le 256) {
        foreach ($name in $script:AntiphonRegisteredCallers) {
            if ($name -ceq $text) { $ok = $true; break }
        }
    }
    if (-not $ok) {
        throw 'X-Antiphon-Caller is not a registered label.'
    }
    $Headers['X-Antiphon-Caller'] = $text
}
