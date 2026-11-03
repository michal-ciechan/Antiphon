using System.Reflection;
using System.Runtime.Loader;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1678 (sweep 2deb2419 LG-I, LG-S, LG-C). <c>CanaryRepository.IsIgnoredRepositoryFile</c>
/// tolerates a row the canary pass lacks only for a git-ignored repository file. Through the whole
/// tool its three refusals mask one another on Linux: the canary is the repository's sibling, so an
/// outside path resolves through it to the same file, and the canary holds exactly the copy set. Here
/// each refusal is pinned on a hand-built canary where that one condition alone is violated, beside a
/// tolerated control in the same canary:
/// <list type="bullet">
/// <item>the in-repository check: an existing ordinary file outside the repository, not copied and
/// absent from a canary under another parent;</item>
/// <item>the copy-set exclusion: a copied file missing from the canary;</item>
/// <item>the canary-absence check: a file the copy set does not hold, present in the canary.</item>
/// </list>
///
/// <para>Seam: the tool targets net10.0 and this assembly net9.0 with no assembly reference to it, so
/// the staged <c>land-graph-audit/Antiphon.LandGraphAudit.dll</c> is loaded into a collectible context
/// that resolves framework and shared assemblies from the running host. The private constructor, the
/// copy set (<c>_copied</c>) and <c>Root</c> are set by reflection; no product member changes.</para>
///
/// <para>Not observable on Linux: on Windows the copy set compares OrdinalIgnoreCase
/// (<c>CanaryRepository.Create</c>), so another spelling of a copied file is still refused as copied.
/// Linux has no second spelling of one file, so that comparison is owed to a Windows lane.</para>
/// </summary>
[Category("Unit")]
public sealed class LandGraphCanaryToleranceTests
{
    [Test]
    public void C1678_a_path_outside_the_repository_is_refused_by_the_in_repository_check_alone()
    {
        using var world = new CanaryWorld(canaryUnderAnotherParent: true);
        var outside = world.Plant(Path.Combine(world.Parent, "outside", "stray.log"));
        var relative = Path.GetRelativePath(world.Repository, outside);

        relative.ShouldStartWith("..");
        world.Copied.ShouldNotContain(relative.Replace(Path.DirectorySeparatorChar, '/'));
        File.Exists(Path.Combine(world.Canary.Root, relative)).ShouldBeFalse("the canary under another parent lacks it");
        world.Canary.IsIgnored(world.Control).ShouldBeTrue("an ignored repository file is tolerated");
        world.Canary.IsIgnored(outside).ShouldBeFalse("a path outside the repository is never tolerated");
    }

    [Test]
    public void C1678_a_copied_file_missing_from_the_canary_is_refused_by_the_copy_set_alone()
    {
        using var world = new CanaryWorld(canaryUnderAnotherParent: false);
        var copied = world.Plant(Path.Combine(world.Repository, "src", "Copied.cs"));
        world.Copied.Add("src/Copied.cs");

        File.Exists(Path.Combine(world.Canary.Root, "src", "Copied.cs")).ShouldBeFalse("the canary copy is gone");
        world.Canary.IsIgnored(world.Control).ShouldBeTrue("an ignored repository file is tolerated");
        world.Canary.IsIgnored(copied).ShouldBeFalse("a copied file is never tolerated");
    }

    [Test]
    public void C1678_a_file_present_in_the_canary_is_refused_by_the_canary_absence_check_alone()
    {
        using var world = new CanaryWorld(canaryUnderAnotherParent: false);
        var planted = world.Plant(Path.Combine(world.Repository, "TestOutput", "planted.log"));
        world.Plant(Path.Combine(world.Canary.Root, "TestOutput", "planted.log"));

        world.Copied.ShouldNotContain("TestOutput/planted.log");
        world.Canary.IsIgnored(world.Control).ShouldBeTrue("an ignored repository file is tolerated");
        world.Canary.IsIgnored(planted).ShouldBeFalse("a file the canary holds has its own canary row");
    }

