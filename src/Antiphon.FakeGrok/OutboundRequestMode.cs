using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.FakeGrok;

/// <summary>A test-only tool invocation for the real outbound request contract.</summary>
internal static class OutboundRequestMode
{
    private static readonly Regex RequestPath = new(
        @"Read the immutable request JSON at: (?<path>[^\r\n]+)", RegexOptions.Compiled);
    private static readonly Regex BriefPath = new(
        @"Read it in full before you do anything else: '(?<path>[^']+)'", RegexOptions.Compiled);

    public static bool TryRun(string prompt, out string result, out bool succeeded)
    {
        result = string.Empty;
        succeeded = false;
        var tool = Environment.GetEnvironmentVariable("ANTIPHON_FAKE_OUTBOUND_TOOL");
        if (string.IsNullOrWhiteSpace(tool) || !prompt.StartsWith("[antiphon-task:", StringComparison.Ordinal)
            || prompt.Length < "[antiphon-task:".Length + 8)
            return false;
        var match = RequestPath.Match(prompt);
        if (!match.Success && BriefPath.Match(prompt) is { Success: true } brief)
        {
            var path = Path.GetFullPath(brief.Groups["path"].Value);
            var taskId = prompt["[antiphon-task:".Length..("[antiphon-task:".Length + 8)];
            if (Path.GetFileName(Path.GetDirectoryName(path)) == ".antiphon"
                && Path.GetFileName(path) == $"task-{taskId}-brief.md"
                && File.Exists(path))
                match = RequestPath.Match(File.ReadAllText(path));
        }
        if (!match.Success)
            return false;
        try
        {
            var requestPath = Path.GetFullPath(match.Groups["path"].Value.Trim());
            var requestDirectory = Path.GetDirectoryName(requestPath)!;
            using var request = JsonDocument.Parse(File.ReadAllBytes(requestPath));
            var root = request.RootElement;
            var deliveryId = root.GetProperty("deliveryId").GetGuid();
            var sourceFiles = root.GetProperty("sourceFiles").EnumerateArray()
                .Select(source => new
                {
                    Path = source.GetProperty("originalRelativePath").GetString()!,
                    File = "input/" + source.GetProperty("localName").GetString()!,
                }).ToArray();
            if (sourceFiles.Length == 0)
            {
                sourceFiles = root.GetProperty("attachments").EnumerateArray()
                    .Where(attachment => attachment.GetProperty("name").GetString()!
                        .EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    .Select(attachment => new
                    {
                        Path = attachment.GetProperty("name").GetString()!,
                        File = "input/" + attachment.GetProperty("localName").GetString()!,
                    }).ToArray();
            }
            if (sourceFiles.Length == 0)
                throw new InvalidDataException("The frozen request contains no Markdown sources.");
            foreach (var source in sourceFiles)
                if (!File.Exists(Path.Combine(requestDirectory, source.File)))
                    throw new FileNotFoundException("The frozen source file is absent.", source.File);

            if (Environment.GetEnvironmentVariable("ANTIPHON_FAKE_OUTBOUND_TOOL_GATE") is { Length: > 0 } gate)
            {
                File.WriteAllText(gate + ".held", deliveryId.ToString("D"));
                while (!File.Exists(gate + ".release"))
                    Thread.Sleep(25);
            }

            var output = Path.Combine(requestDirectory, "output");
            Directory.CreateDirectory(output);
            if (tool == "fixture:pdf")
            {
                var bytes = "%PDF-1.4 running converter fixture\n"u8.ToArray();
                File.WriteAllBytes(Path.Combine(output, "combined.pdf"), bytes);
                WriteOutputManifest(output, deliveryId, bytes);
                result = $"Converted {sourceFiles.Length} frozen Markdown sources in the running fixture.";
                succeeded = true;
                return true;
            }
            var toolManifest = Path.Combine(requestDirectory, "pdf-input.json");
            File.WriteAllText(toolManifest, JsonSerializer.Serialize(new
            {
                version = 1, title = "Four source documents",
                documents = sourceFiles.Select(s => new { path = s.Path, file = s.File }),
            }));
            var pdfPath = Path.Combine(output, "combined.pdf");
            var start = new ProcessStartInfo(tool.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : tool)
            {
                WorkingDirectory = requestDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (tool.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(tool);
            start.ArgumentList.Add("--manifest");
            start.ArgumentList.Add(toolManifest);
            start.ArgumentList.Add("--output");
            start.ArgumentList.Add(pdfPath);
            if (Environment.GetEnvironmentVariable("ANTIPHON_FAKE_OUTBOUND_BROWSER") is { Length: > 0 } browser)
            {
                start.ArgumentList.Add("--browser-path");
                start.ArgumentList.Add(browser);
            }
            start.ArgumentList.Add("--timeout-seconds");
            start.ArgumentList.Add("60");
            using var child = Process.Start(start) ?? throw new IOException("The PDF tool did not start.");
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(90_000))
            {
                child.Kill(entireProcessTree: true);
                throw new TimeoutException("The PDF tool exceeded its 90-second fixture deadline.");
            }
            Task.WaitAll(stdout, stderr);
            if (child.ExitCode != 0 || !File.Exists(pdfPath))
                throw new InvalidDataException($"The PDF tool exited {child.ExitCode}: {stderr.Result}");
            var bytes = File.ReadAllBytes(pdfPath);
            if (bytes.Length == 0)
                throw new InvalidDataException("The PDF tool produced an empty file.");
            WriteOutputManifest(output, deliveryId, bytes);
            File.WriteAllText(Path.Combine(output, "fakegrok-tool-evidence.json"), JsonSerializer.Serialize(new
            {
                deliveryId, tool, argumentList = start.ArgumentList.ToArray(), exitCode = child.ExitCode,
                sourceCount = sourceFiles.Length, pdfSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            }));
            result = $"Converted {sourceFiles.Length} frozen Markdown sources with the standalone PDF tool.";
            succeeded = true;
        }
        catch (Exception ex)
        {
            result = "Outbound tool failed: " + ex.GetType().Name + ": " + ex.Message;
        }
        return true;
    }

    private static void WriteOutputManifest(string output, Guid deliveryId, byte[] bytes) =>
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
        {
            version = 1, deliveryId, disposition = "converted",
            files = new[] { new { path = "combined.pdf", name = "combined.pdf",
                mime = "application/pdf", length = bytes.Length,
                sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) } },
        }));
}
