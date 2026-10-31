using System.Reflection;
using System.Runtime.Loader;
using Antiphon.TestSupport.LiveClaude;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Antiphon.Agents.Pty.Tests;

internal static class LiveClaudeDeliveryMutants
{
    private sealed record Mutation(string Pc, string V, string From, string To, string Assertion, bool All = false);
    private static readonly Mutation[] Mutations = [
        new("PC-107/head", "V-23", "PromptSubmissionMatch.IsCompleteIn(request.Body, r.Text)", "PromptSubmissionMatch.IsConfirmedBy(request.Body, r.Text)", "prompt-head"),
        new("PC-107/splice", "V-23", "PromptSubmissionMatch.IsCompleteIn(request.Body, r.Text)", "PromptSubmissionMatch.IsConfirmedBy(request.Body, r.Text)", "prompt-splice"),
        new("PC-108", "V-23", "if (!PromptSubmissionMatch.RequiresTextMatch(request.Body)) throw new LiveClaudeRefusal(\"prompt-too-short\");", "/* weak arm admitted */", "prompt-short"),
        new("PC-109/queued", "V-23", "r.Kind == \"UserPrompt\"", "r.Kind is \"UserPrompt\" or \"QueuedUserPrompt\"", "prompt-queued"),
        new("PC-109/screen", "V-23", "r.Kind == \"UserPrompt\"", "r.Kind is \"UserPrompt\" or \"Screen\"", "prompt-screen"),
        new("PC-110", "V-23", "r.Binding.Session == request.Binding.Session", "true", "prompt-session"),
        new("PC-111", "V-23", "r.Binding.Generation == request.Binding.Generation", "true", "prompt-generation"),
        new("PC-112", "V-23", "r.Binding.NativeBinding == request.Binding.NativeBinding", "true", "prompt-native-binding"),
        new("PC-113", "V-23", "r.Sequence > request.Floor", "true", "prompt-floor"),
        new("PC-114", "V-23", "var following = rows.Where", "return; var following = rows.Where", "response-missing"),
        new("PC-115", "V-23", "r.Api == response.Api && r.Id == response.Id", "true", "completion-turn"),
        new("PC-116", "V-23", "if (response.Sequence <= prompt.Sequence) continue;", "/* reversed order admitted */", "completion-order"),
        new("PC-117", "V-23", "r.Kind is \"UserPrompt\" or \"QueuedUserPrompt\"\n                && r.Sequence > prompt.Sequence", "false\n                && r.Sequence > prompt.Sequence", "completion-intervening", true),
        new("PC-118", "V-23", "r.Kind == \"AssistantText\" && !string.IsNullOrWhiteSpace(r.Text)", "r.Kind is \"AssistantText\" or \"Thinking\"", "thinking-end"),
        new("PC-119", "V-23", "if (response.Error) throw new LiveClaudeRefusal(\"provider-error\");", "/* error accepted */", "error-turn"),
        new("PC-120/submit", "V-24", "var prompt = RequirePrompt(request, rows);", "return; var prompt = RequirePrompt(request, rows);", "enter-lost-submit"),
        new("PC-120/menu", "V-24", "_ = RequirePrompt(request, rows);\n        RequireSameBridge", "/* screen verdict */\n        RequireSameBridge", "enter-lost-menu"),
        new("PC-121", "V-24", "await write(\"\\x1b[200~\" + normalized + \"\\x1b[201~\");", "await write(normalized + \"\\r\");", "write-shape"),
        new("PC-122/permission", "V-24", "if (state != DeliveryReadiness.Busy) throw new LiveClaudeRefusal(\"recipient-blocked\");", "if (state == DeliveryReadiness.Permission) break; if (state != DeliveryReadiness.Busy) throw new LiveClaudeRefusal(\"recipient-blocked\");", "dialog-Permission"),
        new("PC-122/question", "V-24", "if (state != DeliveryReadiness.Busy) throw new LiveClaudeRefusal(\"recipient-blocked\");", "if (state == DeliveryReadiness.Question) break; if (state != DeliveryReadiness.Busy) throw new LiveClaudeRefusal(\"recipient-blocked\");", "dialog-Question"),
        new("PC-122/unknown", "V-24", "if (state != DeliveryReadiness.Busy) throw new LiveClaudeRefusal(\"recipient-blocked\");", "if (state == DeliveryReadiness.Unknown) break; if (state != DeliveryReadiness.Busy) throw new LiveClaudeRefusal(\"recipient-blocked\");", "dialog-Unknown"),
        new("PC-123", "V-24", "if (state == DeliveryReadiness.Ready) break;", "if (state is DeliveryReadiness.Ready or DeliveryReadiness.Busy) break;", "busy-recipient"),
        new("PC-125/missing", "V-25", "RequireSameBridge(owner, beforeEsc, \"before-Esc\");", ";", "before-Esc-missing"),
        new("PC-125/changed", "V-25", "RequireSameBridge(owner, beforeEsc, \"before-Esc\");", ";", "before-Esc-changed"),
        new("PC-126/missing", "V-25", "RequireSameBridge(owner, afterEsc, \"after-Esc\");", ";", "after-Esc-missing"),
        new("PC-126/changed", "V-25", "RequireSameBridge(owner, afterEsc, \"after-Esc\");", ";", "after-Esc-changed"),
        new("PC-127/missing", "V-25", "RequireSameBridge(owner, afterSubmit, \"after-submit\");", ";", "after-submit-missing"),
        new("PC-127/changed", "V-25", "RequireSameBridge(owner, afterSubmit, \"after-submit\");", ";", "after-submit-changed"),
        new("PC-128", "V-25", "observed.Pid != owner.Pid", "false", "bridge-process"),
        new("PC-129", "V-25", "observed.Started != owner.Started", "false", "bridge-start"),
        new("PC-130", "V-25", "observed.Session != owner.Session", "false", "bridge-session"),
        new("PC-131", "V-25", "observed is not { Readable: true }", "observed is null", "bridge-read-failure")
    ];

