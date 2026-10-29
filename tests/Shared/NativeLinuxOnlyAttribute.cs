using TUnit.Core;

namespace Antiphon.TestSupport;

/// <summary>
/// CARD-1613. Skips unless this process is Linux. The reason is the test's current Windows skip text.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class NativeLinuxOnlyAttribute : SkipAttribute
{
    public NativeLinuxOnlyAttribute(string reason)
        : base(RequireReason(reason))
    {
    }

    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(!OperatingSystem.IsLinux());

    private static string RequireReason(string reason)
    {
        if (string.IsNullOrEmpty(reason))
            throw new ArgumentException("A NativeLinuxOnly reason must be nonempty.", nameof(reason));
        return reason;
    }
}
