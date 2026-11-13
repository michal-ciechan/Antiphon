# Review cfec26fc: CARD-1836 S4a opt-in boundary packer (round 1, Final)

Subject: `origin/feat/card-task-6c1c4d05` tip `f47407281797cdc6db615181a7f0cf2381d94f31`,
base `272ee17c0500be3de39097acd5de321dc51840c5` (7 commits). Code landing owner
`6c1c4d05-c107-433b-837f-f882784093ac`. Verdict: **CLEAN** (no blockers).

## Checkpoints (rerun at the reviewed SHA)

Run `20261113-061746-e95c`, plan extract `.antiphon/review-cfec26fc-plan2.md`
(the S4a table, CP-90 reusing the CP-8 build). Verdict GREEN exit=0;
`validate --expected-source-sha f47407281...` -> `CHECKPOINT SOURCE VALID rows=4`.

| CP | executed/passed/failed/skipped | per class (TRX) | wall |
|---|---|---|---|
| CP-8 | 8/8/0/0 | RcBoundaryPack 3, LiveLaneRunner 4, NightlyCore 1 | 102 s |
| CP-90 | 166/166/0/0 | TestClassificationPolicy 41, TestClassificationGuard 1, SlowTestRegistryMerge 5, SlowTestTripwire 2, NativeLinuxPolicyWitness 3, MechanicalPcContract 11, TaskPlatformGuidance 5, C1729LayeringGuard 9, TestLaneCategoryGuard 1, ProcessSpawnLimit 57, DerivedImpactIndex 27, ReleaseImpactEnvelope 4 | 722 s |
| CP-91 | 2/2/0/0 | Pty ProcessSpawnLimitTests | 3 s |
| CP-92 | 2/2/0/0 | SessionRunner ProcessSpawnLimitTests | 4 s |

All rows `sourceState=clean buildSource=verified`. An earlier run `20261113-054831-45db`
had CP-90 165/166: `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`
failed with "The application to execute does not exist ... Antiphon.DerivedImpactIndex.dll".
That is the known environment precondition (the regenerate driver is not in the sln build);
after a gated build of `scripts/regenerate-derived-impact-index` the full rerun above is green.
No credit is claimed from the first run.

Other gates: `check-evidence-diff.ps1 -BaseRef 272ee17c0 -HeadRef f47407281` ->
`commits=7 entries=0 violations=0`. `git merge-tree --write-tree origin/master(ab7af12c6) f47407281`
-> clean, tree `de781800cc54d4bb5806f83bf49ce61b8302c6ea`.

## Real-tree probes (not fixtures)

Gated build of `tests/Antiphon.Tests` at the reviewed SHA in a detached scratch worktree,
`--list-tests` diagnostic discovery -> 23,144 nodes, 1,674 classes. A probe drove the real
leaf producer (`Get-NightlyProfileExecutionChunks` with real `tests/test-execution-policy.json`
exclusions) and the packer, then re-derived selection independently from each chunk's
exact/method/group filter (TUnit treenode prefix semantics) and from `caseMembership`.

| Input | leaves (exact) | required UIDs | chunks | dup / extra / missing selected | case dup / missing / extra | mixed boundary |
|---|---|---|---|---|---|---|
| rc, linux, real S2 map | 1669 (23) | 23,066 | 206 | 0/0/0 | 0/0/0 | 0 |
| same, discovery reversed | 1669 (23) | 23,066 | 206 | 0/0/0 | 0/0/0 | 0 |
| rc, windows, real map | 2310 (730) | 22,357 | 892 | 0/0/0 | 0/0/0 | 0 |
| no exclusions (Full-like) | 1674 (0) | 23,144 | 183 | 0/0/0 | 0/0/0 | 0 |
| rc, linux, hash-scrambled 8-boundary map (11% unassigned) | 1669 (23) | 23,066 | 250 | 0/0/0 | 0/0/0 | 0 |

1. Completeness: union of chunks equals the required universe exactly once, at case level, in every input.
2. Opt-in off: legacy output from the tip library is byte-identical to the base library on the real
   inputs (rc/linux `9d7ff552b0ca0258`, rc/windows `bc97d4ebbace3881`, both sides), so legacy
   `all-group-N`/`-split-N` numbering for seeds and failed-first lists is unchanged.
3. Prefix safety: no filter selects any UID twice or outside its chunk, including the scrambled map
   that places prefix siblings in different boundaries.
4. Determinism: two runs plus reversed discovery give the same digest `b19eeb3b66e40ce3`. The path
   uses ordinal comparers and no filesystem paths; the Windows-side native run stays pending.
5. Boundaries never mix: every class in every chunk maps to that chunk's boundary.

## Mutants (scratch copies, harness run directly, one build-slot lease per case)

Control: all three cases PASS unmutated.

| Mutant | Killed by |
|---|---|
| M1 skip boundary separation (all leaves -> `core`) | Separation (not separated, mixed, unassigned carryable), Default (opt-in ignored) |
| M2 drop prefix safety (partition-only universe) | Separation (filters duplicate or omit a prefix sibling) |
| M3 allow duplicate UID (skip instead of throw) | Repack (duplicate discovery UID accepted) |
| M4 allow class case collision | Repack (case-colliding classes accepted) |
| M5 lose one leaf (skip first leaf) | Separation, Repack, Default |

The operator waived mutation checks. These were cheap scratch mutants, not PC discharge.
PC-V8-boundary, -prefix, -case-membership and -default-ID remain pending.

## Opt-in exposure (7)

`BoundaryAware` is a `[switch]` with no environment or config read. Its only callers are the
internal dispatch in `ConvertTo-NightlyProfileLaunchGroups` and `scripts/test-rc-boundary-pack.ps1`.
The production `Get-NightlyProfileLaunchPlan` (nightly-tests-impl.ps1:628) and
`test-nightly-stopped-seed.ps1` call it without the switch. `NightlyCore.cs`, the launch,
coverage and publisher code are untouched.

## Advisories (non-blocking; for cards)

- A-1 performance at activation: on the real tree the boundary path packs in 15-26 s (rc/linux)
  and 49 s (rc/windows), against 3-13 s legacy. Each partition re-indexes the full 23k-node
  universe, and every exact leaf filters its class with Where-Object. Measure and budget this
  before any caller opts in (test-performance standards).
- A-2 land step: master (ab7af12c6) and this branch each bump the same `ScriptHarness.cs` and
  `ScriptHarnessProcess.cs` index hunks with the same text: master adds ReleaseImpactRelationTests,
  the branch adds RcBoundaryPackTests. merge-tree merges them cleanly as one bump, so the merged
  `tests/derived-impact-index.json` is probably one class stale. Run the sanctioned regenerate
  step at land/merge.
- A-3 plan hygiene: CP-90's DerivedImpactIndexTests regenerate method needs the regenerate
  driver prebuilt. The plan's Build column does not provide it, so a cold checkpoint run of the
  table goes 165/166 red.

## Pending (not discharged here)

Windows native startup receipt / native activation gates, S7d CP-17/18/19, operational admission
and Shadow, and all PCs remain pending as the plan states.
