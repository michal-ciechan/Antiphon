using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0490: the build-card0490-bootdisk command (Card0490BootDisk.Run), in process on a MockFileSystem with a recording
/// launcher for qemu-img and a fake guest boot. It replaced scripts/build-card0490-bootdisk.ps1 (unit 39 of the 2026-11-22
/// plan), which had no test. The boot sector is pinned to the script's bytes (its SHA-256, and the 64 MiB raw image's, were
/// read from the script's New-BootSector before it was deleted). Nothing here starts QEMU: the converted qcow2 and its pinned
/// hash in the committed lock need the real qemu-img, and the smoke needs the real guest; both stay operator steps.
/// </summary>
[Category("Unit")]
public sealed class Card0490BootDiskTests
{
    private const string SectorSha256 = "65b3d65057c8efd6697cc2ae77972b4e6bc565d16bc4db04d459d6f15e0182a0";
    private const string Raw64Sha256 = "ee1aa6062c8b051ae99c5709243338499c9be346a2f93cee683f6059b27f0e8c";
    private const string Raw1Sha256 = "e9fe9b1da35f9cf19975801e6470b5a9a5f0f067b7e47c683af226543ac68962";
    private const string SectorCodeHex =
        "FA31C08ED88EC08ED0BC007CFBBAF90330C0EEBAFB03B080EEBAF803B001EEBAF90330C0EEBAFB03B003EEBAFA03B0C7EEBAFC03B003EEBE877CAC84C07405"
        + "E82500EBF6E831003C717405E81900EBF4BE9A7CAC84C07405E80C00EBF6BA0406B80020EFF4EBFD5250BAFD03ECA82074F858BAF803EE5AC352BAFD03ECA8"
        + "0174F8BAF803EC5AC343415244303439302D424F4F542D4F4B0D0A000D0A43415244303439302D48414C540D0A00";

    private const string GoodSerial = "CARD0490-BOOT-OK\r\nCARD0490-DRIVEN\r\nCARD0490-HALT\r\n";

    [Test]
    public void Boot_sector_is_the_scripts_table()
    {
        var sector = Card0490BootDisk.BootSector();
        sector.Length.ShouldBe(512);
        Sha(sector).ShouldBe(SectorSha256);
        Convert.ToHexString(sector, 0, 0xAC).ShouldBe(SectorCodeHex);
        sector.Skip(0xAC).Take(510 - 0xAC).ShouldAllBe(b => b == 0);
        (sector[510], sector[511]).ShouldBe(((byte)0x55, (byte)0xAA));
    }

    [Test]
    public void Build_converts_the_padded_sector_with_the_pinned_qemu_img()
    {
        var world = new World();
        var run = world.Run("--virtual-size-mib", "1");

        run.ShouldBe(0, world.Error.ToString());
        var output = world.Files.Path.Combine(world.Repo, ".antiphon", "card0490-assets", "card0490-bootdisk.qcow2");
        var raw = world.Files.Path.ChangeExtension(output, ".raw");
        var convert = world.Launcher.Launches.ShouldHaveSingleItem();
        convert.FileName.ShouldBe(world.QemuImg);
        convert.Arguments.ShouldBe(["convert", "-f", "raw", "-O", "qcow2", "-o", "compat=1.1", raw, output]);
        world.ConvertedRawSha.ShouldBe(Raw1Sha256);
        world.Files.File.Exists(raw).ShouldBeFalse("the raw image is deleted after the convert");
        var lines = world.Lines();
        lines.ShouldContain("raw image 1048576 bytes sha256=" + Raw1Sha256);
        lines.ShouldContain("bootDisk " + world.Files.FileInfo.New(output).Length + " bytes sha256=" + Sha(world.Files.File.ReadAllBytes(output)) + " path=" + output);
        world.Files.File.ReadAllText(world.Lock).ShouldBe(World.LockText(world.ImgPin, world.SystemPin), "no --write-lock, no lock change");
        world.Boots.ShouldBeEmpty();
    }

