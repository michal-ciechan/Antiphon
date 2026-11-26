// Unit 63 (N1 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md): scripts/antiphon.exe, the CLI entry for
// PowerShell on Windows. In PowerShell `scripts/antiphon` resolves to scripts\antiphon.exe ahead of scripts\antiphon.cmd
// (PATHEXT), and PowerShell passes a native program every argument it would pass dotnet: a `|`, an empty argument, inner
// quotes and cmd.exe's metacharacters arrive, where cmd.exe broke them. (A PowerShell script entry would not do: PowerShell
// binds a script's arguments first and drops `--` and the `-x:` of a -x:y token.) The file is generated, never tracked
// (/scripts/antiphon.exe in .gitignore): this project published as one framework-dependent file (Product: dotnet publish -r
// <this RID> --no-self-contained -p:PublishSingleFile=true) by the cached launcher's machinery under
// bin-launcher/antiphon-entry/ (identity, lock, incremental build, atomic publish, pruning), then copied to the checkout.
//
// Run (Program.Main when this process is antiphon.exe): the checkout is the folder above the exe's. The published CLI
// bin-launcher/antiphon-scripts/current names runs as `dotnet exec <dll> <arguments>` with ANTIPHON_CLI_CHECK=<checkout>,
// the arguments as a list, on this console, in this directory and environment; this process ignores Ctrl+C and Ctrl+Break
// (the CLI gets them too) and exits with the CLI's exit code (CliGate.RunChild). With nothing published it publishes the
// CLI in process first (CliPublisher.Ensure, what the .cmd's bootstrap runs). Exit 9 with one stderr line: the CLI could
// not be built or started.
//
// Refresh (CliGate.Enter's Entry hook, production on Windows): when the command runs from a current published build, once
// per build (bin-launcher/antiphon-entry/checked names the last build checked and the outcome), the entry is published for
// the launcher project's current inputs (built only when that identity has no folder yet) and installed when
// scripts/antiphon.exe is missing or differs. A running entry cannot be replaced, so it is renamed to a trash-<guid>.exe
// beside the published entries first (Windows renames a running program) and deleted once nothing runs it. A refresh never
// waits for another one (install.lock) and never fails the command: a failure is one stderr line and is not tried again
// until the CLI is published again. Before the first refresh (a new or cleaned checkout) PowerShell runs the .cmd.
// ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Antiphon.CliLauncher
{
    /// <summary>The PowerShell entry, scripts/antiphon.exe: its run and its refresh.</summary>
    public static class CliEntry
    {
        /// <summary>The entry's file name, in a checkout's scripts folder.</summary>
        public const string FileName = "antiphon.exe";

        /// <summary>The launcher project, relative to a checkout: the entry's only source.</summary>
        public const string LauncherProject = "scripts/lib/Antiphon.CliLauncher/Antiphon.CliLauncher.csproj";

        /// <summary>The record of the last refresh: the CLI build it ran for, a space, then "ok" or "failed".</summary>
        public const string CheckedName = "checked";

        /// <summary>The runtime the entry is published for (win-x64 on the laptop and the desktop).</summary>
        public static string Rid => RuntimeInformation.RuntimeIdentifier;

        /// <summary>The entry as the cached launcher publishes it: bin-launcher/antiphon-entry/&lt;identity&gt;/Antiphon.CliLauncher.exe.</summary>
        public static CliProduct Product { get; } = new CliProduct("antiphon-entry", LauncherProject, "Antiphon.CliLauncher", "the PowerShell entry",
            "antiphon-cli",
            template: new[]
            {
                "publish", CliPublisher.ProjectToken, "-c", "Release", "-r", Rid, "--no-self-contained", "-p:PublishSingleFile=true",
                "--artifacts-path", CliPublisher.ArtifactsToken, "-nologo", "-v", "q", "-m:2", "-nodeReuse:false", "-p:UseSharedCompilation=false",
            },
            output: "publish/Antiphon.CliLauncher/release_" + Rid, mainFile: "Antiphon.CliLauncher.exe");

        /// <summary>A checkout's entry: &lt;checkout&gt;/scripts/antiphon.exe.</summary>
        public static string PathIn(string checkout) => Path.Combine(checkout, "scripts", FileName);

        /// <summary>Whether a process is the entry: its executable is named antiphon.exe (a bootstrap runs under dotnet).</summary>
        public static bool IsEntry(string processPath) =>
            !string.IsNullOrEmpty(processPath) && string.Equals(Path.GetFileName(processPath), FileName, StringComparison.OrdinalIgnoreCase);

        /// <summary>The checkout of an entry at &lt;checkout&gt;/scripts/antiphon.exe: the folder above the exe's.</summary>
        public static string CheckoutOf(string exePath)
        {
            var scripts = Path.GetDirectoryName(Path.GetFullPath(exePath));
            var checkout = scripts == null ? null : Path.GetDirectoryName(scripts);
            if (string.IsNullOrEmpty(checkout)) throw new ArgumentException("no checkout above " + exePath, nameof(exePath));
            return checkout;
        }

        /// <summary>The real entry: this process's path, production's publish and child.</summary>
        public static int Run(IReadOnlyList<string> args, string exePath) => Run(args, exePath, new CliPublishOptions(), CliGate.RunChild);

        /// <summary>
        /// The entry against seams: the published CLI (or the one publish makes) runs through runChild(dll, args, checkout),
        /// whose exit code this returns; CliPublisher.Unavailable (9) after one line on publish.Error when none could be had
        /// or started.
        /// </summary>
        public static int Run(IReadOnlyList<string> args, string exePath, CliPublishOptions publish, Func<string, IReadOnlyList<string>, string, int> runChild)
        {
            publish ??= new CliPublishOptions();
            string checkout;
            try { checkout = CheckoutOf(exePath); }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                publish.Error.WriteLine("antiphon-cli: no checkout above " + exePath);
                return CliPublisher.Unavailable;
            }
            var dll = Published(checkout) ?? CliPublisher.Ensure(checkout, publish);
            if (dll == null) return CliPublisher.Unavailable;
            try
            {
                return runChild(dll, args, checkout);
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
            {
                publish.Error.WriteLine("antiphon-cli: cannot start the CLI (dotnet exec " + dll + "): " + e.Message.Replace("\r", " ").Replace("\n", " "));
                return CliPublisher.Unavailable;
            }
        }

        /// <summary>The CLI the pointer names when its DLL is there (as scripts/antiphon and the .cmd resolve it), else null.</summary>
        public static string Published(string checkout)
        {
            var cache = new CliCache(checkout);
            var name = cache.ReadPointer();
            if (name == null) return null;
            var dll = Path.Combine(cache.FolderOf(name), CliCache.DllName);
            return File.Exists(dll) ? dll : null;
        }

        /// <summary>Production's refresh: the real publish, the build lock waited for at most two minutes.</summary>
        public static void Refresh(string checkout, string cliFolder, TextWriter error) =>
            Refresh(checkout, cliFolder, error, new CliPublishOptions { Error = TextWriter.Null, LockWait = TimeSpan.FromMinutes(2) });

        /// <summary>
        /// Keeps &lt;checkout&gt;/scripts/antiphon.exe current for the CLI build in cliFolder (see the file comment). Best effort:
        /// one line on error when it could not be published or installed, nothing otherwise; never throws for I/O.
        /// </summary>
        public static void Refresh(string checkout, string cliFolder, TextWriter error, CliPublishOptions publish)
        {
            try
            {
                var build = Path.GetFileName((cliFolder ?? "").TrimEnd('\\', '/'));
                var cache = new CliCache(checkout, Product);
                var exe = PathIn(checkout);
                var record = Path.Combine(cache.Root, CheckedName);
                if (Done(record, build, exe)) return;
                Directory.CreateDirectory(cache.Root);
                using var held = TryLock(Path.Combine(cache.Root, "install.lock"));
                if (held == null || Done(record, build, exe)) return;
                var captured = new StringWriter();
                var source = CliPublisher.Ensure(checkout, new CliPublishOptions
                {
                    Error = captured, UtcNow = publish.UtcNow, Sleep = publish.Sleep, LockWait = publish.LockWait, LockPoll = publish.LockPoll,
                    Attempts = publish.Attempts, Keep = publish.Keep, Build = publish.Build,
                }, Product);
                string failure = null;
                if (source == null) failure = captured.ToString().Trim();
                else if (!Install(source, exe, cache.Root)) failure = "cannot replace it";
                if (failure != null && failure.StartsWith(Product.Prefix + ": ", StringComparison.Ordinal)) failure = failure.Substring(Product.Prefix.Length + 2);
                if (failure == "") failure = "no reason given";
                File.WriteAllText(record, build + (failure == null ? " ok" : " failed"));
                if (failure != null)
                {
                    error.WriteLine("antiphon-cli: the PowerShell entry " + exe + " is not current (" + Line(failure)
                        + "); PowerShell keeps the old one or scripts\\antiphon.cmd until the CLI is published again");
                    error.Flush();
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is CliInputException || e is ArgumentException)
            {
                error.WriteLine("antiphon-cli: the PowerShell entry was not refreshed: " + Line(e.Message));
                error.Flush();
            }
        }

        /// <summary>
        /// Copies the published entry to exe unless it holds the same bytes: a new file renamed over it, or, when it runs (a
        /// running program cannot be replaced), after it is renamed to trash-&lt;guid&gt;.exe in folder. Then removes the trash
        /// nothing runs any more. False when it could not be replaced.
        /// </summary>
        public static bool Install(string source, string exe, string folder)
        {
            if (File.Exists(exe) && SameBytes(source, exe))
            {
                Sweep(folder);
                return true;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            var temporary = Path.Combine(folder, "tmp-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                File.Copy(source, temporary, true);
                try
                {
                    File.Move(temporary, exe, true);
                }
                catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && File.Exists(exe))
                {
                    File.Move(exe, Path.Combine(folder, "trash-" + Guid.NewGuid().ToString("N") + ".exe"));
                    File.Move(temporary, exe);
                }
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
            finally
            {
                TryDelete(temporary);
                Sweep(folder);
            }
        }

        // Whether the record says this build was refreshed: failed (not tried again), or ok with the entry still there.
        private static bool Done(string record, string build, string exe)
        {
            string text;
            try { text = File.ReadAllText(record).Trim(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return false; }
            if (text == build + " failed") return true;
            return text == build + " ok" && File.Exists(exe);
        }

        private static FileStream TryLock(string path)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { return null; }
        }

        private static bool SameBytes(string left, string right)
        {
            var a = new FileInfo(left);
            var b = new FileInfo(right);
            if (a.Length != b.Length) return false;
            return CliIdentity.HashFile(left) == CliIdentity.HashFile(right);
        }

        // Old entries renamed away while they ran; one still running stays for the next install.
        private static void Sweep(string folder)
        {
            try
            {
                foreach (var file in Directory.GetFiles(folder, "trash-*.exe")) TryDelete(file);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        private static string Line(string text) => (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
