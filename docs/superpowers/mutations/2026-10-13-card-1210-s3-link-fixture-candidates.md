# CARD-1210 S3 additional control candidates

Unbound candidates for SourceLanding Mutation; no deliberate mutants ran in Code.
The original PC-7, PC-8, PC-22 and PC-26 through PC-29 remain pending.

| Candidate | Compiling defect | Exact detecting filter | Expected assertion |
|---|---|---|---|
| S3-PC-A | Replace the directory fixture's `DirectoryLink.TryCreate` with direct directory symlink creation | `/*/*/ChannelOutboundStorageTests/C1059_Source_reads_reject_directory_links` | `c1210-link-fixture-route` |
| S3-PC-B | Replace the file fixture's `FileLink.TryCreate` with direct file symlink creation | `/*/*/ChannelOutboundStorageTests/C1059_Source_reads_reject_file_links` | `c1210-link-fixture-route` |
| S3-PC-C | Omit the helper's successful-creation ReparsePoint observation check | `/*/*/ChannelOutboundStorageTests/C1059_Source_reads_reject_directory_links` | `c1210-filelink-reparse-required` on the injected ordinary-file case |
| S3-PC-D | Remove storage from the Windows child filter | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Audit_rejects_empty_or_incomplete_outcomes` | `c1210-child-storage-selection` |
| S3-PC-E | Remove or change one Conditional or StoragePortable entry | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Audit_rejects_empty_or_incomplete_outcomes` | frozen identity/reason roster assertion |

PC-26 variants use the hand-written injected creation table in
`C1059_Source_reads_reject_directory_links`: access denied (UnauthorizedAccessException
and IOException HRESULT 0x80070005, plus Win32 error 5), already exists, path not found,
sharing violation, generic IO, missing file, raw non-Win32 HRESULT 1314 and
zero/unknown/unset HRESULT must preserve the identical exception. Only Windows
Win32 error 1314, IOException HRESULT 0x80070522 and UnauthorizedAccessException
HRESULT 0x80070522 return null. Every table row also runs with isWindows=false;
all errors must propagate on that branch, including simulated privilege-not-held.
All variants remain pending, including separate removal of each whitelist alternative
and removal of its Windows condition. PC-27 requires a non-null real Linux link;
PC-28 checks disposal retains target sentinel bytes; PC-29 checks retained-handle sealing
at `c1210-retained-read-completes` and retains the exact output/sentinel byte assertions.

| Candidate | Compiling defect | Exact detecting filter | Expected assertion |
|---|---|---|---|
| S3-PC-F | Catch every UnauthorizedAccessException as unavailable | `/*/*/ChannelOutboundStorageTests/C1059_Source_reads_reject_directory_links` | `c1210-filelink-unknown-error: access denied; isWindows=True; HRESULT=0x80070005` |
| S3-PC-G | Omit the UnauthorizedAccessException 0x80070522 alternative | `/*/*/ChannelOutboundStorageTests/C1059_Source_reads_reject_directory_links` | `c1210-filelink-privilege-unavailable: unauthorized privilege not held` |
| S3-PC-H | Omit the Windows-only condition | `/*/*/ChannelOutboundStorageTests/C1059_Source_reads_reject_directory_links` | `c1210-filelink-unknown-error: unauthorized privilege not held; isWindows=False` |

All new candidates and each of the fifteen table rows on both platform branches
remain pending for method-scoped SourceLanding Mutation. Code's fail-first test
against the unchanged helper is ordinary defect verification, not a deliberate PC.

--- next stage ---
next: review
handoff: Review the additional S3 candidates and bind them in SourceLanding Mutation after ordinary Review and original-owner land; all controls are pending.
artifact: docs/superpowers/mutations/2026-10-13-card-1210-s3-link-fixture-candidates.md
