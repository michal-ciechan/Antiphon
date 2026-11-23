using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1500: the rc qualification status (RcQualificationStatus, `antiphon-scripts rc-qualification-status`), in-process. These
/// cases replaced the C1500 fixture scripts/fixtures/c1500-status.ps1, which scripts/test-release-gate.ps1 dot-sourced and
/// these tests ran through it (plan 2026-11-22-remaining-ps1-to-csharp.md, unit 7). The records are real files in a temp
/// root (the share-mode and identity checks are file-system behaviour); the owners are a QualificationProcessWorld.
/// Not ported: the Windows PowerShell 5.1 and pwsh host rows of C1500_WindowsSharedReadAndPs51 (PowerShell hosts; the
/// shared-read row stays) and the entry's Start-Process/Stop-Process/Remove-Item/Invoke-RestMethod interception (PowerShell
/// mechanics; the core has a recording launcher and qualification seams, and the test checks neither was used).
/// </summary>
[Category("Unit")]
public sealed class RcQualificationStatusTests
{
    private const string ShaA = "0123456789abcdef0123456789abcdef01234567";
    private const string ShaB = "fedcba9876543210fedcba9876543210fedcba98";
    private const int Owner = 900;

    private sealed class Fixture : IDisposable
    {
        public readonly QualificationWorld World = QualificationWorld.Create("c1500");
        public readonly RcQualificationStatusOptions Options;

        public Fixture(int owner = Owner)
        {
            var root = World.Fixture.Root;
            Options = new RcQualificationStatusOptions
            {
                CandidateId = "rc-fixture", RunId = "fixture-run", StateRoot = Path.Combine(root, "state"),
                CoordinationRoot = Path.Combine(root, "coord"), ReleaseRoot = Path.Combine(root, "release"),
            };
            Directory.CreateDirectory(Options.StateRoot);
            Directory.CreateDirectory(Options.CoordinationRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(Journal)!);
            QualificationWorld.WriteJson(State, new JsonObject
            {
                ["runId"] = "fixture-run", ["candidateId"] = "rc-fixture", ["profile"] = "rc", ["ref"] = "rc/fixture", ["sha"] = ShaA, ["expectedSha"] = ShaA,
                ["phase"] = "Tests", ["startedAt"] = "2026-01-01T00:00:00Z", ["lastActivityAt"] = "2026-01-01T00:01:00Z", ["exitCode"] = null,
                ["coverageComplete"] = false, ["testsPassed"] = false, ["reportDelivered"] = false,
            }.ToJsonString());
            var lockRecord = new JsonObject
            {
                ["pid"] = owner, ["processStartedAt"] = QualificationProcessWorld.StartedText(owner), ["runId"] = "fixture-run", ["lane"] = "rc",
                ["continuationId"] = "fixture-continuation",
            }.ToJsonString();
            QualificationWorld.WriteJson(RunLock, lockRecord);
            QualificationWorld.WriteJson(Native, lockRecord);
            QualificationWorld.WriteJson(Journal, new JsonObject { ["candidateId"] = "rc-fixture", ["sha"] = ShaA, ["ref"] = "rc/fixture" }.ToJsonString());
            World.Processes.Set(owner, "alive");
        }

        public string State => Path.Combine(Options.StateRoot, "last-run.json");
        public string RunLock => Path.Combine(Options.StateRoot, "run.lock");
        public string Native => Path.Combine(Options.CoordinationRoot, "native-run.lock");
        public string Journal => Path.Combine(Options.ReleaseRoot, "candidates", "rc-fixture", "candidate.json");

        public JsonObject Status(RcQualificationStatusOptions? options = null, Action<string>? afterRead = null) =>
            RcQualificationStatus.Observe(options ?? Options, World.Host(), World.Files, afterRead);

        public void Set(string path, string name, JsonNode? value)
        {
            var record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            record[name] = value;
            QualificationWorld.WriteJson(path, record.ToJsonString());
        }

        public void Dispose() => World.Dispose();
    }

    private static bool Complete(JsonObject status) => status["observationComplete"]!.GetValue<bool>();

    private static string? Text(JsonNode? node, params string[] path)
    {
        foreach (var name in path) node = node?[name];
        return node?.GetValueKind() == System.Text.Json.JsonValueKind.String ? node.GetValue<string>() : node?.ToJsonString();
    }

