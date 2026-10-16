# Fixture completion repair evidence

The refinement arrived and capped verification: one final narrow green rerun,
no adjacent sweep and no retry of checkpoint output-confinement refusals.
Implementation is complete for ordinary Review under that revised scope.

- Original Code / landing owner: `13dee490-6ef0-40fa-995c-8f37b73cc170`.
- Branch: `feat/card-task-13dee490`.
- Worktree: `C:\Antiphon\worktrees\card-task-13dee490`.
- Base: `6ca859d006b0a3cac9a88dc6bd551c37e98b2217`.
- Target: `rc/2026-10-15`; caller owns landing and later merge to master.
- Final implementation / runtime script SHA: `61b6e2d3d51171e33bc26be0453d179acc67bace`.
- Plan: `docs/superpowers/plans/2026-10-16-fixture-completion-repair.md`.
- Candidate: `docs/superpowers/mutations/2026-10-16-fixture-completion-repair.md`.
- Restart: **none**. No live rollout, drain, deploy, AppHost/runner change or
  server2 container/volume operation was performed.

## Changes

`scripts/c590-remote.sh` validates recovery namespace, canonical directory and
size in the selected payload filesystem, rejects failed or malformed size reads,
releases identity-checked donors before Compose absence audit, preserves inner
phase diagnosis/exit in bounded durable receipts, and propagates each required
running-Inventory probe failure. Existing deletion/identity guards stay in place.
`scripts/c590-real.ps1` retains bounded allowlisted diagnostics separately from
numeric SSH status and rejects nonzero transport even with a success receipt.
The command remains positional for existing bridge consumers.

The new `CacheFixtureCompletionTests` class and temporary-resource shell/PS
models exercise real preview, F-8 prune, donor finalization and Inventory command
bodies. Model fixes cover caller-local Bash variable shadowing, path-boundary
remapping and native du fault injection. The bridge model normalizes CRLF before
exact diagnostic-line assertions; no timeout or intended assertion was loosened.

## Ordinary outcomes

| ID | Actual outcome |
|---|---|
| V-1 | PASS: host and volume, 2 cases; missing/link/escape/noncanonical recovery and failed partial size reads refuse |
| V-2 | PASS: 1 real F-8 model case, preview plus production PC-21..25 and prune |
| V-3 | PASS: 4 donor cases; acquired donor released before audit, foreign/replaced/unknown protected |
| V-4 | PASS: 2 cases, inner preview/apply diagnosis and exit retained |
| V-5 | PASS: 1 case exercises dotnet/npm/four du/stat failures plus optional absent paths |
| V-6 | PASS on final rerun: 1 bridge case, bounded safe log, numeric exit, missing/contradictory receipt refusal |
| R-1 | DEFERRED to RC roster by refinement; adjacent checkpoint refused before tests |
| R-2 | DEFERRED to RC roster by refinement; new V-3 guards passed but existing adjacent cases were not run |
| R-3 | Bash and PowerShell parsing and diff whitespace checks passed; source inspection completed; final history/merge checks recorded below |

First committed red checkpoint: 10 executed, 0 passed, 10 failed. Real witnesses
included volume CacheRecoveryInvalid, false donor residue and swallowed Inventory
dotnet failure. Some initial failures were model defects, not production witnesses.
The model was corrected before relying on those paths. Additional native red
witnesses reached real preview/apply inner failures without durable receipt, and
the bridge lacked remote-output.txt before the fix. Each ran through build-slot,
slot=granted waited=0s (preview/apply held 1s each, bridge held 2s).

Fixed full class at `aef0eadde7b357cb28fb37f80edad34ff939955b`:
11 executed / 10 passed / 1 failed / 0 skipped. Only the Windows CRLF model
assertion failed. Final runtime source `61b6e2d3d51171e33bc26be0453d179acc67bace`:
1 executed / 1 passed / 0 failed / 0 skipped, exact bridge method. Fresh TRX:
`C:\Antiphon\worktrees\card-task-13dee490\.antiphon\fixture-final-bridge\run.trx`.
Inspected counters and intended test name. No test remains red in the revised
ordinary scope; this is cumulative targeted evidence, not a clean all-green
checkpoint manifest or completed RC gate.

