# CARD-1771 Code report

Task 54e0daae-a765-4b8c-9d11-d8f6d9e14848. Branch feat/card-task-54e0daae. Worktree /work/worktrees/task-54e0daae. Base 7f0a8bb1f8542d75bb9d8654d43b0c6fabefb2a6.

HTTP buffer, snapshot, and transcript reads now admit the HTTP source-generated contracts. Phone-home reads and the ANSI golden stay on the phone contracts.

## Commits

- Red 1ffab5d8e4042d64b3ade519750b64ebddb99a5e `test(card-1771): failing HTTP contract identity witness plus phone-home and ANSI pins`
- Fix and tested source 249144215ccc45e7216fcb6b9c6e4a1cc52dd1dd `fix(card-1771): bind HTTP runner reads to the HTTP JSON contract by type`

## Behavior

SessionRunnerHttpClient binds GetBuffer, GetSnapshot, and GetTranscript through RunnerReadJson.HttpOf, which returns HttpBuffer, HttpSnapshot, and HttpTranscript by CLR type. Phone-home GetBuffer, GetSnapshot, and GetTranscript still call ReadElement with PhoneBuffer, PhoneSnapshot, and PhoneTranscript. RunnerReadJsonContext records that the attribute configures JsonSerializerContext.Default only and nothing reads Default.

A-4 stays declined. The admission witness remains an internal AsyncLocal on RunnerReadJson because the identity pin has to see the JsonTypeInfo the production read passes to Use. A test-side resolver cannot observe ReadFromJsonAsync or ReadElement.

A-5 is not this card. Event records, PhoneHomeFrame, and one-pass decode stay on reflection. The sampled cost was the large DTOs, already source-generated in CARD-1762. The remainder is a separate wire-pipeline change.

## Publication checks

- Evidence-diff over 7f0a8bb1f8542d75bb9d8654d43b0c6fabefb2a6..249144215ccc45e7216fcb6b9c6e4a1cc52dd1dd: 2 commits, 0 entries, 0 violations.
- `git merge-tree --write-tree origin/master HEAD` against origin/master 7f0a8bb1f8542d75bb9d8654d43b0c6fabefb2a6 wrote 6db6de9d731bb819f555c31d3a27a21e55b35e25 with no conflicts. Not merged.
- tests/test-execution-policy.json did not change. policyHash and PinnedInputDigest were not refreshed.
- No plan `### Checkpoints` table, so the checkpoint tool was not used. No named V-n/R-n rows. Restart: none.
- Touched classes were split by assembly. One process cannot host Antiphon.Tests and Antiphon.SessionRunner.Tests together.

## Ordinary results

Red at 1ffab5d8, Antiphon.Tests `/*/*/RunnerReadJsonClientTests/*` plus `/*/*/PhoneHomeReadAdmissionTests/*`: total 2, failed 1, succeeded 1, 9s 944ms. `Server_reads_an_old_runner_body_through_the_source_generated_contract` failed at tests/Antiphon.Tests/Agents/RunnerReadJsonClientTests.cs:57 (admitted PhoneBuffer, expected HttpBuffer). Phone-home passed. Slot 2ce8f265-62ba-4e1f-9d6e-07c3932eee16 waited=0s held=13s.

Golden at that tree, SessionRunner.Tests RunnerReadJsonTests: 3/3, 1s 086ms. Slot e98d72b3 waited=0s held=4s. Both encoders already escaped the ANSI sample as `\u001B`.

Green on the fix bytes before the fix commit: Antiphon.Tests 2/2, 4s 890ms, slot 81d930fa-fba1-450c-9582-eedb5b66a0c4 waited=0s held=8s. Golden 3/3, 889ms, slot 6daf14f5-1ef3-44be-8e59-37b8711f4c64 waited=0s held=4s.

Post-restore re-green on 249144215ccc45e7216fcb6b9c6e4a1cc52dd1dd after the mutants:

- RunnerReadJsonClientTests 1/1, 5s 036ms. Build slot 53d39163-b077-482b-b88d-70fac7bf4c4c waited=0s held=18s. Test slot 29d79cfd-dfff-482f-8581-312c0c7dd496 waited=0s held=9s.
- PhoneHomeReadAdmissionTests 1/1, 10s 463ms. Build slot 12256943-b12c-435b-9b17-6f5963f98f82 waited=135s held=194s. Test slot bd738df6-364a-4d2f-9e51-701beb6b8a5d waited=60s held=13s.

Guards, one run, 77/77, 1m 29s 003ms. Slot 3c129890-20db-4a8f-9761-04660da9fe87 waited=120s held=93s.

| Class | Count |
|---|---|
| TestClassificationPolicyTests | 41 |
| TestClassificationGuardTests | 1 |
| SlowTestRegistryMergeTests | 5 |
| SlowTestTripwireTests | 2 |
| NativeLinuxPolicyWitnessTests | 3 |
| MechanicalPcContractTests | 11 |
| TaskPlatformGuidanceTests | 5 |
| C1729LayeringGuardTests | 9 |

## Local mutants

Uncommitted, restored with `git checkout --` of the exact path, and not SourceLanding positive controls.

- HTTP: HttpOf returned PhoneBuffer, PhoneSnapshot, and PhoneTranscript. RunnerReadJsonClientTests.cs:57 failed, total 1, failed 1, 5s 940ms. Build slot a9e687ee-7fcd-4a09-9007-ac7a86fda16e waited=345s held=263s. Test slot 3b5a5345-31e8-4283-a30b-6d0f7c565235 waited=0s held=9s. Restored, then the re-green above.
- ANSI: the sample lost the ESC byte. RunnerReadJsonTests.cs:203 `\u001B` was false, total 1, failed 1, 868ms. Build slot 38946d82 waited=1s held=34s. Test slot a72384f1 waited=30s held=4s. Restored to the green source and not re-run.
- Phone-home: the three ReadElement calls became `Deserialize` through PhoneHomeFraming.Json. PhoneHomeReadAdmissionTests.cs:35 watch.Count was 0, expected 3, total 1, failed 1, 4s 848ms. Build slot f97d2672-e71d-4209-ac73-5cb9686b811c waited=0s held=355s. Test slot cdb6949e-158b-4f74-84c9-9278a6d07090 waited=76s held=8s. Restored, then the re-green above.

Phone-home already called ReadElement before this card, so that witness could not go red at the pre-fix commit. The reflection swap is its red proof.

## Owed

No PC-n rows were defined. Every PC stays pending for method-scoped SourceLanding Mutation. This round does not discharge any.
