using PityLoot.Logic;

namespace PityLoot.Tests;

public class LootProbabilityManagerTests
{
    private static ItemRequirement Quest(string item, double amount, int raids, bool fir = false, string cond = "c1") =>
        new(RequirementType.Quest, item, amount, 0, raids, cond, fir);

    private static ItemRequirement Hideout(string item, double amount, int raids) =>
        new(RequirementType.Hideout, item, amount, 0, raids);

    private static readonly Dictionary<string, double> NoProgress = new();

    [Fact]
    public void Requirement_with_no_items_in_inventory_is_incomplete()
    {
        var manager = new LootProbabilityManager(new PityLootConfig());
        var result = manager.GetIncompleteRequirements([], NoProgress, [Quest("a", 2, 3)], []);
        Assert.Single(result);
    }

    [Fact]
    public void Money_requirements_are_ignored()
    {
        var manager = new LootProbabilityManager(new PityLootConfig());
        var result = manager.GetIncompleteRequirements([], NoProgress, [], [Hideout("5449016a4bdc2d6f028b456f", 100000, 1)]);
        Assert.Empty(result);
    }

    [Fact]
    public void Condition_progress_is_subtracted_from_quest_amount()
    {
        var manager = new LootProbabilityManager(new PityLootConfig());
        var inventory = new[] { new InventoryItem("a", 1, false) };
        var progress = new Dictionary<string, double> { ["c1"] = 2 };

        var result = manager.GetIncompleteRequirements(inventory, progress, [Quest("a", 3, 1)], []);

        Assert.Empty(result);
    }

    [Fact]
    public void Fir_quest_requirement_is_not_satisfied_by_non_fir_items()
    {
        var manager = new LootProbabilityManager(new PityLootConfig());
        var inventory = new[] { new InventoryItem("a", 5, false) };

        var result = manager.GetIncompleteRequirements(inventory, NoProgress, [Quest("a", 1, 1, fir: true)], []);

        Assert.Single(result);
    }

    [Fact]
    public void Fir_requirements_claim_items_first_so_shared_stock_is_not_double_counted()
    {
        var manager = new LootProbabilityManager(new PityLootConfig());
        var inventory = new[] { new InventoryItem("a", 1, true) };

        // One FIR item: the FIR quest takes it, the hideout requirement stays incomplete
        var result = manager.GetIncompleteRequirements(inventory, NoProgress, [Quest("a", 1, 10, fir: true)], [Hideout("a", 1, 5)]);

        var remaining = Assert.Single(result);
        Assert.Equal(RequirementType.Hideout, remaining.Type);
    }

    [Fact]
    public void Non_fir_requirement_uses_non_fir_then_fir_stock()
    {
        var manager = new LootProbabilityManager(new PityLootConfig());
        var inventory = new[] { new InventoryItem("a", 1, false), new InventoryItem("a", 1, true) };

        var result = manager.GetIncompleteRequirements(inventory, NoProgress, [], [Hideout("a", 2, 1)]);

        Assert.Empty(result);
    }

    [Fact]
    public void Stacked_multipliers_add_across_requirements_for_same_item()
    {
        var manager = new LootProbabilityManager(new PityLootConfig { IncreasesStack = true, DropRateIncreasePerRaid = 0.25 });

        var multipliers = manager.GetDropRateMultipliers([Quest("a", 1, 4), Hideout("a", 1, 2)]);

        // 1 + 4*0.25 = 2, then 2 + 2*0.25 = 2.5
        Assert.Equal(2.5, multipliers["a"].RaidBased, 3);
    }

    [Fact]
    public void Unstacked_multiplier_takes_the_max_requirement()
    {
        var manager = new LootProbabilityManager(new PityLootConfig { IncreasesStack = false, DropRateIncreasePerRaid = 0.25 });

        var multipliers = manager.GetDropRateMultipliers([Quest("a", 1, 4), Hideout("a", 1, 2)]);

        Assert.Equal(2.0, multipliers["a"].RaidBased, 3);
    }

    [Fact]
    public void Updater_applies_multiplier_capped_and_rounded()
    {
        var config = new PityLootConfig { DropRateIncreasePerRaid = 0.25, MaxDropRateMultiplier = 10 };
        var manager = new LootProbabilityManager(config);

        var updater = manager.CreateLootProbabilityUpdater(null, [Quest("a", 1, 4), Quest("b", 1, 1000)]);

        Assert.Equal(200, updater("a", 100, "test"));   // x2
        Assert.Equal(1000, updater("b", 100, "test"));  // capped at x10
        Assert.Equal(100, updater("c", 100, "test"));   // not needed: untouched
        Assert.Equal(3, updater("a", 1.25, "test"));    // 2.5 rounds half up like JS Math.round
    }

    [Fact]
    public void Keys_get_the_additional_multiplier()
    {
        var config = new PityLootConfig { DropRateIncreasePerRaid = 0.25, KeysAdditionalMultiplier = 2.5 };
        var manager = new LootProbabilityManager(config);

        var updater = manager.CreateLootProbabilityUpdater(null, [new ItemRequirement(RequirementType.QuestKey, "key", 1, 0, 4)]);

        Assert.Equal(500, updater("key", 100, "test")); // x2 * 2.5
    }

    [Fact]
    public void Wishlist_multiplier_overrides_pity_when_enabled()
    {
        var config = new PityLootConfig { AppliesToWishlist = true };
        var manager = new LootProbabilityManager(config);

        var updater = manager.CreateLootProbabilityUpdater(new Dictionary<string, int> { ["w"] = 1 }, []);

        Assert.Equal(500, updater("w", 100, "test"));
    }

    [Fact]
    public void Boosted_tpls_only_include_items_above_x1()
    {
        var config = new PityLootConfig { DropRateIncreasePerRaid = 0.25, KeysAdditionalMultiplier = 2.5 };
        var manager = new LootProbabilityManager(config);

        var boosted = manager.GetBoostedTpls(null, [
            Quest("fresh", 1, 0),
            Quest("old", 1, 3),
            new ItemRequirement(RequirementType.QuestKey, "key", 1, 0, 0),
        ]);

        Assert.DoesNotContain("fresh", boosted); // 0 raids: x1
        Assert.Contains("old", boosted);
        Assert.Contains("key", boosted);         // key bonus applies even at 0 raids
    }

    [Fact]
    public void Time_based_mode_uses_hours_since_started()
    {
        var config = new PityLootConfig { DropRateIncreaseType = "time", DropRateIncreasePerHour = 0.05 };
        var manager = new LootProbabilityManager(config);
        var req = new ItemRequirement(RequirementType.Hideout, "a", 1, SecondsSinceStarted: 20 * 3600, RaidsSinceStarted: 0);

        var updater = manager.CreateLootProbabilityUpdater(null, [req]);

        Assert.Equal(200, updater("a", 100, "test")); // 1 + 20*0.05
    }
}
