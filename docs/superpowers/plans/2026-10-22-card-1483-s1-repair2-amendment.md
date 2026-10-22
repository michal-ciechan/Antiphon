# CARD-1483 S1 repair 2: amendment

Date: 2026-10-22. Code task d205268d, after Final Review 8e9f68b4 D-1. Amends
`docs/superpowers/plans/2026-10-22-card-1483-real-msbuild-land-audit-plan.md` as
`2026-10-22-card-1483-s1-repair-amendment.md` already did; everything else is unchanged.

- **D-1 (unreadable link component).** On Linux `FileInfo.LinkTarget` is null when readlink fails with
  EACCES, the same as for a plain path. The real-path walk now confirms every non-link component with
  `File.GetAttributes`: a missing component (FileNotFound/DirectoryNotFound) is kept as written, any other
  error (UnauthorizedAccessException, IOException) is `evaluation-failed` (exit 2).
- **V-1b.** A link inside a mode 000 directory (`App/locked/lk -> ../../docs`, item `locked/lk/guide.md`)
  gives exit 2 `evaluation-failed` naming `App/locked/lk`, never `approved`:
  `C1483_S1_UnreadableLinkComponentDenies`. It skips on Windows and when the user lists the mode 000
  directory anyway (root or CAP_DAC_OVERRIDE); a skip is a failed CP-1 row, so CP-1 runs on a non-root
  Linux runner (server2).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1483-s1/` | c1483-s1 | `/*/*/LandGraphEvaluationTests/(C1483_S1_*)` | V-1 V-1a V-1b V-2 V-3 V-3a V-4 | exactly 7 executed, 0 failed/skipped | 7 | 15 |

CP-1 becomes 7 (was 6). Later rows' S1 shares grow by one more in their own amendments.

Disclosure only (plan R-3 class, not fixed): an include through `/proc/self/cwd` resolves through a
kernel magic link.
