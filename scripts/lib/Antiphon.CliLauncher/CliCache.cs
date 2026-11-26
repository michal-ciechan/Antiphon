// N0 (unit 57): the CLI cache of one checkout, <checkout>/bin-launcher/antiphon-scripts/ (ignored; outside scripts/ so
// fixtures that copy scripts/lib never carry it):
//   current        the published build the launchers run: its folder name (24 hex characters), replaced atomically
//   <identity>/    one published build per identity (CliIdentity): the CLI's output, cli-identity (its manifest) and
//                  cli-inuse (the lease); never written again once it has its name
//   build/         the shared incremental build (dotnet build --artifacts-path); never run from
//   build.record   the manifest of the inputs build/ was last built from, with the build's end time
//   build.lock     one build at a time (FileShare.None; MSBuild-free callers never take it)
//   build.log      the last build's output
//   tmp-<guid>/    a build being published, renamed to <identity> when complete
//   trash-<guid>/  a pruned folder being deleted
//   boot-<n>/      a launcher's first-run build of scripts/lib/Antiphon.CliLauncher (see scripts/antiphon)
// A process running a published build holds its cli-inuse open (shared); pruning opens it exclusively and skips the
// folder when that fails, so a nightly that holds its build for hours keeps it. On Linux the share modes are .NET's
// advisory flock (shared and exclusive). Unit 59: another product (CliProduct) has the same layout under
// bin-launcher/<its cache name>/ (the checkpoint tool's antiphon-checkpoints/, whose runs the checkpoints command leases
// while its child runs: OpenLease); only the CLI's pruning sweeps the old launcher's folders. A 24-hex folder without a
// manifest (the checkpoint tool's builds before unit 59, named by a source hash) is kept until it is untouched for 7 days.
// ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.CliLauncher
{
    /// <summary>The CLI cache of one checkout.</summary>
    public sealed class CliCache
    {
        /// <summary>The CLI's assembly in a published folder.</summary>
        public const string DllName = "Antiphon.Scripts.dll";

        /// <summary>A published folder's manifest.</summary>
        public const string StampName = "cli-identity";

        /// <summary>A published folder's lease: open (shared) while a process runs from the folder.</summary>
        public const string LeaseName = "cli-inuse";

        /// <summary>The pointer to the published build the launchers run.</summary>
        public const string PointerName = "current";

        /// <summary>Folders of the old launcher (run copies, the in-place build) are removed when untouched this long.</summary>
        public static readonly TimeSpan LegacyRetention = TimeSpan.FromDays(7);

        /// <summary>A launcher's boot-* folder is removed when untouched this long.</summary>
        public static readonly TimeSpan BootRetention = TimeSpan.FromHours(1);

        private static readonly Regex Published = new Regex("^[0-9a-f]{24}$", RegexOptions.CultureInvariant);
        private static FileStream _lease;

        /// <summary>The CLI's cache of a checkout.</summary>
        public CliCache(string checkout) : this(checkout, CliProduct.Cli) { }

        /// <summary>A product's cache of a checkout: &lt;checkout&gt;/bin-launcher/&lt;product.CacheName&gt;.</summary>
        public CliCache(string checkout, CliProduct product)
        {
            Product = product ?? throw new ArgumentNullException(nameof(product));
            Checkout = Path.GetFullPath(checkout).TrimEnd('\\', '/');
            Launcher = Path.Combine(Checkout, "bin-launcher");
            Root = Path.Combine(Launcher, product.CacheName);
        }

        /// <summary>What this cache publishes.</summary>
        public CliProduct Product { get; }

        /// <summary>The checkout.</summary>
        public string Checkout { get; }

        /// <summary>&lt;checkout&gt;/bin-launcher.</summary>
        public string Launcher { get; }

        /// <summary>&lt;checkout&gt;/bin-launcher/antiphon-scripts.</summary>
        public string Root { get; }

        /// <summary>The pointer file.</summary>
        public string PointerPath => Path.Combine(Root, PointerName);

        /// <summary>The build lock.</summary>
        public string LockPath => Path.Combine(Root, "build.lock");

        /// <summary>The last build's output.</summary>
        public string LogPath => Path.Combine(Root, "build.log");

        /// <summary>The shared build's artifacts path.</summary>
        public string BuildDirectory => Path.Combine(Root, "build");

        /// <summary>The shared build's output folder (the artifacts layout).</summary>
        public string BuildOutput => Path.Combine(BuildDirectory, "bin", Product.Assembly, "release");

        /// <summary>The manifest of the inputs the shared build was last built from.</summary>
        public string RecordPath => Path.Combine(Root, "build.record");

        /// <summary>The folder of a published build.</summary>
        public string FolderOf(string name) => Path.Combine(Root, name);

        /// <summary>The published folder the pointer names, or null when there is none.</summary>
        public string ReadPointer()
        {
            try
            {
                var name = File.ReadAllText(PointerPath).Trim();
                return Published.IsMatch(name) ? name : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return null; }
        }

        /// <summary>
        /// Points the launchers at a published folder: a new file renamed over the old one. A reader holding the old one
        /// open can make the rename fail for a moment, so it is retried; false when it never succeeded.
        /// </summary>
        public bool WritePointer(string name, Action<TimeSpan> sleep)
        {
            var temporary = Path.Combine(Root, PointerName + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, name, Encoding.ASCII);
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        File.Move(temporary, PointerPath, true);
                        return true;
                    }
                    catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && attempt < 40)
                    {
                        sleep(TimeSpan.FromMilliseconds(50));
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                TryDeleteFile(temporary);
                return false;
            }
        }

        /// <summary>The manifest of a published folder, or null.</summary>
        public static CliManifest Stamp(string folder) => CliManifest.Read(Path.Combine(folder, StampName));

        /// <summary>Whether a folder is a complete published build of the identity.</summary>
        public static bool IsPublished(string folder, string identity) => IsPublished(folder, identity, DllName);

        /// <summary>Whether a folder is a complete published build of the identity, with the product's DLL.</summary>
        public static bool IsPublished(string folder, string identity, string dllName)
        {
            var stamp = Stamp(folder);
            return stamp != null && string.Equals(stamp.Identity, identity, StringComparison.Ordinal) && File.Exists(Path.Combine(folder, dllName));
        }

        /// <summary>
        /// Holds this process's lease on the folder it runs from, until it exits: a no-op for a folder without one (not a
        /// published build) and when a lease is already held.
        /// </summary>
        public static void HoldLease(string folder)
        {
            if (_lease != null || string.IsNullOrEmpty(folder)) return;
            try { _lease = new FileStream(Path.Combine(folder, LeaseName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        /// <summary>
        /// A lease on a published folder for a child this process runs from it (the checkpoints command's tool): shared, held
        /// until disposed. Null for a folder without a lease file.
        /// </summary>
        public static IDisposable OpenLease(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            try { return new FileStream(Path.Combine(folder, LeaseName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return null; }
        }

        /// <summary>Whether a process holds the folder's lease (an exclusive open fails). A folder without a lease is not in use.</summary>
        public static bool InUse(string folder)
        {
            try
            {
                using (new FileStream(Path.Combine(folder, LeaseName), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                return false;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return true; }
        }

        /// <summary>
        /// Best effort, under the build lock: keeps the current folder and the newest others up to keep in all (by their
        /// manifest's time) and every folder in use, and removes the rest; then leftover tmp-* and trash-* folders, boot-*
        /// folders untouched for an hour, and the old launcher's folders untouched for 7 days (bin-launcher/run-*, tmp-*,
        /// trash-*, and the in-place build's bin and obj here; the CLI's cache only). A 24-hex folder without a manifest
        /// (an older build, unleased) goes when untouched for 7 days. A folder is renamed before it is deleted, so a
        /// half-deleted one never keeps a published name.
        /// </summary>
        public void Prune(string current, int keep, DateTime nowUtc)
        {
            if (!Directory.Exists(Root)) return;
            var published = Directory.GetDirectories(Root)
                .Where(folder => Published.IsMatch(Path.GetFileName(folder)) && !string.Equals(Path.GetFileName(folder), current, StringComparison.Ordinal))
                .Select(folder => (Folder: folder, Time: StampTime(folder)))
                .OrderByDescending(item => item.Time)
                .ToList();
            var kept = 1;
            foreach (var (folder, time) in published)
            {
                if (time == DateTime.MinValue)
                {
                    if (Untouched(folder, nowUtc, LegacyRetention)) Discard(folder);
                    continue;
                }
                if (kept < keep)
                {
                    kept++;
                    continue;
                }
                if (InUse(folder)) continue;
                Discard(folder);
            }
            foreach (var folder in Directory.GetDirectories(Root))
            {
                var name = Path.GetFileName(folder);
                if (name.StartsWith("tmp-", StringComparison.Ordinal) || name.StartsWith("trash-", StringComparison.Ordinal)) Discard(folder);
                else if (name.StartsWith("boot-", StringComparison.Ordinal) && Untouched(folder, nowUtc, BootRetention)) Discard(folder);
                else if (Product.SweepsLegacyLauncher && (name == "bin" || name == "obj") && Untouched(folder, nowUtc, LegacyRetention)) Discard(folder);
            }
            if (!Product.SweepsLegacyLauncher || !Directory.Exists(Launcher)) return;
            foreach (var folder in Directory.GetDirectories(Launcher))
            {
                var name = Path.GetFileName(folder);
                if ((name.StartsWith("run-", StringComparison.Ordinal) || name.StartsWith("tmp-", StringComparison.Ordinal)
                    || name.StartsWith("trash-", StringComparison.Ordinal)) && Untouched(folder, nowUtc, LegacyRetention))
                    Discard(folder);
            }
        }

        /// <summary>Renames a folder to trash-&lt;guid&gt; beside it and deletes it; a folder that cannot be renamed (in use on Windows) stays.</summary>
        public static void Discard(string folder)
        {
            var parent = Path.GetDirectoryName(folder);
            var target = folder;
            if (!Path.GetFileName(folder).StartsWith("trash-", StringComparison.Ordinal))
            {
                target = Path.Combine(parent, "trash-" + Guid.NewGuid().ToString("N"));
                try { Directory.Move(folder, target); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return; }
            }
            try { Directory.Delete(target, true); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        /// <summary>Marks a published folder as just used: pruning keeps the newest.</summary>
        public static void Touch(string folder)
        {
            try { File.SetLastWriteTimeUtc(Path.Combine(folder, StampName), DateTime.UtcNow); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        private static DateTime StampTime(string folder)
        {
            var stamp = Path.Combine(folder, StampName);
            return File.Exists(stamp) ? File.GetLastWriteTimeUtc(stamp) : DateTime.MinValue;
        }

        private static bool Untouched(string folder, DateTime nowUtc, TimeSpan retention)
        {
            try { return nowUtc - Directory.GetLastWriteTimeUtc(folder) > retention; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return false; }
        }

        private static void TryDeleteFile(string path)
        {
            try { File.Delete(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }
}
