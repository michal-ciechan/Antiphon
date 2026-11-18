using System.Text.RegularExpressions;
using Antiphon.Checkpoints;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// CARD-1981. A checkpoint-tool dotnet child must see a private lease TMPDIR and must not
/// leave the SDK's 6-character directory in that directory or in the parent /tmp.
/// </summary>
[Category("Integration")]
[ParallelLimiter<CheckpointToolSpawnLimit>]
public sealed class C1981LeaseTempDriverTests : CheckpointTestBase
{
    private static readonly Regex LeasePath = new("^/tmp/aslot-[0-9a-f]{8}$", RegexOptions.CultureInvariant);
    private static readonly Regex TopLevelSdk = new("^/tmp/[A-Za-z0-9]{6}$", RegexOptions.CultureInvariant);
    private static readonly Regex NestedSdk = new("^/tmp/aslot-[0-9a-f]{8}/[A-Za-z0-9]{6}$", RegexOptions.CultureInvariant);
    private static readonly Regex Six = new("^[A-Za-z0-9]{6}$", RegexOptions.CultureInvariant);

    [Test]
    [NativeLinuxOnly("CARD-1981 the SDK temp leak was observed on Linux only")]
    [Timeout(60_000)]
    public async Task C1981_dotnet_child_tmpdir_is_removed_with_the_lease()
    {
        var root = TempDir();
        var shim = Path.Combine(root, "dotnet-shim.ps1");
        await File.WriteAllTextAsync(shim, """
            $rootDir = [string]$env:TMPDIR
            if ([string]::IsNullOrWhiteSpace($rootDir)) { $rootDir = '/tmp' }
            $alphabet = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'
            $sdk = ''
            for ($n = 0; $n -lt 8; $n++) {
                $chars = New-Object char[] 6
                for ($c = 0; $c -lt 6; $c++) { $chars[$c] = $alphabet[(Get-Random -Maximum 62)] }
                $candidate = Join-Path $rootDir (-join $chars)
                if (-not (Test-Path -LiteralPath $candidate)) { $sdk = $candidate; break }
            }
            New-Item -ItemType Directory -Path $sdk | Out-Null
            Write-Output ('TMPDIR=' + $env:TMPDIR)
            Write-Output ('TEMP=' + $env:TEMP)
            Write-Output ('TMP=' + $env:TMP)
            Write-Output ('SDKDIR=' + $sdk)
            exit 0
            """);

        var parentBefore = Environment.GetEnvironmentVariable("TMPDIR");
        var before = SixCharTemps();
        string? sdk = null;
        try
        {
            var driver = new ProcessDriver { DotnetShim = shim };
            var result = await driver.RunAsync(new DriverRequest("dotnet", ["build"], root), CancellationToken.None);
            result.ExitCode.ShouldBe(0, result.Stdout + result.Stderr);
            Environment.GetEnvironmentVariable("TMPDIR").ShouldBe(parentBefore);
            var tmpdir = Value(result.Stdout, "TMPDIR=");
            sdk = Value(result.Stdout, "SDKDIR=");
            tmpdir.ShouldMatch(LeasePath.ToString(), "leased dotnet child TMPDIR");
            tmpdir.Length.ShouldBeLessThan(60);
            LeasePath.IsMatch(Value(result.Stdout, "TEMP=")).ShouldBeFalse();
            LeasePath.IsMatch(Value(result.Stdout, "TMP=")).ShouldBeFalse();
            Directory.Exists(sdk).ShouldBeFalse("the lease removes the sdk scratch with the private directory");
            Directory.Exists(tmpdir).ShouldBeFalse();
            SixCharTemps().Except(before).ShouldBeEmpty("parent /tmp gained a six-character directory");
        }
        finally
        {
            RemoveExactSdk(sdk);
        }
    }

    private static string Value(string stdout, string prefix)
    {
        var line = stdout.ReplaceLineEndings("\n").Split('\n').Single(item => item.StartsWith(prefix, StringComparison.Ordinal));
        return line[prefix.Length..];
    }

    private static HashSet<string> SixCharTemps()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dir in Directory.EnumerateDirectories("/tmp"))
        {
            var name = Path.GetFileName(dir);
            if (!Six.IsMatch(name))
                continue;
            var info = new DirectoryInfo(dir);
            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            names.Add(name);
        }
        return names;
    }

    private static void RemoveExactSdk(string? sdk)
    {
        if (string.IsNullOrEmpty(sdk) || (!TopLevelSdk.IsMatch(sdk) && !NestedSdk.IsMatch(sdk)))
            return;
        var info = new DirectoryInfo(sdk);
        if (!info.Exists || info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return;
        if (info.EnumerateFileSystemInfos().Any())
            return;
        info.Delete();
    }
}
