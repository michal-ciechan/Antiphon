using Antiphon.E2E.Fixtures;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

[Category("OptIn")]
public class OutputDistillationCanaryGuardTests
{
    [Test]
    [Arguments("opt-in")] [Arguments("expired")] [Arguments("future")] [Arguments("stale")]
    [Arguments("refused")] [Arguments("model")] [Arguments("db")] [Arguments("runner")]
    [Arguments("production-runner")] [Arguments("manifest")] [Arguments("broker")]
    [Arguments("absent")]
    public async Task Configuration_refuses_unowned_resources_before_start(string scenario)
    {
        var now = DateTimeOffset.UtcNow;
        var approval = new DistillerCanaryApproval("human-card-revision", now.AddMinutes(-1), now.AddMinutes(20),
            "Review", "ClaudeCode", "haiku", now, "allowed");
        var manifest = Path.Combine(Path.GetTempPath(), "owned-run", "logs");
        if (scenario == "absent")
        {
            await Should.ThrowAsync<InvalidOperationException>(() => DistillerCanaryApproval.ReadAsync(null, now, CancellationToken.None));
            return;
        }
        Should.Throw<InvalidOperationException>(() => {
            DistillerCanaryGuard.ValidateOptIns("1", scenario == "opt-in" ? null : "1");
            approval = scenario switch {
                "expired" => approval with { ExpiresUtc = now }, "future" => approval with { ApprovedUtc = now.AddSeconds(1) },
                "stale" => approval with { AvailabilityCheckedUtc = now.AddMinutes(-6) },
                "refused" => approval with { AvailabilityVerdict = "held" }, _ => approval };
            approval.Validate(now); approval.ValidateModel("ClaudeCode", scenario == "model" ? "sonnet" : "haiku");
            DistillerCanaryGuard.ValidateResources(scenario == "db" ? "foreign" : "owned", "owned",
                scenario == "production-runner" ? "http://127.0.0.1:17204" : scenario == "runner" ? "http://127.0.0.1:34568" : "http://127.0.0.1:34567",
                "http://127.0.0.1:34567", scenario == "manifest" ? manifest + "-foreign" : manifest, manifest,
                scenario == "broker" ? "live:9092" : "127.0.0.1:1");
        });
    }

    [Test]
    [Arguments("valid")] [Arguments("source")] [Arguments("parent")] [Arguments("note")] [Arguments("digest")]
    [Arguments("path")] [Arguments("screen")] [Arguments("assistant")] [Arguments("clipped")] [Arguments("raw-middle")]
    [Arguments("api")] [Arguments("rejected")] [Arguments("late")] [Arguments("shadow")] [Arguments("model")]
    [Arguments("read")] [Arguments("tool")] [Arguments("teardown")] [Arguments("restart")]
    [Arguments("file-identity")] [Arguments("file-name")]
    public void Evidence_rejects_missing_or_wrong_delivery_proof(string scenario)
    {
        var id = Guid.NewGuid(); var parent = Guid.NewGuid(); var note = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var raw = new string('x', 5000);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
        var path = Path.Combine(Path.GetTempPath(), id.ToString("D"), hash + ".md");
        var otherPath = Path.Combine(Path.GetTempPath(), scenario == "file-identity" ? Guid.NewGuid().ToString("D") : id.ToString("D"),
            scenario == "file-name" ? "wrong.md" : hash + ".md");
        var header = "[task " + DelegationReportFormatter.Short(id) + " done] next=review";
        var evidence = new DistillerCanaryEvidence(id, parent, note, id, note, parent, raw, DelegationNoteDigest.Compute(raw),
            path, raw, "accepted summary", header, "Apply", "Applied", now.AddSeconds(45), now,
            "UserPrompt", 5, now, header + "\n\naccepted summary\n\nFull report: " + path + "\nmarker",
            "Delivered", "ClaudeCode", "haiku", "haiku",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))),
            raw.Length, true, true, 1, 1, true);
        evidence.Validate("distinctive middle", ["marker"]);
        evidence = scenario switch {
            "source" => evidence with { LedgerSourceId = Guid.NewGuid() }, "parent" => evidence with { TranscriptParentId = Guid.NewGuid() },
            "note" => evidence with { LedgerNoteId = Guid.NewGuid() }, "digest" => evidence with { RawDigest = "wrong" },
            "path" => evidence with { FileRaw = "different" }, "screen" => evidence with { TranscriptKind = "Screen" },
            "file-identity" or "file-name" => evidence with { FilePath = otherPath, Prompt = evidence.Prompt.Replace(path, otherPath) },
            "assistant" => evidence with { TranscriptKind = "AssistantText" }, "clipped" => evidence with { Prompt = header },
            "raw-middle" => evidence with { Prompt = evidence.Prompt + "distinctive middle" },
            "api" => evidence with { Prompt = header + "\naccepted summary\nGET /api/agent-tasks/" + id + "\nmarker" },
            "rejected" => evidence with { Outcome = "RejectedOverCompressed" }, "late" => evidence with { Decision = evidence.Deadline },
            "shadow" => evidence with { Mode = "Shadow" }, "model" => evidence with { ModelAlias = "opus" },
            "read" => evidence with { ParentReadHash = "wrong" }, "tool" => evidence with { ParentToolEvidence = false },
            "teardown" => evidence with { Teardown = false }, "restart" => evidence with { CompletionsAfter = 2 }, _ => evidence };
        if (scenario != "valid") Should.Throw<InvalidOperationException>(() => evidence.Validate("distinctive middle", ["marker"]));
    }
}
