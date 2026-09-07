using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CardFilePrivacyPathTests
{
    [Test]
    [Arguments("root")] [Arguments("cards")] [Arguments("board")]
    public async Task Reparse_alias_refuses_sync_and_content_mutations_before_pinning_or_touching_outside_bytes(string placement)
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(); var id = await world.AddCardAsync();
        var outside = Directory.CreateDirectory(Path.Combine(world.Repo.WorktreeRoot, "outside")).FullName;
        var sentinel = Path.Combine(outside, "sentinel.md"); await File.WriteAllTextAsync(sentinel, "C408_OUTSIDE");
        var link = placement == "root" ? Path.Combine(world.Repo.WorktreeRoot, "alias") : Path.Combine(world.Repo.Path, placement == "cards" ? "docs/cards" : "docs/cards/board");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "New-Item -ItemType Junction -Path $env:C408_LINK -Target $env:C408_TARGET -ErrorAction Stop | Out-Null" }) start.ArgumentList.Add(arg);
        start.Environment["C408_LINK"] = link; start.Environment["C408_TARGET"] = placement == "root" ? world.Repo.Path : outside;
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, await output + await error);
        try
        {
            await using var db = world.Db();
            if (placement == "root") await db.Projects.Where(p => p.Id == world.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.LocalRepositoryPath, link));
            var policy = world.Service(db); var events = new MockEventBus();
            (await Should.ThrowAsync<ConflictException>(() => policy.SyncBoardAsync(world.BoardId))).Code.ShouldBe("unsafe_card_file_path");
            var cards = new CardService(db, null!, null!, null!, events, TimeProvider.System, null!, cardFiles: policy);
            (await Should.ThrowAsync<ConflictException>(() => cards.CreateAsync(world.BoardId, new(null, "C408_NEW"), default))).Code.ShouldBe("unsafe_card_file_path");
            var before = await db.Cards.AsNoTracking().SingleAsync(c => c.Id == id);
            (await Should.ThrowAsync<ConflictException>(() => cards.UpdateContentAsync(id, new(before.ConcurrencyToken, "edit", PrivateNotes: "C408_NEW"), default))).Code.ShouldBe("unsafe_card_file_path");
            if (placement != "board")
                (await Should.ThrowAsync<ConflictException>(() => new BoardService(db, events, TimeProvider.System, cardFiles: policy).CreateAsync(new(world.ProjectId, "New board"), default))).Code.ShouldBe("unsafe_card_file_path");
            if (placement == "root")
                (await Should.ThrowAsync<ConflictException>(() => new ProjectService(db, null!, Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance, cardFiles: policy).CreateAsync(new("C408_NEW_PROJECT", "", null, false, false, link, "master"), default))).Code.ShouldBe("unsafe_card_file_path");
            (await db.Cards.CountAsync(c => c.BoardId == world.BoardId)).ShouldBe(1);
            (await db.Cards.AsNoTracking().SingleAsync(c => c.Id == id)).ConcurrencyToken.ShouldBe(before.ConcurrencyToken);
            (await db.Boards.AsNoTracking().SingleAsync(b => b.Id == world.BoardId)).CardFilesDirectorySlug.ShouldBeNull();
            events.PublishedEvents.ShouldBeEmpty(); (await File.ReadAllTextAsync(sentinel)).ShouldBe("C408_OUTSIDE");
            var status = await policy.GetStatusAsync(world.BoardId, default); status.Reason.ShouldBe("unsafe_card_file_path"); status.RepositoryPath.ShouldBeNull(); status.Directory.ShouldBeNull();
        }
        finally { Directory.Delete(link); }
    }
}
