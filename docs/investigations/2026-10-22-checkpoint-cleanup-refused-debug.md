# Windows checkpoint cleanup refusal, 2026-10-22

Debug task 46085b1e separated two failures: the checkpoint's own deletion guard
refused during cleanup, then Codex rejected an agent's manual cleanup command
before PowerShell started. Self-cleanup already exists. The precise failing
checkpoint predicate and Codex policy rationale are not retained; neither can
honestly be inferred from the generic messages. New Backlog CARD-1520 tracks
the proven diagnostic defect. No product code, retained output, service, or live
Windows rc qualification was changed.

## Evidence and corrected chronology

| Task/run | Actual outcome | Cleanup outcome |
| --- | --- | --- |
| f7f161f3 / 20261022-211505-021c | CP-3: 1 executed, 0 passed, 1 fixture-startup failure; source 55c0dc6da27c587e9a1b3a2012e099fa98968393; report exit 1 | `cleanOnRed=false`: deliberately retained; final `cleanupRefusals=["still-present"]` |
| f7f161f3 / 20261022-213150-ad59 | Repaired-source attempt stopped before CP-3; no final receipt | Retained partial build |
| b94623ea / 20261022-220505-a7e9 | CP-3: 1 executed, 1 passed, 0 failed, 0 skipped; source 510f0a3bd88f42159641e61bd6ffc0b8b83c6cc1 | Tool's own cleanup refused; aggregate RED/exit 2; validator rejects `cleanup_refused` |
| server2 05f0443d / 20261022-195957-6fc0 | CP-1 and CP-2: 1 pass each; source e90c47450df4247bc254a027390711a555e59980 | Downloaded original report: GREEN/exit 0, `cleanedOutputs=true`, `cleanupRefusals=[]` |

Thus the first Windows attempt was not a green test turned red by cleanup.
In the second, the tool finished exit 2 at 21:26:58Z, before the manual command
was rejected at 21:30:53Z. A later agent-tool policy decision did not cause the
earlier checkpoint failure.

Unedited second-attempt checkpoint line:

```text
CHECKPOINT CP-3 commit=510f0a3bd88f42159641e61bd6ffc0b8b83c6cc1 build=ok filter=/*/*/RcQualificationStatusTests/C1500_WindowsSharedReadAndPs51 executed=1 passed=1 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-b94623ea\.antiphon\checkpoints\20261022-220505-a7e9\rows\CP-3\run.trx slot=granted waited=0s dirty=0 source=510f0a3bd88f42159641e61bd6ffc0b8b83c6cc1 sourceState=clean buildSource=verified
```

Exact persisted refusal:

```text
refused: left in place: C:\Antiphon\worktrees\card-task-b94623ea\tests\Antiphon.Tests\bin-c1500-status-windows\artifacts/obj/Antiphon.Resilience/debug/refint/Antiphon.Resilience.dll
```

Original report SHA256 remains
`B10689C7D21EB3E9289FC081B95F16BEAE51AC82DE920C5F8310AD20C48D26A1`.
Re-running the read-only PowerShell receipt validator returned
`CHECKPOINT SOURCE INVALID reason=cleanup_refused`. Historical build costs and
failed fixture outcomes remain in the two original task reports; none is replaced.

## Who refused, and what is knowable

The checkpoint refusal comes from
`src/Antiphon.HostCleanup/Checkpoints/OwnedOutputLedger.cs:94`, inside
`CheckpointOutputLedger.Remove`. Before each unlink it checks
`SafeWrite.Preflight(root, [full])` and `SafeWrite.MatchesCreation(full)`.
Either failure returns only `left in place: <path>`; the directory loop does
the same at line 100. The earlier complete-ledger checks had passed. This is
an Antiphon-owned safety guard in the checkpoint process, not a request to an
agent to run `Remove-Item`. The named DLL's unlink had not yet been attempted.
An actual unlink exception would take the separate `cleanup-failed` catch.

The on-disk build ledger has 2,579 entries; 43 are now absent, and 2,184 files
remain. The retained DLL is a 27,648-byte ordinary Archive file, last written
21:14:06Z. Its current SHA256 is
`4D1EEDDA1B05EBBB9E024084BCFBBA92BF16496F44F0DAA8D019C745308B20FB`.
A current `git ls-files -z -- <exact DLL>` returned no tracked match. These
observations do not reconstruct the dead executor's append-only content records.

`SafeWrite.Preflight` includes path/link checks and a Git tracked-path query.
`SafeDelete.PreflightRoots` can return `git-timeout`, `git-unavailable`,
`git-failed`, or `tracked`; those distinctions are discarded by `Remove`.
Changed bytes also produce the same final message. The executor log records
owner HTTP timeouts but no cleanup predicate detail. A transient Git failure
is plausible given the per-entry process overhead and loaded host, but is
not proven. A file lock, antivirus intervention, bad hash, or path-length
problem is likewise not established. The relevant cleanup files are identical
between the failed source and this investigation's base
`ffa98a8f76588c12df6b0437d24b541762b95f09`.

