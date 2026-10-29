using System.Reflection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.TestSupport;

[Category("Unit")]
public sealed class NativeLinuxPolicyWitnessTests
{
    [Test]
    public void Policy_rows_require_pinned_attribute()
    {
        var report = InspectHost();
        report.Success.ShouldBeTrue(report.Text);
    }

    [Test]
    public void Pinned_attribute_contract_matches_native_platform()
    {
        var assembly = typeof(NativeLinuxPolicyWitnessTests).Assembly;
        var checkout = File.ReadAllBytes(Path.Combine(RepoRoot(), "tests", "Shared", "NativeLinuxOnlyAttribute.cs"));
        var embedded = NativeLinuxPolicyWitness.ReadResource(assembly, NativeLinuxPolicyWitness.AttributeResourceName);
        embedded.ShouldNotBeNull();
        embedded.ShouldBe(checkout);
        NativeLinuxPolicyWitness.Sha256Utf8(checkout).ShouldBe(NativeLinuxPolicyWitness.AttributeSha256);

        var attribute = typeof(NativeLinuxOnlyAttribute);
        attribute.IsSealed.ShouldBeTrue();
        attribute.BaseType.ShouldBe(typeof(SkipAttribute));
        var usage = attribute.GetCustomAttribute<AttributeUsageAttribute>().ShouldNotBeNull();
        usage.ValidOn.ShouldBe(AttributeTargets.Class | AttributeTargets.Method);
        usage.AllowMultiple.ShouldBeFalse();
        usage.Inherited.ShouldBeFalse();
        attribute.Assembly.ShouldBe(assembly);

        var skip = new NativeLinuxOnlyAttribute("pin");
        skip.ShouldSkip(null!).GetAwaiter().GetResult().ShouldBe(!OperatingSystem.IsLinux());

        var baseline = NativeLinuxPolicyWitness.ReadResource(assembly, NativeLinuxPolicyWitness.BaselineResourceName);
        baseline.ShouldNotBeNull();
        var checkoutBaseline = File.ReadAllBytes(Path.Combine(RepoRoot(), "tests", "Shared", "NativeLinuxWindowsBaseline.json"));
        baseline.ShouldBe(checkoutBaseline);
    }

    [Test]
    public void Markers_do_not_expand_policy_exclusions()
    {
        var report = InspectHost();
        report.Diagnostics.Any(item => item.Code == "reverse-coverage").ShouldBeFalse(report.Text);
    }

    static InspectionReport InspectHost()
    {
        var assembly = typeof(NativeLinuxPolicyWitnessTests).Assembly;
        var suite = NativeLinuxPolicyWitness.KnownSuite(assembly).ShouldNotBeNull();
        var root = RepoRoot();
        var policy = File.ReadAllText(Path.Combine(root, "tests", "test-execution-policy.json"));
        var baseline = File.ReadAllText(Path.Combine(root, "tests", "Shared", "NativeLinuxWindowsBaseline.json"));
        return NativeLinuxPolicyWitness.Inspect(new InspectionRequest(assembly, suite, policy, baseline));
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Antiphon.sln was not found from " + AppContext.BaseDirectory);
    }
}
