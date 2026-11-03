# task-833bd9b4: Review round 1 (full) of CARD-1689, feat/card-task-bbe3899e

**Outcome:** clean. No defects. The diff is diagnostic-only. Tested SHA `8e7a72fa2206ba740a27aae32de4ea94c87e1fb7` (subject tip; base `f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0`). The whole Unit lane was not run (see Scope).

## (1) Safety: scripts/c1008-retirement.sh

Line by line, the change only alters refusal text. It does not change what is refused.

- **No refusal can become a pass.** `c1008_refuse` (`scripts/c590-remote.sh:6025`) always ends in `write_result false … 2`, which `exit`s 2 in both of its branches.
  - The new direct `c1008_refuse` calls in `c1609_prepare_exchange` replace `return 1`. Previously that `return 1` made both callers (`c590-remote.sh:9269`, `:9411`) run `|| c1008_refuse RecycleGitAuditUnknown`. It is the same shell, the same exit 2 and the same `C1609_APPLY_ACTIVE`/`C1008_RECORD` path.
  - An unnamed export failure (persist) still takes `*) return 1` and gets the caller's bare diagnosis.
- **The registry check was split, not changed.** The original single jq predicate is now two checks: shape, then `.projects[$project]!=null`. Their conjunction is the same as before.
  - The new missing-file pre-check (`! -e && ! -L`) only selects a label. Without it the file still refuses (`c1609_read_canonical` → `c1609_custody` needs `-f`).
  - A dangling symlink falls through to `registry-invalid`.
- **`C1609_PROOF_STAGE` and `C1609_ORIGINS_REASON` are label variables only.**
  - Each is assigned only from fixed literals.
  - Neither is read anywhere else in `scripts/`.
  - `detail` is a new `local`.
- **Nothing can be forged.**
  - `reason`/`stage` are literals.
  - `detail=` is `$SERVER2_ROOT/retirement-origins.json` or a nonce path in the retirement root. Never an origin or registry content.
  - The inventory variant takes the first `audit check=` line with `sed {p;q}`, a single line. This is the established pattern of `c590-remote.sh:8915/9290/10236`, and `rel_repo` emits `?` for any path with CR/LF and redacts token-like segments.
  - The coordinator (`c590-real.ps1` `Invoke-C1609RetirementCoordinator`) splits stdout on LF only. It acts only on full-regex `C1609_PROOF_REQUEST`/`C1609_CONTROL_REQUEST` lines, so a `DIAGNOSIS=` line cannot inject one.
- **Consumers still recognise the diagnosis.** Desktop recipients:
  - The coordinated retire-temp path takes `$result.diagnosis` from `/work/test-evidence/<run>/retire-temp-runner/c590-result.json` verbatim (`c590-real.ps1:881`). Then `deploy-server2.ps1:1159` compares only `TempContainerStillRunning`.
  - `Test-C1008SafeAuditDiagnosis` (`c1008-host-proof.ps1:67`) would collapse the new forms to bare. It is reached only from `c590-real.ps1:493`, which reads the inventory dir's `c590-result.json`. The refusal paths never write that file, so it never sees them.
  - `c1008-recycle-real-cases.mjs:334` `gitRefusal` requires `<code> audit check=`. The new inventory form satisfies it, and the old bare form did not.
  - No exact-equality consumer of `RecycleGitAuditUnknown` exists on these paths.
- **Unchanged, as the worker claimed:** bare `RecycleUnpublishedWork`/`RecycleWorktreeDirty`, `case_retire_temp_inventory`'s `reason=unapproved-origin`, and the two `c590-remote.sh` fallbacks.

## (2) Docs (docs/docker-stack.md)

**Schema.** I derived it independently from `c1609_custody`, `c1609_read_canonical` and `c1609_export_trusted_origins`, and the doc matches. The reader requires:

- owner uid == EUID (`mc` in production);
- one link, mode `&022 == 0`, at most 1 MiB, `readlink -e` equal to the path;
- each ancestor owned by the applier or root and not g/w-writable, except a root-owned sticky directory strictly above `SERVER2_ROOT`;
- canonical `jq -cS` bytes, where a trailing newline survives `$(cat)`;
- keys `projects` and `schema`, with `schema` 1;
- `^[0-9a-f-]{36}$` project keys, each with only `origins` holding a unique non-empty string array;
- an exact-string `index` match.

