using System.Diagnostics;
using System.Text.Json;

namespace Antiphon.Tests.TestHelpers;

internal sealed class ManifestPublicationResult
{
    public ManifestPublicationResult(bool ok, string reason)
    {
        Ok = ok;
        Reason = reason ?? "";
    }

    public bool Ok { get; }

    public string Reason { get; }
}

internal static class ManifestPublicationFixture
{
    public static async Task<ManifestPublicationResult> Run(
        CancellationToken cancellationToken,
        string caseName,
        string work,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        Directory.CreateDirectory(work);
        var harness = Path.Combine(
            C1514Support.RepoRoot(),
            "tests",
            "Antiphon.Tests",
            "TestHelpers",
            "ManifestPublicationHarness.ps1");
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = work,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(harness);
        start.ArgumentList.Add("-Case");
        start.ArgumentList.Add(caseName);
        start.ArgumentList.Add("-Work");
        start.ArgumentList.Add(work);
        start.ArgumentList.Add("-Repo");
        start.ArgumentList.Add(C1514Support.RepoRoot());
        if (environment is not null)
        {
            foreach (var pair in environment)
                start.Environment[pair.Key] = pair.Value;
        }

        var ran = await C1514Support.RunProcess(start, cancellationToken);
        var resultPath = Path.Combine(work, "result.json");
        if (!File.Exists(resultPath))
        {
            return new ManifestPublicationResult(false, "no result.json exit=" + ran.ExitCode + " " + Trim(ran.Text));
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(resultPath));
        var ok = doc.RootElement.GetProperty("ok").GetBoolean();
        var reason = doc.RootElement.TryGetProperty("reason", out var reasonElement)
            ? reasonElement.GetString() ?? ""
            : "";
        if (!ok)
            reason = reason + " exit=" + ran.ExitCode + " " + Trim(ran.Text);
        return new ManifestPublicationResult(ok, reason);
    }

    private static string Trim(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var flat = text.Replace('\r', ' ').Replace('\n', ' ');
        return flat.Length <= 1500 ? flat : flat[..1500];
    }
}
