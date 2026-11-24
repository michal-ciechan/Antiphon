#nullable disable
using System;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Configuration.Json;
using Npgsql;

namespace Antiphon.Nightly
{
    internal static class DevPostgresCheck
    {
        internal const string SkipReason = DevCompose.SkipVariable + "=1";

        private const string ConnectionKey = "ConnectionStrings:DefaultConnection";
        private const string InvalidEndpoint = ConnectionKey + " has no usable TCP host and port";
        private const int TimeoutMs = 2000;

        internal static bool SkipLocal(NightlyHost host) =>
            DevCompose.Skipped(host.Variables?.Invoke(DevCompose.SkipVariable) ?? host.UserVariables?.Invoke(DevCompose.SkipVariable));

        internal static (bool Ok, string Detail) Probe(NightlyHost host, IFileSystem files, string root, bool simpleMode,
            Func<string, int, int, bool> tcpOpen)
        {
            string server;
            int port;
            UriHostNameType hostType;
            try
            {
                var connection = new NpgsqlConnectionStringBuilder(ResolveConnectionString(host, files, root, simpleMode));
                server = connection.Host?.Trim() ?? "";
                hostType = Uri.CheckHostName(server);
                port = connection.Port;
                if (hostType == UriHostNameType.Unknown || port is < 1 or > 65535) return (false, InvalidEndpoint);
                if (hostType == UriHostNameType.IPv6 && server.StartsWith('[')) server = server[1..^1];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or
                ArgumentException or FormatException or OverflowException or NotSupportedException or InvalidOperationException)
            {
                // A parser or file error may include credentials from configuration.
                return (false, InvalidEndpoint);
            }

            var open = tcpOpen(server, port, TimeoutMs);
            var address = hostType == UriHostNameType.IPv6 ? "[" + server + "]" : server;
            return (open, "TCP " + address + ":" + port.ToString(CultureInfo.InvariantCulture) + (open ? " open" : " closed or unreachable"));
        }

        private static string ResolveConnectionString(NightlyHost host, IFileSystem files, string root, bool simpleMode)
        {
            string Variable(string name) => host.Variables?.Invoke(name);
            var value = Variable("ConnectionStrings__DefaultConnection") ?? Variable(ConnectionKey);
            if (value != null) return value;

            // Aspire supplies the AppHost setting to the server. Simple mode starts the Development server directly.
            var project = simpleMode ? "server" : "Antiphon.AppHost";
            var secretsId = simpleMode ? "antiphon-server" : "aspire-antiphon-apphost";
            var appData = Variable("APPDATA");
            var profile = Variable("HOME") ?? Variable("USERPROFILE");
            string secretsRoot = null;
            if (!string.IsNullOrEmpty(appData))
                secretsRoot = files.Path.Combine(appData, "Microsoft", "UserSecrets");
            else if (!string.IsNullOrEmpty(profile))
                secretsRoot = files.Path.Combine(profile, ".microsoft", "usersecrets");
            if (secretsRoot != null)
            {
                value = ReadConnectionString(files, files.Path.Combine(secretsRoot, secretsId, "secrets.json"));
                if (value != null) return value;
            }

            return ReadConnectionString(files, files.Path.Combine(root, project, "appsettings.Development.json"))
                ?? ReadConnectionString(files, files.Path.Combine(root, project, "appsettings.json"));
        }

        private static string ReadConnectionString(IFileSystem files, string path)
        {
            try
            {
                using var stream = files.File.OpenRead(path);
                var provider = new JsonStreamConfigurationProvider(new JsonStreamConfigurationSource());
                provider.Load(stream);
                return provider.TryGet(ConnectionKey, out var value) ? value ?? "" : null;
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }
        }

        internal static bool TcpOpen(string host, int port, int timeoutMs)
        {
            try
            {
                using var client = new TcpClient();
                using var deadline = new CancellationTokenSource(timeoutMs);
                client.ConnectAsync(host, port, deadline.Token).AsTask().GetAwaiter().GetResult();
                return client.Connected;
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException or ArgumentException or InvalidOperationException)
            {
                return false;
            }
        }
    }
}
