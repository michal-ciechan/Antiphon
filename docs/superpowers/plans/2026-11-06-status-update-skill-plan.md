# Status update skill and read-only snapshot

Task/landing owner: 23abede2-8099-4671-a46b-b0fcbd40dc27. Base:
df1fc145181a3bedf444f9113f822f30d3b136f6. Final ordinary verification.

S1 commits the HTTP-stub regression before the script exists. S2 adds the portable
ASCII snapshot, skill and two discovery links. No server behavior or startup changes.
The API has no master-tip route: the test uses the established HTTP stub for task
facts and a private real Git remote for authoritative master/rc refs.

## Verification design

- V-1: snapshot text/JSON preserves scoped tasks, queued/blocked counts, authenticated
  GETs, remote master, rc progress and heartbeat; filesystem bytes remain unchanged.
- V-2: full CardRelationshipScriptTests and CardListSearchScriptTests preserve the
  shared HTTP-stub pattern. No new exclusion is needed for a portable Integration class.
- V-3: AgentContextContractTests and MechanicalPcContractTests cover instruction contracts.
- V-4: TaskPlatformGuidanceTests covers placement guidance.
- V-5: classification policy/guard, slow registry/tripwire and native Linux witness sweep.
- V-6: C1729LayeringGuardTests covers the named architecture guard.
- V-7: whole Unit lane, including method-level tags. This is required Final coverage;
  shared instruction/classification invariants cannot be bounded to changed script tests.
  Unit omits delivery, landing, leases and persistence; no such production code changes.
- R-1: skill manual acceptance: all six ordered phone sections, facts vs inference,
  live target, decision questions/recommendations, dated instructions and common misreadings.
- R-2: ASCII/parser validation, evidence-diff over full base..HEAD, OS inventory,
  fetch origin/master and conflict-free merge-tree. Restart: none; rc startup unaffected.

No deliberate mutation in Code. PC-1 pending for SourceLanding Mutation: remove the
queued-count projection; exact V-1 method must fail. PC-2 pending: substitute the
master-tip projection; same method must fail. PC-3 pending: add a filesystem write;
same method must fail. Mutation owns variants, red/restore/green and missing controls.

## Performance budget

All work runs in this Linux worktree; nothing runs on desktop. CP-1 and CP-2 have
separate cold checkpoint builds. S2 rows reuse CP-2's isolated verified output.
Each invocation selects at most three rows and is serial. The checkpoint launcher
build is an explicitly listed bootstrap expense through build-slot; row drivers lease
their own slots. TEMP=/tmp, UseAppHost=false. No direct custom output.
Estimates below are provisional, not measured p95; first execution collects fresh
TRX per-case durations and slot/build/run timings in ignored checkpoint evidence.
Script test shares one HTTP fixture and private Git fixture for its text and JSON
observations, no host/container/DB startup. Each HTTP/Git call has a 20-second bound;
the test follows the pattern's 60-second child bound pending the first measurement.
No production timeout is changed. Uninstrumented fixture/IO phases remain unknown.
CP-1 measured cold build wall 724.778s (driver build 662.25s), slot wait 15s,
test wall 1.032s; native Linux, source 8c3e7b629382adfb1a33a68c41ed795ef426954d,
ambient shared-host load, one sample, not p95. CP-2 keeps the same derived build
deadline inherited from its Unit dependent; no timeout is widened. Guard rows are
packed into CP-3 so all three S2 rows share a single build in one invocation.
Unit historical duration is unmeasured and may exceed 30 minutes (CARD-1573,
CARD-1514); a long run requires Debug with its timing evidence, not repeated proof.
Rows use existing derived deadlines; no explicit total override or widened timeout.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-status-red/` | regression-red | `/*/*/StatusSnapshotScriptTests/*` | V-1 | StatusSnapshotScriptTests; expected red before script | 1 | 4 | true | TEMP=/tmp |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-status-final/` | script-pattern | `/*/*/(StatusSnapshotScriptTests*)\|(CardRelationshipScriptTests*)\|(CardListSearchScriptTests*)/*` | V-1, V-2 | all listed, 0 failed | 3 | 4 | true | TEMP=/tmp |
| CP-3 | S2 | CP-2 | instruction-policy-guards | `/*/*/(AgentContextContractTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)/*` | V-3, V-4, V-5, V-6 | all listed, 0 failed | 9 | 5 | true | TEMP=/tmp |
| CP-4 | S2 | CP-2 | whole-unit | `/*/*/*/*[Category=Unit]` | V-7 | >= 1 executed, 0 failed | 1 | 15 | true | TEMP=/tmp |
