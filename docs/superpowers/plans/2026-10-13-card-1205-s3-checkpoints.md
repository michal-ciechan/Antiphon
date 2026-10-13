# CARD-1205 S3 checked recovery

Repair Code/landing owner: 080e0111-220a-4633-bc3e-efa40d4016ff.
Original S3 Code task: 3e736c72-542c-472a-b6c9-1dcd367c7a36.
Repair base: 742e3404c26df504e30727a6178f66076cb41485.
Scope: S3 only; frozen T10..T16, V-8..12,V-25,V-26,R-2,R-7.
The explicit slice brief and frozen design exclude a broad Unit lane.
S4/S5 own scheduling, flush callbacks and recipient receipts. No receipt claim in S3.

Baseline uses a temporary test-only extension that calls the existing real
CatchUpForReceiptAsync path. It derives observations from actual committed state;
the implementation commit removes the extension and calls the checked API.
Rows protecting existing persistence semantics can be baseline green controls;
missing guards must fail outcome/no-append assertions, not fixture/build errors.
All PC-11..40 and variants remain pending for method-scoped SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| F1-BASE | Tests | `tests/Antiphon.Tests -> bin-c1205-f1-base/` | late-generation-baseline | `/*/Antiphon.Tests.Application/WorkingTranscriptRecoveryTests*/C1205_Generation_changed_after_validation_must_not_append_old_end*` | F1 | One guarded assertion red on unchanged 742e3404 production | 1 | 6 |
| CP-1 | Repair | `tests/Antiphon.Tests -> bin-c1205-f1-final/` | gap-baseline | `/*/Antiphon.Tests.Application/TurnEndGapReproductionTests*/*` | V-1,V-2,R-1 | Exactly 2 green | 2 | 6 |
| CP-2 | Repair | `tests/Antiphon.Tests -> bin-c1205-f1-final/` | subscription | `/*/Antiphon.Tests.Application/(TurnEndGapReproductionTests*)\|(SessionRunnerStreamReadinessTests*)\|(SessionRunnerEventPumpTests*)\|(SessionRunnerHeldEventDeliveryTests*)\|(SessionRunnerHeldEventExitMatrixTests*)/*` | V-1..7,R-1,R-6 | Exactly 211 green | 211 | 10 |
| ADJ-1 | Repair | `tests/Antiphon.Tests -> bin-c1205-f1-final/` | adjacent | `/*/*/(SessionRunnerHttpClientHostStatsTests*)\|(SessionRunnerGenerationWireTests*)\|(SessionRunnerSettingsTests*)\|(SessionStateCommitTests*)\|(TranscriptWorkingStateQueryTests*)/*` | wire and working-state compatibility | Exactly 33 green | 33 | 3 |
| CP-3 | Repair | `tests/Antiphon.Tests -> bin-c1205-f1-final/` | checked-recovery | `/*/Antiphon.Tests.Application/(WorkingTranscriptRecoveryTests*)\|(SessionStateCommitTests*)\|(TranscriptWorkingStateQueryTests*)/*` | V-8..12,V-25,V-26,R-2,R-7,F1,V-F1..6 | Exactly 74 green: original 62 plus 12 repair rows | 74 | 11 |

## Test-first baseline

At 459f3c500566679d0a85aa4095d9e5303e9cb85a, unchanged pre-S3 production
ran 62 executions: 33 assertion reds, 29 controls green, zero skipped or fixture
errors. T10..T16 use real HTTP mapping, persistence and state readers. The nine
new baseline-green variants protect already-landed persistence/rejection behavior;
they are compatibility controls, with mutant proof still pending. Two earlier
diagnostic runs found fixture errors in incomplete remote bindings; the valid
all-or-none binding fixes those. The final API has no baseline adapter.

The result method returns after its using-owned lease disposes (T13/T15).
S4 owns scheduling and ordinary completion flush work; S5 owns recipient witnesses.
No delivery receipt, operational restart, bounded payload size or self-consistent
truncated-history detection is claimed by this slice.

Implementation verification: run CP-3 with the exact committed HEAD as expected
source SHA. Preserve receipts and all expanded method/argument counts in
.antiphon/task-3e736c72.md.

## F1 repair decision and precondition audit

Use the existing session-row FOR UPDATE idiom, with a checked-only transaction
in PersistTranscriptCoreAsync spanning the deciding read and every append save.
This is the SQL-fence form of option (2): validate under the row lock in the same
transaction that appends, rather than change every lifecycle writer. An UPDATE
or DELETE by resume/restart/start/kill/replace conflicts with this lock. If it
commits first, the reread rejects; if recovery locks first, the writer follows
recovery's durable commit. Publication follows the committed SQL transaction and
represents those committed rows, under the still-held state lease. A later
lifecycle update is ordered after that durable append, not a failed precommit
fence; the existing fresh observation may conservatively return Unknown.
No Agents or registration locks are acquired; no I/O occurs inside a monitor.
Lock order: optional existing queue lock -> state async lease -> SQL session row;
probe takes no queue lock and calls no queue callback. The overlay's row lock
uses the same fence. Checked batch/individual/stub saves reuse the outer SQL
transaction and EF savepoint rollback; ordinary ingestion retains its existing
transaction path. Missing/deleted session, generation, owner, status or frontier
fence rejection returns Unknown, zero added rows, no publication, one Warning.
No retained persistence failure is cleared by rejection.

| S3 read -> action | Fenced by / audit result |
|---|---|
| Initial session/generation/rows/stamp -> HTTP fetch | Observation only, lease released before HTTP; final stamp/row comparison plus SQL fence before idle append. |
| Snapshot session/generation/completeness/frontier -> runner classification | Immutable fetched DTO; no durable mutation; checked against captured durable identity. |
| Final generation/status/runner binding -> idle append | Previously unfenced lifecycle writer; fixed by session-row lock and same-transaction reread of all predicates before INSERT. |
| Final row identities/revision/reset/epoch -> idle append | State lease holds ordinary ingest/retention; SQL fence rereads complete row identities/content to cover late DB/overlay change. |
| Runner Working + final observation -> CheckedWorking | Same lease and final full observation; no append/publication/callback. This is an observation at that read, not future idle permission. |
| Dedupe/max sequence/closure decision -> batch/fallback/stub save | Staging creates no durable row; fence reread precedes INSERT; row lock spans all checked saves; state lease serializes transcript writers; existing unique conflicts still disclose uncertainty. |
| Committed rows -> state publication | SQL commit precedes PublishCommittedAsync; state lease remains held; fence rejection bypasses publication entirely. |
| Postcommit ownership/SQL/cache verdict -> returned RecoveredIdle | State lease serializes transcript projection; fresh durable reads and retained-failure/NeedsReload vetoes; no queue action in S3. Later lifecycle changes cannot retroactively invalidate a prior correctly ordered durable commit. |
| Result -> future flush/receipt | S4/S5 outside scope; callers must reread ordinary Working/destination receipt evidence. No delivery claim here. |

Repair ordinary run: select `--rows CP-1,CP-2,ADJ-1,CP-3 --serial` with exact
committed `--expected-source-sha`; one shared isolated build, UseAppHost=false,
OutputPath `bin-c1205-f1-final/`. Baseline F1-BASE is separately selected before
the fix, not included as an expected-red row in Final. The tool bootstrap is the
only extra build: build-slot gated, `bin-c1205-f1-tool/`, needed because this
checkout had no executable checkpoint tool. Both alternate outputs are owned.
No Unit/full-assembly run or repetition is required by this explicit repair brief.
No S3 manual acceptance is required. All PCs remain pending.
