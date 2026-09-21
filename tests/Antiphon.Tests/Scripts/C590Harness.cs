using System.Diagnostics;
using System.Text.Json;

namespace Antiphon.Tests.Scripts;

internal sealed record C590Run(int ExitCode, string Output, string ResultJson, IReadOnlyList<string> Commands);

internal static class C590Harness
{
    public static string RepoRoot => DockerStackRoot();

    public static async Task<C590Run> RunAsync(
        string scriptName,
        IDictionary<string, object?> manifest,
        IDictionary<string, string>? environment = null,
        params string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "c590-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var evidence = Path.Combine(root, "evidence");
        Directory.CreateDirectory(evidence);
        manifest["evidenceRoot"] = evidence;
        var manifestPath = Path.Combine(root, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));
        var sink = Path.Combine(root, "sink.jsonl");
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["ANTIPHON_C590_SINK"] = sink;
        psi.Environment["ANTIPHON_C590_STUB"] = "1";
        if (environment is not null)
        {
            foreach (var pair in environment)
                psi.Environment[pair.Key] = pair.Value;
        }

        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(Path.Combine(RepoRoot, "scripts", scriptName));
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.ArgumentList.Add("-Manifest");
        psi.ArgumentList.Add(manifestPath);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("pwsh did not start");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var commands = File.Exists(sink) ? await File.ReadAllLinesAsync(sink) : [];
        var resultPath = Path.Combine(evidence, "c590-result.json");
        var result = File.Exists(resultPath) ? await File.ReadAllTextAsync(resultPath) : "";
        return new C590Run(process.ExitCode, stdout + stderr, result, commands);
    }

    public static Dictionary<string, object?> Happy() => new()
    {
        ["sourceRoot"] = RepoRoot,
        ["checkoutRoot"] = RepoRoot,
        ["sourceSha"] = "abc",
        ["runId"] = "run-1",
        ["task"] = new Dictionary<string, object?> { ["readable"] = true, ["sourceLandingOperationId"] = "" },
        ["socket"] = new Dictionary<string, object?> { ["present"] = true, ["gid"] = 999, ["groups"] = new[] { 999 } },
        ["dbProbeOk"] = true,
        ["childExit"] = 0,
        ["reportPresent"] = true,
        ["report"] = new Dictionary<string, object?>
        {
            ["executed"] = 1, ["passed"] = 1, ["failed"] = 0, ["skipped"] = 0,
            ["classes"] = new[] { "A" }, ["runId"] = "run-1",
        },
        ["expectedClasses"] = new[] { "A" },
        ["artifactDigest"] = "digest",
        ["copiedDigest"] = "digest",
    };

    private static string DockerStackRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root was not found");
    }
}
