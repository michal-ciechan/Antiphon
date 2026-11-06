Landed merge batch 94 on master at 955145f83ab717e3467a06cd192401eb13618129; all 179 required test results passed, with zero failures or skips.

Base: b16f8a0826eabe5c97cdc317f089a50feb44e5c4. Assigned branch: feat/card-task-a212c28b. Publication loop count: 1; master-advance re-picks: 0; master push attempts: 1; push retries: 0. A fresh fetch immediately before publication still named the assigned base. merge-tree --write-tree origin/master HEAD was conflict-free, tree 91128ccbf1f793d3b2501a7cb1648422e879d6b4. The plain fast-forward master push succeeded and git ls-remote confirmed the full SHA above. No rc ref was pushed. The source branch stayed local until master publication; this report is published afterward on the assigned branch.

Used the caller's standing merge/land authority and test-only red/green exception to separate Review; no additional approval was requested.

Changes and conflict resolutions:

- Formatter guards now locate server/Application/Core/DelegationReportFormatter.cs. Source 311888ef's two modified companion documents were absent on master because its preceding documentation-only commit was excluded. Resolved both modify/delete conflicts by retaining their complete source-commit contents. Their blobs exactly match source: candidate 2b6425c85564b9066d931ffd248487f21f32008a; verification plan 72278466e17cf0843e39fdfba0b26064c8d61ac4. The two test changes have identical stable patch-id a3cdc32c729ac59a8d51a2a132b494f1fb2d5e9b. Other branch commits 4034b60bd99619d9f411bddafc360e13ee867581 and 9aa8ee0c2c5460e38b17ab1ee414434e31b8bf46 were excluded.
- ControlledLandingGit whitelist conflict combines master's CARD-1717 explanation with all four modeled S4a reads. Its final complete file byte-matched the author's proposed landing-resolution-ControlledLandingGit.cs.txt. Compared with source 41f1dc86's fixture file, the only difference is master's one explanatory comment. The final whitelist is:

```csharp
// CARD-1717: the guarded removal's late-untracked check lists the tracked index.
["ls-files", "-z"] => true,
["ls-files", "--stage", "-z"] => true,
["ls-tree", "-r", "-z", "--full-tree", var revision] => IsRevision(revision),
["-c", "core.safecrlf=false", "hash-object", "--", ..] =>
    args.Count > 4 && args.Skip(4).All(path => IsRelativeFile(path, afterSeparator: true)),
```

  Kept the complete index/tree/blob response model, immutable commit trees, source set-aside handling and exact-vector validation. Exactly one ls-files -z arm remains. The registry auto-merge preserves master's additional entries and adds the new reason/class pair; its patch-id differs because the insertion context and preexisting trailing blank line differ.
- CARD-1737 R1 witnesses and their plan/TestDesign/verification artifacts were picked in source order. Own separate commit 955145f83ab717e3467a06cd192401eb13618129 accepts CancellationToken cancellationToken in C1737_MatchingContentReplacementIsNotDeleted and C1737_PostHashWriteSurvivesFinalStat. The compiler accepted both signatures, both witnesses passed, and neither file emits TUnit0015 in the build log. Timeout values and assertions are unchanged.
- Production, project files, solution/build definitions and target pins have an empty diff from the base. Executable changes are test-side; the policy and its live-lane pin remain coupled.

Per-commit stable patch-id proof (git cherry listed each selected source as +; cherry-pick -x, linear history):

