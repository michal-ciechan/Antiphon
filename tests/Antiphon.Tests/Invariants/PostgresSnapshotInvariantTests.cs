using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts;
using static Antiphon.Tests.Scripts.PostgresSnapshotTests;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class PostgresSnapshotInvariantTests
{
    [Test]
    public void Sources_are_ascii_and_carry_no_recipient_or_password_literal()
    {
        // WhatIfPlan's script checks, now over the command's sources and the launcher.
        var files = new[] { "Antiphon.Nightly", "Antiphon.Scripts" }.SelectMany(project => Directory.GetFiles(Path.Combine(Repo, "scripts", "lib", project), "*PostgresSnapshot*.cs"))
            .Append(Path.Combine(Repo, "scripts", "postgres-snapshot.ps1")).ToArray();
        files.Length.ShouldBeGreaterThanOrEqualTo(5);
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            bytes.ShouldAllBe(b => b < 128, file + " is ASCII only");
            var text = Encoding.ASCII.GetString(bytes);
            Regex.IsMatch(text, "age1[0-9a-z]{10,}").ShouldBeFalse(file + " has no age recipient literal");
            Regex.IsMatch(text, @"(?i)password\s*=").ShouldBeFalse(file + " has no password literal");
        }
        File.Exists(Path.Combine(Repo, "scripts", "test-postgres-snapshot.ps1")).ShouldBeFalse("the driver's cases are this class");
    }

    [Test]
    public void Snapshot_launcher_declares_the_copy_switch()
    {
        File.ReadAllText(Path.Combine(Repo, "scripts", "postgres-snapshot.ps1")).ShouldContain("[switch]$OneDriveCopy");
    }
}
