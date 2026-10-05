# CARD-0667 S3h integration port verification

Task `5f187792-e80f-4a3d-8ef0-bdb09dc72625` ports only the eight commits in
`a2467393702da9c8cffea1b755ac93c0cb1aaa03..e1780854daf6d311a07ddbe9ecdcffde4c275d0a`
onto `6f1bb64f0284a9478a8a8a65d6d9a48053d244bd`. S3f/S3g are already on this base.
The source plan remains
[the terminal task seat release plan](2026-10-01-card-0667-terminal-task-seat-release-plan.md).
All eight commits applied without conflict; both predecessor reports are retained.

This task's explicit Final brief overrides the generic Unit profile: run CP-21
and the whole runner `TerminalSeatReleaseTests` class once, with a 30-minute budget
and at most one repair round. Do not repeat the predecessor's incomplete Unit lane
or full server classes. CP-21 below is copied exactly from the source plan;
CP-24 declares the additionally commissioned full runner class. Its current census
is 34 methods / 38 results (two methods have three provider arguments), including
the three CARD-0519 terminal-completion results inherited from current master.

The invariants are fresh runner-owned native delivery evidence, conservative
restart/capability holds, and runner conditional-release safety. There is no
unbounded shared-impact or full-assembly selection. The isolated build/test estimate
is 7 + 10 = 17 minutes, plus at most 3 minutes for the declared checkpoint-tool
bootstrap; the remaining budget is available for gate waits or one repair.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-21 | S3h | `tests/Antiphon.Tests -> bin-c667-s3h/` | linux-s3h-postgres | `/*/*/RunnerSeatOrphanSweepTests*/(Discovery_request_uses_runner_owned_delivery_evidence*)\|(Server_restart_reacquires_runner_delivery_evidence*)\|(Evidence_missing_or_peer_unsupported_defers_discovery*)` | V-3 | all 3 listed results; named assertion red then 0 failed/skipped green | 3 | 7 | true | `C804_ORPHAN_SWEEP_ROOT=c667-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-24 | S3h-port | `tests/Antiphon.SessionRunner.Tests -> bin-c667-s3h-port-runner/` | linux-s3h-port-runner | `/*/*/TerminalSeatReleaseTests*/*` | V-1 | all 38 current class results, 0 failed/skipped | 38 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Commit and push before the run, then freeze source. Prefer a prepared tool; none
was present in the assigned worktree at inspection. The declared setup build is:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c667-s3h-port-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c667-tool/ --property:UseAppHost=false
$sha = git rev-parse HEAD
dotnet run --no-build --no-restore --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c667-tool/ --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-10-05-card-0667-s3h-port-verification.md --rows CP-21,CP-24 --expected-source-sha $sha --total-timeout 30m --max-wait 50s
```

The no-build tool entry point needs no outer lease: each build/test driver owns
its host slot. Await emitted `wait` commands until exit is not 75; inspect every
intended method in fresh TRX, require nonzero counts, and validate the receipt
against the committed expected SHA. No unchanged green repetition is required.
Preserve actual CHECKPOINT lines in the final stored task Result. Remove owned
alternate outputs after completion and run `scripts/check-evidence-diff.ps1`
over the full task base..HEAD, including all carried predecessor reports.

V-1 is qualified only for the full current runner class; V-3 only for the three
S3h methods. V-2 and R-1/R-2/R-3/R-4 are not selected by this port; remaining
CP-16/17/18 and final CP-1..6 remain with their planned slices. None is inferred
passed here. No live activation acceptance is commissioned. Manual checks here
are authenticated runner-defaults/catalogue reads, dormant default, no migration,
and equivalence of the carried C# files to the predecessor tip.

PC-1..PC-104 and every variant remain pending for post-land SourceLanding Mutation.
S3h specifically carries PC-100 (HTTP/phone-home, Claude/Grok/Codex, old generation,
Enter retry), PC-101 (both transports, server restart vs runner capture loss,
persisted-token refusal), and PC-102 (both transports, capability combinations,
missing/invalid capture, malformed/lost response, peer availability/staleness/
adoption/store mismatch, valid control). Mutation owns deliberate mutants and
method-scoped red/restore/green; this port imports existing defect-detecting tests.

Next is a fresh Review whose subject and direct landing owner is this port task,
`5f187792-e80f-4a3d-8ef0-bdb09dc72625`, superseding the predecessor ownership for
this integrated branch. Restart: none for this task; eventual server activation
is owned by the caller's separate post-land commission.
