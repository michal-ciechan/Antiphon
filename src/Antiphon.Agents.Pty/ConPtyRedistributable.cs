using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Antiphon.Agents.Pty;

/// <summary>
/// The redistributable pseudoconsole we ship beside the pty binaries, and the rules for finding it.
///
/// <para><b>Why we ship one at all (CARD-0030/CARD-0037).</b> A pseudoconsole is served by a conhost
/// binary, and the one <c>CreatePseudoConsole</c> in <c>kernel32</c> launches is
/// <c>%SystemRoot%\System32\conhost.exe</c> — 10.0.19041.1 on the machine this was measured on, a
/// 2020 build that <b>strips <c>ESC[200~</c>/<c>ESC[201~</c> out of written input before the child
/// sees them</b>. The TUI therefore receives every paste we write as a burst of typing and clips it
/// to one ~1 KB read chunk. Loading <c>CreatePseudoConsole</c> from a modern <c>conpty.dll</c>
/// instead — which spawns the <c>OpenConsole.exe</c> sitting beside it — delivers the markers byte
/// for byte. VS Code (node-pty), the JetBrains IDEs (pty4j) and Windows Terminal all ship this
/// exact pair; it is the normal thing to do.</para>
///
/// <para><b>Provenance.</b> The binaries come from Microsoft's own signed NuGet package
/// <see cref="PackageId"/> <see cref="PackageVersion"/> (<c>github.com/microsoft/terminal</c>, MIT),
/// restored by <c>Antiphon.Agents.Pty.csproj</c> and staged into <see cref="RelativeDirectory"/> of
/// the build output. They are NOT scavenged out of another product's install — the investigation had
/// to do that to get a measurement, and an unidentifiable binary is not a thing to ship.
/// <see cref="VerifyShippedHashes"/> pins both files by SHA-256 so a package bump that changes the
/// binary is a test failure rather than a silent change of the delivery envelope.</para>
///
/// <para>Only <c>win-x64</c> is staged: it is the only architecture whose envelope has been
/// measured. Anywhere else <see cref="TryLocate"/> reports "not shipped" and the caller falls back
/// to the inbox conhost with the existing ceilings in force.</para>
/// </summary>
public static class ConPtyRedistributable
{
    public const string PackageId = "Microsoft.Windows.Console.ConPTY";
    public const string PackageVersion = "1.24.260710001";
    public const string PackageSource =
        "https://api.nuget.org/v3-flatcontainer/microsoft.windows.console.conpty/1.24.260710001/"
        + "microsoft.windows.console.conpty.1.24.260710001.nupkg";

    /// <summary>SHA-256 of <c>runtimes/win-x64/native/conpty.dll</c> in that package (109 920 B).</summary>
    public const string ConPtyDllSha256 = "39fba2713e2495117b1591ae8c32a3b904bea7aa66069cf7815e2844c76d75d8";

    /// <summary>SHA-256 of <c>build/native/runtimes/x64/OpenConsole.exe</c> in that package (1 066 296 B).</summary>
    public const string OpenConsoleSha256 = "b7fd936c2668b87b9ecf7b3366dc6568afc1c6f981874cba3e955a1c35cf8160";

    /// <summary>Points the loader at a directory holding conpty.dll + OpenConsole.exe (tests, ops).</summary>
    public const string DirectoryEnvVar = "ANTIPHON_CONPTY_DIR";

    public const string DllName = "conpty.dll";
    public const string ConsoleHostName = "OpenConsole.exe";

    /// <summary>
    /// Where the build stages the pair, relative to the app base directory. The two files sit SIDE
    /// BY SIDE deliberately: this conpty.dll accepts either <c>&lt;dir&gt;\OpenConsole.exe</c> or
    /// <c>&lt;dir&gt;\x64\OpenConsole.exe</c> and prefers the sibling when both exist (measured), so
    /// one copy in one place is the whole layout.
    /// </summary>
    public static string RelativeDirectory =>
        Path.Combine("conpty", "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());

    /// <summary>
    /// The directories probed, in order. Exposed so a failure can say where it looked.
    /// <see cref="DirectoryEnvVar"/> is EXCLUSIVE when set: pointing it somewhere is an instruction
    /// about which binary to run, and silently falling through to the built-in one would hide both a
    /// typo and a deliberate downgrade test.
    /// </summary>
    public static IReadOnlyList<string> ProbeDirectories() =>
        Environment.GetEnvironmentVariable(DirectoryEnvVar) is { Length: > 0 } custom
            ? [custom]
            : [Path.Combine(AppContext.BaseDirectory, RelativeDirectory)];

    /// <summary>
    /// Finds the shipped conpty.dll, or explains why there is none. A directory that has the DLL but
    /// not its <c>OpenConsole.exe</c> is a MISS, not a hit: conpty.dll silently falls back to the
    /// inbox <c>conhost.exe</c> when it cannot find its console host, which would put us back on the
    /// stripping binary while claiming the modern backend.
    /// </summary>
    public static bool TryLocate(out string? dllPath, out string reason)
    {
        dllPath = null;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            reason = "not Windows — there is no pseudoconsole to redirect";
            return false;
        }

        var probed = ProbeDirectories();
        foreach (var dir in probed)
        {
            var dll = Path.Combine(dir, DllName);
            var host = Path.Combine(dir, ConsoleHostName);
            if (!File.Exists(dll))
                continue;
            if (!File.Exists(host))
            {
                reason = $"{dll} has no sibling {ConsoleHostName} — conpty.dll would fall back to the "
                         + "inbox conhost, which is the binary we are trying to get away from";
                return false;
            }

            dllPath = dll;
            reason = $"{PackageId} {PackageVersion} at {dir}";
            return true;
        }

        reason = $"no {DllName} in [{string.Join("; ", probed)}] "
                 + $"(only win-x64 is staged; this process is {RuntimeInformation.ProcessArchitecture})";
        return false;
    }

    /// <summary>
    /// Confirms the staged files are the bytes recorded above. Used by the contract test — the point
    /// of shipping a known binary is lost if nothing checks which binary shipped.
    /// </summary>
    public static (bool Ok, string Detail) VerifyShippedHashes(string dllPath)
    {
        var dir = Path.GetDirectoryName(dllPath)!;
        var dll = Sha256(dllPath);
        var host = Sha256(Path.Combine(dir, ConsoleHostName));
        var ok = string.Equals(dll, ConPtyDllSha256, StringComparison.OrdinalIgnoreCase)
                 && string.Equals(host, OpenConsoleSha256, StringComparison.OrdinalIgnoreCase);
        return (ok, $"{DllName}={dll} {ConsoleHostName}={host}");
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
