// The CARD-0849 host cache import (unit 68 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md, W10 part 3). It replaced
// scripts/c849-import-saved-donor.ps1, which scripts/c590-remote.sh ran with pwsh inside the pinned runner image; c590-remote.sh's
// c849_saved_copy now runs this command there from the host-published CLI, mounted read-only and started with dotnet exec, with
// only the explicit source (/saved, read-only) and a private staging directory (/stage) mounted. Diagnosis only: nothing but one
// Cache* word on stdout.
//
// The same steps in the same order, the same refusals and exits: a directory source or a tar archive is read twice. The first pass
// checks every entry (the path rules and root map of Resolve-Entry, a duplicate after mapping, the per-root budgets: packages 10 GiB,
// npm 2 GiB); then the space gate (free bytes on the stage below 20 GiB of headroom plus both payloads is CacheDiskLow); the second
// pass copies (a new file each, the archive's permission bits masked to 0777: setuid, setgid and sticky never kept). A directory
// entry is refused before either pass reads it when it is a reparse point, or when /usr/bin/stat -c %F names anything but a regular
// file or a directory, or disagrees with the entry (a FIFO would block FileInfo.Length and OpenRead). Any failure prints one line,
// the exception's message when it is a Cache* word and CacheSavedDonorReadFailed otherwise, and exits 2; success prints nothing and
// exits 0. PowerShell's string semantics are kept where they decide an entry: -eq, -in and -notmatch ignore case; String.StartsWith
// with a string is the current culture's. A zero-length archive member (no data stream) fails the copy as the script's null
// stream did, after its file was created: CacheSavedDonorReadFailed (kept as it was; unit 68 changes no server2 path).
//
// Seams: the file system (IFileSystem), the stage's free bytes (the stage's DriveInfo by default), the Unix type reading
// (/usr/bin/stat by default) and the Unix file mode reads and writes (File.GetUnixFileMode and SetUnixFileMode by default). Plain C#:
// no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The import's arguments: --source and --stage, both required.</summary>
    public sealed class C849SavedDonorImportOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "c849-import-saved-donor --source <path> --stage <path>";

        /// <summary>The saved donor: a directory or a tar archive.</summary>
        public string Source { get; init; } = "";

        /// <summary>The private staging directory the payload is copied into (packages/ and npm/).</summary>
        public string Stage { get; init; } = "";

        /// <summary>The arguments after the command name.</summary>
        public static C849SavedDonorImportOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--source", "--stage" }, Array.Empty<string>());
            return new C849SavedDonorImportOptions { Source = flags.Required("--source"), Stage = flags.Required("--stage") };
        }
    }

    /// <summary>What the import reads and writes besides its file system.</summary>
    public sealed class C849SavedDonorImportSeams
    {
        /// <summary>The files. Required.</summary>
        public IFileSystem Files { get; init; }

        /// <summary>The stage's free bytes (GetAvailableFreeBytes); default the stage's DriveInfo reading.</summary>
        public Func<string, long> AvailableFreeBytes { get; init; } = stage => new DriveInfo(stage).AvailableFreeSpace;

        /// <summary>/usr/bin/stat -c %F -- &lt;path&gt;: its exit code and stdout lines; default the real stat (its stderr passes through).</summary>
        public Func<string, (int Exit, string[] Lines)> UnixType { get; init; } = C849SavedDonorImport.Stat;

        /// <summary>File.GetUnixFileMode (Windows has none: the call throws there, as the script's did).</summary>
        public Func<string, UnixFileMode> GetMode { get; init; } = path =>
            OperatingSystem.IsWindows() ? throw new PlatformNotSupportedException("Unix file modes are not supported on this platform.") : File.GetUnixFileMode(path);

        /// <summary>File.SetUnixFileMode (Windows has none: the call throws there, as the script's did).</summary>
        public Action<string, UnixFileMode> SetMode { get; init; } = (path, mode) =>
        {
            if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Unix file modes are not supported on this platform.");
            File.SetUnixFileMode(path, mode);
        };
    }

    /// <summary>The CARD-0849 saved-donor import (scripts/c849-import-saved-donor.ps1 until unit 68).</summary>
    public static class C849SavedDonorImport
    {
        /// <summary>The space gate's headroom (20GB in the script).</summary>
        public const long Headroom = 20L * 1024 * 1024 * 1024;

        /// <summary>The source paths a payload root may sit under, in the script's order, and the root each maps to.</summary>
        public static readonly IReadOnlyList<(string Prefix, string Role)> Roots = new[]
        {
            ("home/app/.nuget/packages", "packages"),
            ("home/app/.npm/_cacache", "npm"),
            (".nuget/packages", "packages"),
            (".npm/_cacache", "npm"),
            ("packages", "packages"),
            ("npm", "npm"),
        };

        /// <summary>The per-root budgets (packages 10GB, npm 2GB in the script).</summary>
        public static readonly IReadOnlyDictionary<string, long> Budgets = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["packages"] = 10L * 1024 * 1024 * 1024,
            ["npm"] = 2L * 1024 * 1024 * 1024,
        };

        private static readonly Regex Word = new Regex("^Cache[A-Za-z]+$", RegexOptions.IgnoreCase);

        private sealed class Refusal : Exception
        {
            public Refusal(string diagnosis) : base(diagnosis) { }
        }

        private sealed class Mapped
        {
            public string Role;
            public string Rest;
        }

        /// <summary>Runs the import: 0 imported (nothing printed), 2 and one diagnosis line on output.</summary>
        public static int Run(C849SavedDonorImportOptions options, C849SavedDonorImportSeams seams, TextWriter output)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (seams?.Files == null) throw new ArgumentNullException(nameof(seams));
            if (output == null) throw new ArgumentNullException(nameof(output));
            var run = new Import(options, seams);
            try
            {
                var files = seams.Files;
                if (files.Directory.Exists(options.Source))
                {
                    run.ReadDirectory(false);
                    run.CheckSpace();
                    run.ReadDirectory(true);
                }
                else if (files.File.Exists(options.Source))
                {
                    run.ReadTar(false);
                    run.CheckSpace();
                    run.ReadTar(true);
                }
                else throw new Refusal("CacheSavedDonorInvalid");
            }
            catch (Exception e)
            {
                var diagnosis = e.Message ?? "";
                if (!Word.IsMatch(diagnosis)) diagnosis = "CacheSavedDonorReadFailed";
                output.WriteLine(diagnosis);
                return 2;
            }
            return 0;
        }

        /// <summary>The production Unix type reading: /usr/bin/stat -c %F -- path, its stdout lines and exit code (stderr inherited).</summary>
        public static (int Exit, string[] Lines) Stat(string path)
        {
            var start = new ProcessStartInfo("/usr/bin/stat") { UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = new UTF8Encoding(false) };
            foreach (var argument in new[] { "-c", "%F", "--", path }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length > 0 && lines[lines.Length - 1].Length == 0) lines = lines.Take(lines.Length - 1).ToArray();
            return (process.ExitCode, lines);
        }

        private sealed class Import
        {
            private readonly C849SavedDonorImportOptions _options;
            private readonly C849SavedDonorImportSeams _seams;
            private readonly IFileSystem _files;
            private readonly Dictionary<string, long> _sizes = new Dictionary<string, long>(StringComparer.Ordinal) { ["packages"] = 0, ["npm"] = 0 };
            private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

            public Import(C849SavedDonorImportOptions options, C849SavedDonorImportSeams seams)
            {
                _options = options;
                _seams = seams;
                _files = seams.Files;
            }

            // Resolve-Entry: the entry's root and the path under it; null for an entry that is no payload (".", a root itself, a
            // directory on the way to a root); CacheDonorUnsafePath for anything else.
            private static Mapped Resolve(string name, bool directory)
            {
                if (string.IsNullOrEmpty(name) || name.StartsWith("/", StringComparison.CurrentCulture) || name.Contains('\\') ||
                    name.Contains('\n') || name.Contains('\r')) throw new Refusal("CacheDonorUnsafePath");
                while (name.StartsWith("./", StringComparison.CurrentCulture)) name = name.Substring(2);
                name = name.TrimEnd('/');
                if (RollingPs.Eq(name, ".") || RollingPs.Eq(name, "")) return null;
                var parts = name.Split('/');
                if (parts.Any(part => RollingPs.In(part, "", ".", ".."))) throw new Refusal("CacheDonorUnsafePath");
                foreach (var (prefix, role) in Roots)
                {
                    if (RollingPs.Eq(name, prefix) || name.StartsWith(prefix + "/", StringComparison.Ordinal))
                    {
                        var rest = name.Substring(prefix.Length).TrimStart('/');
                        if (rest.Length == 0) return null;
                        return new Mapped { Role = role, Rest = rest };
                    }
                    if (directory && prefix.StartsWith(name + "/", StringComparison.Ordinal)) return null;
                }
                throw new Refusal("CacheDonorUnsafePath");
            }

            // Check-Entry: the first pass's duplicate check (after mapping) and budgets.
            private void Check(string name, bool directory, long length)
            {
                var mapped = Resolve(name, directory);
                if (mapped == null) return;
                if (!_seen.Add(mapped.Role + "/" + mapped.Rest)) throw new Refusal("CacheDonorDuplicateEntry");
                if (directory) return;
                _sizes[mapped.Role] += length;
                if (_sizes[mapped.Role] > Budgets[mapped.Role]) throw new Refusal("CacheBudgetExceeded");
            }

            // Check-Space: the stage keeps 20 GiB free beyond both payloads.
            public void CheckSpace()
            {
                var free = _seams.AvailableFreeBytes(_options.Stage);
                if (free < Headroom + _sizes["packages"] + _sizes["npm"]) throw new Refusal("CacheDiskLow");
            }

            // Write-Entry: a directory, or a new file with the entry's bytes and its permission bits masked to 0777.
            private void Write(Mapped mapped, bool directory, UnixFileMode mode, Stream input)
            {
                if (mapped == null) return;
                var target = _files.Path.Combine(_files.Path.Combine(_options.Stage, mapped.Role), mapped.Rest);
                if (directory)
                {
                    _files.Directory.CreateDirectory(target);
                    return;
                }
                _files.Directory.CreateDirectory(_files.Path.GetDirectoryName(target));
                var output = _files.File.Open(target, FileMode.CreateNew);
                try
                {
                    // The script called CopyTo on the entry's stream, which a zero-length member does not have.
                    if (input == null) throw new InvalidOperationException("You cannot call a method on a null-valued expression.");
                    input.CopyTo(output);
                }
                finally { output.Dispose(); }
                // Archive permission bits are untrusted. Never retain setuid, setgid or sticky bits.
                _seams.SetMode(target, (UnixFileMode)((int)mode & 511));
            }

            // Read-Tar: only directories and regular files.
            public void ReadTar(bool copy)
            {
                using var stream = _files.File.OpenRead(_options.Source);
                using var reader = new TarReader(stream, true);
                TarEntry entry;
                while ((entry = reader.GetNextEntry()) != null)
                {
                    var kind = entry.EntryType.ToString();
                    var directory = RollingPs.Eq(kind, "Directory");
                    if (!directory && !RollingPs.In(kind, "RegularFile", "V7RegularFile")) throw new Refusal("CacheDonorUnsafeEntry");
                    if (copy) Write(Resolve(entry.Name, directory), directory, entry.Mode, entry.DataStream);
                    else Check(entry.Name, directory, entry.Length);
                }
            }

            // Read-Directory: a depth-first walk (a stack of directories); each entry's Unix type is read before either pass touches
            // it, because FileInfo.Length and OpenRead can block forever on a FIFO (the source bind is read-only).
            public void ReadDirectory(bool copy)
            {
                var pending = new Stack<string>();
                pending.Push(_options.Source);
                while (pending.Count > 0)
                {
                    var parent = pending.Pop();
                    foreach (var path in _files.Directory.EnumerateFileSystemEntries(parent))
                    {
                        var attributes = _files.File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new Refusal("CacheDonorUnsafeEntry");
                        var directory = (attributes & FileAttributes.Directory) != 0;
                        var (exit, lines) = _seams.UnixType(path);
                        // A PowerShell capture of no line is $null and of several an array: neither is one of the two types.
                        var kind = lines != null && lines.Length == 1 ? lines[0] : null;
                        if (exit != 0 || kind == null || !RollingPs.In(kind, "regular file", "directory") || directory != RollingPs.Eq(kind, "directory"))
                            throw new Refusal("CacheDonorUnsafeEntry");
                        if (directory) pending.Push(path);
                        var relative = _files.Path.GetRelativePath(_options.Source, path).Replace('\\', '/');
                        if (copy)
                        {
                            var mapped = Resolve(relative, directory);
                            if (directory) Write(mapped, true, 0, null);
                            else
                            {
                                // The script opened the file and read its mode before Write-Entry looked at the mapping.
                                using var input = _files.File.OpenRead(path);
                                Write(mapped, false, _seams.GetMode(path), input);
                            }
                        }
                        else Check(relative, directory, directory ? 0 : _files.FileInfo.New(path).Length);
                    }
                }
            }
        }
    }
}
