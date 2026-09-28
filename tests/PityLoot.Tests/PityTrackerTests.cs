using PityLoot.Logic;

namespace PityLoot.Tests;

public class PityTrackerTests
{
    private static HideoutUpgradeInfo Upgrade(int area, int level) => new(area, level, []);

    [Fact]
    public void Started_quest_counter_increments_on_counted_raid()
    {
        var old = new UserPityTracker { Quests = { ["q1"] = new QuestTrackerEntry { RaidsSinceStarted = 2 } } };

        var updated = PityTrackerLogic.Update(old, ["q1"], [], incrementRaidCount: true, nowMs: 0);

        Assert.Equal(3, updated.Quests["q1"].RaidsSinceStarted);
    }

    [Fact]
    public void Counter_does_not_change_when_raid_not_counted()
    {
        var old = new UserPityTracker { Quests = { ["q1"] = new QuestTrackerEntry { RaidsSinceStarted = 2 } } };

        var updated = PityTrackerLogic.Update(old, ["q1"], [], incrementRaidCount: false, nowMs: 0);

        Assert.Equal(2, updated.Quests["q1"].RaidsSinceStarted);
    }

    [Fact]
    public void Quests_no_longer_started_are_dropped()
    {
        var old = new UserPityTracker { Quests = { ["done"] = new QuestTrackerEntry { RaidsSinceStarted = 9 } } };

        var updated = PityTrackerLogic.Update(old, ["new"], [], true, 0);

        Assert.False(updated.Quests.ContainsKey("done"));
        Assert.Equal(1, updated.Quests["new"].RaidsSinceStarted);
    }

    [Fact]
    public void Hideout_area_resets_when_next_level_goes_up()
    {
        var old = new UserPityTracker
        {
            Hideout = { ["3"] = new HideoutTrackerEntry { CurrentLevel = 1, RaidsSinceStarted = 7, TimeAvailable = 100 } },
        };

        var updated = PityTrackerLogic.Update(old, [], [Upgrade(3, 2)], incrementRaidCount: false, nowMs: 5000);

        Assert.Equal(2, updated.Hideout["3"].CurrentLevel);
        Assert.Equal(0, updated.Hideout["3"].RaidsSinceStarted);
        Assert.Equal(5000, updated.Hideout["3"].TimeAvailable);
    }

    [Fact]
    public void Hideout_area_keeps_counting_while_same_level()
    {
        var old = new UserPityTracker
        {
            Hideout = { ["3"] = new HideoutTrackerEntry { CurrentLevel = 2, RaidsSinceStarted = 7, TimeAvailable = 100 } },
        };

        var updated = PityTrackerLogic.Update(old, [], [Upgrade(3, 2)], incrementRaidCount: true, nowMs: 5000);

        Assert.Equal(8, updated.Hideout["3"].RaidsSinceStarted);
        Assert.Equal(100, updated.Hideout["3"].TimeAvailable);
    }

    [Fact]
    public void Store_round_trips_per_profile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pity-{Guid.NewGuid()}", "pityTracker.json");
        var store = new PityTrackerStore(path);
        store.Save("p1", new UserPityTracker { Quests = { ["q"] = new QuestTrackerEntry { RaidsSinceStarted = 4 } } });
        store.Save("p2", new UserPityTracker());

        Assert.Equal(4, store.Load("p1").Quests["q"].RaidsSinceStarted);
        Assert.Empty(store.Load("p2").Quests);
        Assert.Empty(store.Load("unknown").Quests);

        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}
