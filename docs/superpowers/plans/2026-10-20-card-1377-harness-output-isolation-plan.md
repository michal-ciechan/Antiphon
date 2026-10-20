# CARD-1377: isolate the remaining checkpoint harness outputs

Date: 2026-10-20. Plan with folded verification design; next: Code.
Inspected base: `fef2503a3e5e4127315be61a17336477dd11b4d2` (CARD-1312).
Test-only, master-only. No tests or builds run during planning.
Predecessor: [CARD-1312 plan](2026-10-20-card-1312-shared-bin-isolation-plan.md).

## Ground truth

All file:line references describe the inspected base.

| Card assumption | What the code does | Plan consequence |
|---|---|---|
| Remaining cases share build output despite unique results. | `scripts/test-run-checkpoint.ps1:65` creates unique fixture/results roots; `:84` independently defaults every omitted OutputPath to `bin-c585h/`. | Give each fixture arm a full-GUID output token. |
| Two sequential cases can collide. | `scripts/run-checkpoint.ps1:386` refuses an existing build-source stamp; `:1133` writes it after a successful shim build. `RunCheckpointScriptTests.cs:13` already serializes process-spawning methods. | Use one ordered witness; concurrency is unnecessary. |
| One token per TUnit method suffices. | RosterMiss has four builds, QuotedExpect six, MsBuildForwarding two builds plus a NoBuild arm, MsBuildLinuxDefault three builds, and SlotUnreachable three builds. | Allocate per `New-C585Case`, including each loop iteration, rather than per containing method. |
| Updating C671's path literals suffices. | `scripts/run-checkpoint.ps1:607` appends ArtifactsPath and UseArtifactsOutput; `:1110` gives the shim a relative build OutputPath, `:1160` gives run an absolute one, and `:1279` reports the output properties. | Update independent exact argv expectations and the Windows property-report assertion together. |
| NoBuild needs another case's output. | `scripts/run-checkpoint.ps1:616` permits unknown binding unless ExpectedSourceSha or Repeat requires verification; this offline harness sets neither and its run shim only copies TRX. | Use fresh private tokens without adding seed builds; retain no-build and lease assertions. |
| CARD-1312 already supplies cleanup. | Its D-2 specifies post-run inventory cleanup by Code, not general deletion in the harness. Existing FreshResultsDir and SlotGranted have explicit unique tokens at `scripts/test-run-checkpoint.ps1:317` and `:471`. | Keep those bodies and their witness intact; retain the same cleanup procedure. |

## Decisions

- **D-1 — fixture-owned tokens.** Add `OutputPath = 'bin-c585h-' + $Name + '-' + [guid]::NewGuid().ToString('N') + '/'` to `New-C585Case`'s returned object. Replace the fixed `Invoke-C585Runner` default with that fixture's OutputPath, used only when the caller omitted the parameter (check `PSBoundParameters`, preserving explicit invalid inputs). The factory allocates a name, never the output directory. This applies CARD-1312's relative/full-GUID/forward-slash pattern to every remaining fixture arm. Existing explicit CARD-1312 tokens still win. Reject one global GUID per script/method, which still collides within multi-arm cases, and randomizing every invocation, which obscures intentional same-fixture reuse.
- **D-2 — retain post-run inventory cleanup.** Record the exact initially absent roots produced by each checkpoint, including the red witness's legacy root, and capture evidence before cleanup. After all children exit, remove only task-owned, unchanged, unlinked outputs after containment, tracked-file and file-inventory checks; remove files individually and empty directories nonrecursively. Preserve unfamiliar/modified residue and report it. Never pre-create an output directory, delete a stamp to permit a build, or add a broad `bin-*` sweep. Preserve raw receipts/logs/TRX as ignored evidence. Do not add a new cleanup subsystem.
- **D-3 — independent argv oracles.** Replace fixed C671Build/C671Run strings with small harness expectation builders parameterized by the fixture token and explicitly expected property sequence. Derive paths from RepoRoot and the token, never observed calls or production layout helpers. Retain exact build equality, ordinal run-prefix comparison, property ordering and all slot/order/exit/count assertions. Correct stale Windows wording and its three-row inventory to describe mandatory output properties and no implicit UseAppHost. Reject substring-only replacements and merely interpolating a GUID into the old incomplete constants.
- **D-4 — bounded verification and scope.** One committed red witness and one narrow green over the 18 affected methods plus the witness. No broad class/assembly sweep, repetition, extra mutation cycle, or second OS run. Production driver, shared ScriptHarness, process custody, C578's separate output and historical plans are out of scope. No session waiting for input is added or changed. The unused new fixture property does not change C578's explicit output argument.

## Complete remaining-case inventory and changes

Every invocation below either inherits the fixed default or explicitly supplies it.
`H` means `scripts/test-run-checkpoint.ps1`; `T` means
`tests/Antiphon.Tests/Scripts/RunCheckpointScriptTests.cs`.
Line references use `H:line` / `T:line` as file:line shorthand. All fixture arms
consume D-1's private token unless an explicit invalid-path probe says otherwise.

