namespace PityLoot.Logic;

/// <summary>Computes a new relative probability for an item tpl. <c>location</c> is only used for trace logging.</summary>
public delegate double LootProbabilityUpdater(string tpl, double relativeProbability, string location);

/// <summary>Port of the pure parts of LootProbabilityManager.ts.</summary>
public class LootProbabilityManager(PityLootConfig config, Action<string>? log = null)
{
    public static readonly HashSet<string> ExcludedItems =
    [
        "569668774bdc2da2298b4568", // Euros
        "5449016a4bdc2d6f028b456f", // Roubles
        "5696686a4bdc2da3298b456a", // Dollars
    ];

    public static readonly HashSet<string> GunsmithContainers =
    [
        "5909d5ef86f77467974efbd8", // Weapon box
        "5909d76c86f77471e53d2adf", // Weapon box
        "5909d7cf86f77470ee57d75a", // Weapon box
        "5909d89086f77472591234a0", // Weapon box
        "578f87ad245977356274f2cc", // Wooden crate
    ];

    public static readonly HashSet<string> QuestKeyContainers =
    [
        "578f8778245977358849a9b5", // Jacket
        "578f87b7245977356274f2cd", // Drawer
    ];

    public const string KeycardId = "5c94bbff86f7747ee735c08f";

    public static readonly HashSet<string> BotTypesToIgnore = ["bear", "usec", "gifter"];

    private static readonly Dictionary<int, string> WishlistCategories = new()
    {
        [0] = "tasks",
        [1] = "hideout",
        [2] = "barter",
        [3] = "equipment",
        [4] = "other",
    };

    private sealed class Stock
    {
        public double FoundInRaid;
        public double NotFoundInRaid;
    }

    /// <summary>
    /// Returns the requirements that can't be completed with what's in the inventory, allocating
    /// found-in-raid items to found-in-raid quest requirements first.
    /// </summary>
    public List<ItemRequirement> GetIncompleteRequirements(
        IEnumerable<InventoryItem> inventory,
        IReadOnlyDictionary<string, double> conditionProgress,
        IEnumerable<ItemRequirement> questRequirements,
        IEnumerable<ItemRequirement> hideoutRequirements)
    {
        var stock = new Dictionary<string, Stock>();
        foreach (var item in inventory)
        {
            if (!stock.TryGetValue(item.Tpl, out var record))
            {
                record = stock[item.Tpl] = new Stock();
            }

            if (item.FoundInRaid)
            {
                record.FoundInRaid += item.Count;
            }
            else
            {
                record.NotFoundInRaid += item.Count;
            }
        }

        // Found-in-raid quest requirements first, then ascending pity. There's no "right" order, this is a heuristic.
        var all = questRequirements
            .Concat(hideoutRequirements)
            .OrderBy(r => IsFir(r) ? 0 : 1)
            .ThenBy(r => config.IsRaidBased ? r.RaidsSinceStarted : r.SecondsSinceStarted)
            .ToList();

        var incomplete = all.Where(req =>
        {
            if (ExcludedItems.Contains(req.ItemId))
            {
                return false;
            }

            if (!stock.TryGetValue(req.ItemId, out var itemCount))
            {
                return true;
            }

            return req.Type switch
            {
                RequirementType.QuestKey => !CheckIfHasEnoughAndRemove(itemCount, 1, false),
                RequirementType.Quest => !CheckIfHasEnoughAndRemove(
                    itemCount,
                    req.AmountRequired - (req.ConditionId is null ? 0 : conditionProgress.GetValueOrDefault(req.ConditionId)),
                    req.FoundInRaid),
                _ => !CheckIfHasEnoughAndRemove(itemCount, req.AmountRequired, false),
            };
        }).ToList();

        if (config.Debug)
        {
            foreach (var req in incomplete)
            {
                log?.Invoke($"Found incomplete item requirements. type: {req.Type}, itemId: {req.ItemId}, amountRequired: {req.AmountRequired}");
            }
        }

        return incomplete;
    }

    private static bool IsFir(ItemRequirement r) => r.Type == RequirementType.Quest && r.FoundInRaid;

