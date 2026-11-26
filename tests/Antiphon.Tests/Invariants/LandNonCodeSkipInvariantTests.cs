using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.LandGraphAudit;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Application;
using static Antiphon.Tests.Application.LandNonCodeSkipTests;

namespace Antiphon.Tests.Invariants;

[Category("Integration")]
[Category("Slow")]
[Category("RepositoryInvariant")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandNonCodeSkipInvariantTests
{
    [Test]
    public async Task C1385_RegistryMatchesNarrowGraph()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var raw = await ReadHeadTreeAsync(root);
        raw.EndsWith('\0').ShouldBeTrue();
        var graph = new LandVerificationBuildGraph();
        var selected = new List<string>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in raw.Split('\0').SkipLast(1))
        {
            var tab = row.IndexOf('\t');
            tab.ShouldBeGreaterThanOrEqualTo(0);
            var metadata = row[..tab].Split(' ');
            var path = row[(tab + 1)..];
            metadata.Length.ShouldBe(3);
            paths.Add(path).ShouldBeTrue(path);
            if (!graph.Includes(path)) continue;
            (metadata[0] is "100644" or "100755").ShouldBeTrue(path);
            metadata[1].ShouldBe("blob", path);
            selected.Add(row);
        }

        var digest = LandVerificationBuildGraph.FingerprintOf(selected);
        digest.ShouldBe(LandVerificationBuildGraph.Fingerprint,
            $"selected definitions={selected.Count}; audited definitions={LandVerificationBuildGraph.AuditedCount}; " +
            "run scripts/antiphon land-graph-audit --check to capture the admission refusal");
        LandVerificationBuildGraph.AuditedCount.ShouldBe(selected.Count);

        var manifestLines = await File.ReadAllLinesAsync(Path.Combine(root, "server", "Application", "Services",
            "LandVerificationBuildGraph.manifest.txt"));
        manifestLines.Length.ShouldBe(2);
        manifestLines[0].ShouldBe(digest);
        int.Parse(manifestLines[1], CultureInfo.InvariantCulture).ShouldBe(selected.Count);

        // The land-graph-audit command in process, on the staged tool as given (the tool and the evaluator are real children).
        var audit = await LandGraphEvaluationTests.RunGraphScriptAsync(root, ["--check", "--tool-path", LandGraphEvaluationTests.ToolDll()]);
        audit.ExitCode.ShouldBe(0, audit.Text);
        audit.StandardOutput.ShouldContain("digest=" + digest);
        audit.StandardOutput.ShouldContain("count=" + selected.Count.ToString(CultureInfo.InvariantCulture));
    }
}
