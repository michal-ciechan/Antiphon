# Mechanical positive-control contract

Contract version: 1

This document is the owner for a mechanical positive control. It is a repository instruction contract. It is not a service, a task role, a parser, or an admission API. The fenced snippets below are the checks a worker runs. Prose around them does not give the worker a choice.

A design recipe is not a bound execution pack. When the final bytes do not exist, the recipe says `binding required after Code`. A line number is a navigation hint, never patch authority. Code may check in candidate payloads under `docs/superpowers/mutations/<card-identifier>/`. Candidates stay unbound: they do not embed the commit SHA that contains them, and a candidate hash computed at reviewed C is not eligibility at landed L.

The binder, after confirmed operation O and L=`O.VerifiedSourceSha`, publishes the pack outside the snapshot at `<verification-root>/packs/<revision>/`. The index lists each member path and SHA-256. The pack digest is the SHA-256 of the index bytes, stored beside the index, not inside it. Never overwrite a published revision. Later remote tip R does not substitute for L. A different raw checkout preimage, including CRLF versus LF, requires rebinding.

Low executes only a pack already classified `mechanically-eligible`. `binding`, `higher-tier-analysis`, and any `higher-tier-required` row stay off Low. Low never locates sites, authors diffs, or discovers controls.

## Identity and classification

Contract version; original and verification card GUIDs; Code and binding task IDs; O, reviewed C, and L; plan and recipe revision; pack id, revision, and detached digest; every PC and variant id mapped to its guard. Each row is `mechanically-eligible` or `higher-tier-required` with the reason names below and the owning next action. Execution status is separate from eligibility.

## Exact mutation

For each variant: a unified diff or a byte-preserving before/after replacement; payload SHA-256; repository-relative paths using `/`; target symbol and advisory line range at L; unique context; expected match count (normally 1); the exact allowed changed-file set; Git blob ids plus preimage and predicted mutant byte SHA-256 for each file. No wildcard discovery, fuzzy hunks, three-way apply, whitespace relaxation, regex mutation, or an inferred adjacent edit. Expected match count greater than one lists every intended context. The worker does not choose among matches.

Never use `git apply --3way`, `git checkout .`, or a whole-worktree reset.

## Resolved execution manifest

Working directory relative to the snapshot root, and the actual directory recorded for the run. Test project path, executable, argument vector, and the copyable command for every build, test, and restore phase. SDK, configuration, and target framework. Exact methods and filters. Expanded argument cases and outcome counts. Prerequisite commands and expected responses. Environment values or approved secret-reference names, never credential bytes. Output paths, schedules, repetition counts, and resource ownership.

Phase transport is the checkpoint driver. Driver exit semantics: exit 0 green, exit 1 named red, exit 2 build or TRX invalid (`prerequisite-missing`), exit 3 roster or zero (`selector-unresolved`). Read `CHECKPOINT`, `EXECUTED`, and `FAILED` lines. `--list-tests` is not execution evidence.

## Decisive oracle

Per negative case: method and argument identity, assertion site, and the exact expected and defect-induced values or stable error fields. Baseline, mutant, and restored outcome for every selected case, including green companions. Expected exit, executed, failed, passed, and skipped counts, and the exact acceptable failure set. An extra failure is `unexpected-red` even when the named assertion also fails.

## Reachability

Guard and entry path. Every earlier or parallel guard, cache, fixture default, feature flag, reservation, backend capability, assertion order, or schedule that could mask the defect, and how each is neutralized without dropping unrelated safeguards. Equivalent-mutant risk, and why the mutation changes the protected behaviour. A sentence that says the mutant should reach the guard is not a record.

## Restoration and evidence

Task-owned backup outside source, with hashes, taken only after the live bytes match the bound preimage. Allowed mutation state after each step. Forced rebuild and loaded output identity. Fresh baseline, mutant, and restored directories. Command, exit, result, and trace inventory. Final HEAD, index, and preimage checks. Interrupted-run recovery. Do not overwrite an unexplained edit.

