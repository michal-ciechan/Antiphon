# Review CARD-1762 (CARD-1758 slice 2), round 1 Final: CLEAN

Subject: Code task `4142c507-a1be-42b8-86a9-553ede581529` (Grok), branch `feat/card-task-4142c507`,
tip `4f676427af5206a7a13bdd3439ac00c6578dc4da`, base `9e7ff34988cd8c273d94db3c002975d93ea26f9e`,
red `e62c70951f40a54c4217684caaac2ed94803188c`. Ten files, +446/-14. Reviewed read-only on the
runner mirror `/work/worktrees/task-b46848cb`; nothing ran on the desktop; no product file changed.

## Verdict

No blockers. Source-generated JSON for the runner buffer, snapshot and transcript bodies is
byte-identical to the previous reflection serializers on both the HTTP and phone-home paths, old and
new peers read each other in both directions, the originating-resolver guard throws, phone-home framing
and the SSE/event decode are untouched, and the witnesses were red by assertion at the red commit.

## Verification (one checkpoint run, all leased)

Run `20261108-013045-4669`, plan `.antiphon/c1762-review-plan.md` (ignored), expected source SHA
`4f676427af5206a7a13bdd3439ac00c6578dc4da`; `validate` printed `CHECKPOINT SOURCE VALID ... rows=6`.
Builds: `bin-c1762-rv-sr` (SessionRunner.Tests, 152 s) and `bin-c1762-rv-srv` (Antiphon.Tests, 998 s on a
host at load 12-26); both reclaimed by the tool. Wall 29m20s. Unlisted runs: the checkpoint tool's own
bootstrap build (`build-slot.ps1 -Label c1762-review-checkpoints-tool`, 14 s).

```
CHECKPOINT CP-1 commit=4f676427af5206a7a13bdd3439ac00c6578dc4da build=ok filter=/*/*/RunnerReadJsonTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-b46848cb/.antiphon/checkpoints/20261108-013045-4669/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=4f676427af5206a7a13bdd3439ac00c6578dc4da sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=4f676427af5206a7a13bdd3439ac00c6578dc4da build=ok filter=/*/*/RunnerReadJsonClientTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b46848cb/.antiphon/checkpoints/20261108-013045-4669/rows/CP-2/run.trx slot=granted waited=90s dirty=0 source=4f676427af5206a7a13bdd3439ac00c6578dc4da sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=4f676427af5206a7a13bdd3439ac00c6578dc4da build=reused filter=/*/*/(TestClassificationPolicyTests*)|(C1729LayeringGuardTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(SlowTestRegistryMergeTests*)|(NativeLinuxPolicyWitnessTests*)|(SlowTestTripwireTests*)|(TestClassificationGuardTests*)/* executed=75 passed=75 failed=0 skipped=0 trx=/work/worktrees/task-b46848cb/.antiphon/checkpoints/20261108-013045-4669/rows/CP-3/run.trx slot=granted waited=105s dirty=0 source=4f676427af5206a7a13bdd3439ac00c6578dc4da sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=4f676427af5206a7a13bdd3439ac00c6578dc4da build=reused filter=/*/*/PhoneHomeSessionRoutingTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-b46848cb/.antiphon/checkpoints/20261108-013045-4669/rows/CP-4/run.trx slot=granted waited=315s dirty=0 source=4f676427af5206a7a13bdd3439ac00c6578dc4da sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=4f676427af5206a7a13bdd3439ac00c6578dc4da build=reused filter=/*/*/HerdrSnapshotBoundedReadTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-b46848cb/.antiphon/checkpoints/20261108-013045-4669/rows/CP-5/run.trx slot=granted waited=15s dirty=0 source=4f676427af5206a7a13bdd3439ac00c6578dc4da sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=4f676427af5206a7a13bdd3439ac00c6578dc4da build=reused filter=/*/*/PhoneHomeConnectionTests/* executed=29 passed=29 failed=0 skipped=0 trx=/work/worktrees/task-b46848cb/.antiphon/checkpoints/20261108-013045-4669/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=4f676427af5206a7a13bdd3439ac00c6578dc4da sourceState=clean buildSource=verified
```

