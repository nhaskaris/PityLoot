namespace PityLoot.Logic;

/// <summary>Port of HideoutUtils.ts.</summary>
public static class HideoutPlanner
{
    public static int GetSkillLevelFromProgress(double progress)
    {
        var xpToLevel = 10.0;
        var level = 0;
        while (progress > xpToLevel)
        {
            level++;
            progress -= xpToLevel;
            xpToLevel = Math.Min(100, xpToLevel + 10);
        }

        return level;
    }

    /// <summary>
    /// Hideout upgrades whose area/skill/trader prerequisites are all met, and the items they need.
    /// </summary>
    public static List<HideoutUpgradeInfo> GetPossibleHideoutUpgrades(
        IEnumerable<HideoutAreaDef> areas,
        HideoutProfile profile,
        Action<string>? warn = null)
    {
        var skillLevels = profile.SkillProgress.ToDictionary(kv => kv.Key, kv => GetSkillLevelFromProgress(kv.Value));
        var possible = new List<HideoutUpgradeInfo>();

        foreach (var area in areas)
        {
            var currentLevel = profile.AreaLevels.GetValueOrDefault(area.Type);
            var nextLevel = currentLevel + 1;
            if (!area.Stages.TryGetValue(nextLevel, out var requirements))
            {
                continue;
            }

            var canUpgrade = true;
            var requiredItems = new List<RequiredItem>();
            foreach (var req in requirements)
            {
                switch (req.Type)
                {
                    case "Area":
                        if (req.AreaType is null || req.RequiredLevel is null)
                        {
                            warn?.Invoke($"Corrupt area stage requirement for area {area.Id} stage {nextLevel}");
                            break;
                        }

                        if (profile.AreaLevels.GetValueOrDefault(req.AreaType.Value) < req.RequiredLevel)
                        {
                            canUpgrade = false;
                        }

                        break;
                    case "Skill":
                        if (req.SkillLevel is null || string.IsNullOrEmpty(req.SkillName))
                        {
                            warn?.Invoke($"Corrupt skill stage requirement for area {area.Id} stage {nextLevel}");
                            break;
                        }

                        if (skillLevels.GetValueOrDefault(req.SkillName) < req.SkillLevel)
                        {
                            canUpgrade = false;
                        }

                        break;
                    case "TraderLoyalty":
                        if (string.IsNullOrEmpty(req.TraderId) || req.LoyaltyLevel is null)
                        {
                            warn?.Invoke($"Corrupt trader stage requirement for area {area.Id} stage {nextLevel}");
                            break;
                        }

                        if (profile.TraderLoyalty.GetValueOrDefault(req.TraderId) < req.LoyaltyLevel)
                        {
                            canUpgrade = false;
                        }

                        break;
                    case "Item":
                        if (req.Count is null || string.IsNullOrEmpty(req.TemplateId))
                        {
                            warn?.Invoke($"Corrupt item stage requirement for area {area.Id} stage {nextLevel}");
                            break;
                        }

                        requiredItems.Add(new RequiredItem(req.TemplateId, req.Count.Value));
                        break;
                    default:
                        warn?.Invoke($"Unknown stage requirement '{req.Type}' for area {area.Id} stage {nextLevel}");
                        break;
                }
            }

            if (canUpgrade)
            {
                possible.Add(new HideoutUpgradeInfo(area.Type, nextLevel, requiredItems));
            }
        }

        return possible;
    }

    public static List<ItemRequirement> GetHideoutRequirements(
        IEnumerable<HideoutUpgradeInfo> possibleUpgrades,
        UserPityTracker tracker,
        long nowMs)
    {
        return possibleUpgrades.SelectMany(upgrade =>
        {
            var entry = tracker.Hideout.GetValueOrDefault(upgrade.Area.ToString());
            var secondsSinceStarted = entry is { TimeAvailable: > 0 }
                ? Math.Round((nowMs - entry.TimeAvailable) / 1000.0)
                : 0;
            return upgrade.RequiredItems.Select(item => new ItemRequirement(
                RequirementType.Hideout,
                item.Id,
                item.Count,
                secondsSinceStarted,
                entry?.RaidsSinceStarted ?? 0));
        }).ToList();
    }
}