| Case / constant | Base locations | Change and preserved assertion |
|---|---|---|
| Factory / runner default | H:65, H:71, H:84, H:122 | Allocate OutputPath once per fixture; use it for omitted arguments. |
| C585_Green | H:192, H:194 | Private green root; retain all 5 success/roster/report rows. |
| C585_Failures | H:206, H:208 | Private failures root; retain exit 1, failed count and qualified failed name (3 rows). |
| C585_ZeroExecuted | H:217, H:219 | Private zero root; retain exit 3, zero counts and empty roster (3 rows). |
| C585_RosterMiss | H:227; calls H:229, H:233, H:237, H:240 | Four independent roots; preserve matching, absent and comma-token controls (5 rows). |
| C585_QuotedExpect | H:244; calls H:255, H:261, H:267, H:273, H:280, H:287 | Six independent roots; preserve single/double/mixed/absent/interior/empty quote cases (6 rows). |
| C585_BadOutputPath | H:292, H:296, H:302, H:303; T:91 | Keep all three explicit invalid variants. Replace control's fixed token with its fixture token; rename both PASS label and C# required label to `C585 BadOutputPath control isolated forward-slash path is accepted`; retain 10 rows. |
| C585_BuildFailed | H:306, H:308 | Private root; still exactly one build, zero runs, exit 2 (3 rows). |
| C585_NoBuild | H:329, H:331 | Private NoBuild root, no seed build; retain reused/zero builds/exit 0 (3 rows). |
| C585_LineFormat | H:338, H:340 | Private root; retain exact report/trailer grammar (2 rows). |
| C671Build / C671Run / tail | H:365, H:366, H:367 | Replace fixed-path strings under D-3; keep C671RunTail unchanged. |
| C585_MsBuildForwarding | H:377; calls H:379, H:391, H:397; oracles H:383, H:385, H:393, H:401 | Three roots; use each arm's own expected build/run prefix. Preserve single property, NoBuild and ordered comma-property forwarding (6 rows). |
| C585_MsBuildLinuxDefault | H:405; calls H:407, H:415, H:421; oracles H:411, H:417, H:423 | Three roots; retain implicit false, explicit true override and unrelated property followed by implicit false (4 rows). |
| C585_MsBuildWindowsUnchanged | H:427, H:429, H:432, H:434, H:437; T:147, T:149 | Private root; update build/run oracles and exactly two mandatory MSBUILD PROPERTY report lines. Rename matching harness/C# labels and summary to current output contract; retain 3 rows and no implicit UseAppHost. |
| C585_MsBuildInvalid | H:441, H:448, H:449 | Private token per invalid-property variant; retain malformed/reserved/embedded OutDir refusals and zero dotnet calls (3 rows). |
| C589_SlotWaitsThenGranted | H:493, H:495 | Private root; retain busy/memory-floor/grant order, report and release (5 rows). |
| C589_SlotTimeout | H:508, H:510 | Private token even though admission never reaches build; retain exit 4, polling and zero calls/releases (5 rows). |
| C589_SlotUnreachable | H:522, H:525, H:530, H:536 | Three private roots, including failure arm; exact per-arm fallback build prefix with maxcpucount 4; retain no releases and run failure exit 1 (7 rows). |
| C589_NoBuildStillLeases | H:540, H:542 | Private NoBuild token; retain POST,run,DELETE and reused/granted report (2 rows). |
| C589_NoSlotSkips | H:550, H:552, H:556 | Private root; exact build expectation without maxcpucount and zero broker calls (2 rows). |

This covers every executable literal `bin-c585h` occurrence at the base:
H:84, H:302, H:303, H:317, H:365, H:366, H:471 and T:91.
H:317/H:471 are already isolated and stay unchanged. C585_AsciiOnly makes no
runner invocation; retain it unchanged and inspect changed script bytes for ASCII.

### C671 expected command shape

Let `O` be the relative fixture token, `A` its slash-normalized absolute path
under `<RepoRoot>/tests/Antiphon.Tests/` **with trailing slash**, and `P` the
explicitly expected user/platform properties in order. The expectation builders
produce these prefixes in the shim's logged spelling:

```text
build tests/Antiphon.Tests --property OutputPath=<O><P> --property ArtifactsPath=<A>artifacts --property UseArtifactsOutput=true
run --project tests/Antiphon.Tests --no-build --property OutputPath=<A><P> --property ArtifactsPath=<A>artifacts --property UseArtifactsOutput=true
```

Append ` --nologo` to builds, preceded by ` -maxcpucount 4` only for
SlotUnreachable. Append ` ` plus C671RunTail (or the existing ` -- ` boundary)
to run prefixes. `P` is empty for Windows/no user properties,
` --property UseAppHost=false` for Linux default and explicit forwarding,
` --property UseAppHost=true` for Linux explicit override,
` --property UseAppHost=false --property C671Probe=yes` for forwarding's comma arm,
and ` --property C671Probe=yes --property UseAppHost=false` for Linux's other arm.
For WindowsUnchanged, require exactly the two mandatory output-property report
lines in order, with their independently computed values, and no others.

