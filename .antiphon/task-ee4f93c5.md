# Review CARD-1783 round 1 (task ee4f93c5)

Subject: origin/feat/card-task-8d2b200a, range 7f938e410f9f196e2b8b1dd9617276704c57c5f8..e99271f377ba5de8acb0f7b51abd1512f34fad91 (2 commits, Grok Code author).
Verdict: **1 blocker (D-1)**, 2 advisories.

## Runs (server2, reviewed SHA e99271f377ba5de8acb0f7b51abd1512f34fad91, build-slot leased, native default output, UseAppHost=false)

All were direct `dotnet run --no-build` runs under build-slot, not the checkpoint tool. There is no CARD-1783 plan or `### Checkpoints` table, so there are no CHECKPOINT lines or receipts.

| Run | Filter | Result |
|---|---|---|
| build tests/Antiphon.Tests | - | ok, 4m05s |
| touched + guard | `(MergeHighRadiusSubsetTests*)\|(ImpactedClassesSelectorTests*)\|(AdditiveMigrationRuleWitnessTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)` | 78/78 passed, 0 skipped (3+18+7+39+1+5+2+3), 6m15s |
| fixed subset, Antiphon.Tests | doc's TestsFilter | 21/21 passed (TestTiming 2, AppDbContextModel 3, CheckpointReceiptSafety 8, HostCleanupCheckpointReceipt 8), 1m33s |
| build tests/Antiphon.E2E | - | ok, 21 s incremental after the Tests build (slot wait 255 s) |
| fixed subset, E2E | `/*/*/(ContractSnapshotTests*)/*` | 5/5 passed, 2m00s (lease held 123 s); only Kafka connection-refused noise |

The fixed subset is green on master content. The full fixed subset is 26 tests: 21 in Antiphon.Tests and 5 in E2E. The card's "2+3+5+Unit" is correct.

**Cheap claim:** it holds. Once leased, ContractSnapshotTests took about 2 minutes on server2. It needs no Chromium, so the Merge host (server2) can run it. A cold E2E build costs extra beyond that. The downgrade selector was not executed. The brief did not require it, and the card puts it at under an hour.

## Selector checks

- `-MergeSubset` with no range, and with the quiet range: exactly the 5 fixed classes, `migrations=false`, two filters (Tests and E2E).
- `-MergeSubset -Range ff7cceaf^..ff7cceaf` (a migration commit): `migrations=true`, 58 classes. That is the fixed 5 plus all 54 classes that the plain `-Range` selector picks for the `migrations` kind. AppDbContextModelTests appears in both sets, so the union is 58, not 59. The output is byte-identical across two runs, so it is deterministic.
- Fails closed: a table naming a missing class gives exit 1 with the row named. Schema 2 gives exit 1. `-MergeSubset -Audit` gives exit 2.
- Both range SHAs in the test are ancestors of master b35fb137.
- No change to server/Bundles. No unrelated files.
- check-evidence-diff over the range: commits=2, violations=0, exit 0.
- merge-tree against origin/master 8fb32b25: plain merge-tree conflicts (add/add on scripts/impacted-classes.ps1, content conflict on docs/testing-and-build.md). The cause is that this branch carries CARD-1782's pre-rebase commits, while master holds the rebased copies (ac1a7797/6025ba73/a1de447c). Those rebased copies match 7f938e410f byte for byte on the shared files. `git merge-tree --merge-base 7f938e410f origin/master HEAD` is clean (tree e4f14f2e). Land must rebase only the 2 CARD-1783 commits.

## D-1 (blocker): the documented CP-merge-subset row is a vacuous command row

- **Where:** docs/orchestration-loop.md, the `### Merge process (CARD-1783)` table row `CP-merge-subset`. MergeHighRadiusSubsetTests pins `CP-merge-subset` but nothing pins that the row can run.
- **Failure:** the row's Filter cell is `scripts/impacted-classes.ps1 -MergeSubset -Range <base>..<tip> -Json`, and Min is `n/a`. I fed that exact row to `Antiphon.Checkpoints import --plan`. It imports as `isCommand: true` with `command: scripts/impacted-classes.ps1 ...`, and the build cell is ignored with a warning. Run as written, the row executes the selector, which only prints filters, exits 0, and runs zero tests. The result is a green CHECKPOINT with `executed=n/a`. Even if the Merge agent substitutes the filters, one row has one Build/project. The subset always spans two projects (tests/Antiphon.Tests and tests/Antiphon.E2E), and three when migrations change (tests/Antiphon.Messaging.Tests gets InboxConversationMigrationTests). So no single TUnit row can carry it.
- **Why it matters:** the card's point is that "an omission is visible". A row that is green while running no tests hides exactly the omission it exists to show. Because Min is n/a, a TUnit substitute that executes 0 tests is not caught either.
- **Fix:** replace the single command row with one TUnit row per project. CP-merge-subset-tests runs on tests/Antiphon.Tests with the fixed filter, with the migration classes appended when the diff touches migrations, and Min 21 (the fixed floor). CP-merge-subset-e2e runs on tests/Antiphon.E2E with `/*/*/(ContractSnapshotTests*)/*` and Min 5. CP-merge-subset-messaging runs on tests/Antiphon.Messaging.Tests only when `migrations=true`. Optionally keep the selector as a separate resolution step that is not a CHECKPOINT row. Extend MergeHighRadiusSubsetTests so it imports or parses the documented rows and goes red on a command row or on a missing Min.

## Advisories (do not hold the land)

- **A-1:** the prose says "A migration diff appends the downgrade classes to the `tests/Antiphon.Tests` filter". The selector also emits a tests/Antiphon.Messaging.Tests filter (InboxConversationMigrationTests). The prose should say "one row per `filters` entry".
- **A-2:** the text-mode output (no `-Json`) prints only the Antiphon.Tests `treenodeFilter`. It silently omits the E2E and Messaging filters. Either print every filter or refuse text mode for `-MergeSubset`.