Per-PC evidence uses a new attempt directory: `baseline/`, `mutant/`, and `restored/` under that attempt. Preserve the pack digest and the resolved-manifest digest. Repeating a phase creates a new attempt directory. An existing attempt directory refuses. Do not overwrite a failed phase or reuse an old TRX.

## Tokens

Instantiation may substitute only these tokens: `snapshotRoot`, `evidenceRoot`, `taskId`, `runId`, `pcId`, `phase`. Repository paths in the pack use `/`. Expanded Windows filesystem paths use backslashes. An alternate `OutputPath` argument still ends in a forward slash. Unknown tokens, a missing value, an empty results path, or a path containing `..` refuse before any attempt directory is created. Selecting a project, a filter, a symbol, or an environment value is not instantiation.

## Reasons

| Reason | Disposition |
|---|---|
| `binding-missing` | Bind the missing recipe or digest at the actual O/L. |
| `source-mismatch` | HEAD is not L. Do not substitute another SHA. |
| `artifact-mismatch` | Index digest, member hash, or external backend bytes differ. |
| `patch-unresolved` | The literal payload or context is not resolved at L. |
| `patch-mismatch` | Preimage, match count, or changed-file set disagrees. Restore any partial write. |
| `selector-unresolved` | Driver exit 3, or the executed roster is not established. |
| `selector-drift` | Zero, missing, extra, or duplicate rows, or skipped greater than zero. |
| `oracle-unresolved` | The decisive assertion or companion outcomes are unspecified. |
| `unexpected-red` | The failure set is not the declared set. |
| `reachability-unproven` | Masking or schedule evidence is incomplete. |
| `surviving-mutant` | The mutant did not fail the declared oracle. Diagnose; do not repair the test here. |
| `backend-race-unproven` | Real isolated backend, barriers, and process identity are not established. |
| `prerequisite-missing` | Driver exit 2, or the environment or build setup is absent. |
| `baseline-red` | The baseline is already red. Do not apply a mutant. |
| `stale-output` | Results were reused, build provenance is unknown, or the restored output identity equals the mutant output. |
| `restore-mismatch` | A mutated path does not match the recorded mutant bytes. Leave it in place and report the path. |
| `restored-red` | The restored run is not green. Stop. |
| `adequacy-gap` | A missing control. Design it in separate TestDesign or Code work. |

## Cycle

One variant at a time unless the binder supplied independence evidence. Every command is foreground-owned.

1. Preflight. Confirm O, HEAD=L, pack and index digests, clean tracked source and index, and preimage hashes. Record the backup. Failure stops the variant.
2. Baseline. Forced build and the exact cases in a fresh directory. A pre-existing red, skip, or zero selection is not permission to mutate.
3. Mutant. Check context and preimage with no write. Apply only the payload. Check the changed-file set and predicted hashes. Forced rebuild. The case and failure sets must match.
4. Restore. Runs after failure too. Replace only paths whose current hash equals the recorded mutant hash. Restored bytes must equal the preimage. An unexplained edit is `restore-mismatch`: do not overwrite it.
5. Restored run. Forced rebuild of the affected graph, then the same cases in a fresh directory. Output identity must differ from the mutant phase (`buildSource=verified`).
6. Settle. HEAD=L, clean index, preimage hashes, and owned outputs. Never commit, push, land, or deploy from the snapshot.

## Snippet invocation

Each fenced block is a standalone script. Its first line is the label. The harness writes the block to a temporary `.ps1` and invokes it with the named parameters. Refusal prints one `C479 REFUSE <reason>` line and exits 2. Success prints `C479 ACCEPT` and exits 0. Refusal does not leave a partial mutant in place, except `restore-mismatch`, which leaves the unexplained bytes and names the path.

