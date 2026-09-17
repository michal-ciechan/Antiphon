using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.NightlyWatchdog;

/// <summary>
/// CARD-0545 typed configuration. Production reads <c>ANTIPHON_WATCHDOG_*</c> from the host env file
/// (systemd <c>EnvironmentFile</c>); a qualification instance reads <c>--config ./qual.json</c>. No
/// host, address, user or credential has a built-in default: unset means refuse (D-12).
/// </summary>
public sealed class WatchdogOptions
{
    public const string ProductionNamespace = "mc";

    public string Namespace { get; set; } = ProductionNamespace;
    public string? InstanceId { get; set; }
    public string Version { get; set; } = typeof(WatchdogOptions).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public string? WindmillBaseUrl { get; set; }
    public string? WindmillToken { get; set; }
    public string WindmillWorkspace { get; set; } = "mc";
    public string ScriptPath { get; set; } = "u/lndcobra/antiphon_nightly_tests";
    public string SchedulePath { get; set; } = "u/lndcobra/antiphon_nightly_tests";
    public string? ExpectedScriptHash { get; set; }
    public string DesktopWorkerGroup { get; set; } = "desktop";

    public string? DestinationChatId { get; set; }
    public string? TelegramBotToken { get; set; }

    public int ReaderApiId { get; set; }
    public string? ReaderApiHash { get; set; }
    public string? ReaderSessionPath { get; set; }
    /// <summary>The authorized peer id as the reader sees it (the bot's user id in the operator's DM).</summary>
    public string? ReaderPeer { get; set; }
    public string? ReaderPeerUsername { get; set; }

    public string StateDir { get; set; } = "./state";
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
    public string? SnapshotBind { get; set; }
    public string Runtime { get; set; } = "native";

    public int TickSeconds { get; set; } = 600;
    public int StartGraceMinutes { get; set; } = 30;
    public int RunBudgetHours { get; set; } = 6;
    public int ReceiptGraceMinutes { get; set; } = 30;
    public int ReaderHoldExpiryMinutes { get; set; } = 30;
    public int ConsecutiveTicksToOpen { get; set; } = 2;
    public int ConsecutiveCleanTicksToClose { get; set; } = 2;
    public int DesktopWorkerMissingMinutes { get; set; } = 60;
    public int HttpTimeoutSeconds { get; set; } = 15;

    public bool AllowFaultInjection { get; set; }
    public string? CrashAfter { get; set; }
    public string? HoldControlPath { get; set; }

