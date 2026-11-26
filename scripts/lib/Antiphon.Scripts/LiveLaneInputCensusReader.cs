// The live lane's census read (W2 slice S21): Read-Census of the deleted scripts/test-live-lane-run.ps1, with this process's
// compiled policy reader and census (the nightly core). It is the reader the isolated Antiphon.LiveLane project's
// LiveLaneInputDigest takes as a delegate, so that project references no nightly code; the CLI's live-lane-input-digest
// --check binds it, and Antiphon.Tests compiles this file in (LiveLaneRunnerTests). ASCII only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Antiphon.LiveLane;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    /// <summary>
    /// The live lane's census read (Read-Census of the deleted test-live-lane-run.ps1) with this process's compiled policy
    /// reader and census: the root's discovery.diag parsed, the sentinels added, the rc/antiphon/linux exclusions of the
    /// root's policy, the profile-aware census. The pin covers these sources, so a checkout that matches the pin reads
    /// with the pinned code.
    /// </summary>
    internal static class LiveLaneInputCensusReader
    {
        internal static LiveLaneInputCensus Read(string root, IReadOnlyList<LiveLaneSentinel> sentinels)
        {
            var discovery = NightlyDiagnosticLog.Read(Path.Combine(root, LiveLaneInputDigest.DiscoveryPath), "discovery");
            var policy = NightlyPolicy.Read("", Path.Combine(root, LiveLaneInputDigest.PolicyPath));
            var nodes = PsValue.AsArray(PsValue.Prop(discovery, "Nodes")).ToList();
            foreach (var sentinel in sentinels ?? Array.Empty<LiveLaneSentinel>())
                nodes.Add(new Hashtable(StringComparer.OrdinalIgnoreCase)
                {
                    ["Uid"] = sentinel.Uid, ["ClassName"] = sentinel.ClassName, ["Method"] = sentinel.Method,
                    ["Categories"] = sentinel.Categories.Cast<object>().ToArray()
                });
            var exclusions = NightlyPolicy.ProfileExclusions(policy.Object, "rc", "antiphon", "linux").Cast<object>().ToArray();
            var census = NightlyCensus.Census(nodes.ToArray(), exclusions, true);
            static IEnumerable<string> Uids(object rows) => PsValue.AsArray(rows).Select(row => PsValue.Str(PsValue.Prop(row, "uid")));
            return new LiveLaneInputCensus(policy.Hash, Uids(PsValue.Prop(census, "Required")), Uids(PsValue.Prop(census, "Excluded")));
        }
    }
}
