# Review 48a4f5af: CARD-1015 evidence-diff guard console encoding (subject 3cb45e84)

Outcome: clean. No defects. Final round, run at Interim scope as the brief set it.
Reviewed tip a37b94ac8219bda1d5d93c8148a1d567e9290905 (red witness 4bc44c4e5, fix a37b94ac8) on base rc/2026-10-27 fffa9e954e9dfe6685a4e6793ae0ae543688fcb8.

## Runs (server2/Linux, pwsh 7.5.4/.NET 9.0.10)
One build through the build slot (UseAppHost=false, OutputPath=bin-rv48/): 0 errors, held=186s. Then two dotnet exec --no-build runs, each in its own slot:
- run1 `(EvidenceDiffGuardTests*)|(EvidencePolicyWorkflowTests*)|(RcRosterRepairTests*)/*`: total 27, failed 0, skipped 0. That is EvidenceDiffGuardTests 20 (includes the new witness), EvidencePolicyWorkflowTests 4 and RcRosterRepairTests 3.
- run2 `C1514LandingSplitBudgetTests`/`WindowsUnitPlatformAdmissionTests` x `C1404|C1577|C1584|C1410|G2865|C1210`: total 10, passed 9, skipped 1. The skip is C1210_Windows_linux_only_outcomes_are_exact, which needs Windows placement and is skipped on Linux by construction.
- Red witness: I took a `--shared` scratch clone at 4bc44c4e5 and ran the same test binary against it (test code is byte-identical between 4bc44c4e5 and the tip). Reports_non_ascii_paths_when_console_encoding_is_legacy failed 1/1 on the `c1015-legacy-encoding` path assertion (expected `é`, got U+FFFD). It passes at the tip.
- Manual check of the guard under pwsh with ISO-8859-1 set first: at the base the path bytes are `5c 6e e9`; at the fix they are `5c 6e c3 a9`. For an ASCII violation and a clean ASCII range, base and fix output are byte-identical (cmp) and the exit codes match (1, 1, 0).
- check-evidence-diff.ps1 itself, with a non-ASCII path, stdout redirected to a file and stdin closed (`<&-`): exit 1 with the correct UTF-8 path. Piped through `cat`: exit 1. check-evidence-deletion.ps1 with stdin closed: the lib loads, then a normal `ref_input` refusal (exit 2). No setter throws.
- check-evidence-diff.ps1 over fffa9e954..a37b94ac8: commits=2 entries=0 violations=0, exit 0.
- Landability: merge-tree onto origin/rc/2026-10-27 is clean (base == rc tip). Whole-branch merge-tree onto origin/master 2fb1c5a65 conflicts on rc-only files. A cherry-pick of 4bc44c4e5 then a37b94ac8, simulated per commit with merge-tree --merge-base, is clean.
- Cleanup: all 32 bin-rv48 directories were removed by literal path. The scratch clone stays in the session scratchpad.

## Checks
(1) Robustness. The assignment is not wrapped in try/catch. This is acceptable, not a defect:
- 5.1 cannot load the lib before or after this change, because `ProcessStartInfo.ArgumentList` (evidence-policy.ps1:30) exists only in .NET Core. So the change adds no 5.1 exposure.
- On pwsh 7.5 (.NET 9) and 7.6 (.NET 10, the desktop), ConsolePal.Windows `HandleSetConsoleEncodingError` swallows ERROR_INVALID_HANDLE and ERROR_INVALID_ACCESS when no console is attached. On Unix the setter does not throw (shown above).
- Any other throw would fail closed. Both callers set `$ErrorActionPreference='Stop'` and dot-source inside try/catch, which prints `EVIDENCE error` and exits 2. The two in-function calls sit inside the existing try. A refusal can never become a pass.
- The file is ASCII-only (grep). It is not a daemon or auto-start script.
(2) Output consumers. The GitHub workflow (ubuntu) reads only the exit code. The bundles are for human readers. EvidenceDiffGuardTests and EvidenceGitFixture parse the lines and are green. ASCII bytes are unchanged; non-ASCII is now UTF-8.
(3) The witness is not tautological. It is red at the red commit on the exact assertion and green at the fix.
(4) The other exposed scripts. `scripts/run-checkpoint.ps1` `Invoke-CheckpointGit` (:116) is a production path. It reads git stdout via `CheckpointProcessRunner.Run`, i.e. `StandardOutput.ReadToEndAsync` (scripts/lib/CheckpointProcessRunner.cs:85-86) with `StandardOutputEncoding` unset, so Windows decodes with the console code page. A non-ASCII path then gets a mangled key, and its content stamp reads `absent|0|` both before and after a row. `Complete-CheckpointBaseline` (:945) therefore misses content changes to such a file. That is a narrow, Windows-only fail-open in tamper detection; git-status dirty detection still catches tracked edits, but under a mangled name. It is latent: 0 non-ASCII tracked paths today. The receipt SourceSnapshot uses RunBytes with strict UTF-8 and is not affected. test-checkpoint-stamp.ps1 and test-checkpoint-reader-deadline.ps1 are test-only. Filed as CARD-1608 (Backlog) after a duplicate search (4 phrases, 0 hits).

## Advisories (no rerun needed)
- A-1: Set-EvidenceConsoleUtf8 (evidence-policy.ps1:4-14) also sets `[Console]::InputEncoding`, which the fix does not need. On Windows, SetConsoleCP and SetConsoleOutputCP change the code page of the whole shared console, so the calling cmd/pwsh window stays on 65001 after the guard exits. Neither is restored. This is benign for Antiphon; the InputEncoding line could be dropped.
- A-2: An optional try/catch around the setters would only matter on pwsh 7.4/.NET 8, which throws on no-console. Not a supported host.
- A-3: The master port must be a cherry-pick of both commits, not a branch merge.
- A-4: The brief asked for next: merge at Interim scope. The standing rule says an Interim Review cannot approve a land. That conflict is the caller's to resolve.
