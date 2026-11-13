# CARD-1895: drop bin/obj from a finished published tree

Original Code task: 2ec9dc6b. The card moved Backlog to InProgress with this Code task; there is no earlier Plan or TestDesign artifact. This file is the verification design the checkpoint tool and the OS-branch witness checker read.

Whole-tree retention (CARD-1677) is unchanged. It still waits out MinTerminalHours before a mirror can be removed. This step deletes only allow-listed build output directories while that age floor is the sole remaining mirror hold (`settling`), and only when the runner advertises `workspaceOutputDropV1`.

Eligibility is the retention classify with the age comparison skipped, then the runner's existing `CheckAsync` (confinement, slot, repository, branch, clean, pushed, live origin, holders, ignored-secret floor). A tree old enough for whole-tree removal is not also sent a drop. A refusal deletes nothing. Names are exact path segments: `bin`, `obj`, or `bin-` plus 1..64 of `[A-Za-z0-9._-]`. Symlinks of those names are left in place, and a symlink child is unlinked as a link. The saving is the summed regular-file size, recorded on the candidate row and, when bytes were deleted, on a warning event `runner_outputs_dropped`.

## Verification design

- V-1: execute deletes `tests/Foo/bin`, `tests/Foo/obj`, and `tests/Foo/bin-cp1` (14 bytes) and leaves `binary`, `node_modules`, `dist`, `obj2`, `bin-`, a file named `bin`, the source, and the git registration. No mutating git verb.
- V-2: dry run reports `would_drop` and the same bytes; the directories stay.
- V-3: dirty, unpushed, and origin-behind each keep the output and name that retention reason.
- V-4: a live holder and an incomplete holder scan keep the output.
- V-5: a symlink named `bin` is not followed (target file and the link stay) while a sibling real `obj` is deleted.
- V-6: tracked content forced under `src/bin` stays; a sibling ignored `tests/obj` is deleted.
- V-7: outside root, nested leaf, symlinked leaf, leaf/task mismatch, slot leaf, a control character in the path, and an unknown mode each delete nothing and name the retention reason.
- V-8: `RetentionFeatures(true)` contains `workspaceOutputDropV1`; `RetentionFeatures(false)` does not. The dispatcher routes `WorkspaceDropOutputs` at the engine.
- V-9: a one-hour terminal mirror is dropped (execute, lane `RunnerOutputDrop`, outcome Dropped, digest `4096`, path kept, mirror row still `settling`) while a day-old mirror is only retained and a Blocked task is neither.
- V-10: a young control is dropped beside each of session-running, sync-debt Ready, mutation, and land-pending, and that subject is not dropped.
- V-11: a runner without `workspaceOutputDropV1` is sent no drop frame. Output-drop Mode Preview (retention Execute stays true) sends DryRun twice (`would_drop` does not back off) and writes no drop event. Mode Off sends no output-drop rows and no frames.
- V-12: a proven `tests/Foo/bin` and `bin-*` beside a project file are deleted. `node_modules/pkg/bin`, `dist/tool/bin`, a bin with no project sibling, a bin symlink, and a tracked bin inside an ignored nested repository stay. `/` and `/proc` report different mount ids. A live bind-mounted bin was not executed.
- V-13: a file that becomes tracked, or a new untracked file, between validation and delete removes nothing and reports `changed_during_drop`. When the change arrives after the first proven directory, that directory is deleted and the rest are refused with the same reason.
- V-14: an unlink that fails after some files are gone reports `delete_failed`, the bytes and file counts actually removed, and the server records Partial plus `runner_outputs_dropped` when bytes are above zero. The real transport partial leaves the blocked file and records digest `6`.
- V-15: the mutation lock is held through the delete. A session started during the delete stops the drop with `changed_during_drop` and deletes nothing.
- V-16: a lost reply retries the same request id stored before the send. The runner replays the fsynced ledger and deletes nothing more. The real transport path is server command, runner, reply.
- R-1: the guard union in CP-3.
- R-2: `scripts/check-evidence-diff.ps1` over the task base..HEAD, OS-branch witness inventory, and `git merge-tree --write-tree origin/master HEAD`.

PC-1 pending: delete a directory whose name is not on the allow-list. PC-2 pending: treat a dirty tree as clean. PC-3 pending: follow a symlink named `bin`. Mutation owns method-scoped deliberate red/restore/green. Ordinary Code runs no mutants. Bind after ordinary Review and landing. Candidate: `docs/superpowers/mutations/2026-11-13-card-1895-output-drop.md`.

Restart: none from this task. The Linux feature string takes effect only when a runner that contains this code is deployed; this task does not deploy or restart.

The new Linux-only advertisement is the OS-selected behavior (`RetentionFeatures` already takes the OS flag; output drop is added to that Linux list). Delete walks both operating systems inside `RunnerWorkspaceNoFollowDelete`, so the engine cases are not `NativeLinuxOnly`. Unchanged `OperatingSystem` branches in edited files are non-behavior.

## Performance budget

CP-1 is real git per case, budgeted at 8 minutes from the CARD-1677 retention engine shape (60s git floor, cases well under that). CP-2 is shared Postgres, budgeted at 10 minutes. CP-3 reuses the CP-2 build and is budgeted at 15 minutes. No selection is expected past 30 minutes. The checkpoint tool's own bootstrap takes its own build slot. Two earlier `--list-tests` probes of the guard union listed the whole assembly (23251) because TUnit discovery ignores `--treenode-filter`; they are not receipts. CP-3 Min is the counted case floor: policy 42, merge 5, tripwire 2, guard 1, native Linux 3, derived impact 27. Run 20261113-173348-624e kept `bin-c1895-runner/` and `bin-c1895-server/` after CP-3 saw a stale index. The closed rerun uses `bin-c1895b-runner/` and `bin-c1895b-server/`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | code | tests/Antiphon.SessionRunner.Tests -> bin-c1895b-runner/ | output-drop-runner | /*/*/C1895OutputDropEngineTests/* | V-1 V-2 V-3 V-4 V-5 V-6 V-7 V-8 | 19 executed, 0 failed | 19 | 8 | true | TEMP=/tmp |
| CP-2 | code | tests/Antiphon.Tests -> bin-c1895b-server/ | output-drop-server | /*/*/C1895OutputDropServerTests/* | V-9 V-10 V-11 | 7 executed, 0 failed | 7 | 10 | true | TEMP=/tmp |
| CP-3 | code | CP-2 | output-drop-guards | /*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)/* | R-1 | 80 executed, 0 failed | 80 | 15 | true | TEMP=/tmp |
