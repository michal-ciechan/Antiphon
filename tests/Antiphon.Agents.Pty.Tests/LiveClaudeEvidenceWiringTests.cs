using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.TestSupport.LiveClaude;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>Portable sanitizer witnesses. No native process, provider or live admission.</summary>
[Category("Unit"), Timeout(180_000)]
[ParallelLimiter<ProcessSpawnLimit>]
public class LiveClaudeEvidenceWiringTests
{
    private static readonly (string Path, string Method)[] Callers =
    [
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs", "Control_auto_connects_a_bridge_session_before_the_first_user"),
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs", "Settings_file_prevents_auto_connect_and_keeps_startup_records"),
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlAtStartupCanaryTests.cs", "Remote_control_slash_command_still_arms_after_settings_off"),
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractLiveTests.cs", "Submitting_via_two_writes_completes_a_turn"),
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitContractTests.cs", "SubmitClaudeAsync"),
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlMenuCanaryTests.cs", "C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt"),
        ("tests/Antiphon.Agents.Pty.Tests/ClaudeRemoteControlMenuCanaryTests.cs", "One_Esc_dismisses_the_remote_control_management_menu")
    ];

    private static readonly string[] Families = ["control", "off", "slash", "submit", "c514"];
    private static readonly string[] Kinds = ["success", "assertion", "skip"];

    [Test]
    public void EvidenceIsSanitized()
    {
        var project = typeof(LiveClaudeExecutable).Assembly.GetType(
            "Antiphon.TestSupport.LiveClaude.LiveClaudeEvidenceSanitizer")?.GetMethod("Project");
        if (project is null)
            throw new InvalidOperationException("C1545 V-5/entry required sanitizer is absent");
        var run = RunToken();
        var exported = new List<string>();
        foreach (var family in Families)
        {
            foreach (var kind in Kinds)
                exported.Add(Emit(family + "-" + kind, kind, run, LiveClaudeEvidenceSentinels.Link,
                    LiveClaudeEvidenceSentinels.Screen, LiveClaudeEvidenceSentinels.Secret, LiveClaudeEvidenceSentinels.Device));
        }
        exported.Add(Emit("enc-lf", "success", run, null, LiveClaudeEvidenceSentinels.Link + "\n" + LiveClaudeEvidenceSentinels.Screen, null, null));
        exported.Add(Emit("enc-crlf", "success", run, null, LiveClaudeEvidenceSentinels.Screen + "\r\n" + LiveClaudeEvidenceSentinels.Link, null, null));
        exported.Add(Emit("enc-ansi", "success", run, null, "SENTINEL" + "\u001b[31m" + "-SCREEN-3a7c132f", null, null));
        exported.Add(Emit("enc-osc8", "success", run, null, "\u001b]8;;" + LiveClaudeEvidenceSentinels.Link + "\u001b\\", null, null));
        exported.Add(Emit("enc-json", "success", run, null, "\\u0053ENTINEL-SCREEN-3a7c132f", null, null));
        exported.Add(Emit("enc-xml", "success", run, null, "&#x53;ENTINEL-SCREEN-3a7c132f", null, null));
        exported.Add(Emit("enc-double", "success", run, null, "&amp;#x53;ENTINEL-LINK-3a7c132f", null, null));
        var unsafeRun = LiveClaudeEvidence.Projected("enc-double", "EvidenceIsSanitized", "success",
            "bad " + LiveClaudeEvidenceSentinels.Secret, null, null, null, null);
        if (!unsafeRun.Contains("run=rejected", StringComparison.Ordinal) || LiveClaudeEvidenceSanitizer.ContainsSentinel(unsafeRun))
            throw new InvalidOperationException("C1545 V-5/unsafe-run-id unsafeRunIdRejectedOrProjected");
        var export = ExportPath();
        if (export is not null)
            File.AppendAllText(export, string.Join('\n', exported) + "\n");
        ProveMutants();
    }

