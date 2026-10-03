# CARD-0998 checkpoint namespace census

Original Code / landing owner: 3eceba1b. Start: c6d5d56b5b4c565d36157e21de9c85029cc45b53.

The card asks: "find which landings added the first 27 cases without moving the census, set the census to the compiled count at the time of the fix (337 once CARD-0891 lands), and consider deriving it from `CheckpointRoster.CompiledCases` instead of a literal, or putting the guard in a lane that Code stages actually run so additions to `Antiphon.Tests.Checkpoints` cannot drift silently."

## Scope and decision

Only scripts/lib/checkpoint-usage.ps1, tests/Antiphon.Tests/Checkpoints/CheckpointTempUsageTests.cs and the testing owner documentation change; no Coverage implementation changes. Keep an independent literal census: the existing comments and Namespace/Full admission checks use it to catch an incomplete selected roster. Deriving it from that same roster would discard independence. Add method-level Category=Unit to the compiled guard, retaining the Integration classification of its class and its process limiter.

The unchanged start guard observed 348 compiled cases vs 290. First 27: CARD-0835 source checks +14 (8 state, 6 execution); CARD-0885 repeat checks +10 (5 configuration, 3 host, 1 build isolation, 1 documentation); CARD-0833 wait follow-ups +3 (1 contract, 2 executor). CARD-0891 and subsequent fixes contribute 31 PlanCoverage cases at the start SHA. New Full-floor guard contributes one, making 349 at S2.

## Slices

S1: add Unit selection of the existing compiled guard and a Full evidence guard. Commit/push; prove red with census unchanged. The initial method-OR filter selected zero cases; CP-1 now uses the exact census-name wildcard and is rerun.
S2: update census to 349 and document the independent guard. Commit/push; run Final checkpoints and the two explicitly requested scratch controls.

## Verification design

V-1 `CheckpointTempUsageTests.namespace_census_matches_compiled_checkpoint_cases`: unchanged-base red; final literal equals compiled expanded cases; every OS skip pattern resolves. Unit predicate must select the existing method.
V-2 `CheckpointTempUsageTests.namespace_census_uses_the_native_execution_roster`: Namespace native inputs accepted; short roster, skip executed and unexplained skip rejected.
V-3 `CheckpointTempUsageTests.full_suite_checkpoint_floor_uses_the_current_census`: Full accepts actual compiled roster plus 1000 supplied ordinary records; rejects missing checkpoint execution and truncated checkpoint roster with exact current expected count. Supplied evidence is not a full-assembly execution claim.
R-1 Full `CheckpointTempUsageTests*` class (9 cases) retains inspection, incomplete TRX, allocation, repeat manifest, explicit roster and sweep checks. Search found no other test class calling Get-NamespaceCensus or matching namespace census.
R-2 Whole Category=Unit lane, required by commissioned Final profile; verify V-1 is present in its fresh TRX. No shared test helper or registry changes. This row is mandated by Final profile despite the narrower brief rule.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c998-tests-red/` | guards-red | `/*/*/CheckpointTempUsageTests*/*census*` | V-1,V-2,V-3 | exactly 3 executed; red at S1, rerun green after S2 | 3 | 5 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c998-final/` | affected-class | `/*/*/CheckpointTempUsageTests*/*` | V-1,V-2,V-3,R-1 | exactly 9 executed, 0 failed/skipped | 9 | 5 | true |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c998-unit/` | unit | `/*/*/*/*[Category=Unit]` | R-2 | nonzero Unit lane, compiled guard present, 0 failed | 1000 | 6 | true |

### Positive controls (pending SourceLanding Mutation)

PC-1: change census 349 to 350; exact V-1 method must fail equality. PC-2: add one non-explicit compiled test in a scratch copy; exact V-1 with Category=Unit must fail equality. The brief additionally requests these two Code spot-checks; they do not discharge SourceLanding PCs.
PC-3: bypass Full execution floor; exact V-3 missingExecution assertion must fail. PC-4: bypass Full roster count; exact V-3 short-roster assertion must fail. All PCs and variants remain pending for Mutation.

### Cost

Ordinary floor 16 minutes plus authoring/history/manual controls; task time box 45 minutes. No full assembly run: shared runtime integration classes are unaffected; Full usage acceptance is tested with supplied roster/TRX/events in V-3. The invariant is independent checkpoint count and execution floor. No unbounded shared production impact.
