using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

[Category("Integration")]
[NotInParallel("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public class C1022TypedInputTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void ReadPrompts_preserves_string_and_text_block_bodies(bool blocks)
    {
        const string body = "L0000 é中😀\r\nL0001 whole body\r\nL0002 tail";
        object content = blocks
            ? new object[]
            {
                new { type = "text", text = "L0000 é中😀\r\n" },
                new { type = "image", source = "not prompt text" },
                new { type = "text", text = "L0001 whole body\r\nL0002 tail" },
            }
            : body;
        var line = JsonSerializer.Serialize(new { type = "user", message = new { content } });
        var path = Path.Combine(Path.GetTempPath(), "c1022-prompt-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            // Windows JSONL delimiters, an unrelated row, and an unfinished trailing row.
            File.WriteAllText(path, "{\"type\":\"assistant\"}\r\n" + line + "\r\n" + line[..20]);
            ReadPrompts(path).ShouldBe([body]);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task Typed_and_paste_modes_reach_the_modern_peer_distinctly()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows modern peer proof");
        ConPtyRedistributable.TryLocate(out _, out var why).ShouldBeTrue(why);
        NodeStdinProbe.NodeAvailable.ShouldBeTrue("required node.exe");
        var fake = Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");
        File.Exists(fake).ShouldBeTrue("required clipping fake");
        const string body = "L0000 é中😀\r\nL0001 whole body\r\nL0002 tail";
        foreach (var paste in new[] { false, true })
        {
            await using (var probe = await NodeStdinProbe.StartAsync(chunkLog: false, decset2004: true, backend: "modern"))
            {
                var result = await probe.DeliverAsync(body, wrap: paste);
                result.HasPasteStart.ShouldBe(paste, paste ? "paste-markers" : "typed-no-markers");
                result.HasPasteEnd.ShouldBe(paste, paste ? "paste-markers" : "typed-no-markers");
                // Independent expected bytes; hashing the same helper output would not guard it.
                const string normalized = "L0000 é中😀\nL0001 whole body\nL0002 tail";
                var expected = paste ? "\u001b[200~" + normalized + "\u001b[201~" : normalized;
                result.BodySha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expected))), "whole-body");
                result.MissingCount.ShouldBe(0);
            }

            var transcript = Path.Combine(Path.GetTempPath(), "c1022-typed-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                await using var runner = new PtyAgentRunner("modern");
                await runner.StartAsync(fake, [], cols: 120, rows: 250, env: new Dictionary<string, string>
                {
                    ["ANTIPHON_FAKE_STDIN_CLIP"] = "1",
                    ["ANTIPHON_FAKE_BURST_MS"] = "80",
                    ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = transcript,
                });
                try
                {
                    (await runner.WaitForOutputAsync(s => s.Contains("CLIP:mode=deterministic") && s.Contains("Fake Claude ready"),
                        TimeSpan.FromSeconds(15))).ShouldBeTrue("clip model armed");
                    runner.ClearLiveBuffer();
                    var marked = string.Join("\n", Enumerable.Range(0, 200).Select(i => $"Q{i:D5}"));
                    await runner.WriteAsync(C1022InputMode.Encode(marked, paste));
                    await Task.Delay(300); // Existing clip fixture's separate burst/Enter boundary.
                    await runner.WriteAsync("\r");
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    List<string> prompts = [];
                    while (watch.Elapsed < TimeSpan.FromSeconds(15))
                    {
                        prompts = ReadPrompts(transcript);
                        if (prompts.Count > 0) break;
                        await Task.Delay(50);
                    }
                    var received = prompts.ShouldHaveSingleItem("recipient-receipt");
                    received.ShouldBe(paste ? marked : marked[1024..], paste ? "whole-body" : "typed-modeled-chunk-loss");
                    if (paste) runner.SnapshotText().ShouldNotContain("CLIPPED:");
                    else runner.SnapshotText().ShouldContain("CLIPPED:");
                }
                finally { await runner.KillAsync(TimeSpan.FromSeconds(3)); }
            }
            finally { File.Delete(transcript); File.Delete(transcript + ".timing"); }
        }
    }

    private static List<string> ReadPrompts(string path)
    {
        var found = new List<string>();
        if (!File.Exists(path)) return found;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        foreach (var line in reader.ReadToEnd().Split('\n').SkipLast(1).Where(line => line.Length > 0))
        {
            using var json = JsonDocument.Parse(line);
            if (json.RootElement.GetProperty("type").GetString() != "user") continue;
            var content = json.RootElement.GetProperty("message").GetProperty("content");
            // FakeClaude writes a string; native Claude may write an array of content blocks.
            found.Add(content.ValueKind == JsonValueKind.String
                ? content.GetString()!
                : string.Concat(content.EnumerateArray()
                    .Where(p => p.GetProperty("type").GetString() == "text")
                    .Select(p => p.GetProperty("text").GetString())));
        }
        return found;
    }
}