    [Test]
    public void CanarySinksCannotBypassSanitizer()
    {
        var counts = Count(Callers.Select(caller => (
            LiveClaudeExecutableAudit.Method(LiveClaudeExecutableAudit.Read(caller.Path), caller.Method).Body!,
            caller.Method)));
        if (counts.Console != 0)
            throw new InvalidOperationException("C1545 V-6/raw-console unprotectedSuccessSinkCount");
        if (counts.Assertion != 0)
            throw new InvalidOperationException("C1545 V-6/raw-assertion unprotectedAssertionSinkCount");
        if (counts.Skip != 0)
            throw new InvalidOperationException("C1545 V-6/raw-skip unprotectedSkipSinkCount");
        if (counts.Actual != 0)
            throw new InvalidOperationException("C1545 V-6/raw-actual rawAssertionOperandCount");
        if (counts.Recognized == 0)
            throw new InvalidOperationException("C1545 V-6/raw-console unprotectedSuccessSinkCount");
        var menu = LiveClaudeExecutableAudit.Method(
            LiveClaudeExecutableAudit.Read(Callers[6].Path), Callers[6].Method).ToString();
        if (!menu.Contains("LiveClaudeEvidence.Success", StringComparison.Ordinal)
            || !menu.Contains("LiveClaudeEvidence.Assertion", StringComparison.Ordinal)
            || !menu.Contains("LiveClaudeEvidence.Skip", StringComparison.Ordinal))
            throw new InvalidOperationException("C1545 V-6/raw-console unprotectedSuccessSinkCount");
        ProveCallerMutants();
    }

    private static string Emit(string caseId, string kind, string run, string? link, string? screen, string? secret, string? device)
    {
        var text = LiveClaudeEvidence.Projected(caseId, "EvidenceIsSanitized", kind, run, link, screen, secret, device);
        if (!text.Contains("C1545-EVIDENCE case=" + caseId + " test=EvidenceIsSanitized kind=" + kind, StringComparison.Ordinal))
            throw new InvalidOperationException("C1545 V-5/suppress-all actualSinkHasCaseMarker");
        if (LiveClaudeEvidenceSanitizer.ContainsSentinel(text) || text.Contains("SENTINEL-", StringComparison.Ordinal)
            || text.Contains("https://claude.ai", StringComparison.Ordinal))
            throw new InvalidOperationException("C1545 V-5/link payloadHasSentinel");
        return text.Split('\n')[0];
    }

    private static void ProveMutants()
    {
        var source = LiveClaudeExecutableAudit.Read("tests/Shared/LiveClaude/LiveClaudeEvidenceSanitizer.cs");
        var mutants = new (string Pc, string From, string To, string Assertion)[]
        {
            ("PC-33", ".Append(Classify(capture.Link))", ".Append(capture.Link)", "payloadHasSentinel"),
            ("PC-34", ".Append(Classify(capture.Screen))", ".Append(capture.Screen)", "payloadHasScreenSentinel"),
            ("PC-35", ".Append(Classify(capture.Secret))", ".Append(capture.Secret)", "payloadHasCredentialSentinel"),
            ("PC-36", ".Append(Classify(capture.Device))", ".Append(capture.Device)", "payloadHasDeviceSentinel"),
            ("PC-37", "var marker = \"C1545-EVIDENCE case=\" + capture.CaseId + \" test=\" + capture.TestId\n            + \" kind=\" + capture.Kind + \" run=\" + run;", "var marker = \"\";", "actualSinkHasCaseMarker")
        };
        foreach (var mutant in mutants)
        {
            var count = source.Split(mutant.From).Length - 1;
            if (count != 1) throw new InvalidOperationException("C1545 " + mutant.Pc + " mutation site ambiguous: " + count);
            var text = InvokeProject(source.Replace(mutant.From, mutant.To, StringComparison.Ordinal));
            var leaked = LiveClaudeEvidenceSanitizer.ContainsSentinel(text) || text.Contains("https://claude.ai", StringComparison.Ordinal);
            var marked = text.Contains("C1545-EVIDENCE case=control-success", StringComparison.Ordinal);
            var red = mutant.Pc == "PC-37" ? !marked : leaked;
            if (!red) throw new InvalidOperationException("C1545 " + mutant.Pc + " did not reach expected assertion");
            var restored = InvokeProject(source);
            if (LiveClaudeEvidenceSanitizer.ContainsSentinel(restored) || !restored.Contains("C1545-EVIDENCE case=control-success", StringComparison.Ordinal))
                throw new InvalidOperationException("C1545 " + mutant.Pc + " restore failed");
            Console.WriteLine("C1545 " + mutant.Pc + " assertion-red; restored-green " + mutant.Assertion);
        }
    }