    public static async Task RunAsync(string v)
    {
        if (Environment.GetEnvironmentVariable("C1545_DELIVERY_CONTROLS") != v) return;
        if (v == "V-26") { LiveClaudeDeliveryAudit.Controls(); return; }
        var root = LiveClaudeExecutableAudit.Root();
        var source = File.ReadAllText(Path.Combine(root, "tests/Shared/LiveClaude/LiveClaudeDeliveryEvidence.cs"));
        var cases = File.ReadAllText(Path.Combine(root, "tests/Antiphon.Agents.Pty.Tests/LiveClaudeDeliveryCases.cs"));
        cases = cases.Replace(LiveClaudeStartupAudit.Method(cases, "RunAsync").ToFullString(), "", StringComparison.Ordinal);
        var context = File.ReadAllText(Path.Combine(root, "tests/Shared/LiveClaude/LiveClaudeLaunchContext.cs"));
        var matcher = File.ReadAllText(Path.Combine(root, "src/Antiphon.SessionRunner.Contracts/PromptSubmissionMatch.cs"));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Distinct()
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToArray();
        byte[]? restored = null;
        var list = Mutations.Where(m => m.V == v).ToList();
        if (v == "V-24")
        {
            foreach (var cut in new[] { "before-body", "between-body-enter", "after-enter", "read", "after-prompt" })
                list.Add(new("PC-124/" + cut, v, "await send();\n        var rows = await observe();\n        if (completion) RequireSubmit(request, rows);\n        else _ = RequirePrompt(request, rows);\n        return rows;",
                    "try { await send(); var first = await observe(); if (completion) RequireSubmit(request, first); else _ = RequirePrompt(request, first); return first; } catch { await send(); var retry = await observe(); if (completion) RequireSubmit(request, retry); else _ = RequirePrompt(request, retry); return retry; }", "no-resend-" + cut));
        }
        foreach (var m in list)
        {
            var count = source.Split(m.From, StringSplitOptions.None).Length - 1;
            if (count != (m.All ? 2 : 1)) throw new InvalidOperationException("C1545 " + m.Pc + " mutation site ambiguous: " + count);
            var mutated = source.Replace(m.From, m.To, StringComparison.Ordinal);
            if (m.Pc == "PC-118") mutated = mutated.Replace("r.Kind == \"AssistantText\"\n                && r.Api", "r.Kind is \"AssistantText\" or \"Thinking\"\n                && r.Api", StringComparison.Ordinal);
            if (m.Pc == "PC-119") mutated = mutated.Replace("if (end.Error) throw new LiveClaudeRefusal(\"provider-error\");", "/* error end accepted */", StringComparison.Ordinal);
            // Each variant selects only its own negative fixture; otherwise an earlier negative could
            // conceal whether this particular guard was killed. All healthy paths still run.
            var chosen = SelectCase(cases, m.Assertion);
            var red = await ExecuteAsync(Compile(mutated, chosen), v);
            if (red is not InvalidOperationException || !red.Message.StartsWith("C1545 " + v + "/" + m.Assertion, StringComparison.Ordinal))
                throw new InvalidOperationException("C1545 " + m.Pc + " did not reach expected assertion: " + red?.Message);
            restored ??= Compile(source, cases);
            var green = await ExecuteAsync(restored, v);
            if (green is not null) throw new InvalidOperationException("C1545 " + m.Pc + " restoration failed", green);
            Console.WriteLine($"C1545 {m.Pc} assertion-red={m.Assertion}; restored-green source={LiveClaudeProfile.Hash(source)} patch={LiveClaudeProfile.Hash(m.From + "\n" + m.To)}");
        }

        byte[] Compile(string evidence, string fixtures)
        {
            const string usings = "global using System; global using System.IO; global using System.Linq; global using System.Collections.Generic; global using System.Threading.Tasks;\n";
            const string entry = "namespace Antiphon.Agents.Pty.Tests; public static class DeliveryEntry { public static Task Run(string v) => v switch { \"V-23\" => LiveClaudeDeliveryCases.PromptAsync(), \"V-24\" => LiveClaudeDeliveryCases.LostAsync(), \"V-25\" => LiveClaudeDeliveryCases.BridgeAsync(), _ => throw new InvalidOperationException() }; }";
            var compilation = CSharpCompilation.Create("DeliveryMutant" + Guid.NewGuid().ToString("N"),
                new[] { evidence, fixtures, context, matcher, entry }.Select(s => CSharpSyntaxTree.ParseText(usings + s)), references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            using var bytes = new MemoryStream(); var emit = compilation.Emit(bytes);
            if (!emit.Success) throw new InvalidOperationException("C1545 mutant compilation: " + string.Join(";", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
            return bytes.ToArray();
        }
    }
    private static string SelectCase(string cases, string assertion)
    {
        // Filter the negative checks at the assertion sink, while preserving their actual actions.
        // This does not manufacture a failure: the selected predicate must observe the mutant.
        return cases.Replace("if (!value) throw new InvalidOperationException", "if (!value && (c == \"" + assertion + "\" || c.StartsWith(\"" + assertion + "-\", StringComparison.Ordinal))) throw new InvalidOperationException", StringComparison.Ordinal)
            .Replace("throw new InvalidOperationException($\"C1545 {v}/{c} expected refusal\");", "Assert(false, v, c);", StringComparison.Ordinal);
    }
    private static async Task<Exception?> ExecuteAsync(byte[] payload, string v)
    {
        var load = new AssemblyLoadContext("c1545-delivery-copy", isCollectible: true);
        try
        {
            using var bytes = new MemoryStream(payload); var assembly = load.LoadFromStream(bytes);
            try { await (Task)assembly.GetType("Antiphon.Agents.Pty.Tests.DeliveryEntry")!.GetMethod("Run")!.Invoke(null, [v])!; }
            catch (TargetInvocationException e) { return e.InnerException; }
            catch (Exception e) { return e; }
            return null;
        }
        finally { load.Unload(); }
    }
}