CP-1, CP-2 and CP-3 are the Code-claimed witnesses and guard batch (counts match the Code report:
3, 1, 75). CP-4, CP-5 and CP-6 are directly affected consumers of the changed read paths
(`PhoneHomeRunnerClient` buffer/snapshot/transcript reads, dispatcher snapshot framing, the client over a
live connection). `scripts/check-evidence-diff.ps1` over `9e7ff34988..4f676427af`: commits=2 entries=0
violations=0, exit 0. `git merge-tree --write-tree origin/master HEAD` against master
`74a8004f405bc54666fea33889839f0ff4d62ec7`: clean, tree `7ef89bfece342e91647ff30faa833d7897f6b812`.
Subject branch tip confirmed by `ls-remote` as `4f676427af5206a7a13bdd3439ac00c6578dc4da`.

## Brief items

1. **Wire equivalence.** Old HTTP writer: `Results.Ok` under the runner's unconfigured ASP.NET
   `JsonOptions` (Web defaults plus `UnsafeRelaxedJsonEscaping`; no `ConfigureHttpJsonOptions` or
   `AddJsonOptions` exists in `src/Antiphon.SessionRunner`). New: `RunnerReadJsonContext` over
   `new JsonSerializerOptions(Web) { Encoder = UnsafeRelaxedJsonEscaping }`. Old phone-home writer:
   `PhoneHomeFraming.Json` = Web defaults; new: context over `new(Web)`. Old server readers:
   `SessionRunnerHttpClient.JsonOptions` (Web) and `PhoneHomeFraming.Json` (Web); new: the same context
   (encoder is irrelevant on read). Option by option: camelCase naming, `DefaultIgnoreCondition.Never`,
   `NumberHandling.AllowReadingFromString`, zero converters (no `JsonStringEnumConverter`, so
   `ProviderDialogState` stays numeric in both), `WriteIndented=false`, `IncludeFields=false`,
   `PropertyNameCaseInsensitive=true`. The three roots and their closure (`RunnerTranscriptEvent`,
   `RunnerTranscriptReadiness`, `ProviderDialogObservation`, `SubmittedInputProof`, enum, list) carry no
   `[JsonConverter]`, `[JsonPropertyName]`, `[JsonIgnore]`, polymorphism or `object`/`JsonElement`
   members, so the same built-in converters produce the same bytes (Guid `D`, DateTime/DateTimeOffset
   ISO-8601, longs, nulls written). The golden test pins bytes for null-optional, empty-list, unicode
   (CJK, quotes, backslash, newline, emoji surrogate pair), and 256 KiB bodies, with dates and both enum
   values, for all six writer combinations, and round-trips old->new and new->old by bytes. The server
   HTTP client test reads a reflection-written old-runner body for all three routes. Old server reading
   a new runner: bytes are identical, and `Same()` also deserializes the generated bytes with the old
   options. Both peers skip unknown members (default `UnmappedMemberHandling`), so the additive-optional
   contract in `SessionRunnerContracts.cs` still holds.
2. **Missing-type fallback / trimming.** Every entry point takes a typed `JsonTypeInfo<T>` from the
   context's generated properties, so there is no runtime type lookup that could fall back to `{}`;
   a new nested type is generated transitively at compile time, and an `object` member would throw
   `NotSupportedException` rather than serialize silently (none exist). No `PublishTrimmed`,
   `IsAotCompatible` or `JsonSerializerIsReflectionEnabledByDefault` is set anywhere; the SessionRunner
   checkpoint build log has no `SYSLIB`/`IL` warning and no warning in the Contracts project.
