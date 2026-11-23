// CARD-0398: operator issue, rotate, revoke and list for a named Delegation Capability. It replaced scripts/capability.ps1
// (deleted 2026-11-23, unit 24 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md); the CLI runs it as
// `antiphon-scripts capability <verb> [...]` (scripts\antiphon.cmd capability on Windows, scripts/antiphon capability on sh).
//
//   capability issue  --name <n> --roots <dir>[,dir...] [--board-id <guid>] [--project-id <guid>]
//   capability rotate --name <n> | --id <guid>
//   capability revoke --name <n> | --id <guid>
//   capability list
//
// The CLI is the only writer of the capability store: the server answers the raw token once on issue and rotate, and this
// command writes it, DPAPI-protected for the current Windows user, to <store>\<name>.dpapi and prints the name, the path and
// the roots, never the token. delegate.ps1 -Capability <name> reads it (ProtectedData, CurrentUser, no entropy). The store is
// ANTIPHON_CAPABILITY_STORE, else %LOCALAPPDATA%\Antiphon\capabilities (the CLI passes it in). DPAPI is Windows only: off
// Windows the CLI passes no protector and issue and rotate refuse before any request, so no token is issued that cannot be
// stored. No task token is sent (the script sent none). Exit 0 done; 1 a failed request, an unknown or ambiguous name, an
// answer without a token or no protector (one stderr line); usage errors are NightlyUsageException (the CLI's exit 2). Plain
// C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The capability command's options (capability.ps1 &lt;verb&gt; -Name -Roots -BoardId -ProjectId -Id).</summary>
    public sealed class CapabilityCliOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "capability <issue --name <n> --roots <dir>[,dir...] [--board-id <guid>] [--project-id <guid>] | rotate (--name <n> | --id <guid>)"
            + " | revoke (--name <n> | --id <guid>) | list>";

        private static readonly string[] Verbs = { "issue", "rotate", "revoke", "list" };
        private static readonly string[] Valued = { "--name", "--roots", "--board-id", "--project-id", "--id" };

        /// <summary>issue, rotate, revoke or list.</summary>
        public string Verb { get; init; } = "list";

        /// <summary>The capability's name (issue; rotate and revoke without --id).</summary>
        public string Name { get; init; } = "";

        /// <summary>issue: the roots the capability may delegate under.</summary>
        public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();

        /// <summary>issue: the board it is scoped to; empty: none.</summary>
        public string BoardId { get; init; } = "";

        /// <summary>issue: the project it is scoped to; empty: none.</summary>
        public string ProjectId { get; init; } = "";

        /// <summary>rotate and revoke: the capability's id; empty: looked up by name.</summary>
        public string Id { get; init; } = "";

        /// <summary>The API base (ANTIPHON_API), without a trailing slash.</summary>
        public string Api { get; init; } = OperatorApi.DefaultApi;

        /// <summary>The capability store directory.</summary>
        public string Store { get; init; } = "";

        /// <summary>
        /// The verb and the flags after the command name; the API and the store come from the CLI. A missing verb, issue
        /// without --name or --roots, rotate or revoke without --name or --id, or a flag the verb does not take is a
        /// NightlyUsageException.
        /// </summary>
        public static CapabilityCliOptions Parse(IReadOnlyList<string> arguments, string api, string store)
        {
            var verb = BoardCli.Verb(arguments, Verbs, null, out var rest);
            var flags = NightlyArgs.Parse(rest, Valued, Array.Empty<string>());
            switch (verb)
            {
                case "issue":
                    BoardCli.Only(flags, verb, Valued, new[] { "--name", "--roots", "--board-id", "--project-id" });
                    if (string.IsNullOrWhiteSpace(flags.Value("--name"))) throw new NightlyUsageException("issue requires --name");
                    if (flags.List("--roots").Count == 0) throw new NightlyUsageException("issue requires --roots");
                    break;
                case "list":
                    BoardCli.Only(flags, verb, Valued, Array.Empty<string>());
                    break;
                default:
                    BoardCli.Only(flags, verb, Valued, new[] { "--name", "--id" });
                    if (string.IsNullOrWhiteSpace(flags.Value("--name")) && string.IsNullOrWhiteSpace(flags.Value("--id")))
                        throw new NightlyUsageException(verb + " requires --name or --id");
                    break;
            }
            return new CapabilityCliOptions
            {
                Verb = verb,
                Name = flags.Value("--name", ""),
                Roots = flags.List("--roots"),
                BoardId = flags.Value("--board-id", ""),
                ProjectId = flags.Value("--project-id", ""),
                Id = flags.Value("--id", ""),
                Api = OperatorApi.Base(api),
                Store = store ?? "",
            };
        }
    }

    /// <summary>The Delegation Capability issue, rotate, revoke and list (capability.ps1).</summary>
    public static class CapabilityCli
    {
        private const string Collection = "/api/delegation-capabilities";

        /// <summary>
        /// Runs the verb: the lines on host.Out; exit 0, or 1 after one line on host.Error. protect seals a token for the store
        /// (ProtectCurrentUser in production; null off Windows, where issue and rotate refuse before any request).
        /// </summary>
        public static int Run(CapabilityCliOptions options, NightlyHost host, IFileSystem files, NightlyReportHttp http, Func<byte[], byte[]> protect)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            if ((options.Verb == "issue" || options.Verb == "rotate") && protect == null)
                return BoardCli.Refuse(host, "capability " + options.Verb + " writes a DPAPI-protected token and runs on Windows only.");

            JsonNode answer;
            string failure;
            switch (options.Verb)
            {
                case "list":
                {
                    if (!BoardCli.Send(http, options.Api, "GET", Collection, headers, null, out answer, out failure)) return BoardCli.Refuse(host, failure);
                    var rows = BoardCli.Items(answer);
                    if (rows.Count == 0)
                    {
                        host.Out.WriteLine("No delegation capabilities.");
                        return 0;
                    }
                    foreach (var row in rows)
                    {
                        var state = BoardCli.Truthy(BoardCli.Get(row, "revokedAt")) ? "revoked" : BoardCli.Truthy(BoardCli.Get(row, "rotatedAt")) ? "rotated" : "active";
                        host.Out.WriteLine(BoardCli.Text(BoardCli.Get(row, "name")) + "  " + BoardCli.Text(BoardCli.Get(row, "id")) + "  " + state + "  roots: "
                            + BoardCli.Join(BoardCli.Get(row, "roots")));
                    }
                    return 0;
                }
                case "issue":
                {
                    var body = new JsonObject { ["name"] = options.Name, ["roots"] = new JsonArray(options.Roots.Select(root => (JsonNode)JsonValue.Create(root)).ToArray()) };
                    if (!string.IsNullOrWhiteSpace(options.BoardId)) body["boardId"] = options.BoardId;
                    if (!string.IsNullOrWhiteSpace(options.ProjectId)) body["projectId"] = options.ProjectId;
                    if (!BoardCli.Send(http, options.Api, "POST", Collection, headers, OperatorApi.Json(body), out answer, out failure)) return BoardCli.Refuse(host, failure);
                    var name = BoardCli.Text(BoardCli.Get(answer, "name"));
                    var path = Store(host, files, options.Store, name, BoardCli.Text(BoardCli.Get(answer, "token")), protect);
                    if (path == null) return 1;
                    host.Out.WriteLine("Issued capability '" + name + "' stored at " + path);
                    host.Out.WriteLine("Roots: " + BoardCli.Join(BoardCli.Get(answer, "roots")));
                    return 0;
                }
                default:
                {
                    var id = ResolveId(options, host, http, headers);
                    if (id == null) return 1;
                    var route = Collection + "/" + Uri.EscapeDataString(id) + "/" + options.Verb;
                    if (!BoardCli.Send(http, options.Api, "POST", route, headers, null, out answer, out failure)) return BoardCli.Refuse(host, failure);
                    var name = BoardCli.Text(BoardCli.Get(answer, "name"));
                    if (options.Verb == "revoke")
                    {
                        host.Out.WriteLine("Revoked capability '" + name + "'");
                        return 0;
                    }
                    var path = Store(host, files, options.Store, name, BoardCli.Text(BoardCli.Get(answer, "token")), protect);
                    if (path == null) return 1;
                    host.Out.WriteLine("Rotated capability '" + name + "' stored at " + path);
                    return 0;
                }
            }
        }

        // --id as given; else the one capability of that name (ignoring case, as -eq compared): the unrevoked one, or the only
        // one when every match is revoked. null after one stderr line when none or several match.
        private static string ResolveId(CapabilityCliOptions options, NightlyHost host, NightlyReportHttp http, IReadOnlyDictionary<string, string> headers)
        {
            if (!string.IsNullOrWhiteSpace(options.Id)) return options.Id;
            if (!BoardCli.Send(http, options.Api, "GET", Collection, headers, null, out var answer, out var failure))
            {
                host.Error.WriteLine(failure);
                return null;
            }
            var named = BoardCli.Items(answer).Where(row => string.Equals(BoardCli.Text(BoardCli.Get(row, "name")), options.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            var hits = named.Where(row => !BoardCli.Truthy(BoardCli.Get(row, "revokedAt"))).ToList();
            if (hits.Count == 0) hits = named;
            if (hits.Count == 0)
            {
                host.Error.WriteLine("No capability named '" + options.Name + "'.");
                return null;
            }
            if (hits.Count > 1)
            {
                host.Error.WriteLine("Capability name '" + options.Name + "' is ambiguous; pass --id.");
                return null;
            }
            return BoardCli.Text(BoardCli.Get(hits[0], "id"));
        }

        // Writes <store>\<name>.dpapi (the store created first) and answers its path; null after one stderr line when the answer
        // has no token or a name that is not a file name. The token is never printed.
        private static string Store(NightlyHost host, IFileSystem files, string store, string name, string token, Func<byte[], byte[]> protect)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                host.Error.WriteLine("issue/rotate response did not include a token for '" + name + "'.");
                return null;
            }
            if (name.Length == 0 || name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(new[] { '/', '\\' }) >= 0)
            {
                host.Error.WriteLine("The capability name '" + name + "' is not a file name; the token was not stored.");
                return null;
            }
            files.Directory.CreateDirectory(store);
            var path = Path.Combine(store, name + ".dpapi");
            files.File.WriteAllBytes(path, protect(Encoding.UTF8.GetBytes(token)));
            return path;
        }

        /// <summary>
        /// DPAPI for the current user (CryptProtectData, no entropy, no UI): what ProtectedData.Protect(data, null,
        /// DataProtectionScope.CurrentUser) writes, so ProtectedData.Unprotect reads it back. Windows only.
        /// </summary>
        public static byte[] ProtectCurrentUser(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI is Windows only.");
            var input = new DataBlob { Size = data.Length, Data = Marshal.AllocHGlobal(Math.Max(1, data.Length)) };
            try
            {
                Marshal.Copy(data, 0, input.Data, data.Length);
                var output = new DataBlob();
                if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    var sealedBytes = new byte[output.Size];
                    Marshal.Copy(output.Data, sealedBytes, 0, output.Size);
                    return sealedBytes;
                }
                finally
                {
                    LocalFree(output.Data);
                }
            }
            finally
            {
                // The plain token does not outlive the call in unmanaged memory.
                Marshal.Copy(new byte[data.Length], 0, input.Data, data.Length);
                Marshal.FreeHGlobal(input.Data);
            }
        }

        private const int UiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob dataIn, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
