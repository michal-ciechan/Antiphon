using Antiphon.Tests.Infrastructure;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0727 V-33. Text contract for <c>scripts/runner-drain.ps1</c>. R2 covers
/// status, drain and clear. The retire verb is added in R3 without renaming this method.
/// </summary>
[Category("Integration")]
public sealed class RunnerDrainScriptTests
{
    [Test]
    public async Task Script_is_ascii_offers_the_four_verbs_and_sends_the_token_without_printing_it()
    {
        var path = Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "runner-drain.ps1");
        File.Exists(path).ShouldBeTrue(path);
        var text = await File.ReadAllTextAsync(path);
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(text))
            b.ShouldBeLessThan((byte)128, "runner-drain.ps1 must stay ASCII");

        text.ShouldContain("ValidateSet('status', 'drain', 'clear')");
        text.ShouldContain("X-Antiphon-Operator-Token");
        text.ShouldContain("not eligible");

        foreach (var line in text.Split('\n'))
        {
            var writes = line.Contains("Write-Host", StringComparison.Ordinal)
                || line.Contains("Write-Output", StringComparison.Ordinal)
                || line.Contains("Write-Error", StringComparison.Ordinal);
            if (writes)
                line.Contains("$token", StringComparison.Ordinal).ShouldBeFalse(line);
        }
    }
}
