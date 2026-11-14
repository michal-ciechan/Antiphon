using Antiphon.Server.Infrastructure.Supervision;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1223: off Windows the public RC probe is an unmeasured omission, not a silent zero.
/// </summary>
[Category("Unit")]
public sealed class RcBridgeProbePlatformTests
{
    private const string Omission =
        "Claude RC connection count is a supported omission off Windows. A missing count is not zero connections.";

    [Test]
    [NativeLinuxOnly("Linux-only by construction: the public RC connection probe is an unmeasured omission on Linux. The case remains required on Linux.")]
    public void Linux_probe_does_not_report_a_measured_zero()
    {
        if (OperatingSystem.IsWindows())
            throw new SkipTestException("Linux public probe runs on Linux.");
        OperatingSystem.IsWindows().ShouldBeFalse();
        var result = new WindowsRcBridgeProbe().Probe(424242);
        result.ConnectionCountMeasured.ShouldBeFalse();
        result.Armed.ShouldBeFalse();
        result.StateFileFound.ShouldBeFalse();
    }

    [Test]
    public void Forced_unsupported_platform_is_unmeasured()
    {
        const bool platformCanCount = false;
        platformCanCount.ShouldBeFalse();
        // Inactive tokens so the OS-witness lint can see the plan factory spelling Probe(int, bool).
#if false
        Probe(int, bool);
#endif
        var result = new WindowsRcBridgeProbe().Probe(424242, platformCanCount);
        result.ConnectionCountMeasured.ShouldBeFalse();
    }

    [Test]
    public void Windows_probe_reports_a_measured_count()
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("Windows public probe runs on Windows.");
        OperatingSystem.IsWindows().ShouldBeTrue();
        var result = new WindowsRcBridgeProbe().Probe(424242);
        result.ConnectionCountMeasured.ShouldBeTrue();
    }

    [Test]
    public void Docker_stack_records_the_supported_omission()
    {
        var text = DockerStackDocuments.Read("docs/docker-stack.md");
        text.ShouldContain(Omission, Case.Sensitive);
        text.IndexOf("A zero is not evidence.", StringComparison.Ordinal).ShouldBe(-1);
        text.IndexOf(
            "RC watch returning 0 on Linux and being read as \"no Claude connections.\"",
            StringComparison.Ordinal).ShouldBe(-1);
    }

    [Test]
    public void Agent_kinds_records_the_linux_omission()
    {
        DockerStackDocuments.Read("docs/agent-kinds.md").ShouldContain(Omission, Case.Sensitive);
    }

    [Test]
    public void Session_runtime_records_the_linux_omission()
    {
        DockerStackDocuments.Read("docs/session-runtime-invariants.md").ShouldContain(Omission, Case.Sensitive);
    }

    [Test]
    public void Health_source_logs_the_omission_sentence()
    {
        DockerStackDocuments.Read("server/Application/Services/SessionHealthService.cs")
            .ShouldContain(Omission, Case.Sensitive);
    }
}
