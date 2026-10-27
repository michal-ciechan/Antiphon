# Native Windows rerun: terminal, PTY and delivery

Task: `32365cdf`. Investigation in progress; no green claim or confirmed root cause yet.

## Provenance

- Requested rc: `origin/rc/2026-10-27`, fetched at `d1d090f766451797a3bd9b30c9d2966f2f8c034c`.
- Assigned branch: `feat/card-task-32365cdf`. It was unpublished and clean, initially based at `718bd29f4a1504c64c491d607d31403fcb87dae2`. Repositioned the unpublished branch onto the requested rc with no task commits replayed or published history rewritten.
- Host: Windows 10 Pro `10.0.19045`, .NET SDK `10.0.300`; preflight available memory 9,424 MiB; no matching Antiphon test hosts, FakeClaude or FakeGrok processes; native qualification lock absent.
- Historical results (read-only): `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87`.
- Current generated evidence: `C:\Antiphon\worktrees\card-task-32365cdf\logs\native-32365cdf` (gitignored).
- One native build of the three required project graphs, followed by individual class invocations, is pending. Product and test source are unchanged.

## Results

Pending the native runs. Each class will retain its exact filter, pass/fail/skip counts, errors and stack in generated evidence. The historical digest is corroboration; raw TRX rows own the before counts.

## Remaining uncertainties

All native outcomes and root causes are still open. CARD-0680 and CRLF/ConPTY are hypotheses from the brief, not findings.

## Not done, noted

No fix designed or implemented; this is an evidence-only Investigate task.

--- next stage ---
next: investigate
handoff: Complete individual native class reruns and reconstruct any remaining failures from their exact error and source evidence.
artifact: docs/investigations/2026-10-27-native-rerun-terminal-pty-delivery.md
