# CARD-1718 B-1r2 nested ignored repository repair

Original Code landing owner: `5e05fc80-930f-4f03-8c71-6c323d552d63`.
Task base: `2d4e80a2169052fee591bd4fafde9f4102c828d6`; Review round 3 diff base:
`40b176ce1225a40bd8290e77975633f17025c4d2`.

Invariant: a collapsed ignored repository entry must not hide a secret from active runner
retention. Enumerate descendants without following links; inaccessible entries, links and
control-character names keep the tree. Secret-free nested repositories remain disposable.

## Verification design

- V-1: two independent engine secrets, `.antiphon/seed/.env` and
  `bin/tool/credentials/token`, keep bytes and registration in DryRun and Execute.
- V-2: one independently planted nested secret survives real server/phone-home/runner/Git
  transport, with Held reason, bytes, server path and Git registration asserted.
- V-3: file/directory links, an actually unreadable directory and a control-character name
  inside a collapsed entry all refuse before mutation. Linux native filesystem inputs;
  permission restored in finally. Windows native reparse/ACL qualification stays pending.
- R-1: every full runner retention/ignored-content class and completed-card removal class.
- R-2: full server transport, ignored-content, settings, guarded cleanup and legacy release
  classes plus classification/slow-test guards. Prior V-B1/B2/B3 remain covered here.
- R-3: secret-free ignored nested repository removal remains green in the full engine class.

Run CP-1/2 on the committed test-only slice for historical red (production identical to base),
then CP-3/4 on the fix. CP-3/4 include the new witnesses, so no redundant green-only rerun.
No full assembly run. Whole Unit lane is explicitly CARD-1720's work per this repair brief.
No deliberate mutants in Code: all prior PC IDs and new drop-walk, follow-link and
unreadable-as-clean variants stay pending for method-scoped SourceLanding Mutation.
No startup composition or production OS-selected branch changes; actual engine runtime and
ProcWorkspaceSlotProcessProbe are exercised through the real Linux transport. Restart: none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | witnesses | tests/Antiphon.SessionRunner.Tests -> bin-c1718-nested-red-runner/ | nested-engine-red | /*/*/C1677RetentionEngineTests*/(Ignored_nested_repository_secret_keeps_bytes_and_registration*)\|(Doubt_inside_collapsed_ignored_repository_keeps_the_tree*) | V-1, V-3 | 6 expected assertion failures before fix | 6 | 5 | true | TEMP=/tmp |
| CP-2 | witnesses | tests/Antiphon.Tests -> bin-c1718-nested-red-server/ | nested-transport-red | /*/*/C1677RetentionServerTests*/Ignored_nested_repository_secret_survives_real_transport* | V-2 | 1 expected assertion failure before fix | 1 | 7 | true | TEMP=/tmp |
| CP-3 | fix | tests/Antiphon.SessionRunner.Tests -> bin-c1718-nested-runner/ | runner-retention-regressions | /*/*/(RunnerWorkspaceIgnoredContentFloorTests*)\|(C1677RetentionEngineTests*)\|(C1677RetentionDispatchTests*)\|(C1677RetentionInventoryTests*)\|(C1677RetentionNativeProcessTests*)\|(CompletedCardWorkspaceRemovalTests*)\|(TestClassificationGuardTests*)/* | V-1, V-3, R-1, R-3, V-B1, V-B2, V-B3 | all listed, 0 failed; native skips pending | 240 | 6 | true | TEMP=/tmp |
| CP-4 | fix | tests/Antiphon.Tests -> bin-c1718-nested-server/ | server-transport-regressions | /*/*/(C1677RetentionServerTests*)\|(WorktreeIgnoredContentClassifierTests*)\|(WorktreeCleanupSettingsValidatorTests*)\|(WorktreeGuardedCleanupTests*)\|(C0824DesktopLegacyRelease*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/* | V-2, R-2, V-B1, V-B2, V-B3, V-Native | all listed, 0 failed; native skips pending | 440 | 9 | true | TEMP=/tmp |

## Performance budget

Provisional per-row estimates use Review d6e48431: 235 runner tests and 443 server tests,
two cold builds; server build 856 seconds under load, execution 151 seconds in prior Code.
One collection per selection; no repetitions after green. Shared Postgres/web-host fixtures
are inherited, per-test schema/Git isolation. Existing 15/45-second test deadlines unchanged.
Build/row timeout 18 minutes (provisional load allowance under CARD-1718), execution cap
28 minutes per two-row slice, hard wall 45 minutes; admission separately bounded by tool
default 45 minutes and outer hard wall. TRX per-test durations and tool build/admission spans
stay in ignored `.antiphon/checkpoints/`; uninstrumented internal phases remain unknown.
The checkpoint bootstrap is an explicitly unlisted, separately leased necessary build with
native default outputs and UseAppHost=false. No csproj/props or timeout/assertion relaxation.
