using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Messaging;
using MessagingJson = Antiphon.Messaging.MessagingJson;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Files;

/// <summary>CARD-0418: atomic staging under a server-owned <c>.antiphon/outbound/&lt;id&gt;</c> tree.</summary>
public sealed class ChannelOutboundFileStore : IChannelOutboundFileStore
{
    internal Func<string, CancellationToken, Task>? TestBarrier { get; set; }

    private readonly ChannelOutboundSettings _settings;
    private readonly ILogger<ChannelOutboundFileStore> _logger;

    public ChannelOutboundFileStore(
        IOptions<ChannelOutboundSettings> settings,
        ILogger<ChannelOutboundFileStore> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        Root = ResolveRoot(_settings.StoreRoot);
    }

    public string Root { get; }

    public string InputDirectory(Guid deliveryId) => Path.Combine(Root, deliveryId.ToString("D"), "input");

    public string OutputDirectory(Guid deliveryId) => Path.Combine(Root, deliveryId.ToString("D"), "output");

    public async Task StageInputAsync(Guid deliveryId, ChannelOutboundStagedInput input, CancellationToken ct)
    {
        var dest = Path.Combine(Root, deliveryId.ToString("D"));
        var temp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(temp);
        try
        {
            var inputDir = Path.Combine(temp, "input");
            var filesDir = Path.Combine(inputDir, "files");
            Directory.CreateDirectory(inputDir);
            Directory.CreateDirectory(filesDir);
            Directory.CreateDirectory(Path.Combine(temp, "output"));
            await File.WriteAllTextAsync(Path.Combine(inputDir, "reply.json"), input.FrozenReplyJson, Encoding.UTF8, ct);
            await File.WriteAllTextAsync(Path.Combine(inputDir, "request.json"), input.RequestJson, Encoding.UTF8, ct);
            await File.WriteAllTextAsync(Path.Combine(inputDir, "hash.txt"), input.InputHash, Encoding.UTF8, ct);
            foreach (var file in input.Files)
            {
                RejectEscapingName(file.SafeName);
                await File.WriteAllBytesAsync(Path.Combine(filesDir, file.SafeName), file.Bytes, ct);
            }

            if (TestBarrier is not null)
                await TestBarrier("InputStaging", ct);

            if (Directory.Exists(dest))
                Directory.Delete(dest, recursive: true);
            Directory.Move(temp, dest);
        }
        catch
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
            throw;
        }
    }

    public async Task<ChannelOutboundStagedInput> ReadInputAsync(Guid deliveryId, CancellationToken ct)
    {
        var inputDir = InputDirectory(deliveryId);
        var reply = await File.ReadAllTextAsync(Path.Combine(inputDir, "reply.json"), Encoding.UTF8, ct);
        var request = await File.ReadAllTextAsync(Path.Combine(inputDir, "request.json"), Encoding.UTF8, ct);
        var hash = await File.ReadAllTextAsync(Path.Combine(inputDir, "hash.txt"), Encoding.UTF8, ct);
        var files = new List<ChannelOutboundStagedFile>();
        var filesDir = Path.Combine(inputDir, "files");
        if (Directory.Exists(filesDir))
        {
            foreach (var path in Directory.EnumerateFiles(filesDir))
            {
                var bytes = await File.ReadAllBytesAsync(path, ct);
                files.Add(new ChannelOutboundStagedFile
                {
                    SafeName = Path.GetFileName(path),
                    Mime = "application/octet-stream",
                    Length = bytes.LongLength,
                    Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                    Bytes = bytes,
                });
            }
        }

        return new ChannelOutboundStagedInput
        {
            FrozenReplyJson = reply,
            RequestJson = request,
            InputHash = hash.Trim(),
            Files = files,
        };
    }

    public async Task SealOutputAsync(Guid deliveryId, ChannelReply payload, CancellationToken ct)
    {
        var dest = Path.Combine(Root, deliveryId.ToString("D"), "sealed.json");
        var temp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.Serialize(payload, MessagingJson.Options);
        await File.WriteAllTextAsync(temp, json, Encoding.UTF8, ct);
        File.Move(temp, dest, overwrite: true);
    }

    public async Task<ChannelReply?> ReadSealedPayloadAsync(Guid deliveryId, CancellationToken ct)
    {
        var dest = Path.Combine(Root, deliveryId.ToString("D"), "sealed.json");
        if (!File.Exists(dest))
            return null;
        var json = await File.ReadAllTextAsync(dest, Encoding.UTF8, ct);
        return JsonSerializer.Deserialize<ChannelReply>(json, MessagingJson.Options);
    }

    public async Task WriteWorkerRequestAsync(Guid deliveryId, string json, CancellationToken ct)
    {
        var path = Path.Combine(InputDirectory(deliveryId), "request.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8, ct);
    }

    public Task<string> WorkerDirectoryAsync(Guid deliveryId, CancellationToken ct)
    {
        var dir = Path.Combine(Root, deliveryId.ToString("D"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "input"));
        Directory.CreateDirectory(Path.Combine(dir, "output"));
        return Task.FromResult(dir);
    }

    private static void RejectEscapingName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Contains("..", StringComparison.Ordinal)
            || name.Contains('/')
            || name.Contains('\\')
            || Path.IsPathRooted(name))
        {
            throw new InvalidOperationException($"refusing outbound store name '{name}'");
        }
    }

    private static string ResolveRoot(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);
        return Path.Combine(AppContext.BaseDirectory, ".antiphon", "outbound");
    }
}