    private static bool CheckIfHasEnoughAndRemove(Stock itemCount, double amountNeeded, bool foundInRaid)
    {
        if (foundInRaid)
        {
            if (itemCount.FoundInRaid >= amountNeeded)
            {
                itemCount.FoundInRaid -= amountNeeded;
                return true;
            }

            return false;
        }

        // Use non-FIR first, then top up with FIR
        if (itemCount.NotFoundInRaid >= amountNeeded)
        {
            itemCount.NotFoundInRaid -= amountNeeded;
            return true;
        }

        if (itemCount.NotFoundInRaid + itemCount.FoundInRaid >= amountNeeded)
        {
            amountNeeded -= itemCount.NotFoundInRaid;
            itemCount.NotFoundInRaid = 0;
            itemCount.FoundInRaid -= amountNeeded;
            return true;
        }

        return false;
    }

    public record DropRateMultiplier(double TimeBased, double RaidBased, bool IsKey);

    public Dictionary<string, DropRateMultiplier> GetDropRateMultipliers(IEnumerable<ItemRequirement> incomplete)
    {
        var result = new Dictionary<string, DropRateMultiplier>();
        foreach (var req in incomplete)
        {
            var stats = result.GetValueOrDefault(req.ItemId)
                        ?? new DropRateMultiplier(1, 1, req.Type == RequirementType.QuestKey);

            var hoursSinceStarted = Math.Round(req.SecondsSinceStarted / 60 / 60, MidpointRounding.AwayFromZero);
            var timeMult = hoursSinceStarted * config.DropRateIncreasePerHour + (config.IncreasesStack ? stats.TimeBased : 1);
            var raidMult = req.RaidsSinceStarted * config.DropRateIncreasePerRaid + (config.IncreasesStack ? stats.RaidBased : 1);

            result[req.ItemId] = stats with
            {
                TimeBased = Math.Max(stats.TimeBased, timeMult),
                RaidBased = Math.Max(stats.RaidBased, raidMult),
            };
        }

        return result;
    }

    /// <summary>Item tpls whose weight the updater actually raises (multiplier above 1, key bonus, or wishlist).</summary>
    public HashSet<string> GetBoostedTpls(IReadOnlyDictionary<string, int>? wishList, IEnumerable<ItemRequirement> incomplete)
    {
        var boosted = GetDropRateMultipliers(incomplete)
            .Where(kv => kv.Value.IsKey && config.KeysAdditionalMultiplier > 1
                         || Math.Min(config.MaxDropRateMultiplier, config.IsRaidBased ? kv.Value.RaidBased : kv.Value.TimeBased) > 1)
            .Select(kv => kv.Key)
            .ToHashSet();

        if (config.AppliesToWishlist && wishList is not null)
        {
            boosted.UnionWith(wishList.Keys);
        }

        return boosted;
    }

    /// <param name="wishList">Profile wishlist: item tpl -> category id.</param>
    public LootProbabilityUpdater CreateLootProbabilityUpdater(
        IReadOnlyDictionary<string, int>? wishList,
        IEnumerable<ItemRequirement> incomplete)
    {
        var multipliers = GetDropRateMultipliers(incomplete);
        if (config.Debug)
        {
            foreach (var (tpl, m) in multipliers)
            {
                log?.Invoke($"Calculated new drop rate {config.DropRateIncreaseType} multiplier for {tpl}: {(config.IsRaidBased ? m.RaidBased : m.TimeBased)}");
            }
        }

        var staticWishlistMultipliers = new Dictionary<string, double>();
        if (config.AppliesToWishlist && wishList is not null)
        {
            foreach (var (itemId, categoryId) in wishList)
            {
                var category = WishlistCategories.GetValueOrDefault(categoryId, "other");
                if (config.WishlistMultipliers.TryGetValue(category, out var staticMultiplier))
                {
                    if (config.Debug)
                    {
                        log?.Invoke($"Applied static loot multiplier of {staticMultiplier} to item {itemId} in category {category}");
                    }

                    staticWishlistMultipliers[itemId] = staticMultiplier;
                }
            }
        }

        return (tpl, relativeProbability, location) =>
        {
            var newProbability = relativeProbability;
            if (staticWishlistMultipliers.TryGetValue(tpl, out var staticMult) && staticMult != 0)
            {
                newProbability *= staticMult;
            }
            else if (multipliers.TryGetValue(tpl, out var mult))
            {
                newProbability *= Math.Min(config.MaxDropRateMultiplier, config.IsRaidBased ? mult.RaidBased : mult.TimeBased);
                if (mult.IsKey)
                {
                    newProbability *= config.KeysAdditionalMultiplier;
                }

                // JS Math.round semantics
                newProbability = Math.Floor(newProbability + 0.5);
            }

            if (config.Trace)
            {
                log?.Invoke($"Updated drop rate for item {tpl} in {location} from {relativeProbability} to {newProbability}");
            }

            return newProbability;
        };
    }
}