`index.json` is `{ "members": [ { "path": "src/a.txt", "sha256": "<lower hex>" } ] }`. `index.sha256` is the detached SHA-256 of those exact index bytes. `apply.json` is `{ "allowedFiles": ["src/a.txt"], "closure": [{ "path": "fixtures/config.txt", "sha256": "<hex>" }], "edits": [{ "path": "src/a.txt", "preimageSha256": "<hex>", "mutantSha256": "<hex>", "context": "BEFORE", "replacement": "AFTER", "expectedMatches": 1 }] }`. `oracle.json` carries `driverExit`, `buildSource`, `mutantOutputHash`, `restoredOutputHash`, `skipped`, `resultsPreexisted`, `executed`, `failed`, `expectedExecuted`, and `expectedFailed`. `restore.json` lists `{ "path", "preimageSha256", "mutantSha256" }` and reads backups from `evidenceRoot/backup/<path>`. A restored phase also requires `evidenceRoot/forced-rebuild.marker`.

```powershell
# mechanical-pc-contract v1: preflight
param(
  [Parameter(Mandatory = $true)][string]$SnapshotRoot,
  [Parameter(Mandatory = $true)][string]$PackRoot,
  [Parameter(Mandatory = $true)][string]$L
)
$ErrorActionPreference = 'Stop'
function Refuse([string]$Reason) { Write-Output "C479 REFUSE $Reason"; exit 2 }
$head = (& git -C $SnapshotRoot rev-parse HEAD).Trim()
if ($head -ne $L) { Refuse 'source-mismatch' }
$digestPath = Join-Path $PackRoot 'index.sha256'
if (-not (Test-Path -LiteralPath $digestPath)) { Refuse 'binding-missing' }
$indexPath = Join-Path $PackRoot 'index.json'
$indexBytes = [System.IO.File]::ReadAllBytes($indexPath)
$sha = [System.Security.Cryptography.SHA256]::Create()
$actual = ([System.BitConverter]::ToString($sha.ComputeHash($indexBytes))).Replace('-', '').ToLowerInvariant()
$expectedDigest = (Get-Content -LiteralPath $digestPath -Raw).Trim().ToLowerInvariant()
if ($actual -ne $expectedDigest) { Refuse 'artifact-mismatch' }
$index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
foreach ($member in @($index.members)) {
  $relative = ([string]$member.path).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
  $full = Join-Path $SnapshotRoot $relative
  $hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($hash -ne ([string]$member.sha256).ToLowerInvariant()) { Refuse 'artifact-mismatch' }
}
Write-Output 'C479 ACCEPT'
exit 0
```

```powershell
# mechanical-pc-contract v1: apply
param(
  [Parameter(Mandatory = $true)][string]$SnapshotRoot,
  [Parameter(Mandatory = $true)][string]$PackRoot
)
$ErrorActionPreference = 'Stop'
function Refuse([string]$Reason) { Write-Output "C479 REFUSE $Reason"; exit 2 }
function FullPath([string]$Relative) {
  $rel = $Relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
  return (Join-Path $SnapshotRoot $rel)
}
$spec = Get-Content -LiteralPath (Join-Path $PackRoot 'apply.json') -Raw | ConvertFrom-Json
$saved = @{}
foreach ($item in @($spec.closure)) {
  $full = FullPath ([string]$item.path)
  $hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($hash -ne ([string]$item.sha256).ToLowerInvariant()) { Refuse 'artifact-mismatch' }
}
foreach ($edit in @($spec.edits)) {
  $full = FullPath ([string]$edit.path)
  $live = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($live -ne ([string]$edit.preimageSha256).ToLowerInvariant()) {
    foreach ($path in @($saved.Keys)) { [System.IO.File]::WriteAllBytes($path, $saved[$path]) }
    Refuse 'patch-mismatch'
  }
  $text = [System.IO.File]::ReadAllText($full)
  $expectedMatches = [int]$edit.expectedMatches
  $found = ([regex]::Matches($text, [regex]::Escape([string]$edit.context))).Count
  if ($found -ne $expectedMatches) {
    foreach ($path in @($saved.Keys)) { [System.IO.File]::WriteAllBytes($path, $saved[$path]) }
    Refuse 'patch-mismatch'
  }
  $saved[$full] = [System.IO.File]::ReadAllBytes($full)
  $updated = $text.Replace([string]$edit.context, [string]$edit.replacement)
  [System.IO.File]::WriteAllText($full, $updated)
  $mutant = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($mutant -ne ([string]$edit.mutantSha256).ToLowerInvariant()) {
    foreach ($path in @($saved.Keys)) { [System.IO.File]::WriteAllBytes($path, $saved[$path]) }
    Refuse 'patch-mismatch'
  }
}
$allowed = @($spec.allowedFiles | ForEach-Object { ([string]$_).Replace('\', '/') })
$changed = @($saved.Keys | ForEach-Object {
  $full = $_
  $rel = $full.Substring($SnapshotRoot.TrimEnd('\','/').Length).TrimStart('\','/').Replace('\', '/')
  $rel
})
$cmp = Compare-Object @($changed) @($allowed)
if ($null -ne $cmp) {
  foreach ($path in @($saved.Keys)) { [System.IO.File]::WriteAllBytes($path, $saved[$path]) }
  Refuse 'patch-mismatch'
}
Write-Output 'C479 ACCEPT'
exit 0
```

