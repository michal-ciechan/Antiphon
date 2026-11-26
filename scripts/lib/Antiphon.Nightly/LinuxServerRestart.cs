// CARD-1745 Linux server restart gate (unit 64 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice C
// and W10). It replaced scripts/restart-linux-server.ps1 and its driver scripts/test-restart-linux-server.ps1; nothing outside
// the repository started either by path (the CARD-1775 production server deploy started the script as a child; its command
// now runs this gate in process, verify-only). The CLI runs it as
//
//   antiphon-scripts restart-linux-server [--config <file>] [--expected-server-sha <sha>] [--execute | --what-if]
//       [--timeout-sec <n>] [--lock-max-age-minutes <n>]
//
// (scripts/antiphon restart-linux-server on the server2 host).
//
// Off unless --config or ANTIPHON_LINUX_SERVER_CONFIG names a Linux server. The default is a what-if probe: GET /health and
// GET /api/version, no docker. --execute probes the running server before any docker call and never stops it. Exit 0 requires
// /health 200 and version equal to --expected-server-sha. A server already at that SHA exits 0. Any other probe exits 5, keeps
// the lock, and leaves the container running. docker start <container> resumes that same image if the container is later
// found stopped. Desktop AppHost ports 17200-17209 are refused. A what-if probe whose /health is not 200 exits 1; every refusal
// before the probe exits 3.
//
// The config is a key=value file: baseUrl, container, lockPath (keys ignore case, a later key wins, a line without '=' after its
// first character voids the file). --timeout-sec bounds nothing: the script's only probe was a single request (its poll was
// never enabled), so the value is accepted and clamped as the script did. --lock-max-age-minutes: a newer lock refuses another
// run.
//
// Every step, refusal, reason and line is the script's, in its order (captured from it before its deletion, with function fakes
// of Invoke-WebRequest and docker), with PowerShell's semantics where they decide: -match is a case-insensitive regex whose $
// also matches before a final newline, -eq and -ne compare strings ignoring case, a terminating error (a config that is a
// directory, a JSON body that is null, a lock that cannot be written or removed) is one stderr line and exit 1 (pwsh -File's
// exit), and the version body is read as ConvertFrom-Json read it (LinuxServerRestart.Identity). HTTP is the Http seam (GET, 5 s,
// no redirect, any status returned, as Invoke-WebRequest -TimeoutSec 5 -MaximumRedirection 0 -SkipHttpErrorCheck); docker is
// ANTIPHON_LINUX_SERVER_DOCKER or docker on PATH (Resolve), run on the host's launcher with its stdout captured. Files are
// IFileSystem's; the clock is the host's. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Antiphon.Nightly
{
    /// <summary>The restart-linux-server command's options (restart-linux-server.ps1's parameters in kebab case).</summary>
    public sealed class LinuxServerRestartOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "restart-linux-server [--config <file>] [--expected-server-sha <sha>] [--execute | --what-if]"
            + " [--timeout-sec <n>] [--lock-max-age-minutes <n>]";

        /// <summary>--config: the key=value file (blank: ANTIPHON_LINUX_SERVER_CONFIG).</summary>
        public string Config { get; init; } = "";

        /// <summary>--expected-server-sha: the full 40- or 64-character SHA the running server must report.</summary>
        public string ExpectedServerSha { get; init; } = "";

        /// <summary>--execute: probe the running server and compare the container's image (never stops it).</summary>
        public bool Execute { get; init; }

        /// <summary>--what-if: probe only (also the default without --execute).</summary>
        public bool WhatIf { get; init; }

        /// <summary>--timeout-sec (20): accepted and clamped to at least 1; the probe is a single request.</summary>
        public int TimeoutSec { get; init; } = 20;

        /// <summary>--lock-max-age-minutes (15): a lock newer than this refuses the run (clamped to at least 1).</summary>
        public int LockMaxAgeMinutes { get; init; } = 15;

        /// <summary>The flags after the command name; an unknown, repeated or valueless flag, a stray argument or a non-integer is a usage error.</summary>
        public static LinuxServerRestartOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--config", "--expected-server-sha", "--timeout-sec", "--lock-max-age-minutes" },
                new[] { "--execute", "--what-if" });
            return new LinuxServerRestartOptions
            {
                Config = flags.Value("--config", ""),
                ExpectedServerSha = flags.Value("--expected-server-sha", ""),
                Execute = flags.Switch("--execute"),
                WhatIf = flags.Switch("--what-if"),
                TimeoutSec = flags.Int("--timeout-sec", 20),
                LockMaxAgeMinutes = flags.Int("--lock-max-age-minutes", 15),
            };
        }
    }

    /// <summary>One GET's answer: the status code (0 when no response came) and the body as PowerShell read it.</summary>
    public sealed record LinuxServerHttpAnswer(int StatusCode, string Body);

    /// <summary>GET the URL (5 s, no redirect followed, any status returned); a failure is status 0 with an empty body.</summary>
    public delegate LinuxServerHttpAnswer LinuxServerHttp(string url);

    /// <summary>What the gate observes outside its files, clock and launcher.</summary>
    public sealed class LinuxServerRestartSeams
    {
        /// <summary>The HTTP GET (production: LinuxServerRestart.Http()). Required.</summary>
        public LinuxServerHttp Http { get; init; }

        /// <summary>Command lookup for a docker given by name: its path, null when there is none. Null: OperatorToolOs.Find over the host's PATH.</summary>
        public Func<string, string> Resolve { get; init; }
    }

    /// <summary>The version body's identity: ok, or malformed with the text the script read as the version.</summary>
    public sealed record LinuxServerIdentity(string Kind, string Sha);

    /// <summary>The CARD-1745 Linux server restart gate (restart-linux-server.ps1; see the file comment).</summary>
    public static class LinuxServerRestart
    {
        /// <summary>The config variable read when --config is blank.</summary>
        public const string ConfigVariable = "ANTIPHON_LINUX_SERVER_CONFIG";

        /// <summary>The docker override variable (a path; blank or docker: docker on PATH).</summary>
        public const string DockerVariable = "ANTIPHON_LINUX_SERVER_DOCKER";

        // PowerShell's -match and -notmatch: case-insensitive, $ also before a final newline.
        private static readonly Regex Sha40 = new Regex("^[0-9a-fA-F]{40}$", RegexOptions.IgnoreCase);
        private static readonly Regex Sha64 = new Regex("^[0-9a-fA-F]{64}$", RegexOptions.IgnoreCase);
        private static readonly Regex ContainerName = new Regex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$", RegexOptions.IgnoreCase);
        private static readonly Regex ImageRef = new Regex("^[A-Za-z0-9][A-Za-z0-9._:/-]{0,200}$", RegexOptions.IgnoreCase);
        private static readonly string[] BannedContainers = { "session-runner", "antiphon-runner", "antiphon-postgres", "fake-gateway" };

        private static readonly JsonDocumentOptions JsonRead = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 1024,
        };

        private sealed class GateStop : Exception
        {
            public GateStop(int code) => Code = code;

            public int Code { get; }
        }

        // A terminating error: pwsh -File printed the error record and exited 1.
        private sealed class GateFault : Exception
        {
            public GateFault(string message) : base(message) { }
        }

        private sealed record Probe(string Kind, string Observed);

        private sealed record Replacement(string Kind, string PreviousImage, string TagImage);

        private sealed record DockerResult(int Code, string Output);

        // ---- the checks (the script's functions) ----

        /// <summary>Test-GateSha: a 40- or 64-character hex SHA (either case; a final newline passes, as $ matched before it).</summary>
        public static bool IsSha(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return Sha40.IsMatch(value) || Sha64.IsMatch(value);
        }

        /// <summary>Test-GateContainer: a docker name of at most 64 characters that is not a runner, Postgres, gateway or phone container.</summary>
        public static bool ContainerAllowed(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (!ContainerName.IsMatch(name)) return false;
            var lower = name.ToLowerInvariant();
            foreach (var banned in BannedContainers)
                if (PsEquals(lower, banned)) return false;
            foreach (var word in new[] { "runner", "postgres", "gateway", "phone" })
                if (lower.Contains(word, StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>
        /// Read-GateConfig's parse: each trimmed line that is not blank or a comment needs '=' after its first character (else the
        /// whole file is void: null); keys and values are trimmed, keys ignore case and a later key wins.
        /// </summary>
        public static IReadOnlyDictionary<string, string> ParseConfig(IEnumerable<string> lines)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var text = line.Trim();
                if (text.StartsWith("#", StringComparison.Ordinal)) continue;
                var index = text.IndexOf('=', StringComparison.Ordinal);
                if (index < 1) return null;
                map[text.Substring(0, index).Trim()] = text.Substring(index + 1).Trim();
            }
            return map;
        }

        /// <summary>
        /// Resolve-GateIdentity: ok with the lower-cased SHA when the body's version (ConvertFrom-Json, the property found ignoring
        /// case) is a full SHA; otherwise malformed with the version as [string] gave it (empty for a blank or unreadable body, a
        /// body that is not one object, or no version property). Read as standard JSON with comments and trailing commas (the
        /// script's Newtonsoft reader also took single quotes, bare names, NaN and hex numbers: those read as malformed here); keys
        /// that differ only in case, or an empty key, anywhere in the body are unreadable, as ConvertFrom-Json refused them.
        /// A body that is JSON null, [] or [null] throws InvalidOperationException ("Cannot index into a null array."): the
        /// script's terminating error.
        /// </summary>
        public static LinuxServerIdentity Identity(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return new LinuxServerIdentity("malformed", "");
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(body, JsonRead);
            }
            catch (JsonException)
            {
                return new LinuxServerIdentity("malformed", "");
            }
            using (document)
            {
                var root = document.RootElement;
                if (!Convertible(root)) return new LinuxServerIdentity("malformed", "");
                // ConvertFrom-Json enumerated an array: none or one null item is $null, one item is that item, more is an array.
                if (root.ValueKind == JsonValueKind.Array)
                {
                    var items = root.EnumerateArray().ToList();
                    if (items.Count == 0 || (items.Count == 1 && items[0].ValueKind == JsonValueKind.Null))
                        throw new InvalidOperationException("Cannot index into a null array.");
                    if (items.Count > 1) return new LinuxServerIdentity("malformed", "");
                    root = items[0];
                }
                if (root.ValueKind == JsonValueKind.Null) throw new InvalidOperationException("Cannot index into a null array.");
                var sha = "";
                if (root.ValueKind == JsonValueKind.Object)
                {
                    JsonElement? version = null;
                    foreach (var property in root.EnumerateObject())
                        if (string.Equals(property.Name, "version", StringComparison.OrdinalIgnoreCase)) version = property.Value;
                    if (version.HasValue) sha = Text(version.Value);
                }
                if (!IsSha(sha)) return new LinuxServerIdentity("malformed", sha);
                return new LinuxServerIdentity("ok", sha.ToLowerInvariant());
            }
        }

        /// <summary>
        /// Invoke-WebRequest's choice of body: a text, XML or JSON content type (text/*, application/json, application/xml,
        /// the javascript types, +json, +xml) is the decoded text (its charset, else UTF-8); anything else, an absent type
        /// included, was a byte array whose [string] is the byte values joined by spaces.
        /// </summary>
        public static string Body(string contentType, byte[] bytes)
        {
            bytes ??= Array.Empty<byte>();
            var signature = (contentType ?? "").Split(';')[0].Trim().ToUpperInvariant();
            if (!IsText(signature)) return string.Join(" ", bytes.Select(b => b.ToString(CultureInfo.InvariantCulture)));
            var encoding = Encoding.UTF8;
            foreach (var part in (contentType ?? "").Split(';').Skip(1))
            {
                var pair = part.Split('=', 2);
                if (pair.Length != 2 || !string.Equals(pair[0].Trim(), "charset", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    encoding = Encoding.GetEncoding(pair[1].Trim().Trim('"'));
                }
                catch (ArgumentException)
                {
                    encoding = Encoding.UTF8;
                }
            }
            return encoding.GetString(bytes);
        }

        private static bool IsText(string signature)
        {
            if (signature.Length == 0) return false;
            if (signature.StartsWith("TEXT/", StringComparison.Ordinal)) return true;
            if (signature == "APPLICATION/XML" || signature == "APPLICATION/XML-EXTERNAL-PARSED-ENTITY" || signature == "APPLICATION/XML-DTD"
                || signature.EndsWith("+XML", StringComparison.Ordinal)) return true;
            return signature == "APPLICATION/JSON" || signature == "APPLICATION/X-JAVASCRIPT" || signature == "APPLICATION/JAVASCRIPT"
                || signature.EndsWith("+JSON", StringComparison.Ordinal);
        }

        /// <summary>
        /// The production HTTP (Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 -MaximumRedirection 0 -SkipHttpErrorCheck): the
        /// system proxy settings, no redirect followed, any status returned with its body (Body), a failure status 0.
        /// </summary>
        public static LinuxServerHttp Http()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
            return url =>
            {
                try
                {
                    using var response = client.GetAsync(url).GetAwaiter().GetResult();
                    var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    var type = response.Content.Headers.ContentType?.ToString() ?? "";
                    return new LinuxServerHttpAnswer((int)response.StatusCode, Body(type, bytes));
                }
                catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is OperationCanceledException
                    || e is InvalidOperationException || e is UriFormatException || e is IOException)
                {
                    return new LinuxServerHttpAnswer(0, "");
                }
            };
        }

        // ConvertFrom-Json refused keys that differ only in case, and an empty key, at any depth.
        private static bool Convertible(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name.Length == 0) return false;
                        if (names.TryGetValue(property.Name, out var seen) && !string.Equals(seen, property.Name, StringComparison.Ordinal)) return false;
                        names[property.Name] = property.Name;
                        if (!Convertible(property.Value)) return false;
                    }
                    return true;
                case JsonValueKind.Array:
                    return element.EnumerateArray().All(Convertible);
                default:
                    return true;
            }
        }

        // [string] of a ConvertFrom-Json value: a string itself, a number in invariant digits, True or False, null empty, an array's
        // items joined by spaces (an object item empty, a nested array System.Object[]), an object as @{name=value; ...} (a nested
        // object empty, an array System.Object[]).
        private static string Text(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Array:
                    return string.Join(" ", value.EnumerateArray().Select(Item));
                case JsonValueKind.Object:
                    return "@{" + string.Join("; ", Properties(value).Select(pair => pair.Key + "=" + Item(pair.Value))) + "}";
                default:
                    return Scalar(value);
            }
        }

        private static string Item(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Array => "System.Object[]",
            JsonValueKind.Object => "",
            _ => Scalar(value),
        };

        // The object's properties once each, a repeated name keeping the last value in the first one's place.
        private static IEnumerable<KeyValuePair<string, JsonElement>> Properties(JsonElement value)
        {
            var order = new List<string>();
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!values.ContainsKey(property.Name)) order.Add(property.Name);
                values[property.Name] = property.Value;
            }
            return order.Select(name => new KeyValuePair<string, JsonElement>(name, values[name]));
        }

        private static string Scalar(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString();
                case JsonValueKind.Number:
                    var raw = value.GetRawText();
                    if (long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)) return whole.ToString(CultureInfo.InvariantCulture);
                    if (System.Numerics.BigInteger.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var big)) return big.ToString(CultureInfo.InvariantCulture);
                    return value.TryGetDouble(out var real) ? real.ToString(CultureInfo.InvariantCulture) : raw;
                case JsonValueKind.True:
                    return "True";
                case JsonValueKind.False:
                    return "False";
                default:
                    return "";
            }
        }

        internal static bool PsEquals(string left, string right) =>
            CultureInfo.InvariantCulture.CompareInfo.Compare(left ?? "", right ?? "", CompareOptions.IgnoreCase) == 0;

        // ---- the run ----

        /// <summary>
        /// Runs the gate: the refusals (REFUSED: ... / Nothing was restarted., exit 3), then the probe (VERIFIED ..., exit 0; a
        /// what-if /health that is not 200, exit 1; unverified, exit 5, the lock written). A terminating error is one host.Error
        /// line, exit 1.
        /// </summary>
        public static int Run(LinuxServerRestartOptions options, NightlyHost host, IFileSystem files, LinuxServerRestartSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams?.Http == null) throw new ArgumentNullException(nameof(seams));
            var resolve = seams.Resolve ?? new OperatorToolOs(host.Variables).Find;
            try
            {
                return Gate(options, host, files, seams.Http, resolve);
            }
            catch (GateStop stop)
            {
                return stop.Code;
            }
            catch (Exception e) when (e is GateFault || e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                || e is NotSupportedException || e is InvalidOperationException || e is System.Security.SecurityException)
            {
                host.Error.WriteLine(e.Message);
                return 1;
            }
        }

        private static void Refuse(NightlyHost host, string message)
        {
            host.Out.WriteLine("REFUSED: " + message);
            host.Out.WriteLine("Nothing was restarted.");
            throw new GateStop(3);
        }

        private static int Gate(LinuxServerRestartOptions options, NightlyHost host, IFileSystem files, LinuxServerHttp http, Func<string, string> resolve)
        {
            var configPath = options.Config ?? "";
            if (string.IsNullOrWhiteSpace(configPath)) configPath = host.Variables(ConfigVariable) ?? "";
            if (string.IsNullOrWhiteSpace(configPath)) Refuse(host, "Linux server is not configured.");
            if (options.Execute && options.WhatIf) Refuse(host, "-Execute and -WhatIf were both set.");
            var mode = options.Execute ? "execute" : "whatif";

            var config = ReadConfig(files, configPath);
            if (config == null || !config.ContainsKey("baseUrl") || !config.ContainsKey("container") || !config.ContainsKey("lockPath"))
                Refuse(host, "Linux server config is missing baseUrl, container, or lockPath.");
            var baseUrl = config["baseUrl"];
            var container = config["container"];
            var lockPath = config["lockPath"];

            var expectedSha = options.ExpectedServerSha ?? "";
            if (!IsSha(expectedSha)) Refuse(host, "-ExpectedServerSha is not a full 40- or 64-character SHA.");
            var expected = expectedSha.ToLowerInvariant();

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || !PsEquals(uri.Scheme, "http") || uri.UserInfo.Length > 0)
                Refuse(host, "baseUrl must be an http URL without user info.");
            if (!PsEquals(uri.Host, "127.0.0.1") && !PsEquals(uri.Host, "localhost")) Refuse(host, "baseUrl must use the host loopback.");
            if (uri.IsDefaultPort || uri.Port < 1) Refuse(host, "baseUrl must name an explicit port.");
            if (uri.Port >= 17200 && uri.Port <= 17209)
                Refuse(host, "baseUrl port " + uri.Port.ToString(CultureInfo.InvariantCulture) + " is a desktop AppHost port.");
            if (!ContainerAllowed(container)) Refuse(host, "container " + container + " is outside this gate.");
            if (!files.Path.IsPathRooted(lockPath) || lockPath.Contains("..", StringComparison.Ordinal)) Refuse(host, "lockPath must be a rooted path.");
            var lockMaxAge = Math.Max(1, options.LockMaxAgeMinutes);

            if (LockFresh(host, files, lockPath, lockMaxAge)) Refuse(host, "a restart lock is held at " + lockPath + ".");

            if (mode == "execute")
            {
                var pre = ProbeServer(http, baseUrl, expected);
                if (pre.Kind == "ok")
                {
                    RemoveLock(files, lockPath);
                    host.Out.WriteLine("already at expected SHA");
                    host.Out.WriteLine("VERIFIED sha=" + pre.Observed + " mode=execute");
                    return 0;
                }
                var reason = pre.Kind;
                var previousImage = "";
                var tagImage = "";
                if (pre.Kind == "mismatch")
                {
                    var decision = ResolveReplacement(host, files, resolve, container);
                    reason = decision.Kind;
                    previousImage = decision.PreviousImage;
                    tagImage = decision.TagImage;
                }
                if (reason != "mismatch" && reason != "malformed" && reason != "unavailable" && reason != "health" && reason != "image-moved") reason = "unavailable";
                return Unverified(host, files, reason, expected, pre.Observed, mode, previousImage, tagImage, lockPath, container);
            }

            // Wait-GateProbe without its poll: one probe.
            var probe = ProbeServer(http, baseUrl, expected);
            if (probe.Kind == "ok")
            {
                RemoveLock(files, lockPath);
                host.Out.WriteLine("VERIFIED sha=" + probe.Observed + " mode=" + mode);
                return 0;
            }
            if (probe.Kind == "health")
            {
                host.Out.WriteLine("REFUSED: /health was not 200.");
                host.Out.WriteLine("Nothing was restarted.");
                return 1;
            }
            var whatIfReason = probe.Kind;
            if (whatIfReason != "mismatch" && whatIfReason != "malformed" && whatIfReason != "unavailable") whatIfReason = "unavailable";
            return Unverified(host, files, whatIfReason, expected, probe.Observed, mode, "", "", lockPath, container);
        }

        // Read-GateConfig: null for a blank or absent path; Get-Content of a directory was a terminating error.
        private static IReadOnlyDictionary<string, string> ReadConfig(IFileSystem files, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Exists(files, path)) return null;
            if (files.Directory.Exists(path))
                throw new GateFault("Unable to get content because it is a directory: '" + path + "'. Please use 'Get-ChildItem' instead.");
            return ParseConfig(files.File.ReadAllLines(path));
        }

        // Test-Path -LiteralPath: a file or a directory.
        private static bool Exists(IFileSystem files, string path) => files.File.Exists(path) || files.Directory.Exists(path);

        // Test-GateLockFresh: the lock (file or directory) was last written less than the maximum age ago (a future time is fresh).
        private static bool LockFresh(NightlyHost host, IFileSystem files, string path, int maxAgeMinutes)
        {
            if (!Exists(files, path)) return false;
            var written = files.File.Exists(path) ? files.File.GetLastWriteTimeUtc(path) : files.Directory.GetLastWriteTimeUtc(path);
            return (host.UtcNow - written).TotalMinutes < maxAgeMinutes;
        }

        // Write-GateLock: the parent directory, then six ASCII lines (Set-Content -Encoding ascii: each line and the platform newline).
        private static void WriteLock(NightlyHost host, IFileSystem files, string path, string reason, string expected, string observed, string mode, string previousImage)
        {
            var parent = files.Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent)) files.Directory.CreateDirectory(parent);
            if (files.Directory.Exists(path))
                throw new GateFault("Unable to clear content of '" + path + "' because it is a directory. Clear-Content is only supported on files.");
            var lines = new[]
            {
                "reason=" + reason,
                "expected=" + expected,
                "observed=" + observed,
                "mode=" + mode,
                "previousImage=" + previousImage,
                "utc=" + host.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            };
            files.File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.ASCII);
        }

        // Remove-GateLock: Remove-Item -Force removes a file (read-only included) or an empty directory; a directory with entries
        // asked for -Recurse, which no answer gave (pwsh -NonInteractive failed): a terminating error here.
        private static void RemoveLock(IFileSystem files, string path)
        {
            if (files.File.Exists(path))
            {
                files.File.SetAttributes(path, FileAttributes.Normal);
                files.File.Delete(path);
                return;
            }
            if (!files.Directory.Exists(path)) return;
            if (files.Directory.EnumerateFileSystemEntries(path).Any())
                throw new GateFault("The item at " + path + " has children and the Recurse parameter was not specified.");
            files.Directory.Delete(path);
        }

        // Invoke-GateProbe: /health 200, then /api/version 200 with a full SHA equal to the expected one.
        private static Probe ProbeServer(LinuxServerHttp http, string baseUrl, string expected)
        {
            var root = baseUrl.TrimEnd('/');
            var health = Get(http, root + "/health");
            if (health.StatusCode != 200) return new Probe("health", "");
            var version = Get(http, root + "/api/version");
            if (version.StatusCode != 200) return new Probe("unavailable", "");
            var identity = Identity(version.Body);
            if (identity.Kind != "ok") return new Probe("malformed", identity.Sha);
            if (!PsEquals(identity.Sha, expected.ToLowerInvariant())) return new Probe("mismatch", identity.Sha);
            return new Probe("ok", identity.Sha);
        }

        private static LinuxServerHttpAnswer Get(LinuxServerHttp http, string url) => http(url) ?? new LinuxServerHttpAnswer(0, "");

        private static int Unverified(NightlyHost host, IFileSystem files, string reason, string expected, string observed, string mode, string previousImage,
            string tagImage, string lockPath, string container)
        {
            WriteLock(host, files, lockPath, reason, expected, observed, mode, previousImage);
            host.Out.WriteLine("REFUSED: server build unverified (exit 5).");
            host.Out.WriteLine("  expected SHA: " + expected);
            host.Out.WriteLine("  observed SHA: " + observed);
            host.Out.WriteLine("  reason: " + reason);
            host.Out.WriteLine("  lock: " + lockPath);
            if (reason == "image-moved")
            {
                host.Out.WriteLine("  tag image: " + tagImage);
                host.Out.WriteLine("  The image tag moved. This gate does not recreate the container.");
            }
            host.Out.WriteLine("Nothing was restarted.");
            if (mode == "execute")
            {
                host.Out.WriteLine("The serving container was left running.");
                host.Out.WriteLine("recovery: docker start " + container);
            }
            return 5;
        }

        // Resolve-GateReplacement: the running image and the tag's current image; a moved tag is image-moved, anything else mismatch.
        private static Replacement ResolveReplacement(NightlyHost host, IFileSystem files, Func<string, string> resolve, string container)
        {
            var docker = host.Variables(DockerVariable) ?? "";
            if (string.IsNullOrWhiteSpace(docker)) docker = "docker";
            var byName = PsEquals(docker, "docker");
            if (!byName && !Exists(files, docker)) return new Replacement("mismatch", "", "");
            // The call operator: a name is looked up as a command, a path is run as it is.
            var tool = byName ? resolve(docker) : docker;
            var image = Docker(host, tool, "inspect", "--format", "{{.Image}}", container);
            var reference = Docker(host, tool, "inspect", "--format", "{{.Config.Image}}", container);
            var running = "";
            if (image.Code == 0 && !string.IsNullOrWhiteSpace(image.Output)) running = FirstLine(image.Output);
            if (image.Code != 0 || reference.Code != 0 || string.IsNullOrWhiteSpace(running)) return new Replacement("mismatch", running, "");
            var tagReference = string.IsNullOrWhiteSpace(reference.Output) ? "" : FirstLine(reference.Output);
            if (!ImageRef.IsMatch(tagReference)) return new Replacement("mismatch", running, "");
            var tag = Docker(host, tool, "image", "inspect", "--format", "{{.Id}}", tagReference);
            var tagId = "";
            if (tag.Code == 0 && !string.IsNullOrWhiteSpace(tag.Output)) tagId = FirstLine(tag.Output);
            if (tag.Code != 0 || string.IsNullOrWhiteSpace(tagId) || PsEquals(tagId, running)) return new Replacement("mismatch", running, "");
            return new Replacement("image-moved", running, tagId);
        }

        private static string FirstLine(string text) => text.Split('\n')[0].Trim();

        // Invoke-GateDocker: @(& docker @arguments): the stdout lines joined and trimmed, the exit code; docker's stderr reaches the
        // console's; a docker that cannot start is code 1 with no output.
        private static DockerResult Docker(NightlyHost host, string tool, params string[] arguments)
        {
            if (string.IsNullOrEmpty(tool)) return new DockerResult(1, "");
            NightlyLaunchResult result;
            try
            {
                result = host.Launch(new NightlyLaunch(tool, arguments, "", CaptureOutput: true));
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is FileNotFoundException || e is UnauthorizedAccessException)
            {
                return new DockerResult(1, "");
            }
            foreach (var line in (result.StandardErrorText ?? "").Split('\n').Where(line => line.Length > 0)) host.Error.WriteLine(line);
            var output = (result.StandardOutputText ?? "").Replace("\r\n", "\n").TrimEnd('\n');
            return new DockerResult(result.ExitCode, output.Trim());
        }
    }
}
