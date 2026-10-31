using System.Diagnostics;
using System.Text.Json;
using Antiphon.TestSupport.LiveClaude;

namespace Antiphon.Agents.Pty.Tests;

internal static class LiveClaudeDeliveryCases
{
    internal static DeliveryBinding Binding => new("session-A", "generation-A", "101:start-A");
    internal static string Body => "head-" + new string('h', 210) + "\r\nunique-middle-" + new string('m', 100) + "\r\ntail-distinct";
    internal static DeliveryRequest Request => new(Binding, 10, Body);
    internal static DeliveryBridge Bridge => new(101, "start-A", "session-A", "bridge-A");
    internal static void Assert(bool value, string v, string c)
    { if (!value) throw new InvalidOperationException($"C1545 {v}/{c} required predicate"); }
    internal static void Refuses(Action action, string v, string c, string? reason = null)
    {
        try { action(); }
        catch (LiveClaudeRefusal e)
        { Assert(e.ExitCode == 20 && !e.Credit && (reason is null || e.Reason == reason), v, c); return; }
        throw new InvalidOperationException($"C1545 {v}/{c} expected refusal");
    }
    internal static string User(string body, string session = "session-A", string id = "prompt-A", string type = "user")
        => JsonSerializer.Serialize(new { type, uuid = id, sessionId = session, message = new { content = body } }) + "\n";
    internal static string Assistant(string parent = "prompt-A", string api = "api-A", string id = "response-A",
        string kind = "text", bool end = true, bool error = false)
        => JsonSerializer.Serialize(new { type = "assistant", uuid = id, parentUuid = parent, sessionId = "session-A",
            isApiErrorMessage = error, message = new { id = api, stop_reason = end ? "end_turn" : null,
                content = new[] { new Dictionary<string, string> { ["type"] = kind, [kind] = "synthetic response" } } } }) + "\n";
    internal static IReadOnlyList<DeliveryRow> Rows(string? user = null, string? assistant = null)
        => LiveClaudeDeliveryEvidence.ReadJsonl(string.Concat(Enumerable.Repeat("{}\n", 10))
            + (user ?? User(Body)) + (assistant ?? Assistant()), Binding);
    internal static void Menu(DeliveryRequest request, IReadOnlyList<DeliveryRow> rows)
        => LiveClaudeDeliveryEvidence.RequireMenu(request, rows, Bridge, Bridge, Bridge, Bridge);

    public static async Task RunAsync(string v)
    {
        var clock = Stopwatch.StartNew();
        switch (v)
        {
            case "V-23": await PromptAsync(); break;
            case "V-24": await LostAsync(); break;
            case "V-25": await BridgeAsync(); break;
            case "V-26": LiveClaudeDeliveryAudit.Run(); break;
            default: throw new InvalidOperationException("Unknown delivery witness");
        }
        await LiveClaudeDeliveryMutants.RunAsync(v);
        Console.WriteLine($"C1545 {v} bodyMs={clock.Elapsed.TotalMilliseconds:F1} fixture=synthetic-jsonl db=N/A http=N/A native=N/A");
    }

