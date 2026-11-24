#nullable disable
using System;
using System.ComponentModel;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    // Keep the resolution contract in sync with scripts/lib/desktop-postgres.ps1.
    internal sealed class DesktopPostgres
    {
        private const string Server2Container = "antiphon-server2-postgres-postgres-1";
        private const string BindingFormat = "{{.Id}}|{{.State.Running}}|{{json (index .NetworkSettings.Ports \"5432/tcp\")}}";
        private const string SecretKey = "ConnectionStrings:DefaultConnection";
        private const string ConfigurationError = "Could not resolve the production PostgreSQL connection. Set the AppHost user secret "
            + SecretKey + " for the desktop account, or specify a local/test container explicitly.";
        internal const int TimeoutMilliseconds = 30000;

        private readonly NightlyHost _host;
        private readonly bool _server2;
        private readonly string _container;
        private readonly string _user;
        private readonly string _database;

        private DesktopPostgres(NightlyHost host, bool server2, string container, string user, string database)
        {
            _host = host;
            _server2 = server2;
            _container = container;
            _user = user;
            _database = database;
        }

        internal static DesktopPostgres Resolve(NightlyHost host, string container, string user, string database)
        {
            if (!string.IsNullOrEmpty(container))
            {
                if (string.IsNullOrWhiteSpace(container) || container.StartsWith("-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Specify a PostgreSQL container name or ID.");
                return new DesktopPostgres(host, false, container, user, database);
            }

            var endpoint = ReadConfiguredEndpoint(host);
            var inspection = RunDocker(host, true, "Production PostgreSQL lookup via the antiphon-server2 Docker context",
                "inspect", "--type", "container", "--format", BindingFormat, Server2Container);
            var fields = inspection.Split('|', 3);
            if (fields.Length != 3 || !Regex.IsMatch(fields[0], @"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant)
                || fields[1] != "true" || !HasPublishedEndpoint(fields[2], endpoint.Host, endpoint.Port))
            {
                throw new InvalidOperationException("The running server2 PostgreSQL container must publish the endpoint in the AppHost user secret "
                    + SecretKey + ".");
            }

            return new DesktopPostgres(host, true, fields[0], endpoint.User, endpoint.Database);
        }

        private static (string Host, string Port, string User, string Database) ReadConfiguredEndpoint(NightlyHost host)
        {
            try
            {
                var appData = host.Variables("APPDATA");
                var home = host.Variables("HOME");
                var root = !string.IsNullOrWhiteSpace(appData) ? Path.Combine(appData, "Microsoft", "UserSecrets")
                    : !string.IsNullOrWhiteSpace(home) ? Path.Combine(home, ".microsoft", "usersecrets") : null;
                if (root == null) throw new InvalidOperationException();
                var config = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "aspire-antiphon-apphost", "secrets.json")),
                    new JsonNodeOptions { PropertyNameCaseInsensitive = true },
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var value = (config?[SecretKey] ?? config?["ConnectionStrings"]?["DefaultConnection"])?.GetValue<string>();
                var connection = new DbConnectionStringBuilder { ConnectionString = value };
                string Read(string key) => connection.TryGetValue(key, out var setting) ? setting as string : null;

                var hostName = Read("Host");
                var portText = Read("Port") ?? "5432";
                var database = Read("Database");
                string user = null;
                foreach (var key in new[] { "Username", "User ID", "UserId", "User Name", "User", "UID" })
                {
                    user = Read(key);
                    if (user != null) break;
                }
                if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535
                    || string.IsNullOrWhiteSpace(hostName) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(database))
                    throw new InvalidOperationException();

                return (hostName, port.ToString(CultureInfo.InvariantCulture), user, database);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
            {
                // Neither a parser error nor its inner exception may disclose the secret.
                throw new InvalidOperationException(ConfigurationError);
            }
        }

        private static bool HasPublishedEndpoint(string json, string host, string port)
        {
            try
            {
                using var bindings = JsonDocument.Parse(json);
                if (bindings.RootElement.ValueKind != JsonValueKind.Array) return false;
                foreach (var binding in bindings.RootElement.EnumerateArray())
                {
                    if (binding.ValueKind == JsonValueKind.Object
                        && binding.TryGetProperty("HostIp", out var ip) && ip.ValueKind == JsonValueKind.String
                        && binding.TryGetProperty("HostPort", out var publishedPort) && publishedPort.ValueKind == JsonValueKind.String
                        && string.Equals(ip.GetString(), host, StringComparison.OrdinalIgnoreCase) && publishedPort.GetString() == port)
                        return true;
                }
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        internal string Query(string sql)
        {
            // psql treats a dbname with '=' or a URI as connection options, which can replace the selected socket.
            if (string.IsNullOrWhiteSpace(_database) || _database.Contains('=')
                || _database.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
                || _database.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Specify a PostgreSQL database name.");

            string[] socket = _server2 ? ["-h", "/var/run/postgresql", "-p", "5432"] : [];
            return RunDocker(_host, _server2, "PostgreSQL query", [
                "exec", "-e", "PGOPTIONS=-c default_transaction_read_only=on", _container,
                "psql", "-X", "-A", "-t", "-w", "-v", "ON_ERROR_STOP=1", ..socket, "-U", _user, "-d", _database, "-c", sql]);
        }

        internal string InspectIdentity() => RunDocker(_host, _server2, "PostgreSQL container identity lookup",
            "inspect", "--type", "container", "--format", "{{.Id}}|{{.State.StartedAt}}", _container);

        internal string ReadCpuAccounting() => RunDocker(_host, _server2, "PostgreSQL container CPU lookup", "exec", _container, "sh", "-c",
            "if [ -r /sys/fs/cgroup/cpu.stat ]; then cat /sys/fs/cgroup/cpu.stat; else cat /sys/fs/cgroup/cpuacct/cpuacct.usage; fi");

        private static string RunDocker(NightlyHost host, bool server2, string operation, params string[] arguments)
        {
            if (server2) arguments = ["--context", "antiphon-server2", ..arguments];
            NightlyLaunchResult result;
            try
            {
                result = host.Launch(new NightlyLaunch("docker", arguments, "", TimeoutMilliseconds, CaptureOutput: true));
            }
            catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                throw new InvalidOperationException(operation + " could not start Docker. Check that Docker is installed and accessible.");
            }
            if (result.TimedOut) throw new InvalidOperationException(operation + " timed out.");
            if (result.ExitCode != 0)
                throw new InvalidOperationException(operation + " failed (exit " + result.ExitCode.ToString(CultureInfo.InvariantCulture)
                    + "). Check Docker access and PostgreSQL availability.");
            return (result.StandardOutputText ?? "").TrimEnd();
        }
    }
}
