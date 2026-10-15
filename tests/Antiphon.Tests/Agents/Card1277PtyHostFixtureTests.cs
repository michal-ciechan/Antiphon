using System.Collections;
using System.Diagnostics;
using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-1277. Linux PtyHost pins for the standing-recovery fakes: the screen echo stays at
/// 60 characters, and the normalized assistant text keeps the nonce past that cut. Claude's
/// body write and a later CR must become a normalizer turn. ANTIPHON_FAKE_LF_ENTER stays unset.
/// </summary>
[Category("Integration")]
[NotInParallel(nameof(Card1277PtyHostFixtureTests))]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class Card1277PtyHostFixtureTests
{
    [Test]
    public async Task Grok_jsonl_reply_keeps_the_nonce_past_the_screen_cut()
    {
        var (prompt, nonce) = ChannelWire();
        prompt.Length.ShouldBe(108);
        prompt.IndexOf(nonce, StringComparison.Ordinal).ShouldBe(52);
        prompt[..60].IndexOf(nonce, StringComparison.Ordinal).ShouldBe(-1);

        var root = Path.Combine(Path.GetTempPath(), "c1277-grok-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "grok-home");
        var cwd = Path.Combine(root, "cwd");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid().ToString("D");
        var updates = Path.Combine(home, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), sessionId, "updates.jsonl");
        var exe = TestAppHostPath.Require("fakegrok", AppContext.BaseDirectory);
        await using var runner = new PtyAgentRunner("inbox");
        try
        {
            await runner.StartAsync(exe, ["--cwd", cwd, "--session-id", sessionId], cwd,
                ChildEnv(("GROK_HOME", home)));
            (await runner.WaitForOutputAsync(s => s.Contains("Fake Grok ready", StringComparison.Ordinal), TimeSpan.FromSeconds(15)))
                .ShouldBeTrue("fakegrok banner missing. screen=" + runner.SnapshotText());
            await DeliverTwoWriteAsync(runner, prompt);

            var screenCut = "FAKE response to: " + prompt[..60];
            var screenPast = "FAKE response to: " + prompt[..61];
            var sawCut = await runner.WaitForOutputAsync(
                s => s.Contains(screenCut, StringComparison.Ordinal), TimeSpan.FromSeconds(8));
            var screen = runner.SnapshotText();
            var jsonl = await PollFileAsync(updates, t => t.Contains("turn_completed", StringComparison.Ordinal), TimeSpan.FromSeconds(5));
            var detail = "screen=" + screen + "\njsonl=" + jsonl;
            sawCut.ShouldBeTrue("screen echo missing the 60-character cut. " + detail);
            screen.IndexOf(screenPast, StringComparison.Ordinal).ShouldBe(-1, "screen echo passed the 60-character cut. " + detail);

            var parts = NormalizeGrok(jsonl);
            var user = parts.FirstOrDefault(p => p.Kind == TranscriptKinds.UserPrompt).Text ?? "";
            var assistant = string.Join("\n", parts.Where(p => p.Kind == TranscriptKinds.AssistantText).Select(p => p.Text));
            user.IndexOf(nonce, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, "user chunk lost the nonce. " + detail);
            assistant.IndexOf(nonce, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0,
                "assistant text lost the nonce past the screen cut. assistant=" + assistant + "\n" + detail);
        }
        finally
        {
            await StopAsync(runner);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    public async Task Claude_two_write_submit_is_a_normalizer_turn()
    {
        var (prompt, nonce) = ChannelWire();
        prompt.Length.ShouldBe(108);
        prompt[..60].IndexOf(nonce, StringComparison.Ordinal).ShouldBe(-1);

        var root = Path.Combine(Path.GetTempPath(), "c1277-claude-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "claude-home");
        var cwd = Path.Combine(root, "cwd");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid();
        var transcript = Path.Combine(home, sessionId.ToString("D"), "conversation.jsonl");
        var exe = TestAppHostPath.Require("fakeclaude", AppContext.BaseDirectory);
        await using var runner = new PtyAgentRunner("inbox");
        try
        {
            await runner.StartAsync(exe, ["--session-id", sessionId.ToString("D")], cwd,
                ChildEnv(("ANTIPHON_FAKE_NATIVE_HOME", home)));
            (await runner.WaitForOutputAsync(s => s.Contains("Fake Claude ready", StringComparison.Ordinal), TimeSpan.FromSeconds(15)))
                .ShouldBeTrue("fakeclaude banner missing. screen=" + runner.SnapshotText());
            await DeliverTwoWriteAsync(runner, prompt);

            var jsonl = await PollFileAsync(transcript,
                t => t.Contains("\"type\":\"user\"", StringComparison.Ordinal) || t.Contains("\"type\": \"user\"", StringComparison.Ordinal),
                TimeSpan.FromSeconds(8));
            var screen = runner.SnapshotText();
            var parts = NormalizeClaude(jsonl);
            var user = string.Join("\n", parts.Where(p => p.Kind == TranscriptKinds.UserPrompt).Select(p => p.Text));
            var assistant = string.Join("\n", parts.Where(p => p.Kind == TranscriptKinds.AssistantText).Select(p => p.Text));
            var detail = "screen=" + screen + "\njsonl=" + jsonl + "\nuser=" + user + "\nassistant=" + assistant;
            user.IndexOf(prompt, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0,
                "normalizer yielded no UserPrompt for the two-write body. " + detail);
            assistant.IndexOf(nonce, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0,
                "assistant text lost the nonce past the screen cut. " + detail);
            var screenCut = "FAKE response to: " + prompt[..60];
            var screenPast = "FAKE response to: " + prompt[..61];
            screen.IndexOf(screenCut, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, "screen echo missing the 60-character cut. " + detail);
            screen.IndexOf(screenPast, StringComparison.Ordinal).ShouldBe(-1, "screen echo passed the 60-character cut. " + detail);
        }
        finally
        {
            await StopAsync(runner);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static (string Prompt, string Nonce) ChannelWire()
    {
        var id = new string('a', 32);
        var nonce = "queued-channel-recovery-" + new string('b', 32);
        return ($"[antiphon-channel:{id}] {nonce}", nonce);
    }

    /// <summary>
    /// Product delivery writes the body, waits, then writes CR as its own burst
    /// (<c>VerifiedPromptSubmitter</c>). This gap is longer than that pause and the fake burst gap.
    /// </summary>
    private static async Task DeliverTwoWriteAsync(PtyAgentRunner runner, string prompt)
    {
        await runner.WriteAsync(prompt);
        var echoed = await runner.WaitForOutputAsync(
            s => s.Contains(prompt, StringComparison.Ordinal), TimeSpan.FromSeconds(8));
        echoed.ShouldBeTrue("prompt echo missing before Enter. screen=" + runner.SnapshotText());
        await Task.Delay(50);
        await runner.WriteAsync("\r");
    }

    private static Dictionary<string, string> ChildEnv(params (string Key, string Value)[] set)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
                env[key] = value;
        }

        env.Remove("ANTIPHON_FAKE_LF_ENTER");
        foreach (var (key, value) in set)
            env[key] = value;
        return env;
    }

    private static List<TranscriptPart> NormalizeGrok(string jsonl)
    {
        var grok = new GrokTranscriptNormalizer();
        var parts = new List<TranscriptPart>();
        foreach (var line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            parts.AddRange(grok.Normalize(line));
        parts.AddRange(grok.FlushPending());
        return parts;
    }

    private static List<TranscriptPart> NormalizeClaude(string jsonl)
    {
        var parts = new List<TranscriptPart>();
        foreach (var line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            parts.AddRange(TranscriptNormalizer.Normalize(line));
        return parts;
    }

    private static async Task<string> PollFileAsync(string path, Func<string, bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var text = "";
        while (true)
        {
            text = await ReadSharedAsync(path);
            if (done(text) || DateTime.UtcNow >= deadline)
                return text;
            await Task.Delay(50);
        }
    }

    private static async Task<string> ReadSharedAsync(string path)
    {
        if (!File.Exists(path))
            return "";
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static async Task StopAsync(PtyAgentRunner runner)
    {
        try { await runner.KillAsync(TimeSpan.FromSeconds(3)); } catch (Exception) { }
        if (runner.Pid is not int pid)
            return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
                return;
            process.Kill(entireProcessTree: true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }
}