**Scratch run against the real reader.** I sourced the real script with SERVER2_ROOT in my scratchpad and did not touch server2.

| Registry or origin | Result |
|---|---|
| Exact documented `umask 077; jq -ncS …` file | ACCEPT, and exports `{origins,projectId,schema}` |
| chmod 0646 | registry-invalid |
| `jq -nS` pretty-printed | registry-invalid |
| Uppercase project key | registry-invalid |
| Origin without `.git` | unapproved-origin |
| Origin with a case change | unapproved-origin |

A root-owned file is refused by `uid == EUID`, and the doc says owner `mc`.

**Diagnosis table.** Both directions match. The six reasons the script emits are exactly the six documented, and the six stages are exactly the six documented. The stage meanings match the code: `freshness` is `c1609_publication_fresh` custody plus the 900 s age window.

## (3) Witnesses

**Throwaway mutant.** I changed the registry-missing branch so it sets an empty reason, which takes the bare caller fallback. The witness `Exchange_registry_refusals_name_the_registry_cause` went red for the right reason: `run.Exit should be 2 but was 1, "missing; PREPARE_EXIT=1"`. The file was restored with `git checkout --`, and the tree is clean.

**Untested stages.** `freshness`/`digest`/`record` are pinned only by the docs test's static `C1609_PROOF_STAGE=<x>\n` presence check. I accept this, because the refusals themselves are unchanged and the labels are diagnostic. A swapped assignment order would survive, so it is listed for Mutation.

## (4) Bookkeeping

- The three roster rows match the class's existing `VolumeRetirementContentTests` pattern: same reason text, count 1.
- The class is already `[NativeLinuxOnly]`.
- C1210 is green.

## (5) Runs (server2/Linux, tip 8e7a72fa2)

I ran one direct `UseAppHost=false` build (159 s held). Every run below took its own build-slot lease and wrote a TRX that I checked per class. There is no CARD-1689 plan or checkpoint table, so no checkpoint-tool run applies.

| Run | Result | Lease |
|---|---|---|
| Mutant: `Exchange_registry_refusals_name_the_registry_cause` | 1 failed / 1 (expected) | 3 s |
| VolumeRetirement Content 25 (including 3 Exchange_*), Receipt 11, ApplyGate 13, HostProof 9, Wrapper 6, plus DockerStackDocumentationTests 17 | 81/81 | ~37 min |
| WindowsUnitPlatformAdmissionTests `C1210_*` | 5 passed, 1 NotExecuted | 31 s |
| RemoteScriptContractTests | 511/511 | 3746 s |

- The `C1210_*` NotExecuted test is `C1210_Windows_linux_only_outcomes_are_exact`, which is Windows-only by design.
- The VolumeRetirement/docs lane and the RemoteScriptContractTests lane ran concurrently.

**Other checks:**

- `check-evidence-diff.ps1 -Repository . -BaseRef f32aed42e -HeadRef 8e7a72fa2`: commits=7, violations=0.
- `git merge-tree --write-tree origin/master(e56e4d1b6) HEAD`: clean.
- Peer branches, merge-tree against this tip: ef4c37b4 (4cedfe0a2), c9cb7349 (ed67056c5), 5f63ea92 (c9ab2497c) and 6f66c43e (4f9999e70) are all clean.

## Advisories (no rerun needed)

- **A-1, docs trust wording.** The section says trust is the operator's decision and says to review each origin. It does not say what is trusted. Suggested sentence: "The desktop fetches from each listed origin, and objects found there count as proof that the volume's work is published. Listing a wrong or foreign origin can let a temp volume holding unpublished work be deleted." It does not over-trust, so this is not a defect.
- **A-2.** `! -e` also reports `registry-missing` when stat fails with EACCES on an unreadable parent. It still refuses, and only the label is wrong.
- **A-3 (Mutation).** Moving or swapping the `freshness`/`digest`/`record` assignments is not caught by any behaviour witness. The worker's PC-1..PC-10 stay pending.

## Scope

- The verification profile is Final and asks for the whole Unit lane (~3 h on server2). I did not run it. The brief's 60-minute budget and explicit class list set this round's scope, so it is not claimed.
- ordinaryScopeCompleted: Interim.
- PCs pending: all of them.