| Land SHA | Source SHA | Land patch-id | Source patch-id |
|---|---|---|---|
| e9a8f1c468641ee41115ea37077b77f3997208ab | 311888ef27307a25798eeea75de337f8e542fc8d | a7e6726dd7f25489889cb834f6441a5b61e1c3c5 | 592d97cae09dfaac0f9c7e8e873e087dec9430e5 |
| 17e8e7b9064f1752e6b23dd644129a9ca6d4ee17 | 4db79b55b0db5e26a1c5a615697fc5b6f31de559 | c0854595580f0ae3d6a82b2e21b0903c3af6ef7e | c0854595580f0ae3d6a82b2e21b0903c3af6ef7e |
| d2923d7976711773bfb81c84e7061330c1487140 | 41f1dc86b90ce40e06436c26454392002d908d4a | 716b50dbd05e87814325f5285701376ba1c8b179 | ae04933d2cc3ff3c26bd64efff4fea10f90ad5b4 |
| 12f8f9d49428df58013b8d743adbaa51518c6510 | da3b40c599de223e7d4ba91ba2dd3d20bccae202 | 84d07c12e2474275f3d0f95e0d7d8d66d6e3197b | bd2fb50625321334f87be2a12e7ec964a80d10ce |
| aec51f16feb057c679e486fce2836622bad02505 | d83a52d8c320583bcd191e250be73f2ce04923e2 | 91306a1d8913328c0c458a1ff05d90881968b8a8 | 91306a1d8913328c0c458a1ff05d90881968b8a8 |
| f5f3dc536c9e53dc24bdfc92d1fd155a510c3ced | b8efe5cc91d3ae4015b100a9bcf89d40e92827b7 | c8b3bec7d31713b457c69f8cf3d6af28170fd985 | c8b3bec7d31713b457c69f8cf3d6af28170fd985 |
| 395e3aeebdd9858c893864b89bada5f7cedcccfe | 94ad9650a60922a652f7054c0bd5de660b7c95b3 | 24aa4dcd218746a95d44493edf51382dc96b0201 | 24aa4dcd218746a95d44493edf51382dc96b0201 |
| 550bd2a92b37a78050e0f461a9c49fe13edb9d3e | 30d64476c89d135b24d10f72cecd4a40533b3c0f | f860637ab1714156eca78f1990256ed7a3742aca | f860637ab1714156eca78f1990256ed7a3742aca |
| b9b145b266a193eaa4692ee4883e767f1c651602 | 3acf39d25f9b5020f984cb2aba00abd2d0af0d4c | 65e07ed742b0cbc683060d6baa0d68d28abf0d99 | 65e07ed742b0cbc683060d6baa0d68d28abf0d99 |
| 955145f83ab717e3467a06cd192401eb13618129 | own signature fix | 9bea744404960d0b46893426d6def43b82a4d336 | — |


The first, fixture and registry rows have the explicit resolutions above; other seven source picks match their complete stable patch-ids. The last row is the commissioned signature-only fix. No report-only source commit was picked.

Recomputed input identities:

| Identity | Base | Final |
|---|---|---|
| policyHash, stored and independently computed | 2c445d46531ccff2a572e27b5e641e984af40c3b4691c67661dd9a4a360eba72 | f2716a2ed5a3f7e06301acfaea9a1dbf89a2f1aaca4ec2e8e75b014dcd5cbdca |
| live-lane -PrintInputDigest, matching PinnedInputDigest | 417936f79e8bf453a32f2f91b202ae6435777c3ba21e159d831ca9b5a052c939 | 65629ab0a1f1e75c2dff12df4efb8e60b9000e574486c7764d4ba98a76adee4a |

The source R1 commit updated policyHash and PinnedInputDigest together; the merged recomputation matched both without an additional refresh.

Verification at exact source 955145f83ab717e3467a06cd192401eb13618129:

One serialized Antiphon.sln build passed: 0 errors, 1961 warnings, 259.34 seconds reported build time; slot held 260s. TEMP=/tmp, native default bin/obj, UseAppHost=false on build and every --no-build run. No timeout was widened. No other task build was run. Each build/test invocation acquired its own host slot, all granted with 0s wait. Test groups contained at most three rows: (cp6, registry, source), (formatter, cp1, cp2), (cp3-remainder). No green row was repeated.

| Row | Executed | Passed | Failed | Skipped | Wall seconds, including launch/slot |
|---|---:|---:|---:|---:|---:|
| cp6 | 70 | 70 | 0 | 0 | 160.345 |
| registry | 5 | 5 | 0 | 0 | 14.702 |
| source | 94 | 94 | 0 | 0 | 158.553 |
| formatter | 2 | 2 | 0 | 0 | 10.161 |
| cp1 | 3 | 3 | 0 | 0 | 10.707 |
| cp2 | 2 | 2 | 0 | 0 | 54.000 |
| cp3-remainder | 3 | 3 | 0 | 0 | 21.084 |
| Total unique results | 179 | 179 | 0 | 0 | |

CP-1: 3/3; CP-2: 2/2; CP-3: 5/5. CP-3's Registry_matches_compiled_metadata and Policy_rows_require_pinned_attribute use their exact passing CP-6 results, plus the three fresh remainder results. CP-6: 70/70. Full policy sweep: 50/50 (39 classification policy, 1 classification guard, 5 registry merge, 2 slow tripwire, 3 native Linux policy). Mechanical instruction contract: 11/11; platform guidance: 5/5. Formatter-path witnesses: 2/2. Landing source freshness/persistence: 59/59 and 35/35, including all eight original cleanup witnesses.

Layering: 9/9; VIOLATIONS 0, LISTED 168, STALE 0, AMBIGUOUS 3.

