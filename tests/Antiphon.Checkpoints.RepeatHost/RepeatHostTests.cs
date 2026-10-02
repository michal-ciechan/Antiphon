using System.Text.RegularExpressions;
using Antiphon.Checkpoints;
using TUnit.Core;

[assembly: Repeat(4)]
[assembly: RepeatEvidenceAttribute(5)]

namespace Antiphon.Checkpoints.RepeatHost;

public sealed class RepeatHostTests
{
    private readonly Guid _instance = Guid.NewGuid();
    [Before(Assembly)]
    public static void SetUp() => Log("assembly-setup");

    [After(Assembly)]
    public static void TearDown() => Log("assembly-teardown");

    [Before(Test)]
    public void Before() => Log("before:" + _instance);

    [After(Test)]
    public void After() => Log("after:" + _instance);

    [Test]
    public void plain() => Body("plain");

    [Test]
    [Arguments(1, DisplayName = "same")]
    [Arguments(2, DisplayName = "same")]
    public void arguments(int value) => Body("arguments:" + value);

    private void Body(string name)
    {
        var id = TestContext.Current!.Metadata.TestDetails.TestId;
        var ordinal = Regex.Match(id, @"\.(\d+)(?:_inherited\d+)?$").Groups[1].Value;
        Log("body:" + name + ":" + ordinal + ":" + _instance);
        if (Environment.GetEnvironmentVariable("C885_FAIL_ORDINAL") == ordinal && name == "plain")
            throw new InvalidOperationException("deliberate-repeat-failure-ordinal-" + ordinal);
        if (Environment.GetEnvironmentVariable("C885_SKIP_ORDINAL") == ordinal && name == "plain")
            Skip.Test("deliberate-repeat-skip-ordinal-" + ordinal);
    }

    private static void Log(string text)
    {
        var path = Environment.GetEnvironmentVariable("C885_PROBE_LOG");
        if (path is not null) File.AppendAllText(path, text + Environment.NewLine);
    }
}

public sealed class RepeatConflictSentinel
{
    [Test]
    [Repeat(1)]
    public void conflicting_repeat() { }
}
