# RC Windows fixture deletion and session-runner watchdog

Code task and landing owner: `416937f3`. Assigned branch: `feat/card-task-416937f3`.
Task base: `42cbbd0417a2611f3a690e6671b13c9ba51fbf34` on `rc/2026-10-18`.
Round: Final. Restart: none. Publication is only to the assigned task ref.

The brief requests two independent fixes. S1 uses the existing `ReadOnlyDirectory.Delete`
for all three recursive deletes in `AgentTaskLandBareRepositoryCleanupTests`' head-only
fixture setup. S2 sets only `watchdogs.session-runner` to `1800000` and recomputes
`policyHash` through `Get-NightlyPolicyHash`, the canonical reader used by CARD-1345.
No assertion failure in the session-runner suite is repaired or discharged by that policy change.

The mandatory test-first commit is S0: the existing PowerShell witness and its C# required-row
name expect 1800000, while policy still resolves 1200000. S1 and S2 remain separate fix commits.
Run CP-1 at S0 and CP-2 through CP-5 together after both fix commits. No source edits during runs.

## Verification design

The original base reproduction is explicitly required outside the ordinary checkpoint table:
one build through `scripts/build-slot.ps1`, then one five-minute-capped UID run through the
same gate. Exact UID:
`Antiphon.Tests.Application.AgentTaskLandBareRepositoryCleanupTests.1.1.C1211_PublicationPreservesBareRepository(System.String, System.String).5.1.0`.
It executed 1 case, failed with `UnauthorizedAccessException` at line 40 in 35.478 seconds;
evidence is `.antiphon/416937f3/base-head-only/run.trx`, source was clean at the base SHA.
The build also produced the checkpoint tool used for the following manifests.

| ID | Required outcome / coverage |
|---|---|
| V-1 | Head-only case reproduces the read-only-object failure at base, then passes with private bytes and publication/cleanup assertions intact. CP-4 executes its full affected class. |
| V-2 | Production policy resolution returns session-runner 1800000; the committed witness must first fail on 1200000. CP-1 then CP-2. |
| V-3 | Production reader accepts the recomputed canonical hash; antiphon 3600000, e2e 7200000 and missing-key fallback 3600000 retain their independent assertions. CP-2 and CP-5. |
| R-1 | Whole Unit lane, CP-3. |
| R-2 | All 15 expanded cases of `AgentTaskLandBareRepositoryCleanupTests`, CP-4. |
| R-3 | All 6 methods of `ReleaseGatePolicyTests`, CP-5. |

Manual acceptance: inspect each fresh TRX identity/count, the four named script assertions,
the minimal policy diff/hash, and clean source/build receipts; run the full base..HEAD evidence
diff guard and a read-only merge-tree against fetched origin/master. No deployment/manual UI work.
No ordinary V/R is deferred. No full assembly is selected: impact is bounded to these two classes
plus the required Unit lane. No repetitions are requested after green.

### Mutation handoff

Every control remains pending for method-scoped SourceLanding Mutation after Review and landing:

| PC | Method / variant pending |
|---|---|
| PC-1 | `C1211_PublicationPreservesBareRepository`: restore direct `Directory.Delete` for the `objects` tree on Windows; head-only must fail, restored helper must pass. |
| PC-2 | `C599_E2EWatchdog`: restore session-runner 1200000 and recompute its canonical hash; only the session-runner assertion must fail, restored policy must pass. |

Mutation owns deliberate mutants, red/restore/green and discovery of missing controls.
Candidate artifact location: `docs/superpowers/mutations/` (unbound).

### Cost

Ordinary CP-2 through CP-5 estimate: 14 minutes, including a single isolated build.
CP-1 is a one-minute red characterization. Baseline build cost was 189 seconds including the
slot, and its one-case run held the slot for 39 seconds; both had `slot=granted waited=0s`.
Use a five-minute cap for the red witness and each affected-class row; Unit/build retains the
manifest's normal bound. Additional runs require an actual failure or missing execution.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S0 | n/a | watchdog-red | `pwsh -NoProfile -File scripts/test-release-gate.ps1 -Case C599_E2EWatchdog` | V-2 | expected red: 3 passed assertions, 1 failed session-runner assertion, exit 1 | n/a | 1 |
| CP-2 | S1-S2 | n/a | watchdog-green | `pwsh -NoProfile -File scripts/test-release-gate.ps1 -Case C599_E2EWatchdog` | V-2, V-3 | 4 passed assertions, 0 failed, exit 0 | n/a | 1 |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-416937f3-final/` | unit | `/*/*/*/*[Category=Unit]` | R-1 | all Unit cases, 0 failed | 1 | 7 |
| CP-4 | S1-S2 | CP-3 | bare-cleanup | `/*/*/AgentTaskLandBareRepositoryCleanupTests/*` | V-1, R-2 | all 15 expanded cases, 0 failed/skipped | 15 | 4 |
| CP-5 | S1-S2 | CP-3 | policy | `/*/*/ReleaseGatePolicyTests/*` | V-3, R-3 | all 6 methods, 0 failed/skipped | 6 | 2 |