```powershell
# mechanical-pc-contract v1: oracle
param(
  [Parameter(Mandatory = $true)][string]$PackRoot
)
$ErrorActionPreference = 'Stop'
function Refuse([string]$Reason) { Write-Output "C479 REFUSE $Reason"; exit 2 }
function SameSet($Left, $Right) {
  $l = @($Left)
  $r = @($Right)
  if ($l.Count -ne $r.Count) { return $false }
  return $null -eq (Compare-Object $l $r)
}
$obs = Get-Content -LiteralPath (Join-Path $PackRoot 'oracle.json') -Raw | ConvertFrom-Json
if ([bool]$obs.resultsPreexisted) { Refuse 'stale-output' }
if ([string]$obs.buildSource -eq 'unknown') { Refuse 'stale-output' }
$mutantOutputHash = [string]$obs.mutantOutputHash
$restoredOutputHash = [string]$obs.restoredOutputHash
# restoredOutputHash -ne mutantOutputHash
if (-not ($restoredOutputHash -ne $mutantOutputHash)) { Refuse 'stale-output' }
$driverExit = [int]$obs.driverExit
if ($driverExit -eq 2) { Refuse 'prerequisite-missing' }
if ($driverExit -eq 3) { Refuse 'selector-unresolved' }
if ([int]$obs.skipped -gt 0) { Refuse 'selector-drift' }
$executed = @($obs.executed)
if ($executed.Count -eq 0) { Refuse 'selector-drift' }
if ((@($executed | Select-Object -Unique)).Count -ne $executed.Count) { Refuse 'selector-drift' }
if (-not (SameSet $executed @($obs.expectedExecuted))) { Refuse 'selector-drift' }
$failed = @($obs.failed)
if (-not (SameSet $failed @($obs.expectedFailed))) { Refuse 'unexpected-red' }
Write-Output 'C479 ACCEPT'
exit 0
```

```powershell
# mechanical-pc-contract v1: restore
param(
  [Parameter(Mandatory = $true)][string]$SnapshotRoot,
  [Parameter(Mandatory = $true)][string]$EvidenceRoot,
  [Parameter(Mandatory = $true)][string]$PackRoot,
  [string]$Phase = 'restore'
)
$ErrorActionPreference = 'Stop'
function Refuse([string]$Reason) { Write-Output "C479 REFUSE $Reason"; exit 2 }
$spec = Get-Content -LiteralPath (Join-Path $PackRoot 'restore.json') -Raw | ConvertFrom-Json
foreach ($file in @($spec.files)) {
  $relative = ([string]$file.path).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
  $full = Join-Path $SnapshotRoot $relative
  $current = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
  $recordedMutantHash = ([string]$file.mutantSha256).ToLowerInvariant()
  if ($current -ne $recordedMutantHash) {
    Write-Output "C479 REFUSE restore-mismatch path=$($file.path)"
    exit 2
  }
  $backup = Join-Path (Join-Path $EvidenceRoot 'backup') $relative
  [System.IO.File]::Copy($backup, $full, $true)
  $restored = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($restored -ne ([string]$file.preimageSha256).ToLowerInvariant()) { Refuse 'restore-mismatch' }
}
if ($Phase -eq 'restored') {
  $marker = Join-Path $EvidenceRoot 'forced-rebuild.marker'
  if (-not (Test-Path -LiteralPath $marker)) { Refuse 'stale-output' }
}
Write-Output 'C479 ACCEPT'
exit 0
```

