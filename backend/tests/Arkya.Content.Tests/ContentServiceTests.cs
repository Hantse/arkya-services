using Arkya.Content.Api.Application;
using Arkya.Content.Api.Contracts;

namespace Arkya.Content.Tests;

public sealed class ContentServiceTests
{
    [Theory]
    [InlineData("Quest")]
    [InlineData("quest_id")]
    [InlineData("-quest")]
    [InlineData("quest-")]
    [InlineData("quest--id")]
    [InlineData("")]
    [InlineData("quest\n")]
    public async Task QuestIdsMustBeStableAsciiSlugs(string id)
    {
        var service = Service();
        var result = await service.SaveDraftAsync(new(0, [ContentFixtures.Quest(questId: id)]), CancellationToken.None);
        Assert.Equal(ContentChangeStatus.Invalid, result.Status);
        Assert.Contains("quests[0].questId", result.Errors!.Keys);
    }

    [Fact]
    public async Task ValidatesReferencesBoundsDuplicatesAndCatalogSize()
    {
        var service = Service();
        var invalid = new[]
        {
            ContentFixtures.Quest() with { QuestId = new string('a', 65) },
            ContentFixtures.Quest() with { Title = new string('a', 121) },
            ContentFixtures.Quest() with { Title = "   " },
            ContentFixtures.Quest() with { Title = "A\nB" },
            ContentFixtures.Quest() with { Description = new string('a', 2001) },
            ContentFixtures.Quest() with { Description = null },
            ContentFixtures.Quest() with { EnemyId = "unregistered-enemy" },
            ContentFixtures.Quest(0), ContentFixtures.Quest(10001)
        };
        foreach (var quest in invalid)
            Assert.Equal(ContentChangeStatus.Invalid,
                (await service.SaveDraftAsync(new(0, [quest]), CancellationToken.None)).Status);
        Assert.Equal(ContentChangeStatus.Invalid,
            (await service.SaveDraftAsync(new(0, [ContentFixtures.Quest(), ContentFixtures.Quest()]), CancellationToken.None)).Status);
        Assert.Equal(ContentChangeStatus.Invalid, (await service.SaveDraftAsync(new(0,
            Enumerable.Range(0, 129).Select(index => ContentFixtures.Quest(questId: $"quest-{index}")).ToArray()), CancellationToken.None)).Status);
        Assert.Equal(0, (await service.GetDraftAsync(CancellationToken.None)).Revision);
    }

    [Fact]
    public async Task AllowsEmptyDraftAndBoundaryValuesButRequiresAQuestToPublish()
    {
        var service = Service();
        Assert.Equal(ContentChangeStatus.Saved, (await service.SaveDraftAsync(new(0, []), CancellationToken.None)).Status);
        Assert.Equal(ContentChangeStatus.Invalid, (await service.PublishAsync(new(1), CancellationToken.None)).Status);
        var valid = ContentFixtures.Quest(10000) with
        {
            QuestId = new string('a', 64), Title = new string('b', 120), Description = new string('c', 2000)
        };
        Assert.Equal(ContentChangeStatus.Saved, (await service.SaveDraftAsync(new(1, [valid]), CancellationToken.None)).Status);
        Assert.Equal(ContentChangeStatus.Published, (await service.PublishAsync(new(2), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task ConcurrentPublicationsReturnOneStableVersionAndTimestamp()
    {
        var service = Service();
        await service.SaveDraftAsync(new(0, [ContentFixtures.Quest()]), CancellationToken.None);
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => service.PublishAsync(new(1), CancellationToken.None)));
        Assert.All(results, result => Assert.Equal(ContentChangeStatus.Published, result.Status));
        Assert.Single(results.Select(result => result.Catalog!.Version).Distinct());
        Assert.Single(results.Select(result => result.Catalog!.PublishedAt).Distinct());
        Assert.Single(await service.ListCatalogsAsync(CancellationToken.None));
    }

    private static ContentService Service() => new(new MemoryContentRepository(), new FixedTimeProvider());
}
