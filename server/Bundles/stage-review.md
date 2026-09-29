Read-only. Do not fix anything. Defects: Where/Failure/Why/Fix.

SCOPE: Re-run the claimed checks (Unit plus named affected integration classes) as one checkpoint-tool run. Match CP-n lines to ### Checkpoints; check the build against the plan. Defects: missing row, zero count, unlisted build/test run without a reason, build or test driver outside the slot gate, broad run without invariant/cost, test that cannot go red (self-compare, constant, no outcome assertion).

`rm`: reject empty variable components with nonzero exit; quote variable expansions; resolve the target inside the intended scratch root.

ROUND: the brief's verification profile governs. A Final Review reruns the complete ordinary scope itself, including every row an Interim round deferred; an Interim pass never discharges it. Require fresh executed identities and nonzero counts; exit 0, --list-tests or missing parameter rows are not evidence. Required manual work stays pending and nightly green never satisfies manual or PC checks. Executed PCs are not a prerequisite.

INVARIANTS: Check V/R; judge PC evidence read-only (PCs stay pending). Carry original Code landing owner.

Audit producer/destination/persistence/recovery/receipt/identity. Trace ordinary V/R evidence through the real queue: busy/eligible, crash/enqueue. Acceptance needs matching complete UserPrompt transcript. Reject a missing producer-to-recipient test or recipient evidence; queue/event/Sent/ack fails.

Before next-stage, emit bare, unfenced, unindented, unquoted lines:

--- review evidence ---
subjectTaskId: <full GUID of task whose exact pushed tip was reviewed>
reviewedSourceSha: <full SHA actually reviewed>
ordinaryScopeCompleted: <Full|Interim|None>

Full only when the whole required selection ran. Ordinary/recovery subject: original Code/Worktree landing owner; adoption subject: source. `-Land <owner> -FromTask <source> -ExpectedSourceSha <sha> -ReviewEvidenceId <evidence>` adopts; omit `-FromTask` for owner, add `-RecoverReviewedSource` for recovery. Review ID/`-StartRef` cannot identify source; brief names both. Keep `-Card`; follow-up must match FollowUpOfTaskId or use fresh same-card Review.

Platform: GET /api/runner-defaults, GET /api/session-runners; embed no fleet location. Omit -Runner unless pinning host; -Platform only for OS; -Platform Any unpins.

next: land when there are no defects and this was a Final Review; review (Final) when a clean Interim; code for defects; decide for choices.
