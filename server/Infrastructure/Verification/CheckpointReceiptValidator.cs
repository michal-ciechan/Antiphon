using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Files;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Verification;

/// <summary>Imports an operator-pinned producer inventory, never a caller-selected filesystem path.</summary>
public sealed class CheckpointReceiptValidator(IOptions<CheckpointReceiptSettings> options, ILandingGit git)
    : ICheckpointReceiptValidator
{
    private sealed record Entry(string OriginalPath, string RelativePath, long Bytes, string Sha256);
    private sealed record Inventory(string Task, string SourceCommit, string SourceWorktree,
        string CleanCheckpointRun, Entry[] Files);
    private sealed record RequiredRow(string Id, string Filter, int Minimum);

    public async Task<ImportedCheckpointEvidence> ImportAsync(ReviewEvidenceCorrectionRequest request, CancellationToken ct)
    {
        try { return await ImportCoreAsync(request, ct); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException
            or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
        { throw ReviewEvidenceCorrectionService.Refuse("receipt_invalid"); }
    }

    private async Task<ImportedCheckpointEvidence> ImportCoreAsync(ReviewEvidenceCorrectionRequest request, CancellationToken ct)
    {
        var settings = options.Value;
        var producer = settings.Producers.SingleOrDefault(p => p.Identity == request.ProducerIdentity)
            ?? throw ReviewEvidenceCorrectionService.Refuse("producer_untrusted");
        if (producer.RepositoryPath != request.RepositoryPath) throw ReviewEvidenceCorrectionService.Refuse("producer_repository_mismatch");
        var rows = await SelectionAsync(producer, request.Selection, ct);
        ImportedCheckpointEvidence Incomplete(IEnumerable<string> missing) => new("", "{}", missing.ToArray());
        if (producer.InventoryDigest != request.BundleDigest) return Incomplete(request.Selection.Rows);
        var retained = Path.Combine(settings.StorageRoot, request.BundleDigest);
        var retainedIndex = Path.Combine(retained, "producer-pack-index.json");
        var indexPath = File.Exists(retainedIndex) ? retainedIndex : producer.InventoryPath;
        var pack = Path.GetDirectoryName(indexPath)!;
        var indexBytes = await ReadAsync(indexPath, pack, ct);
        if (Hash(indexBytes) != producer.InventoryDigest) throw ReviewEvidenceCorrectionService.Refuse("inventory_digest_mismatch");
        var inventory = Parse<Inventory>(indexBytes);
        if (inventory.Task != producer.Identity || inventory.SourceCommit != request.SourceSha
            || inventory.Files.Length is < 1 or > 1000 || inventory.Files.Sum(e => e.Bytes) > 256L * 1024 * 1024
            || inventory.Files.Any(e => e.Bytes < 0 || !ReviewEvidenceCorrectionService.IsDigest(e.Sha256))
            || inventory.Files.Select(e => e.OriginalPath).Distinct(StringComparer.Ordinal).Count() != inventory.Files.Length
            || inventory.Files.Select(e => e.RelativePath).Distinct(StringComparer.Ordinal).Count() != inventory.Files.Length)
            throw ReviewEvidenceCorrectionService.Refuse("producer_mismatch");
        foreach (var entry in inventory.Files) _ = Resolve(pack, entry.RelativePath);
        async Task<byte[]> OriginalAsync(string path)
        {
            var entry = inventory.Files.SingleOrDefault(e => e.OriginalPath == path)
                ?? throw new InvalidDataException("required_provenance_missing");
            return await ReadEntryAsync(pack, entry, ct);
        }
        var reportEntry = inventory.Files.SingleOrDefault(e => e.RelativePath ==
            $".antiphon/checkpoints/{inventory.CleanCheckpointRun}/report.json");
        if (reportEntry is null) return Incomplete(request.Selection.Rows);
        byte[] reportBytes;
        try { reportBytes = await ReadEntryAsync(pack, reportEntry, ct); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return Incomplete(request.Selection.Rows); }
        using var reportDocument = JsonDocument.Parse(reportBytes);
        var report = reportDocument.RootElement;
        if (report.GetProperty("schemaVersion").GetInt32() != 2
            || report.GetProperty("commit").GetString() != request.SourceSha
            || "refs/heads/" + report.GetProperty("branch").GetString() != request.SourceRef
            || report.GetProperty("worktree").GetString() != inventory.SourceWorktree)
            throw ReviewEvidenceCorrectionService.Refuse("receipt_coordinates_mismatch");
        var reportRows = report.GetProperty("rows").EnumerateArray().ToArray();
        if (reportRows.GroupBy(r => r.GetProperty("id").GetString()).Any(g => g.Count() != 1))
            throw ReviewEvidenceCorrectionService.Refuse("duplicate_rows");
        var missing = new List<string>();
        foreach (var required in rows)
        {
            var matches = reportRows.Where(r => r.GetProperty("id").GetString() == required.Id).ToArray();
            if (matches.Length != 1) { missing.Add(required.Id); continue; }
            var row = matches[0];
            try
            {
                if (row.GetProperty("filter").GetString() != required.Filter
                    || row.GetProperty("state").GetString() != "green" || row.GetProperty("exitCode").GetInt32() != 0
                    || row.GetProperty("executed").GetInt32() < required.Minimum
                    || row.GetProperty("passed").GetInt32() != row.GetProperty("executed").GetInt32()
                    || row.GetProperty("failed").GetInt32() != 0 || row.GetProperty("skipped").GetInt32() != 0
                    || row.GetProperty("command").ValueKind != JsonValueKind.Null)
                    throw new InvalidDataException("required_execution_incomplete");
                var trx = await OriginalAsync(row.GetProperty("trx").GetString()!);
                using var xmlStream = new MemoryStream(trx);
                var xml = XDocument.Load(xmlStream);
                var results = xml.Descendants().Where(e => e.Name.LocalName == "UnitTestResult").ToArray();
                if (results.Length != row.GetProperty("executed").GetInt32()
                    || results.Any(e => (string?)e.Attribute("outcome") != "Passed"))
                    throw new InvalidDataException("trx_incomplete");
                var build = row.GetProperty("build").GetString();
                var stampEntry = inventory.Files.SingleOrDefault(e => e.RelativePath.EndsWith(
                    "/" + build + "/checkpoint-build-source.json", StringComparison.Ordinal))
                    ?? throw new InvalidDataException("build_provenance_missing");
                using var stamp = JsonDocument.Parse(await ReadEntryAsync(pack, stampEntry, ct));
                if (stamp.RootElement.GetProperty("commit").GetString() != request.SourceSha
                    || stamp.RootElement.GetProperty("repositoryRoot").GetString() != inventory.SourceWorktree
                    || stamp.RootElement.GetProperty("sourceState").GetString() != "clean"
                    || stamp.RootElement.GetProperty("fingerprint").GetString() != row.GetProperty("source").GetProperty("start").GetProperty("fingerprint").GetString())
                    throw new InvalidDataException("build_provenance_mismatch");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException or JsonException or InvalidOperationException or KeyNotFoundException)
            { missing.Add(required.Id); }
        }
        if (missing.Count > 0) return Incomplete(missing);
        var manifest = await OriginalAsync(report.GetProperty("manifestPath").GetString()!);
        if (Hash(manifest) != report.GetProperty("manifestHash").GetString())
            throw ReviewEvidenceCorrectionService.Refuse("manifest_digest_mismatch");

        if (!File.Exists(retainedIndex))
        {
            RequireDirectory(settings.StorageRoot);
            var staging = Path.Combine(settings.StorageRoot, ".import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            foreach (var entry in inventory.Files)
            {
                var bytes = await ReadEntryAsync(pack, entry, ct);
                var destination = Resolve(staging, entry.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await stream.WriteAsync(bytes, ct);
                stream.Flush(true);
            }
            await using (var stream = new FileStream(Path.Combine(staging, "producer-pack-index.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { await stream.WriteAsync(indexBytes, ct); stream.Flush(true); }
            try { Directory.Move(staging, retained); }
            catch (IOException) when (Directory.Exists(retained))
            {
                // A concurrent identical import won. Verify it before accepting it; retain this
                // owned staging tree on failure for diagnosis, never replace an existing pack.
                await VerifyRetainedAsync(retainedIndex, request.BundleDigest, ct);
                Directory.Delete(staging, true);
            }
        }
        await VerifyRetainedAsync(retainedIndex, request.BundleDigest, ct);
        var retainedReport = Resolve(retained, reportEntry.RelativePath);
        var arguments = new[] { "-NoProfile", "-NonInteractive", "-File", settings.ValidatorScript,
            "-Evidence", retainedReport, "-ExpectedSourceSha", request.SourceSha, "-Rows", string.Join(',', request.Selection.Rows) };
        var validation = await RunValidatorAsync(arguments, ct);
        if (validation.Exit != 0) return Incomplete(request.Selection.Rows);
        return new(retainedIndex, JsonSerializer.Serialize(new { schemaVersion = 1, verified = true,
            sourceSha = request.SourceSha, rows = request.Selection.Rows, bundleDigest = request.BundleDigest,
            validator = settings.ValidatorScript, arguments, exitCode = validation.Exit, output = validation.Output }), []);
    }

    private async Task<RequiredRow[]> SelectionAsync(CheckpointProducer producer, ReviewCorrectionSelection selection, CancellationToken ct)
    {
        InterimVerificationPolicy.ValidateSelectionShape(new(selection.Path, selection.CommitSha, selection.Section));
        if (!ReviewEvidenceCorrectionService.IsDigest(selection.Digest) || selection.Rows is not { Length: > 0 and <= 100 }
            || selection.Rows.Any(r => !System.Text.RegularExpressions.Regex.IsMatch(r, "^CP-[0-9]+$"))
            || selection.Rows.Distinct(StringComparer.Ordinal).Count() != selection.Rows.Length)
            throw ReviewEvidenceCorrectionService.Refuse("selection_invalid");
        var tree = await git.RunAsync(producer.SelectionRepositoryPath,
            ["ls-tree", selection.CommitSha, "--", selection.Path], new(TimeSpan.FromSeconds(30)), ct);
        if (!tree.Succeeded || !tree.Output.StartsWith("100644 blob ", StringComparison.Ordinal)
            || !tree.Output.TrimEnd().EndsWith("\t" + selection.Path, StringComparison.Ordinal))
            throw ReviewEvidenceCorrectionService.Refuse("selection_not_committed");
        var result = await git.RunAsync(producer.SelectionRepositoryPath,
            ["show", selection.CommitSha + ":" + selection.Path], new(TimeSpan.FromSeconds(30)), ct);
        if (!result.Succeeded || ReviewEvidenceCorrectionService.Digest(result.Output) != selection.Digest)
            throw ReviewEvidenceCorrectionService.Refuse("selection_digest_mismatch");
        var lines = result.Output.Replace("\r\n", "\n").Split('\n');
        var starts = Enumerable.Range(0, lines.Length).Where(i => lines[i] == selection.Section).ToArray();
        if (starts.Length != 1) throw ReviewEvidenceCorrectionService.Refuse("selection_section_ambiguous");
        var table = Array.FindIndex(lines, starts[0] + 1, line => line == "### Checkpoints");
        if (table < 0) throw ReviewEvidenceCorrectionService.Refuse("selection_table_missing");
        var selected = new List<RequiredRow>();
        foreach (var line in lines.Skip(table + 1).TakeWhile(line => !line.StartsWith('#')))
        {
            if (!line.StartsWith("| CP-", StringComparison.Ordinal)) continue;
            var cells = line.Replace("\\|", "\u0001").Split('|').Select(c => c.Trim().Trim('`').Replace('\u0001', '|')).ToArray();
            if (!selection.Rows.Contains(cells[1], StringComparer.Ordinal)) continue;
            if (cells.Length != 11 || !int.TryParse(cells[8], out var minimum) || minimum < 1)
                throw ReviewEvidenceCorrectionService.Refuse("selection_row_unsupported");
            selected.Add(new(cells[1], cells[5], minimum));
        }
        if (selected.Count != selection.Rows.Length || selected.Select(r => r.Id).Distinct().Count() != selected.Count)
            throw ReviewEvidenceCorrectionService.Refuse("selection_rows_missing");
        return selection.Rows.Select(id => selected.Single(r => r.Id == id)).ToArray();
    }

    internal static async Task VerifyRetainedAsync(string inventoryPath, string digest, CancellationToken ct)
    {
        try
        {
            var root = Path.GetDirectoryName(inventoryPath)!;
            var bytes = await ReadAsync(inventoryPath, root, ct);
            if (Hash(bytes) != digest) throw new InvalidDataException("retained_inventory_changed");
            var inventory = Parse<Inventory>(bytes);
            foreach (var entry in inventory.Files) await ReadEntryAsync(root, entry, ct);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        { throw ReviewEvidenceCorrectionService.Refuse("retained_evidence_unavailable"); }
    }

    private static async Task<byte[]> ReadEntryAsync(string root, Entry entry, CancellationToken ct)
    {
        var bytes = await ReadAsync(Resolve(root, entry.RelativePath), root, ct);
        if (bytes.LongLength != entry.Bytes || Hash(bytes) != entry.Sha256) throw new InvalidDataException("blob_digest_mismatch");
        return bytes;
    }
    private static Task<byte[]> ReadAsync(string path, string root, CancellationToken ct) =>
        new ChannelReplyAttachmentReader().ReadAttachmentAsync(path, [root], 20 * 1024 * 1024, ct);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static T Parse<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("inventory_invalid");
    private static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':')
            || Path.IsPathRooted(relative) || relative.Split('/').Any(s => s is "" or "." or ".."))
            throw new InvalidDataException("inventory_path_invalid");
        return Path.Combine(root, relative);
    }
    private static void RequireDirectory(string root)
    {
        if (!Path.IsPathFullyQualified(root)) throw ReviewEvidenceCorrectionService.Refuse("storage_unconfigured");
        Directory.CreateDirectory(root);
        for (string? path = root; path is not null; path = Path.GetDirectoryName(path))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("storage_linked");
    }
    private static async Task<(int Exit, string Output)> RunValidatorAsync(string[] arguments, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new IOException("validator_start_failed");
        var stdout = ReadBoundedAsync(child.StandardOutput, budget.Token);
        var stderr = ReadBoundedAsync(child.StandardError, budget.Token);
        try
        {
            await Task.WhenAll(child.WaitForExitAsync(budget.Token), stdout, stderr).WaitAsync(budget.Token);
            return (child.ExitCode, await stdout + await stderr);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(CancellationToken.None);
        }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var text = new StringBuilder(); var buffer = new char[4096]; int length;
        while ((length = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (text.Length + length > 65536) throw new InvalidDataException("validator_output_too_large");
            text.Append(buffer, 0, length);
        }
        return text.ToString();
    }
}