## Slices

1. **S1 — witness, commit/push, then CP-1.** Add `[Test] public async Task C1377_GreenThenFailures()` in `tests/Antiphon.Tests/Scripts/RunCheckpointScriptTests.cs`, awaiting `C585_Green()` then `C585_Failures()`. Preserve the limiter and existing wrappers. No PowerShell repair yet. One native result exercises both existing assertion inventories.
2. **S2 — repair, commit/push, then CP-2.** Change `scripts/test-run-checkpoint.ps1` using D-1/D-3 and the inventory; update the two affected label inventories/comments in `RunCheckpointScriptTests.cs`. Retain S1 and CARD-1312's witness. Keep the script ASCII-only. Run evidence-diff over Code base..HEAD and finish D-2 cleanup after evidence capture.

## Verification design

**V-1 — ordered red witness.** Before CP-1, establish that
`tests/Antiphon.Tests/bin-c585h/` is absent in the assigned Code checkout; preserve
foreign residue and request a fresh checkout if it is occupied. Run no legacy
case first. On S1, Green must satisfy its five rows, then Failures must execute
and fail because its checkpoint exits 2 with `pre-existing-build-source`
instead of reaching its expected exit 1/failed-TRX behavior. This is one native
failed test, not two. Capture the second case's refusal diagnostics and S1 SHA.
Build/fixture failure, zero selection, timeout or failure of Green is not the
authorized red witness. Leave this run's legacy stamp through CP-2.

**V-2 — narrow green and intra-case isolation.** CP-2 selects exactly the new
witness and the 18 inventory methods. Require 19 executed, 19 passed, zero failed
or skipped; the witness must now complete both wrappers. The untouched legacy
stamp must no longer affect success. Every independent build arm gets its own
root; negative TRX, path, property and slot outcomes remain expected internal
assertions, not native failed results. NoBuild arms still perform no build.

**V-3 — argv and regression contracts.** The selected MsBuild, BadOutputPath and
slot methods retain all named assertion counts, with only the specified stale
labels/oracles corrected. Inspect both changed files and the literal inventory:
no live caller/default/oracle still uses the fixed `bin-c585h/`; full-GUID tokens
retain their trailing slash; production driver and CARD-1312 bodies are unchanged.
Static inspection of ASCII and output inventory does not add a test run.

Execute the checkpoint tool once per committed slice group:
`run --plan docs/superpowers/plans/2026-10-20-card-1377-harness-output-isolation-plan.md --rows CP-1 --expected-source-sha <S1-sha>`,
then CP-2 bound to S2. Use the testing owner's isolated, build-slot-gated tool
bootstrap; continue `wait` after exit 75 until terminal. Keep source frozen while
running. The outer build/test must take a real host slot; the harness's inner
offline shim/dead endpoint remains unchanged. Preserve unedited CHECKPOINT
lines, counts and provenance. CP-1 is historical expected red, never a green
receipt; CP-2 needs clean committed source and verified build provenance.
No additional tests are authorized by this plan.

Lane for both rows: **portable offline script integration, serial, effective
default placement**. GET `/api/runner-defaults` and GET `/api/session-runners`
were read on 2026-10-20: defaults revision 2; configured default stale/draining,
eligible Linux and Windows capacity available. Re-read at dispatch, omit
`-Runner` and `-Platform`; use `-Platform Any` only to clear an inherited OS pin.
Simulated C671 platforms require no native Windows lane or fleet location.

### Cost

Code budget: **45-60 minutes**: checkpoints 20 minutes, implementation 15-20,
inventory/evidence/cleanup 5-10, and 5-10 contingency including slot wait.
One Code dispatch with two committed slices fits this scope. If measured cost
exceeds 60 minutes, preserve completed receipts and hand off only the remaining
named work; do not widen the selection. No separate mutation round is planned.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1377-red/ | portable-script-red | `/*/*/RunCheckpointScriptTests/C1377_GreenThenFailures*` | V-1 | 1 executed, 1 failed at Failures after Green passed; pre-existing-build-source; expected red | 1 | 8 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c1377-green/ | portable-script-green | `/*/*/RunCheckpointScriptTests/(C1377_GreenThenFailures*)\|(C585_Green*)\|(C585_Failures*)\|(C585_ZeroExecuted*)\|(C585_RosterMiss*)\|(C585_QuotedExpect*)\|(C585_BadOutputPath*)\|(C585_BuildFailed*)\|(C585_NoBuild*)\|(C585_LineFormat*)\|(C585_MsBuildForwarding*)\|(C585_MsBuildLinuxDefault*)\|(C585_MsBuildWindowsUnchanged*)\|(C585_MsBuildInvalid*)\|(C589_SlotWaitsThenGranted*)\|(C589_SlotTimeout*)\|(C589_SlotUnreachable*)\|(C589_NoBuildStillLeases*)\|(C589_NoSlotSkips*)` | V-1, V-2, V-3 | exactly 19 executed/passed, 0 failed, 0 skipped | 19 | 12 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
