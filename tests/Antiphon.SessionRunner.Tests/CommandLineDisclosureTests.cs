using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public sealed class CommandLineDisclosureTests
{
    [Test]
    public void Prompt_argument_and_any_long_argument_are_replaced()
    {
        var payload = "rules body line\nsecond";
        var message = $"grok --append-system-prompt \"{payload}\" --session-id abc";
        CommandLineDisclosure.Redact(message).ShouldBe(
            $"grok --append-system-prompt <redacted {payload.Length} chars> --session-id abc");

        var longArg = new string('Z', 201);
        CommandLineDisclosure.Redact($"note {longArg} tail").ShouldBe("note <redacted 201 chars> tail");
        CommandLineDisclosure.Redact("--system-prompt=hi").ShouldBe("--system-prompt=<redacted 2 chars>");
        CommandLineDisclosure.Redact("--rules short").ShouldBe("--rules <redacted 5 chars>");
        CommandLineDisclosure.Redact("--system-prompt-file settings.json").ShouldBe("--system-prompt-file settings.json");
    }

    [Test]
    public void Problem_detail_is_redacted_before_it_is_clipped()
    {
        const string marker = "GROK-RULES-ARGV-SENTINEL-2dec24ba";
        var sentinel = marker + new string('Q', 3000 - marker.Length);
        var padding = string.Join(' ', Enumerable.Repeat("pad", 200));
        var detail = CommandLineDisclosure.ForProblemDetail(
            $"Could not start terminal process grok --append-system-prompt {sentinel} {padding}");

        detail.Length.ShouldBe(CommandLineDisclosure.ProblemDetailMaxChars);
        detail.ShouldEndWith("…");
        detail.ShouldContain("<redacted 3000 chars>");
        detail.ShouldNotContain(marker);
    }
}
