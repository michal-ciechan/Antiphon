using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1782. The path-glob table and scripts/impacted-classes.ps1 select the test
/// classes a high-blast-radius diff puts at risk. These witnesses are the ordinary
/// proof: they fail while the selector is absent or stale, and they name the classes
/// the 2026-11-08 rc misses actually broke.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed partial class ImpactedClassesSelectorTests
{
    private const string BatchSpan =
        "ff7cceaf4f65bed039f5b7f11dd726d27e762607^..9e7ff34988cd8c273d94db3c002975d93ea26f9e";

    private const string RepairFilesCommit = "517b705036e1496388a562835c03d3e15b522370";

    // The marker literal the linux-only-policy row matches. The A5 census (test-rc-platform.ps1
    // C1686_LinuxOnlyCensus) fails a code line that names the attribute outside a canonical marker,
    // so the literal is joined from two halves and never appears whole in this file.
    private const string MarkerLiteral = "[Native" + "LinuxOnly(";

    private static readonly string[] SeedKinds =
    [
        "migrations", "contracts", "timing-labels", "remote-scripts", "statement-budgets", "checkpoint-tool",
    ];

    [GeneratedRegex(@"MigrateAsync\s*\(\s*[^)\s]", RegexOptions.CultureInvariant)]
    private static partial Regex MigrateTarget();

    [GeneratedRegex(@"\bIMigrator\b", RegexOptions.CultureInvariant)]
    private static partial Regex MigratorType();

    [GeneratedRegex(@"\bMigrationTestDatabase\b", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationDatabase();

    [GeneratedRegex(@"\.Down\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex DownCall();

    [Test]
    public async Task seed_kinds_globs_and_owner_sentences_stay_present()
    {
        var root = Root();
        var table = await File.ReadAllTextAsync(Path.Combine(root, "tests", "impacted-classes.json"));
        foreach (var kind in SeedKinds)
            table.ShouldContain($"\"id\": \"{kind}\"", Case.Sensitive, kind);
        table.ShouldContain("\"cache\": \"none\"", Case.Sensitive, "no selector cache");

        var audit = await RunAsync(root, "-Audit", "-Json");
        audit.Exit.ShouldBe(0, audit.Text);
        using var json = JsonDocument.Parse(audit.Stdout);
        json.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue(audit.Text);
        json.RootElement.GetProperty("emptyGlobs").GetArrayLength().ShouldBe(0, audit.Text);
        json.RootElement.GetProperty("missingClasses").GetArrayLength().ShouldBe(0, audit.Text);

        var codeBundle = await File.ReadAllTextAsync(Path.Combine(root, "server", "Bundles", "stage-code.md"));
        codeBundle.ShouldContain(
            "impacted-classes rows whose globs match git diff --name-only base..HEAD",
            Case.Sensitive,
            "server/Bundles/stage-code.md");

        // CARD-1782 D-3. The review bundle must name the impacted rows beside the
        // downgrade selector. Deleting " +impacted rows" is the mutant that fails this witness.
        var reviewBundle = await File.ReadAllTextAsync(Path.Combine(root, "server", "Bundles", "stage-review.md"));
        reviewBundle.ShouldContain(
            "MIGRATION: run downgrade selector; report its count. +impacted rows",
            Case.Sensitive,
            "server/Bundles/stage-review.md");

        var guide = await File.ReadAllTextAsync(Path.Combine(root, "docs", "testing-and-build.md"));
        guide.ShouldContain("scripts/impacted-classes.ps1", Case.Sensitive, "owner usage");
        guide.ShouldContain(
            "Code and Review Final scope includes the impacted-classes rows",
            Case.Sensitive,
            "review scope stays in the owner doc");
        guide.ShouldContain("A Merge batch appends the union", Case.Sensitive, "merge usage");
        guide.ShouldContain("maximum staleness is zero", Case.Sensitive, "staleness");
    }

    [Test]
    public async Task downgrade_call_sites_are_exactly_the_derived_migration_files()
    {
        var root = Root();
        var audit = await RunAsync(root, "-Audit", "-Json");
        audit.Exit.ShouldBe(0, audit.Text);
        using var json = JsonDocument.Parse(audit.Stdout);
        var derived = json.RootElement.GetProperty("derivedFiles").EnumerateArray()
            .Select(row => row.GetString() ?? "")
            .ToHashSet(StringComparer.Ordinal);
        var scanned = ScanDowngradeFiles(root);
        var missing = scanned.Where(file => !derived.Contains(file)).Order(StringComparer.Ordinal).ToArray();
        var extra = derived.Where(file => !scanned.Contains(file)).Order(StringComparer.Ordinal).ToArray();
        missing.ShouldBeEmpty("MigrateAsync(<target>), IMigrator, MigrationTestDatabase or .Down( files outside the derived list: "
            + string.Join(", ", missing));
        extra.ShouldBeEmpty("derived list names files the downgrade scan does not: " + string.Join(", ", extra));
        scanned.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task audit_fails_when_a_named_class_is_missing()
    {
        var root = Root();
        var table = await File.ReadAllTextAsync(Path.Combine(root, "tests", "impacted-classes.json"));
        table.ShouldContain("AppDbContextModelTests", Case.Sensitive);
        var broken = table.Replace("AppDbContextModelTests", "NoSuchImpactedClassTests", StringComparison.Ordinal);
        var path = Path.Combine(Path.GetTempPath(), "impacted-classes-missing-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, broken);
        try
        {
            var audit = await RunAsync(root, "-Audit", "-Json", "-Table", path);
            audit.Exit.ShouldBe(1, audit.Text);
            audit.Stdout.ShouldContain("NoSuchImpactedClassTests", Case.Sensitive);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task audit_fails_when_a_glob_matches_nothing()
    {
        var root = Root();
        var broken = """
            {
              "schema": 1,
              "cache": "none",
              "kinds": [
                {
                  "id": "empty-glob",
                  "globs": ["scripts/lib/*.sh"],
                  "classes": ["ContractSnapshotTests"]
                }
              ]
            }
            """;
        var path = Path.Combine(Path.GetTempPath(), "impacted-classes-empty-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, broken);
        try
        {
            var audit = await RunAsync(root, "-Audit", "-Json", "-Table", path);
            audit.Exit.ShouldBe(1, audit.Text);
            audit.Stdout.ShouldContain("scripts/lib/*.sh", Case.Sensitive);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task telemetry_s1_span_selects_the_downgrade_files_and_the_contract_and_label_rows()
    {
        var root = Root();
        var selected = await SelectAsync(root, BatchSpan);
        // CARD-1801: the span also edits HostStats*.cs, the C1514 host-load fixture and the policy file.
        // CARD-1807: and it adds ParallelLimiter< lines under tests/Antiphon.Tests.
        // CARD-1819: those same paths also select the lane guard, the nightly lane, and the audit project row.
        Names(selected, "kinds").ShouldBe(
            [
                "contracts", "host-stats", "land-graph-audit-project", "linux-only-policy", "migrations",
                "nightly-live-lane", "spawn-limit-rosters", "task-load-window", "test-lane-category", "timing-labels",
            ],
            ignoreOrder: false);
        var files = Names(selected, "files").ToHashSet(StringComparer.Ordinal);
        var repair = Git(root, "diff-tree", "--no-commit-id", "--name-only", "-r", RepairFilesCommit);
        repair.Count.ShouldBe(37, "the host-load downgrade repair edited 37 files");
        var absent = repair.Where(file => !files.Contains(file)).ToArray();
        absent.ShouldBeEmpty("batch span did not list repaired downgrade files: " + string.Join(", ", absent));
        var classes = string.Join("\n", Names(selected, "classes"));
        foreach (var name in new[]
                 {
                     "AppDbContextModelTests", "C1514HostLoadMigrationTests", "StandingSessionOwnershipTests",
                     "CardFilePrivacyMigrationTests", "LandRequestWriteDiagnosticTests", "TranscriptHotPathMigrationTests",
                     "ContractSnapshotTests", "TestTimingOperationLabelTests",
                 })
            classes.ShouldContain(name, Case.Sensitive, name);
    }

    [Test]
    public async Task contract_snapshot_ranges_select_contract_snapshot_tests()
    {
        var root = Root();
        foreach (var sha in new[]
                 {
                     "f194ff221a7645bbcaecf9d44b9e65d5595dbbbf",
                     "ebd31b2498c814813ebe9180e73e61216b52f9bd",
                 })
        {
            var selected = await SelectAsync(root, sha + "^.." + sha);
            Names(selected, "kinds").ShouldContain("contracts", sha);
            var rendered = selected.RootElement.GetRawText();
            rendered.ShouldContain("ContractSnapshotTests", Case.Sensitive, sha);
            rendered.ShouldContain("tests/Antiphon.E2E", Case.Sensitive, sha);
        }
    }

    [Test]
    public async Task label_remote_budget_and_checkpoint_ranges_select_their_rows()
    {
        var root = Root();
        // CARD-1801: dcb973d8 also edits the policy file and c6eee1a0 the C1514 host-load fixture.
        // CARD-1807: c6eee1a0 adds a ParallelLimiter< class and ceda26b3 edits tests/Antiphon.Tests/Checkpoints.
        // CARD-1819: an Antiphon.Tests source edit selects the lane guard, and the policy file selects the nightly lane.
        await AssertKinds(
            root, "dcb973d896f1d12776a796b71466977b22737b79",
            ["linux-only-policy", "nightly-live-lane", "test-lane-category", "timing-labels"],
            "TestTimingOperationLabelTests");
        await AssertKinds(
            root, "c6eee1a0713a0c43238a23993acad8e20c0242f2",
            ["spawn-limit-rosters", "task-load-window", "test-lane-category", "timing-labels"],
            "TestTimingOperationLabelTests");
        await AssertKinds(
            root,
            "a54a6d4f755fa139b41fd7dc8aef456d3d2c0975",
            ["remote-scripts", "test-lane-category"],
            "C849_Deploy_temp_names_saved_import_when_seed_is_missing",
            "C0995_Deploy_temp_checks_host_absence_before_checkout",
            "C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle",
            "C849_Deploy_ordering_contract_is_pinned",
            "C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo");
        await AssertKinds(
            root,
            "43d819da7189349c548b07ebd2677e6be086d95b",
            ["statement-budgets", "test-lane-category"],
            "C1149_C1150_Statement_budgets");
        await AssertKinds(
            root,
            "ceda26b31ef344bc07efd590fa9bdb59bae0f23f",
            ["checkpoint-tool", "spawn-limit-rosters", "test-lane-category"],
            "CheckpointReceiptSafetyTests",
            "CheckpointTerminalReceiptTests",
            "HostCleanupCheckpointReceiptTests");
    }

    [Test]
    [Arguments("{}", "schema", "object without a kinds array")]
    [Arguments("[]", "kinds", "json array root")]
    [Arguments(
        "{\"schema\":1,\"kind\":[{\"id\":\"migrations\",\"globs\":[\"server/Migrations/**\"],\"classes\":[\"AppDbContextModelTests\"]}]}",
        "unknown property 'kind'",
        "kind-key typo")]
    [Arguments(
        "{\"schema\":1,\"kinds\":[{\"id\":\"bare\",\"classes\":[\"AppDbContextModelTests\"]}]}",
        "bare: no globs",
        "kind with no globs")]
    [Arguments(
        "{\"schema\":1,\"kinds\":[{\"id\":\"glob-only\",\"globs\":[\"server/Migrations/**\"]}]}",
        "glob-only: no classes",
        "kind with no classes, methods, or derive")]
    [Arguments(
        "{\"schema\":1,\"kinds\":[{\"id\":\"remote-scripts\",\"globs\":[\"scripts/*.sh\"],\"methods\":[\"RemoteScriptContractTests\"]}]}",
        "RemoteScriptContractTests",
        "methods entry without Class/Method")]
    [Arguments(
        "{\"schema\":1,\"kinds\":[{\"id\":\"migrations\",\"globs\":[\"server/Migrations/**\"],\"classes\":[\"MigrationTestDatabase\"]}]}",
        "MigrationTestDatabase",
        "real class that is not a *Tests class")]
    [Arguments("{\"schema\":2,\"kinds\":[]}", "schema", "schema other than 1")]
    [Arguments(
        "{\"schema\":1,\"kinds\":[{\"globs\":[\"server/Migrations/**\"],\"classes\":[\"AppDbContextModelTests\"]}]}",
        "missing id",
        "kind without an id")]
    [Arguments(
        "{\"schema\":1,\"kinds\":[{\"id\":\"contracts\",\"globs\":[\"client/src/api/**\"],\"class\":[\"ContractSnapshotTests\"]}]}",
        "unknown property 'class'",
        "unknown kind property")]
    [Arguments(
        "{\"schema\":1,\"kinds\":[{\"id\":\"lane\",\"globs\":[\"server/Migrations/**\"],\"classes\":[\"AppDbContextModelTests\"],\"windows\":\"yes\"}]}",
        "lane: windows must be 'required'",
        "windows lane other than required")]
    [Arguments(
        "{\"schema\":1,\"kinds\":[{\"id\":\"lane\",\"globs\":[\"server/Migrations/**\"],\"classes\":[\"AppDbContextModelTests\"],\"windows\":true}]}",
        "lane: windows must be 'required'",
        "windows lane as a boolean")]
    [Arguments(
        "{\"schema\":1,\"kinds\":[{\"id\":\"marker\",\"added\":{\"glob\":\"tests/**\",\"literals\":[\"" + MarkerLiteral + "\"],\"removed\":\"yes\"},\"classes\":[\"AppDbContextModelTests\"]}]}",
        "marker: removed must be true or false",
        "removed flag that is not a boolean")]
    public async Task malformed_table_is_refused(string table, string needle, string shape)
    {
        var root = Root();
        var path = Path.Combine(Path.GetTempPath(), "impacted-classes-bad-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, table);
        try
        {
            var audit = await RunAsync(root, "-Audit", "-Json", "-Table", path);
            audit.Exit.ShouldBe(1, shape + " audit " + audit.Text);
            audit.Text.ShouldContain(needle, Case.Sensitive, shape + " audit");
            using (var json = JsonDocument.Parse(audit.Stdout))
                json.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse(shape + " audit ok");

            var selected = await RunAsync(root, "-Path", "server/Migrations/x.cs", "-Json", "-Table", path);
            selected.Exit.ShouldBe(1, shape + " select " + selected.Text);
            selected.Text.ShouldContain(needle, Case.Sensitive, shape + " select");
            using var selectedJson = JsonDocument.Parse(selected.Stdout);
            selectedJson.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse(shape + " select ok");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task malformed_range_and_path_are_refused()
    {
        var root = Root();
        var both = await RunAsync(root, "-Range", "HEAD^..HEAD", "-Path", "server/Migrations/x.cs", "-Json");
        both.Exit.ShouldNotBe(0, both.Text);
        both.Text.ShouldContain("-Range", Case.Sensitive);
        both.Text.ShouldContain("-Path", Case.Sensitive);

        var list = Path.Combine(Path.GetTempPath(), "impacted-classes-list-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(list, "server/Migrations/x.cs\n");
        try
        {
            var withList = await RunAsync(root, "-Range", "HEAD^..HEAD", "-FileList", list, "-Json");
            withList.Exit.ShouldNotBe(0, withList.Text);
            withList.Text.ShouldContain("-Range", Case.Sensitive);
            withList.Text.ShouldContain("-FileList", Case.Sensitive);
        }
        finally
        {
            File.Delete(list);
        }
    }

    // CARD-1801. One row per 2026-11-08 why-missed note: a path the note names, the table kind that owns it,
    // the class or Class/Method that path selects, and whether the note asks for the native Windows lane.
    private static readonly (string Path, string Kind, string Selects, bool Windows)[] WhyMissedRows =
    [
        ("server/Application/Services/AgentTaskLandNotificationService.cs", "land-notification", "AgentTaskLandReceiptWatermarkTests", false),
        ("server/Application/Services/LandNotificationReconcileQueries.cs", "land-notification", "ReceiptScanDestinationIntegrationTests", false),
        ("tests/test-execution-policy.json", "linux-only-policy", "ReleaseGatePlatformTests/C1686_LinuxOnlyCensus", false),
        ("src/Antiphon.SessionRunner/HostStatsStore.cs", "host-stats", "RunnerCapabilitiesTests", false),
        ("server/Infrastructure/Git/WorktreeManager.cs", "server-git-start-sites", "ServerGitProcessTests", false),
        ("server/Infrastructure/ExternalChanges/ChangeDetectionService.cs", "server-git-start-sites", "ServerGitProcessTests", false),
        ("server/Infrastructure/Git/ServerGitIdentity.cs", "server-image-push-credentials", "ServerImagePushCredentialTests", true),
        ("scripts/server2-repository-mirrors.ps1", "server-repository-mirror", "ServerRepositoryMirrorScriptTests", true),
        ("server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs", "runner-read-poll", "Card1761RunnerReadPollTests", true),
        ("tests/Antiphon.Tests/TestHelpers/C1514HostLoadFixture.cs", "task-load-window", "C1514TaskLoadWindowTests", false),
        ("tests/Antiphon.Tests/Application/C0824SlotTransportWorld.cs", "workspace-slot-role-gate", "C0824WorkspaceSlotRoleGateTests", true),
        ("tests/Antiphon.Tests/Application/LandGraphLandRefreshTests.cs", "land-graph-land-refresh", "LandGraphLandRefreshTests", true),
        ("scripts/lib/nightly-core.ps1", "test-selection", "TestSelectionTests", false),
        ("tests/Antiphon.Tests/Checkpoints/PhaseJournalKillDiagnosticTests.cs", "spawn-limit-rosters", "ProcessSpawnLimitTests", false),
    ];

    private static readonly string[] LandNotificationClasses =
    [
        "AgentTaskLandHeldNotificationTests", "AgentTaskLandNotificationParkFollowUpTests",
        "AgentTaskLandNotificationPersistenceTests", "AgentTaskLandNotificationReconcileLoadTests",
        "AgentTaskLandNotificationRecoveryTests", "AgentTaskLandNotificationRescanTests",
        "AgentTaskLandReceiptTests",
        "AgentTaskLandReceiptWatermarkSafetyTests", "AgentTaskLandReceiptWatermarkTests",
        "ExpectationDebtTests", "LandReceiptScanCacheTests", "PreDispatchFailureDeliveryTests",
        "ReceiptScanDestinationIntegrationTests",
    ];

    // The C1432 roster: every server file that starts a git process.
    private static readonly string[] ServerGitStartSites =
    [
        "server/Infrastructure/Git/LandingGit.cs", "server/Infrastructure/Git/GitService.cs",
        "server/Infrastructure/Git/WorktreeManager.cs", "server/Infrastructure/Git/CardFileRepository.cs",
        "server/Application/Services/GitWorkspaceService.cs", "server/Application/Services/DelegationWorktreeService.cs",
        "server/Application/Services/DelegationWorkspaceResolver.cs", "server/Application/Services/OrchestratorWorkspaceLayout.cs",
        "server/Application/Services/AgentTaskReplyService.cs", "server/Infrastructure/ExternalChanges/ChangeDetectionService.cs",
    ];

    // Downgrade callers the notes name. The derive scan finds them today; the explicit list keeps
    // them selected after a fixture repair stops matching a derive pattern.
    private static readonly string[] DowngradeCallerClasses =
    [
        "AgentTaskLandHeldNotificationTests", "AgentTaskLandNotificationPersistenceTests", "DispatchBaseNotificationTests",
        "RunnerDefaultMigrationTests", "SessionRuntimeActionRoutingTests", "StandingRestartEscalationTests",
        "StandingSpecialistRoutingMigrationTests", "WorktreeBaseSelectionTests",
    ];

    private const string WindowsTestsFilter =
        "/*/*/(C0824WorkspaceSlotRoleGateTests*)|(Card1761RunnerReadPollTests*)|(LandGraphLandRefreshTests*)"
        + "|(ServerImagePushCredentialTests*)|(ServerRepositoryMirrorScriptTests*)/*";

    private const string LinuxOnlyCensusFilter =
        "/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)|(LinuxOnlyWindowsExclusionPolicy*)";

    // CARD-1677 S2 witnesses: adds two [NativeLinuxOnly( markers and does not touch the policy file.
    private const string MarkerCommit = "c9cc1a1c0c59252f6bce2ecec96996e84cdcac67";

    [Test]
    public async Task C1801_why_missed_rows_are_table_rows_with_their_windows_lane()
    {
        var root = Root();
        var table = await File.ReadAllTextAsync(Path.Combine(root, "tests", "impacted-classes.json"));
        using (var json = JsonDocument.Parse(table))
            AssertWhyMissedTable(json.RootElement);

        // Each mutant loses one note's row: a renamed kind, a dropped Windows lane, a dropped class or start site.
        foreach (var (from, to) in new[]
                 {
                     ("\"id\": \"runner-read-poll\"", "\"id\": \"runner-read-poll-renamed\""),
                     ("\"windows\": \"required\"", "\"windowsLane\": \"required\""),
                     ("\"AgentTaskLandReceiptWatermarkSafetyTests\"", "\"AgentTaskLandReceiptWatermarkTests\""),
                     ("\"RunnerDefaultMigrationTests\"", "\"AppDbContextModelTests\""),
                     ("\"server/Infrastructure/ExternalChanges/ChangeDetectionService.cs\"", "\"server/Infrastructure/Git/GitService.cs\""),
                     ("\"removed\": true", "\"removed\": false"),
                 })
        {
            var at = table.IndexOf(from, StringComparison.Ordinal);
            at.ShouldBeGreaterThanOrEqualTo(0, from);
            var mutant = table[..at] + to + table[(at + from.Length)..];
            using var broken = JsonDocument.Parse(mutant);
            Should.Throw<ShouldAssertException>(() => AssertWhyMissedTable(broken.RootElement), from);
        }
    }

    [Test]
    public async Task C1801_why_missed_paths_select_their_classes_and_windows_filters()
    {
        var root = Root();
        var list = Path.Combine(Path.GetTempPath(), "impacted-classes-why-missed-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(list, string.Join("\n", WhyMissedRows.Select(row => row.Path)) + "\n");
        try
        {
            var ran = await RunAsync(root, "-FileList", list, "-Json");
            ran.Exit.ShouldBe(0, ran.Text);
            using var json = JsonDocument.Parse(ran.Stdout);
            AssertWhyMissedSelection(json.RootElement);

            var text = await RunAsync(root, "-FileList", list);
            text.Exit.ShouldBe(0, text.Text);
            text.Stdout.Replace("\r\n", "\n").Split('\n')
                .ShouldContain("windowsFilter=tests/Antiphon.Tests " + WindowsTestsFilter);

            var raw = json.RootElement.GetRawText();
            var mutant = raw.Replace("(LandGraphLandRefreshTests*)|", "", StringComparison.Ordinal);
            mutant.ShouldNotBe(raw);
            using var broken = JsonDocument.Parse(mutant);
            Should.Throw<ShouldAssertException>(() => AssertWhyMissedSelection(broken.RootElement));
        }
        finally
        {
            File.Delete(list);
        }
    }

    // CARD-1801 R2-D-1: the non-merge output is what a Code or Review stage runs for its diff, so its
    // treenodeFilter (text line and -Json top level) keeps every selected class in one filter; only
    // -MergeSubset splits land classes into -land-<n> rows.
    [Test]
    public async Task C1801_non_merge_land_notification_filter_names_every_class()
    {
        var root = Root();
        var expected = "/*/*/" + string.Join("|", LandNotificationClasses.Select(name => "(" + name + "*)")) + "/*";
        // CARD-1819 repair: AgentTaskLandDeliveryE2ETests asserts Windows in LandDeliveryFixture
        // and must not be on the Linux filter. The probe stays the Linux half.
        const string e2e = "/*/*/(LandDeliveryFixtureProbeTests*)/*";
        const string windowsE2e = "/*/*/(AgentTaskLandDeliveryE2ETests*)/*";
        const string path = "server/Application/Services/AgentTaskLandNotificationService.cs";

        var text = await RunAsync(root, "-Path", path);
        text.Exit.ShouldBe(0, text.Text);
        text.Stdout.Replace("\r\n", "\n").Split('\n')
            .Where(line => line.StartsWith("treenodeFilter=", StringComparison.Ordinal))
            .ShouldBe(["treenodeFilter=" + expected]);

        var ran = await RunAsync(root, "-Path", path, "-Json");
        ran.Exit.ShouldBe(0, ran.Text);
        using var json = JsonDocument.Parse(ran.Stdout);
        json.RootElement.GetProperty("treenodeFilter").GetString().ShouldBe(expected);
        // The printed treenodeFilter stays the Antiphon.Tests row; the E2E row is the Linux probe only.
        FilterRows(json.RootElement, "filters").ShouldBe([
            ("tests/Antiphon.E2E", e2e),
            ("tests/Antiphon.Tests", expected),
        ]);
        FilterRows(json.RootElement, "windowsFilters").ShouldBe([
            ("tests/Antiphon.E2E", windowsE2e),
        ]);
    }

    [Test]
    public async Task C1801_native_linux_only_marker_edits_select_the_census_pair()
    {
        var root = Root();
        using (var added = await SelectAsync(root, MarkerCommit + "^.." + MarkerCommit))
            AssertCensusPair(added.RootElement);

        // A removed marker selects the pair too. No commit on master removes one, so a scratch repository does,
        // read through the real table's row.
        var table = await File.ReadAllTextAsync(Path.Combine(root, "tests", "impacted-classes.json"));
        string kind;
        using (var json = JsonDocument.Parse(table))
        {
            kind = json.RootElement.GetProperty("kinds").EnumerateArray()
                .Single(row => row.GetProperty("id").GetString() == "linux-only-policy").GetRawText();
        }

        var scratch = Path.Combine(Path.GetTempPath(), "impacted-classes-marker-" + Guid.NewGuid().ToString("N"));
        var tablePath = scratch + ".json";
        try
        {
            CreateMarkerRemovalRepository(scratch);
            await File.WriteAllTextAsync(tablePath, "{\"schema\":1,\"kinds\":[" + kind + "]}");
            var removed = await RunAsync(root, "-Repo", scratch, "-Table", tablePath, "-Range", "HEAD^..HEAD", "-Json");
            removed.Exit.ShouldBe(0, removed.Text);
            using (var selected = JsonDocument.Parse(removed.Stdout))
                AssertCensusPair(selected.RootElement);

            var addedOnly = kind.Replace("\"removed\": true", "\"removed\": false", StringComparison.Ordinal);
            addedOnly.ShouldNotBe(kind);
            await File.WriteAllTextAsync(tablePath, "{\"schema\":1,\"kinds\":[" + addedOnly + "]}");
            var missed = await RunAsync(root, "-Repo", scratch, "-Table", tablePath, "-Range", "HEAD^..HEAD", "-Json");
            missed.Exit.ShouldBe(0, missed.Text);
            using var notSelected = JsonDocument.Parse(missed.Stdout);
            Should.Throw<ShouldAssertException>(() => AssertCensusPair(notSelected.RootElement));
        }
        finally
        {
            File.Delete(tablePath);
            DeleteTree(scratch);
        }
    }

    private static void AssertWhyMissedTable(JsonElement root)
    {
        var kinds = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var row in root.GetProperty("kinds").EnumerateArray())
            kinds[row.GetProperty("id").GetString() ?? ""] = row;
        foreach (var row in WhyMissedRows)
        {
            kinds.ContainsKey(row.Kind).ShouldBeTrue(row.Kind);
            var kind = kinds[row.Kind];
            Strings(kind, "classes").Concat(Strings(kind, "methods")).ShouldContain(row.Selects, row.Kind);
            var lane = kind.TryGetProperty("windows", out var windows) ? windows.GetString() : null;
            lane.ShouldBe(row.Windows ? "required" : null, row.Kind + " windows lane");
        }

        Strings(kinds["land-notification"], "classes").ShouldBe(LandNotificationClasses, ignoreOrder: true);
        Strings(kinds["server-git-start-sites"], "globs").ShouldBe(ServerGitStartSites, ignoreOrder: true);
        var migrations = Strings(kinds["migrations"], "classes");
        foreach (var name in DowngradeCallerClasses)
            migrations.ShouldContain(name, "migrations " + name);
        var census = kinds["linux-only-policy"];
        Strings(census, "methods").ShouldBe(
            ["ReleaseGatePlatformTests/C1686_LinuxOnlyCensus", "ReleaseGatePlatformTests/LinuxOnlyWindowsExclusionPolicy"],
            ignoreOrder: true);
        var marker = census.GetProperty("added");
        Strings(marker, "literals").ShouldBe([MarkerLiteral]);
        marker.GetProperty("removed").GetBoolean().ShouldBeTrue("a removed marker changes the census too");
    }

    private static void AssertWhyMissedSelection(JsonElement root)
    {
        var kinds = root.GetProperty("kinds").EnumerateArray().Select(row => row.GetString() ?? "").ToList();
        var filters = FilterRows(root, "filters");
        var windows = FilterRows(root, "windowsFilters");
        foreach (var row in WhyMissedRows)
        {
            kinds.ShouldContain(row.Kind, row.Path);
            var slash = row.Selects.IndexOf('/');
            var needle = "(" + (slash < 0 ? row.Selects : row.Selects[(slash + 1)..]) + "*)";
            filters.Any(filter => filter.Filter.Contains(needle, StringComparison.Ordinal)).ShouldBeTrue(row.Path + " " + needle);
            windows.Any(filter => filter.Filter.Contains(needle, StringComparison.Ordinal))
                .ShouldBe(row.Windows, row.Path + " windows lane " + needle);
        }

        filters.ShouldContain(("tests/Antiphon.Tests", LinuxOnlyCensusFilter));
        filters.ShouldContain(("tests/Antiphon.SessionRunner.Tests", "/*/*/(RunnerCapabilitiesTests*)/*"));
        windows.ShouldBe([
            ("tests/Antiphon.E2E", "/*/*/(AgentTaskLandDeliveryE2ETests*)/*"),
            ("tests/Antiphon.Tests", WindowsTestsFilter),
        ]);
    }

    private static void AssertCensusPair(JsonElement root)
    {
        root.GetProperty("kinds").EnumerateArray().Select(row => row.GetString() ?? "").ShouldContain("linux-only-policy");
        FilterRows(root, "filters").ShouldContain(("tests/Antiphon.Tests", LinuxOnlyCensusFilter));
    }

    private static List<(string Project, string Filter)> FilterRows(JsonElement root, string property) =>
        root.GetProperty(property).EnumerateArray()
            .Select(row => (row.GetProperty("project").GetString() ?? "", row.GetProperty("treenodeFilter").GetString() ?? ""))
            .ToList();

    private static List<string> Strings(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value)
            ? value.EnumerateArray().Select(item => item.GetString() ?? "").ToList()
            : [];

    private static void CreateMarkerRemovalRepository(string dir)
    {
        var scripts = Path.Combine(dir, "tests", "Antiphon.Tests", "Scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "ReleaseGatePlatformTests.cs"), "public sealed class ReleaseGatePlatformTests\n{\n}\n");
        var marked = Path.Combine(scripts, "MarkerTests.cs");
        File.WriteAllText(
            marked,
            "public sealed class MarkerTests\n{\n    " + MarkerLiteral + "\"fixture\")]\n    public void Linux() { }\n}\n");
        Git(dir, "init", "-q");
        Git(dir, "add", "-A");
        Git(dir, "-c", "user.name=impacted", "-c", "user.email=impacted@example.invalid", "-c", "commit.gpgsign=false",
            "commit", "-q", "-m", "marked");
        File.WriteAllText(marked, "public sealed class MarkerTests\n{\n    public void Linux() { }\n}\n");
        Git(dir, "-c", "user.name=impacted", "-c", "user.email=impacted@example.invalid", "-c", "commit.gpgsign=false",
            "commit", "-q", "-am", "unmarked");
    }

    private static void DeleteTree(string dir)
    {
        if (!Directory.Exists(dir))
            return;
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(dir, recursive: true);
    }

    private static async Task AssertKinds(string root, string sha, string[] kinds, params string[] needles)
    {
        var selected = await SelectAsync(root, sha + "^.." + sha);
        Names(selected, "kinds").ShouldBe(kinds, ignoreOrder: false, customMessage: sha);
        var rendered = selected.RootElement.GetRawText();
        foreach (var needle in needles)
            rendered.ShouldContain(needle, Case.Sensitive, sha + " " + needle);
    }

    private static HashSet<string> ScanDowngradeFiles(string root)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var tests = Path.Combine(root, "tests");
        foreach (var file in Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
                continue;
            var text = File.ReadAllText(file);
            if (MigrateTarget().IsMatch(text) || MigratorType().IsMatch(text)
                || MigrationDatabase().IsMatch(text) || DownCall().IsMatch(text))
                found.Add(relative);
        }

        return found;
    }

    private static async Task<JsonDocument> SelectAsync(string root, string range)
    {
        var ran = await RunAsync(root, "-Range", range, "-Json");
        ran.Exit.ShouldBe(0, range + " " + ran.Text);
        return JsonDocument.Parse(ran.Stdout);
    }

    private static List<string> Names(JsonDocument json, string property) =>
        json.RootElement.GetProperty(property).EnumerateArray()
            .Select(row => row.GetString() ?? "")
            .ToList();

    private static List<string> Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.quotepath=false");
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, stderr);
        return stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static async Task<(int Exit, string Stdout, string Text)> RunAsync(string root, params string[] args)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(root, "scripts", "impacted-classes.ps1"));
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await process.WaitForExitAsync(timeout.Token);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (process.ExitCode, stdout, stdout + stderr);
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }

    // CARD-1819. One fake diff per why-missed row that was still absent from the table.
    // spawn-limit-rosters is already a CARD-1807 row and is not repeated here.
    [Test]
    public async Task C1819_pty_host_fixture_selects_the_fixture_class_on_windows()
    {
        await AssertFakeDiff(
            "src/Antiphon.SessionRunner/TranscriptNormalizer.cs",
            "pty-host-fixture",
            "Card1277PtyHostFixtureTests",
            windows: true);
    }

    [Test]
    public async Task C1819_dispatcher_selects_pre_dispatch_failure_delivery()
    {
        await AssertFakeDiff(
            "server/Application/Services/AgentTaskDispatcher.cs",
            "land-notification",
            "PreDispatchFailureDeliveryTests",
            windows: false);
    }

    [Test]
    public async Task C1819_migration_sql_text_selects_both_migration_classes_on_windows()
    {
        await AssertFakeDiff(
            "server/Migrations/20261108121426_Card1773TranscriptPromptStampIndex.cs",
            "migration-generated-sql-text",
            "Card1773TranscriptPromptStampMigrationTests",
            windows: true,
            also: "TranscriptHotPathMigrationTests");
    }

    [Test]
    public async Task C1819_landing_git_selects_repository_mutation_lease_tests()
    {
        await AssertFakeDiff(
            "server/Infrastructure/Git/LandingGit.cs",
            "repository-lease-reflected-constructors",
            "RepositoryMutationLeaseTests",
            windows: false);
    }

    [Test]
    public async Task C1819_land_graph_core_selects_the_evaluation_class()
    {
        await AssertFakeDiff(
            "tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs",
            "land-graph-precheck",
            "LandGraphEvaluationTests",
            windows: false);
    }

    [Test]
    public async Task C1819_test_project_selects_the_land_graph_project_instance_class()
    {
        await AssertFakeDiff(
            "tests/Antiphon.Tests/Antiphon.Tests.csproj",
            "land-graph-audit-project",
            "LandGraphAuditProjectInstanceTests",
            windows: false);
    }

    [Test]
    public async Task C1819_antiphon_tests_edit_selects_the_lane_category_guard()
    {
        await AssertFakeDiff(
            "tests/Antiphon.Tests/Application/C1708ReflogSalvageTests.cs",
            "test-lane-category",
            "TestLaneCategoryGuardTests",
            windows: false);
    }

    [Test]
    public async Task C1819_e2e_source_edit_selects_the_expanded_budget_method()
    {
        await AssertFakeDiff(
            "tests/Antiphon.E2E/AgentTaskLandDeliveryE2ETests.cs",
            "e2e-source-roster",
            "C1584_E2EExpandedBudget",
            windows: false);
    }

    [Test]
    public async Task C1819_capability_script_selects_the_dpapi_classes_on_windows()
    {
        await AssertFakeDiff(
            "scripts/capability.ps1",
            "dpapi-protected-data",
            "CapabilityScriptTests",
            windows: true,
            also: "DelegateScriptCapabilityTests",
            windowsOnly: true,
            alsoWindowsOnly: true);
    }

    [Test]
    public async Task C1819_land_notification_host_selects_the_delivery_e2e_and_the_linux_probe()
    {
        await AssertFakeDiff(
            "server/Infrastructure/Orchestration/AgentTaskLandNotificationHostedService.cs",
            "e2e-land-delivery",
            "AgentTaskLandDeliveryE2ETests",
            windows: true,
            also: "LandDeliveryFixtureProbeTests",
            windowsOnly: true);
    }

    // CARD-1819 repair. "windows": "only" is the form that owes the class on Windows and
    // leaves every Linux filter free of it. "required" still runs on Linux and is owed too.
    [Test]
    public async Task C1819_windows_only_lane_stays_out_of_linux_filters()
    {
        var root = Root();
        var only = await SelectLaneAsync(root, "only");
        AssertLane(only.RootElement, linux: false);
        var dropped = only.RootElement.GetRawText().Replace(
            "AppDbContextModelTests", "DroppedC1819Selection", StringComparison.Ordinal);
        dropped.ShouldNotBe(only.RootElement.GetRawText());
        using (var broken = JsonDocument.Parse(dropped))
            Should.Throw<ShouldAssertException>(() => AssertLane(broken.RootElement, linux: false));

        var required = await SelectLaneAsync(root, "required");
        AssertLane(required.RootElement, linux: true);
    }

    // The ProtectedData literal must not match the table or a roster JSON. A real Antiphon.Tests
    // source line still selects the DPAPI classes.
    [Test]
    public async Task C1819_protected_data_literal_does_not_select_the_table()
    {
        var root = Root();
        var table = Path.Combine(root, "tests", "impacted-classes.json");
        var source = Path.Combine(Path.GetTempPath(), "impacted-classes-protected-source-" + Guid.NewGuid().ToString("N"));
        var tableDiff = Path.Combine(Path.GetTempPath(), "impacted-classes-protected-table-" + Guid.NewGuid().ToString("N"));
        try
        {
            CreateLiteralDiffRepository(
                source,
                "tests/Antiphon.Tests/Application/OtherProtectedTests.cs",
                "public sealed class OtherProtectedTests\n{\n}\n",
                "public sealed class OtherProtectedTests\n{\n    // ProtectedData\n}\n");
            var selected = await RunAsync(root, "-Repo", source, "-Table", table, "-Range", "HEAD^..HEAD", "-Json");
            selected.Exit.ShouldBe(0, selected.Text);
            using (var json = JsonDocument.Parse(selected.Stdout))
                Names(json, "kinds").ShouldContain("dpapi-protected-data");
            var dropped = selected.Stdout.Replace("dpapi-protected-data", "DroppedC1819Selection", StringComparison.Ordinal);
            dropped.ShouldNotBe(selected.Stdout);
            using (var broken = JsonDocument.Parse(dropped))
                Should.Throw<ShouldAssertException>(() => Names(broken, "kinds").ShouldContain("dpapi-protected-data"));

            CreateLiteralDiffRepository(
                tableDiff,
                "tests/impacted-classes.json",
                "{\n  \"schema\": 1\n}\n",
                "{\n  \"schema\": 1,\n  \"note\": \"ProtectedData\"\n}\n");
            var self = await RunAsync(root, "-Repo", tableDiff, "-Table", table, "-Range", "HEAD^..HEAD", "-Json");
            self.Exit.ShouldBe(0, self.Text);
            using var selfJson = JsonDocument.Parse(self.Stdout);
            Names(selfJson, "kinds").ShouldNotContain("dpapi-protected-data");
            var injected = self.Stdout.Contains("\"kinds\": []", StringComparison.Ordinal)
                ? self.Stdout.Replace("\"kinds\": []", "\"kinds\": [\"dpapi-protected-data\"]", StringComparison.Ordinal)
                : self.Stdout.Replace("\"kinds\": [", "\"kinds\": [\"dpapi-protected-data\", ", StringComparison.Ordinal);
            injected.ShouldNotBe(self.Stdout);
            using var brokenSelf = JsonDocument.Parse(injected);
            Should.Throw<ShouldAssertException>(() => Names(brokenSelf, "kinds").ShouldNotContain("dpapi-protected-data"));
        }
        finally
        {
            DeleteTree(source);
            DeleteTree(tableDiff);
        }
    }

    [Test]
    public async Task C1819_pty_test_root_selects_the_temp_family_registry()
    {
        await AssertFakeDiff(
            "tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs",
            "temp-family-registry",
            "C1677TempFamilyRegistryTests",
            windows: false);
    }

    [Test]
    public async Task C1819_nightly_policy_selects_the_live_lane_and_nightly_core_classes()
    {
        await AssertFakeDiff(
            "tests/test-execution-policy.json",
            "nightly-live-lane",
            "LiveLaneRunnerTests",
            windows: false,
            also: "NightlyCoreTests");
    }

    private static async Task AssertFakeDiff(
        string path, string kind, string needle, bool windows, string? also = null,
        bool windowsOnly = false, bool alsoWindowsOnly = false)
    {
        var root = Root();
        var ran = await RunAsync(root, "-Path", path, "-Json");
        ran.Exit.ShouldBe(0, path + " " + ran.Text);
        using (var json = JsonDocument.Parse(ran.Stdout))
            AssertSelected(json.RootElement, kind, needle, windows, also, windowsOnly, alsoWindowsOnly);

        if (windowsOnly)
        {
            var merged = await RunAsync(root, "-MergeSubset", "-Path", path, "-Json");
            merged.Exit.ShouldBe(0, path + " merge " + merged.Text);
            using var mergeJson = JsonDocument.Parse(merged.Stdout);
            AssertSelected(mergeJson.RootElement, kind, needle, windows, also, windowsOnly, alsoWindowsOnly);
            var mergeMutant = merged.Stdout.Replace(needle, "DroppedC1819Selection", StringComparison.Ordinal);
            mergeMutant.ShouldNotBe(merged.Stdout, needle);
            using var brokenMerge = JsonDocument.Parse(mergeMutant);
            Should.Throw<ShouldAssertException>(() =>
                AssertSelected(brokenMerge.RootElement, kind, needle, windows, also, windowsOnly, alsoWindowsOnly));
        }

        // The replacement must not contain the needle: ShouldContain would still match a prefixed copy.
        var mutant = ran.Stdout.Replace(needle, "DroppedC1819Selection", StringComparison.Ordinal);
        mutant.ShouldNotBe(ran.Stdout, needle);
        using var broken = JsonDocument.Parse(mutant);
        Should.Throw<ShouldAssertException>(() =>
            AssertSelected(broken.RootElement, kind, needle, windows, also, windowsOnly, alsoWindowsOnly));
    }

    private static void AssertSelected(
        JsonElement root, string kind, string needle, bool windows, string? also,
        bool windowsOnly = false, bool alsoWindowsOnly = false)
    {
        root.GetProperty("kinds").EnumerateArray().Select(row => row.GetString() ?? "")
            .ShouldContain(kind);
        var rendered = root.GetRawText();
        rendered.ShouldContain(needle, Case.Sensitive, kind + " " + needle);
        if (also is not null)
            rendered.ShouldContain(also, Case.Sensitive, kind + " " + also);
        var windowsHit = root.GetProperty("windowsFilters").EnumerateArray()
            .Any(row => (row.GetProperty("treenodeFilter").GetString() ?? "").Contains(needle, StringComparison.Ordinal));
        windowsHit.ShouldBe(windows, kind + " windows lane " + needle);
        if (!windowsOnly)
            return;

        LinuxFilters(root).Any(filter => filter.Contains(needle, StringComparison.Ordinal))
            .ShouldBeFalse(kind + " linux filter " + needle);
        if (also is null)
            return;
        if (alsoWindowsOnly)
        {
            LinuxFilters(root).Any(filter => filter.Contains(also, StringComparison.Ordinal))
                .ShouldBeFalse(kind + " linux filter " + also);
            root.GetProperty("windowsFilters").EnumerateArray()
                .Any(row => (row.GetProperty("treenodeFilter").GetString() ?? "").Contains(also, StringComparison.Ordinal))
                .ShouldBeTrue(kind + " windows lane " + also);
        }
        else
        {
            LinuxFilters(root).Any(filter => filter.Contains(also, StringComparison.Ordinal))
                .ShouldBeTrue(kind + " linux filter " + also);
        }
    }

    private static async Task<JsonDocument> SelectLaneAsync(string root, string lane)
    {
        var path = Path.Combine(Path.GetTempPath(), "impacted-classes-lane-" + Guid.NewGuid().ToString("N") + ".json");
        var table = "{\"schema\":1,\"kinds\":[{\"id\":\"lane\",\"globs\":[\"server/Migrations/**\"],\"classes\":[\"AppDbContextModelTests\"],\"windows\":\""
            + lane + "\"}]}";
        await File.WriteAllTextAsync(path, table);
        try
        {
            var ran = await RunAsync(root, "-Path", "server/Migrations/x.cs", "-Json", "-Table", path);
            ran.Exit.ShouldBe(0, lane + " " + ran.Text);
            return JsonDocument.Parse(ran.Stdout);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertLane(JsonElement root, bool linux)
    {
        const string needle = "AppDbContextModelTests";
        root.GetProperty("kinds").EnumerateArray().Select(row => row.GetString() ?? "").ShouldContain("lane");
        LinuxFilters(root).Any(filter => filter.Contains(needle, StringComparison.Ordinal)).ShouldBe(linux, needle);
        root.GetProperty("windowsFilters").EnumerateArray()
            .Any(row => (row.GetProperty("treenodeFilter").GetString() ?? "").Contains(needle, StringComparison.Ordinal))
            .ShouldBeTrue(needle);
    }

    private static List<string> LinuxFilters(JsonElement root) =>
        FilterRows(root, "filters").Select(row => row.Filter).ToList();

    private static void CreateLiteralDiffRepository(string dir, string relative, string before, string after)
    {
        var file = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, before);
        Git(dir, "init", "-q");
        Git(dir, "add", "-A");
        Git(dir, "-c", "user.name=impacted", "-c", "user.email=impacted@example.invalid", "-c", "commit.gpgsign=false",
            "commit", "-q", "-m", "before");
        File.WriteAllText(file, after);
        Git(dir, "-c", "user.name=impacted", "-c", "user.email=impacted@example.invalid", "-c", "commit.gpgsign=false",
            "commit", "-q", "-am", "after");
    }
}
