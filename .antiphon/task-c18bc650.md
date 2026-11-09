# CARD-1771 Review round 1 (Final/Full): CLEAN

Task c18bc650-fa3b-4ccb-b097-ca79bcf16913. Subject Code 54e0daae-a765-4b8c-9d11-d8f6d9e14848 (Grok author), branch feat/card-task-54e0daae.
Reviewed 7f0a8bb1f8542d75bb9d8654d43b0c6fabefb2a6..2b6523befa66d9cd0250921f60a4ceb0c32072c4 (red 1ffab5d8e4, tested code 249144215c, tip adds only the Code report).
origin/master is still 7f0a8bb1f8542d75bb9d8654d43b0c6fabefb2a6.

Verdict: CLEAN. No blockers, no regressions.

## Checks

1. Behavioural equivalence. Phone and Http contexts are the same generated RunnerReadJsonContext built with Web options. They differ only in Encoder, which affects writing only. Scratch probe (outside the repo; Contracts referenced by project): 34 payloads across buffer, snapshot and transcript (ESC as `\u001b` and `\u001B`, CRLF, NUL, surrogate pairs, 300 KB entries, PascalCase/UPPER/mixed-case names, extra and nested unknown members, missing members, explicit nulls, numbers as strings, NaN, fractional long, long.MaxValue, duplicate keys, enum as int, as string and out of range, DateTime and DateTimeOffset offsets and date-only, bad Guid, `null`, `{}`, `[]`). PhoneX, HttpOf<T>() and reflection `JsonSerializerDefaults.Web` (the pre-CARD-1762 HTTP path) gave identical results, including the same exception type on every rejecting payload. 34/34, 0 mismatches.
   HttpOf returns the same instances as HttpBuffer/HttpSnapshot/HttpTranscript. HttpOf<RunnerTranscriptEvent>() throws InvalidOperationException. Use() of a reflection JsonTypeInfo throws InvalidOperationException. Http.Options.TypeInfoResolver is the context itself, chain length 1, so there is no reflection fallback. GetTypeInfo(typeof(Uri)) returns null, and the serializer then throws NotSupportedException.
2. AsyncLocal witness. Internal. InternalsVisibleTo covers only Antiphon.Tests and Antiphon.SessionRunner.Tests. Only 4 test call sites use Watch(). In production Witness.Value is always null, so Use costs one AsyncLocal read and returns the type unchanged. Note() only records. Watch() is a synchronous method, so the value persists in the caller's flow. Dispose clears it only if the value is still its own box. The pattern predates this card (CARD-1762 StrongBox). This card only widens the box to a list.
3. Phone-home and golden. Phone-home ReadElement calls still pass PhoneBuffer/PhoneSnapshot/PhoneTranscript. My mutant swapped PhoneHomeRunnerClient.cs:249 to HttpBuffer, and PhoneHomeReadAdmissionTests.cs:36 failed on the ShouldBeSameAs identity pin. That is a different mutant from Code's reflection swap, so the witness guards contract identity, not only admission. The golden Escaped() pins `\u001B` with absolute text assertions, not only by comparison against reflection.
4. Red witness. With SessionRunnerHttpClient.cs restored to its 1ffab5d8 bytes (Phone* on HTTP reads), RunnerReadJsonClientTests.cs:57 failed on `watch.Admitted[0] should be same as` (HttpBuffer vs PhoneBuffer). That is red for the right reason. Both mutants ran in one build. They were restored with `git checkout --` of the exact paths. After a rebuild, 2/2 were green again.
5. tests/test-execution-policy.json is unchanged. No migration.

## Runs (all at 2b6523befa66d9cd0250921f60a4ceb0c32072c4)

- Checkpoint run 20261109-160055-207b from the ignored extract `.antiphon/c18bc650-plan.md`: GREEN exit 0, unlisted none, wall 14m06s, 2 cold builds.
  - CP-1, Antiphon.Tests: executed=103 passed=103 failed=0 skipped=0. Rows: RunnerReadJsonClientTests 1, PhoneHomeReadAdmissionTests 1, TranscriptSinceClientTests 24, TestClassificationPolicyTests 41, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, MechanicalPcContractTests 11, TaskPlatformGuidanceTests 5, C1729LayeringGuardTests 9. dirty=0, sourceState=clean, buildSource=verified. Build 530 s, tests 86 s.
  - CP-2, SessionRunner.Tests: executed=10 passed=10 failed=0 skipped=0 (RunnerReadJsonTests 3, TranscriptSinceReadTests 7). dirty=0, clean, verified.
  - `validate --expected-source-sha 2b6523befa66d9cd0250921f60a4ceb0c32072c4`: CHECKPOINT SOURCE VALID rows=2.
- The same selection ran earlier through build-slot (103/103 at lease 88af2eeb, 10/10 at lease 0d41dfda). New tests take 0.41 s and 0.57 s.
- Mutant build and run: 2/2 red (lease dfda1df5). Restored build and run: 2/2 green (lease c9031a9f).
- check-evidence-diff 7f0a8bb1f..2b6523bef: commits=3, entries=1, violations=0.
- merge-tree --write-tree origin/master HEAD: 907416f03ac2c9be58413f55e8f43dd363534467, no conflicts. master is an ancestor, so this equals the tip tree.
- audit-land-verification-graph.ps1 -Check with the native Release evaluator: exit 0, `approved`.

## Advisories (cards, never hold the land)

- A-1: AdmissionBox.Note uses List.Add with no lock. A future test that runs concurrent reads under one Watch() scope could lose entries or throw. The fix is a lock or ConcurrentQueue. Only test scopes are affected.
- A-2: HttpOf<T> binds by a runtime type switch, so a wrong T fails at call time, not at compile time. Passing HttpBuffer/HttpSnapshot/HttpTranscript directly would bind at compile time. Equivalent today. It is public API on Contracts with no external caller.

PCs: none defined. All stay pending for SourceLanding Mutation.
