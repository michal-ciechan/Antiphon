using Antiphon.SessionRunner;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0497: hermetic npm Codex layout. Files are zero-byte; nothing is spawned.</summary>
internal sealed class CodexNpmLayout : IDisposable
{
    public CodexNpmLayout(
        string? rootName = null,
        bool siblingNode = true,
        bool js = true,
        bool native = true,
        string? nativeTriple = null,
        string? shimText = null)
    {
        Root = Path.Combine(Path.GetTempPath(), rootName ?? $"c0497-npm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        ShimPath = Path.Combine(Root, "codex.cmd");
        File.WriteAllText(ShimPath, shimText ?? CodexWindowsLaunchPolicy.StockNpmShimText);

        if (siblingNode)
        {
            SiblingNodePath = Path.Combine(Root, "node.exe");
            File.WriteAllBytes(SiblingNodePath, []);
        }

        JsPath = Path.Combine(Root, "node_modules", "@openai", "codex", "bin", "codex.js");
        if (js)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(JsPath)!);
            File.WriteAllBytes(JsPath, []);
        }

        var triple = nativeTriple ?? "x86_64-pc-windows-msvc";
        NativePath = Path.Combine(Root, "node_modules", "@openai", "codex-win32-x64", "vendor", triple, "bin", "codex.exe");
        if (native)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(NativePath)!);
            File.WriteAllBytes(NativePath, []);
        }
    }

    public string Root { get; }
    public string ShimPath { get; }
    public string JsPath { get; }
    public string NativePath { get; }
    public string? SiblingNodePath { get; }

    public string PathNodeDir(string? name = null)
    {
        var dir = Path.Combine(Root, name ?? "path-node");
        Directory.CreateDirectory(dir);
        var node = Path.Combine(dir, "node.exe");
        File.WriteAllBytes(node, []);
        return dir;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Best-effort.
        }
    }
}