```powershell
# mechanical-pc-contract v1: instantiate
param(
  [Parameter(Mandatory = $true)][string]$SnapshotRoot,
  [Parameter(Mandatory = $true)][string]$EvidenceRoot,
  [Parameter(Mandatory = $true)][string]$TemplatePath,
  [string]$TaskId,
  [string]$RunId,
  [string]$PcId,
  [string]$Phase,
  [string]$ResultsRelative
)
$ErrorActionPreference = 'Stop'
function Refuse([string]$Reason) { Write-Output "C479 REFUSE $Reason"; exit 2 }
if ([string]::IsNullOrWhiteSpace($ResultsRelative)) { Refuse 'empty-results' }
$names = @('snapshotRoot', 'evidenceRoot', 'taskId', 'runId', 'pcId', 'phase')
$values = @{
  snapshotRoot = $SnapshotRoot
  evidenceRoot = $EvidenceRoot
  taskId = $TaskId
  runId = $RunId
  pcId = $PcId
  phase = $Phase
}
$text = [System.IO.File]::ReadAllText($TemplatePath)
$tokens = [regex]::Matches($text, '\{([A-Za-z0-9]+)\}')
foreach ($token in @($tokens)) {
  $name = [string]$token.Groups[1].Value
  if (-not $names.Contains($name)) { Refuse 'unknown-token' }
  if ([string]::IsNullOrWhiteSpace([string]$values[$name])) { Refuse 'missing-value' }
}
foreach ($name in @($values.Keys)) {
  $text = $text.Replace('{' + $name + '}', [string]$values[$name])
}
if ($text -match '\{[A-Za-z0-9]+\}') { Refuse 'unknown-token' }
if ($text.Contains('..') -or $ResultsRelative.Contains('..')) { Refuse 'path-not-contained' }
$attempt = Join-Path $EvidenceRoot (Join-Path $PcId (Join-Path $Phase $RunId))
try {
  New-Item -ItemType Directory -Path $attempt -ErrorAction Stop | Out-Null
} catch {
  Refuse 'existing-attempt'
}
$resolved = Join-Path $attempt 'resolved-manifest.txt'
[System.IO.File]::WriteAllText($resolved, $text)
Write-Output 'C479 ACCEPT'
exit 0
```

## Worked examples

The following examples are illustrative. They are not executed evidence for this card.

CARD-0470 PC-8 is illustrative of a reservation window mask. A warm agent stayed hidden because the fixture returned inside `PoolReservedForCallerMinutes`. The mutant looked green until the fixture seeded idle time beyond that window, after which `launched.AgentId.ShouldNotBe(oldAgent)` failed. A masked mutant is `reachability-unproven` or `surviving-mutant`. It is not proof that the assertion is missing, and it is not a Low repair.

An equivalent mutant is illustrative when the edit compiles and the oracle stays green because the edit does not change the protected behaviour. Classify it `higher-tier-required` with reason `surviving-mutant`. Do not weaken the assertion.

A missing-control audit is illustrative in two shapes. One shape adds a new PC id with a recipe, a patch, and an oracle. The other is a zero-addition inventory: the inspected guards are listed and no redundant control is invented to make the count nonzero.

A barrier-controlled concurrency qualification is illustrative when the mutation, the schedule, the oracle, and a green companion are fixed from existing test evidence. CARD-0461 PC-83 stays `higher-tier-required` with reason `backend-race-unproven` until real process identity, isolation, and acknowledged barriers exist. A fake or sleep-based schedule does not qualify those rows, and this contract does not run them.
