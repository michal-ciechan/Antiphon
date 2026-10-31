using Antiphon.E2E.Fixtures;
using Antiphon.Server.Domain.Enums;
using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

[NotInParallel("C467LandDelivery")]
public class LandDeliveryFixtureProbeTests
{
    [Test]
    public async Task Stalled_receipt_names_identity_last_progress_and_safe_log_tail()
    {
        await using var f = new LandDeliveryFixture();
        Directory.CreateDirectory(f.Root);
        var request = Guid.NewGuid();
        var notification = Guid.NewGuid();
        var git = Path.Combine(f.Root, "protocol-git-last.json");
        var logDirectory = Path.Combine(f.Root, "server-logs");
        Directory.CreateDirectory(logDirectory);
        var log = Path.Combine(logDirectory, "antiphon-test.log");
        const string privatePayload = "fixture-private-payload-do-not-echo";
        await File.WriteAllTextAsync(git, JsonSerializer.Serialize(new {
            arguments = new[] { "push", privatePayload }, ExitCode = 0,
            at = DateTime.UtcNow, pid = Environment.ProcessId }));
        await File.WriteAllTextAsync(log,
            "2026-10-31 00:00:00.000 +00:00 [INF] Landing verifier BUILD SLOT waiting " + privatePayload + "\n");
        // Held beyond the quiet window; no delayed task or child survives the witness.
        var producer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var original = await Should.ThrowAsync<TimeoutException>(() => f.UntilProtocolAsync(
                () => Task.FromResult(producer.Task.IsCompleted), "complete native Land receipt", quietSeconds: 1));
            original.Message.ShouldContain("quiet 1s");
            var error = await f.ReceiptTimeoutAsync(original, LandNotificationKind.Outcome, request, notification,
                _ => Task.FromResult("operation=Prepared; notification=missing; producer=held"));
            error.InnerException.ShouldBeSameAs(original);
            error.Message.ShouldContain("kind=Outcome");
            error.Message.ShouldContain("requestId=" + request);
            error.Message.ShouldContain("notificationId=" + notification);
            error.Message.ShouldContain("operation=Prepared; notification=missing; producer=held");
            error.Message.ShouldContain("lastProtocolGit=" + git);
            error.Message.ShouldContain("gitCommand=push exit=0");
            error.Message.ShouldContain("logTail=" + log);
            error.Message.ShouldContain("build-slot-waiting");
            error.Message.ShouldNotContain(privatePayload);
            File.Exists(Path.Combine(f.Root, "receipt-timeout-" + notification.ToString("N") + ".txt")).ShouldBeTrue();
            producer.Task.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            producer.TrySetResult(true);
            await producer.Task;
            Directory.Delete(f.Root, recursive: true);
        }
    }

    [Test]
    public async Task ProtocolProgress_counts_committed_protocol_git_records_only()
    {
        await using var f = new LandDeliveryFixture();
        Directory.CreateDirectory(f.Root);
        try
        {
            f.ProtocolProgress().ShouldBe(0);
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-git-{Guid.NewGuid():N}.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-git-{Guid.NewGuid():N}.json.tmp"), "{}");
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"attention-{Guid.NewGuid():N}.json"), "{}");
            f.ProtocolProgress().ShouldBe(1, "a partial .tmp record and a foreign evidence file are not progress");
            await File.WriteAllTextAsync(Path.Combine(f.Root, $"protocol-git-{Guid.NewGuid():N}.json"), "{}");
            f.ProtocolProgress().ShouldBe(2);
        }
        finally { Directory.Delete(f.Root, recursive: true); }
    }
}