    public string ResolvedStateDir => Path.GetFullPath(Path.IsPathRooted(StateDir) ? StateDir : Path.Combine(WorkingDirectory, StateDir));
    public string LedgerPath => Path.Combine(ResolvedStateDir, "ledger.db");
    public string ResolvedReaderSessionPath => ReaderSessionPath is { Length: > 0 } p
        ? (Path.IsPathRooted(p) ? p : Path.Combine(WorkingDirectory, p))
        : Path.Combine(ResolvedStateDir, "reader.session");

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Namespace))
            errors.Add("namespace-missing");
        if (string.IsNullOrWhiteSpace(SnapshotBind))
            errors.Add("snapshot-bind-missing");
        else if (IsWildcardBind(SnapshotBind) && !string.Equals(Runtime, "container", StringComparison.Ordinal))
            errors.Add("snapshot-bind-wildcard");
        // D-11: the production namespace can never inject faults.
        if (Namespace == ProductionNamespace && (AllowFaultInjection || CrashAfter != null))
            errors.Add("fault-injection-forbidden");
        if (!string.IsNullOrWhiteSpace(Namespace) && Namespace != ProductionNamespace)
        {
            var production = Path.GetFullPath(Path.Combine(WorkingDirectory, "state"));
            if (string.Equals(Path.TrimEndingDirectorySeparator(ResolvedStateDir), Path.TrimEndingDirectorySeparator(production),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                errors.Add("state-dir-shared-with-production");
        }
        return errors;
    }

    public static bool IsWildcardBind(string bind)
    {
        var host = SplitBind(bind).Host;
        return host is "0.0.0.0" or "::" or "*" or "+" || (IPAddress.TryParse(host, out var ip) &&
            (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)));
    }

    public static (string Host, int Port) SplitBind(string bind)
    {
        var text = bind.Trim();
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            var host = text[1..close];
            var port = close + 2 <= text.Length && text.Length > close + 1 ? int.Parse(text[(close + 2)..], CultureInfo.InvariantCulture) : 0;
            return (host, port);
        }
        var colon = text.LastIndexOf(':');
        return colon < 0 ? (text, 0) : (text[..colon], int.Parse(text[(colon + 1)..], CultureInfo.InvariantCulture));
    }

    /// <summary>Stable hash of the non-secret configuration, published in the snapshot.</summary>
    public string ConfigHash()
    {
        var text = string.Join("\n", Namespace, WindmillBaseUrl, WindmillWorkspace, ScriptPath, SchedulePath, ExpectedScriptHash,
            DestinationHash(DestinationChatId), ReaderPeer, SnapshotBind, Runtime, TickSeconds, StartGraceMinutes, RunBudgetHours,
            ReceiptGraceMinutes, ReaderHoldExpiryMinutes);
        return Sha256Hex(text)[..16];
    }

    public static string? DestinationHash(string? chatId) =>
        string.IsNullOrWhiteSpace(chatId) ? null : Sha256Hex(chatId.Trim())[..16];

    public static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static WatchdogOptions FromEnvironment(Func<string, string?> env)
    {
        var o = new WatchdogOptions();
        o.Namespace = env("ANTIPHON_WATCHDOG_NAMESPACE") is { Length: > 0 } ns ? ns : ProductionNamespace;
        o.WindmillBaseUrl = env("ANTIPHON_WATCHDOG_WINDMILL_BASE_URL");
        o.WindmillToken = env("ANTIPHON_WATCHDOG_WINDMILL_TOKEN");
        if (env("ANTIPHON_WATCHDOG_WINDMILL_WORKSPACE") is { Length: > 0 } ws) o.WindmillWorkspace = ws;
        if (env("ANTIPHON_WATCHDOG_SCRIPT_PATH") is { Length: > 0 } sp) o.ScriptPath = sp;
        if (env("ANTIPHON_WATCHDOG_SCHEDULE_PATH") is { Length: > 0 } sc) o.SchedulePath = sc;
        o.ExpectedScriptHash = env("ANTIPHON_WATCHDOG_EXPECTED_SCRIPT_HASH");
        o.DestinationChatId = env("ANTIPHON_WATCHDOG_DESTINATION_CHAT_ID");
        o.TelegramBotToken = env("ANTIPHON_WATCHDOG_TELEGRAM_BOT_TOKEN");
        o.ReaderApiId = int.TryParse(env("ANTIPHON_WATCHDOG_TG_API_ID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
        o.ReaderApiHash = env("ANTIPHON_WATCHDOG_TG_API_HASH");
        o.ReaderSessionPath = env("ANTIPHON_WATCHDOG_TG_SESSION_PATH");
        o.ReaderPeer = env("ANTIPHON_WATCHDOG_READER_PEER");
        o.ReaderPeerUsername = env("ANTIPHON_WATCHDOG_READER_PEER_USERNAME");
        if (string.IsNullOrWhiteSpace(o.ReaderPeer) && o.TelegramBotToken is { } token && token.IndexOf(':') > 0)
            o.ReaderPeer = token[..token.IndexOf(':')];
        if (env("ANTIPHON_WATCHDOG_STATE_DIR") is { Length: > 0 } sd) o.StateDir = sd;
        o.SnapshotBind = env("ANTIPHON_WATCHDOG_SNAPSHOT_BIND");
        if (env("ANTIPHON_WATCHDOG_RUNTIME") is { Length: > 0 } rt) o.Runtime = rt;
        o.HoldControlPath = env("ANTIPHON_WATCHDOG_HOLD_CONTROL_PATH");
        return o;
    }

    /// <summary>Overlay a qualification JSON config (<c>qual.json</c>) on top of the env values.</summary>
    public void ApplyJson(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("config must be a JSON object");
        foreach (var (key, value) in node)
        {
            var property = typeof(WatchdogOptions).GetProperty(key,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (property is null || !property.CanWrite || value is null) continue;
            var converted = value.Deserialize(property.PropertyType);
            property.SetValue(this, converted);
        }
    }
}
