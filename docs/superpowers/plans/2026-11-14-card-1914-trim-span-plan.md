# CARD-1914 span trim for the remaining TranscriptKinds checks

Invariant: `IsTaskNotificationPrompt`, `IsCompactionContinuationPrompt`,
`TryReadLocalCommandName`, and `IsRawLocalCommandEcho` keep the UserPrompt kind
gate and the ordinal `StartsWith` / `IndexOf` comparisons they have today.
Leading trim stays parameterless `TrimStart` (Unicode White_Space, not BOM
U+FEFF). A returned command name may allocate that name only. The prompt itself
is not copied. `IsInterruptPrompt` stays on `string.TrimStart` and is outside
this slice.

The private string-TrimStart oracles in `LocalCommandRecordTests` are the old
behavior. Allocation witnesses use `GC.GetAllocatedBytesForCurrentThread`, five
samples, empty-loop baseline subtracted. They fail on the pre-fix string copy.
`IsRawLocalCommandEcho` is witnessed on both `HashSet<string>` (the
`TranscriptPromptSpan` caller) and `List<string>` (the session-runtime caller).

Unbounded consumers of `TranscriptKinds` outside the named families stay with
nightly and rc. No migration, OS branch, timeout, or assertion change.
Impacted-class addition: editing `tests/Antiphon.Tests/**/*.cs` selects
`TestLaneCategoryGuardTests`, included in CP-10. No other high-radius kind
matches `SessionRunnerContracts.cs`.

CP-2 is red at the test-only commit (the five new allocation methods) and green
after the production edit. Count that rerun. Do not repeat a green row.

## Verification design

- V-1: old/new corpus equality for the local-record check (whitespace scalars,
  BOM, ZWSP, surrogate pair, mixed case, near misses, empty, long) and the same
  corpus for the four methods, including a non-list command collection.
- V-2: five-sample zero-byte witnesses for notification, compaction, command-name
  miss, and raw echo; command-name return allocates only the returned name.
- R-1: direct classifier classes plus the named server and runner families.
- R-2: brief guard union plus `TestLaneCategoryGuardTests`.
- R-3: derived-impact driver build, witness, and a second regeneration that
  prints unchanged. No nightly pin input changes.

### Checkpoints

Run through `tools/Antiphon.Checkpoints` with `--serial --row-timeout 9m` and
`--expected-source-sha` of the committed HEAD under test. Command rows own
their lease. CP-2 runs red on the test-only commit, then green after the fix.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-0 | all | n/a | impact-driver-build | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:1 --nologo` | R-3 | exit 0, standalone driver only | n/a | 2 | true |
| CP-1 | all | n/a | impact-driver-witness | `pwsh -NoProfile -File scripts/test-derived-impact-driver.ps1 -EvidenceRoot .antiphon/c1914-driver-witness` | R-3 | DRIVER WITNESS passed=3 failed=0 | n/a | 2 | true |
| CP-2 | all | `tests/Antiphon.Tests -> bin-c1914-proof/` | trim-span-proof | `/*/*/LocalCommandRecordTests/*` | V-1, V-2 | all listed, 0 failed/skipped after the fix | 11 | 10 | true |
| CP-3 | all | CP-2 | direct-classifiers | `/*/*/(TranscriptLocalCommandEchoTests*)\|(SubagentNotificationTests*)/*` | R-1 | all listed, 0 failed/skipped | 8 | 2 | true |
| CP-4 | all | CP-2 | compaction-notification | `/*/*/(CompactionContinuationPolicyTests*)\|(TranscriptNormalizerTests*)/*` | R-1 | all listed, 0 failed/skipped | 52 | 2 | true |
| CP-5 | all | CP-2 | prompt-working-contract | `/*/*/(TranscriptPromptSpanTests*)\|(TranscriptWorkingStateQueryTests*)\|(TaskReportHousekeepingTests*)/*` | R-1 | all listed, 0 failed/skipped | 32 | 3 | true |
| CP-6 | all | CP-2 | sibling-boundaries | `/*/*/SiblingToolReportBoundaryTests/*` | R-1 | all listed, 0 failed/skipped | 26 | 3 | true |
| CP-7 | all | CP-2 | running-converter | `/*/*/ChannelOutboundRecoveryTests/Running_converter_reconciles_after_dispatcher_death` | R-1 | exact method, 0 failed/skipped | 1 | 4 | true |
| CP-8 | all | CP-2 | publication-boundaries | `/*/*/ChannelOutboundRecoveryTests/Recovery_preserves_settled_worker_and_publication_boundaries` | R-1 | all six argument cases, 0 failed/skipped | 6 | 4 | true |
| CP-9 | all | CP-2 | channel-remainder | `/*/*/ChannelOutboundRecoveryTests/(Resume_held_after_conversion_returns_to_ready*)\|(Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed*)\|(Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head*)\|(Broker_ack_before_process_death_remains_uncertain_without_replay*)\|(Process_death_preserves_ownership_at_each_boundary*)\|(Definite_queue_refusal_retries_sealed_payload_but_ambiguous_failure_does_not*)\|(Expired_publishing_lease_is_uncertain_until_explicit_retry*)` | R-1 | all listed, 0 failed/skipped | 11 | 8 | true |
| CP-10 | all | CP-2 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-2 | all listed, 0 failed/skipped | 80 | 5 | true |
| CP-11 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c1914-runner/` | runner-working-state | `/*/*/TranscriptWorkingStateTests/*` | R-1 | all listed, 0 failed/skipped | 13 | 8 | true |
| CP-12 | all | CP-11 | terminal-seat | `/*/*/TerminalSeatReleaseTests/*` | R-1 | all listed, 0 failed/skipped | 34 | 9 | true |

`TranscriptCandidateProbe` has no test class of that name. The probe calls
`IsLocalCommandRecord`, which this card does not change. No zero-test row is
scheduled for that glob.
