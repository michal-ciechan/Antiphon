using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

internal static class SessionQueueTranscriptPump
{
    public static async Task RunAsync(string path, Guid sessionId, CancellationToken ct,
        Func<Task>? beforeRead = null, Func<Task>? beforeSave = null,
        Task? hold = null)
    {
        var consumed = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (beforeRead is not null) await beforeRead();
                if (hold is not null) await hold;
                if (File.Exists(path))
                {
                    var text = await File.ReadAllTextAsync(path, CancellationToken.None);
                    var lines = text.Split('\n');
                    var complete = lines.Length - 1;
                    for (var i = consumed; i < complete; i++)
                    {
                        var parts = Antiphon.SessionRunner.TranscriptNormalizer.Normalize(lines[i]);
                        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
                        var sequence = await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId)
                            .Select(t => (long?)t.Sequence).MaxAsync(CancellationToken.None) ?? 0;
                        // A normalizer line can contain several parts with the same UUID.
                        // Commit them together and consult durable UUIDs on every restart.
                        var uuids = parts.Select(p => p.Uuid).Where(u => !string.IsNullOrEmpty(u)).ToArray();
                        var persisted = await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId
                                && uuids.Contains(t.Uuid)).Select(t => t.Uuid).ToListAsync(CancellationToken.None);
                        foreach (var part in parts)
                        {
                            if (part.Uuid is { Length: > 0 } && persisted.Contains(part.Uuid))
                                continue;
                            db.TranscriptEntries.Add(new TranscriptEntry
                            {
                                Id = Guid.NewGuid(),
                                AgentSessionId = sessionId,
                                Sequence = ++sequence,
                                Kind = part.Kind,
                                Uuid = part.Uuid,
                                Role = part.Role,
                                Text = part.Text,
                                StopReason = part.StopReason,
                                Timestamp = part.Timestamp?.UtcDateTime,
                                CreatedAt = DateTime.UtcNow,
                            });
                        }
                        if (beforeSave is not null) await beforeSave();
                        await db.SaveChangesAsync(CancellationToken.None);
                        consumed = i + 1;
                    }
                }
            }
            catch (IOException) { }
            catch (DbUpdateException) { }
            try { await Task.Delay(100, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public static async Task<List<string>> DestinationUserPromptsAsync(Guid sessionId, long afterSequence)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        return await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId
                && t.Sequence > afterSequence
                && t.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt)
            .OrderBy(t => t.Sequence)
            .Select(t => t.Text ?? "")
            .ToListAsync();
    }

    public static async Task<long> MaxSequenceAsync(Guid sessionId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        return await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId)
            .Select(t => (long?)t.Sequence).MaxAsync() ?? 0;
    }

    public static List<string> FileUserPrompts(string transcriptPath)
    {
        if (!File.Exists(transcriptPath)) return [];
        var prompts = new List<string>();
        foreach (var line in File.ReadAllLines(transcriptPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            foreach (var part in Antiphon.SessionRunner.TranscriptNormalizer.Normalize(line))
                if (part.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt)
                    prompts.Add(part.Text ?? "");
        }
        return prompts;
    }
}
