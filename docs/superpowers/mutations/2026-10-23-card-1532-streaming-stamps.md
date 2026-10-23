# CARD-1532 unbound Mutation candidate

Code owner: `12dd82bc-d419-4247-af23-4ba6aff5cd04`. This is not a source-bound
pack and authorizes no mutation execution. Bind to the confirmed SourceLanding
snapshot after ordinary Review and landing. Every row/variant below is **pending**.
Use method-scoped red/restore/green runs; zero tests, build errors and fixture
failures are not successful controls. Never change a timeout or weaken an oracle.

| PC | Independent deliberate bypass | Exact method witness |
|---|---|---|
| PC-1 | Restore .NET regular stamp to ReadBytes | CheckpointLargeStampTests.C1532_regular_stamp_crosses_receipt_limit |
| PC-2a/b | Exclude prior bin / current obj from capture | CheckpointLargeStampTests.C1532_large_ignored_paths_remain_covered |
| PC-3a/b/c | Hash only a prefix / compare size+mtime / exempt tail-edited ignored paths | CheckpointLargeStampTests.C1532_tail_edit_with_restored_mtime_is_tampered |
| PC-4a/b | Skip second-build baseline / omit its after-release verification | CheckpointLargeStampTests.C1532_second_build_and_retry_admit_large_prior_output |
| PC-5a/b/c/d | Content bound 64 MiB / omit initial 1 GiB bound / omit growth bound / enlarge receipt cap | CheckpointLargeStampTests.C1532_content_and_receipt_limits_are_distinct |
| PC-6a/b/c/d/e/f/g/h | Bypass hard-link / regular / containment / cancellation / stall rearm / EOF growth / shrink / final-inspection refusal | CheckpointLargeStampTests.C1532_digest_preserves_verified_read_refusals |
| PC-7a/b/c | Restore PS stamp ReadBytes / prefix hash / accept missing or empty digest result | CheckpointLargeStampScriptTests.C1532_script_stamp_matches_full_digest |
| PC-8a/b/c/d/e/f/g/h/i | PS initial bound / growth bound / receipt bound / stall rearm / cancellation / EOF growth / shrink / inspection / unsafe handle admission | CheckpointLargeStampScriptTests.C1532_script_digest_limits_stalls_and_eof |
| PC-9a/b | Disable parser / ASCII validation | CheckpointLargeStampScriptTests.C1532_scripts_are_ascii_and_parse_in_pwsh |
| PC-10a/b | Disable actual Windows parser / ASCII validation, using scratch malformed/non-ASCII copies | scripts/test-checkpoint-stamp-ps51-parse.ps1 on Windows 5.1 Desktop |

Mutation owns missing-control discovery, including refining variants whose bypass
hits an earlier guard. The baseline intentional red proves V-1..V-4/V-7 only;
ordinary green cannot discharge any PC. Reuse the plan's R-1..R-4 evidence as
regression context, not as a substitute for each precise method's red/green cycle.
