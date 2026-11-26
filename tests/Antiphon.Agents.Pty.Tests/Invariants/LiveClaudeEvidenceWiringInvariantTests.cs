using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.TestSupport.LiveClaude;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TUnit.Core;
using Antiphon.Agents.Pty.Tests;
using static Antiphon.Agents.Pty.Tests.LiveClaudeEvidenceWiringTests;

namespace Antiphon.Agents.Pty.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
[Timeout(180_000)]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveClaudeEvidenceWiringInvariantTests
{
    [Test]
    public void CrlfCheckoutCountsSanitizerAnchorsOnce()
    {
        var original = LiveClaudeExecutableAudit.Read("tests/Shared/LiveClaude/LiveClaudeEvidenceSanitizer.cs");
        var crlf = ToCrlf(original);
        var pc37 = Pc37Anchor();
        var identity = IdentityAnchor();
        var pc37Count = LiveClaudeExecutableAudit.CountSourceAnchor(crlf, pc37);
        var identityCount = LiveClaudeExecutableAudit.CountSourceAnchor(crlf, identity);
        if (LiveClaudeExecutableAudit.CountSourceAnchor(original, pc37) != 1
            || pc37Count != 1
            || LiveClaudeExecutableAudit.CountSourceAnchor(original, identity) != 1
            || identityCount != 1)
            throw new InvalidOperationException("C1545 CRLF anchor count pc37=" + pc37Count + " identity=" + identityCount);
        var removedMarker = crlf.Replace(ToCrlf(pc37), "var marker = \"\";", StringComparison.Ordinal);
        var removedIdentity = crlf.Replace(ToCrlf(identity), "if (false) { }", StringComparison.Ordinal);
        if (LiveClaudeExecutableAudit.CountSourceAnchor(removedMarker, pc37) != 0)
            throw new InvalidOperationException("C1545 CRLF PC-37 removal still matched");
        if (LiveClaudeExecutableAudit.CountSourceAnchor(removedIdentity, identity) != 0)
            throw new InvalidOperationException("C1545 CRLF identity removal still matched");
    }

    [Test]
    public void CanarySinksCannotBypassSanitizer()
    {
        var counts = Count(Callers.Select(caller =>
            LiveClaudeExecutableAudit.Method(LiveClaudeExecutableAudit.Read(caller.Path), caller.Method)));
        if (counts.Console != 0)
            throw new InvalidOperationException("C1545 V-6/raw-console unprotectedSuccessSinkCount");
        if (counts.Assertion != 0)
            throw new InvalidOperationException("C1545 V-6/raw-assertion unprotectedAssertionSinkCount");
        if (counts.Skip != 0)
            throw new InvalidOperationException("C1545 V-6/raw-skip unprotectedSkipSinkCount");
        if (counts.Actual != 0)
            throw new InvalidOperationException("C1545 V-6/raw-actual rawAssertionOperandCount");
        if (counts.File != 0)
            throw new InvalidOperationException("C1545 V-6/raw-file unprotectedFileSinkCount");
        if (counts.Process != 0)
            throw new InvalidOperationException("C1545 V-6/raw-process unprotectedProcessSinkCount");
        if (counts.Escape != 0)
            throw new InvalidOperationException("C1545 V-6/raw-escape unprotectedEscapeSinkCount");
        if (counts.Recognized == 0)
            throw new InvalidOperationException("C1545 V-6/raw-console unprotectedSuccessSinkCount");
        var menu = LiveClaudeExecutableAudit.Method(
            LiveClaudeExecutableAudit.Read(Callers[6].Path), Callers[6].Method).ToString();
        if (!menu.Contains("LiveClaudeEvidence.Success", StringComparison.Ordinal)
            || !menu.Contains("LiveClaudeEvidence.Assertion", StringComparison.Ordinal)
            || !menu.Contains("LiveClaudeEvidence.Skip", StringComparison.Ordinal))
            throw new InvalidOperationException("C1545 V-6/raw-console unprotectedSuccessSinkCount");
        ProveCallerMutants();
    }

    [Test]
    public void Sanitizer_source_anchors_are_executable()
    {
        var project = typeof(LiveClaudeExecutable).Assembly.GetType(
            "Antiphon.TestSupport.LiveClaude.LiveClaudeEvidenceSanitizer")?.GetMethod("Project");
        if (project is null)
            throw new InvalidOperationException("C1545 V-5/entry required sanitizer is absent");
        ProveMutants();
    }
}
