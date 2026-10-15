using System.Reflection;
using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

// Drives the in-process adapter's raw-response call site without a native shell or provider.
// This is extraction coverage; native startup/delivery/completion remain in LocalShellTests.
[Category("Unit")]
public class CodexAdapterRawResponseTests
{
    [Test]
    [Arguments("Run a\u001b[1;6Hbc?\r\nDone.", "Run abc?", "Done.")]
    [Arguments("Run \u001b[1Cabc?\r\nDone.", "Run abc?", "Done.")]
    [Arguments("Echo\u001b[1;6Hthis?\r\nC907\u001b[1Calpha\u001b[1Cbeta.", "Echo this?", "C907 alpha beta.")]
    public async Task Raw_response_excludes_echo_and_preserves_codex_word_boundaries(
        string raw, string prompt, string expected)
    {
        await using var adapter = new CodexAdapter(Options.Create(new AgentRegistrySettings
        {
            CodexDoneMaxWaitMs = 0,
        }));
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var runner = (PtyAgentRunner)typeof(CodexAdapter).GetField("_runner", fields)!.GetValue(adapter)!;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        await runner.ReadStreamForTestAsync(stream);
        typeof(CodexAdapter).GetField("_started", fields)!.SetValue(adapter, true);
        typeof(CodexAdapter).GetField("_lastPrompt", fields)!.SetValue(adapter, prompt);

        var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
        turn.ResponseText.ShouldBe(expected, "C907-D1-inproc-codex-echo");
        turn.IsAskingQuestion.ShouldBeFalse("C907-D1-inproc-codex-question");
        turn.RawSnapshot.ShouldBe(raw);
        turn.TurnCompleted.ShouldBeFalse("No working lifecycle was supplied by this extraction fixture.");
    }
}
