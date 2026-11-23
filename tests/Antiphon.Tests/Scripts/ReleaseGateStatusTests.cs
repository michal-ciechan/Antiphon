using System.Text.RegularExpressions;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 V-7: publication performs no activation. A real publication through the production publisher (C# since unit
/// 14 of 2026-11-22-remaining-ps1-to-csharp.md: Antiphon.Nightly.ReleasePublish, the publish-release command) over the
/// fake remote, and the git calls it actually made: no restart, kill or deploy, no force push, no tag deletion. This
/// replaced C599_NoActivation of scripts/test-release-gate.ps1 (same rows); the four text rows now read the publisher's C#
/// (ReleasePublish.cs and its host ReleaseGateHost.cs) instead of the deleted publish-release.ps1. The status itself is C#
/// since unit 12 (antiphon-scripts release-status); its identity and no-activation rows are <see cref="ReleaseStatusTests"/>.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGateStatusTests
{
    [Test]
    public void C599_NoActivation()
    {
        var rows = new NightlyCaseRows();
        var core = Path.Combine(ReleaseGateFixture.ScriptsRoot, "lib", "Antiphon.Nightly");
        var publisher = File.ReadAllText(Path.Combine(core, "ReleasePublish.cs")) + File.ReadAllText(Path.Combine(core, "ReleaseGateHost.cs"));
        foreach (var forbidden in new[] { "restart-apphost", "Stop-Process", "taskkill", "deploy-local" })
            rows.Check(!publisher.Contains(forbidden, StringComparison.OrdinalIgnoreCase), "C599 NoActivation publish-release performs no " + forbidden);

        // Behavioural, not textual: run a real publication and inspect the git calls it actually made. A prose mention of
        // --force must not pass this, and a run that pushed nothing at all must not pass it either.
        using var fx = ReleaseGateFixture.Create();
        var r = fx.Publish().Result;
        rows.Check(r.ExitCode == 0 && r.Published, "C599 NoActivation control: the publication ran", r.Refusal);
        var pushes = fx.Remote.Trace.Where(line => line.StartsWith("GIT push", StringComparison.Ordinal)).ToArray();
        rows.Check(pushes.Length >= 1, "C599 NoActivation the publication really pushed something", pushes.Length.ToString());
        var forced = pushes.Where(line => line.Contains("--force") || Regex.IsMatch(line, @"push\s+\S+\s+\+")).ToArray();
        rows.Check(forced.Length == 0, "C599 NoActivation no observed push carried a force flag or plus refspec", string.Join(";", forced));
        static bool IsTagDeletion(string call) =>
            Regex.IsMatch(call, @"\btag\s+(-d|--delete)(\s|$)", RegexOptions.IgnoreCase)
            || (Regex.IsMatch(call, @"^GIT push\s", RegexOptions.IgnoreCase)
                && (Regex.IsMatch(call, @"\s:refs/tags/", RegexOptions.IgnoreCase) || Regex.IsMatch(call, @"\s(--delete|-d)(\s|$)", RegexOptions.IgnoreCase)));
        rows.Check(IsTagDeletion("GIT tag -d v2026.10.15.1") && IsTagDeletion("GIT push origin :refs/tags/v2026.10.15.1") && IsTagDeletion("GIT push origin --delete v2026.10.15.1")
            && !IsTagDeletion("GIT push origin refs/antiphon-release-push/fixture/*:refs/tags/*"), "C599 NoActivation deletion matcher distinguishes a populated source refspec");
        var deletes = fx.Remote.Trace.Where(IsTagDeletion).ToArray();
        rows.Check(deletes.Length == 0, "C599 NoActivation no observed call deleted or re-created a tag", string.Join(";", deletes));
        rows.ShouldAllPass(9,
            "C599 NoActivation publish-release performs no restart-apphost",
            "C599 NoActivation control: the publication ran",
            "C599 NoActivation the publication really pushed something",
            "C599 NoActivation no observed push carried a force flag or plus refspec",
            "C599 NoActivation no observed call deleted or re-created a tag",
            "C599 NoActivation deletion matcher distinguishes a populated source refspec");
    }
}
