# CARD-1032 S7 witness viability probe

Complete: 10 family mutations detected; 5 survived (witness gaps); 0 not yet run.

Production source: `cfd114a6d2b64a19cf3d4f8f62eda2ac03172a1c`. Reporting HEAD when this evidence was written is the documentation-only descendant `cd3e5263c464e14e112679d98c5ac2d2f2657a3c`; production and test sources are otherwise unchanged. Each per-family entry below records its actual execution commit. The baseline test-host binary was built at 1f870536edfcca5bc40ee48c11251746a639c7a6. Every production edit is throw-away and restored with `git checkout -- .`. No production change is committed.

Baseline: corrected exact-method OR selection executed all 14 CP-13/14 methods, 14 passed, zero failed/skipped. The initial OR without suffix wildcards executed zero tests (driver exit 3); it is invalid evidence. A single-method diagnostic reused that build and passed 1/1.

Each mutated state gets exactly one isolated affected-production-project build through `scripts/build-slot.ps1`, `UseAppHost=false`, `OutputPath=bin-s7vp045e9719/`. The existing baseline test host is reused without compiling it; the changed module DLL/PDB is copied from its just-built output. SHA-256 of the production DLL and loaded test-host DLL are equal and differ from baseline. The other two relevant production modules are restored from baseline before/after every family. Only method bodies change, with no public API change. Tests run through a second leased `build-slot.ps1 -- dotnet run --no-build` call, source frozen throughout both drivers. Exact commands, hashes, full diffs, logs and fresh TRX are ignored under `.antiphon/s7-viability-045e9719/`; these are scratch evidence, not clean-source checkpoint receipts.

All formal PCs, per-field variants, SourceLanding Mutation, native Windows qualification and S8 remain pending. This narrowly commissioned family probe discharges none of them. Constant-time comparison cannot be established by these witnesses and is skipped. Caller proof fields are not an input to the current producer API; server-record guard mutations below test the relevant authority boundary.

