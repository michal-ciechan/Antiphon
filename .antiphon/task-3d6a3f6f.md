# CARD-1789 Review round 1 (Final/Full) — task 3d6a3f6f

Subject: Code 092e692a, origin/feat/card-task-092e692a, base a0e14b3e1e5c83425ac070fac5d9e953b8d1be74,
red 3151278953ee29df8d6f902fe6749b485bc724a5, tip bcdf6cbbb83451285ad643e921af0125d93d8735 (Grok Code author).

Verdict: CLEAN. No blockers, no regressions. Three advisories (below) go to a card, they do not hold the land.

## Checks

1. Rule text. testing-and-build.md (owner), project-context.md, the orchestrator skill and
   server/Bundles/stage-code.md all carry "Migrations should be additive; Down must fail safely" (or the
   bundle's compressed "additive, ...; Down fails safely") plus "sorted after master's latest
   [migration]". Owner, project-context and skill carry the dated attribution
   "(2026-11-07, reaffirmed 2026-11-08)"; the owner heading keeps "(operator rule, 2026-11-07)". The
   project-context link target `testing-and-build.md#additive-migrations-operator-rule-2026-11-07` resolves:
   the heading "### Additive migrations (operator rule, 2026-11-07)" occurs once (line 1197) and slugs to
   that anchor. The skill's selector list (`MigrateAsync(<target>)`, `GetService<IMigrator>().MigrateAsync(`,
   `MigrationTestDatabase.CreateAsync`, `.Down(`, `CreateIsolatedSchemaAsync` then backwards) matches the owner. The
   owner's remove-and-re-add instruction is consistent with "additive". stage-review's MIGRATION line is
   unchanged and does not contradict. No other doc/bundle states the rule (git grep).
2. stage-code bundle, word by word (2478 -> 2477 bytes, cap 2480):
   - SCOPE: "Plus the impacted-classes rows" -> "Plus impacted-classes rows": article only.
   - Platform: "read GET /api/runner-defaults and /api/session-runners" -> "GET /api/runner-defaults,
     /api/session-runners": same two routes, same obligation (stage-review already uses this form).
   - MIGRATION: old "grep tests/ for MigrateAsync(<target>), .Down(, CreateIsolatedSchemaAsync then back;
     run model classes; report red/green counts" -> new "additive, sorted after master's latest; Down
     fails safely. Run migrations row (downgrade selector, model classes); report red/green counts". The
     explicit grep tokens are replaced by the impacted-classes migrations row, which the SCOPE line already
     obliges and which the owner names as the selector. That row's derive patterns
     (`MigrateAsync\s*\(\s*[^)\s]`, `\bIMigrator\b`, `\bMigrationTestDatabase\b`, `\.Down\s*\(`) cover
     every old token (CreateIsolatedSchemaAsync-then-back is a MigrateAsync(<target>) call); model classes
     and red/green counts are kept. No operative requirement lost; the rule clauses are new.
   - Bookkeeping: bundle versions are runtime content hashes (`[bundle:stage-code v...]`); no generated
     copy or docs/ mirror of stage-code.md exists (git grep stage-code); the test byte comment is updated.
3. Selector-token witness `owner_selector_tokens_each_name_a_real_test_call_site` is real:
   - Red at 315127895 docs (fix-tip test dll over a git-archive of 315127895): 5/8 failed;
     this method failed with `unmatched ["IMigrator.Migrate"]`, the right reason.
   - Mutant M1 (owner token -> `GetService<IMigrator>().MigrateBackAsync(`): killed (this method + the owner test).
   - Mutant M2 (extra token `UnapplyMigrationAsync(<target>)` in the owner sentence): killed, unmatched names it.
   - Control on restored docs: 8/8 green. The witness excludes its own file by full path.
   - `GetService<IMigrator>().MigrateAsync(` is a real call in 19 test files (e.g.
     AgentTaskLandApprovalPersistenceTests.cs:78); `MigrationTestDatabase.CreateAsync` in 13 (e.g.
     C1514HostLoadMigrationTests.cs:33). `IMigrator.Migrate` occurs in 0 test files outside the witness.
4. tests/impacted-classes.json and scripts/impacted-classes.ps1 untouched is right:
   `impacted-classes.ps1 -Path server/Migrations/20261109000000_FakeAdditive.cs` selects kind migrations,
   55 classes / 69 files, and every one of the 19 `GetService<IMigrator>().MigrateAsync(` files is in it
   (AgentTaskLandApprovalPersistenceTests, C0824RefOnlySnapshotMigrationTests, InboxConversationMigrationTests,
   DispatchConcurrencyMigrationTests, AppDbContextModelTests all present). Downgrade selector count: 55 classes.
5. stage-test-design.md is not in base..tip (2472 bytes on disk); the dropped TestDesign qualifiers are a
   pre-existing owed note (card A-3 said "if bytes allow"), not a regression here.

## Runs at bcdf6cbbb (Linux server2, all through build slots)

- build Antiphon.Tests (UseAppHost=false): 0 errors, 253 s slot.
- Touched/named classes, one run: 161/161 passed, 0 failed, 0 skipped — AdditiveMigrationRuleWitnessTests 8,
  InstructionBundleTests 78, CheckpointRepeatDocumentationTests 1, ScopedVerificationInstructionTests 17,
  ImpactedClassesSelectorTests 25, CheckpointManifestDocumentationTests 8, AgentBundleAttachmentTests 24
  (the last two also read stage-code.md; added for blast radius).
- Guard batch, one run: 52/52 passed — TestClassificationPolicyTests 41, TestClassificationGuardTests 1,
  SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3.
- Red/mutant probes: 4 direct dll runs of AdditiveMigrationRuleWitnessTests (8 each) in a scratch copy
  (red, M1, M2 killed; M2 first attempt used `RewindAsync`, a real helper in
  DispatchConcurrencyMigrationTests, so it correctly survived and was replaced).
- Checkpoint receipt: two-row review extract `.antiphon/c1789-review-plan.md`, run 20261109-074330-06c7: CP-1 executed=161 passed=161 failed=0 skipped=0, CP-2 executed=52 passed=52, sourceState=clean buildSource=verified, verdict GREEN, unlisted none; `validate --expected-source-sha bcdf6cbbb...` -> `CHECKPOINT SOURCE VALID source=bcdf6cbbb83451285ad643e921af0125d93d8735 rows=2`. The earlier direct build-slot runs gave the per-class TRX counts above; the checkpoint run gives the receipt.
- check-evidence-diff a0e14b3e1..bcdf6cbbb: commits=2 entries=0 violations=0, exit 0.
- merge-tree vs origin/master 8acb630c643a0a7bcf730122f7802cd7165c08f0 (1 commit past base, CARD-1590 test
  files only): clean.

## Advisories (card; do not hold the land)

- A-1 `.Down(` passes the "real call site" witness on prose: its only tests/ occurrence outside the witness is
  the assertion message string at tests/Antiphon.Tests/Scripts/ImpactedClassesSelectorTests.cs:100
  (that message also contains `MigrateAsync(<target>)` literally). The witness proves textual presence in
  tests/, not a call site. Failure: a future doc-pin test quoting a dead token keeps the witness green. Fix:
  skip string literals/comments, or rename the method to "occurs in tests/". Not a blocker: the two A-2 tokens
  are real calls.
- A-2 The green commit edited the red commit's pinned CodeLine (dropped "the") to match the compressed
  bundle. Equivalent strength; noted only.
- A-3 TestDesign qualifiers remain owed at 2472/2480 bytes (pre-existing).

PCs: none in this card; nothing pending from this Review.
