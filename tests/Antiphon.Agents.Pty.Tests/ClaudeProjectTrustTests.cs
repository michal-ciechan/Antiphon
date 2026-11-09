using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0391 S4: <see cref="ClaudeProjectTrust"/> is exact-key, never throws, never creates the
/// file, and must not be pointed at the operator's real <c>~/.claude.json</c> from tests (D-6).
/// </summary>
public class ClaudeProjectTrustTests
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    [Test]
    public void Seed_writes_a_forward_slash_case_preserved_key_and_leaves_siblings_intact()
    {
        using var cfg = new TempConfig("""
            {
              "numStartups": 42,
              "userID": "test-user",
              "projects": {
                "C:/src/antiphon": {
                  "hasTrustDialogAccepted": true,
                  "allowedTools": ["Bash"]
                },
                "C:/src/Antiphon": {
                  "hasTrustDialogAccepted": true,
                  "dontCrawl": true
                }
              }
            }
            """);
        var directory = @"C:\logs\antiphon\CasePreserve-AbCdEf-" + Guid.NewGuid().ToString("N");

        var result = ClaudeProjectTrust.Seed(directory, cfg.Path);

        result.Outcome.ShouldBe(ClaudeProjectTrustOutcome.Seeded);
        result.Key.ShouldBe(ClaudeProjectTrust.ProjectKey(directory));
        result.Key.ShouldContain("AbCdEf");
        result.Key.ShouldContain('/');
        result.Key.ShouldNotContain('\\');

        var node = JsonNode.Parse(File.ReadAllText(cfg.Path)) as JsonObject;
        node.ShouldNotBeNull();
        node!["numStartups"]!.GetValue<int>().ShouldBe(42);
        node["userID"]!.GetValue<string>().ShouldBe("test-user");
        var projects = node["projects"] as JsonObject;
        projects.ShouldNotBeNull();
        projects!["C:/src/antiphon"]!["allowedTools"]![0]!.GetValue<string>().ShouldBe("Bash");
        projects["C:/src/Antiphon"]!["dontCrawl"]!.GetValue<bool>().ShouldBeTrue();
        projects[result.Key]!["hasTrustDialogAccepted"]!.GetValue<bool>().ShouldBeTrue();

        var first = node.ToJsonString(WriteOptions);
        var second = JsonNode.Parse(first)!.ToJsonString(WriteOptions);
        second.ShouldBe(first);
    }

    [Test]
    public void Seed_merges_into_an_existing_project_object_without_dropping_fields()
    {
        using var cfg = new TempConfig();
        var directory = Directory.CreateTempSubdirectory("antiphon-trust-merge").FullName;
        var key = ClaudeProjectTrust.ProjectKey(directory);
        cfg.Write($$"""
            {
              "projects": {
                "{{key}}": {
                  "allowedTools": ["Read"],
                  "hasCompletedProjectOnboarding": true
                }
              }
            }
            """);

        try
        {
            var result = ClaudeProjectTrust.Seed(directory, cfg.Path);

            result.Outcome.ShouldBe(ClaudeProjectTrustOutcome.Seeded);
            var project = JsonNode.Parse(File.ReadAllText(cfg.Path))?["projects"]?[key] as JsonObject;
            project.ShouldNotBeNull();
            project!["hasTrustDialogAccepted"]!.GetValue<bool>().ShouldBeTrue();
            project["hasCompletedProjectOnboarding"]!.GetValue<bool>().ShouldBeTrue();
            project["allowedTools"]![0]!.GetValue<string>().ShouldBe("Read");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    public void Seed_returns_AlreadyTrusted_on_repeat_and_does_not_touch_the_file()
    {
        using var cfg = new TempConfig();
        var directory = Directory.CreateTempSubdirectory("antiphon-trust-repeat").FullName;
        try
        {
            var first = ClaudeProjectTrust.Seed(directory, cfg.Path);
            first.Outcome.ShouldBe(ClaudeProjectTrustOutcome.Seeded);
            var before = File.GetLastWriteTimeUtc(cfg.Path);
            Thread.Sleep(50);

            var second = ClaudeProjectTrust.Seed(directory, cfg.Path);

            second.Outcome.ShouldBe(ClaudeProjectTrustOutcome.AlreadyTrusted);
            File.GetLastWriteTimeUtc(cfg.Path).ShouldBe(before);
        }
        finally
        {
            ClaudeProjectTrust.Remove(directory, cfg.Path);
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    public void Seed_NoConfigFile_creates_nothing()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"no-claude-{Guid.NewGuid():N}", ".claude.json");
        File.Exists(path).ShouldBeFalse();

        var result = ClaudeProjectTrust.Seed(@"C:\logs\antiphon\unused", path);

        result.Outcome.ShouldBe(ClaudeProjectTrustOutcome.NoConfigFile);
        File.Exists(path).ShouldBeFalse();
        Directory.Exists(System.IO.Path.GetDirectoryName(path)).ShouldBeFalse();
    }

    [Test]
    public void Seed_Unparseable_leaves_the_file_byte_identical()
    {
        using var cfg = new TempConfig("not-json{{{");
        var before = File.ReadAllBytes(cfg.Path);

        var result = ClaudeProjectTrust.Seed(@"C:\logs\antiphon\unused", cfg.Path);

        result.Outcome.ShouldBe(ClaudeProjectTrustOutcome.Unparseable);
        File.ReadAllBytes(cfg.Path).ShouldBe(before);
    }

    [Test]
    public void ProjectKey_trims_a_trailing_separator_and_does_not_lower_case()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "CasePreserve-AbCdEf");
        var expected = ClaudeProjectTrust.ProjectKey(directory);
        expected.ShouldContain("CasePreserve-AbCdEf", Case.Sensitive);
        expected.ShouldNotBe(expected.ToLowerInvariant());
        expected.ShouldNotContain('\\');
        ClaudeProjectTrust.ProjectKey(directory + System.IO.Path.DirectorySeparatorChar)
            .ShouldBe(expected);
    }

    [Test]
    public void Seed_preserves_the_config_file_mode()
    {
        using var cfg = new TempConfig("""{"projects":{}}""");
        var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        try
        {
            File.SetUnixFileMode(cfg.Path, expected);
        }
        catch (PlatformNotSupportedException)
        {
            throw new SkipTestException("Unix file mode is not available on this host");
        }

        var directory = Directory.CreateTempSubdirectory("antiphon-trust-mode").FullName;
        try
        {
            var result = ClaudeProjectTrust.Seed(directory, cfg.Path);
            result.Outcome.ShouldBe(ClaudeProjectTrustOutcome.Seeded);
            File.GetUnixFileMode(cfg.Path).ShouldBe(expected);
            ClaudeProjectTrust.IsTrusted(directory, cfg.Path).ShouldBeTrue();
        }
        finally
        {
            ClaudeProjectTrust.Remove(directory, cfg.Path);
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    public void Seed_keeps_every_concurrent_project_key()
    {
        using var cfg = new TempConfig("""{"numStartups":1,"projects":{}}""");
        const int writers = 8;
        var directories = Enumerable.Range(0, writers)
            .Select(_ => Directory.CreateTempSubdirectory("antiphon-trust-race").FullName)
            .ToArray();
        try
        {
            var barrier = new Barrier(writers);
            var results = new ClaudeProjectTrustResult[writers];
            Parallel.For(0, writers, i =>
            {
                barrier.SignalAndWait();
                results[i] = ClaudeProjectTrust.Seed(directories[i], cfg.Path);
            });

            foreach (var result in results)
            {
                (result.Outcome is ClaudeProjectTrustOutcome.Seeded or ClaudeProjectTrustOutcome.AlreadyTrusted)
                    .ShouldBeTrue(result.Error);
            }

            var node = JsonNode.Parse(File.ReadAllText(cfg.Path)) as JsonObject;
            node.ShouldNotBeNull();
            node!["numStartups"]!.GetValue<int>().ShouldBe(1);
            foreach (var directory in directories)
                ClaudeProjectTrust.IsTrusted(directory, cfg.Path).ShouldBeTrue();
        }
        finally
        {
            foreach (var directory in directories)
            {
                ClaudeProjectTrust.Remove(directory, cfg.Path);
                try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
            }
        }
    }

    [Test]
    public void Seed_waits_for_the_shared_config_lock()
    {
        using var cfg = new TempConfig("""{"projects":{}}""");
        var directory = Directory.CreateTempSubdirectory("antiphon-trust-lock").FullName;
        var lockPath = cfg.Path + ".antiphon-lock";
        FileStream? held = null;
        Thread? worker = null;
        try
        {
            held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var started = new ManualResetEventSlim(false);
            var box = new ClaudeProjectTrustResult[1];
            worker = new Thread(() =>
            {
                started.Set();
                box[0] = ClaudeProjectTrust.Seed(directory, cfg.Path);
            })
            {
                IsBackground = true,
            };
            worker.Start();
            started.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
            var finishedEarly = worker.Join(TimeSpan.FromMilliseconds(300));
            held.Dispose();
            held = null;
            finishedEarly.ShouldBeFalse();
            worker.Join(TimeSpan.FromSeconds(5)).ShouldBeTrue();
            box[0].Outcome.ShouldBe(ClaudeProjectTrustOutcome.Seeded);
            ClaudeProjectTrust.IsTrusted(directory, cfg.Path).ShouldBeTrue();
        }
        finally
        {
            held?.Dispose();
            if (worker is { IsAlive: true })
                worker.Join(TimeSpan.FromSeconds(5));
            ClaudeProjectTrust.Remove(directory, cfg.Path);
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    public void Remove_deletes_only_the_key_it_is_given()
    {
        using var cfg = new TempConfig();
        var keep = Directory.CreateTempSubdirectory("antiphon-trust-keep").FullName;
        var drop = Directory.CreateTempSubdirectory("antiphon-trust-drop").FullName;
        try
        {
            ClaudeProjectTrust.Seed(keep, cfg.Path).Outcome.ShouldBe(ClaudeProjectTrustOutcome.Seeded);
            ClaudeProjectTrust.Seed(drop, cfg.Path).Outcome.ShouldBe(ClaudeProjectTrustOutcome.Seeded);

            ClaudeProjectTrust.Remove(drop, cfg.Path).ShouldBeTrue();

            ClaudeProjectTrust.IsTrusted(keep, cfg.Path).ShouldBeTrue();
            ClaudeProjectTrust.IsTrusted(drop, cfg.Path).ShouldBeFalse();
            var projects = JsonNode.Parse(File.ReadAllText(cfg.Path))?["projects"] as JsonObject;
            projects.ShouldNotBeNull();
            projects!.ContainsKey(ClaudeProjectTrust.ProjectKey(keep)).ShouldBeTrue();
            projects.ContainsKey(ClaudeProjectTrust.ProjectKey(drop)).ShouldBeFalse();
        }
        finally
        {
            ClaudeProjectTrust.Remove(keep, cfg.Path);
            ClaudeProjectTrust.Remove(drop, cfg.Path);
            try { Directory.Delete(keep, recursive: true); } catch (IOException) { }
            try { Directory.Delete(drop, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class TempConfig : IDisposable
    {
        private readonly string _dir;
        public string Path { get; }

        public TempConfig(string json = """{"projects":{}}""")
        {
            _dir = Directory.CreateTempSubdirectory("antiphon-claude-trust-test").FullName;
            Path = System.IO.Path.Combine(_dir, ".claude.json");
            File.WriteAllText(Path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public void Write(string json) =>
            File.WriteAllText(Path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); }
            catch (IOException) { }
        }
    }
}