    private static string InvokeProject(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Distinct().Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("c1545-evidence-" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var emit = compilation.Emit(bytes);
        if (!emit.Success)
            throw new InvalidOperationException("C1545 mutant compile error: " + string.Join(";",
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id)));
        bytes.Position = 0;
        var load = new AssemblyLoadContext("c1545-evidence", isCollectible: true);
        try
        {
            var assembly = load.LoadFromStream(bytes);
            var captureType = assembly.GetType("Antiphon.TestSupport.LiveClaude.EvidenceCapture")!;
            var capture = Activator.CreateInstance(captureType, "control-success", "EvidenceIsSanitized", "success", "portable",
                LiveClaudeEvidenceSentinels.Link, LiveClaudeEvidenceSentinels.Screen, LiveClaudeEvidenceSentinels.Secret,
                LiveClaudeEvidenceSentinels.Device, null);
            var projected = assembly.GetType("Antiphon.TestSupport.LiveClaude.LiveClaudeEvidenceSanitizer")!
                .GetMethod("Project")!.Invoke(null, [capture]);
            return (string)projected!.GetType().GetProperty("Text")!.GetValue(projected)!;
        }
        finally { load.Unload(); }
    }

    private static void ProveCallerMutants()
    {
        var inserts = new (string Pc, string Code, string Assertion)[]
        {
            ("PC-38", "Console.WriteLine(\"SCREEN:\\n\" + runner.SnapshotScreen());", "raw-console"),
            ("PC-39", "true.ShouldBeTrue(\"screen:\\n\" + runner.SnapshotScreen());", "raw-assertion"),
            ("PC-40", "throw new SkipTestException(\"screen:\\n\" + runner.SnapshotScreen());", "raw-skip"),
            ("PC-41", "runner.SnapshotScreen().ShouldContain(\"x\");", "raw-actual")
        };
        foreach (var family in new[] { Callers[0], Callers[3], Callers[5] })
        {
            var source = LiveClaudeExecutableAudit.Read(family.Path);
            var method = LiveClaudeExecutableAudit.Method(source, family.Method);
            var body = method.Body ?? throw new InvalidOperationException("C1545 V-6/no-body");
            var close = body.ToString();
            var brace = close.LastIndexOf('}');
            foreach (var insert in inserts)
            {
                if (insert.Pc == "PC-41" && family.Method != Callers[6].Method && family.Method != Callers[5].Method
                    && family.Method != Callers[0].Method && family.Method != Callers[3].Method)
                    continue;
                var mutatedBody = close[..brace] + insert.Code + close[brace..];
                var mutated = source.Replace(close, mutatedBody, StringComparison.Ordinal);
                var parsed = LiveClaudeExecutableAudit.Method(mutated, family.Method).Body!;
                var found = Count([(parsed, family.Method)]);
                var red = insert.Assertion switch
                {
                    "raw-console" => found.Console > 0,
                    "raw-assertion" => found.Assertion > 0,
                    "raw-skip" => found.Skip > 0,
                    _ => found.Actual > 0
                };
                if (!red) throw new InvalidOperationException("C1545 " + insert.Pc + " did not reach expected assertion " + family.Method);
                Console.WriteLine("C1545 " + insert.Pc + " assertion-red; restored-green " + family.Method + " " + insert.Assertion);
            }
        }
    }

    private readonly record struct SinkCounts(int Recognized, int Console, int Assertion, int Skip, int Actual);