3. **Resolver check and framing.** `RunnerReadJson.Use<T>` throws `InvalidOperationException` for a
   type info whose `OriginatingResolver` is not the context (`Source_generated_contract_rejects_a_reflection_type_info`
   green here). The typed `Result<T>` overload in `PhoneHomeCommandDispatcher` builds the same
   `PhoneHomeFrame(Result, epoch, requestId, operation, element)` as the untyped one; the frame itself is
   still written by `PhoneHomeFraming`, and the embedded element is pre-escaped with the same Web encoder.
4. **SSE and one-pass decode.** `Program.cs` `/events` (`data: {evt.Json}`), `PhoneHomeFraming.cs`,
   `PhoneHomeLiveConnection.cs` and `SessionRunnerRuntime.cs` have no diff in the range; the server
   still parses events via `GetRawText()` exactly as before.
5. **Witnesses.** The red commit already carried `RunnerReadJson` but left the route helpers on
   `Results.Ok` and both clients and the dispatcher on reflection, so `ShouldBeOfType<JsonHttpResult<T>>`
   and `watch.Count` (0 vs 3) failed by assertion in both assemblies; the green commit's only test edit
   adds `RequestServices` (logging + `JsonOptions`) that `JsonHttpResult<T>.ExecuteAsync` needs, not an
   assertion change. The four Code mutants each map to a named assertion (WhenWritingNull -> golden
   bytes; resolver check removed -> `Should.Throw`; `Results.Ok` -> `ShouldBeOfType`; reflection client
   reads -> `watch.Count`). Mutants were not re-executed in this round (60-minute budget).

Delivery contract: transcript rows and snapshots are byte-identical, so the UserPrompt evidence verdict
(`docs/session-runtime-invariants.md`) is unaffected; no cache was added, staleness is unchanged.
No OS-selected behaviour changed (no OS-branch witness obligation). No timeout was added or changed.

## Advisories (no Review rerun needed)

- **A-1 naming.** `SessionRunnerHttpClient` reads with `RunnerReadJson.PhoneBuffer/PhoneSnapshot/PhoneTranscript`.
  Behaviour is identical on read, but the name suggests the phone-home encoder governs HTTP; expose
  read-neutral names or use the `Http` properties on the HTTP client.
- **A-2 unwitnessed admission.** No test asserts `RunnerReadJson.Watch().Count` across
  `PhoneHomeRunnerClient.GetBufferAsync/GetSnapshotAsync/GetTranscriptAsync`; reverting those three
  reads to `Read<T>` passes every test (the Code report notes the gap). `PhoneHomeConnectionTests`
  (29 green here) already drives that client over a live connection and can host the assertion.
- **A-3 golden gaps.** The samples lack an ESC (`\u001b`) / CSI sequence and `\r`, although the buffer
  body is literally the ANSI tail, and every `DateTime` is `Kind=Utc`. No wire risk today (shared
  converters), but the golden set is the regression net for later option edits.
- **A-4 test hook in production.** The `AsyncLocal` witness and `WitnessScope` live in the Contracts
  assembly and cost one `AsyncLocal` read per hot call; negligible, but a test-side counting resolver
  would keep the production surface clean.
- **A-5 decision remainder.** Decision slice 2 also named event records, `PhoneHomeFrame` and one-pass
  payload decode; this slice leaves them on reflection and the phone-home client still parses frame ->
  element -> DTO. A follow-up card is needed if the caller wants the rest of slice 2.
- **A-6 inert attribute.** The `[JsonSourceGenerationOptions]` values govern only
  `RunnerReadJsonContext.Default`, which nothing uses; both live contexts take explicit options. A
  one-line comment would stop a future edit of the attribute from being mistaken for a wire change.
- **Timing note.** CP-3 took 236 s (Code: 164 s) under load; the tool flagged
  `TestClassificationPolicyTests` at 198 s for 39 tests (`SLOW CLASS`), pre-existing and unrelated.

## Scope

Commissioned selection ran in full: both witness classes and the 75-test guard batch, plus three
affected consumer classes. The whole Unit lane was not run under this brief's 60-minute budget.
PCs: the plan defines none; nothing was discharged; method-scoped SourceLanding Mutation owns any later
positive control.