    [Test]
    public void C1678_each_tolerated_control_violates_nothing()
    {
        using var sibling = new CanaryWorld(canaryUnderAnotherParent: false);
        using var apart = new CanaryWorld(canaryUnderAnotherParent: true);

        Path.GetDirectoryName(sibling.Canary.Root).ShouldBe(sibling.Parent, "the production layout: a sibling canary");
        Path.GetDirectoryName(apart.Canary.Root).ShouldNotBe(apart.Parent);
        sibling.Canary.IsIgnored(sibling.Control).ShouldBeTrue();
        apart.Canary.IsIgnored(apart.Control).ShouldBeTrue();
        sibling.Canary.IsIgnored(Path.Combine(sibling.Repository, "TestOutput", "missing.log")).ShouldBeFalse("a missing file");
    }

    /// <summary>A repository with one ignored control file and an empty canary.</summary>
    private sealed class CanaryWorld : IDisposable
    {
        private readonly string _scratch;

        public CanaryWorld(bool canaryUnderAnotherParent)
        {
            _scratch = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "c1678-canary-" + Guid.NewGuid().ToString("N")[..12])).FullName;
            Parent = Directory.CreateDirectory(Path.Combine(_scratch, "parent")).FullName;
            Repository = Directory.CreateDirectory(Path.Combine(Parent, "repo")).FullName;
            Canary = new ToolCanary(Repository, Copied);
            if (canaryUnderAnotherParent)
                Canary.Root = Path.Combine(Directory.CreateDirectory(Path.Combine(_scratch, "elsewhere")).FullName,
                    Path.GetFileName(Canary.Root));
            Directory.CreateDirectory(Canary.Root);
            Control = Plant(Path.Combine(Repository, "TestOutput", "ignored.log"));
        }

        public string Parent { get; }
        public string Repository { get; }
        public HashSet<string> Copied { get; } = new(StringComparer.Ordinal);
        public ToolCanary Canary { get; }
        public string Control { get; }

        public string Plant(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "c1678\n");
            return path;
        }

        public void Dispose()
        {
            Canary.Dispose();
            try { Directory.Delete(_scratch, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The tool's CanaryRepository, reached through the staged build.</summary>
    private sealed class ToolCanary : IDisposable
    {
        private static readonly Lazy<Type> CanaryType = new(LoadCanaryType);
        private readonly object _instance;

        public ToolCanary(string repository, HashSet<string> copied)
        {
            var ctor = CanaryType.Value.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(string)])
                       ?? throw new MissingMethodException("CanaryRepository(string repo)");
            _instance = ctor.Invoke([repository]);
            Field("_copied").SetValue(_instance, copied);
        }

        public string Root
        {
            get => (string)Field("<Root>k__BackingField").GetValue(_instance)!;
            set => Field("<Root>k__BackingField").SetValue(_instance, value);
        }

        public bool IsIgnored(string fullPath) =>
            (bool)CanaryType.Value.GetMethod("IsIgnoredRepositoryFile", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(_instance, [fullPath])!;

        public void Dispose() => ((IDisposable)_instance).Dispose();

        private static FieldInfo Field(string name) =>
            CanaryType.Value.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException("CanaryRepository", name);

        private static Type LoadCanaryType()
        {
            var tool = Path.Combine(AppContext.BaseDirectory, "land-graph-audit", "Antiphon.LandGraphAudit.dll");
            File.Exists(tool).ShouldBeTrue($"staged land-graph audit tool missing: {tool}");
            var context = new ToolLoadContext(Path.GetDirectoryName(tool)!);
            return context.LoadFromAssemblyPath(tool).GetType("Antiphon.LandGraphAudit.CanaryRepository", throwOnError: true)!;
        }
    }

    /// <summary>
    /// The tool's references (net10.0 framework, Antiphon.LandGraphAudit.Core) resolve to the
    /// running host's assemblies by simple name; anything else comes from the tool's own folder.
    /// The members called here use no API newer than the host framework.
    /// </summary>
    private sealed class ToolLoadContext(string directory) : AssemblyLoadContext("c1678-land-graph-audit", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            try
            {
                return Default.LoadFromAssemblyName(new AssemblyName(name.Name!));
            }
            catch (IOException)
            {
                var local = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(local) ? LoadFromAssemblyPath(local) : null;
            }
        }
    }
}
