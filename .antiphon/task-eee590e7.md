# Review eee590e7: CARD-1836 S6a, round 2 (blockers only)

Subject: origin/feat/card-task-557c848e at 677211d0393fa5e78ed16b0bc2f643966dfd8f6f (Grok author).
Range: ca30979480332ca3e15adeaea16973db1898c134..677211d0393fa5e78ed16b0bc2f643966dfd8f6f
(red a6dc9ac6, fix ce442024, plan 677211d0). Round 1 was 2794c818, with D-1 and D-2.

Verdict: CLEAN. No blockers. One advisory, A-1.

## D-1 / D-2 closure

- D-1 closed. `$states` is `Dictionary[string,object]` with `StringComparer.Ordinal`, and `.Add` is used,
  so it fails loudly and never collapses. Case-distinct keys stay separate at every stage I drove:
  launch, band, fix attachment, discharge, omit, and both ordinal sorts. Attack set: `K` Carry / `k` Dirty /
  `B` Sample / `b` Dirty / `a` Carry / `A` Sample gives launch
  `k:failed,B:interrupted,b:changed,A:sample` and omitted `K,a`. A fix keyed `K` never discharges `k`.
  A failed Carry `K` with a green `k` still refuses.
- D-2 closed. Carry rows now go through the same attempt fold. A Carry that ends failed, unexpected-skip
  or interrupted throws `carry-with-open-failure`.

## Carry attack matrix (tip library, scratch repo; 20 checks pass)

| Case | Result |
|---|---|
| Failed at base, then green at a source containing the declared fix | omitted (discharged) |
| Failed, then green at the same tip with FixProven | refused |
| Failed, then green at an unrelated-fix commit | refused |
| Interrupted, then green at the same SHA | refused |
| Attempts supplied out of order (sorted by sequence) | discharged |
| Green before the failure in sequence | refused |
| Failed again after a discharge | refused |
| Failed again at a source that already contained the fix | refused |
| unexpected-skip | refused |
| Carry key absent from the results | omitted |
| Empty RepositoryRoot | refused |
| Shadow constants on a Carry-bearing result | unchanged |

Refusal path: nothing is written to the pipeline (count 0), and an assignment target is left unchanged.
Every `.Add` result is void or `[void]`, and the result object is built only after the ranking loop.

Property-name collisions:
- An ordinal Hashtable that has both `key` and `Key` resolves deterministically to `key`.
- A row with only `Key` refuses `invalid-obligation`.
- `ConvertFrom-Json` refuses keys that differ only by case.
- `-AsHashtable` resolves to `key`.

So no collapse is possible.

## Audit of the case-folding claim

I grepped the library for hashtables, Group-Object, Sort-Object, Select-Object, -eq/-ne, -contains/-in,
switch, -match, Contains and ContainsKey. The author's list is accurate:

- `$cache = @{}` holds only SHA pairs, and each SHA was validated by `-cnotmatch '^[0-9a-f]{40}$'` before lookup.
  An uppercase SHA refuses `repo-sha-unresolved`.
- `$allowed`, `$seen` and `$states` are Ordinal.
- `-eq`/`-ne`/`-lt` compare only numbers and exit codes.
- Both sorts use `StringComparer.Ordinal`.
- `Get-RcDirtyProp` folds case only on property names.

No case-insensitive use touches an obligation key.

## Red/green and mutants (scripts-only git-archive copies, pwsh, one build-slot lease per variant)

- At a6dc9ac6: CaseDistinctKeysStaySeparate is red (`case-distinct-launch:Suite/fooTests:failed:Sample;
  case-distinct-fix:...`). CarryWithOpenFailureRefuses is red (3x `accepted:carry-with-open-failure`).
  UnknownShaRefuses passes, as the author stated. The three old cases pass.
- At the tip: all 6 cases pass.

Mutants of the tip:

| Mutant | Killed by |
|---|---|
| M1: `@{}` plus indexer assignment (exact pre-fix collapse) | CaseDistinctKeysStaySeparate, same text as red |
| M2: OrdinalIgnoreCase dictionary | CaseDistinctKeysStaySeparate |
| M3: Carry skips the open-failure throw | CarryWithOpenFailureRefuses (3 accepted) |
| M4: Carry checks failed only | CarryWithOpenFailureRefuses (interrupt leg) |
| M5: git exit 128 treated as contained | UnknownShaRefuses only (2 accepted) |

Each mutant was killed only by its own witness, so no new witness is vacuous.

## Checkpoint rows (checkpoint tool, plan table, tip 677211d0)

Run 20261120-003900-af6d, --serial, GREEN, `validate` VALID rows=4:

| Row | Executed / passed | Wall |
|---|---|---|
| CP-10 | 8/8 | 98 s (cold build 402 s) |
| CP-90 | 138/138 (includes the 5 named guard classes) | 102 s |
| CP-93 | 27/27 | 198 s |
| CP-94 | 11/11 | 90 s |

Separate runs:
- Run 20261120-010029-8156: CP-91 2/2 (Pty ProcessSpawnLimitTests), GREEN, VALID rows=1.
- Run 20261120-010256-c3dc: CP-92 2/2 (SessionRunner ProcessSpawnLimitTests), GREEN, VALID rows=1.

All rows: 0 failed, 0 skipped, sourceState=clean, buildSource=verified, and the tool reported
"unlisted: none" on every run.

## Gates

- check-evidence-diff over ca309794..677211d0: commits=3 violations=0. Over the slice base ef331572..677211d0:
  commits=6 violations=0.
- merge-tree against master 56d7340c: one content conflict, in `tests/derived-impact-index.json`. This is
  the expected generated-index conflict; land replays and regenerates it.
- Not reachable from production: the only references are `scripts/test-rc-dirty-order.ps1` and
  `RcDirtyLaunchOrderTests`. Assert-OffSwitch also checks release-cut, nightly-run, nightly-tests,
  publish and promote.

## Advisory

- A-1 (pre-existing rule, not in this diff): an interrupted obligation is cleared by any later green at a
  different SHA, with no fix-containment requirement. So a Carry that was interrupted at `base` and then
  green at an unrelated SHA is omitted, not refused. That matches the card's "last result interrupted"
  wording and the plan, which applies containment only to failures. The brief expected a refusal here, so
  the plan could state the interrupt rule explicitly. This should not hold the land.

## Process notes

- Before the first run I deleted stale native outputs left in this mirror by an earlier task:
  `src/{Agents.Pty,PtyHost,PtyHost.Protocol,SessionRunner.Contracts}/{bin,obj}` and
  `tests/Antiphon.Tests/{bin,obj}`, using literal-path `find -delete`. Otherwise the checkpoint builds
  would have hit the known tampered refusal.
- I used the existing checkpoint tool dll, built 2026-11-19 16:19, after the last tools/Antiphon.Checkpoints
  change at 01:37 that day. The load was 20+ at that point, so I did not rebuild it.
- The 1-minute load was 16.51 at the CP-91 start. My reading just before was 14.30, so this was marginally
  over the gate.
