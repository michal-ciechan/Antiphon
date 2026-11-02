using System.Text.RegularExpressions;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1659. HostJqFixture runs a private copy of deploy-server2.ps1, which fails at its first
// missing dot-source. The carried list must cover the scripts' own dot-source closure.
[Category("Unit")]
public sealed class HostJqFixtureClosureTests
{
    private const string Entry = "scripts/deploy-server2.ps1";

    [Test]
    public void C1659_Fixture_carries_every_dot_sourced_script()
    {
        var closure = ScriptDotSourceClosure.Of(Entry);
        // The parser must see the known edges, or an empty closure would pass vacuously.
        foreach (var known in new[] { "scripts/c590-real.ps1", "scripts/c1008-host-proof.ps1", "scripts/c1609-host-proof.ps1",
            "scripts/c1609-read-packet.ps1", "scripts/lib/runner-operator-token.ps1" })
            closure.ShouldContain(known, "dot-source closure of " + Entry);
        var missing = closure.Where(x => x != Entry).Except(HostJqFixture.CarriedScripts, StringComparer.Ordinal).ToArray();
        missing.ShouldBeEmpty("HostJqFixture.CarriedScripts lacks dot-sourced scripts");
    }

    [Test]
    public void C1659_Closure_refuses_an_unrecognised_dot_source_form()
    {
        Should.Throw<InvalidOperationException>(() => ScriptDotSourceClosure.Parse("scripts/x.ps1", ". \"$PSScriptRoot/y.ps1\"\n"))
            .Message.ShouldContain("scripts/x.ps1:1");
        ScriptDotSourceClosure.Parse("scripts/x.ps1", "    . (Join-Path $PSScriptRoot 'lib/y.ps1')\n$a = 1\n")
            .ShouldBe(["scripts/lib/y.ps1"]);
    }
}

// Follows `. (Join-Path $PSScriptRoot '<relative>')` lines transitively. Any other dot-source
// form refuses, so a new spelling cannot drop out of the closure silently.
internal static class ScriptDotSourceClosure
{
    private static readonly Regex AnyDotSource = new(@"^\s*\.\s+\S", RegexOptions.CultureInvariant);
    private static readonly Regex JoinPath = new(@"^\s*\.\s+\(Join-Path\s+\$PSScriptRoot\s+'(?<path>[^']+)'\)\s*$", RegexOptions.CultureInvariant);

    internal static string[] Of(string entry)
    {
        var seen = new List<string> { entry };
        for (var i = 0; i < seen.Count; i++)
            foreach (var next in Parse(seen[i], File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, seen[i]))))
                if (!seen.Contains(next, StringComparer.Ordinal)) seen.Add(next);
        return seen.ToArray();
    }

    internal static string[] Parse(string script, string text)
    {
        var directory = script[..(script.LastIndexOf('/') + 1)];
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var found = new List<string>();
        for (var n = 0; n < lines.Length; n++)
        {
            if (!AnyDotSource.IsMatch(lines[n])) continue;
            var match = JoinPath.Match(lines[n]);
            if (!match.Success) throw new InvalidOperationException("unrecognised dot-source at " + script + ":" + (n + 1) + ": " + lines[n].Trim());
            found.Add(directory + match.Groups["path"].Value);
        }
        return found.ToArray();
    }
}