    [Test]
    public void C1500_StatusIdentityAndObservation()
    {
        using var fx = new Fixture();
        var rows = new Nightly.NightlyCaseRows();
        var r = fx.Status();
        rows.Check(Complete(r) && Text(r, "state", "phase") == "Tests" && r["state"]!["exitCode"] == null
            && r["state"]!["testsPassed"]!.GetValueKind() == System.Text.Json.JsonValueKind.False && Text(r, "locks", "native", "ownerObservation", "status") == "alive",
            "C1500 matching-live", r.ToJsonString());
        foreach (var (field, value, name) in new (string, JsonNode, string)[]
                 {
                     ("runId", "foreign", "foreign-state-run"), ("candidateId", "foreign", "foreign-state-candidate"), ("profile", "nightly", "foreign-profile"),
                     ("sha", ShaB, "foreign-sha"), ("testsPassed", "false", "string-boolean"),
                 })
        {
            var old = JsonNode.Parse(File.ReadAllText(fx.State))![field]?.DeepClone();
            fx.Set(fx.State, field, value);
            r = fx.Status();
            rows.Check(!Complete(r), "C1500 " + name, r.ToJsonString());
            fx.Set(fx.State, field, old);
        }
        fx.Set(fx.State, "sha", "");
        rows.Check(Complete(fx.Status()), "C1500 early-expected-sha");
        fx.Set(fx.State, "sha", ShaA);
        fx.Set(fx.State, "exitCode", 1);
        fx.Set(fx.State, "phase", "Failed");
        r = fx.Status();
        rows.Check(Complete(r) && r["state"]!["exitCode"]!.GetValue<long>() == 1 && !r["state"]!["testsPassed"]!.GetValue<bool>(), "C1500 completed-failure", r.ToJsonString());
        foreach (var status in new[] { "absent", "reused", "unknown" })
        {
            fx.World.Processes.Set(Owner, status);
            r = fx.Status();
            rows.Check(Text(r, "locks", "run", "ownerObservation", "status") == status && (status != "unknown" || !Complete(r)), "C1500 " + status + "-owner", r.ToJsonString());
        }
        fx.World.Processes.Set(Owner, "alive");
        fx.Set(fx.Native, "runId", "other");
        r = fx.Status();
        rows.Check(Text(r, "locks", "native", "relationship") == "other-run" && Text(r, "locks", "native", "record", "runId") == "other", "C1500 foreign-native-lock", r.ToJsonString());
        fx.Set(fx.Native, "runId", "fixture-run");
        fx.Set(fx.Native, "processStartedAt", null);
        rows.Check(Text(fx.Status(), "locks", "native", "ownerObservation", "status") == "unknown", "C1500 missing-owner-start");
        File.Delete(fx.Journal);
        r = fx.Status();
        rows.Check(Text(r, "inputs", "journal", "status") == "missing" && Text(r, "state", "runId") == "fixture-run", "C1500 optional-journal-missing", r.ToJsonString());
        foreach (var field in new[] { "candidateId", "ref", "sha" })
        {
            QualificationWorld.WriteJson(fx.Journal, new JsonObject { ["candidateId"] = "rc-fixture", ["ref"] = "rc/fixture", ["sha"] = ShaA }.ToJsonString());
            fx.Set(fx.Journal, field, "foreign");
            r = fx.Status();
            rows.Check(r["identityMismatches"]!.AsArray().Any(item => item!.GetValue<string>() == "journal-" + field), "C1500 journal-" + field, r.ToJsonString());
        }
        File.Delete(fx.State);
        r = fx.Status();
        rows.Check(!Complete(r) && Text(r, "inputs", "state", "status") == "missing", "C1500 missing-state", r.ToJsonString());
        File.WriteAllText(fx.State, "{broken");
        rows.Check(Text(fx.Status(), "inputs", "state", "status") == "malformed", "C1500 malformed-state");
        File.WriteAllText(fx.State, new string('x', RcQualificationRecords.MaxBytes + 1));
        rows.Check(Text(fx.Status(), "inputs", "state", "reason") == "too-large", "C1500 bounded-read");
        File.WriteAllText(fx.State, "{}");
        rows.Check(RcQualificationRecords.Read(fx.World.Files, fx.State, path => File.WriteAllText(path, "{\"changed\":true}")).Status == "changed", "C1500 changed-record");
        using (new FileStream(fx.State, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            rows.Check(RcQualificationRecords.Read(fx.World.Files, fx.State).Status == "unreadable", "C1500 unreadable-record");
        fx.World.Processes.Throw = "private error detail";
        r = fx.Status();
        rows.Check(Text(r, "locks", "run", "ownerObservation", "status") == "unknown" && !r.ToJsonString().Contains("private error detail", StringComparison.Ordinal),
            "C1500 probe-error-redacted", r.ToJsonString());
        fx.World.Processes.Throw = null;
        r = fx.Status(new RcQualificationStatusOptions
        {
            CandidateId = "../outside", RunId = fx.Options.RunId, StateRoot = fx.Options.StateRoot, CoordinationRoot = fx.Options.CoordinationRoot, ReleaseRoot = fx.Options.ReleaseRoot,
        });
        rows.Check(Text(r, "reason") == "invalid-input", "C1500 invalid-selection", r.ToJsonString());
        File.Delete(fx.State);
        Directory.CreateDirectory(fx.State);
        rows.Check(RcQualificationRecords.Read(fx.World.Files, fx.State).Status == "unsupported-path", "C1500 non-file-refused");
        rows.ShouldAllPass(25, "C1500 foreign-state-run", "C1500 changed-record", "C1500 unknown-owner");
    }

    [Test]
    public void C1500_StatusDoesNotMutate()
    {
        using var fx = new Fixture(owner: int.MaxValue);
        fx.World.Processes.Set(int.MaxValue, "absent");
        var root = fx.World.Fixture.Root;
        File.WriteAllText(Path.Combine(root, "master-sentinel"), "retain me");
        string Inventory() => string.Join("\n", Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => Directory.Exists(path) ? path + "|directory"
                : path + "|" + File.GetLastWriteTimeUtc(path).Ticks + "|" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));
        var before = Inventory();
        var exit = RcQualificationStatus.Run(fx.Options, fx.World.Host(), fx.World.Files);
        var r = fx.World.LastJson();
        var rows = new Nightly.NightlyCaseRows();
        rows.Check(exit == 0 && Text(r, "state", "runId") == "fixture-run" && Text(r, "locks", "run", "ownerObservation", "status") == "absent", "C1500 real-entrypoint", fx.World.Out);
        rows.Check(Inventory() == before, "C1500 input-bytes-and-times-preserved");
        rows.Check(fx.World.Launcher.Launches.Count == 0 && fx.World.Seams.Starts.Count == 0 && fx.World.Seams.Stops.Count == 0 && fx.World.Git.Calls.Count == 0,
            "C1500 no-action-commands");
        rows.ShouldAllPass(3, "C1500 input-bytes-and-times-preserved", "C1500 no-action-commands");
    }

    // C1500_WindowsSharedReadAndPs51 without the PowerShell hosts: the owner's handle (FileAccess.Write, FileShare.Read) stays
    // open while the status reads the native lock, and the bytes are unchanged; the production sources are ASCII.
    [Test]
    public void C1500_WindowsSharedRead()
    {
        using var fx = new Fixture();
        var rows = new Nightly.NightlyCaseRows();
        var ascii = new[] { "RcQualificationRecords.cs", "RcQualificationStatus.cs" }
            .Select(name => Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", name))
            .Append(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Scripts", "RcQualificationStatusCommand.cs"))
            .All(path => File.ReadAllBytes(path).All(b => b <= 127));
        rows.Check(ascii, "C1500 production-ascii");
        var before = File.ReadAllBytes(fx.Native);
        using (new FileStream(fx.Native, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            var exit = RcQualificationStatus.Run(fx.Options, fx.World.Host(), fx.World.Files);
            var r = fx.World.LastJson();
            rows.Check(exit == 0 && Complete(r) && Text(r, "inputs", "nativeLock", "status") == "ok" && Text(r, "locks", "native", "ownerObservation", "status") == "alive"
                && Text(r, "state", "sha") == ShaA, "C1500 shared-read", fx.World.Out);
        }
        rows.Check(Convert.ToBase64String(File.ReadAllBytes(fx.Native)) == Convert.ToBase64String(before), "C1500 native-bytes-preserved");
        rows.ShouldAllPass(3, "C1500 shared-read", "C1500 native-bytes-preserved");
    }

    [Test]
    public void Invalid_selection_prints_the_status_and_exits_2()
    {
        using var fx = new Fixture();
        var exit = RcQualificationStatus.Run(RcQualificationStatusOptions.Parse(["--candidate-id", "rc-fixture", "--state-root", fx.Options.StateRoot]), fx.World.Host(), fx.World.Files);
        exit.ShouldBe(2);
        var r = fx.World.LastJson();
        Text(r, "reason").ShouldBe("invalid-input");
        r["inputs"]!.AsObject().Count.ShouldBe(0);
        Should.Throw<NightlyUsageException>(() => RcQualificationStatusOptions.Parse(["--passthru"]));
    }
}
