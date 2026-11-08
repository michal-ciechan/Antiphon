# CARD-1740 Review round 2 (task cc3ffc66)

Verdict: one blocker (D-1). Round 1 D-1 is repaired. The witness now runs a real Release solution build and checks four facts: exit 0, the producer dll in bin/Release, no producer dll in bin/Debug, and a byte-identical `fake-browser/` copy in the consumer. It goes red at the base .sln. The new defect is that the test takes a second host build slot from inside an outer run that already holds one, and its assertions require that nested lease to be granted.

Subject: origin/feat/card-task-4f4d35b9 at 8fb5c75a053e0136ffd410ffc4fc24d7feae2ef4 (Grok Code 4f4d35b9, owner repair of 95210f87). Round-2 range: 7b2e21923729008fc022a5c798509d004ef8892b..8fb5c75a053e0136ffd410ffc4fc24d7feae2ef4. The range changes tests/Antiphon.MarkdownPdf.Tests/FakeBrowserSolutionReleaseTests.cs and adds one line to slow-tests-allowlist.txt. Antiphon.sln is unchanged.

## Findings

### D-1 (blocker): the witness nests a second build-slot lease and requires it to be granted

- Where: tests/Antiphon.MarkdownPdf.Tests/FakeBrowserSolutionReleaseTests.cs:35-38 (assertions) and 52-78 (`RunLeasedSolutionBuildAsync` calls `scripts/build-slot.ps1 ... -SlotWaitMinutes 3`).
- Failure:
  - The test runs inside a leased driver: a checkpoint row or a `build-slot.ps1 -- dotnet run` lease. It then asks the broker for a second slot, so one test run holds two slots.
  - Observed on server2 during the base-.sln run. The outer lease `r2-1740-base-run` was held while the inner `card-1740-fakebrowser` queued at position 2 with 4/4 occupied for 30 s. Then both were leases together, so one run used half the host budget. Broker poll: scratchpad/broker-poll.txt lines 6-16.
  - The green run took 1m55s against the claimed 10.6 s, almost all of it in the same queue.
  - If the inner wait passes 3 minutes, the wrapper exits 4. The test then fails `exit.ShouldBe(0)`, so it goes red because of load, not because of the product. The desktop budget is 2, so a land verifier or another build plus the outer lease already fills it.
  - Lines 35-38 also require `BUILD SLOT granted` and forbid `unleased`. So the test also goes red when the broker is disabled by the documented rollback lever `SessionRunner__BuildSlots__Enabled=false` (`BUILD SLOT unlimited`), or when the runner is unreachable for 60 s (`BUILD SLOT unleased`). Nightly and Windows qualification run RealToolchain tests (TestClassificationMetadata.cs:19-21).
- Why: docs/testing-and-build.md, Build slots (CARD-0589): "Keep one slot-owning layer ... a child ... would wait for a second lease while the wrapper holds the first". The repo already pins that rule for in-test real builds: AgentTaskLandSourceFreshnessTests.cs:1174-1222 `RealBuildSlotWitness` ("The outer run already holds a host build slot ... the real build-slot.ps1 must not run inside a slot-holding test"). Other real-build tests run their children under the outer lease. CheckpointColdBuildTests and OutputCleanupTests pass `-NoSlot`, and CARD-1747 states the lane "does not take the host build slot". Round 1's fix text said "a leased, isolated witness". That meant the run carrying the witness is leased. It did not mean a lease inside the test.
- Fix (Code owner 4f4d35b9/95210f87; test-only, production bytes unchanged):
  - Start `dotnet msbuild Antiphon.sln -restore -p:Configuration=Release -p:UseAppHost=false -t:tests\Antiphon_MarkdownPdf_Tests -nodeReuse:false` directly as the child, under the outer lease and the existing `ProcessSpawnLimit`, with a bounded `-maxcpucount` (for example 2).
  - Remove the four `BUILD SLOT` assertions and keep the four output facts.
  - Optionally assert that the real build-slot.ps1 is not invoked, the way `RealBuildSlotWitness` does.
  - Re-measure and correct the cost in the allowlist reason.

### Advisories (not blocking)

