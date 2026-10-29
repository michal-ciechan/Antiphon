using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NativeLinuxPolicyMatcherParityTests
{
    private const string Exclusions = """
        [
          {"class":"PhoneHomeMirrorJoinTests","methods":[]},
          {"class":"Outer+Inner","methods":["Nested"]},
          {"class":"GenericProbe`1","methods":["GenericCase"]},
          {"class":"CaseTwin","methods":["Case"]},
          {"class":"OverloadProbe","methods":["Case"]},
          {"class":"Ma\u00dfProbe","methods":[]},
          {"class":"CtorSuffix","methods":["Runs"]}
        ]
        """;

    [Test]
    [Timeout(60_000)]
    public async Task Matcher_agrees_with_nightly_exclusion_predicate(CancellationToken cancellationToken)
    {
        var nodes = new (string ClassName, string Method, bool Excluded)[]
        {
            ("Antiphon.Tests.Application.PhoneHomeMirrorJoinTests", "Any", true),
            ("Other.Namespace.PhoneHomeMirrorJoinTests", "Sibling", true),
            ("Other.Namespace.PortableSibling", "Runs", false),
            ("Ns.Outer+Inner", "Nested", true),
            ("Ns.Outer+Inner", "Portable", false),
            ("Ns.GenericProbe`1", "GenericCase", true),
            ("Ns.GenericProbe`1[[System.Int32]]", "GenericCase", false),
            ("casetwin", "Case", true),
            ("CaseTwin", "Other", false),
            ("OverloadProbe", "Case", true),
            ("OverloadProbe", "case", true),
            ("OverloadProbe", "Sibling", false),
            ("Ns.Ma\u00dfProbe", "Runs", true),
            ("CtorSuffix(System.Int32)", "Runs", true),
            ("CtorSuffix(System.Int32)", "Other", false),
            ("Ns.CtorSuffix", "Runs", true),
        };

        using var document = JsonDocument.Parse(Exclusions);
        var exclusions = document.RootElement;
        var root = RepoRoot();
        var work = Directory.CreateTempSubdirectory("c1613-parity-").FullName;
        var script = Path.Combine(work, "parity.ps1");
        File.WriteAllText(script, Script(nodes), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = work,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(script);
            start.ArgumentList.Add(root);
            process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var text = await stdout;
            var error = await stderr;
            process.ExitCode.ShouldBe(0, error + text);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            lines.Length.ShouldBe(nodes.Length, text);
            for (var i = 0; i < nodes.Length; i++)
            {
                var expected = NativeLinuxPolicyWitness.Excluded(nodes[i].ClassName, "", nodes[i].Method, exclusions);
                expected.ShouldBe(nodes[i].Excluded, nodes[i].ClassName + " " + nodes[i].Method);
                lines[i].ShouldBe(nodes[i].ClassName + "|" + nodes[i].Method + "|" + (expected ? "True" : "False"), text);
            }
        }
        finally
        {
            if (process is { HasExited: false })
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }
            process?.Dispose();
            if (Directory.Exists(work))
                Directory.Delete(work, recursive: true);
        }
    }

    private static string Script((string ClassName, string Method, bool Excluded)[] nodes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("param([string]$Repo)");
        builder.AppendLine("$ErrorActionPreference = 'Stop'");
        builder.AppendLine(". (Join-Path $Repo 'scripts/lib/nightly-common.ps1')");
        builder.AppendLine(". (Join-Path $Repo 'scripts/lib/nightly-policy.ps1')");
        builder.AppendLine(". (Join-Path $Repo 'scripts/lib/nightly-coverage.ps1')");
        builder.AppendLine("$rows = @(");
        builder.AppendLine("  [pscustomobject]@{ Class = 'PhoneHomeMirrorJoinTests'; Methods = @() }");
        builder.AppendLine("  [pscustomobject]@{ Class = 'Outer+Inner'; Methods = @('Nested') }");
        builder.AppendLine("  [pscustomobject]@{ Class = 'GenericProbe`1'; Methods = @('GenericCase') }");
        builder.AppendLine("  [pscustomobject]@{ Class = 'CaseTwin'; Methods = @('Case') }");
        builder.AppendLine("  [pscustomobject]@{ Class = 'OverloadProbe'; Methods = @('Case') }");
        builder.AppendLine("  [pscustomobject]@{ Class = 'MaßProbe'; Methods = @() }");
        builder.AppendLine("  [pscustomobject]@{ Class = 'CtorSuffix'; Methods = @('Runs') }");
        builder.AppendLine(")");
        foreach (var node in nodes)
        {
            var cls = node.ClassName.Replace("'", "''", StringComparison.Ordinal);
            var method = node.Method.Replace("'", "''", StringComparison.Ordinal);
            builder.AppendLine($"$hit = Test-NightlyProfileNodeExcluded ([pscustomobject]@{{ ClassName = '{cls}'; Type = ''; Method = '{method}' }}) $rows");
            builder.AppendLine($"Write-Output ('{cls}|{method}|' + $hit)");
        }

        return builder.ToString();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "scripts", "lib", "nightly-policy.ps1")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("scripts/lib/nightly-policy.ps1 not found");
    }
}