    internal static Task PromptAsync()
    {
        const string v = "V-23";
        var healthy = Rows();
        LiveClaudeDeliveryEvidence.RequireSubmit(Request, healthy);
        Menu(Request, healthy);
        foreach (var (name, request, rows) in new (string, DeliveryRequest, IReadOnlyList<DeliveryRow>)[] {
            ("prompt-head", Request, Rows(User(Body[..200]))),
            ("prompt-splice", Request, Rows(User(Body[..210] + Body[^25..]))),
            ("prompt-short", Request with { Body = "ok" }, Rows(User("unrelated user"))),
            ("prompt-queued", Request, Rows(User(Body, type:"attachment"))),
            ("prompt-screen", Request, Rows(User(Body, type:"screen"))),
            ("prompt-session", Request, Rows(User(Body, "foreign"))),
            ("prompt-generation", Request, healthy.Select(r => r with { Binding = r.Binding with { Generation = "foreign" } }).ToArray()),
            ("prompt-native-binding", Request, healthy.Select(r => r with { Binding = r.Binding with { NativeBinding = "foreign" } }).ToArray()),
            ("prompt-floor", Request with { Floor = 11 }, healthy) })
        {
            Refuses(() => LiveClaudeDeliveryEvidence.RequireSubmit(request, rows), v, name + "-submit");
            Refuses(() => Menu(request, rows), v, name + "-menu");
        }
        Refuses(() => LiveClaudeDeliveryEvidence.RequireSubmit(Request, Rows(assistant:"")), v, "response-missing");
        Refuses(() => LiveClaudeDeliveryEvidence.RequireSubmit(Request, Rows(assistant: Assistant(parent:"prior-prompt"))), v, "response-unrelated");
        var response = healthy[1] with { End = false };
        var unrelatedEnd = healthy[1] with { Sequence = 13, Id = "prior-response", Api = "prior-api", Parent = "prior-prompt" };
        Refuses(() => LiveClaudeDeliveryEvidence.RequireSubmit(Request, [healthy[0], response, unrelatedEnd]), v, "completion-turn");
        Refuses(() => LiveClaudeDeliveryEvidence.RequireSubmit(Request,
            [healthy[0], healthy[1] with { Sequence = 9 }]), v, "completion-order");
        Refuses(() => LiveClaudeDeliveryEvidence.RequireSubmit(Request,
            [healthy[0], healthy[0] with { Sequence = 12, Id = "prompt-B", Text = "unrelated" }, healthy[1] with { Sequence = 13 }]), v, "completion-intervening");
        Refuses(() => LiveClaudeDeliveryEvidence.RequireSubmit(Request, Rows(assistant:Assistant(kind:"thinking"))), v, "thinking-end");
        var split = Rows(assistant: Assistant(kind:"thinking", id:"thinking-A") + Assistant(parent:"thinking-A"));
        LiveClaudeDeliveryEvidence.RequireSubmit(Request, split);
        Refuses(() => LiveClaudeDeliveryEvidence.RequireSubmit(Request, Rows(assistant:Assistant(error:true))), v, "error-turn", "provider-error");
        Refuses(() => LiveClaudeDeliveryEvidence.ReadJsonl(User(Body).TrimEnd('\n'), Binding), v, "partial-record");
        Refuses(() => LiveClaudeDeliveryEvidence.ReadJsonl("{broken}\n", Binding), v, "malformed-record");
        // Own on-disk substitute: recipient records are scripted independently of input writes.
        var root = Directory.CreateTempSubdirectory("c1545-delivery-").FullName;
        try
        {
            var file = Path.Combine(root, "recipient.jsonl");
            File.WriteAllText(file, string.Concat(Enumerable.Repeat("{}\n",10)) + User(Body) + Assistant());
            LiveClaudeDeliveryEvidence.RequireSubmit(Request, LiveClaudeDeliveryEvidence.ReadJsonl(File.ReadAllText(file), Binding));
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    internal static async Task LostAsync()
    {
        const string v = "V-24";
        // Whole composer and done-like screen exist; the independently scripted recipient has no prompt.
        var screen = Body + " Crunched for 2s";
        Assert(screen.Contains(Body) && screen.Contains(" for 2s"), v, "screen-fixture");
        Refuses(() => LiveClaudeDeliveryEvidence.RequireSubmit(Request, []), v, "enter-lost-submit");
        Refuses(() => Menu(Request, []), v, "enter-lost-menu");
        var writes = new List<string>();
        await LiveClaudeDeliveryEvidence.SendOnceAsync(Body, () => DeliveryReadiness.Ready, () => Task.CompletedTask,
            s => { writes.Add(s); return Task.CompletedTask; }, _ => Task.FromResult(true));
        Assert(writes.SequenceEqual(new[] { "\x1b[200~" + Body.ReplaceLineEndings("\n") + "\x1b[201~", "\r" }), v, "write-shape");
        foreach (var state in new[] { DeliveryReadiness.Permission, DeliveryReadiness.Question, DeliveryReadiness.Unknown })
        {
            writes.Clear();
            try { await LiveClaudeDeliveryEvidence.SendOnceAsync(Body, () => state, () => Task.CompletedTask,
                s => { writes.Add(s); return Task.CompletedTask; }, _ => Task.FromResult(true)); }
            catch (LiveClaudeRefusal) { }
            Assert(writes.Count == 0, v, "dialog-" + state);
        }
        var ready = false; var waits = 0; writes.Clear();
        await LiveClaudeDeliveryEvidence.SendOnceAsync(Body, () => ready ? DeliveryReadiness.Ready : DeliveryReadiness.Busy,
            () => { Assert(writes.Count == 0, v, "busy-recipient"); ready = true; waits++; return Task.CompletedTask; },
            s => { writes.Add(s); return Task.CompletedTask; }, _ => Task.FromResult(true));
        Assert(waits == 1 && writes.Count == 2, v, "busy-recipient");
        foreach (var cut in new[] { "before-body", "between-body-enter", "after-enter", "read", "after-prompt" })
        {
            var attempts = 0; var readCalls = 0; writes.Clear(); var refused = false;
            async Task Send()
            {
                attempts++;
                if (cut == "before-body") throw new IOException("synthetic cut");
                await LiveClaudeDeliveryEvidence.SendOnceAsync(Body, () => DeliveryReadiness.Ready, () => Task.CompletedTask,
                    s => { writes.Add(s); if (cut == "between-body-enter") throw new IOException("synthetic cut"); return Task.CompletedTask; },
                    _ => Task.FromResult(true));
                if (cut == "after-enter") throw new IOException("synthetic cut");
            }
            Task<IReadOnlyList<DeliveryRow>> Read()
            {
                readCalls++;
                if (cut == "read") throw new IOException("synthetic cut");
                return Task.FromResult(Rows(assistant: ""));
            }
            try { await LiveClaudeDeliveryEvidence.SubmitOnceAsync(Request, Send, Read, true); }
            catch (Exception e) when (e is IOException or LiveClaudeRefusal) { refused = true; }
            Assert(refused && attempts == 1 && readCalls <= 1, v, "no-resend-" + cut);
        }
    }

    internal static Task BridgeAsync()
    {
        const string v = "V-25";
        var rows = Rows(assistant: "");
        Menu(Request, rows);
        foreach (var phase in new[] { "before-Esc", "after-Esc", "after-submit" })
        foreach (var variant in new[] { "missing", "changed" })
        {
            var observed = variant == "missing" ? null : Bridge with { Id = "bridge-B" };
            Refuses(() => LiveClaudeDeliveryEvidence.RequireMenu(Request, rows, Bridge,
                phase == "before-Esc" ? observed : Bridge,
                phase == "after-Esc" ? observed : Bridge,
                phase == "after-submit" ? observed : Bridge), v, phase + "-" + variant);
        }
        foreach (var (name, observed) in new[] {
            ("bridge-process", Bridge with { Pid = 102 }), ("bridge-start", Bridge with { Started = "start-B" }),
            ("bridge-session", Bridge with { Session = "session-B" }), ("bridge-read-failure", Bridge with { Readable = false }) })
            Refuses(() => LiveClaudeDeliveryEvidence.RequireMenu(Request, rows, Bridge, Bridge, Bridge, observed), v, name);
        return Task.CompletedTask;
    }
}