- A-1: the test deletes `tests/Antiphon.MarkdownPdf.Tests/bin/Release`. In a Release test run that is the running test host's own directory. On Windows the delete throws because the dll is locked. On Linux it unlinks the running assembly's files, and the Release rebuild then replaces them. Ordinary lanes run Debug, so this only matters for a Release run. Consider skipping when `AppContext.BaseDirectory` is under the directory being cleared, or failing clearly. The three deletes are exact paths inside the two projects' own `bin/` (no glob, nothing outside them). Inside the assembly, every MarkdownPdf test class shares the 1-wide `ProcessSpawnLimit`, so no renderer test can read `fake-browser/` while the copy is cleared.
- A-2: the native Release build writes into the worktree's `obj/` while a checkpoint run is in flight. A checkpoint row's custom `OutputPath` still shares `obj/`, and restore rewrites `obj/project.assets.json` for MarkdownPdf.Tests, FakeBrowser and tools/Antiphon.MarkdownPdf. Row-sequential runs are not harmed. The doc rule "never run a direct build in a worktree while a checkpoint run is in flight there" is bent by design.
- A-3: the slow-registry line is correct: the right FQN, its reason on the same line, and the same format as line 1. But nothing compiled checks this registry. MarkdownPdf.Tests does not compile `tests/Shared/TestClassificationGuardTests.cs`, and `SlowTestRegistryMergeTests` reads only the Antiphon.Tests registry. So the green guard batch gives no evidence for this line. The "about 10s" cost is wrong on loaded server2: 45 s at base and 1m55s green, most of it slot queueing (see D-1).

## Brief checks

1. Four facts and red at base: yes. Lines 40-49 assert exit 0, the producer at bin/Release/net9.0, no producer at bin/Debug/net9.0, and SHA-256 equality between the consumer `fake-browser/` copy and the producer. Red at base was reproduced in a detached scratch worktree at the tip with base Antiphon.sln bytes (80cb252b, -15 lines): 1 failed, `exit should be 0 but was 1`, with `error : FakeBrowser build output was not found in .../FakeBrowser/bin/Release/net9.0`.
2. Deletes: three exact paths, `Directory.Delete(recursive)` limited to `<project>/bin/{Debug,Release}`, no globs. Other tests in the assembly cannot interfere (1-wide limiter). See A-1 and A-2 for the Release-run and shared-obj caveats.
3. Slow line correct (A-3). Lease discipline is wrong (D-1).

## Verification (server2 Linux, every driver leased, UseAppHost=false, native output, TEMP=/tmp)

| Row | Lease | Result |
|---|---|---|
| Build MarkdownPdf.Tests Debug (tip) | 0ff05f39 held 7 s | exit 0, 0 errors |
| Witness + MarkdownPdfRendererTests (tip, --no-build) | 3ead4fe5 held 122 s | TRX 11 total / 11 passed / 0 failed / 0 skipped (witness 1m55s, renderer 10) |
| Build MarkdownPdf.Tests Debug (scratch, base .sln) | ad4df2c6 held 9 s | exit 0 |
| Witness against base .sln (scratch) | 840ed812 held 48 s; nested c2acc487 waited 30 s, held 14 s | TRX 1 total / 1 failed: exit 1, "FakeBrowser build output was not found" |
| Build Antiphon.Tests Debug | a43f88a8 held 251 s | exit 0, 0 errors |
| Guard batch, one run | 119a060b held 163 s | TRX 75/75 passed, 0 failed, 0 skipped: TestClassificationPolicy 39, MechanicalPcContract 11, C1729LayeringGuard 9, SlowTestRegistryMerge 5, TaskPlatformGuidance 5, NativeLinuxPolicyWitness 3, SlowTestTripwire 2, TestClassificationGuard 1 |
| check-evidence-diff 80cb252b..8fb5c75a | n/a | commits=3 entries=0 violations=0, exit 0 |
| merge-tree origin/master (d116a618f1ddc048e5b8501b659cd535fd96deef) 8fb5c75a | n/a | e2224b63e801a80f4d2ba12028c866ad62707ad5, exit 0, clean |

Scope: round 2 covers only the diff, as the brief lists it, and that list ran in full. The profile's whole Unit lane did not run, so ordinaryScopeCompleted is Interim. There is no plan, no Checkpoints table and no checkpoint-tool receipt, so reviewedSourceClean is false. Mutation PCs stay pending.
