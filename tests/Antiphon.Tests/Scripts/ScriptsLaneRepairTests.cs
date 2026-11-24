using System.Reflection;
using Antiphon.Checkpoints;
using Antiphon.Nightly;
using Antiphon.Tests.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The portable scripts-lane witnesses, all in process. The bin-* cleanup rows (opaque, owned, replaced) run the
/// cleanup-build-junk command (Antiphon.Nightly.BuildJunkCleanup, unit 21 of the 2026-11-22 plan) with the real HostCleanup
/// ledger as the producer's removal. Since unit 33 (scripts/test-scripts-lane-repairs.ps1 deleted) origin reads real git
/// configs through NightlyRun.OriginUrl, the nightly's origin reader, and state compiles the exact product method
/// CheckpointNativeIo.Move with Roslyn over a modeled File facade, as the driver's Add-Type did.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ScriptsLaneRepairTests
{
    [Test]
    [Arguments("opaque")]
    [Arguments("owned")]
    [Arguments("origin")]
    [Arguments("state")]
    [Arguments("replaced")]
    public void Portable_scripts_lane_witness(string scenario)
    {
        var pass = scenario switch
        {
            "origin" => Origin(),
            "state" => State(),
            _ => InProcess(scenario),
        };
        pass.ShouldStartWith("PASS " + scenario + ":");
    }

    // The origin reader decodes git's quoting and escaping of these values exactly (a string witness even on Linux), and a
    // clone without an origin reads as "".
    private static string Origin()
    {
        var root = Path.Combine(Path.GetTempPath(), "scripts-lane-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Git(root, "init", "--quiet", root);
            foreach (var origin in new[] { @"C:\Antiphon\origin.git", "C:\\a space\\a\"quote\\origin.git", @"\\server\share\origin.git", "https://example.invalid/repo" })
            {
                Git(root, "config", "--local", "remote.origin.url", origin);
                NightlyRun.OriginUrl(root).ShouldBe(origin, "origin mismatch");
            }
            Git(root, "config", "--local", "--unset", "remote.origin.url");
            NightlyRun.OriginUrl(root).ShouldBe("", "missing origin was admitted");
            return "PASS origin: 4 exact strings and missing origin";
        }
        finally
        {
            ReadOnlyDirectory.Delete(root);
        }
    }

    private static void Git(string root, params string[] arguments)
    {
        var result = NightlyNativeGit.Run(root, arguments);
        result.ExitCode.ShouldBe(0, "git " + string.Join(" ", arguments) + ": " + result.Output);
    }

    // The exact product method, with only the OS/native File facade modeled: an overwrite-move refuses an existing destination,
    // and Replace sees one sharing violation. The old state stays whole and the temporary stays until the retried replacement
    // lands; a new destination still publishes through Move (Replace needs an existing file).
    private static string State()
    {
        var root = Path.Combine(Path.GetTempPath(), "scripts-lane-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var witness = CompileNativeMove();
            var mover = Activator.CreateInstance(witness.GetType("ScriptsLaneWitness.NativeMove")!)!;
            var move = mover.GetType().GetMethod("Move")!;
            var file = witness.GetType("ScriptsLaneWitness.File")!;
            int Count(string field) => (int)file.GetField(field)!.GetValue(null)!;
            void Move(string source, string destination)
            {
                try { move.Invoke(mover, [source, destination, true]); }
                catch (TargetInvocationException e) when (e.InnerException is not null) { throw e.InnerException; }
            }

            var path = Path.Combine(root, "state.json");
            var tmp = Path.Combine(root, "state.tmp");
            File.WriteAllText(path, "complete old state");
            File.WriteAllText(tmp, "complete new state");
            var caught = 0;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    Move(tmp, path);
                    break;
                }
                catch (Exception) when (attempt < 5)
                {
                    caught++;
                    File.ReadAllText(path).ShouldBe("complete old state", "partial state during contention");
                    File.Exists(tmp).ShouldBeTrue("temporary lost during contention");
                }
            }
            (caught, Count("Replaces"), Count("Moves")).ShouldBe((1, 2, 0), "atomic replacement/retry path mismatch");
            File.ReadAllText(path).ShouldBe("complete new state", "replacement incomplete");
            File.Exists(tmp).ShouldBeFalse("replacement incomplete");

            var fresh = Path.Combine(root, "fresh.json");
            Move(path, fresh);
            Count("Moves").ShouldBe(1, "initial publication failed");
            File.Exists(path).ShouldBeFalse("initial publication failed");
            return "PASS state: forced sharing violation, old state intact, atomic replacement retried, initial move";
        }
        finally
        {
            ReadOnlyDirectory.Delete(root);
        }
    }

    // CheckpointNativeIo.Move's text, from "public void Move(" to "public void Unlink(", inside a class whose namespace also
    // declares the modeled File and OperatingSystem (they shadow System.IO.File and System.OperatingSystem there).
    private static System.Reflection.Assembly CompileNativeMove()
    {
        var source = File.ReadAllText(Path.Combine(CheckpointFixtures.RepoRoot, "src", "Antiphon.HostCleanup", "Checkpoints", "CheckpointNativeIo.cs"));
        var start = source.IndexOf("    public void Move(", StringComparison.Ordinal);
        var end = start < 0 ? -1 : source.IndexOf("    public void Unlink(", start, StringComparison.Ordinal);
        (start >= 0 && end >= 0).ShouldBeTrue("native Move method not found");
        var method = source[start..end];
        var text = """
            using System;
            using System.IO;
            namespace ScriptsLaneWitness { public sealed class NativeMove {
            """ + "\n" + method + "\n" + """
            }
            public static class OperatingSystem { public static bool IsWindows() => true; }
            public static class File {
                public static int Moves, Replaces;
                public static bool Exists(string path) => System.IO.File.Exists(path);
                public static void Move(string source, string destination, bool overwrite) {
                    Moves++;
                    if (Exists(destination)) throw new UnauthorizedAccessException("modeled Windows replacement contention");
                    System.IO.File.Move(source, destination, overwrite);
                }
                public static void Delete(string path) => System.IO.File.Delete(path);
                public static void Replace(string source, string destination, string backup) {
                    Replaces++;
                    if (System.IO.File.ReadAllText(destination) != "complete old state") throw new Exception("old state damaged");
                    if (Replaces == 1) throw new IOException("forced sharing violation", unchecked((int)0x80070020));
                    System.IO.File.Replace(source, destination, backup);
                }
            }
            }
            """;
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => string.Equals(Path.GetDirectoryName(path), runtime, comparison))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ScriptsLaneWitness" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest))], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics));
        return System.Reflection.Assembly.Load(stream.ToArray());
    }

    // The replaced case proves a foreign directory object survived cleanup. Its directory identity is
    // CheckpointNativeIo.CustodyStat, read in this process: there is no stat child to hide (the PowerShell witness hid
    // coreutils stat from PATH to prove the same, the Windows failure).
    [Test]
    public void Replaced_directory_identity_does_not_shell_out_to_stat()
    {
        var pass = InProcess("replaced");
        pass.ShouldStartWith("PASS replaced:");
        pass.ShouldNotContain("stat");
    }

    // The witness bodies the driver ran for opaque, owned and replaced, on BuildJunkCleanup.Run: a separate process holds no
    // creation records (removeOwned null), a producer that holds them removes through its ledger.
    private static string InProcess(string scenario)
    {
        var root = Path.Combine(Path.GetTempPath(), "scripts-lane-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var context = CheckpointCleanupDefaults.Context;
            string? Removal(string path) => new CheckpointOutputLedger(context).Remove(path, false);
            if (scenario == "replaced")
            {
                var owned = CreateSealedOutput(root);
                var copy = Path.Combine(root, "bin-foreign");
                CopyTree(owned, copy);
                File.Exists(Path.Combine(copy, ".checkpoint-output-ledger.json")).ShouldBeTrue("the copy keeps the ledger");
                var foreignIdentity = Identity(copy);
                var aside = Path.Combine(root, "bin-owned-real");
                Directory.Move(owned, aside);
                Directory.Move(copy, owned);
                Directory.SetLastWriteTime(owned, DateTime.Now.AddDays(-10));
                Directory.SetLastWriteTime(aside, DateTime.Now.AddDays(-10));
                Clean(root, Removal);
                Directory.Exists(owned).ShouldBeTrue("replacement directory was deleted");
                Directory.Exists(aside).ShouldBeTrue("original owned directory was deleted");
                Identity(owned).ShouldBe(foreignIdentity, "replacement identity was not preserved");
                File.ReadAllText(Path.Combine(owned, "payload.txt")).ShouldBe("regenerable");
                File.ReadAllText(Path.Combine(aside, "payload.txt")).ShouldBe("regenerable");
                return "PASS replaced: foreign directory survived a stale custody record";
            }
            var opaque = Path.Combine(root, "bin-private", "nested", "bin-archive", "report.md");
            Directory.CreateDirectory(Path.GetDirectoryName(opaque)!);
            File.WriteAllText(opaque, "irreplaceable");
            Directory.SetLastWriteTime(Path.Combine(root, "bin-private"), DateTime.Now.AddDays(-10));
            if (scenario == "owned")
            {
                var owned = CreateSealedOutput(root);
                var lines = Clean(root, Removal);
                Directory.Exists(owned).ShouldBeFalse("owned output was retained: " + lines);
                lines.ShouldContain("removed: " + owned);
            }
            else
            {
                Clean(root, null).ShouldContain("skip: producer creation records unavailable; outputs left in place");
            }
            File.Exists(opaque).ShouldBeTrue("unowned bin-* bytes were deleted");
            File.ReadAllText(opaque).ShouldBe("irreplaceable", "unowned bytes changed");
            return "PASS " + scenario + ": unowned preserved" + (scenario == "owned" ? ", owned removed" : "");
        }
        finally
        {
            ReadOnlyDirectory.Delete(root);
        }
    }

    // The producer's side: a fresh owned output with a payload, sealed into the ledger.
    private static string CreateSealedOutput(string root)
    {
        var owned = Path.Combine(root, "bin-owned");
        var ownership = SafeWrite.Under(root);
        SafeWrite.CreateFreshDirectory(owned, ownership).ShouldBeNull("create");
        File.WriteAllText(Path.Combine(owned, "payload.txt"), "regenerable");
        OwnedOutputLedger.Seal(owned, DateTimeOffset.UtcNow).ShouldBeNull("seal");
        return owned;
    }

    private static string Clean(string root, Func<string, string?>? removal)
    {
        var output = new StringWriter();
        BuildJunkCleanup.Run(BuildJunkCleanupOptions.Parse(["--repo-root", root, "--skip-if-modified-within-minutes", "0"], null),
            NightlyHost.Production(output, output), new System.IO.Abstractions.FileSystem(), removal is null ? null : path => removal(path)!);
        return output.ToString();
    }

    // Directory identity is the Linux inode or the NTFS file index, read in process.
    private static string Identity(string path)
    {
        var observed = new Antiphon.HostCleanup.Checkpoints.CheckpointNativeIo().CustodyStat(path);
        observed.Kind.ShouldBe("directory");
        observed.Inode.ShouldNotBe(0UL, "directory identity unavailable for " + path);
        return observed.Device + ":" + observed.Inode;
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }
}