    [Test]
    public void The_default_raw_image_is_the_scripts_64_mib()
    {
        var world = new World();
        world.Run().ShouldBe(0, world.Error.ToString());
        world.ConvertedRawSha.ShouldBe(Raw64Sha256);
        world.Lines().ShouldContain("raw image " + 64 * 1024 * 1024 + " bytes sha256=" + Raw64Sha256);
    }

    [Test]
    public void A_missing_or_unpinned_tool_is_refused_before_anything_is_written()
    {
        var missing = new World { QemuDirVariable = "" };
        missing.Run("--virtual-size-mib", "1").ShouldBe(1);
        missing.Error.ToString().Trim().ShouldBe("QemuDir not supplied or missing. Pass --qemu-dir or set CARD0490_QEMU_DIR to the extracted pinned QEMU bundle.");
        missing.Launcher.Launches.ShouldBeEmpty();

        var absent = new World();
        absent.Files.File.Delete(absent.QemuImg);
        absent.Run("--virtual-size-mib", "1").ShouldBe(1);
        absent.Error.ToString().Trim().ShouldBe("qemu-img.exe not found under " + absent.Qemu + ".");

        var foreign = new World(imgPin: new string('0', 64));
        foreign.Run("--virtual-size-mib", "1").ShouldBe(1);
        foreign.Error.ToString().Trim().ShouldBe("qemu-img.exe sha256 " + Sha(Encoding.ASCII.GetBytes(World.QemuImgBytes)) + " does not match the pin "
            + new string('0', 64) + " in " + foreign.Lock + ".");
        foreign.Launcher.Launches.ShouldBeEmpty();
        foreign.Files.AllFiles.ShouldNotContain(path => path.EndsWith(".raw", StringComparison.Ordinal));

        var pending = new World(imgPin: Card0490BootDisk.PendingPin);
        pending.Run("--virtual-size-mib", "1").ShouldBe(0, pending.Error.ToString());

        var flag = new World { QemuDirVariable = "" };
        flag.Run("--qemu-dir", flag.Qemu, "--virtual-size-mib", "1").ShouldBe(0, flag.Error.ToString());
    }

    [Test]
    public void A_failed_convert_is_exit_1_and_keeps_the_raw_image()
    {
        var world = new World { ConvertExit = 3 };
        world.Run("--virtual-size-mib", "1").ShouldBe(1);
        world.Error.ToString().Trim().ShouldBe("qemu-img convert failed (exit 3).");
        world.Files.AllFiles.ShouldContain(path => path.EndsWith("card0490-bootdisk.raw", StringComparison.Ordinal));
        world.Lines().ShouldNotContain(line => line.StartsWith("bootDisk ", StringComparison.Ordinal));
    }

