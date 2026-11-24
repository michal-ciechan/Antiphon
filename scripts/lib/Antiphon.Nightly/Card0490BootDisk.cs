// CARD-0490: the source-free QEMU boot disk of the PC-28-31 native lane (unit 39 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; it replaced scripts/build-card0490-bootdisk.ps1, an operator
// tool nothing else ran). The CLI runs it as
//
//   antiphon-scripts build-card0490-bootdisk [--qemu-dir <dir>] [--output-path <file>] [--lock-file <file>]
//       [--virtual-size-mib 64] [--smoke] [--write-lock]
//
// The image is generated, never downloaded: every byte comes from the boot sector table below plus zero padding, so two runs
// on any machine produce identical bytes. It holds no application source, no compiled Antiphon assemblies, no credentials and
// no saved VM memory - only a 512-byte MBR that proves the guest boots and can be driven over the serial chardev the plan's
// invocation supplies. Guest contract (tests/fixtures/card0490-linux/README.md): on COM1 (0x3F8, 115200 8N1) it emits
// "CARD0490-BOOT-OK" once the MBR runs, echoes every byte it receives, and on 'q' emits "CARD0490-HALT" and powers off
// through ACPI PM1a_CNT (0x604).
//
// The run: qemu-img.exe is found under the QEMU directory (--qemu-dir, else CARD0490_QEMU_DIR, which the CLI passes in) and
// refused unless it matches the lock's qemu-img pin (a pending-operator-pin is not compared); the boot sector is written to
// <output>.raw, padded to --virtual-size-mib MiB, converted with "qemu-img convert -f raw -O qcow2 -o compat=1.1" and the raw
// deleted. --smoke checks qemu-system-x86_64.exe against the qemu pin, makes a "qemu-img create -f qcow2 -F qcow2 -b" overlay,
// boots it with the plan's -blockdev/virtio-blk-pci arguments, drives CARD0490-DRIVEN and then 'q', and requires the banner,
// the echo, the halt and exit 0 within 60 s. --write-lock pins bootDisk in the lock (relative paths: to the checkout). The
// tools run on the host's launcher and share this console (the script's Out-Host); the boot is the seam's
// (Card0490BootDiskSeams.Boot; production BootGuest, which starts QEMU). Files are IFileSystem's. Exit 0 done, 1 a refusal
// or failure (one stderr line), 2 usage. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The build-card0490-bootdisk flags.</summary>
    public sealed class Card0490BootDiskOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "build-card0490-bootdisk [--qemu-dir <dir>] [--output-path <file>] [--lock-file <file>] [--virtual-size-mib 64]"
            + " [--smoke] [--write-lock]";

        /// <summary>The asset lock, relative to the checkout.</summary>
        public const string DefaultLockFile = "tests/fixtures/card0490-linux/assets.lock.json";

        /// <summary>The checkout (relative lock paths and the default output are under it).</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The extracted pinned QEMU bundle; empty when neither the flag nor CARD0490_QEMU_DIR gave one.</summary>
        public string QemuDir { get; init; } = "";

        /// <summary>The qcow2 to write.</summary>
        public string OutputPath { get; init; } = "";

        /// <summary>The asset lock.</summary>
        public string LockFile { get; init; } = "";

        /// <summary>The raw image's size in MiB.</summary>
        public int VirtualSizeMiB { get; init; } = 64;

        /// <summary>Boot the disk and check the guest contract.</summary>
        public bool Smoke { get; init; }

        /// <summary>Pin bootDisk in the lock.</summary>
        public bool WriteLock { get; init; }

        /// <summary>The flags after the command name; --qemu-dir falls back to the CARD0490_QEMU_DIR value the caller passes.</summary>
        public static Card0490BootDiskOptions Parse(IReadOnlyList<string> arguments, string qemuDirVariable, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--qemu-dir", "--output-path", "--lock-file", "--virtual-size-mib" },
                new[] { "--smoke", "--write-lock" });
            var size = flags.Int("--virtual-size-mib", 64);
            if (size < 1 || size > 1024 * 1024) throw new NightlyUsageException("--virtual-size-mib must be between 1 and 1048576");
            var root = repoRoot ?? "";
            var lockFile = flags.Value("--lock-file", "") is { Length: > 0 } givenLock ? givenLock : DefaultLockFile;
            return new Card0490BootDiskOptions
            {
                RepoRoot = root,
                QemuDir = flags.Value("--qemu-dir", "") is { Length: > 0 } givenDir ? givenDir : qemuDirVariable ?? "",
                OutputPath = flags.Value("--output-path", "") is { Length: > 0 } givenOutput
                    ? givenOutput
                    : Path.Combine(root, ".antiphon", "card0490-assets", "card0490-bootdisk.qcow2"),
                LockFile = Path.IsPathRooted(lockFile) ? lockFile : Path.GetFullPath(Path.Combine(root, lockFile)),
                VirtualSizeMiB = size,
                Smoke = flags.Switch("--smoke"),
                WriteLock = flags.Switch("--write-lock"),
            };
        }
    }

    /// <summary>One guest boot: QEMU's path and arguments, the bytes to drive and the waits.</summary>
    public sealed record Card0490GuestBoot(string QemuSystem, IReadOnlyList<string> Arguments, string Driven, TimeSpan BootWait, TimeSpan EchoWait,
        TimeSpan ExitWait);

    /// <summary>What a boot observed: whether QEMU exited within the wait, its exit code, the guest's serial output and QEMU's stderr.</summary>
    public sealed record Card0490GuestRun(bool Exited, int ExitCode, string Serial, string Error);

    /// <summary>The boot-disk run's outside world beyond the host and the file system.</summary>
    public sealed class Card0490BootDiskSeams
    {
        /// <summary>Boots the guest (production: <see cref="Card0490BootDisk.BootGuest"/>).</summary>
        public Func<Card0490GuestBoot, Card0490GuestRun> Boot { get; init; } = Card0490BootDisk.BootGuest;
    }

    /// <summary>The CARD-0490 boot disk (build-card0490-bootdisk.ps1).</summary>
    public static class Card0490BootDisk
    {
        /// <summary>The pin value a lock carries before an operator pins it; it is not compared.</summary>
        public const string PendingPin = "pending-operator-pin";

        /// <summary>What the smoke drives into the guest before 'q'.</summary>
        public const string Driven = "CARD0490-DRIVEN";

        /// <summary>The lock's bootDisk provenance: the generator, because the disk is built, not fetched.</summary>
        public const string Source = "scripts/antiphon build-card0490-bootdisk";

        private const long MiB = 1024 * 1024;

        private static readonly JsonSerializerOptions LockJson = new JsonSerializerOptions
        {
            WriteIndented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private sealed class Refusal : Exception
        {
            public Refusal(string message) : base(message) { }
        }

        /// <summary>
        /// The 512-byte master boot record: hand-assembled 16-bit real mode at org 0x7C00. Each row names its own offset, so a
        /// mis-sized row fails here instead of producing a silently wrong image.
        /// </summary>
        public static byte[] BootSector()
        {
            var code = new List<byte>();
            void Emit(int at, string hex)
            {
                if (at != code.Count) throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "boot sector offset drift: expected 0x{0:X2}, row declares 0x{1:X2}", code.Count, at));
                code.AddRange(hex.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(b => Convert.ToByte(b, 16)));
            }
            void EmitBytes(int at, byte[] bytes)
            {
                if (at != code.Count) throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "boot sector offset drift: expected 0x{0:X2}, row declares 0x{1:X2}", code.Count, at));
                code.AddRange(bytes);
            }

            // entry: flat segments, stack just below the load address
            Emit(0x00, "FA 31 C0 8E D8 8E C0 8E D0 BC 00 7C FB");                 // cli; xor ax,ax; ds=es=ss=0; sp=0x7C00; sti
            // COM1 (0x3F8) to 115200 8N1, FIFO on, DTR/RTS asserted
            Emit(0x0D, "BA F9 03"); Emit(0x10, "30 C0"); Emit(0x12, "EE");          // IER = 0x00
            Emit(0x13, "BA FB 03"); Emit(0x16, "B0 80"); Emit(0x18, "EE");          // LCR = DLAB
            Emit(0x19, "BA F8 03"); Emit(0x1C, "B0 01"); Emit(0x1E, "EE");          // DLL = 1
            Emit(0x1F, "BA F9 03"); Emit(0x22, "30 C0"); Emit(0x24, "EE");          // DLM = 0
            Emit(0x25, "BA FB 03"); Emit(0x28, "B0 03"); Emit(0x2A, "EE");          // LCR = 8N1
            Emit(0x2B, "BA FA 03"); Emit(0x2E, "B0 C7"); Emit(0x30, "EE");          // FCR = 0xC7
            Emit(0x31, "BA FC 03"); Emit(0x34, "B0 03"); Emit(0x36, "EE");          // MCR = DTR|RTS
            // banner
            Emit(0x37, "BE 87 7C");                                                 // mov si, banner
            Emit(0x3A, "AC"); Emit(0x3B, "84 C0"); Emit(0x3D, "74 05");             // lodsb; test al,al; jz echo
            Emit(0x3F, "E8 25 00"); Emit(0x42, "EB F6");                            // call putc; jmp banner loop
            // echo loop: every driven byte comes straight back out; 'q' ends the run
            Emit(0x44, "E8 31 00"); Emit(0x47, "3C 71"); Emit(0x49, "74 05");       // call getc; cmp al,'q'; je halt
            Emit(0x4B, "E8 19 00"); Emit(0x4E, "EB F4");                            // call putc; jmp echo
            // halt: farewell banner then ACPI S5
            Emit(0x50, "BE 9A 7C");
            Emit(0x53, "AC"); Emit(0x54, "84 C0"); Emit(0x56, "74 05");
            Emit(0x58, "E8 0C 00"); Emit(0x5B, "EB F6");
            Emit(0x5D, "BA 04 06"); Emit(0x60, "B8 00 20"); Emit(0x63, "EF");       // out 0x604, 0x2000
            Emit(0x64, "F4"); Emit(0x65, "EB FD");                                  // hlt; jmp $
            // putc: spin on THRE, then write AL
            Emit(0x67, "52"); Emit(0x68, "50");
            Emit(0x69, "BA FD 03"); Emit(0x6C, "EC"); Emit(0x6D, "A8 20"); Emit(0x6F, "74 F8");
            Emit(0x71, "58"); Emit(0x72, "BA F8 03"); Emit(0x75, "EE"); Emit(0x76, "5A"); Emit(0x77, "C3");
            // getc: spin on DR, then read AL
            Emit(0x78, "52");
            Emit(0x79, "BA FD 03"); Emit(0x7C, "EC"); Emit(0x7D, "A8 01"); Emit(0x7F, "74 F8");
            Emit(0x81, "BA F8 03"); Emit(0x84, "EC"); Emit(0x85, "5A"); Emit(0x86, "C3");

            var crlfNul = new byte[] { 13, 10, 0 };
            EmitBytes(0x87, Encoding.ASCII.GetBytes("CARD0490-BOOT-OK").Concat(crlfNul).ToArray());
            EmitBytes(0x9A, new byte[] { 13, 10 }.Concat(Encoding.ASCII.GetBytes("CARD0490-HALT")).Concat(crlfNul).ToArray());
            if (code.Count != 0xAC) throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                "boot sector text ended at 0x{0:X2}, expected 0xAC", code.Count));

            var sector = new byte[512];
            code.CopyTo(sector);
            sector[510] = 0x55;
            sector[511] = 0xAA;
            return sector;
        }

        /// <summary>Builds the disk (and smokes it, and pins it) as the options say. Exit 0 done, 1 refused or failed.</summary>
        public static int Run(Card0490BootDiskOptions options, NightlyHost host, IFileSystem files, Card0490BootDiskSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            seams ??= new Card0490BootDiskSeams();
            try
            {
                Build(options, host, files, seams);
                return 0;
            }
            catch (Exception e) when (e is Refusal || e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                host.Error.WriteLine(e.Message);
                return 1;
            }
        }

        private static void Build(Card0490BootDiskOptions options, NightlyHost host, IFileSystem files, Card0490BootDiskSeams seams)
        {
            var qemuImg = ResolveTool(files, options.QemuDir, "qemu-img.exe");
            if (!(JsonNode.Parse(files.File.ReadAllText(options.LockFile)) is JsonObject assetLock))
                throw new Refusal(options.LockFile + " is not a JSON object.");

            // The disk is only trustworthy if the tool that wrote it is the pinned one.
            CheckPin(files, qemuImg, assetLock, "qemu-img", options.LockFile);

            var output = files.Path.GetFullPath(options.OutputPath);
            var outDir = files.Path.GetDirectoryName(output);
            files.Directory.CreateDirectory(outDir);
            var raw = files.Path.ChangeExtension(output, ".raw");
            Delete(files, raw);
            Delete(files, output);

            using (var stream = files.File.Create(raw))
            {
                var sector = BootSector();
                stream.Write(sector, 0, sector.Length);
                stream.SetLength(options.VirtualSizeMiB * MiB);
            }
            host.Out.WriteLine("raw image " + Length(files, raw) + " bytes sha256=" + Sha256(files, raw));

            Tool(host, qemuImg, options.RepoRoot, "qemu-img convert", "convert", "-f", "raw", "-O", "qcow2", "-o", "compat=1.1", raw, output);
            Delete(files, raw);

            var bootSha = Sha256(files, output);
            host.Out.WriteLine("bootDisk " + Length(files, output) + " bytes sha256=" + bootSha + " path=" + output);

            if (options.Smoke) Smoke(options, host, files, seams, qemuImg, assetLock, output, outDir);

            if (options.WriteLock)
            {
                // The same shape as the qemu and qemu-img entries: version, hash, provenance, file name.
                assetLock["bootDisk"] = new JsonObject
                {
                    ["version"] = "1",
                    ["sha256"] = bootSha,
                    ["source"] = Source,
                    ["file"] = files.Path.GetFileName(output),
                    ["format"] = "qcow2",
                    ["virtualSizeMiB"] = options.VirtualSizeMiB,
                };
                files.File.WriteAllText(options.LockFile, assetLock.ToJsonString(LockJson) + "\n", new UTF8Encoding(false));
                host.Out.WriteLine("Wrote bootDisk pin to " + options.LockFile);
            }
        }

        private static void Smoke(Card0490BootDiskOptions options, NightlyHost host, IFileSystem files, Card0490BootDiskSeams seams, string qemuImg,
            JsonObject assetLock, string output, string outDir)
        {
            var qemuSystem = ResolveTool(files, options.QemuDir, "qemu-system-x86_64.exe");
            CheckPin(files, qemuSystem, assetLock, "qemu", options.LockFile);
            var share = files.Path.Combine(files.Path.GetDirectoryName(qemuSystem), "share");
            var overlay = files.Path.Combine(outDir, "card0490-bootdisk-smoke.qcow2");
            Delete(files, overlay);
            Tool(host, qemuImg, options.RepoRoot, "qemu-img create overlay", "create", "-f", "qcow2", "-F", "qcow2", "-b", output, overlay);

            var rootJson = new JsonObject
            {
                ["driver"] = "qcow2",
                ["node-name"] = "pcroot",
                ["file"] = new JsonObject { ["driver"] = "file", ["filename"] = overlay },
            }.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            var arguments = new List<string>();
            if (files.Directory.Exists(share)) arguments.AddRange(new[] { "-L", share });
            arguments.AddRange(new[]
            {
                "-no-user-config", "-nodefaults",
                "-machine", "q35", "-accel", "tcg,thread=multi", "-cpu", "max", "-smp", "2", "-m", "4096",
                "-display", "none", "-monitor", "none", "-nic", "none", "-no-reboot",
                "-chardev", "stdio,id=pcio,signal=off", "-serial", "chardev:pcio",
                "-blockdev", rootJson,
                "-device", "virtio-blk-pci,drive=pcroot,bootindex=1",
            });

            var run = seams.Boot(new Card0490GuestBoot(qemuSystem, arguments, Driven, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(60)));
            if (!run.Exited) throw new Refusal("Guest did not power off within 60s.");
            host.Out.WriteLine("--- guest serial ---");
            host.Out.WriteLine(run.Serial ?? "");
            if (!string.IsNullOrEmpty(run.Error))
            {
                host.Out.WriteLine("--- qemu stderr ---");
                host.Out.WriteLine(run.Error);
            }
            var serial = run.Serial ?? "";
            if (!serial.Contains("CARD0490-BOOT-OK", StringComparison.Ordinal)) throw new Refusal("Guest never announced CARD0490-BOOT-OK.");
            if (!serial.Contains(Driven, StringComparison.Ordinal)) throw new Refusal("Guest did not echo driven input '" + Driven + "'.");
            if (!serial.Contains("CARD0490-HALT", StringComparison.Ordinal)) throw new Refusal("Guest did not acknowledge the halt byte.");
            if (run.ExitCode != 0) throw new Refusal("QEMU exited " + run.ExitCode.ToString(CultureInfo.InvariantCulture) + " after guest poweroff.");
            Delete(files, overlay);
            host.Out.WriteLine("Smoke passed: boot banner, driven echo and ACPI poweroff all observed.");
        }

        /// <summary>
        /// The production boot: QEMU with its stdio as the guest's COM1. After the boot wait the driven bytes are written, after
        /// the echo wait 'q'; QEMU must exit within the exit wait, else its tree is killed. Starts a VM: no test calls it.
        /// </summary>
        public static Card0490GuestRun BootGuest(Card0490GuestBoot boot)
        {
            var info = new ProcessStartInfo(boot.QemuSystem)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in boot.Arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info) ?? throw new IOException("QEMU did not start: " + boot.QemuSystem);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var exited = false;
            try
            {
                System.Threading.Thread.Sleep(boot.BootWait);
                process.StandardInput.Write(boot.Driven);
                process.StandardInput.Flush();
                System.Threading.Thread.Sleep(boot.EchoWait);
                process.StandardInput.Write('q');
                process.StandardInput.Flush();
                exited = process.WaitForExit((int)boot.ExitWait.TotalMilliseconds);
                if (!exited)
                {
                    process.Kill(true);
                    process.WaitForExit(10000);
                }
            }
            finally
            {
                if (!process.HasExited)
                {
                    try { process.Kill(true); }
                    catch (InvalidOperationException) { }
                }
            }
            return new Card0490GuestRun(exited, exited ? process.ExitCode : 1, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }

        private static string ResolveTool(IFileSystem files, string directory, string leaf)
        {
            if (string.IsNullOrWhiteSpace(directory) || !files.Directory.Exists(directory))
                throw new Refusal("QemuDir not supplied or missing. Pass --qemu-dir or set CARD0490_QEMU_DIR to the extracted pinned QEMU bundle.");
            return files.Directory.EnumerateFiles(directory, leaf, SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new Refusal(leaf + " not found under " + directory + ".");
        }

        private static void CheckPin(IFileSystem files, string tool, JsonObject assetLock, string entry, string lockFile)
        {
            var pin = (assetLock[entry] as JsonObject)?["sha256"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
            if (string.Equals(pin, PendingPin, StringComparison.OrdinalIgnoreCase)) return;
            var sha = Sha256(files, tool);
            if (!string.Equals(sha, pin, StringComparison.OrdinalIgnoreCase))
                throw new Refusal(files.Path.GetFileName(tool) + " sha256 " + sha + " does not match the pin " + pin + " in " + lockFile + ".");
        }

        private static void Tool(NightlyHost host, string tool, string workingDirectory, string what, params string[] arguments)
        {
            var result = host.Launch(new NightlyLaunch(tool, arguments, workingDirectory));
            if (result.ExitCode != 0) throw new Refusal(what + " failed (exit " + result.ExitCode.ToString(CultureInfo.InvariantCulture) + ").");
        }

        private static string Sha256(IFileSystem files, string path)
        {
            using var stream = files.File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static long Length(IFileSystem files, string path) => files.FileInfo.New(path).Length;

        private static void Delete(IFileSystem files, string path)
        {
            if (files.File.Exists(path)) files.File.Delete(path);
        }
    }
}
