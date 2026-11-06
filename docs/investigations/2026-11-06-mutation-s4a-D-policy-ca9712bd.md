# S4a D-policy mutation wave 1 — ca9712bd

**Completed the assigned two-control slice: M-2 killed; X-26 masked by the next guard, with diagnostic-only red and no preservation-kill credit. Restored-source verification passed 6/6.**

2026-11-06, Linux/server2, assigned branch `feat/card-task-ca9712bd`. Tested base **fed5899951daa3c66be3dee31355e6261286dc01**; initial tracked source/index were clean. This is the explicitly waived ordinary-worktree informational sweep, **not CARD-0478 SourceLanding evidence**. No operation ID is applicable or invented, and no overall SourceLanding completion is claimed. Standing caller authority covered the work; no additional approval was requested.

Only X-26 and M-2 were selected. No product/test repair, extra control, class pack, desktop execution, landing, activation, rc push or land-graph update occurred. The two temporary source edits were restored; only this report is committed.

## Control outcomes and exact bindings

Both filters select only the named method, including every argument row:

| Selector | Exact filter | Baseline P/F | Selected P/F | Restored P/F | Classification |
|---|---|---:|---:|---:|---|
| X-26 | `/*/*/C0824DesktopLegacyReleasePolicyTests*/Detached_head_tree_is_never_released` | 2/0 | 0/2 | 2/0 | Second-guard-masked for preservation; diagnostic-only detection |
| M-2 | `/*/*/C0824DesktopLegacyReleasePolicyTests*/Fact_that_appears_after_selection_holds_the_tree` | 4/0 | 0/4 | 4/0 | Killed: three late facts reached Removed; fourth lost its required reason |

**18 executed results: 12 passed, 6 mutation failures, 0 skipped.** Four failures belong to the intended M-2 assertions; the two X-26 reason failures are retained but are not credited as preservation kills. No fixture/build failure, zero-result selection, timeout or unrelated PC-64 failure is used as a kill. No global-equivalence claim is made.

### X-26: literal guard deletion remains protected

At `server/Infrastructure/Git/LandingGit.cs:1321`, the selector bypassed **only** the `symbolic.ExitCode == 1 → detached_head` return. The immediately following `!symbolic.Succeeded → symbolic_head_error` check remained intact. A real detached HEAD makes `git symbolic-ref -q HEAD` exit 1, so that second check still refuses identity authorization.

Both `before-filing` and `after-filing` reached the unchanged `Refused` outcome assertion, then failed at test line 1835:

```text
ShouldAssertException: row.ReasonCode
    should be
"detached_head"
    but was
"symbolic_head_error"
```

The later directory/registration/branch preservation assertions did not execute after that failure. The source control flow and passed Refused assertion establish the second refusal, not an observed acceptance or byte-loss failure. The selected switch was reached (three X-26 activation markers), and PC-64’s job attribute file was unchanged. This supersedes neither the old unrelated PC-64 failure nor any formal pending obligation.

**Caller design choice:** retain this literal control as diagnostic-only/masked evidence, or commission a separately specified detached-identity acceptance control. Repeating this exact guard deletion cannot demonstrate acceptance while the next exit-status check remains. Removing further fences merely to force a red was outside this slice and was not done. This is a control-design finding, not a reproduced unmutated-base product regression.

### M-2: late server facts independently discriminate

At `server/Application/Services/DesktopLegacyReleasePolicyService.cs:274`, the selector used the previously selected candidate and skipped only ActAsync’s initial database row refresh and `Facts.LoadAsync/Classify` reclassification. Subsequent service and removal guards stayed intact. Eight M-2 activation markers cover the older positive control and target across four cases.

| Argument | Exact selected failure | Meaning |
|---|---|---|
| late-sync-debt, sync_debt | `row.Outcome should be "Held" but was "Removed"` (line 1895) | Required late-fact removal prevention lost |
| late-dependent, dependent_open | `row.Outcome should be "Held" but was "Removed"` (line 1895) | Required late-fact removal prevention lost |
| late-slot-lease, slot_leased | `row.Outcome should be "Held" but was "Removed"` (line 1895) | Required late-fact removal prevention lost |
| late-path-shared, path_shared | `row.ReasonCode should be "path_shared" but was "identity_ambiguous"` (line 1896) | Held by the later unique-owner guard; specific-reason detection only |

M-2 is one killed control, not four independently killed guards. Its shared-path argument alone does not prove loss of preservation. Full assertion messages, stacks, test and execution IDs remain in the fresh TRX and `summary.json`.

## Build, selector and source provenance

Persistent raw evidence: **/work/evidence/task-ca9712bd**. The immutable dispatch inventory was read at Investigate `0037ffbd94ab6d42074d77b31f90015f43ea6a34`; Custom 6569dd36 precedent report/Goal/Result at `7e8a86a4e13fbe51cf98df1a1f5a0bbb677a6e1b`. The later witness source was also inspected at `72b9962054452b5a431e0ab5ebf03adc8d55a9f2`; actual executed tests are hash-bound to the pinned base above.

