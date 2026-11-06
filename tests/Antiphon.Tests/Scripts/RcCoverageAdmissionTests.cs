using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcCoverageAdmissionTests
{
    [Test]
    public Task Windows_and_linux_obligations_remain_exact() =>
        ScriptHarness.RunHarnessCaseAsync("test-rc-coverage-admission.ps1", "CoverageAdmission", "", 8,
            "CoverageAdmission separate-method-workflows", "CoverageAdmission source-roster",
            "CoverageAdmission exact-windows-dispositions", "CoverageAdmission linux-retains-native",
            "CoverageAdmission portable-siblings", "CoverageAdmission coverage-sweep",
            "CoverageAdmission required-skips-stay-incomplete",
            "CoverageAdmission class-scope-rejected-by-assertion");
}
