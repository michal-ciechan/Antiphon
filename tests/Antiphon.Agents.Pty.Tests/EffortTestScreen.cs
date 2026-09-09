using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shouldly;

namespace Antiphon.Agents.Pty.Tests;

internal sealed class EffortTestScreen
{
    public static string Capture(int index)
    {
        using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Card0449.EffortCaptures.json")!;
        using var json = JsonDocument.Parse(stream);
        var capture = json.RootElement.GetProperty("captures")[index];
        var screen = capture.GetProperty("renderedScreen").GetString()!;
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(screen))).ToLowerInvariant()
            .ShouldBe(capture.GetProperty("renderedScreenSha256").GetString());
        Console.WriteLine($"CARD-0449 capture {index + 1}: {capture.GetProperty("sessionId").GetString()} SHA256={capture.GetProperty("renderedScreenSha256").GetString()}");
        return screen;
    }

    public EffortTestScreen(int capture = 0) { Template = Capture(capture); Raw = Template; }
    public string Template { get; set; }
    public string Model { get; set; } = "Fable 5.1";
    public string Current { get; set; } = "xhigh";
    public string Suggested { get; set; } = "high";
    public int Highlight { get; set; } = 1;
    public string? MovesOn { get; set; } = "j";
    public int SwallowEnters { get; set; }
    public bool Dialog { get; set; } = true;
    public string? Override { get; set; }
    public string? ResultBanner { get; set; }
    public bool ShowBanner { get; set; } = true;
    public string Composer { get; set; } = "";
    public string? AppliedEffort { get; private set; }
    public int? AcceptedOption { get; private set; }
    public string Raw { get; private set; }
    public int Snapshots { get; private set; }
    public int ClearObservations { get; set; }
    public int TokenWrites { get; private set; }
    public Action<EffortTestScreen>? AfterSnapshot { get; set; }
    public Action<EffortTestScreen>? OnSnapshot { get; set; }
    public Action<EffortTestScreen, string>? AfterWrite { get; set; }
    public List<(string Key, TimeSpan At, bool Dialog, int Highlight)> Writes { get; } = [];
    public List<string> Trace { get; } = [];
    public Stopwatch Clock { get; } = Stopwatch.StartNew();
    public string Screen => Override ?? (Dialog
        ? Template.Replace("Fable 5.1", Model).Replace("xhigh", "CURRENT").Replace("high", Suggested).Replace("CURRENT", Current)
            .Replace("> Keep", Highlight == 1 ? "> Keep" : "  Keep")
            .Replace("     Switch", Highlight == 2 ? "   > Switch" : "     Switch")
        : (ShowBanner ? $"{Model} with {ResultBanner ?? AppliedEffort ?? Current} effort · Claude Max\n" : "")
            + $"> {Composer}\n? for shortcuts");

    public Task<string> SnapshotAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Snapshots++;
        OnSnapshot?.Invoke(this);
        var screen = Screen;
        if (!Dialog && Override is null) ClearObservations++;
        Trace.Add($"{Clock.ElapsedMilliseconds}: read {Snapshots}, dialog={Dialog}, highlight={Highlight}, applied={AppliedEffort}, text={Composer}");
        Raw += "\n" + screen;
        AfterSnapshot?.Invoke(this);
        return Task.FromResult(screen);
    }

    public Task WriteAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Writes.Add((key, Clock.Elapsed, Dialog, Highlight));
        Trace.Add($"{Clock.ElapsedMilliseconds}: write {key.Replace("\r", "Enter")}, dialog={Dialog}, highlight={Highlight}");
        if (Override is null)
        {
            if (Dialog)
            {
                if (key == MovesOn) Highlight = Highlight == 1 ? 2 : 1;
                if (key == "\r" && SwallowEnters-- <= 0)
                {
                    AppliedEffort = Highlight == 1 ? Current : Suggested;
                    AcceptedOption = Highlight;
                    Dialog = false;
                    Composer = "";
                }
            }
            else if (key == "\x15") Composer = "";
            else { Composer += key; TokenWrites++; }
        }
        AfterWrite?.Invoke(this, key);
        return Task.CompletedTask;
    }

    public async Task<ClaudeEffortResolution> ResolveAsync(string[]? args = null, int budgetMs = 8000, CancellationToken ct = default) =>
        await ClaudeEffortPrompt.ResolveAsync(SnapshotAsync, WriteAsync,
            ClaudeEffortIntent.Read(args ?? ["--effort", "xhigh"]), TimeSpan.FromMilliseconds(budgetMs), ct);

    public Task<ClaudeReadinessResult> ReadyAsync(int totalMs = 12000, int maxWrites = 3,
        Task? exited = null, CancellationToken ct = default) => ClaudeStartupReadiness.RunAsync("zzdeadbeef",
        SnapshotAsync, WriteAsync, new("xhigh"), new(
            ComposerProbeOptions.FromMilliseconds(totalMs, 25, 1000, 500, maxWrites),
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(7)),
        Trace.Add, exited ?? new TaskCompletionSource().Task, ct);

    public string Evidence => string.Join("\n", Trace);
}
