using System.Diagnostics;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ChannelOutboundCrashTests
{
    public static IEnumerable<Func<(string Path, string Cut)>> PublishCuts()
    {
        foreach (var path in new[] { "main", "trailing", "machine" })
        foreach (var cut in new[] { "attempt-committed", "producer-entered",
                     "producer-accepted", "outcome-committed" })
        {
            var p = path;
            var c = cut;
            yield return () => (p, c);
        }
    }

    public static IEnumerable<Func<(string Path, string Cut)>> MaterializationCuts()
    {
        foreach (var path in new[] { "main", "trailing", "machine" })
        foreach (var cut in new[] { "before-publication-commit", "publication-committed" })
        {
            var p = path;
            var c = cut;
            yield return () => (p, c);
        }
    }

    [Test]
    [MethodDataSource(nameof(PublishCuts))]
    public async Task C519_Killed_publish_recovers_from_committed_evidence(string path, string cut) =>
        await CrashAndRecoverAsync(path, cut, newerTurn: false);

    [Test]
    [MethodDataSource(nameof(MaterializationCuts))]
    public async Task C519_Killed_materialization_recovers_without_an_event(string path, string cut) =>
        await CrashAndRecoverAsync(path, cut, newerTurn: true);

    private static async Task CrashAndRecoverAsync(string path, string cut, bool newerTurn)
    {
        await using var fixture = await ChannelOutboundFixture.CreateAsync();
        var (sourceId, answer, baseline) = await fixture.CompleteSourceTurnAsync(path);
        var root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,
            ".antiphon", "acceptance", "card-0519", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "owner"), "card-0519");
        Process? first = null;
        Process? second = null;
        try
        {
            var request = new ChannelOutboundCrashRequest(root, fixture.ConnectionString,
                fixture.SessionId, fixture.AgentId, sourceId, path, cut, Recover: false);
            first = Start(request);
            var held = await WaitCutAsync(root, first);
            held.Pid.ShouldBe(first.Id);
            held.Mvid.ShouldBe(typeof(ChannelOutboundCrashWorker).Module.ModuleVersionId);
            held.Cut.ShouldBe(cut);
            await using (var independent = fixture.CreateContext())
            {
                (await independent.SessionQueuedMessages.AnyAsync(m => m.Id == sourceId
                    && m.Status == QueuedMessageStatus.Sent)).ShouldBeTrue();
                (await independent.TranscriptEntries.AnyAsync(t => t.AgentSessionId == fixture.SessionId
                    && t.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.AssistantText
                    && t.Text != null && t.Text.Contains(answer))).ShouldBeTrue();
            }
            var before = await fixture.ReadPublicationsAsync(sourceId, path);
            if (cut == "before-publication-commit") before.ShouldBeEmpty();
            else
            {
                var row = before.ShouldHaveSingleItem();
                row.Id.ShouldBe(held.PublicationId);
                row.State.ShouldBe(cut switch
                {
                    "publication-committed" => "Pending",
                    "outcome-committed" => "Published",
                    _ => "Publishing",
                });
                row.EnvelopeJson.ShouldContain(answer);
                row.Sources.Select(s => s.QueueMessageId).ShouldContain(sourceId);
            }
            var prior = Receipts(root);
            prior.Count.ShouldBe(cut is "producer-accepted" or "outcome-committed" ? 1 : 0);
            first.Kill(entireProcessTree: true);
            await first.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await first.StandardOutput.ReadToEndAsync();
            await first.StandardError.ReadToEndAsync();
            if (newerTurn)
                await fixture.Harness.InsertTurnAsync("Newer unrelated prompt after killed source",
                    "Wrong newer answer must never replace the original.");

            second = Start(request with { Cut = "", Recover = true });
            await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
            var stdout = await second.StandardOutput.ReadToEndAsync();
            var stderr = await second.StandardError.ReadToEndAsync();
            second.ExitCode.ShouldBe(0, stdout + stderr);

            var final = (await fixture.ReadPublicationsAsync(sourceId, path)).ShouldHaveSingleItem();
            final.State.ShouldBe("Published");
            final.PublishedAt.ShouldNotBeNull();
            final.Sources.Select(s => s.QueueMessageId).ShouldContain(sourceId);
            var receipts = Receipts(root);
            receipts.Count.ShouldBe(cut == "producer-accepted" ? 2 : 1);
            var frozen = JsonSerializer.Deserialize<ChannelReply>(final.EnvelopeJson)!;
            foreach (var receipt in receipts)
            {
                JsonSerializer.Serialize(receipt, MessagingJson.Options)
                    .ShouldBe(JsonSerializer.Serialize(frozen, MessagingJson.Options));
                receipt.Text.ShouldContain(answer);
                receipt.Text.ShouldNotContain("Wrong newer answer");
            }
            await using var verify = fixture.CreateContext();
            var incidents = await verify.AgentIncidents.AsNoTracking()
                .Where(i => i.SessionId == fixture.SessionId
                    && i.Kind == AgentIncidentKind.ChannelReplyLost).ToListAsync();
            if (cut is "attempt-committed" or "producer-entered" or "producer-accepted")
            {
                incidents.Count.ShouldBe(1);
                incidents[0].Severity.ShouldBe(AlertSeverity.Critical);
                incidents[0].Message.ShouldContain("may have happened");
            }
            else incidents.ShouldBeEmpty();
            fixture.Producer.Accepted.Count.ShouldBe(baseline);
        }
        finally
        {
            await DrainAsync(first);
            await DrainAsync(second);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static Process Start(ChannelOutboundCrashRequest request)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(ChannelOutboundCrashWorker).Assembly.Location);
        start.Environment[ChannelOutboundCrashWorker.Marker] = JsonSerializer.Serialize(request);
        return Process.Start(start) ?? throw new InvalidOperationException("Outbound crash child did not start.");
    }

    private static async Task<ChannelOutboundCutReceipt> WaitCutAsync(string root, Process process)
    {
        var file = Path.Combine(root, "cut.json");
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(file))
                return JsonSerializer.Deserialize<ChannelOutboundCutReceipt>(
                    await File.ReadAllTextAsync(file))!;
            if (process.HasExited)
                throw new InvalidOperationException("Crash child exited before cut: "
                    + await process.StandardError.ReadToEndAsync());
            await Task.Delay(25);
        }
        throw new TimeoutException("Crash child did not reach its committed cut.");
    }

    private static List<ChannelReply> Receipts(string root)
    {
        var file = Path.Combine(root, "receipts.ndjson");
        return File.Exists(file)
            ? File.ReadAllLines(file).Select(line =>
                JsonSerializer.Deserialize<ChannelReply>(line, MessagingJson.Options)!).ToList()
            : [];
    }

    private static async Task DrainAsync(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await process.StandardOutput.ReadToEndAsync();
            await process.StandardError.ReadToEndAsync();
        }
        finally { process.Dispose(); }
    }
}
