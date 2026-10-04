using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyDiscoveryParserTests
{
    [Test]
    public Task C1044_MultilineDiscovery() => RunCaseAsync(nameof(C1044_MultilineDiscovery), 8,
        "C1044 LF complete inventory", "C1044 LF exact metadata", "C1044 LF dispositions", "C1044 LF sorted digest",
        "C1044 CRLF complete inventory", "C1044 CRLF exact metadata", "C1044 CRLF dispositions", "C1044 CRLF sorted digest");

    [Test]
    public Task C1044_DiscoveryReconciliation() => RunCaseAsync(nameof(C1044_DiscoveryReconciliation), 8,
        "C1044 prefix orphan refuses", "C1044 suffix orphan refuses",
        "C1044 partial cannot borrow state", "C1044 partial cannot borrow identity", "C1044 duplicate refuses",
        "C1044 producer absent output stays absent", "C1044 producer sentinel unchanged",
        "C1044 producer publishes complete multiline inventory");

    [Test]
    public Task C1044_ParserCompatibility() => RunCaseAsync(nameof(C1044_ParserCompatibility), 17,
        "C1044 golden discovery inventory", "C1044 golden discovery metadata", "C1044 golden terminal inventory",
        "C1044 nonterminal updates filtered", "C1044 prefixed envelopes accepted", "C1044 multiline terminal inventory",
        "C1044 discovery JSON roundtrip", "C1044 default OptIn excluded", "C1044 profile OptIn required",
        "C1044 explicit profile exclusion", "C1044 unsupported TUnit refuses", "C1044 unsupported MTP refuses",
        "C1044 compatibility duplicate refuses", "C1044 missing type refuses", "C1044 missing method refuses",
        "C1044 garbage refuses", "C1044 header only refuses");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-tests.ps1", "C1044", caseName, expectedRows, requiredRows);
}
