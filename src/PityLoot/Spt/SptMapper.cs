using PityLoot.Logic;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Enums;

namespace PityLoot.Spt;

/// <summary>Converts SPT server models into the plain types used by PityLoot.Logic.</summary>
public static class SptMapper
{
    public static IEnumerable<InventoryItem> Inventory(PmcData pmc)
    {
        return (pmc.Inventory?.Items ?? []).Select(item => new InventoryItem(
            item.Template.ToString(),
            item.Upd?.StackObjectsCount ?? 1,
            item.Upd?.SpawnedInSession ?? false));
    }

    public static Dictionary<string, double> ConditionProgress(PmcData pmc)
    {
        return (pmc.TaskConditionCounters ?? [])
            .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.Value ?? 0);
    }

    public static IEnumerable<QuestProgress> StartedQuests(PmcData pmc)
    {
        return (pmc.Quests ?? [])
            .Where(q => q.Status == QuestStatusEnum.Started)
            .Select(q => new QuestProgress(
                q.QId.ToString(),
                q.StartTime,
                q.StatusTimers?.TryGetValue(QuestStatusEnum.Started, out var t) == true ? t : null,
                q.CompletedConditions ?? []));
    }

    public static QuestDef Quest(Quest quest)
    {
        var conditions = (quest.Conditions.AvailableForFinish ?? [])
            .Select(c => new QuestConditionDef(
                c.Id.ToString(),
                c.ConditionType,
                Targets(c.Target),
                c.OnlyFoundInRaid ?? false,
                c.Value))
            .ToList();
        return new QuestDef(quest.Id.ToString(), quest.QuestName, conditions);
    }

    private static List<string> Targets(SPTarkov.Server.Core.Utils.Json.ListOrT<string>? target)
    {
        if (target is null)
        {
            return [];
        }

        if (target.IsList)
        {
            return target.List!.Where(t => !string.IsNullOrEmpty(t)).ToList();
        }

        return string.IsNullOrEmpty(target.Item) ? [] : [target.Item];
    }

    public static HideoutAreaDef HideoutArea(HideoutArea area)
    {
        var stages = new Dictionary<int, IReadOnlyList<StageRequirementDef>>();
        foreach (var (key, stage) in area.Stages ?? [])
        {
            if (!int.TryParse(key, out var level))
            {
                continue;
            }

            stages[level] = (stage.Requirements ?? [])
                .Select(r => new StageRequirementDef(
                    r.Type,
                    r.AreaType,
                    r.RequiredLevel,
                    r.SkillName,
                    r.SkillLevel,
                    r.TraderId.ToString(),
                    r.LoyaltyLevel,
                    r.TemplateId.ToString(),
                    r.Count))
                .ToList();
        }

        return new HideoutAreaDef(area.Id.ToString(), (int)(area.Type ?? 0), stages);
    }

    public static HideoutProfile HideoutProfile(PmcData pmc)
    {
        // An area under construction counts as already upgraded (matches the TypeScript mod)
        var areaLevels = new Dictionary<int, int>();
        foreach (var area in pmc.Hideout?.Areas ?? [])
        {
            var level = area.Level ?? 0;
            areaLevels[(int)area.Type] = area.Constructing == true ? level + 1 : level;
        }

        var skills = (pmc.Skills?.Common ?? [])
            .GroupBy(s => s.Id.ToString())
            .ToDictionary(g => g.Key, g => g.First().Progress);

        var traders = (pmc.TradersInfo ?? [])
            .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.LoyaltyLevel ?? 0);

        return new HideoutProfile(areaLevels, skills, traders);
    }

    public static Dictionary<string, int>? WishList(PmcData pmc)
    {
        return pmc.WishList?.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
    }
}
