using System.Globalization;
using TL;

namespace Antiphon.NightlyWatchdog;

/// <summary>
/// CARD-0545 D-6: recipient-side readback through an MTProto user session on the operator's own account
/// (WTelegramClient). Reads exactly one peer with <c>Messages_GetHistory</c> plus
/// <c>Messages_GetPeerDialogs</c> for the read marker; never sends. With no session file or no api
/// credentials it is <c>Held</c>, never eligible-empty, and constructs no client.
/// </summary>
public sealed class TelegramUserReader(WatchdogOptions options) : IRecipientReader, IAsyncDisposable
{
    private WTelegram.Client? _client;
    private InputPeer? _peer;
    private FileStream? _session;

    public async Task<ReaderReadResult> ReadAsync(DateTime floorUtc, CancellationToken ct)
    {
        if (options.ReaderApiId == 0 || string.IsNullOrWhiteSpace(options.ReaderApiHash) || string.IsNullOrWhiteSpace(options.ReaderPeer))
            return ReaderReadResult.Held("reader-unconfigured");
        if (!File.Exists(options.ResolvedReaderSessionPath))
            return ReaderReadResult.Held("session-missing");

        try
        {
            var client = await ClientAsync();
            var peer = await PeerAsync(client);
            if (peer is null) return ReaderReadResult.Held("peer-unresolved");

            var dialogs = await client.Messages_GetPeerDialogs(new InputDialogPeer { peer = peer });
            var readInboxMaxId = dialogs.dialogs.OfType<Dialog>().Select(d => d.read_inbox_max_id).DefaultIfEmpty(0).Max();

            var floor = LondonClock.AsUtc(floorUtc).AddHours(-24);
            var observations = new List<RecipientObservation>();
            var offsetId = 0;
            for (var page = 0; page < 20; page++)
            {
                var history = await client.Messages_GetHistory(peer, offset_id: offsetId, limit: 50);
                var messages = history.Messages.OfType<Message>().ToList();
                if (messages.Count == 0) break;
                foreach (var message in messages)
                {
                    if (LondonClock.AsUtc(message.date) < floor) continue;
                    observations.Add(RecipientObservation.From(message, readInboxMaxId));
                }
                if (messages.Min(m => LondonClock.AsUtc(m.date)) < floor) break;
                offsetId = messages.Min(m => m.id);
            }
            return ReaderReadResult.Eligible(observations);
        }
        catch (RpcException ex) when (ex.Code is 401 or 406)
        {
            return ReaderReadResult.Held("session-revoked");
        }
    }

    private async Task<WTelegram.Client> ClientAsync()
    {
        if (_client is not null) return _client;
        _session = new FileStream(options.ResolvedReaderSessionPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var client = new WTelegram.Client(Config, _session);
        await client.LoginUserIfNeeded();
        _client = client;
        return client;
    }

    private string? Config(string what) => what switch
    {
        "api_id" => options.ReaderApiId.ToString(CultureInfo.InvariantCulture),
        "api_hash" => options.ReaderApiHash,
        // A running service never answers interactive prompts: a session needing them is held.
        "verification_code" or "password" or "phone_number" => throw new InvalidOperationException("reader session requires interactive login"),
        _ => null,
    };

    private async Task<InputPeer?> PeerAsync(WTelegram.Client client)
    {
        if (_peer is not null) return _peer;
        if (string.IsNullOrWhiteSpace(options.ReaderPeerUsername)) return null;
        var resolved = await client.Contacts_ResolveUsername(options.ReaderPeerUsername.TrimStart('@'));
        if (resolved.User is not { } user || user.id.ToString(CultureInfo.InvariantCulture) != options.ReaderPeer) return null;
        _peer = new InputPeerUser(user.id, user.access_hash);
        return _peer;
    }

    /// <summary>
    /// Operator-only interactive login (<c>--reader-login</c>): phone number, code and 2FA password are read
    /// from the console; only WTelegramClient's session file is stored.
    /// </summary>
    public static async Task<int> LoginInteractiveAsync(WatchdogOptions options, TextReader input, TextWriter output)
    {
        if (options.ReaderApiId == 0 || string.IsNullOrWhiteSpace(options.ReaderApiHash))
        {
            await output.WriteLineAsync("reader-unconfigured: set ANTIPHON_WATCHDOG_TG_API_ID and ANTIPHON_WATCHDOG_TG_API_HASH");
            return 3;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(options.ResolvedReaderSessionPath)!);
        await using var session = new FileStream(options.ResolvedReaderSessionPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var client = new WTelegram.Client(what =>
        {
            switch (what)
            {
                case "api_id": return options.ReaderApiId.ToString(CultureInfo.InvariantCulture);
                case "api_hash": return options.ReaderApiHash;
                case "phone_number":
                case "verification_code":
                case "password":
                    output.Write($"{what}: ");
                    return input.ReadLine();
                default: return null;
            }
        }, session);
        var me = await client.LoginUserIfNeeded();
        await output.WriteLineAsync($"reader session stored for user id {me.id}");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(options.ResolvedReaderSessionPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return 0;
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_session is not null) await _session.DisposeAsync();
    }
}
