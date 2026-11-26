using System.Diagnostics;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1545 S1a. Ordinary Unit roster for the six live identities.
/// Classification is Unit, not Integration: the method is parameterless inventory
/// and does not host a session. Matrices stay inside this one method.
/// The gate matrix (W2-nightly S23, LiveLaneGateScan.cs), the roster (W2-nightly S24, LiveLaneRoster.cs) and the
/// census witnesses (W2-nightly S25, LiveLaneCensus.cs) run in-process; the roster still lists this assembly's tests in
/// a child (dotnet exec --list-tests), and the census witnesses run git in scratch under .antiphon/live-lane-capture.
/// </summary>
[Category("Unit")]
public sealed class LiveLaneInventoryTests
{

    /// <summary>CARD-1645 item 2: a declared additive identity is project, namespace, class and method together.</summary>
    [Test]
    public void CensusAdditiveIdentity()
    {
        var failures = LiveLaneCensus.AdditiveIdentityWitness(LiveLaneProcess.FindRepo());
        failures.ShouldBeEmpty(string.Join('\n', failures));
    }

    /// <summary>CARD-1645 items 1 and 4: the pre-S1 base must be on the landing target; a later tree is refused.</summary>
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void CensusTargetScope()
    {
        var failures = LiveLaneCensus.TargetScopeWitness(LiveLaneProcess.FindRepo());
        failures.ShouldBeEmpty(string.Join('\n', failures));
    }

    internal static class LiveLaneProcess
    {
        /// <summary>
        /// The roster case in-process (LiveLaneRoster.RunCase, the old ExactCoreRoster / ExactPtyCoreRoster of the
        /// driver): the assembly is listed into .antiphon/live-lane-capture under the listing's own watchdog (160 s).
        /// </summary>
        public static LiveLaneCaseResult Roster(string caseName, string project, string assembly)
        {
            var repo = FindRepo();
            var clock = Stopwatch.StartNew();
            assembly.ShouldNotBeNullOrWhiteSpace("C1545 assembly-location");
            var result = LiveLaneRoster.RunCase(repo, project, assembly);
            var phase = ReadPhase(repo);
            result.ExitCode.ShouldBe(0, $"C1545 {caseName} discovery exit error={result.Error} elapsedMs={clock.ElapsedMilliseconds} phase={phase}");
            result.DiscoveryOk.ShouldBeTrue($"C1545 {caseName} discoveryOk error={result.Error} elapsedMs={clock.ElapsedMilliseconds} phase={phase}");
            return result;
        }

        private static string ReadPhase(string repo)
        {
            var path = Path.Combine(repo, ".antiphon", "live-lane-capture", "phase.log");
            if (!File.Exists(path))
                return "";
            var text = File.ReadAllText(path).Trim();
            return text.Length <= 800 ? text : text[^800..];
        }

        internal static string FindRepo()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), Path.GetDirectoryName(typeof(LiveLaneInventoryTests).Assembly.Location) })
            {
                if (string.IsNullOrWhiteSpace(start))
                    continue;
                var dir = new DirectoryInfo(start);
                while (dir is not null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }

            throw new InvalidOperationException("C1545 repo-root-missing");
        }
    }
}
