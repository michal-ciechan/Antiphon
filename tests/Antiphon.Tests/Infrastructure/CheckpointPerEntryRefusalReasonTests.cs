using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-1520: per-entry cleanup must keep the preflight reason, or a distinct
// content/creation-record mismatch, with the deletion stage and path. The
// refusal is what the checkpoint report persists. Deletion stays fail-closed.
[Category("Unit")]
public sealed class CheckpointPerEntryRefusalReasonTests
{
    [Test]
    public void Git_preflight_failure_keeps_the_reason_stage_and_path_and_does_not_unlink()
    {
        var file = new HostCleanupCheckpointFixture();
        file.Seal();
        file.Io.GitResult = request => request.Arguments.Any(argument => argument == file.Payload)
            ? new(0, "", "", true)
            : new(0, "", "", false);

        var fileReason = file.Ledger.Remove(file.Root);
        fileReason.ShouldBe(Refusal(file.Payload, "file", "preflight=git-timeout"),
            "CARD-1520.Git_preflight_failure.file");
        file.Io.Count("unlink", file.Payload).ShouldBe(0, "CARD-1520.Git_preflight_failure.file:unlink");
        file.Io.Count("unlink").ShouldBe(0, "CARD-1520.Git_preflight_failure.file:no-unlink");
        file.Io.Text(file.Payload).ShouldBe("original", "CARD-1520.Git_preflight_failure.file:bytes");
        AssertPersisted(fileReason!, "CARD-1520.Git_preflight_failure.file");

        var directory = new HostCleanupCheckpointFixture();
        var child = Path.Combine(directory.Root, "child");
        directory.Io.CreateDirectory(child);
        directory.Seal();
        directory.Io.GitResult = request => request.Arguments.Any(argument => argument == child)
            ? new(0, "", "", true)
            : new(0, "", "", false);

        var directoryReason = directory.Ledger.Remove(directory.Root);
        directoryReason.ShouldBe(Refusal(child, "directory", "preflight=git-timeout"),
            "CARD-1520.Git_preflight_failure.directory");
        directoryReason.ShouldNotBe(fileReason, "CARD-1520.Git_preflight_failure.stage");
        directory.Io.Count("rmdir", child).ShouldBe(0, "CARD-1520.Git_preflight_failure.directory:rmdir");
        directory.Io.DirectoryExists(child).ShouldBeTrue("CARD-1520.Git_preflight_failure.directory:kept");
        AssertPersisted(directoryReason!, "CARD-1520.Git_preflight_failure.directory");
    }

    [Test]
    public void Changed_content_refusal_names_the_mismatch_and_does_not_unlink()
    {
        var f = new HostCleanupCheckpointFixture();
        f.Seal();
        var reads = 0;
        f.Io.Before = (op, path) =>
        {
            if (op == "open-read" && path == f.Payload && ++reads == 2)
                f.Io.Put(path, "changed!");
        };

        var reason = f.Ledger.Remove(f.Root);
        reason.ShouldBe(Refusal(f.Payload, "file", "content-or-creation-record-mismatch"),
            "CARD-1520.Changed_content.file");
        reason.ShouldNotContain("preflight=", Case.Sensitive, "CARD-1520.Changed_content.distinct");
        reason.ShouldNotContain("git-timeout", Case.Sensitive, "CARD-1520.Changed_content.distinct-git");
        f.Io.Count("unlink", f.Payload).ShouldBe(0, "CARD-1520.Changed_content.file:unlink");
        f.Io.Text(f.Payload).ShouldBe("changed!", "CARD-1520.Changed_content.file:bytes");
        AssertPersisted(reason!, "CARD-1520.Changed_content.file");
    }

    private static string Refusal(string path, string stage, string detail) =>
        "left in place: " + path + "; stage=" + stage + "; " + detail;

    private static void AssertPersisted(string reason, string label)
    {
        var model = new ReportModel
        {
            ExitCode = 0,
            Verdict = "GREEN",
            CleanedOutputs = false,
            CleanupRefusals = [reason],
            Reason = "refused: " + reason,
        };
        ReportValidator.Validate(model, new string('a', 40)).ShouldBe("cleanup_refused", label + ":validator");
        ReportWriter.Markdown(model).ShouldContain("refused: " + reason, Case.Sensitive, label + ":markdown");
        ReportWriter.JsonText(model).ShouldContain(reason, Case.Sensitive, label + ":json");
    }
}
