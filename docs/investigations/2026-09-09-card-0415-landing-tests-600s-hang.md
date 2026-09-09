# CARD-0415 fix round: Landing* test suite hit 600s Bash timeout

## Session and evidence

- Live session `89093d49-ea9e-4d2a-a2ef-c3456988e2a0` (task `d87a154c`), transcript entries
  sequence 253-266, fetched via `GET http://localhost:17202/api/sessions/{id}/transcript?since=0`.
  (Note: the runner's `:17204/sessions/{id}/transcript` route does not exist — 404 — and the
  session ID must be the full GUID; the 8-char prefix alone 404s on both hosts.)

## What ran

Sequence 253 (2026-09-09T08:30:24.600Z), a single `Bash` tool call in worktree
`/c/Antiphon/worktrees/card-task-2ddbfaf6`:

```
cd /c/Antiphon/worktrees/card-task-2ddbfaf6 && \
for f in "*Migration*" "Landing*" "SpecialistRoleContractTests"; do \
  echo "### $f"; \
  ./tests/Antiphon.Tests/bin-c415fix/Antiphon.Tests.exe --treenode-filter "/*/*/$f/*" 2>&1 \
    | grep -E "^\s+(total|failed|succeeded|skipped):|^failed " | head -12; \
done
```

Tool-call JSON carried an explicit `"timeout":600000` field on this specific Bash invocation —
600000ms is the Bash tool's documented ceiling ("up to 600000ms / 10 minutes"). This is not a
script default or a TUnit/runsettings value; it is the per-call `timeout` parameter passed by the
agent, set to the tool's maximum.

## The timeout

- Sequence 254 (2026-09-09T08:40:25.438Z, i.e. ~10m01s after the call): "Command did not
  complete within its 600s timeout and was moved to the background (ID: bsg0b0ja1)."
- Interim output captured at that point (seq 258, and again at seq 262/264):
  ```
  ### *Migration*
    total: 13
    failed: 0
    succeeded: 13
    skipped: 0
  ### Landing*
  ```
- The agent then polled the backgrounded task twice more with `TaskOutput(block:true,
  timeout:600000)`: at 08:40:42 (returned `timeout`/`running` at 08:50:42) and at 08:40:45→ actually
  08:50:45 (returned `timeout`/`running` at 09:00:45). **The captured `<output>` block was byte-for-byte
  identical across all three checks (08:40, 08:50, 09:00)** — no new line was written after
  `### Landing*` for a full 20 additional minutes beyond the original 600s window.
- At 09:00:51 the agent ran `Get-Process -Name Antiphon.Tests` and found one process still alive
  (PID 41308, ~117s accumulated CPU time), so the process had not crashed or exited silently — it
  was running but not producing output.

## Verdict

The `*Migration*` filter (13 tests) completed quickly and printed its full total/failed/succeeded
line. The very next filter, `Landing*` (`--treenode-filter "/*/*/Landing*/*"` against
`Antiphon.Tests.exe` built at `bin-c415fix`), printed only its `###` header and then produced zero
further output for at least 30 minutes total (10 minutes inside the original call, plus two more
10-minute blocking polls) while a live process kept consuming CPU. This is a genuine hang, not a
large-suite-needs-more-than-600s case — a suite that's merely slow keeps advancing (assertions,
partial stats) over that span; here the transcript shows no change at all across three consecutive
snapshots 10 minutes apart.

## Not done, noted

- Not determined which specific `Landing*` test class/method is stuck, nor the root mechanism
  (deadlock, process-spawn wait, DB lock, etc.) — that requires attaching to PID 41308 or
  re-running the `Landing*` filter alone with diagnostics, which is out of scope for this
  read-only pass.
- Fix idea (not designed): re-run `--treenode-filter "/*/*/Landing*/*"` alone under a debugger or
  with `dotnet-trace`/thread-dump on hang to catch the stuck await; check whether `Landing*` tests
  share the process-spawn `ParallelLimiter<ProcessSpawnLimit>` with another concurrently-running
  suite in that worktree.

--- next stage ---
next: investigate
handoff: Reproduce the Landing* filter hang alone (thread/process dump on stall) to find which specific test and await point is stuck; this pass only confirmed genuine hang vs slow-suite from transcript evidence.
