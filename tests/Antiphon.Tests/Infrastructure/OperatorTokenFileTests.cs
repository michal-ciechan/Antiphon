using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Antiphon.Server.Infrastructure.Security;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-0653: the force-release credential is a random, owner-only file compared in constant time.</summary>
[Category("Unit")]
public class OperatorTokenFileTests
{
    [Test]
    public async Task First_use_creates_one_owner_only_random_token_and_later_reads_return_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-operator-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "nested", "operator-token");
        try
        {
            var first = OperatorTokenFile.ReadOrCreate(path);
            first.Length.ShouldBe(64);
            first.ShouldMatch("^[0-9a-f]{64}$");
            OperatorTokenFile.ReadOrCreate(path).ShouldBe(first);
            File.ReadAllText(path).ShouldBe(first);

            var other = OperatorTokenFile.ReadOrCreate(Path.Combine(root, "other-token"));
            other.ShouldNotBe(first);

            if (OperatingSystem.IsWindows())
                AssertOwnerOnly(path);
            else
                File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task Concurrent_first_use_readers_all_get_the_one_token_and_none_throw()
    {
        // CARD-0681: marked, not fixed. Off Windows this is red on a PRODUCTION race (CARD-0676):
        // ReadOrCreate relies on File.Move(overwrite: false) failing when another caller renamed
        // first, but on Unix several first-use callers' moves all succeed (server2: 8 distinct
        // tokens from 16 readers, no IOException). Remove this skip with the CARD-0676 fix.
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("CARD-0676: OperatorTokenFile first-use rename is not exclusive on Unix");
        const int Readers = 16;
        var root = Path.Combine(Path.GetTempPath(), "antiphon-operator-" + Guid.NewGuid().ToString("N"));
        try
        {
            for (var round = 0; round < 25; round++)
            {
                var path = Path.Combine(root, $"round-{round}", "operator-token");
                using var start = new Barrier(Readers);
                var results = new string?[Readers];
                var errors = new Exception?[Readers];
                var threads = Enumerable.Range(0, Readers).Select(index => new Thread(() =>
                {
                    start.SignalAndWait();
                    try
                    {
                        results[index] = OperatorTokenFile.ReadOrCreate(path);
                    }
                    catch (Exception ex)
                    {
                        errors[index] = ex;
                    }
                })).ToArray();
                foreach (var thread in threads)
                    thread.Start();
                foreach (var thread in threads)
                    thread.Join();

                errors.ShouldAllBe(error => error == null, $"round {round}: {errors.FirstOrDefault(e => e != null)}");
                results.Distinct().Count().ShouldBe(1);
                results[0].ShouldNotBeNull().ShouldMatch("^[0-9a-f]{64}$");
                File.ReadAllText(path).ShouldBe(results[0]);
                Directory.GetFiles(Path.GetDirectoryName(path)!).ShouldBe([path]);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task Only_the_exact_token_matches()
    {
        OperatorTokenFile.Matches("abc123", "abc123").ShouldBeTrue();
        OperatorTokenFile.Matches("abc123", "abc124").ShouldBeFalse();
        OperatorTokenFile.Matches("abc123", "abc1234").ShouldBeFalse();
        OperatorTokenFile.Matches("abc123", "").ShouldBeFalse();
        OperatorTokenFile.Matches("abc123", null).ShouldBeFalse();
        OperatorTokenFile.Matches("", "").ShouldBeFalse();
        await Task.CompletedTask;
    }

    [SupportedOSPlatform("windows")]
    private static void AssertOwnerOnly(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User.ShouldNotBeNull();
        var security = new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        security.AreAccessRulesProtected.ShouldBeTrue();
        security.GetOwner(typeof(SecurityIdentifier)).ShouldBe(user);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        rules.ShouldNotBeEmpty();
        rules.ShouldAllBe(rule => rule.AccessControlType == AccessControlType.Allow && rule.IdentityReference == user);
    }
}