| Row | Full compiled class | Passed/failed/skipped |
|---|---|---|
| cp6 | Antiphon.Tests.Infrastructure.C1729LayeringGuardTests | 9/0/0 |
| cp6 | Antiphon.Tests.Application.MechanicalPcContractTests | 11/0/0 |
| cp6 | Antiphon.Tests.Application.TaskPlatformGuidanceTests | 5/0/0 |
| cp6 | Antiphon.Tests.TestHelpers.SlowTestTripwireTests | 2/0/0 |
| cp6 | Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | 3/0/0 |
| cp6 | Antiphon.TestSupport.TestClassificationGuardTests | 1/0/0 |
| cp6 | Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | 39/0/0 |
| registry | Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests | 5/0/0 |
| source | Antiphon.Tests.Application.AgentTaskLandSourcePersistenceTests | 35/0/0 |
| source | Antiphon.Tests.Application.AgentTaskLandSourceFreshnessTests | 59/0/0 |
| formatter | Antiphon.Tests.Application.RepairSourceDocumentationTests | 1/0/0 |
| formatter | Antiphon.Tests.Application.SpecialistRoleContractTests | 1/0/0 |
| cp1 | Antiphon.Tests.Infrastructure.LandingRemovalPolicyControlTests | 3/0/0 |
| cp2 | Antiphon.Tests.Infrastructure.WorktreeGuardedCleanupTests | 2/0/0 |
| cp3-remainder | Antiphon.Tests.Infrastructure.WorktreeNoFollowDeleteTests | 1/0/0 |
| cp3-remainder | Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests | 1/0/0 |
| cp3-remainder | Antiphon.Tests.Scripts.LiveLaneRunnerTests | 1/0/0 |

All seven row receipts bind the same test binary SHA-256 d6ee8c9834e8f73efa4d9afd17340fe6edfe5f52f34dce2b332513883f8d74d3 and source SHA above; source/index were clean before and after each row. Per-case outcomes, durations, exact filters, commands, timing JSONL and unknown/unavailable subphases remain in retained evidence. No row or batch exceeded 30 minutes.

Land-graph -Check: base and final both approved; digest D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03, 69 selected records, unchanged pins. No inherited mismatch warning was needed and -Update was never run. Two initial base audit launches stopped before evaluation because the requested evaluator path was missing (the attempted Python copy helper was unavailable); Node copied an existing evaluator whose complete tool sources matched the base. The successful base check used that copied evaluator; the final check used this worktree's solution-built evaluator. Logs and hash provenance preserve those initial setup outcomes. No extra build was used to repair that setup.

check-evidence-diff over b16f8a0826eabe5c97cdc317f089a50feb44e5c4..955145f83ab717e3467a06cd192401eb13618129: 10 commits, 0 violations. The report-only addition is checked separately before its assigned-branch push.

The known 11 inherited land-order failures were outside this closed selection; their classes were neither run nor changed, and their count is not freshly re-certified here. They remain with the separate product repair. Historical R1 whole-Unit timeout, formal SourceLanding Mutation and Windows reservations retain their previous status; this merge supplies only the brief's required final-tree checks. No desktop execution, deployment, activation, runner restart or tracker write occurred.

Cleanup completed: removed 104 owned native bin/obj roots and the worktree's retained test-result/scratch directory using exact literal absolute paths. Final census: zero bin/obj/TestResults roots. No scratch worktree was created. All owned foreground commands were awaited. Retained TRX/logs/receipts and per-case timing evidence were copied outside the checkout before deletion; generated payloads remain outside tracked source. Tracked source/index were clean after cleanup. This Markdown report is the only additional branch deliverable after master publication.

Full retained evidence: /work/evidence/task-a212c28b/merge94/. Summary: /work/evidence/task-a212c28b/merge94/test-summary.json; patch proof: /work/evidence/task-a212c28b/merge94/patch-proof.json; build provenance: /work/evidence/task-a212c28b/merge94/build-provenance.json; row commands: /work/evidence/task-a212c28b/merge94/run-row.cjs. Full report mirror: /work/evidence/task-a212c28b/task-a212c28b.md.

Rerun, if needed: from a clean desired source, first pwsh -NoProfile -File scripts/build-slot.ps1 -Label merge94-build -- dotnet build Antiphon.sln -p:UseAppHost=false with TEMP=/tmp, then node /work/evidence/task-a212c28b/merge94/run-row.cjs <row-name>. Each row driver acquires a distinct lease, uses --no-build with the same property, preserves TRX and enforces a 15m wall cap including at most 7m admission. See its exact selectors; rebuild outputs were intentionally removed.

--- next stage ---
next: done
handoff: Master publication is complete; retain the independent ordering-repair and historical R1 obligations with their existing owners.
artifact: /work/worktrees/task-a212c28b/.antiphon/task-a212c28b.md