    private static SinkCounts Count(IEnumerable<(BlockSyntax Body, string Name)> methods)
    {
        var recognized = 0;
        var rawConsole = 0;
        var rawAssertion = 0;
        var rawSkip = 0;
        var rawActual = 0;
        foreach (var method in methods)
        {
            var tainted = Tainted(method.Body);
            recognized += method.Body.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Count(call => call.Expression.ToString().StartsWith("LiveClaudeEvidence.", StringComparison.Ordinal));
            foreach (var call in method.Body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = call.Expression.ToString();
                var sinks = call.ArgumentList.Arguments.Any(arg => UnsafeText(arg.Expression, tainted))
                    || UnsafeText(call.Expression, tainted);
                if (!sinks) continue;
                if (name.Contains("WriteLine", StringComparison.Ordinal)) rawConsole++;
                else if (name.Contains("SkipTestException", StringComparison.Ordinal)) rawSkip++;
                else if (name.Contains("Should", StringComparison.Ordinal))
                {
                    if (UnsafeText(call.Expression, tainted)) rawActual++;
                    if (call.ArgumentList.Arguments.Any(arg => UnsafeText(arg.Expression, tainted))) rawAssertion++;
                }
            }
            foreach (var thrown in method.Body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                if (thrown.Type.ToString() != "SkipTestException") continue;
                if (thrown.ArgumentList?.Arguments.Any(arg => UnsafeText(arg.Expression, tainted)) == true) rawSkip++;
            }
        }
        return new SinkCounts(recognized, rawConsole, rawAssertion, rawSkip, rawActual);
    }

    private static HashSet<string> Tainted(BlockSyntax body)
    {
        var tainted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var local in body.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (local.Initializer?.Value.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                .Any(call => call.Expression.ToString().EndsWith("SnapshotScreen", StringComparison.Ordinal)) == true)
                tainted.Add(local.Identifier.ValueText);
        }
        return tainted;
    }

    private static bool UnsafeText(ExpressionSyntax expression, HashSet<string> tainted)
    {
        if (expression is InvocationExpressionSyntax call
            && call.Expression.ToString().StartsWith("LiveClaudeEvidence.", StringComparison.Ordinal))
            return false;
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            var textJoin = node is InterpolatedStringExpressionSyntax
                || node is BinaryExpressionSyntax binary && binary.OperatorToken.Text == "+";
            if (textJoin && node.DescendantNodes().Any(inner => CarriesScreen(inner, tainted)))
                return true;
        }
        return ShouldActualIsRaw(expression, tainted);
    }

    private static bool ShouldActualIsRaw(ExpressionSyntax expression, HashSet<string> tainted)
    {
        if (expression is not MemberAccessExpressionSyntax member) return false;
        if (!member.Name.Identifier.ValueText.Contains("Should", StringComparison.Ordinal)) return false;
        return CarriesScreen(member.Expression, tainted);
    }

    private static bool CarriesScreen(SyntaxNode node, HashSet<string> tainted) =>
        node is InvocationExpressionSyntax screen
            && screen.Expression.ToString().EndsWith("SnapshotScreen", StringComparison.Ordinal)
        || node is IdentifierNameSyntax name && tainted.Contains(name.Identifier.ValueText);

    private static string RunToken()
    {
        var context = Context();
        if (context is not null && LiveClaudeEvidenceSanitizer.IsToken(context.RunToken)) return context.RunToken!;
        return "portable";
    }

    private static string? ExportPath() => Context()?.ExportPath;

    private static EvidenceFile? Context()
    {
        var path = Environment.GetEnvironmentVariable("ANTIPHON_LIVE_EVIDENCE_CONTEXT");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        return JsonSerializer.Deserialize<EvidenceFile>(File.ReadAllText(path), JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class EvidenceFile
    {
        [JsonPropertyName("exportPath")] public string? ExportPath { get; set; }
        [JsonPropertyName("runToken")] public string? RunToken { get; set; }
    }
}
