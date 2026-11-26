// CARD-0817, CARD-1245: refresh the server2 runners' GitHub token (unit 51 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice C; it replaced scripts/refresh-server2-github-token.ps1,
// which nothing started by path: docs/agent-credentials.md tells the operator to run it after a rotation). Operator-only: the
// vault is read through its relay session, never through argv. The CLI runs it as
//
//   antiphon-scripts refresh-server2-github-token [--server mc@server2]
//
// In order, as the script:
//
// 1. The relay session: BW_SESSION from the host's environment, else ~/.bw-session (trimmed). Without one, or without bw or ssh
//    on PATH: the skipped warning, exit 2 (nothing read, nothing sent).
// 2. `bw get password 97819ca3-710f-43f8-99ce-b4da013e32c1 --nointeraction` with BW_SESSION in bw's environment (its stderr
//    discarded). A failed read, an empty value or one with whitespace: the skipped warning, exit 2.
// 3. The upload: `ssh -o BatchMode=yes -o ConnectTimeout=30 <server> <remote>`, the token and a line end on ssh's stdin and
//    nowhere else (no argv, no environment, no file, no output). The remote command is `sudo -n sh -c '<payload>'` with the
//    payload's carriage returns removed (CARD-1245: sh on server2 aborts on `set -eu\r`): a unique sibling temporary file in
//    /home/mc/antiphon-server2/secrets/github-token, owner 1654:1654 and mode 0400 before an atomic rename over `token`. A
//    non-zero exit: the not-confirmed warning and the remote stderr (one line, control characters as spaces, the token
//    redacted, at most 2000 characters), exit 1.
// 4. The verification: the same ssh with a payload that prints only `stat -c '%u:%g %a'` and `present=true|false` (never the
//    contents). Anything but exactly "1654:1654 400" and "present=true": the verification warning and the remote stderr, exit 1.
//    Otherwise those two lines are the only stdout, exit 0.
//
// An exception never renders (it could carry a credential): before the upload it is the skipped warning, exit 2; after, the
// not-confirmed warning, exit 1. Warnings are "WARNING: <text>" lines on the host's output, where pwsh -File wrote them. The
// tools are IOperatorToolSeams', the session file IFileSystem's. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The refresh-server2-github-token command's options (the script's -Server in kebab case).</summary>
    public sealed class GithubTokenRefreshOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "refresh-server2-github-token [--server mc@server2]";

        /// <summary>The server the script defaulted to.</summary>
        public const string DefaultServer = "mc@server2";

        // The script's ValidatePattern (which ignores case).
        private static readonly Regex ServerPattern = new Regex("^(?:[A-Za-z0-9_][A-Za-z0-9_.-]*@)?[A-Za-z0-9][A-Za-z0-9.-]*$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>The ssh destination: [user@]host.</summary>
        public string Server { get; init; } = DefaultServer;

        /// <summary>The home directory that holds .bw-session.</summary>
        public string Home { get; init; } = "";

        /// <summary>The flags after the command name; the home directory comes from the CLI. A server outside the pattern is a usage error.</summary>
        public static GithubTokenRefreshOptions Parse(IReadOnlyList<string> arguments, string home)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--server" }, Array.Empty<string>());
            var server = flags.Value("--server", DefaultServer) ?? "";
            if (!ServerPattern.IsMatch(server)) throw new NightlyUsageException("--server must be [user@]host, not '" + server + "'");
            return new GithubTokenRefreshOptions { Server = server, Home = home ?? "" };
        }
    }

    /// <summary>The server2 GitHub token refresh (refresh-server2-github-token.ps1's body; see the file comment).</summary>
    public static class GithubTokenRefresh
    {
        /// <summary>The token is on server2 with its owner and mode verified.</summary>
        public const int Refreshed = 0;

        /// <summary>The upload or the verification was not confirmed.</summary>
        public const int NotConfirmed = 1;

        /// <summary>No relay session, no bw or ssh, or no readable token: nothing was sent.</summary>
        public const int Skipped = 2;

        /// <summary>The vault item "GitHub PAT - server2 push" (its password field).</summary>
        public const string VaultItem = "97819ca3-710f-43f8-99ce-b4da013e32c1";

        /// <summary>The skipped warning.</summary>
        public const string SkippedWarning = "GitHub token refresh skipped, previous file unchanged: unlock the vault relay and retry.";

        /// <summary>The upload's warning.</summary>
        public const string UploadWarning = "GitHub token refresh was not confirmed over SSH. Write errors before atomic replacement leave the previous file unchanged; a lost connection may require a metadata check.";

        /// <summary>The verification's warning.</summary>
        public const string VerifyWarning = "GitHub token was delivered, but owner/mode/presence verification failed. No file contents were printed.";

        /// <summary>The warning for an exception after the vault read.</summary>
        public const string FailedWarning = "GitHub token refresh was not confirmed; check SSH and the directory metadata before retrying.";

        /// <summary>The upload payload. Nothing changes the old inode until the complete, non-empty replacement has its final owner and mode.</summary>
        public static readonly string UploadPayload = string.Join("\n",
            "set -eu",
            "umask 077",
            "d=/home/mc/antiphon-server2/secrets/github-token",
            "test -d \"$d\" && test ! -L \"$d\"",
            "tmp=$(mktemp \"$d/token.tmp.XXXXXXXX\")",
            "trap 'rm -f -- \"$tmp\"' EXIT HUP INT TERM",
            "cat > \"$tmp\" && test -s \"$tmp\" && chown 1654:1654 \"$tmp\" && chmod 0400 \"$tmp\" && mv -f \"$tmp\" \"$d/token\"");

        /// <summary>The verification payload: owner, group and mode, and presence; never the contents.</summary>
        public static readonly string VerifyPayload = string.Join("\n",
            "set -eu",
            "d=/home/mc/antiphon-server2/secrets/github-token",
            "stat -c '%u:%g %a' \"$d/token\"",
            "if test -s \"$d/token\"; then printf 'present=true\\n'; else printf 'present=false\\n'; fi");

        private static readonly Regex Whitespace = new Regex(@"\s", RegexOptions.CultureInvariant);
        private static readonly Regex Control = new Regex("[\\x00-\\x1f\\x7f]", RegexOptions.CultureInvariant);

        /// <summary>ConvertTo-RemoteCommand: `sudo -n sh -c '&lt;payload&gt;'`, the payload's carriage returns removed and its quotes escaped.</summary>
        public static string RemoteCommand(string payload) =>
            "sudo -n sh -c '" + (payload ?? "").Replace("\r", "").Replace("'", "'\\''") + "'";

        /// <summary>ssh's argv for one remote command.</summary>
        public static IReadOnlyList<string> SshArguments(string server, string remote) =>
            new[] { "-o", "BatchMode=yes", "-o", "ConnectTimeout=30", server, remote };

        /// <summary>
        /// Get-RemoteStderr: the stderr lines trimmed, the empty ones dropped, joined with " | ", control characters as spaces, the
        /// secret (when there is one) as [redacted], at most 2000 characters and "...".
        /// </summary>
        public static string RemoteStderr(IEnumerable<string> errorLines, string secret)
        {
            var lines = (errorLines ?? Array.Empty<string>()).Select(line => (line ?? "").Trim()).Where(line => line.Length > 0);
            var text = Control.Replace(string.Join(" | ", lines), " ");
            if (!string.IsNullOrEmpty(secret)) text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
            if (text.Length > 2000) text = text.Substring(0, 2000) + "...";
            return text;
        }

        /// <summary>The refresh; the exit code (see the file comment).</summary>
        public static int Run(GithubTokenRefreshOptions options, NightlyHost host, IFileSystem files, IOperatorToolSeams tools)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (tools == null) throw new ArgumentNullException(nameof(tools));
            var stage = "vault";
            try
            {
                var session = host.Variables("BW_SESSION");
                if (string.IsNullOrEmpty(session))
                {
                    var pickup = files.Path.Combine(options.Home ?? "", ".bw-session");
                    if (files.File.Exists(pickup) || files.Directory.Exists(pickup)) session = files.File.ReadAllText(pickup).Trim();
                }
                var bw = tools.Find("bw");
                var ssh = tools.Find("ssh");
                if (string.IsNullOrEmpty(session) || bw == null || ssh == null) return Warn(host, SkippedWarning, Skipped);

                // BW_SESSION reaches bw alone; bw's stderr is discarded (2>$null).
                var read = tools.Run(new OperatorToolCall(bw, new[] { "get", "password", VaultItem, "--nointeraction" },
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["BW_SESSION"] = session }));
                var token = string.Join("\n", read.OutputLines ?? Array.Empty<string>()).Trim();
                if (read.ExitCode != 0 || token.Length == 0 || Whitespace.IsMatch(token)) return Warn(host, SkippedWarning, Skipped);

                stage = "upload";
                // The token and the platform's line end on stdin, as the script's pipe wrote it; never argv.
                var upload = tools.Run(new OperatorToolCall(ssh, SshArguments(options.Server, RemoteCommand(UploadPayload)), null, token + Environment.NewLine));
                var detail = RemoteStderr(upload.ErrorLines, token);
                token = "";
                if (upload.ExitCode != 0)
                {
                    Warn(host, UploadWarning, NotConfirmed);
                    if (detail.Length > 0) Warn(host, "Remote stderr (exit " + upload.ExitCode.ToString(CultureInfo.InvariantCulture) + "): " + detail, NotConfirmed);
                    return NotConfirmed;
                }

                stage = "verify";
                var verify = tools.Run(new OperatorToolCall(ssh, SshArguments(options.Server, RemoteCommand(VerifyPayload))));
                var metadata = (verify.OutputLines ?? Array.Empty<string>()).Select(line => line ?? "").ToList();
                if (verify.ExitCode != 0 || metadata.Count != 2 || !string.Equals(metadata[0], "1654:1654 400", StringComparison.Ordinal)
                    || !string.Equals(metadata[1], "present=true", StringComparison.Ordinal))
                {
                    Warn(host, VerifyWarning, NotConfirmed);
                    detail = RemoteStderr(verify.ErrorLines, "");
                    if (detail.Length > 0) Warn(host, "Remote stderr (exit " + verify.ExitCode.ToString(CultureInfo.InvariantCulture) + "): " + detail, NotConfirmed);
                    return NotConfirmed;
                }
                // Only these two validated metadata lines reach stdout.
                foreach (var line in metadata) host.Out.WriteLine(line);
                return Refreshed;
            }
            catch (Exception)
            {
                // Never render an exception or a tool's output: either could carry a credential.
                if (stage == "vault") return Warn(host, SkippedWarning, Skipped);
                return Warn(host, FailedWarning, NotConfirmed);
            }
        }

        private static int Warn(NightlyHost host, string text, int code)
        {
            host.Out.WriteLine("WARNING: " + text);
            return code;
        }
    }
}
