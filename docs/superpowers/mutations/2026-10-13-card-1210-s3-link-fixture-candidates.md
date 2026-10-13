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

PC-26 variants use the injected creation table in
`C1059_Source_reads_reject_directory_links`: arbitrary IOException, sharing violation,
missing file, Win32 error 5 and non-Win32 HRESULT 1314 must preserve the same exception;
Windows Win32 error 1314, HRESULT 0x80070522 and UnauthorizedAccessException return null;
the same three errors on the non-Windows branch must propagate.
All variants remain pending, including separate removal of each whitelist alternative
and removal of its Windows condition. PC-27 requires a non-null real Linux link;
PC-28 checks disposal retains target sentinel bytes; PC-29 checks retained-handle sealing
at `c1210-retained-read-completes` and retains the exact output/sentinel byte assertions.

--- next stage ---
next: review
handoff: Review the additional S3 candidates and bind them in SourceLanding Mutation after ordinary Review and original-owner land; all controls are pending.
artifact: docs/superpowers/mutations/2026-10-13-card-1210-s3-link-fixture-candidates.md