    [Test]
    public void Smoke_boots_an_overlay_with_the_plans_arguments()
    {
        var world = new World();
        var share = world.Files.Path.Combine(world.Files.Path.GetDirectoryName(world.QemuSystem)!, "share");
        world.Files.AddDirectory(share);
        world.Run("--virtual-size-mib", "1", "--smoke").ShouldBe(0, world.Error.ToString());

        var output = world.Files.Path.Combine(world.Repo, ".antiphon", "card0490-assets", "card0490-bootdisk.qcow2");
        var overlay = world.Files.Path.Combine(world.Repo, ".antiphon", "card0490-assets", "card0490-bootdisk-smoke.qcow2");
        world.Launcher.Launches.Count.ShouldBe(2);
        world.Launcher.Launches[1].Arguments.ShouldBe(["create", "-f", "qcow2", "-F", "qcow2", "-b", output, overlay]);
        var boot = world.Boots.ShouldHaveSingleItem();
        boot.QemuSystem.ShouldBe(world.QemuSystem);
        var rootJson = new JsonObject
        {
            ["driver"] = "qcow2",
            ["node-name"] = "pcroot",
            ["file"] = new JsonObject { ["driver"] = "file", ["filename"] = overlay },
        };
        boot.Arguments.ShouldBe([
            "-L", share,
            "-no-user-config", "-nodefaults",
            "-machine", "q35", "-accel", "tcg,thread=multi", "-cpu", "max", "-smp", "2", "-m", "4096",
            "-display", "none", "-monitor", "none", "-nic", "none", "-no-reboot",
            "-chardev", "stdio,id=pcio,signal=off", "-serial", "chardev:pcio",
            "-blockdev", boot.Arguments[boot.Arguments.ToList().IndexOf("-blockdev") + 1],
            "-device", "virtio-blk-pci,drive=pcroot,bootindex=1",
        ]);
        JsonNode.DeepEquals(JsonNode.Parse(boot.Arguments[boot.Arguments.ToList().IndexOf("-blockdev") + 1]), rootJson).ShouldBeTrue();
        boot.Driven.ShouldBe("CARD0490-DRIVEN");
        (boot.BootWait, boot.EchoWait, boot.ExitWait).ShouldBe((TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60)));
        world.Files.File.Exists(overlay).ShouldBeFalse("a passed smoke deletes its overlay");
        var lines = world.Lines();
        lines.ShouldContain("--- guest serial ---");
        lines.Last().ShouldBe("Smoke passed: boot banner, driven echo and ACPI poweroff all observed.");

