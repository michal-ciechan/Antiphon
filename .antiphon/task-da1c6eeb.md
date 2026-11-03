# CARD-1647 Review round 2 (diff-only), task da1c6eeb

Result: clean. Tested SHA 960120797bf326b3ed0190db5581924f94b777e5 (subject feat/card-task-3f46a92a, landing owner Code 7c150718). Range reviewed: 0d2bc0cd6ac2cc9cc71acc12c5a136bf537fcc98..960120797bf326b3ed0190db5581924f94b777e5.

## (a) D-1 closed
- The pattern `^[ \t]*round:[ \t]*([0-9]{1,4})\b` + `int.TryParse(NumberStyles.None, Invariant)` was probed with the .NET regex (pwsh, same engine). Results: fullwidth U+FF14 and Arabic-Indic U+0664 give null (the old form gave a non-parse, which threw). `round: 4 of 3` gives 4. `ROUND 4` gives null. A 100,000-digit line gives null. `round: \0 4` gives null and `round:4\0` gives 4. An empty brief gives null. `round: 12345` gives null. None of them throw.
- A null or blank Goal is refused at AgentTaskService.cs:288 before the guard runs, and a Goal longer than 20,000 characters is refused at :290.
- Call site AgentTaskService.cs:1310-1324: `catch (Exception ex) when (ex is not OperationCanceledException)`. Cancellation (TaskCanceledException included) propagates. The log template carries only the CardId and the exception, and neither the guard query (a card Guid parameter) nor Evaluate puts brief text into an exception message. The warning carries only `ex.GetType().Name`. The witness asserts that the brief is absent from the warning.
- The guard runs before the create transaction (BeginTransactionAsync at :1602), so a faulted guard query cannot abort it.

## (b) TagWith and pattern shapes
- `TagWith("ReviewRoundGuard")` only prefixes a SQL comment. I grepped the test interceptors for query-text matching: CommandText/ToQueryString, StartsWith("SELECT"), and the AgentTasks/FOR UPDATE matchers in BootStall, DelegationDispatchRecoveryBoundary, ReviewEvidenceRecovery, CapacityRecovery C1593S3, BlockedTaskParkReclaim.Unloadable and StandingBootAttention. None of them matches an unlocked AgentTasks SELECT on the Review create path, so the tag cannot change their behaviour. The production tag pattern is the same as in BootReplyWatch, CommitRecoveryObligations and others.
- Legitimate forms. `round: 2` gives 2. `round: 2 of 3; stringency: diff only` gives 2; this is the documented line, docs/orchestration-loop.md:685. `ROUND: 4 OF 3` gives 4, and an indented `round: 5 of 3` on a later line gives 5.
- KEY SEMANTIC CHECK. The real orchestrator brief shapes do not parse: `REVIEW ROUND 2 OF 3 (...)` gives null, as do `Review round 2 (diff-only): ...` and `Round 2 of 3`. The pre-diff pattern `^\s*round:\s*(\d{1,4})\b` gave null on the same inputs, so this gap was not introduced by the diff (round 1 reviewed this design). What happens: a declared round written that way is ignored and the count rule applies. The guard warns when the card already has 3 or more Succeeded Review tasks. A real round 4 is caught that way as long as rounds 1-3 Succeeded. If an earlier round failed or was cancelled, or a different card carries the rounds, a round 4 is missed silently. A card with several reviewed slices gets a warning even on a declared round 2. See advisory A-1.

## (c) Equivalent mutant
TryParse reverted to Parse alone is equivalent. `[0-9]{1,4}` admits only 1-4 ASCII digits (at most 9999), and with IgnoreCase there are no non-ASCII digit case-equivalents, so Parse cannot throw. Accepted as redundant defence.

## (d) Runs (server2 linux, one build-slot lease each, native output, UseAppHost=false)
- Build at 960120797: 0 errors (266 s, slot wait 0 s).
- 13 classes (AgentBundleAttachment, AgentTaskCardBinding, CheckpointManifestDocumentation, D2AdvisoryDocumentation, InstructionBundle, MechanicalPcContract, PostLandMutationContract, ReviewEvidenceParser, ReviewRoundsPolicy, ScopedVerificationInstruction, StableBuildOutputDocumentation, TaskPlatformGuidance, VerificationRoundInstruction): total 276, failed 0, skipped 0 (TRX, 59.7 s).
- Mutant build (call-site catch `&& false` at :1317 only, plus `round:[ \t]*` to `round:\s*`), filter `(AgentTaskCardBindingTests*)|(ReviewRoundsPolicyTests*)/C1647*`: total 10, failed 2, each red on its own witness.
  - C1647_a_round_guard_fault_still_creates_the_review was red because the injected InvalidOperationException propagated out of CreateAsync.
  - C1647_a_round_label_never_reads_the_next_lines_number was red because DeclaredRound("round:\n7 of 3") returned 7.
  - Both mutated files were restored with git checkout and the tree is clean.
- check-evidence-diff: 0d2bc0cd6..960120797 gave commits=3 violations=0, and 907edaebb..960120797 gave commits=6 violations=0.
- merge-tree onto origin/master 91aa16368 is clean (tree 3d926093180a9896779ad415aa8b11701bc40368).
- Not run: the whole Unit lane. The brief named the 13-class selection, so ordinary scope is Interim. No checkpoint receipts, so reviewedSourceClean is false. PCs: none declared; any stay pending for Mutation.

## Advisories
- A-1 (not diff-introduced): the orchestrator writes `REVIEW ROUND N OF 3` with no colon, while the guard and the docs line require `round: N`. Either have Review briefs carry the documented `round: N of 3; stringency: ...` line, or file a follow-up card to also accept `round[ :]+N` (anchored, ASCII digits). Until then, a declared round from the orchestrator's real briefs is never read; only the count rule fires.
- A-2: the D-1 witness (ii) proves only that the guard falls back to the count rule. Neither (ii) nor (i) can distinguish a `[0-9]`-to-`\d` revert with TryParse kept: that mutant is equivalent, as Code noted.
