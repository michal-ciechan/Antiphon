using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-0589 V-7 (S1, D-2). The repo-root <c>Directory.Build.rsp</c> is MSBuild's auto-response
/// file for every project under the root, on every host and for every invoker (a delegate's raw
/// command, the wrappers, the land verifier, the nightly), so worker nodes exit with the build that
/// made them. <c>-maxcpucount</c> stays out of it: CP-1 measured that the SDK's own bare
/// <c>-maxcpucount</c> outranks an auto-response file, so a count there would read as a cap and do
/// nothing; the per-build count comes from the build-slot grant instead.
/// </summary>
[Category("Unit")]
public sealed class DirectoryBuildRspTests
{
    [Test]
    public void The_rsp_is_ascii_and_turns_node_reuse_off()
    {
        var bytes = File.ReadAllBytes(RspPath);
        bytes.Where(b => b > 127).ShouldBeEmpty("Directory.Build.rsp must stay ASCII");

        Switches().ShouldContain("-nodeReuse:false", "MSBuild worker nodes must exit with the build that started them");
    }

    [Test]
    public void The_rsp_carries_no_maxcpucount_because_the_sdk_outranks_it()
    {
        var counts = Switches()
            .Where(s => s.StartsWith("-m:", StringComparison.OrdinalIgnoreCase)
                        || s.Equals("-m", StringComparison.OrdinalIgnoreCase)
                        || s.StartsWith("-maxcpucount", StringComparison.OrdinalIgnoreCase)
                        || s.StartsWith("/m", StringComparison.OrdinalIgnoreCase))
            .ToList();
        counts.ShouldBeEmpty("CP-1 measured an rsp -maxcpucount as ignored; the cap is the grant's -maxcpucount:N");
    }

    private static string RspPath => Path.Combine(DockerStackDocuments.RepoRoot, "Directory.Build.rsp");

    // One switch per line; '#' starts a comment line (MSBuild response-file syntax).
    private static List<string> Switches() =>
        File.ReadAllLines(RspPath)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();
}
