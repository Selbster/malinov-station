#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._MalinovStation.Lobby.Tiles;
using Content.Client.Changelog;
using NUnit.Framework;

namespace Content.Tests._MalinovStation.Lobby;

[TestFixture]
[TestOf(typeof(MalinovChangelogTileWidget))]
public sealed class MalinovChangelogTileWidgetTest
{
    [Test]
    public void NewestEntriesComeFirstUpToTheLimit()
    {
        // The changelog file keeps its entries oldest first, but the order is not relied on.
        var entries = new[] { 3, 1, 8, 5, 2, 7, 4, 6 }.Select(Item).ToList();
        var latest = new List<MalinovChangelogItem>();

        MalinovChangelogTileWidget.SelectLatest(entries, MalinovChangelogTileWidget.MaxEntries, latest);

        Assert.That(latest.Select(entry => entry.Id), Is.EqualTo(new[] { 8, 7, 6, 5, 4 }));
    }

    [Test]
    public void FewerEntriesThanTheLimitAreAllShown()
    {
        var latest = new List<MalinovChangelogItem> { Item(99) };

        MalinovChangelogTileWidget.SelectLatest(new[] { Item(1), Item(2) }, MalinovChangelogTileWidget.MaxEntries, latest);

        Assert.That(latest.Select(entry => entry.Id), Is.EqualTo(new[] { 2, 1 }), "Entries from before are cleared.");
    }

    [Test]
    public void EmptyChangelogShowsNothing()
    {
        var latest = new List<MalinovChangelogItem>();

        MalinovChangelogTileWidget.SelectLatest(Array.Empty<MalinovChangelogItem>(), MalinovChangelogTileWidget.MaxEntries, latest);

        Assert.That(latest, Is.Empty);
    }

    [TestCase(10, 8, true, true)]
    [TestCase(8, 8, true, false)]
    [TestCase(10, 8, false, false)] // The player has opened the changelog since.
    public void EntryIsNewUntilThePlayerReadsTheChangelog(int id, int lastReadId, bool hasNewEntries, bool expected)
    {
        Assert.That(MalinovChangelogTileWidget.IsNew(Item(id), lastReadId, hasNewEntries), Is.EqualTo(expected));
    }

    private static MalinovChangelogItem Item(int id)
    {
        return new MalinovChangelogItem(
            id,
            "Author",
            new DateTime(2026, 10, 1).AddDays(id),
            new[] { new MalinovChangelogLine(ChangelogManager.ChangelogLineType.Add, $"Change {id}", false) });
    }
}
