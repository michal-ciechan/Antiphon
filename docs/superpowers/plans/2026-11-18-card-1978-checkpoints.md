# CARD-1978 RcBridge Windows placement skip

Code owner: `c099d1cf-69a2-4b12-a935-d88cc5668612`.
Base: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`, current origin/master at start.

Add the existing Windows probe identity to WindowsUnitSkipRoster.WindowsOnly and
route its unchanged off-Windows omission through SkipOnLinux. Preserve native
Windows measurement assertions and the Linux unmeasured-probe assertions. No
production, startup, migration, delivery, lease or persistence behavior changes.

## Verification design

V-1: A Linux receipt containing exactly the RcBridge Windows method skip is accepted
by the real committed-source validator. Extend the existing offline receipt fixture;
commit and run it before the roster fix. Expected red is skip_identity_unlisted.
V-2: Run RcBridgeProbePlatformTests; expect six passing cases and one explicitly
rostered placement skip on Linux. Inspect each fresh TRX identity and its reason.
R-1: Existing receipt controls retain acceptance of C1210 placement and Linux pass,
and rejection of unknown method, Windows host, and unknown host skips.
R-2: WindowsUnitPlatformAdmissionTests, the named classification/selector/pin guard
union, and matching impacted-classes rows retain their invariants. Census source
skip sites and report any broader unrostered sites without silently broadening this
one-card fix. The Windows-only native execution remains unexecuted on this Linux task.
R-3: Recompute all three nightly pins, layering VIOLATIONS=0/LISTED=162,
land-graph -Check, full base..HEAD evidence-diff and conflict-free merge-tree.

Manual M-1: validate real checkpoint provenance using the actual committed SHA;
synthetic fixture CHECKPOINT lines are test input, never actual build evidence.
M-2: rebuild the derived-index driver in native output, regenerate twice (second
unchanged), and commit a changed index last. Inspect the full-range impact selection.
M-3: inspect changed OS branches and callers; test-only changes require no restart.

PC-1 pending SourceLanding Mutation: remove the RcBridge roster entry; the
rc-probe-linux-skip case in CP-1 must refuse with skip_identity_unlisted.
PC-2 pending SourceLanding Mutation: remove the SkipOnLinux call; the roster/source
closure witness must fail. Operator waived mutation execution; neither is passed.

## Performance budget

CP-1 is six offline validator subprocesses with the existing 10-second per-child
deadline, no database, services or compiler outputs. Estimate under one minute;
first-row EstimatedMinutes 15 is the brief's required cold-build allowance, not a
new test timeout or measured p95. Run red once then green once. Preserve per-case
exit results and checkpoint wall/admission timings. No repeated green proof needed.

The requested compiled rows are pending a blocking output-policy decision:
the brief prohibits custom build output directories, while PlanTableImporter and
ManifestValidator require every TUnit checkpoint build to use bin-<name>/.
No compiled row may be represented as passed by a command-only receipt. On approval,
extend this closed table before running V-2/R-2 with exact impacted filters, serial
rows under ten minutes, first compiled row EstimatedMinutes 15, and no row-timeout
override. Direct bootstrap builds keep native output. Read /proc/loadavg before each
driver, admit below 16, poll every 30 seconds, and set ANTIPHON_LINUX_COMPILER_POLICY=0.

### Checkpoints

Run through the checkpoint tool with --expected-source-sha set to committed HEAD,
--serial and --timeout 570s. Wait until terminal completion; never end on exit 75.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | all | n/a | placement-receipt | `pwsh -NoProfile -File scripts/test-checkpoint-placement-skip.ps1 -Label c1978` | V-1,R-1 | six synthetic receipt cases, 0 assertion failures | n/a | 15 | true |

V-2/R-2 and M-1 for compiled rows remain incomplete until the output decision is
resolved and those rows are added and executed. The Unit lane stays nightly/rc.