The manual commands failed at Codex's command-execution boundary. Native
provider records contain the tool error `exec_command failed: CreateProcess`
with `Rejected(...)` ending `rejected: blocked by policy`. No PowerShell
process ran for those calls. Both sessions record CLI 0.160.0,
`approval_policy=never`, and `sandbox_policy.type=danger-full-access`.
The agents described this as automatic approval review, but the raw error
does not identify a reviewer, rule, risk score, or explanation. It does not
establish an OS sandbox/ACL denial, nor an Antiphon command guard.

[Official approval documentation](https://learn.chatgpt.com/docs/agent-approvals-security)
describes Auto-review for eligible interactive approval requests. That generic
description does not explain these recorded never/full-access sessions; an
effective policy/command-launcher trace would be needed to name the exact
policy component. No provider settings, allowlists, or credentials were read
or changed, and no rejected deletion was retried by this Debug task.

## Linux comparison and smallest next change

`CheckpointApp.cs:417` already invokes `CleanOwnedOutputs` after execution
unless `KeepOutputs` is set. That method cleans green runs by default and
retains red runs unless explicitly requested otherwise. It uses in-process
file unlink and nonrecursive directory removal, with creation authority and
fresh safety checks. It never needs a Codex shell deletion approval.

The Linux original report was read directly from server2's
`/work/worktrees/task-05f0443d/.antiphon/checkpoints/20261022-195957-6fc0/report.json`.
It certifies cleanup of `tests/Antiphon.Tests/bin-c1500-status`. This was the
same cleanup mechanism, not the same selected test: Linux ran CP-1/CP-2;
Windows ran CP-3. Linux uses statx custody and Windows uses handle/NTFS custody.
However, this Windows run passed the initial custody/ledger checks and began
deletion, so the OS adoption difference alone does not explain its late refusal.
CARD-1327 also documents a successful, slow Windows cleanup. There is no
evidence here for a universal Windows prohibition on checkpoint self-cleanup.

The smallest proven repair is to preserve the per-entry preflight reason,
stage and path and distinguish a content/record mismatch (CARD-1520), with
focused deterministic refusal witnesses. Then repair the demonstrated cause
under CARD-1445. CARD-1327 owns eliminating one Git child per deleted entry;
do not assume its unlanded remedy is present or that it alone fixes this run.
Keep link, containment, tracked-file and content protections. Adding another
self-cleanup mechanism or loosening receipt validation is not indicated.

No documented command can be claimed approved by the rejecting policy:
the second task's containment-checked per-entry deletion was rejected too.
The tool's separate `clean` verb explicitly refuses old output because a new
process cannot inherit creation authority from the diagnostic disk ledger
(`Program.cs:463`). Existing residue requires a separately authorized recovery
path; changing shell syntax is not a diagnosis or a policy authorization.

## Retained output, cards, and handoff

Existing output prevents same-output reuse: `SourceRunGuard.cs:112` rejects an
existing build-source stamp as `pre-existing-build-source`, otherwise an existing
output directory as `pre-existing-output`; exclusive creation also refuses.
A new run ID alone does not choose a new build output. The first task used
`bin-c1500-status-windows-r1` for that reason. A fresh worktree/output can run,
but does not repair or qualify the old receipt. Manual deletion after the fact
would not remove the persisted `cleanupRefusals` or make that receipt valid.
Retained outputs consume disk and can add source-inventory work to later runs.

Duplicate searches completed through `card.ps1 -Board Antiphon -All` for
`checkpoint cleanup`, `cleanup_refused`, `output directory`, `cleanup reason`,
`per-file`, `git-timeout`, `left in place`, and `cleanup diagnostic`, followed
by full reads of the relevant hits.

- CARD-1445 (Backlog): existing Windows green-to-red cleanup failure.
- CARD-0673 (Backlog): existing Codex manual output-deletion policy rejection.
- CARD-1327 (Review): existing per-entry Git process overhead.
- CARD-1368/CARD-1310/CARD-1358: related ledger/adoption/read-deadline work,
  distinct from this observed late per-entry refusal.
- New CARD-1520 (Backlog), id `6db4cbac-3330-4ab1-9612-78e1e585e259`:
  preserve the lost per-entry refusal reason. No duplicate failure cards filed.

No builds or tests were run in Debug. Validation consisted of retained report/TRX
and transcript inspection, source tracing, the read-only receipt validator,
current file metadata/Git inventory, and original Linux receipt retrieval.
Both original task reports remain at their assigned `.antiphon/task-<id>.md`
paths. Exact native command/refusal excerpts and downloaded evidence are
gitignored at
`C:\Antiphon\worktrees\card-task-46085b1e\.antiphon\cleanup-debug`.
Only this narrative investigation is a tracked deliverable.

## Exact rejected commands

Historical command text recovered from the two bound native provider transcripts; these are evidence, not recovery instructions. All three returned CreateProcess Rejected ending in `rejected: blocked by policy`. The combined first f7f161f3 shell command was rejected in full; its preceding apply_patch was a separate tool action. No `rm -rf` command was found among these refusals.

UTC 22/10/2026 20:30:33; session 7907a97034e344499973c1bf3299ca64; call call_870255ebc8bb4c2fbafbab04b49eaaf0.

```powershell
Get-Command pwsh -All | Select-Object -ExpandProperty Source; git diff --check; git add tests/Antiphon.Tests/Scripts/RcQualificationStatusTests.cs scripts/fixtures/c1500-status.ps1 docs/superpowers/reports/2026-10-22-card-1500-windows-code.md; git commit -m "test(card-1500): select installed PowerShell for Windows fixture"; git push origin HEAD:refs/heads/feat/card-task-f7f161f3; git rev-parse HEAD
$ownedOutput = [IO.Path]::GetFullPath('C:\Antiphon\worktrees\card-task-f7f161f3\tests\Antiphon.Tests\bin-c1500-status-windows')
$ownedWorkspace = [IO.Path]::GetFullPath('C:\Antiphon\worktrees\card-task-f7f161f3') + '\'
if (-not $ownedOutput.StartsWith($ownedWorkspace, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $ownedOutput -Leaf) -ne 'bin-c1500-status-windows') { throw 'output containment failed' }
if (Test-Path -LiteralPath $ownedOutput) { Remove-Item -LiteralPath $ownedOutput -Recurse -Force }
Write-Output "REMOVED OWNED OUTPUT $ownedOutput"
```

UTC 22/10/2026 20:30:59; session 7907a97034e344499973c1bf3299ca64; call call_d310f08280ae435ab6b6ed41edee33a7.

```powershell
Remove-Item -LiteralPath 'C:\Antiphon\worktrees\card-task-f7f161f3\tests\Antiphon.Tests\bin-c1500-status-windows' -Recurse -Force
```

UTC 22/10/2026 21:30:53; session 7de4c216c38140feb7f3fd6abdc8402c; call call_2e3b8f7ae81f42d18f49573566e4250f.

```powershell
$ErrorActionPreference='Stop'
$taskWorkspace=[IO.Path]::GetFullPath((Get-Location).Path)
$ledger=Join-Path $taskWorkspace 'tests/Antiphon.Tests/bin-c1500-status-windows/.checkpoint-output-ledger.json'
if (Test-Path -LiteralPath $ledger) { Copy-Item -LiteralPath $ledger -Destination '.antiphon/checkpoints/20261022-220505-a7e9/retained-output-ledger.json' }
foreach ($relative in @('tests/Antiphon.Tests/bin-c1500-status-windows','tools/Antiphon.Checkpoints/bin-c1500-b94623ea-tool','src/Antiphon.HostCleanup/bin-c1500-b94623ea-tool')) {
  $ownedPath=[IO.Path]::GetFullPath((Join-Path $taskWorkspace $relative))
  if (-not $ownedPath.StartsWith($taskWorkspace+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Output escaped task workspace' }
  if (-not (Test-Path -LiteralPath $ownedPath)) { continue }
  $tracked=@(git ls-files -- $relative)
  if ($LASTEXITCODE -ne 0 -or $tracked.Count -gt 0) { throw 'Tracked output or Git inventory failure' }
  $root=Get-Item -LiteralPath $ownedPath -Force
  $entries=@(Get-ChildItem -LiteralPath $ownedPath -Recurse -Force)
  if (@(@($root)+$entries | Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0}).Count -gt 0) { throw 'Linked output retained' }
  foreach ($entry in $entries) {
    if (-not $entry.FullName.StartsWith($ownedPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Entry escaped owned output' }
  }
  foreach ($entry in @($entries | Where-Object {-not $_.PSIsContainer})) { Remove-Item -LiteralPath $entry.FullName -Force -ErrorAction Stop }
  foreach ($entry in @($entries | Where-Object {$_.PSIsContainer} | Sort-Object { $_.FullName.Length } -Descending)) {
    if (@(Get-ChildItem -LiteralPath $entry.FullName -Force).Count -ne 0) { throw 'Nonempty output directory retained' }
    Remove-Item -LiteralPath $entry.FullName -Force -ErrorAction Stop
  }
  if (@(Get-ChildItem -LiteralPath $ownedPath -Force).Count -ne 0) { throw 'Nonempty output root retained' }
  Remove-Item -LiteralPath $ownedPath -Force -ErrorAction Stop
  Write-Output ('OWNED OUTPUT REMOVED '+$ownedPath)
}
```

--- next stage ---
next: plan
handoff: Plan CARD-1520's precise diagnostics and coordinate CARD-1445/1327 repair. Keep CARD-1500's cleanup receipt gate incomplete; no live rc access or repeat of its passing fixture solely to relabel evidence. CARD-0673 owns the separate policy-approved residue recovery path.
artifact: docs/investigations/2026-10-22-checkpoint-cleanup-refused-debug.md
