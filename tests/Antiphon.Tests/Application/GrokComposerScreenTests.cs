using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0491 V-2. The composer box row decides. The footer is not a prompt.</summary>
[Category("Unit")]
public sealed class GrokComposerScreenTests
{
    [Test]
    [Arguments("empty-measured", GrokComposerState.Empty)]
    [Arguments("empty-trailing-spaces", GrokComposerState.Empty)]
    [Arguments("draft-ascii", GrokComposerState.Draft)]
    [Arguments("draft-marker", GrokComposerState.Draft)]
    [Arguments("null-screen", GrokComposerState.Unreadable)]
    [Arguments("blank-screen", GrokComposerState.Unreadable)]
    [Arguments("overlay-no-prompt", GrokComposerState.Unreadable)]
    [Arguments("prompt-not-last", GrokComposerState.Unreadable)]
    public void C0491_ClassifiesTheComposer(string arm, GrokComposerState expected)
    {
        GrokComposerScreen.Classify(Screen(arm)).ShouldBe(expected, arm);
    }

    private static string? Screen(string arm) => arm switch
    {
        "empty-measured" => Fixture(),
        "empty-trailing-spaces" => WithPrompt(Fixture(), "   "),
        "draft-ascii" => WithPrompt(Fixture(), "GK-DRAFT"),
        "draft-marker" => WithPrompt(Fixture(), "[antiphon-task:"),
        "null-screen" => null,
        "blank-screen" => " \n\t\n",
        "overlay-no-prompt" => "Tasks\n  1. running\n  Esc to close\n",
        "prompt-not-last" => Fixture() + "\nstill on screen\n",
        _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, "unmapped composer row"),
    };

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(
            DelegateScriptRunner.RepoRoot, "tests", "Antiphon.Tests", "Fixtures", "grok-empty-composer.txt"));

    private static string WithPrompt(string screen, string body)
    {
        var lines = screen.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var box = lines[i].IndexOf('\u2502');
            if (box < 0)
                continue;
            var cursor = box + 1;
            while (cursor < lines[i].Length && lines[i][cursor] == ' ')
                cursor++;
            if (cursor >= lines[i].Length || lines[i][cursor] != '>')
                continue;
            var close = lines[i].LastIndexOf('\u2502');
            lines[i] = lines[i][..(cursor + 1)] + " " + body + " " + lines[i][close..];
            break;
        }

        return string.Join('\n', lines);
    }
}