One frozen packed tree and one packed build contained two sites behind `ANTIPHON_MUTATION_CA9712BD`. Exactly one scalar selector was active in each selected process; baselines used the empty selector. Unknown IDs are rejected by the archived driver before any slot/test launch (self-check saved separately) and by the compiled module initializer. There is no test-name/reflection mapping. Baseline-off runs are controls on packed bytes, not pristine restored-source runs.

Patch SHA-256: `37f44d89604efa5e6f27fa9fc6c6d7579c437de9af5c65bdfa5567b959356ccd`. Archived patch: `/work/evidence/task-ca9712bd/mutants.patch`.

| Source path | Original SHA-256 | Packed SHA-256 | Restored SHA-256 |
|---|---|---|---|
| `server/Infrastructure/Git/LandingGit.cs` | `c44a95986c7c7a9a371c9baca5faf1c9c0f28be7f5715f93acae51891d4bea7a` | `b0a9c5dfd1fda03339eca3c2070b5c53410544f8b11e5529bf41d5905835fab8` | `c44a95986c7c7a9a371c9baca5faf1c9c0f28be7f5715f93acae51891d4bea7a` |
| `server/Application/Services/DesktopLegacyReleasePolicyService.cs` | `df86329c758fc5a653b63af2d58b009f9a5ef6232063542674935e208b12b3a3` | `07b817cda300627d4651703d4cf6ea1f761a405a353f5ebaabe6f28885c61539` | `df86329c758fc5a653b63af2d58b009f9a5ef6232063542674935e208b12b3a3` |

Original Git blob IDs are `5a344e9f6e259ef7fb681c89bd1051870b775232` (LandingGit) and `0d681df00e9eb32b9652665588b1eb8ec1743814` (policy). Restored bytes were compared directly with `git show fed5899951daa3c66be3dee31355e6261286dc01:<exact-path>` and original SHA-256 values.

| Artifact | Packed SHA-256 | Restored SHA-256 |
|---|---|---|
| `server/bin/Debug/net9.0/Antiphon.Server.dll` | `d76f624acac7a1cb851e52fbaf0cafc651675fbbfbeba269ee5ac68beb242f84` | `6f8a2b8de3865401765c73ae738a54b9b4d7b0de2f0b80767dce0b4358342105` |
| `tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Server.dll` | `d76f624acac7a1cb851e52fbaf0cafc651675fbbfbeba269ee5ac68beb242f84` | `6f8a2b8de3865401765c73ae738a54b9b4d7b0de2f0b80767dce0b4358342105` |
| `tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.dll` | `9cdd3d3a9416fa2bb657d91d090f60d6ec24c31e8065dbb17cd6873fd912eead` | `8097b68b794545dbe9f132be5895603ab230ecc86d0627e77fa21df1c294bd35` |

Each test receipt records producer/staged/test DLL hashes before and after execution; no drift occurred, and producer/staged server copies matched. Packed binaries contained the selector/helper markers; the restored build contained neither. No initial pristine binary build is claimed.

Unchanged witness SHA-256: `e6e55b88a1604ced52ce198e2c0f85d5cec42d6fe57fbf490c6d799ba4647c89`. Unchanged PC-64 job file SHA-256: `2e59e4712d3c32a2b74a8876468714b561ed47eaf8596655ec478c4709f3d98f`.

## Fresh executions and timing

Exactly two native-default-output builds and six exact-method drivers ran. Every invocation acquired and released its **own genuine** `scripts/build-slot.ps1` lease. Builds used `-SlotWaitMinutes 10` and a 20-minute post-grant execution cap; methods used `-SlotWaitMinutes 5` and a 3-minute post-grant execution cap. All used `-p:UseAppHost=false` and `TEMP=/tmp`. Test attributes were unchanged. No unleased fallback, admission retry, broad run, shared lease around a loop, concurrent own test driver or Pty assembly run occurred. All local inherited children were foreground-owned and awaited.

| Operation | Exit | Executed/P/F | Admission envelope s | Post-grant execution s | Wall s |
|---|---:|---:|---:|---:|---:|
| build-packed | 0 | build | 1.027 | 432.446 | 433.473 |
| baseline-X-26 | 0 | 2/2/0 | 1.271 | 27.495 | 28.766 |
| baseline-M-2 | 0 | 4/4/0 | 1.105 | 28.094 | 29.199 |
| red-X-26 | 2 | 2/0/2 | 0.580 | 24.729 | 25.309 |
| red-M-2 | 2 | 4/0/4 | 1.157 | 26.574 | 27.731 |
| build-restored | 0 | build | 1.167 | 339.373 | 340.540 |
| green-X-26 | 0 | 2/2/0 | 225.848 | 35.054 | 260.902 |
| green-M-2 | 0 | 4/4/0 | 46.390 | 51.215 | 97.605 |

