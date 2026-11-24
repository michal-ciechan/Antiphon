# Mechanical positive-control contract

Contract version: 1

This document is the owner for a mechanical positive control. It is a repository instruction contract. It is not a service, a task role, a parser, or an admission API. The `mechanical-pc` command below runs the checks a worker runs. Prose around it does not give the worker a choice.

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

## Command invocation

The checks are the `mechanical-pc` command, one verb per check: `scripts/antiphon mechanical-pc <verb>` (`scripts\antiphon.cmd mechanical-pc <verb>` on Windows). Refusal prints one `C479 REFUSE <reason>` line and exits 2. Success prints `C479 ACCEPT` and exits 0. Exit 1 means an input could not be read (a missing or malformed pack file, template or backup); it is not a verdict, so stop and report it. Refusal does not leave a partial mutant in place, except `restore-mismatch`, which leaves the unexplained bytes and names the path.

`index.json` is `{ "members": [ { "path": "src/a.txt", "sha256": "<lower hex>" } ] }`. `index.sha256` is the detached SHA-256 of those exact index bytes. `apply.json` is `{ "allowedFiles": ["src/a.txt"], "closure": [{ "path": "fixtures/config.txt", "sha256": "<hex>" }], "edits": [{ "path": "src/a.txt", "preimageSha256": "<hex>", "mutantSha256": "<hex>", "context": "BEFORE", "replacement": "AFTER", "expectedMatches": 1 }] }`. `oracle.json` carries `driverExit`, `buildSource`, `mutantOutputHash`, `restoredOutputHash`, `skipped`, `resultsPreexisted`, `executed`, `failed`, `expectedExecuted`, and `expectedFailed`. `restore.json` lists `{ "path", "preimageSha256", "mutantSha256" }` and reads backups from `evidenceRoot/backup/<path>`. A restored phase also requires `evidenceRoot/forced-rebuild.marker`. Hashes are lower-case SHA-256 of the file bytes. Pack paths use `/` and resolve under the snapshot root.

| Verb | Flags | Checks, in order |
|---|---|---|
| `preflight` | `--snapshot-root <dir> --pack-root <dir> --l <L>` | Snapshot HEAD is L, else `source-mismatch`. `index.sha256` exists, else `binding-missing`. It is the SHA-256 of the `index.json` bytes and every member's bytes match the index, else `artifact-mismatch`. |
| `apply` | `--snapshot-root <dir> --pack-root <dir>` | Every closure file matches its hash before any write, else `artifact-mismatch`. For each edit, the live bytes are the preimage, the context occurs exactly `expectedMatches` times, and the written bytes are the predicted mutant. Then the changed files are exactly `allowedFiles`. A failed check is `patch-mismatch` (an empty context is `patch-unresolved`), after every saved preimage is written back. |
| `oracle` | `--pack-root <dir>` | `resultsPreexisted`, `buildSource` `unknown`, or a restored output hash equal to the mutant's is `stale-output`. Driver exit 2 is `prerequisite-missing` and 3 is `selector-unresolved`. Skipped above zero, no executed row, a duplicate row, or an executed set other than `expectedExecuted` is `selector-drift`. A failed set other than `expectedFailed` is `unexpected-red`. |
| `restore` | `--snapshot-root <dir> --evidence-root <dir> --pack-root <dir> [--phase restore\|restored]` | A path whose bytes are not the recorded mutant is `restore-mismatch path=<path>` and stays as it is. Otherwise the backup is copied back and must equal the preimage, else `restore-mismatch`. `--phase restored` also needs the marker, else `stale-output`. |
| `instantiate` | `--snapshot-root <dir> --evidence-root <dir> --template-path <file> [--task-id <id>] [--run-id <id>] [--pc-id <id>] [--phase <name>] [--results-relative <path>]` | An empty results path is `empty-results`. A token outside the six is `unknown-token`. A used token with no value, or no PC id, phase or run id, is `missing-value`. `..` in the resolved text, the results path or the attempt names is `path-not-contained`. An existing attempt directory `<evidenceRoot>/<pcId>/<phase>/<runId>` is `existing-attempt`. Otherwise the command creates it and writes `resolved-manifest.txt` there. |

The command is `MechanicalPc` in `scripts/lib/Antiphon.Nightly/MechanicalPc.cs`; `MechanicalPcRehearsalTests` rehearses every verb on disposable Git fixtures.
## Worked examples

The following examples are illustrative. They are not executed evidence for this card.

CARD-0470 PC-8 is illustrative of a reservation window mask. A warm agent stayed hidden because the fixture returned inside `PoolReservedForCallerMinutes`. The mutant looked green until the fixture seeded idle time beyond that window, after which `launched.AgentId.ShouldNotBe(oldAgent)` failed. A masked mutant is `reachability-unproven` or `surviving-mutant`. It is not proof that the assertion is missing, and it is not a Low repair.

An equivalent mutant is illustrative when the edit compiles and the oracle stays green because the edit does not change the protected behaviour. Classify it `higher-tier-required` with reason `surviving-mutant`. Do not weaken the assertion.

A missing-control audit is illustrative in two shapes. One shape adds a new PC id with a recipe, a patch, and an oracle. The other is a zero-addition inventory: the inspected guards are listed and no redundant control is invented to make the count nonzero.

A barrier-controlled concurrency qualification is illustrative when the mutation, the schedule, the oracle, and a green companion are fixed from existing test evidence. CARD-0461 PC-83 stays `higher-tier-required` with reason `backend-race-unproven` until real process identity, isolation, and acknowledged barriers exist. A fake or sleep-based schedule does not qualify those rows, and this contract does not run them.