| Family | Mutation | Witness method(s) | RED/GREEN | Exact assertion message |
|---|---|---|---|---|
| VP-01 Mirror HEAD | new CompletedCardWorkspaceProof(inspection.HeadSha, → new CompletedCardWorkspaceProof(observed.Sha!, | `C1017_MirrorBehindPushedTip` | RED | C1017_MirrorBehindPushedTip: ShouldAssertException: await TryAsync(); result.Complete<br>    should be<br>True<br>    but was<br>False<br><br>Additional Info:<br>    head_mismatch |
| VP-02 Done generation | if (card.Status != CardStatus.Done \|\| revision != endpoint.Target.Cleanup.DoneRevisionId) return "done_generation_revoked"; → // scratch: omit Done-generation admission | `C1017_ReopenBetweenEndpoints` | RED | C1017_ReopenBetweenEndpoints: ShouldAssertException: await TryAsync(); result.Complete<br>    should be<br>False<br>    but was<br>True |
| VP-03 Distinct mirror bindings | bindings.Where(b => b.TaskId == task.Id) → bindings.Where(b => b.TaskId == task.Id).OrderBy(b => b.WorktreePath.Length).Take(1) | `C1017_MultipleMirrorsRemainDistinct` | RED | C1017_MultipleMirrorsRemainDistinct: ShouldAssertException: rows.Count<br>    should be<br>2<br>    but was<br>1 |
| VP-04 Current task eligibility | if (task.Status is not (AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled)) return "terminal_required"; → // scratch: omit terminal status predicate | `C1032_AdmissionCurrentEligibility` | RED | C1032_AdmissionCurrentEligibility: ShouldAssertException: await TryAsync(); result.Complete<br>    should be<br>False<br>    but was<br>True |
| VP-05 External artifact bytes | if (pointer == task.ResultFilePath && !bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(task.Result))) → if (false) | `C1032_AdmissionArtifactSurvival` | RED | C1032_AdmissionArtifactSurvival: ShouldAssertException: await TryAsync(); result.Complete<br>    should be<br>False<br>    but was<br>True |
| VP-06 Commit intent before send | await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); → await db.SaveChangesAsync(ct); // scratch: transaction rolls back without commit | `C1032_AdmissionIntentOrdering` | RED | C1032_AdmissionIntentOrdering: ShouldAssertException: await f.EndpointAsync(); row.OperationId<br>    should not be null but was |
| VP-07 Forged/unsigned certificate refusal | !CompletedCardWorkspaceLandingAuthority.Verify(command, landingMapKey) → false | `C1032_AdmissionBoundRebaseProof` | RED | C1032_AdmissionBoundRebaseProof: ShouldAssertException: refusal.Complete<br>    should be<br>False<br>    but was<br>True |
| VP-08 Certificate covers task identity | var digest = CompletedCardWorkspaceContract.CanonicalDigest(JsonSerializer.Serialize(command, → command = command with { Binding = command.Binding with { TaskId = Guid.Empty } };<br>        var digest = CompletedCardWorkspaceContract.CanonicalDigest(JsonSerializer.Serialize(command, | `C1032_AdmissionBoundRebaseProof` | **GREEN: FINDING** | None; all selected witnesses passed under the mutation. |
| VP-09 Publication belongs to task | o => o.TaskId == task.Id && o.Active → o => o.Active | `C1032_AdmissionBoundRebaseProof` | **GREEN: FINDING** | None; all selected witnesses passed under the mutation. |
| VP-10 Landing evidence required | op.Id == id && new AgentTaskLandingState().HasPublication(op) → op.Id == id | `C1032_AdmissionBoundRebaseProof` | **GREEN: FINDING** | None; all selected witnesses passed under the mutation. |
| VP-11 Admission-required inspection continues | inspection.Reason is not null && inspection.Reason != "evidence_requires_admission" → inspection.Reason is not null | `C1017_MirrorBehindPushedTip` | RED | C1017_MirrorBehindPushedTip: ShouldAssertException: await TryAsync(); result.Complete<br>    should be<br>True<br>    but was<br>False<br><br>Additional Info:<br>    evidence_requires_admission |
| VP-12 Valid bound-rebase path succeeds | if (rebased) command = CompletedCardWorkspaceLandingAuthority.Sign(command, publication!.Id, phoneHome!.Value.SharedSecret); → if (rebased) return Hold("preservation_proof_unsupported"); | `C1032_AdmissionBoundRebaseProof` | RED | C1032_AdmissionBoundRebaseProof: ShouldAssertException: await TryAsync(); result.Complete<br>    should be<br>True<br>    but was<br>False<br><br>Additional Info:<br>    preservation_proof_unsupported |
| VP-13 Immutable persisted request digest | !CompletedCardWorkspaceContract.Validate(json).IsValid \|\| CompletedCardWorkspaceContract.CanonicalDigest(json) != endpoint.RequestDigest → !CompletedCardWorkspaceContract.Validate(json).IsValid | `C1032_AdmissionIntentOrdering`, `C1032_ReceiptCompleteBoundReceipt` | **GREEN: FINDING** | None; all selected witnesses passed under the mutation. |
| VP-14 Exact receipt plus ACK-only refusal | response.Reason is not null \|\| response.Receipt != expected → response.Reason is not null | `C1017_ReceiptMustMatchOperation`, `C1017_ReceiptMustMatchEndpoint`, `C1017_ReceiptMustMatchStore`, `C1017_ReceiptMustMatchTaskAttempt`, `C1017_AckIsNotRemoval`, `C1032_ReceiptCompleteBoundReceipt` | RED | C1032_ReceiptCompleteBoundReceipt: ShouldAssertException: await f.TryAsync(); reply.Complete<br>    should be<br>False<br>    but was<br>True<br><br>Additional Info:<br>    schemaVersion<br>C1017_ReceiptMustMatchEndpoint: ShouldAssertException: await f.TryAsync(); reply.Complete<br>    should be<br>False<br>    but was<br>True<br><br>Additional Info:<br>    endpointId<br>C1017_ReceiptMustMatchStore: ShouldAssertException: await f.TryAsync(); reply.Complete<br>    should be<br>False<br>    but was<br>True<br><br>Additional Info:<br>    runnerId<br>C1017_ReceiptMustMatchOperation: ShouldAssertException: await f.TryAsync(); reply.Complete<br>    should be<br>False<br>    but was<br>True<br><br>Additional Info:<br>    operationId<br>C1017_ReceiptMustMatchTaskAttempt: ShouldAssertException: await f.TryAsync(); reply.Complete<br>    should be<br>False<br>    but was<br>True<br><br>Additional Info:<br>    taskId<br>C1017_AckIsNotRemoval: ShouldAssertException: (await f.TryAsync()).Reason<br>    should be<br>"receipt_mismatch"<br>    but was<br>null |
| VP-15 Unsupported peer fails closed | if (!response.Complete) return await RefuseAsync(endpoint, response.Reason ?? "receipt_mismatch", ct); → if (!response.Complete) return response.Reason == PhoneHomeProblemTypes.UnsupportedOperation ? new(true, null, null) : await RefuseAsync(endpoint, response.Reason ?? "receipt_mismatch", ct); | `C1017_UnsupportedNeverFallsBack` | **GREEN: FINDING** | None; all selected witnesses passed under the mutation. |

Finding VP-08: The MAC normalizes binding.TaskId to Guid.Empty before hashing. The nine tampering vectors change other fields, so this method does not detect loss of TaskId signature coverage. Separate binding validation may still refuse a changed TaskId; no exploit is claimed. Add a direct certificate Verify negative for this omitted field.

Finding VP-09: PublicationAsync no longer scopes active landing records by TaskId. The method creates only a valid same-task publication, with no foreign-task negative vector.

Finding VP-10: PublicationAsync no longer requires AgentTaskLandingState.HasPublication. Eligibility retains its narrower publication outcome/timestamp checks, but the full identity/verification/certificate evidence predicate is no longer required. The method exercises only genuine complete landing evidence.

Finding VP-13: ReadCommand accepts a valid-shaped persisted request whose digest differs from RequestDigest. Neither selected method re-enters TryAsync with a tampered existing intent; the replay guard is not exercised.

Finding VP-15: SendAsync manufactures Complete for an unsupported removal response. The existing unsupported-peer case fails inspection first and never reaches this removal-response arm. Inspection refusal remains covered; unsupported removal after a successful inspection does not.

GREEN is a gap in these S7 witnesses, not evidence of a defect in the restored production guard. The original code retains every guard. A surviving mutation needs a negative vector before its safety claim can be qualified.

VP-01: 1 executed, 0 passed, 1 failed, zero skipped; native test exit 2; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/C1017_MirrorBehindPushedTip`. Production path: `server/Application/Services/CardWorktreeMirrorCleanupService.cs`. Execution commit: `1f870536edfcca5bc40ee48c11251746a639c7a6`.

VP-02: 1 executed, 0 passed, 1 failed, zero skipped; native test exit 2; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReopenBetweenEndpoints`. Production path: `server/Application/Services/CardWorktreeMirrorCleanupService.cs`. Execution commit: `1f870536edfcca5bc40ee48c11251746a639c7a6`.

VP-03: 1 executed, 0 passed, 1 failed, zero skipped; native test exit 2; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/C1017_MultipleMirrorsRemainDistinct`. Production path: `server/Application/Services/CardWorktreeCleanupService.cs`. Execution commit: `9c7eabadab9c525fb4ac41c980d0c4471b1be895`.

VP-04: 1 executed, 0 passed, 1 failed, zero skipped; native test exit 2; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionCurrentEligibility`. Production path: `server/Application/Services/CardWorktreeMirrorCleanupService.cs`. Execution commit: `1f870536edfcca5bc40ee48c11251746a639c7a6`.

VP-05: 1 executed, 0 passed, 1 failed, zero skipped; native test exit 2; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival`. Production path: `server/Infrastructure/Files/CardDoneArtifactPreservation.cs`. Execution commit: `1f870536edfcca5bc40ee48c11251746a639c7a6`.

VP-06: 1 executed, 0 passed, 1 failed, zero skipped; native test exit 2; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionIntentOrdering`. Production path: `server/Application/Services/CardWorktreeMirrorCleanupService.cs`. Execution commit: `1f870536edfcca5bc40ee48c11251746a639c7a6`.

VP-07: 1 executed, 0 passed, 1 failed, zero skipped; native test exit 2; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionBoundRebaseProof`. Production path: `src/Antiphon.SessionRunner/RunnerCompletedCardWorkspaceRemoval.cs`. Execution commit: `9c7eabadab9c525fb4ac41c980d0c4471b1be895`.

VP-08: 1 executed, 1 passed, 0 failed, zero skipped; native test exit 0; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionBoundRebaseProof`. Production path: `src/Antiphon.SessionRunner.Contracts/CompletedCardWorkspaceLandingAuthority.cs`. Execution commit: `9c7eabadab9c525fb4ac41c980d0c4471b1be895`.

VP-09: 1 executed, 1 passed, 0 failed, zero skipped; native test exit 0; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionBoundRebaseProof`. Production path: `server/Application/Services/CardWorktreeMirrorCleanupService.cs`. Execution commit: `9c7eabadab9c525fb4ac41c980d0c4471b1be895`.

VP-10: 1 executed, 1 passed, 0 failed, zero skipped; native test exit 0; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionBoundRebaseProof`. Production path: `server/Application/Services/CardWorktreeMirrorCleanupService.cs`. Execution commit: `9c7eabadab9c525fb4ac41c980d0c4471b1be895`.

VP-11: 1 executed, 0 passed, 1 failed, zero skipped; native test exit 2; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/C1017_MirrorBehindPushedTip`. Production path: `server/Application/Services/CardWorktreeMirrorCleanupService.cs`. Execution commit: `cd3e5263c464e14e112679d98c5ac2d2f2657a3c`.

VP-12: 1 executed, 0 passed, 1 failed, zero skipped; native test exit 2; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionBoundRebaseProof`. Production path: `server/Application/Services/CardWorktreeMirrorCleanupService.cs`. Execution commit: `cd3e5263c464e14e112679d98c5ac2d2f2657a3c`.

VP-13: 2 executed, 2 passed, 0 failed, zero skipped; native test exit 0; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/(C1032_AdmissionIntentOrdering*)|(C1032_ReceiptCompleteBoundReceipt*)`. Production path: `server/Application/Services/CardWorktreeMirrorCleanupService.cs`. Execution commit: `cd3e5263c464e14e112679d98c5ac2d2f2657a3c`.

VP-14: 6 executed, 0 passed, 6 failed, zero skipped; native test exit 2; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/(C1017_ReceiptMustMatchOperation*)|(C1017_ReceiptMustMatchEndpoint*)|(C1017_ReceiptMustMatchStore*)|(C1017_ReceiptMustMatchTaskAttempt*)|(C1017_AckIsNotRemoval*)|(C1032_ReceiptCompleteBoundReceipt*)`. Production path: `server/Application/Services/CardWorktreeMirrorCleanupService.cs`. Execution commit: `cd3e5263c464e14e112679d98c5ac2d2f2657a3c`.

VP-15: 1 executed, 1 passed, 0 failed, zero skipped; native test exit 0; build exit 0. Filter: `/*/*/CardWorktreeMirrorCleanupTests/C1017_UnsupportedNeverFallsBack`. Production path: `server/Application/Services/CardWorktreeMirrorCleanupService.cs`. Execution commit: `cd3e5263c464e14e112679d98c5ac2d2f2657a3c`.

Exact minimal source changes (full context diffs retained as VP-nn `.diff` files):

VP-01, `server/Application/Services/CardWorktreeMirrorCleanupService.cs`:

```diff
-new CompletedCardWorkspaceProof(inspection.HeadSha,
+new CompletedCardWorkspaceProof(observed.Sha!,
```

VP-02, `server/Application/Services/CardWorktreeMirrorCleanupService.cs`:

```diff
-if (card.Status != CardStatus.Done || revision != endpoint.Target.Cleanup.DoneRevisionId) return "done_generation_revoked";
+// scratch: omit Done-generation admission
```

VP-03, `server/Application/Services/CardWorktreeCleanupService.cs`:

```diff
-bindings.Where(b => b.TaskId == task.Id)
+bindings.Where(b => b.TaskId == task.Id).OrderBy(b => b.WorktreePath.Length).Take(1)
```

VP-04, `server/Application/Services/CardWorktreeMirrorCleanupService.cs`:

```diff
-if (task.Status is not (AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled)) return "terminal_required";
+// scratch: omit terminal status predicate
```

VP-05, `server/Infrastructure/Files/CardDoneArtifactPreservation.cs`:

```diff
-if (pointer == task.ResultFilePath && !bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(task.Result)))
+if (false)
```

VP-06, `server/Application/Services/CardWorktreeMirrorCleanupService.cs`:

```diff
-await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
+await db.SaveChangesAsync(ct); // scratch: transaction rolls back without commit
```

VP-07, `src/Antiphon.SessionRunner/RunnerCompletedCardWorkspaceRemoval.cs`:

```diff
-!CompletedCardWorkspaceLandingAuthority.Verify(command, landingMapKey)
+false
```

VP-08, `src/Antiphon.SessionRunner.Contracts/CompletedCardWorkspaceLandingAuthority.cs`:

```diff
-var digest = CompletedCardWorkspaceContract.CanonicalDigest(JsonSerializer.Serialize(command,
+command = command with { Binding = command.Binding with { TaskId = Guid.Empty } };
+        var digest = CompletedCardWorkspaceContract.CanonicalDigest(JsonSerializer.Serialize(command,
```

VP-09, `server/Application/Services/CardWorktreeMirrorCleanupService.cs`:

```diff
-o => o.TaskId == task.Id && o.Active
+o => o.Active
```

VP-10, `server/Application/Services/CardWorktreeMirrorCleanupService.cs`:

```diff
-op.Id == id && new AgentTaskLandingState().HasPublication(op)
+op.Id == id
```

VP-11, `server/Application/Services/CardWorktreeMirrorCleanupService.cs`:

```diff
-inspection.Reason is not null && inspection.Reason != "evidence_requires_admission"
+inspection.Reason is not null
```

VP-12, `server/Application/Services/CardWorktreeMirrorCleanupService.cs`:

```diff
-if (rebased) command = CompletedCardWorkspaceLandingAuthority.Sign(command, publication!.Id, phoneHome!.Value.SharedSecret);
+if (rebased) return Hold("preservation_proof_unsupported");
```

VP-13, `server/Application/Services/CardWorktreeMirrorCleanupService.cs`:

```diff
-!CompletedCardWorkspaceContract.Validate(json).IsValid || CompletedCardWorkspaceContract.CanonicalDigest(json) != endpoint.RequestDigest
+!CompletedCardWorkspaceContract.Validate(json).IsValid
```

VP-14, `server/Application/Services/CardWorktreeMirrorCleanupService.cs`:

```diff
-response.Reason is not null || response.Receipt != expected
+response.Reason is not null
```

VP-15, `server/Application/Services/CardWorktreeMirrorCleanupService.cs`:

```diff
-if (!response.Complete) return await RefuseAsync(endpoint, response.Reason ?? "receipt_mismatch", ct);
+if (!response.Complete) return response.Reason == PhoneHomeProblemTypes.UnsupportedOperation ? new(true, null, null) : await RefuseAsync(endpoint, response.Reason ?? "receipt_mismatch", ct);
```

Unedited baseline CHECKPOINT lines:

```text
CHECKPOINT VP-baseline-corrected commit=1f870536edfcca5bc40ee48c11251746a639c7a6 build=reused filter=/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorBehindPushedTip*)|(C1017_ReopenBetweenEndpoints*)|(C1017_MultipleMirrorsRemainDistinct*)|(C1032_AdmissionCurrentEligibility*)|(C1032_AdmissionArtifactSurvival*)|(C1032_AdmissionIntentOrdering*)|(C1032_AdmissionBoundRebaseProof*)|(C1017_ReceiptMustMatchOperation*)|(C1017_ReceiptMustMatchEndpoint*)|(C1017_ReceiptMustMatchStore*)|(C1017_ReceiptMustMatchTaskAttempt*)|(C1017_AckIsNotRemoval*)|(C1017_UnsupportedNeverFallsBack*)|(C1032_ReceiptCompleteBoundReceipt*) executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-045e9719/.antiphon/s7-viability-045e9719/baseline-corrected/VP-baseline-corrected-20261013-153852-49eb/run.trx slot=granted waited=15s dirty=0 source=1f870536edfcca5bc40ee48c11251746a639c7a6 sourceState=clean buildSource=verified
CHECKPOINT VP-baseline-corrected EXIT CODE: 0
```

The initial VP-03 runner-key collapse removed the current binding during fixture discovery and failed with `InvalidOperationException: Sequence contains no matching element`. This is an invalid fixture failure, not witness red; evidence is preserved under `03-invalid`. The corrected mutation retains the shortest/current mirror path and drops the historical path, so the endpoint-count oracle is reachable.

Restored-green and final cleanup: pending.

--- next stage ---
next: decide
handoff: Commission focused witness repairs for surviving guard mutations before claiming S7 authority coverage; keep every systematic PC pending.
artifact: docs/superpowers/mutations/2026-10-13-card-1032-s7-viability-probe.md
