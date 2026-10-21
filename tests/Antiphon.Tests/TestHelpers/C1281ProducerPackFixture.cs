using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Deterministic stand-in for the untracked CARD-1281 producer pack (CARD-1425). It writes the
/// minimal set of files the real CheckpointReceiptValidator and validate-checkpoint-receipt.ps1
/// read, and a one-commit selection repository that owns the pack's source commit, so the
/// correction tests run on every clone. Every byte is fixed, so the inventory digest is a pin.
/// </summary>
internal static class C1281ProducerPackFixture
{
    // Change only together with a deliberate fixture change; C1425_ProducerPackFixtureIsDeterministic guards it.
    public const string InventoryDigest = "3a695f00ff5a6875a5748152b55f83bbd5a30e8e9044ee2c68428b308d767316";

    public const string ProducerTask = "04228bff-e4a8-4ecd-bdbb-9315c2e41395";
    public const string Branch = "feat/card-task-04228bff";
    public const string Run = "20261015-115246-09c4";
    public const string SourceWorktree = "/fixture/c1425/producer-worktree";
    public const string SelectionPath = "docs/superpowers/plans/2026-10-15-card-1281-cheap-slices-checkpoints.md";
    public const string SelectionSection = "## Shared bindings B-1/B-2/B-3 (TestDesign, 2026-10-15, task 598a38d5)";
    private const string Build = "bin-c1281-foundation";
    private const string When = "2026-10-15T11:52:46Z";

    public sealed record Pack(string ProducerRoot, string SelectionRepository, string InventoryJson, string SourceCommit);

    private sealed record Row(string Id, string Filter, string[] Tests);

    // The three real CP-14..16 rows of the CARD-1281 selection, copied verbatim (11 cells each).
    private static readonly string[] PlanRows =
    [
        @"| CP-14 | S0-foundation | `tests/Antiphon.Tests -> bin-c1281-foundation/` | binding-schema | `/*/*/PipelineSpeedIntegrationTests/(C1281_B1Schema*)\|(C1281_B2Schema*)\|(C1281_B3Schema*)` | V-22,V-23,V-24,R-5 | all 3 methods, every schema variant, 0 failed/skipped | 3 | 6 |",
        @"| CP-15 | S0-foundation | CP-14 | binding-admission | `/*/*/PipelineSpeedIntegrationTests/(C1281_B2AtomicChild*)\|(C1281_B3AdmissionFence*)` | V-25,V-26,R-5,R-7,R-8 | both methods, both contender orders/cuts, 0 failed/skipped | 2 | 4 |",
        @"| CP-16 | S0-foundation | CP-14 | binding-old-locks | `/*/*/(CardWorkflowRunFactoryTests*)\|(ReviewEvidenceRecoveryTests*)/(CreateFromRevision_snapshots_every_stage_in_order_with_queued_status_and_first_stage_pointer*)\|(C1043_LockOrder*)` | R-5 | 2 methods, 0 failed/skipped | 2 | 3 |",
    ];

    private static readonly Row[] Rows =
    [
        new("CP-14", "/*/*/PipelineSpeedIntegrationTests/(C1281_B1Schema*)|(C1281_B2Schema*)|(C1281_B3Schema*)",
            ["C1281_B1Schema", "C1281_B2Schema", "C1281_B3Schema"]),
        new("CP-15", "/*/*/PipelineSpeedIntegrationTests/(C1281_B2AtomicChild*)|(C1281_B3AdmissionFence*)",
            ["C1281_B2AtomicChild", "C1281_B3AdmissionFence"]),
        new("CP-16", "/*/*/(CardWorkflowRunFactoryTests*)|(ReviewEvidenceRecoveryTests*)/(CreateFromRevision_snapshots_every_stage_in_order_with_queued_status_and_first_stage_pointer*)|(C1043_LockOrder*)",
            ["CreateFromRevision_snapshots_every_stage_in_order_with_queued_status_and_first_stage_pointer", "C1043_LockOrder"]),
    ];

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string Fingerprint => Sha256(Encoding.UTF8.GetBytes("c1425-fixture-build"));