        var bare = new World();
        bare.Run("--virtual-size-mib", "1", "--smoke").ShouldBe(0, bare.Error.ToString());
        bare.Boots.ShouldHaveSingleItem().Arguments[0].ShouldBe("-no-user-config", "no share directory, no -L");
    }

    [Test]
    [Arguments("banner", "CARD0490-DRIVEN\r\nCARD0490-HALT\r\n", 0, true, "Guest never announced CARD0490-BOOT-OK.")]
    [Arguments("echo", "CARD0490-BOOT-OK\r\nCARD0490-HALT\r\n", 0, true, "Guest did not echo driven input 'CARD0490-DRIVEN'.")]
    [Arguments("halt", "CARD0490-BOOT-OK\r\nCARD0490-DRIVEN\r\n", 0, true, "Guest did not acknowledge the halt byte.")]
    [Arguments("exit", GoodSerial, 1, true, "QEMU exited 1 after guest poweroff.")]
    [Arguments("hung", GoodSerial, 1, false, "Guest did not power off within 60s.")]
    public void Smoke_refuses_a_guest_that_breaks_the_contract(string row, string serial, int exitCode, bool exited, string message)
    {
        var world = new World { Boot = _ => new Card0490GuestRun(exited, exitCode, serial, "") };
        world.Run("--virtual-size-mib", "1", "--smoke").ShouldBe(1, row);
        world.Error.ToString().Trim().ShouldBe(message, row);
        world.Lines().ShouldNotContain(line => line.StartsWith("Smoke passed", StringComparison.Ordinal), row);
    }

    [Test]
    public void Smoke_refuses_an_unpinned_qemu_before_it_boots()
    {
        var world = new World(systemPin: new string('1', 64));
        world.Run("--virtual-size-mib", "1", "--smoke").ShouldBe(1);
        world.Error.ToString().Trim().ShouldStartWith("qemu-system-x86_64.exe sha256 ");
        world.Boots.ShouldBeEmpty();
        world.Launcher.Launches.Count.ShouldBe(1, "the convert ran; no overlay was made");
    }

    [Test]
    public void Write_lock_pins_the_disk_and_keeps_the_other_entries()
    {
        var world = new World();
        world.Run("--virtual-size-mib", "1", "--write-lock").ShouldBe(0, world.Error.ToString());

        var text = world.Files.File.ReadAllText(world.Lock);
        text.ShouldNotContain("\r");
        text.ShouldEndWith("}\n");
        world.Files.File.ReadAllBytes(world.Lock).Take(3).ShouldNotBe(new byte[] { 0xEF, 0xBB, 0xBF });
        var written = JsonNode.Parse(text)!.AsObject();
        var before = JsonNode.Parse(World.LockText(world.ImgPin, world.SystemPin))!.AsObject();
        foreach (var key in new[] { "qemu", "qemu-img", "sdk", "runtime" })
            JsonNode.DeepEquals(written[key], before[key]).ShouldBeTrue(key);
        var output = world.Files.Path.Combine(world.Repo, ".antiphon", "card0490-assets", "card0490-bootdisk.qcow2");
        JsonNode.DeepEquals(written["bootDisk"], new JsonObject
        {
            ["version"] = "1",
            ["sha256"] = Sha(world.Files.File.ReadAllBytes(output)),
            ["source"] = "scripts/antiphon build-card0490-bootdisk",
            ["file"] = "card0490-bootdisk.qcow2",
            ["format"] = "qcow2",
            ["virtualSizeMiB"] = 1,
        }).ShouldBeTrue(text);
        world.Lines().Last().ShouldBe("Wrote bootDisk pin to " + world.Lock);
    }

    [Test]
    public void Flags_parse_in_kebab_case_and_refuse_usage_errors()
    {
        var repo = Path.Combine(Path.GetTempPath(), "checkout");
        var defaults = Card0490BootDiskOptions.Parse([], "from-variable", repo);
        defaults.QemuDir.ShouldBe("from-variable");
        defaults.LockFile.ShouldBe(Path.Combine(repo, "tests", "fixtures", "card0490-linux", "assets.lock.json"));
        defaults.OutputPath.ShouldBe(Path.Combine(repo, ".antiphon", "card0490-assets", "card0490-bootdisk.qcow2"));
        (defaults.VirtualSizeMiB, defaults.Smoke, defaults.WriteLock).ShouldBe((64, false, false));

        var rooted = Path.Combine(Path.GetTempPath(), "elsewhere.json");
        var given = Card0490BootDiskOptions.Parse(["--qemu-dir", "flag", "--lock-file", rooted, "--output-path", "disk.qcow2", "--smoke", "--write-lock"],
            "from-variable", repo);
        (given.QemuDir, given.LockFile, given.OutputPath, given.Smoke, given.WriteLock).ShouldBe(("flag", rooted, "disk.qcow2", true, true));

        Should.Throw<NightlyUsageException>(() => Card0490BootDiskOptions.Parse(["-Smoke"], null, repo));
        Should.Throw<NightlyUsageException>(() => Card0490BootDiskOptions.Parse(["--virtual-size-mib", "0"], null, repo));
        Should.Throw<NightlyUsageException>(() => Card0490BootDiskOptions.Parse(["--virtual-size-mib", "big"], null, repo));
    }

    [Test]
    public void The_committed_lock_names_the_command_as_the_disks_source()
    {
        var text = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, Card0490BootDiskOptions.DefaultLockFile));
        JsonNode.Parse(text)!["bootDisk"]!["source"]!.GetValue<string>().ShouldBe(Card0490BootDisk.Source);
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class World
    {
        public const string QemuImgBytes = "qemu-img fixture bytes";
        public const string QemuSystemBytes = "qemu-system fixture bytes";

        public World(string? imgPin = null, string? systemPin = null)
        {
            Repo = Files.Path.Combine(Files.Path.GetTempPath(), "checkout");
            Qemu = Files.Path.Combine(Files.Path.GetTempPath(), "qemu-9.2.0");
            QemuImg = Files.Path.Combine(Qemu, "bin", "qemu-img.exe");
            QemuSystem = Files.Path.Combine(Qemu, "bin", "qemu-system-x86_64.exe");
            Files.AddFile(QemuImg, new MockFileData(QemuImgBytes));
            Files.AddFile(QemuSystem, new MockFileData(QemuSystemBytes));
            ImgPin = imgPin ?? Sha(Encoding.ASCII.GetBytes(QemuImgBytes));
            SystemPin = systemPin ?? Sha(Encoding.ASCII.GetBytes(QemuSystemBytes));
            Lock = Files.Path.Combine(Repo, "tests", "fixtures", "card0490-linux", "assets.lock.json");
            Files.AddFile(Lock, new MockFileData(LockText(ImgPin, SystemPin)));
            QemuDirVariable = Qemu;
            Launcher = new RecordingLauncher(Answer);
        }

        public MockFileSystem Files { get; } = new();

        public string Repo { get; }

        public string Qemu { get; }

        public string QemuImg { get; }

        public string QemuSystem { get; }

        public string Lock { get; }

        public string ImgPin { get; }

        public string SystemPin { get; }

        public string? QemuDirVariable { get; init; }

        public int ConvertExit { get; init; }

        public string? ConvertedRawSha { get; private set; }

        public RecordingLauncher Launcher { get; }

        public StringWriter Out { get; } = new();

        public StringWriter Error { get; } = new();

        public List<Card0490GuestBoot> Boots { get; } = [];

        public Func<Card0490GuestBoot, Card0490GuestRun> Boot { get; init; } = _ => new Card0490GuestRun(true, 0, GoodSerial, "");

        public static string LockText(string imgPin, string systemPin) =>
            "{\n"
            + "  \"qemu\": {\n    \"version\": \"9.2.0\",\n    \"sha256\": \"" + systemPin + "\",\n    \"source\": \"https://qemu.example.invalid/qemu-setup.exe\",\n    \"file\": \"qemu-system-x86_64.exe\"\n  },\n"
            + "  \"qemu-img\": {\n    \"version\": \"9.2.0\",\n    \"sha256\": \"" + imgPin + "\",\n    \"source\": \"https://qemu.example.invalid/qemu-setup.exe\",\n    \"file\": \"qemu-img.exe\"\n  },\n"
            + "  \"bootDisk\": {\n    \"version\": \"1\",\n    \"sha256\": \"" + new string('9', 64) + "\",\n    \"source\": \"" + Card0490BootDisk.Source + "\",\n    \"file\": \"card0490-bootdisk.qcow2\",\n    \"format\": \"qcow2\",\n    \"virtualSizeMiB\": 64\n  },\n"
            + "  \"sdk\": \"10.0.204\",\n  \"runtime\": \"net9.0\"\n}\n";

        public int Run(params string[] args)
        {
            var options = Card0490BootDiskOptions.Parse(args, QemuDirVariable, Repo);
            var host = NightlyHost.Create(TimeProvider.System, Launcher, Out, Error, null, null, null, null);
            return Card0490BootDisk.Run(options, host, Files, new Card0490BootDiskSeams
            {
                Boot = boot =>
                {
                    Boots.Add(boot);
                    return Boot(boot);
                },
            });
        }

        public IReadOnlyList<string> Lines() => Out.ToString().Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // qemu-img: convert writes a stand-in qcow2 derived from the raw bytes; create writes the overlay.
        private NightlyLaunchResult Answer(NightlyLaunch launch)
        {
            var arguments = launch.Arguments;
            if (arguments[0] == "convert")
            {
                if (ConvertExit != 0) return new NightlyLaunchResult { ExitCode = ConvertExit };
                var raw = Files.File.ReadAllBytes(arguments[^2]);
                ConvertedRawSha = Sha(raw);
                Files.File.WriteAllText(arguments[^1], "QFI qcow2 of " + ConvertedRawSha);
            }
            else if (arguments[0] == "create") Files.File.WriteAllText(arguments[^1], "QFI overlay of " + arguments[^2]);
            return new NightlyLaunchResult { ExitCode = 0 };
        }
    }
}