Packed build: 0 errors, 1769 warnings, MSBuild 7m11.37s. Restored build: 0 errors, 1746 warnings, MSBuild 5m38.80s. Build and test execution bounds were not hit. Broker-reported waits were 0s for the first six operations, 225s for green-X-26 and 45s for green-M-2. The table’s admission envelope also includes PowerShell startup and response overhead.

Summed driver admission 278.545s; post-grant execution 964.980s; driver wall 1243.525s. First build started 2026-11-06T09:49:59.431Z; last green ended 2026-11-06T10:12:38.375Z (within the 90-minute outer budget, including preparatory reading and cleanup). No individual selection or build exceeded 30 minutes. Fixture/startup/body/teardown subdivisions are **unknown** where no phase records were emitted; per-result TRX duration is not relabelled as a measured body-only phase.

| Fresh TRX path relative to evidence root | TestRun ID |
|---|---|
| `baseline-X-26/run.trx` | `f2498ecb-eb27-4cd4-b547-922d2bc843a2` |
| `baseline-M-2/run.trx` | `5a9a0ada-3aa9-4417-bac8-97ce1decb18a` |
| `red-X-26/run.trx` | `08ca16fd-32da-41ab-9df2-ca3fa655a9b3` |
| `red-M-2/run.trx` | `a0cf5426-6a37-465d-93a4-448edfe2bfa0` |
| `green-X-26/run.trx` | `b31cdaa2-abdc-4a90-9d37-fbd4c9eb7928` |
| `green-M-2/run.trx` | `dfe039c1-42ca-4b56-97fa-56234289aa3e` |

All six TestRun IDs and all per-result execution IDs were retained; TestRun IDs were checked unique. Each phase’s argument roster is identical for its selected method. Per-result durations (seconds) are:

| Method argument | Baseline | Selected | Restored |
|---|---:|---:|---:|
| X-26: before-filing | 5.953 | 4.858 | 9.806 |
| X-26: after-filing | 5.980 | 4.838 | 9.786 |
| M-2: late-slot-lease, slot_leased | 8.543 | 5.465 | 16.692 |
| M-2: late-path-shared, path_shared | 8.569 | 5.341 | 16.693 |
| M-2: late-sync-debt, sync_debt | 8.573 | 5.505 | 16.619 |
| M-2: late-dependent, dependent_open | 8.577 | 5.444 | 16.370 |

## Restoration, cleanup and continuation

Restored using literal `git checkout -- server/Infrastructure/Git/LandingGit.cs server/Application/Services/DesktopLegacyReleasePolicyService.cs`. Git blob/byte comparisons passed; timestamps were refreshed before the single restored build. The helper was embedded in LandingGit, so restoring that exact file removed the switch. Restored marker absence and fresh changed DLL hashes were verified before the six green results.

All 72 task-owned bin/obj roots (none existed initially) were inventoried and deleted with the inspected literal absolute `Remove-Item -LiteralPath` commands in `/work/evidence/task-ca9712bd/cleanup.ps1`. No variable/glob deletion, git clean, volume removal or foreign-path cleanup was used. `cleanup-result.json` confirms zero remaining inventoried roots and clean tracked status before this report was written. Raw evidence remains external. `restoration.json` is a local byte-check record, not a SourceLanding custody receipt.

Evidence includes `identity.json`, `selector-manifest.json`, patch, compiled-marker checks, all eight `<operation>/invocation.json` and logs, six fresh TRX/HTML reports, full parsed `summary.json`, `validation.json`, restoration and cleanup records. All evidence is preserved without rewriting historical results.

To reproduce, use a separately authorized ordinary worktree at the tested base and the archived patch. Copy `/work/evidence/task-ca9712bd/run.cjs` to a fresh external evidence root and change its root literal so existing receipts cannot be overwritten. The archived driver enforces source hashes, selector admission, per-command leases and execution caps. Run modes `packed`, `baseline X-26`, `baseline M-2`, `red X-26`, `red M-2`; restore the two exact paths and verify base bytes; then `restored`, `green X-26`, `green M-2`. Carry `identity.json` and use a fresh evidence directory. Stop on a lease timeout rather than retrying.

**Caller continuation:** decide X-26’s diagnostic-only/masked classification or separately commission its acceptance-control design. M-2 has this waived fresh kill only. Sixteen unselected D-policy bindings remain: PC-11, PC-21e, X-13b, X-11, PC-52, PC-58, M-5, X-18, T-D-4-retry; and hold-english-phrase, retry-skip-recheck, length-guard-on-linux, refuse-after-slot, follow-links, skip-unreadable, wse-default-true. Known missing/masked rows, other groups, native controls, historical reuse conditions and formal SourceLanding obligations remain in the caller’s full ce37c38f ledger. None was silently discharged by this slice.

--- next stage ---
next: decide
handoff: Classify the X-26 second-guard-masked preservation control; commission separate design only if acceptance credit is required, then continue the remaining D-policy controls via bounded Mutation slices.
artifact: /work/worktrees/task-ca9712bd/docs/investigations/2026-11-06-mutation-s4a-D-policy-ca9712bd.md
