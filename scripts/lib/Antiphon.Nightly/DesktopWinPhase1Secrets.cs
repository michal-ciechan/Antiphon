// CARD-1744 DW-3: the operator-only desktop-win phase 1 secret and server map entry (unit 51 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W8 slice C; it replaced scripts/desktop-win-phase1-secrets.ps1,
// which nothing started by path: the CARD-1744 runbook tells the operator to run it). The CLI runs it as
//
//   antiphon-scripts desktop-win-phase1-secrets [--execute] [--rotate] [--what-if] [--sweep-stranded] [--sweep-backups]
//       [--user-secrets-path <file>] [--secret-file <file>] [--profile-home <dir>] [--phase1-config-path <file>]
//
// A dry run unless --execute; --what-if stays a dry run. --rotate replaces an existing SharedSecret.
//
// The secret is generated in memory and written in this process. `dotnet user-secrets set` takes the value as a process
// argument, so this helper updates %APPDATA%\Microsoft\UserSecrets\antiphon-server\secrets.json (what the production server
// reads) directly; no child process receives the value, and a command line holding it is refused before it starts. The
// user-secrets file is read by this file's own reader (Dw3Json), which keeps strings (a date-shaped one too), numbers (their
// text), nested objects and arrays, and refuses a repeated key.
//
// Nothing that can fail is done after a secret byte is on disk. The new JSON is validated and serialised in memory, each half
// written to an owner-only sibling (<32 hex>.dw3part) in its directory, then swapped (on Windows through an owner-only .dw3bak,
// checked owner-only after the swap). The backup is deleted after the swap and the byte cross-check. On a failure each target
// is compared with the bytes saved before any change, a target that differs is restored and checked, and the run prints
// step=, differs:/kept: for a half it could not restore, changed: none (or changed: phase1-config) and REFUSED: failed. A hard
// kill can leave a .dw3part or .dw3bak; the next run lists those exact names (stranded:) and deletes them only with --execute
// --sweep-stranded (a .dw3bak only with --sweep-backups too; a file newer than 15 minutes is kept as fresh). Saved byte arrays
// are cleared after use. A case-variant key or a nested PhoneHomeRunner object is refused instead of written beside the flat key.
// The owner check uses the first existing ancestor of the secrets directory, which USERNAME must own.
//
// --execute copies scripts/phone-home.phase1.example.json (no secret) to the phase 1 config when that is absent.
//
// Output lines are the script's; exit 0 done (or the dry run), 2 refused (REFUSED: <code> and a reason), 1 an unexpected error
// before the apply (one stderr line, as pwsh -File's terminating error); usage 2 (the CLI). The ACLs are IDesktopWinAcl's,
// icacls, chmod and stat the tool seam's, the files IFileSystem's, the environment and the clock the host's. The script's DW3_*
// environment test seams are DesktopWinPhase1Faults, set by tests only. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The desktop-win-phase1-secrets command's options (desktop-win-phase1-secrets.ps1's parameters in kebab case).</summary>
    public sealed record DesktopWinPhase1Options
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "desktop-win-phase1-secrets [--execute] [--rotate] [--what-if] [--sweep-stranded] [--sweep-backups] "
            + "[--user-secrets-path <file>] [--secret-file <file>] [--profile-home <dir>] [--phase1-config-path <file>]";

        private static readonly string[] Valued = { "--user-secrets-path", "--secret-file", "--profile-home", "--phase1-config-path" };
        private static readonly string[] Switches = { "--execute", "--rotate", "--what-if", "--sweep-stranded", "--sweep-backups" };

        /// <summary>Write the secret and the map (otherwise a dry run).</summary>
        public bool Execute { get; init; }

        /// <summary>Replace an existing SharedSecret and secret file.</summary>
        public bool Rotate { get; init; }

        /// <summary>A dry run even with Execute.</summary>
        public bool WhatIf { get; init; }

        /// <summary>Delete stale stranded .dw3part files (with Execute).</summary>
        public bool SweepStranded { get; init; }

        /// <summary>Delete stale stranded .dw3bak files too.</summary>
        public bool SweepBackups { get; init; }

        /// <summary>The server's user-secrets file; empty: %APPDATA%\Microsoft\UserSecrets\antiphon-server\secrets.json.</summary>
        public string UserSecretsPath { get; init; } = "";

        /// <summary>The runner's secret file; empty: C:\Antiphon\runner\secrets\phone-home.</summary>
        public string SecretFile { get; init; } = "";

        /// <summary>The runner account's profile home; empty: Home, else USERPROFILE.</summary>
        public string ProfileHome { get; init; } = "";

        /// <summary>The phase 1 config; empty: phone-home.phase1.json in the secret file's runner root.</summary>
        public string Phase1ConfigPath { get; init; } = "";

        /// <summary>PowerShell's $HOME: the user's profile directory.</summary>
        public string Home { get; init; } = "";

        /// <summary>The checkout's scripts directory (the script's $PSScriptRoot), which holds phone-home.phase1.example.json.</summary>
        public string ScriptsDirectory { get; init; } = "";

        /// <summary>The flags after the command name; the home and the scripts directory come from the CLI.</summary>
        public static DesktopWinPhase1Options Parse(IReadOnlyList<string> arguments, string home, string scriptsDirectory)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            return new DesktopWinPhase1Options
            {
                Execute = flags.Switch("--execute"),
                Rotate = flags.Switch("--rotate"),
                WhatIf = flags.Switch("--what-if"),
                SweepStranded = flags.Switch("--sweep-stranded"),
                SweepBackups = flags.Switch("--sweep-backups"),
                UserSecretsPath = flags.Value("--user-secrets-path", "") ?? "",
                SecretFile = flags.Value("--secret-file", "") ?? "",
                ProfileHome = flags.Value("--profile-home", "") ?? "",
                Phase1ConfigPath = flags.Value("--phase1-config-path", "") ?? "",
                Home = home ?? "",
                ScriptsDirectory = scriptsDirectory ?? "",
            };
        }
    }

    /// <summary>
    /// The script's DW3_* environment test seams, set by tests only (production leaves every one empty). FailStep fails the step
    /// it names (user-secrets-write, secret-file-commit); ReplaceFault (user-secrets or secret-file) deletes that target after
    /// keeping its bytes in an owner-only .dw3bak and throws; RestoreFault makes that half's restore throw; ModeAtWrite is a file
    /// that receives "mode=&lt;mode&gt;" per staged write; OwnerFail (backup or temp) fails that owner-only check; DeleteFault "1"
    /// leaves a tracked file in place; ClearProbe is a file that receives "held=&lt;n&gt; cleared=yes|no"; StrandedMaxAgeMinutes
    /// overrides the 15-minute fresh age. None of them ever receives a secret.
    /// </summary>
    public sealed class DesktopWinPhase1Faults
    {
        /// <summary>DW3_FAIL_STEP.</summary>
        public string FailStep { get; init; } = "";

        /// <summary>DW3_REPLACE_FAULT.</summary>
        public string ReplaceFault { get; init; } = "";

        /// <summary>DW3_RESTORE_FAULT.</summary>
        public string RestoreFault { get; init; } = "";

        /// <summary>DW3_MODE_AT_WRITE.</summary>
        public string ModeAtWrite { get; init; } = "";

        /// <summary>DW3_OWNER_FAIL.</summary>
        public string OwnerFail { get; init; } = "";

        /// <summary>DW3_DELETE_FAULT.</summary>
        public string DeleteFault { get; init; } = "";

        /// <summary>DW3_CLEAR_PROBE.</summary>
        public string ClearProbe { get; init; } = "";

        /// <summary>DW3_STRANDED_MAX_AGE_MINUTES.</summary>
        public string StrandedMaxAgeMinutes { get; init; } = "";
    }

    /// <summary>The Windows ACL reads and writes (Get-Acl and Set-Acl). A test binds a fake.</summary>
    public interface IDesktopWinAcl
    {
        /// <summary>Get-Acl's Owner: DOMAIN\name, or the SID when it does not translate.</summary>
        string Owner(string path);

        /// <summary>The Set-Acl half of the owner-only file: protected, every rule removed, one FullControl allow rule for identity.</summary>
        void ProtectOwnerOnly(string path, string identity);

        /// <summary>Protected, exactly one rule, not inherited, for identity (ignoring case), with FullControl.</summary>
        bool IsOwnerOnly(string path, string identity);

        /// <summary>Get-Acl: the file's security, to restore later.</summary>
        object Save(string path);

        /// <summary>Set-Acl with a saved security.</summary>
        void Restore(string path, object saved);
    }

    /// <summary>What desktop-win-phase1-secrets reaches outside its files. The CLI binds the production ones.</summary>
    public sealed class DesktopWinSecretSeams
    {
        /// <summary>The Windows branch ($env:OS -eq 'Windows_NT'): ACLs and icacls; otherwise chmod and stat.</summary>
        public bool Windows { get; init; } = OperatingSystem.IsWindows();

        /// <summary>Split-Path's and Join-Path's platform (the host's, as PowerShell's path cmdlets followed it).</summary>
        public bool WindowsPaths { get; init; } = OperatingSystem.IsWindows();

        /// <summary>icacls, chmod and stat.</summary>
        public IOperatorToolSeams Tools { get; init; }

        /// <summary>The Windows ACLs.</summary>
        public IDesktopWinAcl Acl { get; init; }

        /// <summary>The test seams; production leaves them empty.</summary>
        public DesktopWinPhase1Faults Faults { get; init; } = new DesktopWinPhase1Faults();
    }

    /// <summary>The Windows ACLs on this machine (System.Security.AccessControl).</summary>
    public sealed class DesktopWinAclOs : IDesktopWinAcl
    {
        /// <inheritdoc />
        public string Owner(string path)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("ACLs are read on Windows only.");
            return OwnerName(Directory.Exists(path) ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl());
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static string OwnerName(FileSystemSecurity security)
        {
            try
            {
                return security.GetOwner(typeof(NTAccount))?.Value;
            }
            catch (IdentityNotMappedException)
            {
                return security.GetOwner(typeof(SecurityIdentifier))?.Value;
            }
        }

        /// <inheritdoc />
        public void ProtectOwnerOnly(string path, string identity)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("ACLs are written on Windows only.");
            var file = new FileInfo(path);
            var security = file.GetAccessControl();
            security.SetAccessRuleProtection(true, false);
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(NTAccount))) security.RemoveAccessRule(rule);
            security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, AccessControlType.Allow));
            file.SetAccessControl(security);
        }

        /// <inheritdoc />
        public bool IsOwnerOnly(string path, string identity)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("ACLs are read on Windows only.");
            var security = new FileInfo(path).GetAccessControl();
            if (!security.AreAccessRulesProtected) return false;
            var rules = security.GetAccessRules(true, true, typeof(NTAccount)).Cast<FileSystemAccessRule>().ToList();
            if (rules.Count != 1) return false;
            var rule = rules[0];
            if (rule.IsInherited) return false;
            if (!string.Equals(rule.IdentityReference.Value, identity, StringComparison.OrdinalIgnoreCase)) return false;
            return (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl;
        }

        /// <inheritdoc />
        public object Save(string path)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("ACLs are read on Windows only.");
            return new FileInfo(path).GetAccessControl();
        }

        /// <inheritdoc />
        public void Restore(string path, object saved)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("ACLs are written on Windows only.");
            if (saved is not FileSecurity security) throw new ArgumentException("not a saved file security", nameof(saved));
            // The saved access rules and their protection (the DACL); the owner stays (setting one needs a privilege).
            var restored = new FileSecurity();
            restored.SetSecurityDescriptorBinaryForm(security.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            new FileInfo(path).SetAccessControl(restored);
        }
    }

    /// <summary>The desktop-win phase 1 secrets (desktop-win-phase1-secrets.ps1's body; see the file comment).</summary>
    public static class DesktopWinPhase1Secrets
    {
        /// <summary>Done, or the dry run.</summary>
        public const int Done = 0;

        /// <summary>An unexpected error before the apply.</summary>
        public const int Failed = 1;

        /// <summary>Refused (REFUSED: &lt;code&gt;).</summary>
        public const int Refused = 2;

        /// <summary>The map entry that holds the secret.</summary>
        public const string SecretKey = "PhoneHomeRunner:Runners:desktop-win:SharedSecret";

        /// <summary>The secret file's default.</summary>
        public const string DefaultSecretFile = "C:\\Antiphon\\runner\\secrets\\phone-home";

        /// <summary>Dw3StrandedMaxAgeMinutes: a stranded file newer than this is kept as fresh.</summary>
        public const int StrandedMaxAgeMinutes = 15;

        /// <summary>
        /// Get-DesktopWinEntries: the map entries in order, the secret as given (empty for the dry run's list), the provider homes
        /// under the profile home (WindowsPhoneHome.ChildPath, the twin of Join-PhoneHomeChild).
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, string>> Entries(string home, string secret)
        {
            const string prefix = "PhoneHomeRunner:Runners:desktop-win:";
            return new[]
            {
                Pair(prefix + "Enabled", "true"),
                Pair(prefix + "AllowDelegatedTasks", "true"),
                Pair(prefix + "DisplayName", "desktop-win"),
                Pair(prefix + "HostWorkspaceRoot", "C:\\src\\Antiphon"),
                Pair(prefix + "RunnerWorkspace", "C:/Antiphon/runner/work"),
                Pair(prefix + "RunnerRepository", "C:/Antiphon/runner/work/repos/antiphon"),
                Pair(prefix + "MaxCapacity", "1"),
                Pair(prefix + "RawExeAllowList:0", "C:/Program Files/PowerShell/7/pwsh.exe"),
                Pair(prefix + "CallbackOrigin", "https://antiphon.desktop.codeperf.net"),
                Pair(prefix + "SharedSecret", secret ?? ""),
                Pair(prefix + "ChildClaudeHome", WindowsPhoneHome.ChildPath(home, ".claude")),
                Pair(prefix + "ChildCodexHome", WindowsPhoneHome.ChildPath(home, ".codex")),
                Pair(prefix + "ChildGrokHome", WindowsPhoneHome.ChildPath(home, ".grok")),
                Pair(prefix + "ClaudeAuthProbeEnabled", "true"),
                Pair(prefix + "CodexAuthProbeEnabled", "true"),
            };
        }

        /// <summary>The helper; the exit code (see the file comment).</summary>
        public static int Run(DesktopWinPhase1Options options, NightlyHost host, IFileSystem files, DesktopWinSecretSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            var session = new Session(options, host, files, seams);
            try
            {
                return session.Main();
            }
            catch (Exception e)
            {
                // pwsh -File's terminating error: its message on stderr, exit 1. Never a secret (none exists before the apply,
                // and the apply catches everything).
                host.Error.WriteLine(session.Redact(e.Message));
                return Failed;
            }
        }

        private static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);

        private sealed class Session
        {
            private static readonly Regex StrandedPattern = new Regex("^[0-9a-f]{32}\\.(dw3part|dw3bak)$", RegexOptions.CultureInvariant);
            private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

            private readonly DesktopWinPhase1Options _options;
            private readonly NightlyHost _host;
            private readonly IFileSystem _files;
            private readonly DesktopWinSecretSeams _seams;
            private readonly DesktopWinPhase1Faults _faults;
            private readonly bool _windows;
            private readonly bool _windowsPaths;
            private readonly List<byte[]> _held = new List<byte[]>();
            private readonly List<string> _temps = new List<string>();
            private readonly Dictionary<string, List<string>> _backup = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["secret-file"] = new List<string>(),
                ["user-secrets"] = new List<string>(),
            };
            private readonly Dictionary<string, string> _staged = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["secret-file"] = "",
                ["user-secrets"] = "",
            };
            private string _step = "";
            private string _secret;
            private string _secretTarget = "";
            private string _userSecretsTarget = "";

            public Session(DesktopWinPhase1Options options, NightlyHost host, IFileSystem files, DesktopWinSecretSeams seams)
            {
                _options = options;
                _host = host;
                _files = files;
                _seams = seams;
                _faults = seams.Faults ?? new DesktopWinPhase1Faults();
                _windows = seams.Windows;
                _windowsPaths = seams.WindowsPaths;
            }

            public string Redact(string text) =>
                string.IsNullOrEmpty(_secret) ? text ?? "" : (text ?? "").Replace(_secret, "[redacted]", StringComparison.Ordinal);

            public int Main()
            {
                if (!Blank(Variable("ANTIPHON_ORCHESTRATOR")) || !Blank(Variable("CLAUDECODE")))
                    return Refuse("agent-shell", "ANTIPHON_ORCHESTRATOR or CLAUDECODE is set. This helper is operator-only.");

                var userSecretsPath = _options.UserSecretsPath ?? "";
                var secretFile = _options.SecretFile ?? "";
                var profileHome = _options.ProfileHome ?? "";
                var phase1ConfigPath = _options.Phase1ConfigPath ?? "";
                if (!_windows && (Blank(userSecretsPath) || Blank(secretFile)))
                    return Refuse("not-windows", "Pass --user-secrets-path and --secret-file. The operator runs this helper on Windows.");
                if (Blank(secretFile)) secretFile = DefaultSecretFile;
                if (Blank(userSecretsPath))
                {
                    var appData = Variable("APPDATA");
                    if (Blank(appData) && !Blank(Variable("USERPROFILE"))) appData = Join(Variable("USERPROFILE"), "AppData\\Roaming");
                    if (Blank(appData)) return Refuse("user-secrets-path-missing", "APPDATA is not set. Pass --user-secrets-path.");
                    userSecretsPath = Join(appData, "Microsoft\\UserSecrets\\antiphon-server\\secrets.json");
                }
                if (Blank(profileHome))
                {
                    if (!Blank(_options.Home)) profileHome = _options.Home;
                    else if (!Blank(Variable("USERPROFILE"))) profileHome = Variable("USERPROFILE");
                }
                if (Blank(phase1ConfigPath))
                {
                    var secretsParent = Parent(secretFile);
                    string runnerRoot = null;
                    if (!Blank(secretsParent)) runnerRoot = Parent(secretsParent);
                    if (Blank(runnerRoot)) return Refuse("phase1-path", "Pass --phase1-config-path. The secret path has no runner root.");
                    phase1ConfigPath = Join(runnerRoot, "phone-home.phase1.json");
                }

                var secretsDir = Parent(secretFile);
                if (Blank(secretsDir)) return Refuse("secret-path", "The secret file path has no directory.");
                var userName = Variable("USERNAME");
                if (Blank(userName)) return Refuse("username-missing", "USERNAME is not set.");
                var anchor = OwnerAnchor(secretsDir);
                if (anchor == null) return Refuse("secrets-dir-owner", "The secrets directory does not exist and has no existing parent.");
                var ownerName = PathOwner(anchor);
                if (Blank(ownerName) || !ownerName.Equals(userName, StringComparison.OrdinalIgnoreCase))
                    return Refuse("secrets-dir-owner", "USERNAME does not match the secrets directory owner. A directory created from an elevated shell can be owned by "
                        + "BUILTIN\\Administrators. Run this helper from a shell whose USERNAME owns the secrets directory.");

                var apply = _options.Execute && !_options.WhatIf;
                if (apply && Blank(profileHome)) return Refuse("profile-home-missing", "Profile home is empty.");
                var userSecretsDir = Parent(userSecretsPath);
                var sweepRequested = _options.SweepStranded;
                StrandedReport(userSecretsDir, secretsDir, sweepRequested, sweepRequested && apply, _options.SweepBackups);

                var example = Join(_options.ScriptsDirectory ?? "", "phone-home.phase1.example.json");
                Dw3Node map;
                try
                {
                    map = ReadUserSecretNode(userSecretsPath);
                    Dw3Json.Write(map, 0);
                }
                catch (Exception)
                {
                    return Refuse("user-secrets-malformed", "The user-secrets file could not be read as a JSON object.");
                }

                var existingSecret = Dw3Json.StringValue(map, SecretKey);
                var hasSecret = !Blank(existingSecret);
                if (apply && hasSecret && !_options.Rotate)
                    return Refuse("shared-secret-present", SecretKey + " is already set. Re-run with --rotate to replace it. Nothing was changed.");
                if (apply && Exists(secretFile) && !_options.Rotate)
                    return Refuse("secret-file-present", "The secret file already exists. Re-run with --rotate to replace it. Nothing was changed.");

                var entries = Entries(profileHome, "");
                var ambiguity = Ambiguity(map, entries);
                if (apply && ambiguity.Length > 0)
                    return Refuse("shared-secret-ambiguous", "A case-variant key or a nested PhoneHomeRunner object is already present. Merge or remove the case-variant "
                        + "or nested PhoneHomeRunner entry, then re-run. Nothing was changed.");
                if (!apply)
                {
                    Line("mode: dry-run");
                    Line("secret-file: " + secretFile + " " + FormatExists(secretFile));
                    Line("user-secrets: " + userSecretsPath + " " + FormatExists(userSecretsPath));
                    Line("secrets-dir: " + secretsDir + " " + FormatExists(secretsDir));
                    Line("phase1-config: " + phase1ConfigPath + " " + FormatExists(phase1ConfigPath));
                    Line("phase1-example: " + example + " " + FormatExists(example));
                    if (ambiguity.Length > 0) Line("shared-secret-key: ambiguous");
                    else if (hasSecret) Line("shared-secret-key: present");
                    else Line("shared-secret-key: absent");
                    Line("secret-file-matches-map: " + (SecretMatchesMap(secretFile, existingSecret, hasSecret) ? "yes" : "no"));
                    Line("standing-agent: unset");
                    Line("keys:");
                    foreach (var entry in entries) Line("  " + entry.Key);
                    if (_options.Rotate) Line("rotate: requested");
                    Line("changes: none");
                    return Done;
                }

                if (!Exists(example)) return Refuse("phase1-example-missing", "scripts/phone-home.phase1.example.json is not beside this script.");
                return Apply(map, secretFile, userSecretsPath, secretsDir, profileHome, phase1ConfigPath, example);
            }

            private int Apply(Dw3Node map, string secretFile, string userSecretsPath, string secretsDir, string profileHome, string phase1ConfigPath, string example)
            {
                byte[] oldJsonBytes = null;
                var oldJsonExists = false;
                object oldJsonSaved = null;
                byte[] oldSecretBytes = null;
                var oldSecretExists = false;
                if (Exists(userSecretsPath))
                {
                    oldJsonExists = true;
                    oldJsonBytes = _files.File.ReadAllBytes(userSecretsPath);
                    Hold(oldJsonBytes);
                    oldJsonSaved = _windows ? _seams.Acl.Save(userSecretsPath) : UnixMode(userSecretsPath);
                }
                if (Exists(secretFile))
                {
                    oldSecretExists = true;
                    oldSecretBytes = _files.File.ReadAllBytes(secretFile);
                    Hold(oldSecretBytes);
                }
                _step = "prepare";
                _secretTarget = secretFile;
                _userSecretsTarget = userSecretsPath;
                var phaseWrote = false;

                try
                {
                    _step = "generate";
                    _secret = NewSecret();
                    var real = Entries(profileHome, _secret);
                    foreach (var entry in real) Dw3Json.SetStringValue(map, entry.Key, entry.Value);
                    _step = "serialize";
                    var jsonText = Dw3Json.Write(map, 0) + "\n";
                    var secretText = _secret + "\n";

                    _step = "phase1-copy";
                    if (!Exists(phase1ConfigPath))
                    {
                        var phaseParent = Parent(phase1ConfigPath);
                        if (!Blank(phaseParent) && !Exists(phaseParent)) _files.Directory.CreateDirectory(phaseParent);
                        _files.File.Copy(example, phase1ConfigPath, false);
                        phaseWrote = true;
                    }

                    var storeDir = Parent(userSecretsPath);
                    if (Blank(storeDir)) throw new InvalidOperationException("user-secrets directory is missing");
                    _step = "secret-acl";
                    var secretTemp = StagedText(secretsDir, secretText, _secret, "secret-acl", "secret-write", "");
                    _staged["secret-file"] = secretTemp;
                    _step = "user-secrets-acl";
                    var jsonTemp = StagedText(storeDir, jsonText, _secret, "user-secrets-acl", "user-secrets-write", "user-secrets-write");
                    _staged["user-secrets"] = jsonTemp;

                    _step = "user-secrets-commit";
                    Publish(jsonTemp, userSecretsPath, _secret, "user-secrets", false, oldJsonSaved);
                    _step = "secret-file-commit";
                    if (string.Equals(_faults.FailStep, "secret-file-commit", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("simulated secret-file commit failure");
                    Publish(secretTemp, secretFile, _secret, "secret-file", true, null);

                    Line("mode: execute");
                    Line("secret-file: wrote");
                    Line("user-secrets: updated");
                    Line(phaseWrote ? "phase1-config: wrote" : "phase1-config: present");
                    Line("keys-set: " + real.Count.ToString(CultureInfo.InvariantCulture));
                    Line("standing-agent: unset");
                    Line("NEXT 1: restart AppHost from C:\\src\\Antiphon");
                    Line("pwsh -NoProfile -File C:\\src\\Antiphon\\scripts\\restart-apphost.ps1");
                    Line("NEXT 2: start the phase 1 runner from the side clone in a normal terminal");
                    Line("pwsh -NoProfile -File C:\\Antiphon\\runner\\app\\scripts\\autostart-session-runner.ps1 -PhoneHomeConfigPath " + phase1ConfigPath);
                    Line("NEXT 3: after the restart, verify");
                    Line("GET http://localhost:17202/api/session-runners/desktop-win/status");
                    return Done;
                }
                catch (Exception)
                {
                    bool jsonOk;
                    bool secretOk;
                    try
                    {
                        jsonOk = RepairHalf("user-secrets", userSecretsPath, oldJsonBytes, oldJsonExists, _secret, oldJsonSaved, false);
                    }
                    catch (Exception)
                    {
                        jsonOk = false;
                        _step = "restore";
                    }
                    try
                    {
                        secretOk = RepairHalf("secret-file", secretFile, oldSecretBytes, oldSecretExists, _secret, null, true);
                    }
                    catch (Exception)
                    {
                        secretOk = false;
                        _step = "restore";
                    }
                    Line("step=" + _step);
                    if (!jsonOk)
                    {
                        Line("differs: user-secrets");
                        WriteKept("user-secrets");
                    }
                    if (!secretOk)
                    {
                        Line("differs: secret-file");
                        WriteKept("secret-file");
                    }
                    if (jsonOk && secretOk) Line(phaseWrote ? "changed: phase1-config" : "changed: none");
                    else if (phaseWrote) Line("changed: phase1-config");
                    return Refuse("failed", "The helper stopped before finishing. No secret was written to the console.");
                }
                finally
                {
                    FinishBytes();
                }
            }

            // ---- output, environment and paths ----

            private void Line(string text) => _host.Out.WriteLine(text);

            private int Refuse(string code, string message)
            {
                Line("REFUSED: " + code);
                Line(message);
                return Refused;
            }

            private string Variable(string name) => _host.Variables(name);

            private static bool Blank(string text) => string.IsNullOrWhiteSpace(text);

            private string Parent(string path) => PsPath.Parent(path, _windowsPaths);

            private string Join(string left, string right) => PsPath.Join(left, right, _windowsPaths);

            // Test-Path -LiteralPath.
            private bool Exists(string path) => !string.IsNullOrEmpty(path) && (_files.File.Exists(path) || _files.Directory.Exists(path));

            private string FormatExists(string path) => Exists(path) ? "present" : "absent";

            // .EndsWith with a string argument compares in the current culture, as PowerShell called it.
            private static bool EndsWith(string text, string suffix) => (text ?? "").EndsWith(suffix, StringComparison.CurrentCulture);

            private string FileName(string path) => _files.Path.GetFileName(path) ?? "";

            // ---- secrets and the map ----

            private static string NewSecret()
            {
                var bytes = RandomNumberGenerator.GetBytes(32);
                try
                {
                    var builder = new StringBuilder(64);
                    foreach (var b in bytes) builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                    return builder.ToString();
                }
                finally
                {
                    Array.Clear(bytes, 0, bytes.Length);
                }
            }

            // Test-Dw3SecretMatchesMap.
            private bool SecretMatchesMap(string path, string mapValue, bool hasMap)
            {
                var present = Exists(path);
                if (!present && !hasMap) return true;
                if (!present || !hasMap) return false;
                var text = _files.File.ReadAllText(path, Utf8NoBom);
                if (mapValue == null) return false;
                return string.Equals(text.TrimEnd('\n', '\r'), mapValue, StringComparison.Ordinal);
            }

            // Read-UserSecretNode: an absent or blank file is an empty object; anything else must parse as an object.
            private Dw3Node ReadUserSecretNode(string path)
            {
                if (!Exists(path)) return Dw3Node.EmptyObject();
                var raw = _files.File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(raw)) return Dw3Node.EmptyObject();
                var parsed = Dw3Json.Read(raw);
                if (parsed.Kind != Dw3Kind.Object) throw new FormatException("user-secrets JSON must be an object");
                return parsed;
            }

            // Get-Dw3SecretAmbiguity: "nested" for a PhoneHomeRunner object (any case), "case-variant" for a key that differs from
            // a planned one only in case, else "".
            private static string Ambiguity(Dw3Node node, IReadOnlyList<KeyValuePair<string, string>> planned)
            {
                if (node == null || node.Kind != Dw3Kind.Object) return "";
                foreach (var key in node.Keys)
                {
                    if (!key.Equals("PhoneHomeRunner", StringComparison.OrdinalIgnoreCase)) continue;
                    var child = node.Members[key];
                    if (child != null && child.Kind == Dw3Kind.Object) return "nested";
                }
                if (planned == null) return "";
                foreach (var key in node.Keys)
                    foreach (var entry in planned)
                        if (key.Equals(entry.Key, StringComparison.OrdinalIgnoreCase) && !key.Equals(entry.Key, StringComparison.Ordinal)) return "case-variant";
                return "";
            }

            // ---- owner and permissions ----

            // Find-OwnerAnchor: the directory or its first existing ancestor.
            private string OwnerAnchor(string directory)
            {
                var current = directory;
                while (!Blank(current))
                {
                    if (Exists(current)) return current;
                    var parent = Parent(current);
                    if (Blank(parent) || parent == current) return null;
                    current = parent;
                }
                return null;
            }

            // Get-PathOwner: the owner's name without its domain (Windows), or stat's %U.
            private string PathOwner(string path)
            {
                if (_windows)
                {
                    var owner = _seams.Acl.Owner(path) ?? "";
                    var slash = owner.LastIndexOf('\\');
                    if (slash >= 0 && slash < owner.Length - 1) return owner.Substring(slash + 1);
                    return owner;
                }
                return Stat("%U", path);
            }

            // Get-UnixMode: stat's %a; null when stat fails.
            private string UnixMode(string path) => Stat("%a", path);

            private string Stat(string format, string path)
            {
                var result = _seams.Tools.Run(new OperatorToolCall("stat", new[] { "-c", format, "--", path }));
                PassError(result);
                if (result.ExitCode != 0) return null;
                return string.Join(" ", result.OutputLines ?? Array.Empty<string>()).Trim();
            }

            // Get-Dw3WindowsUser: USERDOMAIN\USERNAME, or USERNAME.
            private string WindowsUser()
            {
                var who = Variable("USERNAME");
                if (!Blank(Variable("USERDOMAIN"))) who = Variable("USERDOMAIN") + "\\" + Variable("USERNAME");
                return who;
            }

            // Invoke-Dw3Native: refuses a command line holding the secret before anything starts; a non-zero exit throws. The tool's
            // stderr is passed on; its stdout is not printed.
            private void Native(string file, IReadOnlyList<string> arguments, string secret)
            {
                if (!string.IsNullOrEmpty(secret))
                {
                    if (file.Contains(secret, StringComparison.Ordinal)) throw new InvalidOperationException("refusing to pass the secret on a command line");
                    foreach (var argument in arguments)
                        if (argument != null && argument.Contains(secret, StringComparison.Ordinal))
                            throw new InvalidOperationException("refusing to pass the secret on a command line");
                }
                var result = _seams.Tools.Run(new OperatorToolCall(file, arguments));
                PassError(result);
                if (result.ExitCode != 0) throw new InvalidOperationException(file + " failed (" + result.ExitCode.ToString(CultureInfo.InvariantCulture) + ")");
            }

            private void PassError(OperatorToolResult result)
            {
                foreach (var line in result.ErrorLines ?? Array.Empty<string>()) _host.Error.WriteLine(Redact(line));
            }

            // Set-OwnerOnlyFile: icacls (inheritance removed, the user granted full control) and the owner-only ACL, or chmod 600.
            private void SetOwnerOnly(string path, string secret)
            {
                if (_windows)
                {
                    Native("icacls", new[] { path, "/inheritance:r" }, secret);
                    var who = WindowsUser();
                    Native("icacls", new[] { path, "/grant:r", who + ":(F)" }, secret);
                    _seams.Acl.ProtectOwnerOnly(path, who);
                    return;
                }
                Native("chmod", new[] { "600", path }, secret);
            }

            // Confirm-Dw3OwnerOnly.
            private void ConfirmOwnerOnly(string path)
            {
                var forced = false;
                var fail = _faults.OwnerFail;
                if (!Blank(fail))
                {
                    var name = FileName(path);
                    if (string.Equals(fail, "backup", StringComparison.Ordinal) && EndsWith(name, ".dw3bak")) forced = true;
                    if (string.Equals(fail, "temp", StringComparison.Ordinal) && EndsWith(name, ".dw3part")) forced = true;
                }
                if (!forced && IsOwnerOnly(path)) return;
                Line("owner-only-failed: " + path);
                throw new InvalidOperationException("owner-only permission was not applied");
            }

            // Test-Dw3OwnerOnly.
            private bool IsOwnerOnly(string path)
            {
                if (_windows) return _seams.Acl.IsOwnerOnly(path, WindowsUser());
                var mode = UnixMode(path);
                return mode != null && mode == "600";
            }

            // Restore-SavedAcl.
            private void RestoreSavedAcl(string path, object saved, string secret)
            {
                if (saved == null) return;
                if (_windows)
                {
                    _seams.Acl.Restore(path, saved);
                    return;
                }
                Native("chmod", new[] { Convert.ToString(saved, CultureInfo.InvariantCulture), path }, secret);
            }

            // ---- tracked files ----

            // Remove-Dw3Temp: only a .dw3part; failures are silent.
            private void RemoveTemp(string path)
            {
                if (Blank(path) || !EndsWith(path, ".dw3part")) return;
                if (Exists(path)) DeleteQuietly(path);
            }

            private void DeleteQuietly(string path)
            {
                try
                {
                    if (_files.Directory.Exists(path)) _files.Directory.Delete(path, false);
                    else _files.File.Delete(path);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { }
            }

            // Remove-Dw3Tracked: only a .dw3part or .dw3bak; a file left behind prints kept:.
            private void RemoveTracked(string path)
            {
                if (Blank(path)) return;
                var name = FileName(path);
                if (Blank(name)) return;
                if (!EndsWith(name, ".dw3part") && !EndsWith(name, ".dw3bak")) return;
                if (Exists(path) && _faults.DeleteFault != "1") DeleteQuietly(path);
                if (Exists(path)) Line("kept: " + path);
            }

            private void Hold(byte[] bytes)
            {
                if (bytes != null) _held.Add(bytes);
            }

            // Finish-Dw3Bytes: every saved byte array cleared; the clear probe gets only the count and whether all are zero.
            private void FinishBytes()
            {
                var count = _held.Count;
                foreach (var item in _held)
                    if (item != null && item.Length > 0) Array.Clear(item, 0, item.Length);
                var probe = _faults.ClearProbe;
                if (Blank(probe)) return;
                var ok = _held.All(item => item == null || item.All(b => b == 0));
                _files.File.WriteAllText(probe, "held=" + count.ToString(CultureInfo.InvariantCulture) + " cleared=" + (ok ? "yes" : "no"), Utf8NoBom);
            }

            // ---- stranded files ----

            private int MaxAgeMinutes()
            {
                var text = _faults.StrandedMaxAgeMinutes;
                if (Blank(text)) return StrandedMaxAgeMinutes;
                if (!int.TryParse(text, out var parsed)) return StrandedMaxAgeMinutes;
                return parsed < 0 ? StrandedMaxAgeMinutes : parsed;
            }

            private bool Fresh(string path)
            {
                if (Blank(path) || !Exists(path)) return false;
                DateTime written;
                try { written = _files.File.GetLastWriteTimeUtc(path); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return false; }
                return (_host.UtcNow - written).TotalMinutes < MaxAgeMinutes();
            }

            private static bool StrandedName(string name) => !Blank(name) && StrandedPattern.IsMatch(name);

            // Get-Dw3StrandedPaths: the directory's files (hidden ones too) whose names are exactly <32 hex>.dw3part or .dw3bak.
            private IEnumerable<string> StrandedPaths(string directory)
            {
                if (Blank(directory) || !Exists(directory)) return Array.Empty<string>();
                try
                {
                    return _files.DirectoryInfo.New(directory).EnumerateFiles().Where(file => StrandedName(file.Name)).Select(file => file.FullName).ToList();
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    return Array.Empty<string>();
                }
            }

            // Write-Dw3StrandedReport.
            private void StrandedReport(string userDir, string secretDir, bool sweepRequested, bool applySweep, bool sweepBackups)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var paths = new List<string>();
                foreach (var directory in new[] { userDir, secretDir })
                    foreach (var path in StrandedPaths(directory))
                        if (!Blank(path) && seen.Add(path)) paths.Add(path);
                if (paths.Count < 1) return;
                foreach (var path in paths) Line("stranded: " + path);
                Line("recovery: rename the .dw3bak back over a missing target, or re-run --execute --rotate");
                if (!sweepRequested)
                {
                    Line("sweep: refused");
                    return;
                }
                foreach (var path in paths)
                {
                    var name = FileName(path);
                    if (!StrandedName(name)) continue;
                    if (Fresh(path))
                    {
                        Line("kept: " + path + " fresh");
                        continue;
                    }
                    if (EndsWith(name, ".dw3bak") && !sweepBackups)
                    {
                        Line("kept: " + path + " backup-of-secrets-keep-until-confirmed");
                        continue;
                    }
                    if (!applySweep)
                    {
                        Line("would-sweep: " + path);
                        continue;
                    }
                    if (Exists(path)) DeleteQuietly(path);
                    Line(Exists(path) ? "kept: " + path : "swept: " + path);
                }
            }

            // ---- the halves ----

            private string HalfDir(string half)
            {
                var target = half == "user-secrets" ? _userSecretsTarget : half == "secret-file" ? _secretTarget : "";
                return Blank(target) ? "" : Parent(target);
            }

            private void RemoveHalfBackups(string half)
            {
                foreach (var path in _backup[half].ToList()) RemoveTracked(path);
            }

            // Remove-Dw3HalfResidue: the staged temp, the backups, and every temp this run made in the half's directory.
            private void RemoveHalfResidue(string half)
            {
                RemoveTracked(_staged[half]);
                RemoveHalfBackups(half);
                var directory = HalfDir(half);
                if (Blank(directory)) return;
                foreach (var temp in _temps.ToList())
                {
                    if (Blank(temp)) continue;
                    if (string.Equals(Parent(temp), directory, StringComparison.Ordinal)) RemoveTracked(temp);
                }
            }

            // Write-Dw3Kept: the half's staged temp, backups and temps that are still on disk.
            private void WriteKept(string half)
            {
                var staged = _staged[half] ?? "";
                if (!Blank(staged) && Exists(staged)) Line("kept: " + staged);
                foreach (var backup in _backup[half])
                {
                    if (Blank(backup)) continue;
                    if (string.Equals(backup, staged, StringComparison.Ordinal)) continue;
                    if (Exists(backup)) Line("kept: " + backup);
                }
                var directory = HalfDir(half);
                if (Blank(directory)) return;
                foreach (var temp in _temps)
                {
                    if (Blank(temp)) continue;
                    if (string.Equals(temp, staged, StringComparison.Ordinal)) continue;
                    if (!Exists(temp)) continue;
                    if (!string.Equals(Parent(temp), directory, StringComparison.Ordinal)) continue;
                    Line("kept: " + temp);
                }
            }

            private static bool BytesEqual(byte[] left, byte[] right)
            {
                if (left == null && right == null) return true;
                if (left == null || right == null) return false;
                return left.AsSpan().SequenceEqual(right);
            }

            // Test-Dw3SavedTarget: the target is as it was before any change (absent, or the saved bytes).
            private bool SavedTarget(string path, bool existed, byte[] oldBytes)
            {
                var present = Exists(path);
                if (!existed) return !present;
                if (!present) return false;
                var now = _files.File.ReadAllBytes(path);
                Hold(now);
                return BytesEqual(now, oldBytes ?? Array.Empty<byte>());
            }

            // Try-New-Dw3UnixModeEmpty: an empty file created with mode 0600 (off Windows).
            private bool TryNewUnixModeEmpty(string path)
            {
                if (OperatingSystem.IsWindows()) return false;
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                };
                _files.FileStream.New(path, options).Dispose();
                return true;
            }

            // New-Dw3OwnerOnlyEmpty: an empty owner-only file, removed again when making it owner-only fails.
            private void NewOwnerOnlyEmpty(string path, string secret, string step)
            {
                if (!Blank(step)) _step = step;
                var parent = Parent(path);
                if (!Blank(parent) && !Exists(parent)) _files.Directory.CreateDirectory(parent);
                var created = false;
                try
                {
                    var made = false;
                    if (!_windows)
                    {
                        try { made = TryNewUnixModeEmpty(path); }
                        catch (Exception)
                        {
                            made = false;
                            RemoveTemp(path);
                        }
                    }
                    if (!made)
                    {
                        RemoveTemp(path);
                        _files.FileStream.New(path, FileMode.CreateNew, FileAccess.Write, FileShare.None).Dispose();
                    }
                    created = true;
                    SetOwnerOnly(path, secret);
                    ConfirmOwnerOnly(path);
                }
                catch (Exception)
                {
                    if (created || Exists(path)) RemoveTemp(path);
                    throw;
                }
            }

            // Write-Dw3ModeProbe: the test probe gets the staged file's mode (owner-only or inherited on Windows), never its bytes.
            private void ModeProbe(string path)
            {
                var probe = _faults.ModeAtWrite;
                if (Blank(probe)) return;
                if (string.Equals(probe, path, StringComparison.OrdinalIgnoreCase)) return;
                var mode = "unknown";
                if (_windows) mode = IsOwnerOnly(path) ? "owner-only" : "inherited";
                else
                {
                    var got = UnixMode(path);
                    if (!Blank(got)) mode = got;
                }
                _files.File.AppendAllText(probe, "mode=" + mode + "\n", Utf8NoBom);
            }

            // Write-Dw3BytesFlush: into the existing file, flushed to disk.
            private void WriteBytesFlush(string path, byte[] bytes)
            {
                using var stream = _files.FileStream.New(path, FileMode.Open, FileAccess.Write, FileShare.None);
                if (bytes != null && bytes.Length > 0) stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            // New-Dw3StagedText: an owner-only <32 hex>.dw3part in the directory holding the text (UTF-8, no BOM).
            private string StagedText(string directory, string text, string secret, string aclStep, string writeStep, string failStep)
            {
                if (Blank(directory)) throw new InvalidOperationException("stage directory is missing");
                if (!Exists(directory)) _files.Directory.CreateDirectory(directory);
                var temp = Join(directory, Guid.NewGuid().ToString("n") + ".dw3part");
                _temps.Add(temp);
                try
                {
                    NewOwnerOnlyEmpty(temp, secret, aclStep);
                    _step = writeStep;
                    if (!Blank(failStep) && string.Equals(_faults.FailStep, failStep, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("simulated write failure");
                    ModeProbe(temp);
                    WriteBytesFlush(temp, Utf8NoBom.GetBytes(text ?? ""));
                    return temp;
                }
                catch (Exception)
                {
                    RemoveTemp(temp);
                    throw;
                }
            }

            // Move-Dw3TempOnto: on Windows File.Replace through an owner-only .dw3bak (checked after the swap), else a rename over.
            private void MoveTempOnto(string temp, string target, string half)
            {
                var fault = _faults.ReplaceFault;
                var fire = !string.Equals(_step, "restore", StringComparison.Ordinal) && !Blank(fault) && string.Equals(fault, half, StringComparison.Ordinal);
                if (fire && Exists(target))
                {
                    var faultBackup = Join(Parent(target), Guid.NewGuid().ToString("n") + ".dw3bak");
                    _backup[half].Add(faultBackup);
                    var old = _files.File.ReadAllBytes(target);
                    Hold(old);
                    NewOwnerOnlyEmpty(faultBackup, "", "");
                    WriteBytesFlush(faultBackup, old);
                    _files.File.Delete(target);
                    throw new IOException("simulated partial replace");
                }
                if (fire) throw new IOException("simulated partial replace");
                var existed = Exists(target);
                if (_windows)
                {
                    if (existed)
                    {
                        var backup = Join(Parent(target), Guid.NewGuid().ToString("n") + ".dw3bak");
                        _backup[half].Add(backup);
                        Exception replaceError = null;
                        try { _files.File.Replace(temp, target, backup); }
                        catch (Exception e) { replaceError = e; }
                        if (Exists(backup))
                        {
                            SetOwnerOnly(backup, "");
                            ConfirmOwnerOnly(backup);
                        }
                        if (replaceError != null) throw replaceError;
                    }
                    else _files.File.Move(temp, target);
                    return;
                }
                if (existed) _files.File.Move(temp, target, true);
                else _files.File.Move(temp, target);
            }

            // Publish-Dw3Staged: swap the staged temp onto the target, cross-check the bytes, then the target's permission.
            private void Publish(string temp, string target, string secret, string half, bool ownerOnly, object savedAcl)
            {
                if (!Exists(temp)) throw new InvalidOperationException("staged temp is missing");
                var expected = _files.File.ReadAllBytes(temp);
                Hold(expected);
                if (ownerOnly && _windows && Exists(target))
                {
                    SetOwnerOnly(target, secret);
                    ConfirmOwnerOnly(target);
                }
                _staged[half] = temp;
                MoveTempOnto(temp, target, half);
                var swapped = false;
                if (Exists(target))
                {
                    var now = _files.File.ReadAllBytes(target);
                    Hold(now);
                    swapped = BytesEqual(now, expected);
                }
                if (!swapped) throw new InvalidOperationException("replace cross-check failed");
                if (ownerOnly)
                {
                    SetOwnerOnly(target, secret);
                    ConfirmOwnerOnly(target);
                }
                else if (savedAcl != null) RestoreSavedAcl(target, savedAcl, secret);
                else
                {
                    SetOwnerOnly(target, secret);
                    ConfirmOwnerOnly(target);
                }
                RemoveHalfBackups(half);
            }

            // Restore-Dw3Target: the saved bytes back through an owner-only temp, or the target removed when it did not exist.
            private void RestoreTarget(string target, byte[] oldBytes, bool existed, string secret, object savedAcl, bool ownerOnly, string half)
            {
                var kept = _step;
                if (!existed)
                {
                    if (Exists(target)) DeleteQuietly(target);
                    return;
                }
                var temp = Join(Parent(target), Guid.NewGuid().ToString("n") + ".dw3part");
                _temps.Add(temp);
                try
                {
                    NewOwnerOnlyEmpty(temp, secret, "restore");
                    WriteBytesFlush(temp, oldBytes ?? Array.Empty<byte>());
                    MoveTempOnto(temp, target, half);
                    if (ownerOnly)
                    {
                        SetOwnerOnly(target, secret);
                        ConfirmOwnerOnly(target);
                    }
                    else if (savedAcl != null) RestoreSavedAcl(target, savedAcl, secret);
                    _step = kept;
                }
                catch (Exception)
                {
                    _step = "restore";
                    throw;
                }
            }

            // Repair-Dw3Half: true when the target matches its saved state (restored if it differed); the half's residue is removed.
            private bool RepairHalf(string half, string target, byte[] oldBytes, bool existed, string secret, object savedAcl, bool ownerOnly)
            {
                var matches = SavedTarget(target, existed, oldBytes);
                if (!matches)
                {
                    if (!Blank(_faults.RestoreFault) && string.Equals(_faults.RestoreFault, half, StringComparison.Ordinal))
                        throw new IOException("simulated restore failure");
                    RestoreTarget(target, oldBytes, existed, secret, savedAcl, ownerOnly, half);
                    matches = SavedTarget(target, existed, oldBytes);
                }
                if (!matches) return false;
                RemoveHalfResidue(half);
                return true;
            }
        }
    }

    /// <summary>The kind of one Dw3Json value.</summary>
    public enum Dw3Kind
    {
        /// <summary>An object: ordered keys, each once.</summary>
        Object,

        /// <summary>An array.</summary>
        Array,

        /// <summary>A string.</summary>
        String,

        /// <summary>A number, kept as its text.</summary>
        Number,

        /// <summary>true or false.</summary>
        Bool,

        /// <summary>null.</summary>
        Null,
    }

    /// <summary>One Dw3Json value (the script's @{ T = ...; V = ... } node).</summary>
    public sealed class Dw3Node
    {
        /// <summary>The kind.</summary>
        public Dw3Kind Kind { get; init; }

        /// <summary>A string's value, or a number's text.</summary>
        public string Text { get; init; } = "";

        /// <summary>A bool's value.</summary>
        public bool Bool { get; init; }

        /// <summary>An object's keys, in order.</summary>
        public List<string> Keys { get; } = new List<string>();

        /// <summary>An object's members by key (exact case).</summary>
        public Dictionary<string, Dw3Node> Members { get; } = new Dictionary<string, Dw3Node>(StringComparer.Ordinal);

        /// <summary>An array's items.</summary>
        public List<Dw3Node> Items { get; } = new List<Dw3Node>();

        /// <summary>An empty object.</summary>
        public static Dw3Node EmptyObject() => new Dw3Node { Kind = Dw3Kind.Object };
    }

    /// <summary>
    /// The user-secrets JSON reader and writer of desktop-win-phase1-secrets.ps1 (Read-Dw3Json*, ConvertTo-Dw3Json*): strings,
    /// numbers (their text), nested objects and arrays round-trip, a date-shaped string stays a string, a repeated key or a value
    /// nested deeper than 32 is malformed. The writer indents by two spaces with ": " after a key; a string escapes '"', '\',
    /// \n, \r and \t, and every other character outside 32..126 as \uXXXX.
    /// </summary>
    public static class Dw3Json
    {
        /// <summary>Every refusal's message.</summary>
        public const string Malformed = "user-secrets JSON is malformed";

        /// <summary>Read-Dw3JsonDocument.</summary>
        public static Dw3Node Read(string text)
        {
            var reader = new Reader(text ?? "");
            reader.SkipWhitespace();
            if (reader.AtEnd) throw new FormatException(Malformed);
            var value = reader.Any(0);
            reader.SkipWhitespace();
            if (!reader.AtEnd) throw new FormatException(Malformed);
            return value;
        }

        /// <summary>ConvertTo-Dw3JsonText.</summary>
        public static string Write(Dw3Node node, int depth)
        {
            if (node == null) throw new FormatException(Malformed);
            if (depth > 32) throw new FormatException(Malformed);
            switch (node.Kind)
            {
                case Dw3Kind.Null:
                    return "null";
                case Dw3Kind.Bool:
                    return node.Bool ? "true" : "false";
                case Dw3Kind.Number:
                    return node.Text;
                case Dw3Kind.String:
                    return Quote(node.Text);
                case Dw3Kind.Array:
                {
                    if (node.Items.Count == 0) return "[]";
                    var builder = new StringBuilder("[\n");
                    var pad = new string(' ', 2 * (depth + 1));
                    for (var i = 0; i < node.Items.Count; i++)
                    {
                        builder.Append(pad).Append(Write(node.Items[i], depth + 1));
                        if (i < node.Items.Count - 1) builder.Append(',');
                        builder.Append('\n');
                    }
                    return builder.Append(new string(' ', 2 * depth)).Append(']').ToString();
                }
                case Dw3Kind.Object:
                {
                    // An empty object keeps its line break: "{\n" and the closing brace at the depth's indent.
                    var builder = new StringBuilder("{\n");
                    var pad = new string(' ', 2 * (depth + 1));
                    for (var i = 0; i < node.Keys.Count; i++)
                    {
                        var key = node.Keys[i];
                        builder.Append(pad).Append(Quote(key)).Append(": ").Append(Write(node.Members[key], depth + 1));
                        if (i < node.Keys.Count - 1) builder.Append(',');
                        builder.Append('\n');
                    }
                    return builder.Append(new string(' ', 2 * depth)).Append('}').ToString();
                }
                default:
                    throw new FormatException(Malformed);
            }
        }

        /// <summary>ConvertTo-Dw3JsonString.</summary>
        public static string Quote(string value)
        {
            var builder = new StringBuilder("\"");
            foreach (var ch in value ?? "")
            {
                if (ch == '"') builder.Append("\\\"");
                else if (ch == '\\') builder.Append("\\\\");
                else if (ch == '\n') builder.Append("\\n");
                else if (ch == '\r') builder.Append("\\r");
                else if (ch == '\t') builder.Append("\\t");
                else if (ch < 32 || ch > 126) builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                else builder.Append(ch);
            }
            return builder.Append('"').ToString();
        }

        /// <summary>Get-Dw3StringValue: an object's member with exactly this key when it is a string; null otherwise.</summary>
        public static string StringValue(Dw3Node node, string key)
        {
            if (node == null || node.Kind != Dw3Kind.Object) return null;
            if (!node.Members.TryGetValue(key, out var child) || child == null) return null;
            return child.Kind == Dw3Kind.String ? child.Text : null;
        }

        /// <summary>Set-Dw3StringValue: the member replaced in place, or added at the end.</summary>
        public static void SetStringValue(Dw3Node node, string key, string value)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (!node.Members.ContainsKey(key)) node.Keys.Add(key);
            node.Members[key] = new Dw3Node { Kind = Dw3Kind.String, Text = value ?? "" };
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _at;

            public Reader(string text) => _text = text;

            public bool AtEnd => _at >= _text.Length;

            private char Current => _text[_at];

            public void SkipWhitespace()
            {
                while (!AtEnd && (Current == ' ' || Current == '\t' || Current == '\r' || Current == '\n')) _at++;
            }

            public Dw3Node Any(int depth)
            {
                if (depth > 32) throw new FormatException(Malformed);
                SkipWhitespace();
                if (AtEnd) throw new FormatException(Malformed);
                var c = Current;
                if (c == '{') return Object(depth + 1);
                if (c == '[') return Array(depth + 1);
                if (c == '"') return String();
                // PowerShell's -eq ignored case here; the literal itself must match exactly.
                if (c == 't' || c == 'T') return Literal("true", new Dw3Node { Kind = Dw3Kind.Bool, Bool = true });
                if (c == 'f' || c == 'F') return Literal("false", new Dw3Node { Kind = Dw3Kind.Bool, Bool = false });
                if (c == 'n' || c == 'N') return Literal("null", new Dw3Node { Kind = Dw3Kind.Null });
                if (c == '-' || (c >= '0' && c <= '9')) return Number();
                throw new FormatException(Malformed);
            }

            private Dw3Node String()
            {
                if (AtEnd || Current != '"') throw new FormatException(Malformed);
                _at++;
                var builder = new StringBuilder();
                while (!AtEnd)
                {
                    var c = Current;
                    _at++;
                    if (c == '"') return new Dw3Node { Kind = Dw3Kind.String, Text = builder.ToString() };
                    if (c == '\\')
                    {
                        if (AtEnd) throw new FormatException(Malformed);
                        var e = Current;
                        _at++;
                        switch (e)
                        {
                            case '"':
                            case '\\':
                            case '/':
                                builder.Append(e);
                                break;
                            case 'b':
                                builder.Append('\b');
                                break;
                            case 'f':
                                builder.Append('\f');
                                break;
                            case 'n':
                                builder.Append('\n');
                                break;
                            case 'r':
                                builder.Append('\r');
                                break;
                            case 't':
                                builder.Append('\t');
                                break;
                            case 'u':
                            {
                                if (_at + 4 > _text.Length) throw new FormatException(Malformed);
                                var hex = _text.Substring(_at, 4);
                                _at += 4;
                                if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) throw new FormatException(Malformed);
                                builder.Append((char)code);
                                break;
                            }
                            default:
                                throw new FormatException(Malformed);
                        }
                    }
                    else
                    {
                        if (c < 32) throw new FormatException(Malformed);
                        builder.Append(c);
                    }
                }
                throw new FormatException(Malformed);
            }

            private Dw3Node Literal(string word, Dw3Node value)
            {
                if (_at + word.Length > _text.Length) throw new FormatException(Malformed);
                if (!string.Equals(_text.Substring(_at, word.Length), word, StringComparison.Ordinal)) throw new FormatException(Malformed);
                _at += word.Length;
                return value;
            }

            private static bool Digit(char c) => c >= '0' && c <= '9';

            private Dw3Node Number()
            {
                var start = _at;
                var c = Current;
                if (c == '-')
                {
                    _at++;
                    if (AtEnd) throw new FormatException(Malformed);
                    c = Current;
                }
                if (!Digit(c)) throw new FormatException(Malformed);
                if (c == '0') _at++;
                else
                {
                    while (!AtEnd && Digit(Current)) _at++;
                }
                if (!AtEnd && Current == '.')
                {
                    _at++;
                    var digits = 0;
                    while (!AtEnd && Digit(Current))
                    {
                        digits++;
                        _at++;
                    }
                    if (digits < 1) throw new FormatException(Malformed);
                }
                if (!AtEnd && (Current == 'e' || Current == 'E'))
                {
                    _at++;
                    if (!AtEnd && (Current == '+' || Current == '-')) _at++;
                    var digits = 0;
                    while (!AtEnd && Digit(Current))
                    {
                        digits++;
                        _at++;
                    }
                    if (digits < 1) throw new FormatException(Malformed);
                }
                if (_at <= start) throw new FormatException(Malformed);
                return new Dw3Node { Kind = Dw3Kind.Number, Text = _text.Substring(start, _at - start) };
            }

            private Dw3Node Object(int depth)
            {
                if (AtEnd || Current != '{') throw new FormatException(Malformed);
                _at++;
                var node = Dw3Node.EmptyObject();
                SkipWhitespace();
                if (!AtEnd && Current == '}')
                {
                    _at++;
                    return node;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || Current != '"') throw new FormatException(Malformed);
                    var key = String().Text;
                    if (node.Members.ContainsKey(key)) throw new FormatException(Malformed);
                    SkipWhitespace();
                    if (AtEnd || Current != ':') throw new FormatException(Malformed);
                    _at++;
                    var value = Any(depth);
                    node.Keys.Add(key);
                    node.Members[key] = value;
                    SkipWhitespace();
                    if (AtEnd) throw new FormatException(Malformed);
                    if (Current == ',')
                    {
                        _at++;
                        continue;
                    }
                    if (Current == '}')
                    {
                        _at++;
                        break;
                    }
                    throw new FormatException(Malformed);
                }
                return node;
            }

            private Dw3Node Array(int depth)
            {
                if (AtEnd || Current != '[') throw new FormatException(Malformed);
                _at++;
                var node = new Dw3Node { Kind = Dw3Kind.Array };
                SkipWhitespace();
                if (!AtEnd && Current == ']')
                {
                    _at++;
                    return node;
                }
                while (true)
                {
                    node.Items.Add(Any(depth));
                    SkipWhitespace();
                    if (AtEnd) throw new FormatException(Malformed);
                    if (Current == ',')
                    {
                        _at++;
                        continue;
                    }
                    if (Current == ']')
                    {
                        _at++;
                        break;
                    }
                    throw new FormatException(Malformed);
                }
                return node;
            }
        }
    }
}