The final run reused the verified CP-1 assembly built at `aef0ead...`; the C# test
method is unchanged and loads current scripts at runtime. Only the two script
files changed between that build and final runtime SHA. No claim that the assembly
was rebuilt at final HEAD. This one unlisted method rerun follows the explicit
refinement to stop chasing receipt/confinement refusals:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label fixture-final-bridge -- dotnet exec tests/Antiphon.Tests/bin-fixture-completion/Antiphon.Tests.dll --treenode-filter '/*/*/CacheFixtureCompletionTests/Bridge_retains_diagnostics_and_refuses_failed_transport' --report-trx --report-trx-filename run.trx --results-directory C:/Antiphon/worktrees/card-task-13dee490/.antiphon/fixture-final-bridge
```

Final slot=granted waited=0s held=2s. Recreate isolated output through the plan's
checkpoint tool before a future rerun; task outputs are archived after use.

## Infrastructure and costs

Checkpoint tool bootstrap was the explicit unlisted prerequisite build:
`build-slot.ps1 -Label fixture-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-fixture-tool/ --nologo`.
Passed, 0 errors / 2 warnings; slot=granted waited=249s.
Initial red checkpoint wall 6m41s; slot wait 45s.
The final full-class checkpoint wall 11m48s; CP-1 verified build 541.23s,
test host 36.26s (tests 30.98s), test slot waited=30s. Build passed with
0 errors / 1081 warnings. These costs include slow output sealing.

Run 20261016-144810-43fe refused output-inventory-failed before executor/tests.
Run 20261016-145034-1715 refused tampered before build/tests (1m27s).
Run 20261016-150003-43b8 CP-2 similarly refused tampered before any tests.
No source was edited while an executor was active. The last executor was awaited
to final report before the CRLF repair. After refinement there were no receipt
retries, adjacent runs, extra builds or repeat-proof loops.

The checkpoint cleanup reported refused: still-present. Automatic approval review
also rejected recursive deletion of task output directories (blocked by policy).
No deletion bypass was attempted; reversible moves archive task-owned build
outputs under `C:\Users\lndco\AppData\Local\Temp\antiphon-13dee490-build-outputs`.
Generated TRX, JSON receipts and logs remain ignored under `.antiphon`.
The artifact records the real refusals; clean source/build provenance in CP-1
does not imply a clean checkpoint cleanup receipt.

## Further static findings (not reproduced or changed)

1. Seed's manifest hash and package/npm byte pipelines around remote script
   lines 2333-2339 lack explicit status handling. Fixture calls occur in a
   conditional subshell where Bash errexit is ineffective: failed sha256sum/du
   may flow into invalid/partial metadata before a later failure.
2. Seed helper removal after smoke around line 2371 ignores removal failure with
   `|| true`. Host Seed can report success without confirming helper removal;
   Fixture's independent finalizer provides additional protection for that path.
3. F-7 retention treats failed Docker volume inspect as absence. A daemon error
   can satisfy that local absence check, though finalization independently audits
   owned resources. These are static follow-up candidates, not live observations.

All PC-1..PC-10 and every listed variant remain pending SourceLanding Mutation.
No deliberate mutant or red/restore/green cycle was performed here.

## Unedited checkpoint lines and publication

The lines and commit list below are copied from generated reports and Git.

```text
CHECKPOINT CP-1 commit=66854ea5812b5098d89062cc4f46b766a6ac10ef build=ok filter=/*/*/CacheFixtureCompletionTests/* executed=10 passed=0 failed=10 skipped=0 trx=C:\Antiphon\worktrees\card-task-13dee490\.antiphon\checkpoints\20261016-143704-58b7\rows\CP-1\run.trx slot=granted waited=45s dirty=0 source=66854ea5812b5098d89062cc4f46b766a6ac10ef sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=36d542258eac9de59d68e5929accd7c7a8653345 build=failed filter=/*/*/CacheFixtureCompletionTests/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: tampered dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-2 commit=aef0eadde7b357cb28fb37f80edad34ff939955b build=failed filter=/*/*/(CacheSeedNpmOwnershipTests)|(RemoteScriptContractTests)/(Volume_seed*)|(C1192_Fixture_seed_and_rerun_keep_payloads_in_volume*)|(C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest*)|(C849_Seed_refuses_invalid_donors_and_partial_payloads*)|(C1195_Seed_import_chmod_restores_volume_root*)|(C1207_Compose_teardown_requires_recorded_positive_absence*)|(C1208_Legacy_container_replacement_is_untouched*)|(C1208_Unknown_identity_retains_residue*) executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: tampered dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-1 commit=aef0eadde7b357cb28fb37f80edad34ff939955b build=ok filter=/*/*/CacheFixtureCompletionTests/* executed=11 passed=10 failed=1 skipped=0 trx=C:\Antiphon\worktrees\card-task-13dee490\.antiphon\checkpoints\20261016-150003-43b8\rows\CP-1\run.trx slot=granted waited=30s dirty=0 source=aef0eadde7b357cb28fb37f80edad34ff939955b sourceState=clean buildSource=verified
```

Implementation commits (all pushed):
```text
66854ea5812b5098d89062cc4f46b766a6ac10ef test: reproduce fixture preview, donor finalization and inventory failures
36d542258eac9de59d68e5929accd7c7a8653345 test: complete F8 model boundary and bridge diagnostic regressions
4e387bb8c0444f33f905c1ab0fe48c2c3fd88729 test: keep modeled image identity independent of Bash caller locals
b871b7796a3de168090a8b35398a78248df3ab0d test: remap mount path boundaries without rewriting fixture filenames
f90c93427138086bc4cf6e18cf9bcad62309f6c4 fix: observe preview recovery in its selected payload filesystem
40db65576a45aa2d8b277eefbe8dc431f71f50c6 fix: release acquired fixture donor before compose absence audit
7c6147b64f456b6203b529cd5388ec8d3a2a49e2 fix: retain bounded inner fixture verdicts and numeric transport failures
f6fbf24b80e215bd6613227791d9a4aff1ddb189 fix: propagate every required running inventory probe failure
aef0eadde7b357cb28fb37f80edad34ff939955b test: inject failed host du at its sudo boundary
61b6e2d3d51171e33bc26be0453d179acc67bace fix: preserve bridge command binding and normalize diagnostic fixture newlines
```

Publication checks: fetched origin/master at
`347b3fc16970d89dc7a8f3c6b713e69d692d85e7`; read-only merge-tree with the
implementation tip succeeded without conflicts (tree
`84226c8d7dbdab795df443527692eb085cee6637`). Final documentation-tip history
and merge-tree receipts are reported in the caller handoff. Alternate outputs
were archived successfully to the final-61b6e2d3d subdirectory above; no build
output directories remain from this task in the worktree. Final PowerShell
parsing and whitespace checks passed. R-1/R-2 remain deferred, not green.
