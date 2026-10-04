# CARD-1020 symlink privilege repair

New Code/landing owner: `561e2b31-becf-4e26-a34a-473ce0110513`.
Branch: `feat/card-task-561e2b31`; runner worktree:
`/work/worktrees/task-561e2b31`. Base: `6a019da5f3c8240b7ca9ffee21423fe5ba81bcf8`,
carrying prior Code owner `1d3e0d92` implementation. Plan:
`docs/superpowers/plans/2026-10-03-card-1020-test-owned-ptyhost-cleanup-plan.md`.

Only symlink creation in the symlink-outside identity row catches Windows
IOException/UnauthorizedAccessException with HRESULT 0x80070522 (Win32 1314).
It uses existing TUnit Skip.Test with the named CARD-1028 missing
SeCreateSymbolicLinkPrivilege reason. All identity assertions are unchanged.
The same row verifies the catch predicate on Linux with constructed positive
exceptions and negative OS, error, facility, message-only and exception-type
cases. These are real predicate calls, not an independent implementation;
returning always true/false would fail the corresponding assertions. They add
no TUnit result, preserving CP-1's 40 and the unrelated census literal 377.
No deliberate mutants were run; Mutation owns those and missing-control discovery.

Audit: this was the only symlink/junction operation in the card-added tests.
Disposal witnesses gate WMI, process modules and ModernConPty checks on Windows;
Linux zombie observation gates /proc on Linux. Raw linger uses cmd.exe on Windows
and /bin/sh elsewhere. FakeGrok resolves the staged test apphost rather than host
PATH. Policy tests inject OS I/O and use platform-correct paths/casing. The Grok
caller uses private roots/provider home and OS-specific fake Enter configuration.
No additional portability defect was identified; no production code changed.

Live runner-defaults and session-runners were read at admission (revision 2;
eligible Linux and Windows lanes). No routing/configuration changes or host pin.
One explicitly named infrastructure build bootstraps the checkpoint tool through
build-slot into bin-c1020-tool/. The plan's portable CP-1/2 are run once at the
committed repair SHA, serially, with exact prefix filters and literal pipes.
Coverage lint, actual receipts and per-method TRX counts are stored in
`.antiphon/task-561e2b31.md`; no generated evidence is committed.

CP-3's conditional floor is amended to 39 executed, but acceptance still requires
40 discovered cases: 40 pass, or 39 pass and only symlink-outside skipped with the
exact named reason. The strict receipt validator rejects skips even when the
checkpoint driver accepts this floor; the Windows caller must retain that explicit
qualification gap instead of claiming a validator-clean certificate. Native
Windows CP-3 at the repair SHA remains mandatory; CP-4's prior 2/2 is historical,
not fresh evidence for this SHA. Whole Unit/final verification not performed by
the brief's bounded portable continuation remains pending for final qualification.
All PC-1 through PC-30 and every parameter variant remain pending post-land
SourceLanding Mutation. Restart: none; Windows commissioning owner: caller.
