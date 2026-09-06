using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;
using TUnit.Core.Exceptions;
using Shouldly;

namespace Antiphon.Tests.Application;

[Explicit]
[Category("Integration")]
[NotInParallel("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GrokRulesCompactionAcceptanceTests
{
    [Test, Timeout(7_260_000)]
    public Task Live_inline_rules_baseline_records_two_genuine_auto_compactions(CancellationToken ct) => RunAsync(false, ct);

    [Test, Timeout(7_260_000)]
    public Task Live_file_rules_remain_effective_across_two_auto_compactions_and_resume(CancellationToken ct) => RunAsync(true, ct);

    private static async Task RunAsync(bool fileArm, CancellationToken outer)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS") != "1"
            || Environment.GetEnvironmentVariable("ANTIPHON_GROK_RULES_LIVE_TESTS") != "1")
            throw new SkipTestException("Explicit live Grok and headed opt-ins required");
        // Predeclared ceiling applies to the complete arm, including setup and native resume.
        using var ceiling = CancellationTokenSource.CreateLinkedTokenSource(outer);
        ceiling.CancelAfter(TimeSpan.FromMinutes(120)); var ct = ceiling.Token;
        var began = DateTimeOffset.UtcNow; var elapsed = Stopwatch.StartNew();
        var root = Path.Combine(DelegateScriptRunner.RepoRoot, "tests/Antiphon.Tests/TestOutput/Logs", nameof(GrokRulesCompactionAcceptanceTests), (fileArm ? "file-" : "inline-") + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var home = Path.Combine(root, "home"); Directory.CreateDirectory(home);
        // The installed supported setting was calibrated against genuine native tool exchanges.
        // Live arms both use 20%: their real usage includes the system and accumulated history,
        // unlike the calibration fixture's unchanged 10-input/5-output usage envelope.
        await File.WriteAllTextAsync(Path.Combine(home, "config.toml"), "[session]\nauto_compact_threshold_percent = 20\n[cli]\nuse_leader = false\n[compat.claude]\nhooks = false\nmcps = false\nrules = false\nskills = false\n[compat.codex]\nhooks = false\nskills = false\n", ct);
        var authPath = Environment.GetEnvironmentVariable("ANTIPHON_GROK_TEST_AUTH_PATH")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok/auth.json");
        File.Exists(authPath).ShouldBeTrue("Existing native authentication is required; no synthesized live credential");
        var env = new Dictionary<string,string>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = (string)entry.Key;
            if (key.StartsWith("GROK_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("XAI_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("X_LLM_", StringComparison.OrdinalIgnoreCase)) env[key] = "";
        }
        env["GROK_HOME"] = home; env["GROK_AUTH_PATH"] = authPath; env["GROK_DISABLE_AUTO_UPDATER"] = "1";
        var keys = Enumerable.Range(0, 6).Select(_ => Enumerable.Range(0, 3).Select(_ => "K" + Guid.NewGuid().ToString("N")).ToArray()).ToArray();
        var expected = Enumerable.Range(0, 6).Select(_ => Enumerable.Range(0, 3).Select(_ => "V" + Guid.NewGuid().ToString("N")).ToArray()).ToArray();
        var audit = "AUDIT-" + Guid.NewGuid().ToString("N");
        string Rule(int checkpoint, int slot) => $"Challenge {keys[checkpoint][slot]} requires the exact answer {expected[checkpoint][slot]}.";
        string FileRules() => string.Join("\n", Enumerable.Range(0, 6).Select(i => Rule(i, 0))) + "\n"
            + string.Join("\n", Enumerable.Range(1, 1200).Select(i => (i == 600 ? string.Join("\n", Enumerable.Range(0, 6).Select(j => Rule(j, 1))) + "\n" : "") + $"RETENTION-LINE-{i:D4} neutral standing material.")) + "\n"
            + string.Join("\n", Enumerable.Range(0, 6).Select(i => Rule(i, 2)));
        var inline = string.Join(" ", Enumerable.Range(0, 6).SelectMany(i => Enumerable.Range(0, 3).Select(s => Rule(i, s))));
        inline.Length.ShouldBeLessThanOrEqualTo(4096); inline.ShouldNotContain('\n');
        await File.WriteAllTextAsync(Path.Combine(root, "expected.json"), JsonSerializer.Serialize(new { keys, expected, audit }), ct);
        using var repo = await RealCliStubBServerHarness.GitRepo.CreateAsync();
        // Each native read stays below the measured 25,000-token file-tool limit. The model
        // must perform its own continuation reads; no output or token count is injected.
        var workloads = new List<string[]>();
        for (var turn = 0; turn < 2; turn++)
        {
            var paths = new List<string>();
            for (var file = 0; file < 4; file++)
            {
                var path = Path.Combine(root, $"work-{turn}-{file}.txt");
                var content = string.Join("\n", Enumerable.Range(1, 1800).Select(i => $"DATA-{turn}-{file}-{i:D4} " + new string((char)('A' + i % 26), 90)));
                await File.WriteAllTextAsync(path, content, ct); paths.Add(path);
            }
            workloads.Add(paths.ToArray());
        }
        await using var runner = new DirectSessionRunnerClient(Path.Combine(root, "runner"), ptyBackend: "modern");
        await using var factory = new GrokRulesLiveMappedDispatchTests.LiveFactory(runner, repo, env, RealCliStubGate.ResolveGrokOrThrow(), fileArm ? null : inline);
        using var http = factory.CreateClient();
        Guid agentId = Guid.Empty, sessionId = Guid.Empty; string outcome = "failed";
        var checkpoints = new List<Checkpoint>(); var compactIds = new List<string>();
        using var pumpingCts = new CancellationTokenSource(); Task? pumping = null;
        GrokRulesReceipt? initialReceipt = null;
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var agent = await scope.ServiceProvider.GetRequiredService<AgentService>().CreateAsync(new CreateAgentRequest(
                    "retention-" + Guid.NewGuid().ToString("N"), repo.RepoPath, ModelLevel: AgentModelLevel.High,
                    ModelId: "grok-4.6", AutoCompactEnabled: false,
                    BundleKeys: fileArm ? [InstructionBundles.Orchestrator] : [], SystemPromptAppend: fileArm ? FileRules() : null), ct);
                agentId = agent.Id;
            }
            await using (var db = factory.Db()) { var agent = await db.Agents.SingleAsync(a => a.Id == agentId, ct); agent.Kind = AgentKind.Grok; await db.SaveChangesAsync(ct); }
            using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AgentControlService>().StartAsync(agentId, new(), ct);
            await using (var db = factory.Db()) sessionId = Guid.Parse((await db.Agents.SingleAsync(a => a.Id == agentId, ct)).PersistentSessionId!);
            pumping = PumpAsync(factory, sessionId, pumpingCts.Token);
            await factory.Services.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromMinutes(4), ct);
            await using (var db = factory.Db())
            {
                var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId, ct);
                session.Status.ShouldBe(SessionStatus.Running, session.FailureReason);
                initialReceipt = GrokRulesRefreshService.Receipt(session);
                if (fileArm)
                {
                    session.GrokRulesState.ShouldBe(GrokRulesState.Ready); initialReceipt.ShouldNotBeNull();
                    var refresh = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == sessionId && m.RulesRefreshKey != null, ct);
                    refresh.RulesAcknowledgedAt.ShouldNotBeNull();
                    AssertFullRead(home, refresh.Id, await File.ReadAllTextAsync(initialReceipt!.Path, ct));
                    await File.WriteAllTextAsync(Path.Combine(root, "initial-rules.md"), await File.ReadAllTextAsync(initialReceipt.Path, ct), ct);
                }
                else { initialReceipt.ShouldBeNull(); (await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == sessionId && m.RulesRefreshKey != null, ct)).ShouldBeFalse(); }
            }
            async Task SaveAsync() => await File.WriteAllTextAsync(Path.Combine(root, "checkpoints.json"), JsonSerializer.Serialize(checkpoints), ct);
            async Task<Checkpoint> ProbeAsync(int checkpoint, string name, string extra, bool needsBoundary)
            {
                var nonce = "PROBE-" + Guid.NewGuid().ToString("N");
                var priorIds = NativeBoundaries(home).Select(b => b.Id).ToHashSet();
                var started = elapsed.Elapsed.TotalSeconds;
                var prompt = nonce + " " + extra + " Resolve these three challenges in order: " + string.Join(", ", keys[checkpoint])
                    + ". Return the values on three plain text lines: EARLY=<first value>, MIDDLE=<second value>, TAIL=<third value>. Make no changes or external calls.";
                await factory.Services.GetRequiredService<SessionMessageQueueService>().EnqueueAsync(sessionId, prompt, MessageSendMode.WhenIdle, ct);
                long promptSequence = 0, endSequence = 0;
                while (endSequence == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    if (pumping?.IsFaulted == true) await pumping;
                    await using var db = factory.Db();
                    promptSequence = await db.TranscriptEntries.Where(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt && e.Text != null && e.Text.Contains(nonce)).Select(e => e.Sequence).FirstOrDefaultAsync(ct);
                    if (promptSequence > 0) endSequence = await db.TranscriptEntries.Where(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.TurnEnd && e.Sequence > promptSequence).OrderBy(e => e.Sequence).Select(e => e.Sequence).FirstOrDefaultAsync(ct);
                    if (endSequence == 0) await Task.Delay(300, ct);
                }
                await using var verify = factory.Db();
                var native = NativeBoundaries(home).Where(b => !priorIds.Contains(b.Id)).ToArray();
                long after = promptSequence; string? boundaryId = null;
                if (needsBoundary)
                {
                    native.Length.ShouldBe(1, "Each workload needs one genuine boundary followed by a pre-idle answer; zero or multiple is inconclusive");
                    boundaryId = native[0].Id; compactIds.Add(boundaryId);
                    var boundary = await verify.TranscriptEntries.SingleAsync(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.CompactBoundary && e.Uuid == boundaryId, ct);
                    boundary.Sequence.ShouldBeGreaterThan(promptSequence); boundary.Sequence.ShouldBeLessThan(endSequence); after = boundary.Sequence;
                }
                var text = string.Join("\n", await verify.TranscriptEntries.Where(e => e.AgentSessionId == sessionId && e.Sequence > after && e.Sequence < endSequence && e.Kind == TranscriptKinds.AssistantText).OrderBy(e => e.Sequence).Select(e => e.Text).ToListAsync(ct));
                var actual = new[] { "EARLY", "MIDDLE", "TAIL" }.Select(label =>
                {
                    var matches = Regex.Matches(text, label + @"\**\s*[:=]\s*[`""']*([A-Za-z0-9-]+)", RegexOptions.IgnoreCase);
                    return matches.Count == 0 ? "<absent>" : matches[^1].Groups[1].Value;
                }).ToArray();
                var matches = actual.SequenceEqual(expected[checkpoint]);
                var row = new Checkpoint(name, expected[checkpoint].ToArray(), actual, matches ? "survived" : actual.Any(a => a != "<absent>") ? "mixed" : "lost", boundaryId, promptSequence, endSequence, started, elapsed.Elapsed.TotalSeconds, text);
                checkpoints.Add(row); await SaveAsync();
                if (fileArm) actual.ShouldBe(expected[checkpoint], "File-arm canaries must survive, including the first post-compact continuation before idle refresh; otherwise return to Plan");
                return row;
            }
            await ProbeAsync(0, "initial", "Keep this audit token for a later continuity question: " + audit + ".", false);
            for (var turn = 0; turn < 2; turn++)
            {
                var before = await ProbeAsync(1 + 2 * turn, $"compact-{turn + 1}-before-idle",
                    "Read all four following disposable data files completely, in order, using continuation ranges as necessary. After all four files have been read, answer the challenges. Files: " + string.Join("; ", workloads[turn]) + ".", true);
                if (fileArm)
                {
                    var boundaryId = before.BoundaryId!;
                    Guid refreshId = Guid.Empty;
                    while (refreshId == Guid.Empty)
                    {
                        if (pumping?.IsFaulted == true) await pumping;
                        ct.ThrowIfCancellationRequested(); await using var db = factory.Db();
                        var boundarySequence = await db.TranscriptEntries.Where(e => e.AgentSessionId == sessionId && e.Uuid == boundaryId).Select(e => e.Sequence).SingleAsync(ct);
                        refreshId = await db.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId && m.RulesBoundarySequence >= boundarySequence && m.RulesAcknowledgedAt != null).Select(m => m.Id).FirstOrDefaultAsync(ct);
                        if (refreshId == Guid.Empty) await Task.Delay(300, ct);
                    }
                    await using var ready = factory.Db(); var session = await ready.AgentSessions.SingleAsync(s => s.Id == sessionId, ct);
                    session.GrokRulesState.ShouldBe(GrokRulesState.Ready);
                    var refresh = await ready.SessionQueuedMessages.SingleAsync(m => m.Id == refreshId, ct);
                    var owning = await ready.TranscriptEntries.Where(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt && e.Text != null && e.Text.Contains(GrokRulesRefreshService.Header(refreshId))).Select(e => e.Sequence).SingleAsync(ct);
                    owning.ShouldBeGreaterThan(before.EndSequence, "The automatic refresh must enter only at the real idle window");
                    AssertFullRead(home, refreshId, await File.ReadAllTextAsync(initialReceipt!.Path, ct));
                    await File.WriteAllTextAsync(Path.Combine(root, $"refresh-{turn + 1}.json"), JsonSerializer.Serialize(new { refreshId, refresh.CreatedAt, refresh.SentAt, refresh.RulesAcknowledgedAt, owning, before.BoundaryId }), ct);
                }
                await ProbeAsync(2 + 2 * turn, $"compact-{turn + 1}-after-refresh", "", false);
            }
            compactIds.Distinct().Count().ShouldBe(2);
            if (fileArm)
            {
                var previousHash = initialReceipt!.Sha256;
                expected[5][2] = "REVISION-" + Guid.NewGuid().ToString("N");
                await using (var db = factory.Db()) { var agent = await db.Agents.SingleAsync(a => a.Id == agentId, ct); agent.SystemPromptAppend = FileRules(); await db.SaveChangesAsync(ct); }
                using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AgentControlService>().StopAsync(agentId, ct);
                using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AgentControlService>().StartAsync(agentId, new(), ct);
                await factory.Services.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromMinutes(4), ct);
                await using (var db = factory.Db())
                {
                    var agent = await db.Agents.SingleAsync(a => a.Id == agentId, ct); agent.PersistentSessionId.ShouldBe(sessionId.ToString("D"));
                    var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId, ct); session.GrokRulesState.ShouldBe(GrokRulesState.Ready);
                    var receipt = GrokRulesRefreshService.Receipt(session).ShouldNotBeNull(); receipt.Path.ShouldBe(initialReceipt.Path); receipt.Sha256.ShouldNotBe(previousHash); receipt.Generation.ShouldNotBe(initialReceipt.Generation);
                    var refresh = await db.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId && m.RulesRefreshKey != null && m.RulesReceiptJson != null && m.RulesReceiptJson.Contains(receipt.Generation.ToString())).OrderByDescending(m => m.CreatedAt).FirstAsync(ct);
                    refresh.RulesAcknowledgedAt.ShouldNotBeNull(); AssertFullRead(home, refresh.Id, await File.ReadAllTextAsync(receipt.Path, ct));
                    await File.WriteAllTextAsync(Path.Combine(root, "resumed-receipt.json"), JsonSerializer.Serialize(receipt), ct);
                }
                var metadataPath = Path.Combine(root, "runner", "transcripts", sessionId.ToString("N") + ".json");
                using (var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath, ct)))
                    metadata.RootElement.GetProperty("resumeLaunch").GetBoolean().ShouldBeTrue("The runner must have issued a real native resume");
                await File.WriteAllTextAsync(Path.Combine(root, "resumed-native-metadata.json"), await File.ReadAllTextAsync(metadataPath, ct), ct);
                var resumed = await ProbeAsync(5, "resumed-revision", "Also report the audit token from the initial continuity question.", false);
                resumed.Text.ShouldContain(audit, customMessage: "The real native resume must preserve the prior task-history fact");
            }
            outcome = "passed";
        }
        finally
        {
            pumpingCts.Cancel();
            if (pumping is not null)
                try { await pumping; } catch (Exception ex) { await File.WriteAllTextAsync(Path.Combine(root, "pump-error.log"), ex.ToString()); }
            if (sessionId != Guid.Empty)
            {
                try { await File.WriteAllTextAsync(Path.Combine(root, "transcript.json"), JsonSerializer.Serialize(await runner.GetTranscriptAsync(sessionId, CancellationToken.None))); await File.WriteAllTextAsync(Path.Combine(root, "snapshot.json"), JsonSerializer.Serialize(await runner.GetSnapshotAsync(sessionId, CancellationToken.None))); }
                finally { await runner.KillAsync(sessionId, CancellationToken.None); }
            }
            await File.WriteAllTextAsync(Path.Combine(root, "native-acp.jsonl"), string.Join("\n", NativeLines(home)));
            await File.WriteAllTextAsync(Path.Combine(root, "expected.json"), JsonSerializer.Serialize(new { keys, expected, audit }));
            await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new { fileArm, outcome, began, ended = DateTimeOffset.UtcNow, elapsedSeconds = elapsed.Elapsed.TotalSeconds, ceilingMinutes = 120, thresholdPercent = 20, sessionId, agentId, compactIds, nativeBoundaries = NativeBoundaries(home), initialReceipt, systemBootstrapRetention = "unobservable on the live interface; behavioral verdict only" }));
        }
    }

    internal sealed record Checkpoint(string Name, string[] Expected, string[] Actual, string Verdict, string? BoundaryId, long PromptSequence, long EndSequence, double StartedSeconds, double EndedSeconds, string Text);
    private sealed record Boundary(string Id, long TimestampMs, long? TokensBefore, long? TokensAfter);
    private static IEnumerable<string> NativeLines(string home) => Directory.EnumerateFiles(home, "updates.jsonl", SearchOption.AllDirectories).SelectMany(File.ReadAllLines).Where(l => !string.IsNullOrWhiteSpace(l));
    private static List<Boundary> NativeBoundaries(string home)
    {
        var result = new List<Boundary>();
        foreach (var line in NativeLines(home))
        {
            try
            {
                using var json = JsonDocument.Parse(line); var p = json.RootElement.GetProperty("params"); var update = p.GetProperty("update");
                if (update.GetProperty("sessionUpdate").GetString() != "auto_compact_completed") continue;
                var meta = p.GetProperty("_meta"); result.Add(new(meta.GetProperty("eventId").GetString()!, meta.GetProperty("agentTimestampMs").GetInt64(), update.TryGetProperty("tokens_before", out var before) ? before.GetInt64() : null, update.TryGetProperty("tokens_after", out var after) ? after.GetInt64() : null));
            }
            catch (JsonException) { /* a still-appending final line is retried on the next observation */ }
        }
        return result.DistinctBy(b => b.Id).ToList();
    }
    private static void AssertFullRead(string home, Guid refreshId, string rules)
    {
        var rows = NativeLines(home).Select(l => JsonDocument.Parse(l)).ToArray();
        try
        {
            var parameters = rows.Select(j => j.RootElement.GetProperty("params")).ToArray();
            var start = Array.FindIndex(parameters, p => p.GetProperty("update").GetProperty("sessionUpdate").GetString() == "user_message_chunk" && string.Join("\n", Strings(p.GetProperty("update"))).Contains(GrokRulesRefreshService.Header(refreshId)));
            start.ShouldBeGreaterThanOrEqualTo(0, "The owning native user prompt must be present");
            // Native 1.0.13 user-message rows have eventId but no promptId. Bound the read
            // by those actual ordered user rows rather than inventing a prompt attribution.
            var end = Array.FindIndex(parameters, start + 1, p => p.GetProperty("update").GetProperty("sessionUpdate").GetString() == "user_message_chunk");
            if (end < 0) end = parameters.Length;
            var output = string.Join("\n", parameters.Skip(start + 1).Take(end - start - 1).Where(p => p.GetProperty("update").GetProperty("sessionUpdate").GetString() == "tool_call_update").SelectMany(p => Strings(p.GetProperty("update"))));
            foreach (var line in rules.ReplaceLineEndings("\n").Split('\n').Where(l => !string.IsNullOrWhiteSpace(l))) output.ShouldContain(line, customMessage: "Every rules line must occur in this refresh's actual native tool output");
        }
        finally { foreach (var row in rows) row.Dispose(); }
    }
    private static IEnumerable<string> Strings(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.String) yield return node.GetString()!;
        else if (node.ValueKind == JsonValueKind.Array) foreach (var c in node.EnumerateArray()) foreach (var s in Strings(c)) yield return s;
        else if (node.ValueKind == JsonValueKind.Object) foreach (var p in node.EnumerateObject()) foreach (var s in Strings(p.Value)) yield return s;
    }
    private static async Task PumpAsync(GrokRulesLiveMappedDispatchTests.LiveFactory factory, Guid sessionId, CancellationToken ct)
    {
        var runtime = factory.Services.GetRequiredService<AgentSessionRuntime>();
        while (!ct.IsCancellationRequested)
        {
            try { await runtime.SyncTranscriptAsync(sessionId, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            try { await Task.Delay(150, ct); } catch (OperationCanceledException) { break; }
        }
    }
}