    /// <summary>Writes the pack under <paramref name="root"/>/input and the selection repo under <paramref name="root"/>/selection-repo.</summary>
    public static async Task<Pack> WriteAsync(string root)
    {
        var selectionRepository = Path.Combine(root, "selection-repo");
        var commit = await CommitSelectionAsync(root, selectionRepository);
        var producerRoot = Path.Combine(root, "input");
        Directory.CreateDirectory(producerRoot);

        var runRelative = $".antiphon/checkpoints/{Run}";
        var files = new List<(string Relative, byte[] Bytes)>();
        var manifest = Text([
            "# CARD-1425 fixture copy of the CARD-1281 cheap-slices manifest",
            "build:",
            $"  - id: {Build}",
            "    project: tests/Antiphon.Tests",
            "rows:",
            .. Rows.Select(r => $"  - id: {r.Id}\n    build: {Build}\n    filter: \"{r.Filter}\""),
            ""]);
        files.Add(($"{runRelative}/manifest.resolved.yaml", manifest));
        files.Add(($"tests/Antiphon.Tests/{Build}/checkpoint-build-source.json", Json(new
        {
            version = 1, repositoryRoot = SourceWorktree, project = SourceWorktree + "/tests/Antiphon.Tests",
            outputPath = $"{SourceWorktree}/tests/Antiphon.Tests/{Build}", commit, fingerprint = Fingerprint,
            sourceState = "clean", pathName = $"{SourceWorktree}/tests/Antiphon.Tests/{Build}/checkpoint-build-source.json",
        })));
        var reportRows = new List<object>();
        foreach (var row in Rows)
        {
            var trxRelative = $"{runRelative}/rows/{row.Id}/run.trx";
            files.Add((trxRelative, Trx(row)));
            var trx = SourceWorktree + "/" + trxRelative;
            var n = row.Tests.Length;
            reportRows.Add(new
            {
                source = Source(commit), id = row.Id, filter = row.Filter, command = (string?)null, build = Build,
                state = "green", exitCode = 0, slot = "granted", executed = n, passed = n, failed = 0, skipped = 0,
                reruns = 0, trx,
                line = $"CHECKPOINT {row.Id} commit={commit} build=ok filter={row.Filter} executed={n} passed={n} failed=0 skipped=0 trx={trx} slot=granted waited=0s dirty=0 source={commit} sourceState=clean buildSource=verified",
                attempt = 1, repeat = (object?)null,
            });
        }
        files.Add(($"{runRelative}/report.json", Json(new
        {
            schemaVersion = 2, source = Source(commit), runId = Run,
            manifestPath = $"{SourceWorktree}/{runRelative}/manifest.resolved.yaml", manifestHash = Sha256(manifest),
            commit, branch = Branch, worktree = SourceWorktree, exitCode = 0, reason = (string?)null, verdict = "GREEN",
            rows = reportRows, unlisted = Array.Empty<string>(), cleanupRefusals = Array.Empty<string>(),
        })));
        files.Add(($"{runRelative}/report.md", Text([
            $"# Checkpoint run {Run} (CARD-1425 fixture)", "",
            .. Rows.Select(r => $"- {r.Id} green executed={r.Tests.Length}"), ""])));

        foreach (var (relative, bytes) in files)
        {
            var destination = Path.Combine(producerRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, bytes);
        }
        var inventory = Encoding.UTF8.GetString(Json(new
        {
            task = ProducerTask, sourceCommit = commit, sourceWorktree = SourceWorktree, cleanCheckpointRun = Run,
            files = files.Select(f => new { originalPath = SourceWorktree + "/" + f.Relative, relativePath = f.Relative,
                bytes = f.Bytes.LongLength, sha256 = Sha256(f.Bytes) }),
        }));
        await File.WriteAllTextAsync(Path.Combine(producerRoot, "producer-pack-index.json"), inventory);
        return new(producerRoot, selectionRepository, inventory, commit);
    }

    private static async Task<string> CommitSelectionAsync(string root, string repository)
    {
        var hooks = Path.Combine(root, "selection-hooks");
        Directory.CreateDirectory(hooks);
        var globalConfig = Path.Combine(root, "selection-gitconfig");
        await File.WriteAllTextAsync(globalConfig, "");
        Directory.CreateDirectory(repository);
        var env = new Dictionary<string, string>
        {
            ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_CONFIG_GLOBAL"] = globalConfig,
            ["GIT_AUTHOR_NAME"] = "CARD-1425 fixture", ["GIT_AUTHOR_EMAIL"] = "c1425-fixture@antiphon.invalid",
            ["GIT_AUTHOR_DATE"] = When, ["GIT_COMMITTER_NAME"] = "CARD-1425 fixture",
            ["GIT_COMMITTER_EMAIL"] = "c1425-fixture@antiphon.invalid", ["GIT_COMMITTER_DATE"] = When,
        };
        string[] config = ["-c", "core.autocrlf=false", "-c", "commit.gpgsign=false", "-c", "core.hooksPath=" + hooks];
        async Task<string> Git(params string[] args)
        {
            var result = await ScratchGitRepo.GitInAsync(repository, env, [.. config, .. args]);
            result.Ok.ShouldBeTrue($"fixture git {string.Join(' ', args)}: {result.StdErr}");
            return result.StdOut.Trim();
        }
        await Git("init", "--quiet", "--object-format=sha1", "--template=", "--initial-branch=master", ".");
        var plan = Path.Combine(repository, SelectionPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(plan)!);
        await File.WriteAllBytesAsync(plan, Text([
            "# CARD-1281 cheap-slices checkpoints (CARD-1425 fixture copy)", "",
            SelectionSection, "",
            "### Checkpoints", "",
            "| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |",
            "|---|---|---|---|---|---|---|---:|---:|",
            .. PlanRows, ""]));
        await Git("add", "--", SelectionPath);
        await Git("commit", "--quiet", "--no-verify", "-m", "CARD-1281 cheap-slices checkpoints (CARD-1425 fixture)");
        return await Git("rev-parse", "HEAD");
    }

    private static object Source(string commit)
    {
        object Point() => new { commit, dirtyFiles = 0, fingerprint = Fingerprint, observedAtUtc = When,
            captureStatus = "known", errorCode = (string?)null };
        return new { version = 1, start = Point(), end = Point(), state = "clean", buildSource = "verified" };
    }

    private static byte[] Trx(Row row) => Text([
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
        $"<TestRun name=\"c1425-fixture {row.Id}\" xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">",
        "  <Results>",
        .. row.Tests.Select(t => $"    <UnitTestResult testName=\"{t}\" outcome=\"Passed\" />"),
        "  </Results>",
        "</TestRun>", ""]);

    private static byte[] Text(IEnumerable<string> lines) => new UTF8Encoding(false).GetBytes(string.Join("\n", lines));
    private static byte[] Json(object value) => JsonSerializer.SerializeToUtf8Bytes(value);
}
