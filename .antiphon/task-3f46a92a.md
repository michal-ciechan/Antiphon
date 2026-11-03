# CARD-1647 D-1 Code fix (task 3f46a92a)

Branch feat/card-task-3f46a92a, continued from feat/card-task-7c150718 tip 0d2bc0cd6ac2cc9cc71acc12c5a136bf537fcc98 (base master 907edaebb).
- d6f9f15764720a965ac2ceb04d314f54ae185aef test (red): four D-1 witnesses + inert `ReviewRoundGuard.QueryTag` TagWith seam on the guard query
- 1da72a65a4d8e56102213f8a24f9415b0a7b3805 fix: guard + call site

## Invariant
The round guard warns and never blocks. No brief text and no guard fault, including a DB error in its card query, may fail a Review create.
- ReviewRoundGuard: `^[ \t]*round:[ \t]*([0-9]{1,4})\b` + `int.TryParse(..., NumberStyles.None, InvariantCulture)`; a non-parse falls back to the count rule. `round:` followed by a newline and then `7` no longer reads as round 7 (A-1).
- AgentTaskService.CreateAsync: the guard call is wrapped in `catch (Exception ex) when (ex is not OperationCanceledException)`. The catch logs card id + exception (no brief text) and adds the caller/timeline warning `review_round_limit: the round check failed (<ExceptionType>); the Review was created unchecked.`. The guard runs before the create transaction (begins ~line 1600), so a DB fault cannot poison it.

## Witnesses (red at d6f9f157: filter C1647 total 10 failed 5, each for its intended reason)
- (i) ReviewRoundsPolicyTests.C1647_a_non_ascii_round_digit_never_throws_and_falls_back_to_the_count [U+FF14, U+0664]: FormatException at the red commit.
- (ii) AgentTaskCardBindingTests.C1647_a_non_ascii_round_digit_still_creates_the_review: CreateAsync threw FormatException; after the fix the Review is created with the count warning.
- (iii) AgentTaskCardBindingTests.C1647_a_round_guard_fault_still_creates_the_review: DbCommandInterceptor throws InvalidOperationException on the tagged guard query; the create failed at the red commit.
- (iv) ReviewRoundsPolicyTests.C1647_a_round_label_never_reads_the_next_lines_number: `round:\n7` gave 7 at the red commit.

## Green at 1da72a65
13 classes total 276 failed 0 (TRX): AgentBundleAttachment 24, AgentTaskCardBinding 22, CheckpointManifestDocumentation 8, D2AdvisoryDocumentation 7, InstructionBundle 78, MechanicalPcContract 11, PostLandMutationContract 30, ReviewEvidenceParser 21, ReviewRoundsPolicy 7, ScopedVerificationInstruction 17, StableBuildOutputDocumentation 40, TaskPlatformGuidance 5, VerificationRoundInstruction 6. (271 before + 5 new.)

## Mutants (filter C1647 in the two classes; each mutant had its own build and run, restored with git checkout)
- MA: call-site catch disabled (`&& false`) + `round:[ \t]*` to `round:\s*`. These are independent files and witnesses. Total 10, failed 2: (iii) red (InjectedFault propagates) and (iv) red (DeclaredRound=7).
- MB: TryParse to Parse alone. Total 10, failed 0. **Survives: an equivalent mutant.** `[0-9]{1,4}` already admits only ASCII digits of at most 4, so Parse cannot throw. The two layers are redundant defence, and no witness can tell one layer's revert from the other.
- MC: the whole parse layer reverted to the tip form (`\d` + Parse). Total 10, failed 3: (i) both cases red (FormatException), and (ii) red because the catch turns the throw into the "round check failed" warning instead of the count warning. The Review is still created, which proves the layering.
- A `[0-9]` to `\d` revert alone with TryParse kept is also equivalent: TryParse fails and the count rule falls back. This was not run.

## Other
- check-evidence-diff 907edaebb..1da72a65: commits=5 entries=1 violations=0.
- merge-tree onto origin/master 7dc9e1168: clean (tree 8237a7811).
- Builds and runs on server2 (linux), every one under build-slot, with native default output and UseAppHost=false. Slot waits were 0-120 s.
- Not run: the whole Unit lane. The brief named the 13-class selection under the minimal-testing rule, so ordinary scope is Interim. The Unit lane is deferred to final and not marked passed.
- PCs: none declared. Mutation owns any; MB is the equivalent-mutant note for it.
