using PityLoot.Logic;

namespace PityLoot.Tests;

public class PlannerTests
{
    private static HideoutAreaDef Area(int type, params (int level, StageRequirementDef[] reqs)[] stages) =>
        new($"area{type}", type, stages.ToDictionary(s => s.level, s => (IReadOnlyList<StageRequirementDef>)s.reqs));

    private static StageRequirementDef Item(string tpl, int count) => new("Item", null, null, null, null, null, null, tpl, count);
    private static StageRequirementDef AreaReq(int area, int level) => new("Area", area, level, null, null, null, null, null, null);
    private static StageRequirementDef Skill(string name, int level) => new("Skill", null, null, name, level, null, null, null, null);

    private static HideoutProfile Profile(Dictionary<int, int>? areas = null, Dictionary<string, double>? skills = null) =>
        new(areas ?? new(), skills ?? new(), new Dictionary<string, int>());

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 0)]
    [InlineData(11, 1)]
    [InlineData(31, 2)]
    public void Skill_level_from_progress_matches_ts(double progress, int level)
    {
        Assert.Equal(level, HideoutPlanner.GetSkillLevelFromProgress(progress));
    }

    [Fact]
    public void Next_level_items_are_returned_when_prerequisites_met()
    {
        var areas = new[] { Area(1, (1, [Item("bolts", 2)]), (2, [Item("nuts", 5)])) };

        var upgrades = HideoutPlanner.GetPossibleHideoutUpgrades(areas, Profile(new() { [1] = 1 }));

        var upgrade = Assert.Single(upgrades);
        Assert.Equal(2, upgrade.Level);
        Assert.Equal("nuts", Assert.Single(upgrade.RequiredItems).Id);
    }

    [Fact]
    public void Upgrade_skipped_when_area_prerequisite_not_met()
    {
        var areas = new[] { Area(1, (1, [AreaReq(2, 1), Item("bolts", 2)])) };

        Assert.Empty(HideoutPlanner.GetPossibleHideoutUpgrades(areas, Profile()));
    }

    [Fact]
    public void Upgrade_skipped_when_skill_too_low()
    {
        var areas = new[] { Area(1, (1, [Skill("Endurance", 2), Item("bolts", 2)])) };

        Assert.Empty(HideoutPlanner.GetPossibleHideoutUpgrades(areas, Profile(skills: new() { ["Endurance"] = 11 })));
        Assert.Single(HideoutPlanner.GetPossibleHideoutUpgrades(areas, Profile(skills: new() { ["Endurance"] = 31 })));
    }

    [Fact]
    public void Max_level_area_has_no_upgrade()
    {
        var areas = new[] { Area(1, (1, [Item("bolts", 2)])) };

        Assert.Empty(HideoutPlanner.GetPossibleHideoutUpgrades(areas, Profile(new() { [1] = 1 })));
    }

    [Fact]
    public void Quest_planner_returns_handover_conditions_not_yet_completed()
    {
        var quest = new QuestDef("q", "Test", [
            new QuestConditionDef("c1", "HandoverItem", ["a"], true, 2),
            new QuestConditionDef("c2", "HandoverItem", ["b"], false, 1),
            new QuestConditionDef("c3", "FindItem", ["a"], true, 2),
        ]);
        var planner = new QuestPlanner(new PityLootConfig(), new Dictionary<string, List<string>>(), new Dictionary<string, List<string>>());
        var status = new QuestProgress("q", 1000, null, ["c2"]);

        var result = planner.GetIncompleteConditionsForQuest(new Dictionary<string, QuestDef> { ["q"] = quest }, status, 3, 4600);

        var req = Assert.Single(result);
        Assert.Equal("a", req.ItemId);
        Assert.True(req.FoundInRaid);
        Assert.Equal(2, req.AmountRequired);
        Assert.Equal(3, req.RaidsSinceStarted);
        Assert.Equal(3600, req.SecondsSinceStarted);
    }

    [Fact]
    public void Quest_keys_and_gunsmith_parts_are_added_but_not_duplicated()
    {
        var quest = new QuestDef("q", "Test", [new QuestConditionDef("c1", "HandoverItem", ["key1"], false, 1)]);
        var keys = new Dictionary<string, List<string>> { ["q"] = ["key1", "key2"] };
        var gunsmith = new Dictionary<string, List<string>> { ["q"] = ["part1"] };
        var planner = new QuestPlanner(new PityLootConfig(), keys, gunsmith);

        var result = planner.GetIncompleteConditionsForQuest(
            new Dictionary<string, QuestDef> { ["q"] = quest }, new QuestProgress("q", 0, null, []), 0, 0);

        Assert.Contains(result, r => r is { Type: RequirementType.QuestKey, ItemId: "key2" });
        Assert.DoesNotContain(result, r => r is { Type: RequirementType.QuestKey, ItemId: "key1" });
        Assert.Contains(result, r => r is { Type: RequirementType.Gunsmith, ItemId: "part1" });
    }

    [Fact]
    public void Collector_is_excluded_when_configured()
    {
        var quest = new QuestDef(QuestPlanner.CollectorQuestId, "Collector", [new QuestConditionDef("c", "HandoverItem", ["a"], true, 1)]);
        var quests = new Dictionary<string, QuestDef> { [quest.Id] = quest };
        var started = new[] { new QuestProgress(quest.Id, 0, null, []) };
        var empty = new Dictionary<string, List<string>>();

        var excluded = new QuestPlanner(new PityLootConfig { ExcludeCollector = true }, empty, empty)
            .GetInProgressQuestRequirements(started, quests, new UserPityTracker(), 0);
        var included = new QuestPlanner(new PityLootConfig(), empty, empty)
            .GetInProgressQuestRequirements(started, quests, new UserPityTracker(), 0);

        Assert.Empty(excluded);
        Assert.Single(included);
    }
}
